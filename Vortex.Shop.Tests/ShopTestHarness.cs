using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Shop;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Shop;
using Vortex.Shop.Payments;
using Vortex.Tests.Support;

namespace Vortex.Shop.Tests;

/// <summary>
/// A real <c>ShopService</c> over an in-memory database, with the two grains it can reach recorded
/// rather than run.
/// </summary>
/// <remarks>
/// The service is the real one on purpose. Everything worth testing about the shop — the price
/// snapshot, the signature, what a replayed webhook does — is behaviour of this class, and a fake
/// would be testing the test.
/// </remarks>
internal sealed class ShopTestHarness : IAsyncDisposable
{
    /// <summary>Long enough to pass <c>ShopConfig.MinimumSecretLength</c>, and obviously fake.</summary>
    public const string Secret = "test-secret-that-is-long-enough-to-be-accepted";

    public const int PlayerId = 7;

    private readonly TestDbContextFactory _factory;

    public ShopTestHarness(Action<ShopConfig>? configure = null)
    {
        _factory = new TestDbContextFactory(
            new DbContextOptionsBuilder<VortexDbContext>()
                .UseInMemoryDatabase($"shop-{Guid.NewGuid():N}")
                .Options
        );

        ShopConfig config = new()
        {
            Enabled = true,
            Provider = "manual",
            ProviderSecrets = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["manual"] = Secret,
            },
        };

        configure?.Invoke(config);

        Service = new ShopService(
            _factory,
            // The factory's GetGrain is generic, so the constructed method's return type is what says
            // which grain the caller asked for. Nothing else about Orleans is needed here.
            FakeProxy.Create<IGrainFactory>(call =>
                call.Method.ReturnType == typeof(IPlayerWalletGrain) ? Wallet
                : call.Method.ReturnType == typeof(IPlayerGrain) ? PlayerGrain
                : null
            ),
            Journal,
            [new ManualPaymentProvider()],
            Options.Create(config),
            NullLogger<ShopService>.Instance
        );
    }

    public IShopService Service { get; }

    public RecordingJournal Journal { get; } = new();

    public RecordingWallet Wallet { get; } = new();

    /// <summary>What the club grant was called with. <c>IPlayerGrain</c> has far too many members to
    /// hand-write, so only the one the shop reaches is answered.</summary>
    public ClubRecorder Club { get; } = new();

    private IPlayerGrain PlayerGrain =>
        FakeProxy.Create<IPlayerGrain>(call =>
        {
            if (call.Method.Name != nameof(IPlayerGrain.GrantClubMonthsAsync))
            {
                return null;
            }

            Club.Record((int)call.Args![0]!, (bool)call.Args[1]!);

            return Task.FromResult(ClubPurchaseResult.Success);
        });

    public async Task<ShopProductEntity> SeedProductAsync(
        string code,
        ShopProductKind kind,
        int amount,
        int priceMinor,
        string currency = "EUR"
    )
    {
        await using VortexDbContext db = _factory.CreateDbContext();

        ShopProductEntity product = new()
        {
            Code = code,
            Kind = kind,
            Amount = amount,
            PriceMinor = priceMinor,
            Currency = currency,
            Section = "credits",
            IsActive = true,
        };

        db.ShopProducts.Add(product);

        await db.SaveChangesAsync();

        return product;
    }

    /// <summary>Takes a product off sale, the way an operator would.</summary>
    public async Task WithdrawAsync(string code)
    {
        await using VortexDbContext db = _factory.CreateDbContext();

        ShopProductEntity product = await db.ShopProducts.FirstAsync(p => p.Code == code);
        product.IsActive = false;

        await db.SaveChangesAsync();
    }

    public async Task<ShopOrderEntity?> ReadOrderAsync(string publicId)
    {
        Guid id = Guid.Parse(publicId);

        await using VortexDbContext db = _factory.CreateDbContext();

        return await db.ShopOrders.AsNoTracking().FirstOrDefaultAsync(o => o.PublicId == id);
    }

    /// <summary>A correctly signed manual notification, which is what a real provider would send.</summary>
    public static ShopWebhookRequest Notify(
        string orderId,
        int paidMinor,
        string currency = "EUR",
        bool paid = true,
        string reference = "pay_1",
        string? secret = null,
        DateTimeOffset? signedAt = null
    )
    {
        string body = JsonSerializer.Serialize(
            new
            {
                orderId,
                reference,
                paidMinor,
                currency,
                paid,
            }
        );

        return Sign(body, secret ?? Secret, signedAt ?? DateTimeOffset.UtcNow);
    }

    /// <summary>The same, with the body and the signing under the caller's control.</summary>
    public static ShopWebhookRequest Sign(string body, string secret, DateTimeOffset signedAt) =>
        new(
            "manual",
            body,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Vortex-Signature"] = WebhookSignature.Build(body, secret, signedAt),
            }
        );

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }
}

/// <summary>
/// The journal, in a dictionary. Real enough for what the shop asks of it: a step may be recorded
/// once, and the last transition is readable.
/// </summary>
internal sealed class RecordingJournal : ICommerceJournal
{
    private readonly HashSet<string> _steps = new(StringComparer.Ordinal);

    public List<CommerceOperationState> Transitions { get; } = [];

    public HashSet<CommerceOperationId> Opened { get; } = [];

    public Task OpenAsync(
        CommerceOperationId id,
        CommerceOperationKind kind,
        int playerId,
        string? detail,
        CancellationToken ct
    )
    {
        Opened.Add(id);

        return Task.CompletedTask;
    }

    public Task OpenIfNewAsync(
        CommerceOperationId id,
        CommerceOperationKind kind,
        int playerId,
        string? detail,
        CancellationToken ct
    ) => OpenAsync(id, kind, playerId, detail, ct);

    public Task TransitionAsync(
        CommerceOperationId id,
        CommerceOperationState state,
        string? step,
        string? error,
        CancellationToken ct
    )
    {
        Transitions.Add(state);

        return Task.CompletedTask;
    }

    public Task<bool> TryRecordStepAsync(
        CommerceOperationId id,
        string stepKey,
        string? result,
        CancellationToken ct
    ) => Task.FromResult(_steps.Add($"{id}:{stepKey}"));

    public Task CompleteWithRelayAsync(
        CommerceOperationId id,
        IEvent criticalEvent,
        CancellationToken ct
    ) => Task.CompletedTask;

    public Task<IReadOnlyList<CommerceRelayEntry>> GetUnrelayedAsync(
        int limit,
        CancellationToken ct
    ) => Task.FromResult<IReadOnlyList<CommerceRelayEntry>>([]);

    public Task MarkRelayedAsync(CommerceOperationId id, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<string?> GetStepResultAsync(
        CommerceOperationId id,
        string stepKey,
        CancellationToken ct
    ) => Task.FromResult<string?>(null);

    public Task<IReadOnlyList<CommerceOperationRecord>> GetIncompletePivotedAsync(
        int limit,
        CancellationToken ct
    ) => Task.FromResult<IReadOnlyList<CommerceOperationRecord>>([]);

    public Task<IReadOnlyList<CommerceOperationRecord>> GetEscalatableAsync(
        DateTime pivotedBefore,
        int limit,
        CancellationToken ct
    ) => Task.FromResult<IReadOnlyList<CommerceOperationRecord>>([]);
}

/// <summary>
/// The wallet, as far as the shop can see it. <c>CreditOnceAsync</c> deduplicates by (operation,
/// step) exactly as the real grain does, because that is the property the replay test is about.
/// </summary>
internal sealed class RecordingWallet : IPlayerWalletGrain
{
    private readonly HashSet<string> _applied = new(StringComparer.Ordinal);

    public List<WalletDebitRequest> Credits { get; } = [];

    /// <summary>Set to make the credit fail, which is what "paid and not granted" looks like.</summary>
    public bool Refuse { get; set; }

    public Task<bool> CreditOnceAsync(
        List<WalletDebitRequest> credits,
        CommerceOperationId operationId,
        string stepKey,
        CancellationToken ct
    )
    {
        if (Refuse)
        {
            return Task.FromResult(false);
        }

        if (!_applied.Add($"{operationId}:{stepKey}"))
        {
            return Task.FromResult(true);
        }

        Credits.AddRange(credits);

        return Task.FromResult(true);
    }

    public Task<WalletDebitResult> TryDebitAsync(
        List<WalletDebitRequest> requests,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<WalletDebitResult> TryDebitAsync(
        List<WalletDebitRequest> requests,
        CommerceOperationId operationId,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task CreditBackAsync(List<WalletDebitRequest> requests, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task CreditBackAsync(
        List<WalletDebitRequest> requests,
        CommerceOperationId operationId,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<int> GetAmountForCurrencyAsync(CurrencyKind kind, CancellationToken ct) =>
        Task.FromResult(0);

    public Task<Dictionary<int, int>> GetActivityPointsAsync(CancellationToken ct) =>
        Task.FromResult(new Dictionary<int, int>());

    public Task GrantCreditsAsync(int amount, CancellationToken ct) => Task.CompletedTask;

    public Task GrantActivityPointsAsync(int activityPointType, int amount, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<bool> GrantCurrencyAsync(CurrencyKind kind, int amount, CancellationToken ct) =>
        Task.FromResult(true);
}

/// <summary>What the club grain was asked for, and how many times.</summary>
internal sealed class ClubRecorder
{
    public int Months { get; private set; }

    public int Calls { get; private set; }

    public bool Vip { get; private set; }

    public void Record(int months, bool isVip)
    {
        Months += months;
        Vip = isVip;
        Calls++;
    }
}
