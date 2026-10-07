using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Authentication;
using Xunit;

namespace Vortex.WebApi.Authentication.Tests;

/// <summary>
/// The security questions are a way INTO the account, not a preference. So the properties that
/// matter are the ones that stop a session alone from becoming the recovery path: changing them
/// needs the password, clearing them needs the password, and an answer is never stored in a form
/// anything can read back.
/// </summary>
public sealed class AccountSafetyQuestionsServiceTests
{
    private const string PASSWORD = "a long enough password";

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);

        public Task<VortexDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
            Task.FromResult(CreateDbContext());
    }

    /// <summary>
    /// Stands in for the real authenticator so the tests do not need its dependencies. It answers
    /// exactly what the password gate needs to see, and nothing here exercises BCrypt on the
    /// account's own password — <see cref="AccountAuthenticatorTests" /> covers that.
    /// </summary>
    private sealed class StubAuthenticator(AccountVerificationOutcome outcome)
        : IAccountAuthenticator
    {
        public Task<AccountVerification> VerifyCredentialsAsync(
            string email,
            string password,
            string? code,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                outcome == AccountVerificationOutcome.Verified && password == PASSWORD
                    ? new AccountVerification(AccountVerificationOutcome.Verified, 1)
                    : new AccountVerification(
                        outcome == AccountVerificationOutcome.Verified
                            ? AccountVerificationOutcome.InvalidCredentials
                            : outcome,
                        0
                    )
            );
    }

    /// <summary>
    /// Stands in for the lock service so these tests do not need Orleans. It records the calls,
    /// which is what the clear-releases-the-lock test asserts on — the real one also tells every
    /// connected avatar, and that is its own class's business.
    /// </summary>
    private sealed class RecordingLockService : IAccountSafetyLockService
    {
        public List<bool> Moves { get; } = [];

        public Task<bool?> IsLockedAsync(int accountId, CancellationToken ct = default) =>
            Task.FromResult<bool?>(Moves.Count > 0 ? Moves[^1] : false);

        public Task ArmAsync(int accountId, CancellationToken ct = default)
        {
            Moves.Add(true);

            return Task.CompletedTask;
        }

        public Task ReleaseAsync(int accountId, CancellationToken ct = default)
        {
            Moves.Add(false);

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
            Moves.Add(locked);

            return Task.FromResult(SafetyLockResult.Success());
        }
    }

    private static async Task<(
        AccountSafetyQuestionsService Service,
        TestDbContextFactory Factory,
        RecordingLockService Locks,
        int AccountId
    )> BuildAsync(
        AccountVerificationOutcome outcome = AccountVerificationOutcome.Verified,
        bool locked = false
    )
    {
        DbContextOptions<VortexDbContext> options = new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        TestDbContextFactory factory = new(options);

        await using VortexDbContext db = factory.CreateDbContext();

        PlayerAccountEntity account = new()
        {
            Email = "player@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(PASSWORD),
            SafetyLocked = locked,
        };

        db.PlayerAccounts.Add(account);
        await db.SaveChangesAsync();

        RecordingLockService locks = new();

        return (
            new AccountSafetyQuestionsService(
                factory,
                new StubAuthenticator(outcome),
                locks,
                NullLogger<AccountSafetyQuestionsService>.Instance
            ),
            factory,
            locks,
            account.Id
        );
    }

    private static Task<SafetyQuestionsOutcome> SaveAsync(
        AccountSafetyQuestionsService service,
        int accountId,
        string password = PASSWORD,
        int question1 = 1,
        int question2 = 4
    ) =>
        service.SaveAsync(
            accountId,
            question1,
            "Mr Whiskers",
            question2,
            "Paris",
            password,
            null,
            CancellationToken.None
        );

    [Fact]
    public async Task Save_storesTheQuestionsAndHashesTheAnswers()
    {
        (AccountSafetyQuestionsService service, TestDbContextFactory factory, _, int accountId) =
            await BuildAsync();

        (await SaveAsync(service, accountId)).Should().Be(SafetyQuestionsOutcome.Succeeded);

        await using VortexDbContext db = factory.CreateDbContext();
        PlayerAccountSafetyQuestionsEntity row = await db.PlayerAccountSafetyQuestions.FirstAsync();

        row.Question1.Should().Be(1);
        row.Question2.Should().Be(4);

        // Never the answer itself, and never a hash anyone could recompute without BCrypt's salt.
        row.Answer1Hash.Should().NotContain("Whiskers").And.StartWith("$2");
        row.Answer2Hash.Should().NotContain("Paris").And.StartWith("$2");
    }

    [Fact]
    public async Task Save_withTheWrongPassword_writesNothing()
    {
        // The property that matters: a stolen session cannot quietly replace the recovery path.
        (AccountSafetyQuestionsService service, TestDbContextFactory factory, _, int accountId) =
            await BuildAsync();

        (await SaveAsync(service, accountId, password: "not the password"))
            .Should()
            .Be(SafetyQuestionsOutcome.WrongPassword);

        await using VortexDbContext db = factory.CreateDbContext();
        (await db.PlayerAccountSafetyQuestions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Save_whenTheAccountHasASecondFactorAndNoCodeCame_asksForOne()
    {
        (AccountSafetyQuestionsService service, _, _, int accountId) = await BuildAsync(
            AccountVerificationOutcome.MfaRequired
        );

        (await SaveAsync(service, accountId)).Should().Be(SafetyQuestionsOutcome.MfaRequired);
    }

    [Theory]
    [InlineData(1, 1)] // the same question twice
    [InlineData(0, 3)] // below the range
    [InlineData(3, 10)] // above it
    public async Task Save_refusesAPairThatIsNotTwoDifferentQuestionsInRange(int first, int second)
    {
        (AccountSafetyQuestionsService service, _, _, int accountId) = await BuildAsync();

        (await SaveAsync(service, accountId, question1: first, question2: second))
            .Should()
            .Be(SafetyQuestionsOutcome.InvalidQuestions);
    }

    [Fact]
    public async Task Save_refusesAnAnswerThatIsOnlyWhitespace()
    {
        (AccountSafetyQuestionsService service, _, _, int accountId) = await BuildAsync();

        (
            await service.SaveAsync(
                accountId,
                1,
                "   ",
                4,
                "Paris",
                PASSWORD,
                null,
                CancellationToken.None
            )
        )
            .Should()
            .Be(SafetyQuestionsOutcome.EmptyAnswer);
    }

    [Fact]
    public async Task Save_twice_replacesRatherThanDuplicates()
    {
        (AccountSafetyQuestionsService service, TestDbContextFactory factory, _, int accountId) =
            await BuildAsync();

        await SaveAsync(service, accountId);
        await SaveAsync(service, accountId, question1: 7, question2: 9);

        await using VortexDbContext db = factory.CreateDbContext();

        (await db.PlayerAccountSafetyQuestions.CountAsync()).Should().Be(1);
        (await db.PlayerAccountSafetyQuestions.FirstAsync()).Question1.Should().Be(7);
    }

    [Fact]
    public async Task Verify_acceptsTheAnswersWhateverTheCasingAndSpacing()
    {
        // The answer is typed from memory months later. The words are the secret; the shift key is
        // not.
        (AccountSafetyQuestionsService service, _, _, int accountId) = await BuildAsync();

        await SaveAsync(service, accountId);

        (await service.VerifyAsync(accountId, "  mr   WHISKERS ", "paris", CancellationToken.None))
            .Should()
            .Be(SafetyQuestionsOutcome.Succeeded);
    }

    [Fact]
    public async Task Verify_needsBothAnswers()
    {
        (AccountSafetyQuestionsService service, _, _, int accountId) = await BuildAsync();

        await SaveAsync(service, accountId);

        (await service.VerifyAsync(accountId, "Mr Whiskers", "Lyon", CancellationToken.None))
            .Should()
            .Be(SafetyQuestionsOutcome.WrongAnswers);

        (await service.VerifyAsync(accountId, "Rex", "Paris", CancellationToken.None))
            .Should()
            .Be(SafetyQuestionsOutcome.WrongAnswers);
    }

    [Fact]
    public async Task Verify_onAnAccountWithNoQuestions_saysSoRatherThanPassing()
    {
        (AccountSafetyQuestionsService service, _, _, int accountId) = await BuildAsync();

        (await service.VerifyAsync(accountId, "anything", "at all", CancellationToken.None))
            .Should()
            .Be(SafetyQuestionsOutcome.NotConfigured);
    }

    [Fact]
    public async Task Clear_needsThePassword()
    {
        (AccountSafetyQuestionsService service, TestDbContextFactory factory, _, int accountId) =
            await BuildAsync();

        await SaveAsync(service, accountId);

        (await service.ClearAsync(accountId, "not the password", null, CancellationToken.None))
            .Should()
            .Be(SafetyQuestionsOutcome.WrongPassword);

        await using VortexDbContext db = factory.CreateDbContext();
        (await db.PlayerAccountSafetyQuestions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Clear_takesTheLockOffWithTheQuestions()
    {
        // Otherwise the account is stranded: the lock is lifted by answering, and there would be
        // nothing left to answer.
        (
            AccountSafetyQuestionsService service,
            TestDbContextFactory factory,
            RecordingLockService locks,
            int accountId
        ) = await BuildAsync(locked: true);

        await SaveAsync(service, accountId);

        (await service.ClearAsync(accountId, PASSWORD, null, CancellationToken.None))
            .Should()
            .Be(SafetyQuestionsOutcome.Succeeded);

        await using VortexDbContext db = factory.CreateDbContext();

        (await db.PlayerAccountSafetyQuestions.CountAsync()).Should().Be(0);

        // Through the lock service, which is also what tells the connected avatars — a write to the
        // column from here would clear the flag and leave every grain still refusing to spend.
        locks.Moves.Should().Equal(false);
    }

    [Fact]
    public async Task GetStatus_neverCarriesTheAnswers()
    {
        (AccountSafetyQuestionsService service, _, _, int accountId) = await BuildAsync();

        (await service.GetStatusAsync(accountId)).Should().Be(SafetyQuestionsStatus.None);

        await SaveAsync(service, accountId);

        SafetyQuestionsStatus status = await service.GetStatusAsync(accountId);

        status.Configured.Should().BeTrue();
        status.Question1.Should().Be(1);
        status.Question2.Should().Be(4);
    }
}
