using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Authentication;

namespace Vortex.WebApi.Authentication;

/// <summary>
/// Owns <c>player_account_safety_questions</c>: the pair of questions that arms an account's
/// protection, and the two BCrypt hashes a challenge is answered against.
/// </summary>
/// <remarks>
/// Re-authentication goes through <see cref="IAccountAuthenticator" /> rather than a BCrypt call of
/// its own, the same rule <see cref="AccountPasswordService" /> follows — which is also how the
/// second factor comes along for free on the two calls that change the questions.
/// </remarks>
public sealed partial class AccountSafetyQuestionsService(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    IAccountAuthenticator authenticator,
    IAccountSafetyLockService locks,
    ILogger<AccountSafetyQuestionsService> logger
) : IAccountSafetyQuestionsService
{
    // The same factor as a password. An answer is shorter and guessier than a password, so if the
    // two ever diverge it is this one that should be the slower of the pair, never the faster.
    private const int WORK_FACTOR = 12;

    private readonly IDbContextFactory<VortexDbContext> _dbContextFactory = dbContextFactory;
    private readonly IAccountAuthenticator _authenticator = authenticator;
    private readonly IAccountSafetyLockService _locks = locks;
    private readonly ILogger<AccountSafetyQuestionsService> _logger = logger;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// What is hashed, and what is compared. A security answer is typed from memory months after it
    /// was chosen, so the casing and the spacing are not part of the secret — only the words are.
    /// Applied in exactly one place so the save path and the verify path cannot drift.
    /// </summary>
    private static string Normalise(string? answer) =>
        Whitespace().Replace((answer ?? string.Empty).Trim(), " ").ToLowerInvariant();

    public async Task<SafetyQuestionsStatus> GetStatusAsync(
        int accountId,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext db = await _dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        // The hashes are deliberately left behind: nothing outside VerifyAsync has any use for them.
        return
            await db
                .PlayerAccountSafetyQuestions.AsNoTracking()
                .Where(q => q.PlayerAccountEntityId == accountId)
                .Select(q => new SafetyQuestionsStatus(true, q.Question1, q.Question2))
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false)
                is { Configured: true } status
            ? status
            : SafetyQuestionsStatus.None;
    }

    public async Task<SafetyQuestionsOutcome> SaveAsync(
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
        if (!SafetyQuestions.IsValidPair(question1, question2))
        {
            return SafetyQuestionsOutcome.InvalidQuestions;
        }

        string first = Normalise(answer1);
        string second = Normalise(answer2);

        if (first.Length == 0 || second.Length == 0)
        {
            return SafetyQuestionsOutcome.EmptyAnswer;
        }

        await using VortexDbContext db = await _dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        PlayerAccountEntity? account = await db
            .PlayerAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, ct)
            .ConfigureAwait(false);

        if (account is null)
        {
            return SafetyQuestionsOutcome.UnknownAccount;
        }

        SafetyQuestionsOutcome refusal = await ReauthenticateAsync(
                account,
                currentPassword,
                code,
                ct
            )
            .ConfigureAwait(false);

        if (refusal != SafetyQuestionsOutcome.Succeeded)
        {
            return refusal;
        }

        PlayerAccountSafetyQuestionsEntity? row = await db
            .PlayerAccountSafetyQuestions.FirstOrDefaultAsync(
                q => q.PlayerAccountEntityId == accountId,
                ct
            )
            .ConfigureAwait(false);

        // BCrypt is CPU-bound by design; keep it off the caller's thread.
        string hash1 = await Task.Run(() => BCrypt.Net.BCrypt.HashPassword(first, WORK_FACTOR), ct)
            .ConfigureAwait(false);
        string hash2 = await Task.Run(() => BCrypt.Net.BCrypt.HashPassword(second, WORK_FACTOR), ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            db.PlayerAccountSafetyQuestions.Add(
                new PlayerAccountSafetyQuestionsEntity
                {
                    PlayerAccountEntityId = accountId,
                    Question1 = question1,
                    Answer1Hash = hash1,
                    Question2 = question2,
                    Answer2Hash = hash2,
                }
            );
        }
        else
        {
            row.Question1 = question1;
            row.Answer1Hash = hash1;
            row.Question2 = question2;
            row.Answer2Hash = hash2;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("Account {AccountId} set its safety questions", accountId);

        return SafetyQuestionsOutcome.Succeeded;
    }

    public async Task<SafetyQuestionsOutcome> ClearAsync(
        int accountId,
        string currentPassword,
        string? code,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext db = await _dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        PlayerAccountEntity? account = await db
            .PlayerAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, ct)
            .ConfigureAwait(false);

        if (account is null)
        {
            return SafetyQuestionsOutcome.UnknownAccount;
        }

        SafetyQuestionsOutcome refusal = await ReauthenticateAsync(
                account,
                currentPassword,
                code,
                ct
            )
            .ConfigureAwait(false);

        if (refusal != SafetyQuestionsOutcome.Succeeded)
        {
            return refusal;
        }

        PlayerAccountSafetyQuestionsEntity? row = await db
            .PlayerAccountSafetyQuestions.FirstOrDefaultAsync(
                q => q.PlayerAccountEntityId == accountId,
                ct
            )
            .ConfigureAwait(false);

        if (row is null)
        {
            return SafetyQuestionsOutcome.NotConfigured;
        }

        db.PlayerAccountSafetyQuestions.Remove(row);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // The lock comes off with the questions. Leaving it on would strand the account: the lock is
        // lifted by answering, and there would be nothing left to answer. Through the lock service
        // rather than by writing the column here — it is the one owner of `safety_locked`, and it is
        // also what tells the connected avatars, which a write from this class would not.
        await _locks.ReleaseAsync(accountId, ct).ConfigureAwait(false);

        _logger.LogInformation("Account {AccountId} cleared its safety questions", accountId);

        return SafetyQuestionsOutcome.Succeeded;
    }

    public async Task<SafetyQuestionsOutcome> VerifyAsync(
        int accountId,
        string answer1,
        string answer2,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext db = await _dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        PlayerAccountSafetyQuestionsEntity? row = await db
            .PlayerAccountSafetyQuestions.AsNoTracking()
            .FirstOrDefaultAsync(q => q.PlayerAccountEntityId == accountId, ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            return SafetyQuestionsOutcome.NotConfigured;
        }

        string first = Normalise(answer1);
        string second = Normalise(answer2);

        // BOTH are verified even when the first has already failed. Returning early would make a
        // wrong first answer measurably faster than a wrong second one, which turns the pair into
        // two independent guesses instead of one.
        bool ok1 = await Task.Run(() => BCrypt.Net.BCrypt.Verify(first, row.Answer1Hash), ct)
            .ConfigureAwait(false);
        bool ok2 = await Task.Run(() => BCrypt.Net.BCrypt.Verify(second, row.Answer2Hash), ct)
            .ConfigureAwait(false);

        return ok1 && ok2 ? SafetyQuestionsOutcome.Succeeded : SafetyQuestionsOutcome.WrongAnswers;
    }

    /// <summary>
    /// The password gate the two writing calls share, mapped onto this service's own outcomes.
    /// </summary>
    private async Task<SafetyQuestionsOutcome> ReauthenticateAsync(
        PlayerAccountEntity account,
        string currentPassword,
        string? code,
        CancellationToken ct
    )
    {
        AccountVerification verification = await _authenticator
            .VerifyCredentialsAsync(account.Email, currentPassword, code, ct)
            .ConfigureAwait(false);

        return verification.Outcome switch
        {
            AccountVerificationOutcome.MfaRequired => SafetyQuestionsOutcome.MfaRequired,
            AccountVerificationOutcome.InvalidCode => SafetyQuestionsOutcome.InvalidCode,
            AccountVerificationOutcome.InvalidCredentials => SafetyQuestionsOutcome.WrongPassword,
            _ => SafetyQuestionsOutcome.Succeeded,
        };
    }
}
