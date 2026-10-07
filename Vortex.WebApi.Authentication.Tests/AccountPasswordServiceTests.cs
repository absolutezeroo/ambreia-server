using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Crypto;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Authentication;
using Xunit;

namespace Vortex.WebApi.Authentication.Tests;

/// <summary>
/// Until this service existed there was no way to change a password at all: registration wrote a
/// hash and nothing ever wrote another. So the two properties worth holding are that a change
/// re-proves the account -- password and second factor both, because a stolen session must not be
/// able to take it -- and that it actually ends the sessions that credential had opened. Revoking a
/// credential while its sessions keep answering revokes nothing.
/// </summary>
public sealed class AccountPasswordServiceTests
{
    private const string EMAIL = "ops@example.com";
    private const string OLD_PASSWORD = "the old password";
    private const string NEW_PASSWORD = "a long enough new one";

    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);

        public Task<VortexDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
            Task.FromResult(CreateDbContext());
    }

    /// <summary>Stands in for the two real stores, and counts what it was asked to drop.</summary>
    private sealed class FakeRevoker(string kind, int sessions) : IAccountSessionRevoker
    {
        public string SessionKind => kind;

        public int Revoked { get; private set; }

        public int RemoveAllForAccount(int accountId)
        {
            Revoked += sessions;
            return sessions;
        }
    }

    private sealed record Harness(
        AccountPasswordService Service,
        TestDbContextFactory Factory,
        int AccountId,
        FakeRevoker Dashboard,
        FakeRevoker Web
    );

    private static async Task<Harness> BuildAsync(string? totpSecret = null)
    {
        DbContextOptions<VortexDbContext> options = new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        TestDbContextFactory factory = new(options);

        await using VortexDbContext db = factory.CreateDbContext();

        PlayerAccountEntity account = new()
        {
            Email = EMAIL,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(OLD_PASSWORD),
            TotpSecret = totpSecret,
        };

        db.PlayerAccounts.Add(account);
        await db.SaveChangesAsync();

        FakeRevoker dashboard = new("dashboard", 2);
        FakeRevoker web = new("web", 1);

        AccountPasswordService service = new(
            factory,
            new AccountAuthenticator(
                factory,
                new AccountMfaService(factory, new RecordingEventPublisher())
            ),
            new List<IAccountSessionRevoker> { dashboard, web },
            new RecordingEventPublisher(),
            NullLogger<AccountPasswordService>.Instance
        );

        return new Harness(service, factory, account.Id, dashboard, web);
    }

    private static async Task<bool> PasswordIsAsync(TestDbContextFactory factory, string password)
    {
        await using VortexDbContext db = factory.CreateDbContext();
        PlayerAccountEntity account = await db.PlayerAccounts.FirstAsync();

        return BCrypt.Net.BCrypt.Verify(password, account.PasswordHash);
    }

    private static string CurrentCode(string secret) =>
        TotpCodes.Compute(
            TotpCodes.FromBase32(secret),
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30
        );

    [Fact]
    public async Task ChangesThePasswordAndEndsEverySessionItHadOpened()
    {
        Harness h = await BuildAsync();

        PasswordChangeResult result = await h.Service.ChangeAsync(
            h.AccountId,
            OLD_PASSWORD,
            NEW_PASSWORD,
            code: null
        );

        result.Outcome.Should().Be(PasswordChangeOutcome.Changed);
        result.SessionsRevoked.Should().Be(3, "both stores were asked, and both are counted");

        (await PasswordIsAsync(h.Factory, NEW_PASSWORD)).Should().BeTrue();
        (await PasswordIsAsync(h.Factory, OLD_PASSWORD)).Should().BeFalse();
    }

    [Fact]
    public async Task RefusesAWrongCurrentPasswordAndLeavesEverythingAlone()
    {
        Harness h = await BuildAsync();

        PasswordChangeResult result = await h.Service.ChangeAsync(
            h.AccountId,
            "not the old password",
            NEW_PASSWORD,
            code: null
        );

        result.Outcome.Should().Be(PasswordChangeOutcome.WrongPassword);
        (await PasswordIsAsync(h.Factory, OLD_PASSWORD)).Should().BeTrue();
        h.Dashboard.Revoked.Should().Be(0, "a refused change must not sign anybody out");
        h.Web.Revoked.Should().Be(0);
    }

    /// <summary>
    /// The reason the change goes through the authenticator rather than a BCrypt call of its own:
    /// the second factor comes along, so a hijacked session cannot take the account with it.
    /// </summary>
    [Fact]
    public async Task RefusesAChangeWithoutTheSecondFactorWhenTheAccountHasOne()
    {
        Harness h = await BuildAsync(TotpCodes.GenerateSecret());

        PasswordChangeResult result = await h.Service.ChangeAsync(
            h.AccountId,
            OLD_PASSWORD,
            NEW_PASSWORD,
            code: null
        );

        result.Outcome.Should().Be(PasswordChangeOutcome.MfaRequired);
        (await PasswordIsAsync(h.Factory, OLD_PASSWORD)).Should().BeTrue();
    }

    [Fact]
    public async Task AcceptsAChangeWithACurrentCode()
    {
        string secret = TotpCodes.GenerateSecret();
        Harness h = await BuildAsync(secret);

        PasswordChangeResult result = await h.Service.ChangeAsync(
            h.AccountId,
            OLD_PASSWORD,
            NEW_PASSWORD,
            CurrentCode(secret)
        );

        result.Outcome.Should().Be(PasswordChangeOutcome.Changed);
        (await PasswordIsAsync(h.Factory, NEW_PASSWORD)).Should().BeTrue();
    }

    [Fact]
    public async Task RefusesAWrongCode()
    {
        Harness h = await BuildAsync(TotpCodes.GenerateSecret());

        PasswordChangeResult result = await h.Service.ChangeAsync(
            h.AccountId,
            OLD_PASSWORD,
            NEW_PASSWORD,
            "000000"
        );

        result.Outcome.Should().Be(PasswordChangeOutcome.InvalidCode);
        (await PasswordIsAsync(h.Factory, OLD_PASSWORD)).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("1234567")]
    public async Task RefusesANewPasswordUnderTheFloor(string tooShort)
    {
        Harness h = await BuildAsync();

        PasswordChangeResult result = await h.Service.ChangeAsync(
            h.AccountId,
            OLD_PASSWORD,
            tooShort,
            code: null
        );

        result.Outcome.Should().Be(PasswordChangeOutcome.TooShort);
        (await PasswordIsAsync(h.Factory, OLD_PASSWORD)).Should().BeTrue();
    }

    [Fact]
    public async Task RefusesAnAccountThatDoesNotExist()
    {
        Harness h = await BuildAsync();

        PasswordChangeResult result = await h.Service.ChangeAsync(
            h.AccountId + 5000,
            OLD_PASSWORD,
            NEW_PASSWORD,
            code: null
        );

        result.Outcome.Should().Be(PasswordChangeOutcome.UnknownAccount);
    }

    /// <summary>
    /// The administrator path for somebody who cannot sign in at all: no old password, no code --
    /// and, deliberately, still every session ended.
    /// </summary>
    [Fact]
    public async Task ResetsWithoutTheOldPasswordAndStillEndsTheSessions()
    {
        Harness h = await BuildAsync(TotpCodes.GenerateSecret());

        PasswordChangeResult result = await h.Service.ResetAsync(h.AccountId, NEW_PASSWORD);

        result.Outcome.Should().Be(PasswordChangeOutcome.Changed);
        result.SessionsRevoked.Should().Be(3);
        (await PasswordIsAsync(h.Factory, NEW_PASSWORD)).Should().BeTrue();
    }

    [Fact]
    public async Task ResetStillRefusesAPasswordUnderTheFloor()
    {
        Harness h = await BuildAsync();

        PasswordChangeResult result = await h.Service.ResetAsync(h.AccountId, "short");

        result.Outcome.Should().Be(PasswordChangeOutcome.TooShort);
        (await PasswordIsAsync(h.Factory, OLD_PASSWORD)).Should().BeTrue();
    }
}
