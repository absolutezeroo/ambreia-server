using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Chain;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Shop;
using Vortex.WebApi.Services;
using Vortex.WebApi.Session;

namespace Vortex.WebApi.Tests;

/// <summary>
/// In-memory auth service for the integration tests. It creates real sessions in the shared
/// <see cref="WebApiSessionStore"/> so the authenticated endpoints resolve an account id exactly as
/// they would in production, without touching the database.
/// </summary>
internal sealed class FakeAuthService(WebApiSessionStore sessions) : IWebApiAuthService
{
    public const string ValidEmail = "user@test.com";
    public const string ValidPassword = "correct-horse";
    public const int AccountId = 1;

    private readonly ConcurrentDictionary<string, byte> _registered = new() { [ValidEmail] = 0 };

    private int _nextAccountId = AccountId + 1;

    /// <summary>An account that has enrolled a second factor, and the only code it accepts.</summary>
    public const string MfaEmail = "mfa@example.com";
    public const string MfaCode = "424242";

    /// <summary>
    /// Every session this fake mints is trusted. Whether a real one is depends on the account's
    /// security questions, which this fake does not model — the tests that care about the challenge
    /// mint their own through <see cref="WebApiSessionStore.CreateSession" />.
    /// </summary>
    public Task<(bool Success, string? SessionId, int AccountId, string? Error)> LoginAsync(
        string email,
        string password,
        string? code,
        string? address,
        string? userAgent,
        CancellationToken ct
    )
    {
        if (email == MfaEmail && password == ValidPassword)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return Task.FromResult<(bool, string?, int, string?)>(
                    (false, null, 0, "pocket.auth.mfa_required")
                );
            }

            if (code != MfaCode)
            {
                return Task.FromResult<(bool, string?, int, string?)>(
                    (false, null, 0, "pocket.auth.invalid_code")
                );
            }

            return Task.FromResult<(bool, string?, int, string?)>(
                (true, sessions.CreateSession(AccountId), AccountId, null)
            );
        }

        if (email != ValidEmail || password != ValidPassword)
        {
            return Task.FromResult<(bool, string?, int, string?)>(
                (false, null, 0, "pocket.auth.login_failed")
            );
        }

        string sessionId = sessions.CreateSession(AccountId);

        return Task.FromResult<(bool, string?, int, string?)>((true, sessionId, AccountId, null));
    }

    public Task<(bool Success, int AccountId, string? Error)> RegisterAsync(
        string email,
        string password,
        CancellationToken ct
    )
    {
        if (!_registered.TryAdd(email, 0))
        {
            return Task.FromResult<(bool, int, string?)>(
                (false, 0, "pocket.auth.valid_email_required")
            );
        }

        int accountId = Interlocked.Increment(ref _nextAccountId);

        return Task.FromResult<(bool, int, string?)>((true, accountId, null));
    }

    public Task<(bool Success, string? Ticket, string? Error)> GetSsoTokenAsync(
        int playerId,
        string ip,
        CancellationToken ct
    )
    {
        if (playerId <= 0)
        {
            return Task.FromResult<(bool, string?, string?)>(
                (false, null, "pocket.auth.login_failed")
            );
        }

        return Task.FromResult<(bool, string?, string?)>((true, $"ticket-{playerId}", null));
    }
}

/// <summary>In-memory player service returning deterministic avatars for contract assertions.</summary>
internal sealed class FakePlayerService : IWebApiPlayerService
{
    public const string OwnedUniqueId = "100";
    public const string TakenName = "taken";

    public Task<List<AvatarInfo>> GetAvatarsForAccountAsync(int accountId, CancellationToken ct) =>
        Task.FromResult(
            new List<AvatarInfo> { new AvatarInfo(OwnedUniqueId, "Tester", "Hi", "hd-180-1", "M") }
        );

    public Task<(bool Success, int PlayerId, string? Error)> CreateAvatarAsync(
        int accountId,
        string name,
        string figure,
        string gender,
        CancellationToken ct
    ) => Task.FromResult<(bool, int, string?)>((true, 101, null));

    public Task<bool> NameAvailableAsync(string name, CancellationToken ct) =>
        Task.FromResult(name != TakenName);

    public Task<bool> SetNameAsync(int playerId, string name, CancellationToken ct) =>
        Task.FromResult(name != TakenName);

    public Task<bool> SaveFigureAsync(
        int playerId,
        string figureString,
        string gender,
        CancellationToken ct
    ) => Task.FromResult(playerId > 0);

    public Task<AvatarInfo?> GetAvatarAsync(int playerId, CancellationToken ct) =>
        Task.FromResult<AvatarInfo?>(null);

    public Task<PlayerPurse?> GetPurseAsync(int playerId, CancellationToken ct) =>
        Task.FromResult<PlayerPurse?>(new PlayerPurse(12480, 36, 2145, 27, 14, 50));

    // Private to start with, like the column's default, so a test that saves can tell it moved.
    private readonly Dictionary<int, bool> _visible = [];

    public Task<bool?> GetProfileVisibleAsync(int playerId, CancellationToken ct) =>
        Task.FromResult<bool?>(_visible.GetValueOrDefault(playerId));

    public Task<bool> SetProfileVisibleAsync(int playerId, bool visible, CancellationToken ct)
    {
        _visible[playerId] = visible;

        return Task.FromResult(true);
    }
}

/// <summary>
/// In-memory sign-in address. Keeps the three refusals the endpoint branches on — a wrong password,
/// a second factor still owed, an address someone else holds — because those are what its status
/// codes are made of.
/// </summary>
internal sealed class FakeEmailService : IAccountEmailService
{
    public const string StartingEmail = "tester@vortex.test";
    public const string TakenEmail = "taken@vortex.test";

    private string _email = StartingEmail;

    /// <summary>Set by a test that wants the account to owe a code.</summary>
    public bool MfaEnrolled { get; set; }

    public Task<string?> GetAsync(int accountId, CancellationToken ct = default) =>
        Task.FromResult<string?>(_email);

    public Task<EmailChangeResult> ChangeAsync(
        int accountId,
        string currentPassword,
        string newEmail,
        string? code,
        CancellationToken ct = default
    )
    {
        if (currentPassword != FakeAuthService.ValidPassword)
        {
            return Task.FromResult(EmailChangeResult.Failed(EmailChangeOutcome.WrongPassword));
        }

        if (MfaEnrolled && string.IsNullOrWhiteSpace(code))
        {
            return Task.FromResult(EmailChangeResult.Failed(EmailChangeOutcome.MfaRequired));
        }

        if (!newEmail.Contains('@', StringComparison.Ordinal))
        {
            return Task.FromResult(EmailChangeResult.Failed(EmailChangeOutcome.Invalid));
        }

        if (newEmail == TakenEmail)
        {
            return Task.FromResult(EmailChangeResult.Failed(EmailChangeOutcome.Taken));
        }

        _email = newEmail;

        return Task.FromResult(EmailChangeResult.Success());
    }
}

/// <summary>
/// In-memory safety lock. Keeps the rule that makes the feature a protection rather than a
/// preference: the password is demanded in BOTH directions.
/// </summary>
internal sealed class FakeSafetyLockService : IAccountSafetyLockService
{
    private bool _locked;

    public Task<bool?> IsLockedAsync(int accountId, CancellationToken ct = default) =>
        Task.FromResult<bool?>(_locked);

    /// <summary>
    /// No password, because neither caller has one to give: the server arms the lock on a sign-in
    /// from an unrecognised place, and releases it when the challenge has already been answered.
    /// </summary>
    public Task ArmAsync(int accountId, CancellationToken ct = default)
    {
        _locked = true;

        return Task.CompletedTask;
    }

    public Task ReleaseAsync(int accountId, CancellationToken ct = default)
    {
        _locked = false;

        return Task.CompletedTask;
    }

    public Task<SafetyLockResult> SetAsync(
        int accountId,
        bool locked,
        string currentPassword,
        string? code,
        CancellationToken ct = default
    )
    {
        if (currentPassword != FakeAuthService.ValidPassword)
        {
            return Task.FromResult(SafetyLockResult.Failed(SafetyLockOutcome.WrongPassword));
        }

        _locked = locked;

        return Task.FromResult(SafetyLockResult.Success());
    }
}

/// <summary>
/// In-memory second factor. It keeps the real service's two rules, which are the ones the endpoints
/// lean on: enrolment does not store the secret until a code confirms it, and an account that
/// already has a factor cannot be handed a new one without disabling the old.
/// </summary>
internal sealed class FakeMfaService : IAccountMfaService
{
    public const string ValidCode = "123456";

    private readonly Dictionary<int, string> _secrets = [];

    public Task<bool> IsEnabledAsync(int accountId, CancellationToken ct = default) =>
        Task.FromResult(_secrets.ContainsKey(accountId));

    public Task<MfaEnrolment> BeginEnrolmentAsync(int accountId, CancellationToken ct = default) =>
        Task.FromResult(new MfaEnrolment("SECRET", "otpauth://totp/Vortex?secret=SECRET"));

    public Task<bool> ConfirmEnrolmentAsync(
        int accountId,
        string secret,
        string code,
        CancellationToken ct = default
    )
    {
        if (code != ValidCode || _secrets.ContainsKey(accountId))
        {
            return Task.FromResult(false);
        }

        _secrets[accountId] = secret;

        return Task.FromResult(true);
    }

    public Task<bool> VerifyAsync(int accountId, string? code, CancellationToken ct = default) =>
        Task.FromResult(_secrets.ContainsKey(accountId) && code == ValidCode);

    public Task<bool> DisableAsync(int accountId, string? code, CancellationToken ct = default)
    {
        if (code != ValidCode || !_secrets.ContainsKey(accountId))
        {
            return Task.FromResult(false);
        }

        _secrets.Remove(accountId);

        return Task.FromResult(true);
    }
}

/// <summary>
/// In-memory password service: accepts <see cref="FakeAuthService.ValidPassword"/> as the current
/// one and nothing else, so the endpoint's success and failure branches are both reachable without
/// a database or a hasher.
/// </summary>
internal sealed class FakePasswordService : IAccountPasswordService
{
    public const int SessionsRevoked = 2;

    public Task<PasswordChangeResult> ChangeAsync(
        int accountId,
        string currentPassword,
        string newPassword,
        string? code,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            currentPassword != FakeAuthService.ValidPassword
                ? PasswordChangeResult.Failed(PasswordChangeOutcome.WrongPassword)
            : newPassword.Length < PasswordChangeResult.MINIMUM_LENGTH
                ? PasswordChangeResult.Failed(PasswordChangeOutcome.TooShort)
            : PasswordChangeResult.Changed(SessionsRevoked)
        );

    public Task<PasswordChangeResult> ResetAsync(
        int accountId,
        string newPassword,
        CancellationToken ct = default
    ) => Task.FromResult(PasswordChangeResult.Changed(SessionsRevoked));
}

/// <summary>
/// Collects what the endpoints emit instead of persisting it. The real sink enqueues onto a
/// channel drained by a background writer, so a test asserting against the database would be
/// asserting against a race; what the endpoint is responsible for is the record it hands over.
/// </summary>
internal sealed class RecordingAuditSink : IAuditSink
{
    private readonly ConcurrentQueue<AuditEvent> _events = new();

    public IReadOnlyCollection<AuditEvent> Events => _events.ToArray();

    public void Emit(in AuditEvent auditEvent) => _events.Enqueue(auditEvent);
}

/// <summary>
/// The shop, faked. What the real one does — the signature, the price snapshot, the replay guard —
/// has its own tests in <c>Vortex.Shop.Tests</c> against the real service; what is left for the
/// endpoints is plumbing, so this answers the shapes and records what it was handed.
/// </summary>
internal sealed class FakeShopService : IShopService
{
    public const string ProductCode = "c-100";

    public const int PriceMinor = 450;

    /// <summary>Every product code an order was opened for, so a test can assert what the route sent.</summary>
    public List<string> Started { get; } = [];

    /// <summary>What the next webhook call answers.</summary>
    public ShopWebhookOutcome WebhookOutcome { get; set; } = ShopWebhookOutcome.Accepted;

    /// <summary>The body the last webhook call was given, verbatim.</summary>
    public string? LastWebhookBody { get; private set; }

    public List<ShopOrder> Orders { get; } = [];

    public Task<ShopCatalog> GetCatalogAsync(CancellationToken ct) =>
        Task.FromResult(
            new ShopCatalog([
                new ShopSection(
                    "credits",
                    [
                        new ShopProduct(
                            ProductCode,
                            ShopProductKind.Credits,
                            100,
                            PriceMinor,
                            "EUR",
                            "credits",
                            3,
                            true
                        ),
                    ]
                ),
            ])
        );

    public Task<ShopOrderResult> StartOrderAsync(
        int playerId,
        string productCode,
        CancellationToken ct
    )
    {
        Started.Add(productCode);

        if (productCode != ProductCode)
        {
            return Task.FromResult(ShopOrderResult.Refused(ShopOrderRefusal.UnknownProduct));
        }

        ShopOrder order = new(
            Guid.NewGuid().ToString(),
            productCode,
            ShopProductKind.Credits,
            100,
            PriceMinor,
            "EUR",
            ShopOrderState.Pending,
            "manual",
            DateTime.UtcNow
        );

        Orders.Add(order);

        return Task.FromResult(
            ShopOrderResult.Opened(new ShopOrderStart(order, "https://pay.test/1"))
        );
    }

    public Task<ShopOrder?> GetOrderAsync(int playerId, string orderId, CancellationToken ct) =>
        Task.FromResult(Orders.Find(o => o.Id == orderId));

    public Task<IReadOnlyList<ShopOrder>> GetOrdersAsync(int playerId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ShopOrder>>(Orders);

    public Task<ShopWebhookOutcome> HandleWebhookAsync(
        ShopWebhookRequest request,
        CancellationToken ct
    )
    {
        LastWebhookBody = request.Body;

        return Task.FromResult(WebhookOutcome);
    }

    public Task<string?> RedeemVoucherAsync(int playerId, string code, CancellationToken ct) =>
        Task.FromResult(code == "GOOD-CODE" ? null : "not_found");
}

/// <summary>
/// In-memory security questions. It keeps the two rules the endpoints lean on: the password is
/// demanded before the questions can be set or removed, and a challenge is answered with the
/// answers and nothing else.
/// </summary>
internal sealed class FakeSafetyQuestionsService : IAccountSafetyQuestionsService
{
    public const string Answer1 = "whiskers";
    public const string Answer2 = "paris";

    private SafetyQuestionsStatus _status = SafetyQuestionsStatus.None;

    /// <summary>Seeds an account that already has questions, without going through the password.</summary>
    public void Configure(int question1 = 1, int question2 = 4) =>
        _status = new SafetyQuestionsStatus(true, question1, question2);

    public Task<SafetyQuestionsStatus> GetStatusAsync(
        int accountId,
        CancellationToken ct = default
    ) => Task.FromResult(_status);

    public Task<SafetyQuestionsOutcome> SaveAsync(
        int accountId,
        int question1,
        string answer1,
        int question2,
        string answer2,
        string currentPassword,
        string? code,
        CancellationToken ct = default
    )
    {
        if (currentPassword != FakeAuthService.ValidPassword)
        {
            return Task.FromResult(SafetyQuestionsOutcome.WrongPassword);
        }

        if (!SafetyQuestions.IsValidPair(question1, question2))
        {
            return Task.FromResult(SafetyQuestionsOutcome.InvalidQuestions);
        }

        _status = new SafetyQuestionsStatus(true, question1, question2);

        return Task.FromResult(SafetyQuestionsOutcome.Succeeded);
    }

    public Task<SafetyQuestionsOutcome> ClearAsync(
        int accountId,
        string currentPassword,
        string? code,
        CancellationToken ct = default
    )
    {
        if (currentPassword != FakeAuthService.ValidPassword)
        {
            return Task.FromResult(SafetyQuestionsOutcome.WrongPassword);
        }

        if (!_status.Configured)
        {
            return Task.FromResult(SafetyQuestionsOutcome.NotConfigured);
        }

        _status = SafetyQuestionsStatus.None;

        return Task.FromResult(SafetyQuestionsOutcome.Succeeded);
    }

    public Task<SafetyQuestionsOutcome> VerifyAsync(
        int accountId,
        string answer1,
        string answer2,
        CancellationToken ct = default
    )
    {
        if (!_status.Configured)
        {
            return Task.FromResult(SafetyQuestionsOutcome.NotConfigured);
        }

        return Task.FromResult(
            answer1 == Answer1 && answer2 == Answer2
                ? SafetyQuestionsOutcome.Succeeded
                : SafetyQuestionsOutcome.WrongAnswers
        );
    }
}

/// <summary>In-memory trusted locations, so a test can see what the unlock route remembered.</summary>
internal sealed class FakeTrustedLocationService : IAccountTrustedLocationService
{
    public List<string> Trusted { get; } = [];

    public string Fingerprint(string? address, string? userAgent) => $"{address}|{userAgent}";

    public Task<bool> IsTrustedAsync(
        int accountId,
        string fingerprint,
        CancellationToken ct = default
    ) => Task.FromResult(Trusted.Contains(fingerprint));

    public Task TrustAsync(int accountId, string fingerprint, CancellationToken ct = default)
    {
        if (!Trusted.Contains(fingerprint))
        {
            Trusted.Add(fingerprint);
        }

        return Task.CompletedTask;
    }

    public Task<int> ResetAsync(int accountId, CancellationToken ct = default)
    {
        int forgotten = Trusted.Count;

        Trusted.Clear();

        return Task.FromResult(forgotten);
    }
}

/// <summary>
/// A hotel with no chain configured, which is what every hotel runs until an operator sets one up.
/// </summary>
/// <remarks>
/// Not a stub that pretends to work: <c>Enabled</c> is false and every call throws exactly as
/// <c>ChainService</c> does in that state, because what the endpoints are being tested on is how
/// they answer when the chain is off — politely, and not with a 500.
/// </remarks>
internal sealed class FakeChainService : IChainService
{
    public bool Enabled => false;

    public string? TreasuryAddress => null;

    public int RelicTransferFeeSilver => 0;

    public ChainInfo GetInfo() => new(31337, "Vortex", "VTX", null, null);

    public bool IsPublicMethodAllowed(string? method) =>
        method is "eth_chainId" or "eth_call" or "eth_sendRawTransaction";

    public Task<BigInteger> GetCoinBalanceAsync(string address, CancellationToken ct) =>
        throw Off();

    public Task<IReadOnlyList<BigInteger>> GetRelicsAsync(string address, CancellationToken ct) =>
        throw Off();

    public Task<string> MintCoinAsync(string to, BigInteger amount, CancellationToken ct) =>
        throw Off();

    public Task<string> MintRelicAsync(string to, BigInteger assetId, CancellationToken ct) =>
        throw Off();

    public Task<string> BurnRelicAsync(BigInteger assetId, CancellationToken ct) => throw Off();

    public Task<JsonElement> ForwardPublicCallAsync(
        string method,
        IReadOnlyList<JsonElement> parameters,
        CancellationToken ct
    ) => throw Off();

    private static InvalidOperationException Off() =>
        new("No chain is configured; check IChainService.Enabled before calling.");
}
