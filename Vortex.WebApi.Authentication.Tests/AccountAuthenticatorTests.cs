using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Authentication;
using Xunit;

namespace Vortex.WebApi.Authentication.Tests;

/// <summary>
/// The single entry point every login goes through. What is asserted is the outcome table: right
/// credentials verify, anything else is refused without saying which half was wrong.
/// </summary>
public sealed class AccountAuthenticatorTests
{
    private const string EMAIL = "ops@example.com";
    private const string PASSWORD = "correct horse battery staple";

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);

        public Task<VortexDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
            Task.FromResult(CreateDbContext());
    }

    private static async Task<(AccountAuthenticator Auth, int AccountId)> BuildAsync()
    {
        DbContextOptions<VortexDbContext> options = new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        TestDbContextFactory factory = new(options);

        await using VortexDbContext db = factory.CreateDbContext();

        PlayerAccountEntity account = new()
        {
            Email = EMAIL,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(PASSWORD),
        };

        db.PlayerAccounts.Add(account);
        await db.SaveChangesAsync();

        return (new AccountAuthenticator(factory), account.Id);
    }

    [Fact]
    public async Task VerifiesTheRightCredentials()
    {
        (AccountAuthenticator auth, int accountId) = await BuildAsync();

        AccountVerification result = await auth.VerifyCredentialsAsync(EMAIL, PASSWORD);

        result.Outcome.Should().Be(AccountVerificationOutcome.Verified);
        result.AccountId.Should().Be(accountId);
    }

    [Theory]
    [InlineData("nobody@example.com", PASSWORD)]
    [InlineData(EMAIL, "wrong")]
    [InlineData("", PASSWORD)]
    [InlineData(EMAIL, "")]
    public async Task RefusesBadCredentialsWithoutSayingWhichHalfWasWrong(
        string email,
        string password
    )
    {
        (AccountAuthenticator auth, _) = await BuildAsync();

        AccountVerification result = await auth.VerifyCredentialsAsync(email, password);

        result.Outcome.Should().Be(AccountVerificationOutcome.InvalidCredentials);
        result.AccountId.Should().Be(0);
    }

    // --- registration: the rules and the hash live with the check ---------------------------------

    [Theory]
    [InlineData("a")]
    [InlineData("short")]
    [InlineData("1234567")]
    public async Task Create_RefusesAPasswordShorterThanTheRule(string password)
    {
        password.Length.Should().BeLessThan(AccountPasswordRules.MINIMUM_LENGTH);
        (AccountAuthenticator auth, _) = await BuildAsync();

        (await auth.CreateAccountAsync("new@example.com", password))
            .Should()
            .Be(AccountCreation.Refused(AccountCreationOutcome.PasswordTooShort));
        (await auth.VerifyCredentialsAsync("new@example.com", password))
            .IsVerified.Should()
            .BeFalse("nothing half-written");
    }

    [Fact]
    public async Task Create_RefusesAPasswordPastTheLengthBCryptActuallyHashes()
    {
        (AccountAuthenticator auth, _) = await BuildAsync();

        (await auth.CreateAccountAsync("new@example.com", new string('p', 73)))
            .Outcome.Should()
            .Be(AccountCreationOutcome.PasswordTooLong);
    }

    [Fact]
    public async Task Create_RefusesAnAddressAlreadyTaken()
    {
        (AccountAuthenticator auth, _) = await BuildAsync();

        (await auth.CreateAccountAsync(" OPS@example.com ", PASSWORD))
            .Outcome.Should()
            .Be(AccountCreationOutcome.EmailTaken);
    }

    /// <summary>
    /// Registration lowered the address, login trimmed and lowered it: one typed with a stray space
    /// was stored with it and could never sign in. Created, then signed in with.
    /// </summary>
    [Fact]
    public async Task Create_StoresWhatTheLoginLooksUp()
    {
        (AccountAuthenticator auth, _) = await BuildAsync();
        string password = new('p', AccountPasswordRules.MINIMUM_LENGTH);

        AccountCreation created = await auth.CreateAccountAsync("  New@Example.com ", password);
        AccountVerification login = await auth.VerifyCredentialsAsync("new@example.com", password);

        created.IsCreated.Should().BeTrue();
        login.AccountId.Should().Be(created.AccountId);
    }

    [Fact]
    public async Task MatchesTheEmailCaseInsensitively()
    {
        (AccountAuthenticator auth, int accountId) = await BuildAsync();

        AccountVerification result = await auth.VerifyCredentialsAsync(
            "  OPS@Example.COM  ",
            PASSWORD
        );

        result.Outcome.Should().Be(AccountVerificationOutcome.Verified);
        result.AccountId.Should().Be(accountId);
    }
}
