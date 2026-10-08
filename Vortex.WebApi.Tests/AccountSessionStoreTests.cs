using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Vortex.WebApi.Session;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// The session table behind the web API. It replaced two implementations that had drifted,
/// so what is asserted here is the behaviour each of them was missing: a token nobody can guess,
/// expiry that holds on every read, revocation of an account's sessions, and a table that does not
/// grow forever because visitors never come back.
/// </summary>
public sealed class AccountSessionStoreTests
{
    private static AccountSessionStore Store(TimeSpan? lifetime = null) =>
        new(lifetime ?? TimeSpan.FromMinutes(30));

    [Fact]
    public void ResolvesTheAccountBehindAToken()
    {
        AccountSessionStore store = Store();

        string token = store.Create(42);

        store.Resolve(token).Should().Be(42);
    }

    /// <summary>
    /// The token is the whole credential. 256 bits, hex, and never the same twice -- the store it
    /// replaced on the web side minted a GUID, which is random by implementation rather than by
    /// contract.
    /// </summary>
    [Fact]
    public void MintsAnUnguessableTokenEachTime()
    {
        AccountSessionStore store = Store();

        List<string> tokens = [.. Enumerable.Range(0, 200).Select(store.Create)];

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
        AccountSessionStore store = Store(TimeSpan.FromMilliseconds(1));

        string token = store.Create(7);
        System.Threading.Thread.Sleep(20);

        store.Resolve(token).Should().BeNull();
    }

    [Fact]
    public void RemovesOneSessionWithoutTouchingTheOthers()
    {
        AccountSessionStore store = Store();

        string kept = store.Create(1);
        string dropped = store.Create(1);

        store.Remove(dropped);

        store.Resolve(dropped).Should().BeNull();
        store.Resolve(kept).Should().NotBeNull();
    }

    /// <summary>
    /// What revoking an account's credential would need: that is half the job while the sessions it
    /// already opened keep answering. Nothing calls it at the moment.
    /// </summary>
    [Fact]
    public void RevokesEverySessionOfOneAccountAndReportsHowMany()
    {
        AccountSessionStore store = Store();

        string a1 = store.Create(1);
        string a2 = store.Create(1);
        string b1 = store.Create(2);

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
        AccountSessionStore store = Store(TimeSpan.FromMilliseconds(1));

        for (int i = 0; i < 64; i++)
        {
            store.Create(i);
        }

        System.Threading.Thread.Sleep(20);
        store.Create(999);

        store.Count.Should().Be(1);
    }

    [Fact]
    public void RefusesALifetimeThatWouldExpireEverySessionImmediately()
    {
        Action act = () => _ = new AccountSessionStore(TimeSpan.Zero);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
