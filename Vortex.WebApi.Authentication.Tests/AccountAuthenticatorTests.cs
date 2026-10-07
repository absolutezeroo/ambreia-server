using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vortex.Crypto;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Authentication;
using Xunit;

namespace Vortex.WebApi.Authentication.Tests;

/// <summary>
/// The single entry point every login now goes through -- the dashboard and the player-facing web
/// API both. It exists as one method precisely so the second factor cannot reach one caller and miss
/// the other, which is what happened while the web API kept its own copy of the password check: the
/// factor guarded the admin cookie while the same password still opened the site and, through it, an
/// SSO ticket into the game.
///
/// <para>
/// So what is asserted here is the outcome table, not the plumbing: an account with no factor is
/// unaffected, an account with one cannot be authenticated by a password alone, and a wrong code is
/// distinguishable from a wrong password only to the caller -- never to the visitor, who gets a 401
/// either way.
/// </para>
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

    private static async Task<(AccountAuthenticator Auth, int AccountId)> BuildAsync(
        string? totpSecret
    )
    {
        DbContextOptions<VortexDbContext> options = new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        TestDbContextFactory factory = new(options);

        await using (VortexDbContext db = factory.CreateDbContext())
        {
            PlayerAccountEntity account = new()
            {
                Email = EMAIL,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(PASSWORD),
                TotpSecret = totpSecret,
            };

            db.PlayerAccounts.Add(account);
            await db.SaveChangesAsync();

            return (
                new AccountAuthenticator(
                    factory,
                    new AccountMfaService(factory, new RecordingEventPublisher())
                ),
                account.Id
            );
        }
    }

    private static string CurrentCode(string secret) =>
        TotpCodes.Compute(
            TotpCodes.FromBase32(secret),
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30
        );

    [Fact]
    public async Task VerifiesAnAccountThatHasNoSecondFactor()
    {
        (AccountAuthenticator auth, int accountId) = await BuildAsync(totpSecret: null);

        AccountVerification result = await auth.VerifyCredentialsAsync(EMAIL, PASSWORD, code: null);

        result.Outcome.Should().Be(AccountVerificationOutcome.Verified);
        result.AccountId.Should().Be(accountId);
    }

    [Fact]
    public async Task RefusesAPasswordAloneOnceAFactorIsEnrolled()
    {
        (AccountAuthenticator auth, _) = await BuildAsync(TotpCodes.GenerateSecret());

        AccountVerification result = await auth.VerifyCredentialsAsync(EMAIL, PASSWORD, code: null);

        result.Outcome.Should().Be(AccountVerificationOutcome.MfaRequired);
        result.AccountId.Should().Be(0, "a half-finished login must not hand back an account id");
    }

    [Fact]
    public async Task VerifiesAPasswordWithACurrentCode()
    {
        string secret = TotpCodes.GenerateSecret();
        (AccountAuthenticator auth, int accountId) = await BuildAsync(secret);

        AccountVerification result = await auth.VerifyCredentialsAsync(
            EMAIL,
            PASSWORD,
            CurrentCode(secret)
        );

        result.Outcome.Should().Be(AccountVerificationOutcome.Verified);
        result.AccountId.Should().Be(accountId);
    }

    [Fact]
    public async Task RefusesAWrongCode()
    {
        (AccountAuthenticator auth, _) = await BuildAsync(TotpCodes.GenerateSecret());

        AccountVerification result = await auth.VerifyCredentialsAsync(EMAIL, PASSWORD, "000000");

        result.Outcome.Should().Be(AccountVerificationOutcome.InvalidCode);
    }

    /// <summary>
    /// A right code never rescues a wrong password: the factor is second, not alternative.
    /// </summary>
    [Fact]
    public async Task RefusesAWrongPasswordEvenWithARightCode()
    {
        string secret = TotpCodes.GenerateSecret();
        (AccountAuthenticator auth, _) = await BuildAsync(secret);

        AccountVerification result = await auth.VerifyCredentialsAsync(
            EMAIL,
            "not the password",
            CurrentCode(secret)
        );

        result.Outcome.Should().Be(AccountVerificationOutcome.InvalidCredentials);
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
        (AccountAuthenticator auth, _) = await BuildAsync(totpSecret: null);

        AccountVerification result = await auth.VerifyCredentialsAsync(email, password, code: null);

        result.Outcome.Should().Be(AccountVerificationOutcome.InvalidCredentials);
        result.AccountId.Should().Be(0);
    }

    [Fact]
    public async Task MatchesTheEmailCaseInsensitively()
    {
        (AccountAuthenticator auth, int accountId) = await BuildAsync(totpSecret: null);

        AccountVerification result = await auth.VerifyCredentialsAsync(
            "  OPS@Example.COM  ",
            PASSWORD,
            code: null
        );

        result.Outcome.Should().Be(AccountVerificationOutcome.Verified);
        result.AccountId.Should().Be(accountId);
    }
}
