using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Main.Console;
using Vortex.Primitives.Console;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans.Observers;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Players;
using Xunit;

namespace Vortex.Hosting.Tests;

/// <summary>
/// The operator commands used to be a private switch writing straight to <c>System.Console</c>,
/// reachable only by typing into the process's stdin — untestable, and unusable from anywhere else.
/// They now sit behind <see cref="IConsoleCommandDispatcher"/> with a caller-supplied output sink,
/// which is what lets the dashboard console reach the same set under a capability check.
/// </summary>
public sealed class ConsoleCommandDispatcherTests
{
    [Fact]
    public async Task Help_ListsEveryCommand()
    {
        (ConsoleCommandDispatcher dispatcher, List<string> output, _) = Dispatcher();

        await dispatcher.ExecuteAsync("help", output.Add, CancellationToken.None);

        foreach (ConsoleCommandDescriptor command in dispatcher.Commands)
        {
            output
                .Should()
                .Contain(
                    line => line.Contains(command.Name, StringComparison.Ordinal),
                    command.Name
                );
        }
    }

    [Fact]
    public void Find_ResolvesAliases() =>
        Dispatcher().Dispatcher.Find("exit")!.Name.Should().Be("quit");

    [Fact]
    public void Find_IsCaseInsensitive() =>
        Dispatcher().Dispatcher.Find("RELOAD-PLUGINS")!.Name.Should().Be("reload-plugins");

    [Fact]
    public void Find_ReturnsNullForAnUnknownWord() =>
        Dispatcher().Dispatcher.Find("drop-database").Should().BeNull();

    [Fact]
    public async Task AnUnknownCommand_IsReportedRatherThanSwallowed()
    {
        (ConsoleCommandDispatcher dispatcher, List<string> output, _) = Dispatcher();

        bool handled = await dispatcher.ExecuteAsync(
            "drop-database",
            output.Add,
            CancellationToken.None
        );

        handled.Should().BeFalse();
        output.Should().ContainSingle().Which.Should().Contain("Unknown command");
    }

    /// <summary>
    /// A capability that is not in the canonical list has no authorization policy behind it, so the
    /// endpoint gating on it would throw at runtime rather than deny — the exact failure mode
    /// <c>Capabilities.Dashboard.All</c> exists to prevent.
    /// </summary>
    [Fact]
    public void EveryCommandCapability_IsADeclaredDashboardCapability()
    {
        IEnumerable<string> declared = Dispatcher()
            .Dispatcher.Commands.Select(c => c.RequiredCapability)
            .OfType<string>();

        declared
            .Should()
            .OnlyContain(capability => Capabilities.Dashboard.All.Contains(capability));
    }

    /// <summary>
    /// Console access is not permission to hand out inventory: the grant commands carry the same
    /// capability their dashboard page does, so one grant means one thing on both routes.
    /// </summary>
    [Theory]
    [InlineData("mystery-key")]
    [InlineData("mystery-box")]
    public void TheMysteryBoxCommands_RequireTheMysteryBoxCapability(string command) =>
        Dispatcher()
            .Dispatcher.Find(command)!
            ?.RequiredCapability.Should()
            .Be(Capabilities.Dashboard.OpsMysteryBoxManage);

    [Fact]
    public void Quit_RequiresTheServerControlCapability() =>
        Dispatcher()
            .Dispatcher.Find("quit")!
            ?.RequiredCapability.Should()
            .Be(Capabilities.Dashboard.OpsServerControl);

    [Fact]
    public void Help_IsReachableWithoutAnyExtraCapability() =>
        Dispatcher().Dispatcher.Find("help")!.RequiredCapability.Should().BeNull();

    /// <summary>
    /// This used to be <c>Environment.Exit(0)</c>, which cut the process before a single hosted
    /// service was stopped — no flush, no persistence, on the operator's own shutdown path.
    /// </summary>
    [Theory]
    [InlineData("quit")]
    [InlineData("exit")]
    public async Task Quit_StopsTheApplicationGracefully(string command)
    {
        (ConsoleCommandDispatcher dispatcher, List<string> output, FakeLifetime lifetime) =
            Dispatcher();

        await dispatcher.ExecuteAsync(command, output.Add, CancellationToken.None);

        lifetime.StopRequested.Should().BeTrue();
    }

    [Fact]
    public async Task BlankInput_IsANoOp()
    {
        (ConsoleCommandDispatcher dispatcher, List<string> output, _) = Dispatcher();

        bool handled = await dispatcher.ExecuteAsync("   ", output.Add, CancellationToken.None);

        handled.Should().BeTrue();
        output.Should().BeEmpty();
    }

    [Fact]
    public void Announce_RequiresTheServerControlCapability() =>
        Dispatcher()
            .Dispatcher.Find("announce")!
            ?.RequiredCapability.Should()
            .Be(Capabilities.Dashboard.OpsServerControl);

    /// <summary>
    /// The downtime is optional and defaults, so a second argument that is not a number must be
    /// refused rather than read as "back in 0 minutes" — <c>int.TryParse</c> writes 0 into its out
    /// parameter on failure, which is a perfectly plausible number for this field.
    /// </summary>
    [Theory]
    [InlineData("announce")]
    [InlineData("announce later")]
    [InlineData("announce -5")]
    [InlineData("announce 5 soon")]
    [InlineData("announce 5 -2")]
    public async Task Announce_WithoutAUsableNumber_AnswersWithItsUsage(string input)
    {
        (ConsoleCommandDispatcher dispatcher, List<string> output, _) = Dispatcher();

        await dispatcher.ExecuteAsync(input, output.Add, CancellationToken.None);

        output
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("Usage: announce <minutes> [downtime]");
    }

    [Theory]
    [InlineData("announce 5")]
    [InlineData("announce 5 10")]
    public async Task Announce_OnAnEmptyHotel_SaysSoRatherThanClaimingASweep(string input)
    {
        (ConsoleCommandDispatcher dispatcher, List<string> output, _) = Dispatcher();

        await dispatcher.ExecuteAsync(input, output.Add, CancellationToken.None);

        output.Should().ContainSingle().Which.Should().Contain("Nobody is connected");
    }

    [Fact]
    public async Task ReloadPlugin_WithoutAKey_AnswersWithItsUsage()
    {
        (ConsoleCommandDispatcher dispatcher, List<string> output, _) = Dispatcher();

        await dispatcher.ExecuteAsync("reload-plugin", output.Add, CancellationToken.None);

        output.Should().ContainSingle().Which.Should().Contain("Usage: reload-plugin <key>");
    }

    private static (
        ConsoleCommandDispatcher Dispatcher,
        List<string> Output,
        FakeLifetime Lifetime
    ) Dispatcher()
    {
        FakeLifetime lifetime = new();

        ServiceCollection services = new();
        services.AddSingleton<IHostApplicationLifetime>(lifetime);
        services.AddSingleton<ISessionGateway>(new EmptyHotel());

        return (new ConsoleCommandDispatcher(services.BuildServiceProvider()), [], lifetime);
    }

    /// <summary>A hotel nobody is connected to, which is every hotel a unit test runs against.</summary>
    private sealed class EmptyHotel : ISessionGateway
    {
        public IReadOnlyCollection<PlayerId> GetOnlinePlayerIds() => [];

        public int GetOnlinePlayerCount() => 0;

        public int GetActiveSessionCount() => 0;

        public bool IsOnline(PlayerId playerId) => false;

        public ISessionContext? GetSession(SessionKey key) => null;

        public ISessionContextObserver? GetSessionObserver(SessionKey key) => null;

        public PlayerId GetPlayerId(SessionKey key) => -1;

        public Task<bool> AddSessionAsync(SessionKey key, ISessionContext ctx) =>
            Task.FromResult(true);

        public Task RemoveSessionAsync(SessionKey key, CancellationToken ct) => Task.CompletedTask;

        public Task AddSessionToPlayerAsync(
            SessionKey key,
            PlayerId playerId,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task RemoveSessionFromPlayerAsync(PlayerId playerId, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        public bool StopRequested { get; private set; }

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => StopRequested = true;
    }
}
