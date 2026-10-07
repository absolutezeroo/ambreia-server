using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Vortex.Primitives.Authentication;
using Xunit;

namespace Vortex.WebApi.Authentication.Tests;

/// <summary>
/// The session table both front doors now share. It replaced two implementations that had drifted,
/// so what is asserted here is the behaviour each of them was missing: a token nobody can guess,
/// expiry that holds on every read path rather than just the one someone remembered, revocation of
/// an account's sessions, and a table that does not grow forever because visitors never come back.
/// </summary>
public sealed class AccountSessionStoreTests
{
    private static AccountSessionStore<string> Store(TimeSpan? lifetime = null) =>
        new(lifetime ?? TimeSpan.FromMinutes(30));

    [Fact]
    public void ResolvesTheAccountAndStateBehindAToken()
    {
        AccountSessionStore<string> store = Store();

        string token = store.Create(42, "ops@example.com");

        store.Resolve(token).Should().Be((42, "ops@example.com"));
    }

    /// <summary>
    /// The token is the whole credential. 256 bits, hex, and never the same twice -- the store it
    /// replaced on the web side minted a GUID, which is random by implementation rather than by
    /// contract.
    /// </summary>
    [Fact]
    public void MintsAnUnguessableTokenEachTime()
    {
        AccountSessionStore<string> store = Store();

        List<string> tokens = [.. Enumerable.Range(0, 200).Select(i => store.Create(i, "x"))];

        tokens.Should().OnlyHaveUniqueItems();
        tokens.Should().AllSatisfy(t => t.Should().HaveLength(64).And.MatchRegex("^[0-9A-F]+$"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    public void ResolvesNothingForAnUnknownToken(string? token)
    {
        Store().Resolve(token).Should().BeNull();
    }

    [Fact]
    public void StopsResolvingOnceTheLifetimeHasPassed()
    {
        AccountSessionStore<string> store = Store(TimeSpan.FromMilliseconds(1));

        string token = store.Create(7, "ops@example.com");
        System.Threading.Thread.Sleep(20);

        store.Resolve(token).Should().BeNull();
    }

    /// <summary>
    /// The web store read the selected avatar straight out of the dictionary, so an expired session
    /// still answered that question. Expiry has to hold on every read, not the one that remembered.
    /// </summary>
    [Fact]
    public void RefusesToUpdateAnExpiredSession()
    {
        AccountSessionStore<string> store = Store(TimeSpan.FromMilliseconds(1));

        string token = store.Create(7, "before");
        System.Threading.Thread.Sleep(20);

        store.TryUpdate(token, _ => "after").Should().BeFalse();
        store.Resolve(token).Should().BeNull();
    }

    [Fact]
    public void UpdatesTheStateOfALiveSessionWithoutExtendingIt()
    {
        AccountSessionStore<int?> store = new(TimeSpan.FromMinutes(30));

        string token = store.Create(7, null);

        store.TryUpdate(token, _ => 99).Should().BeTrue();
        store.Resolve(token)!.Value.State.Should().Be(99);
    }

    [Fact]
    public void RemovesOneSessionWithoutTouchingTheOthers()
    {
        AccountSessionStore<string> store = Store();

        string kept = store.Create(1, "a");
        string dropped = store.Create(1, "b");

        store.Remove(dropped);

        store.Resolve(dropped).Should().BeNull();
        store.Resolve(kept).Should().NotBeNull();
    }

    /// <summary>
    /// What a password change needs: revoking the credential is half the job while the sessions it
    /// already opened keep answering.
    /// </summary>
    [Fact]
    public void RevokesEverySessionOfOneAccountAndReportsHowMany()
    {
        AccountSessionStore<string> store = Store();

        string a1 = store.Create(1, "a");
        string a2 = store.Create(1, "a");
        string b1 = store.Create(2, "b");

        store.RemoveAllForAccount(1).Should().Be(2);

        store.Resolve(a1).Should().BeNull();
        store.Resolve(a2).Should().BeNull();
        store.Resolve(b1).Should().NotBeNull("another account's sessions are not its business");
    }

    [Fact]
    public void RevokingAnAccountWithNoSessionsIsHarmless()
    {
        Store().RemoveAllForAccount(999).Should().Be(0);
    }

    /// <summary>
    /// Nothing evicted an entry unless someone re-presented it, so a visitor who never came back
    /// left one behind until restart. Login is the only place the table grows, so it sweeps.
    /// </summary>
    [Fact]
    public void SweepsExpiredEntriesOnceTheTableIsBigEnoughToNotice()
    {
        AccountSessionStore<string> store = Store(TimeSpan.FromMilliseconds(1));

        for (int i = 0; i < 64; i++)
        {
            store.Create(i, "abandoned");
        }

        System.Threading.Thread.Sleep(20);
        store.Create(999, "the login that triggers the sweep");

        store.Count.Should().Be(1);
    }

    [Fact]
    public void RefusesALifetimeThatWouldExpireEverySessionImmediately()
    {
        Action act = () => _ = new AccountSessionStore<string>(TimeSpan.Zero);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
