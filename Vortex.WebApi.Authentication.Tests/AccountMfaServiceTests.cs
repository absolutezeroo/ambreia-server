using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vortex.Crypto;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Xunit;

namespace Vortex.WebApi.Authentication.Tests;

/// <summary>
/// Enrolment proves possession of a secret the caller supplied, which says nothing about who the
/// caller is. So the property that matters is not "a valid code writes the secret" -- it is that a
/// valid code cannot write it <em>over</em> one already there. Without that, a stolen session posts
/// a secret of its own, becomes the account's second factor, passes the step-up gate on currency,
/// staff roles and the console, and locks the real operator out of a login that now wants a code
/// only the attacker can compute.
/// </summary>
public sealed class AccountMfaServiceTests
{
    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);

        public Task<VortexDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
            Task.FromResult(CreateDbContext());
    }

    private static async Task<(
        AccountMfaService Service,
        TestDbContextFactory Factory,
        int AccountId
    )> BuildAsync(string? totpSecret)
    {
        DbContextOptions<VortexDbContext> options = new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        TestDbContextFactory factory = new(options);

        await using VortexDbContext db = factory.CreateDbContext();

        PlayerAccountEntity account = new()
        {
            Email = "ops@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("a long enough password"),
            TotpSecret = totpSecret,
        };

        db.PlayerAccounts.Add(account);
        await db.SaveChangesAsync();

        return (new AccountMfaService(factory, new RecordingEventPublisher()), factory, account.Id);
    }

    private static string CurrentCode(string secret) =>
        TotpCodes.Compute(
            TotpCodes.FromBase32(secret),
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30
        );

    private static async Task<string?> StoredSecretAsync(TestDbContextFactory factory)
    {
        await using VortexDbContext db = factory.CreateDbContext();

        return (await db.PlayerAccounts.FirstAsync()).TotpSecret;
    }

    [Fact]
    public async Task ConfirmEnrolment_OnAnAccountWithNoFactor_StoresTheSecret()
    {
        (AccountMfaService service, TestDbContextFactory factory, int accountId) = await BuildAsync(
            totpSecret: null
        );

        string secret = TotpCodes.GenerateSecret();

        bool enabled = await service.ConfirmEnrolmentAsync(
            accountId,
            secret,
            CurrentCode(secret),
            CancellationToken.None
        );

        enabled.Should().BeTrue();
        (await StoredSecretAsync(factory)).Should().Be(secret);
    }

    [Fact]
    public async Task ConfirmEnrolment_WhenAFactorAlreadyExists_RefusesAndLeavesItAlone()
    {
        string existing = TotpCodes.GenerateSecret();

        (AccountMfaService service, TestDbContextFactory factory, int accountId) = await BuildAsync(
            existing
        );

        // The attacker's own secret, with a code that verifies against it perfectly -- which is
        // exactly the point: the code is not evidence about the caller.
        string attackerSecret = TotpCodes.GenerateSecret();
        attackerSecret.Should().NotBe(existing);

        bool enabled = await service.ConfirmEnrolmentAsync(
            accountId,
            attackerSecret,
            CurrentCode(attackerSecret),
            CancellationToken.None
        );

        enabled.Should().BeFalse();
        (await StoredSecretAsync(factory)).Should().Be(existing);
    }

    [Fact]
    public async Task ReEnrolment_GoesThroughDisable_WhichDemandsTheFactorAlreadyStored()
    {
        string existing = TotpCodes.GenerateSecret();

        (AccountMfaService service, TestDbContextFactory factory, int accountId) = await BuildAsync(
            existing
        );

        string replacement = TotpCodes.GenerateSecret();

        // A code from the new secret does not remove the old factor either: the refusal is not
        // something the caller can talk their way around by picking a different route.
        (await service.DisableAsync(accountId, CurrentCode(replacement), CancellationToken.None))
            .Should()
            .BeFalse();
        (await StoredSecretAsync(factory)).Should().Be(existing);

        // Proving the factor on the account is what opens the door -- and only then does enrolment
        // have an empty slot to write into.
        (await service.DisableAsync(accountId, CurrentCode(existing), CancellationToken.None))
            .Should()
            .BeTrue();
        (
            await service.ConfirmEnrolmentAsync(
                accountId,
                replacement,
                CurrentCode(replacement),
                CancellationToken.None
            )
        )
            .Should()
            .BeTrue();

        (await StoredSecretAsync(factory)).Should().Be(replacement);
    }
}
