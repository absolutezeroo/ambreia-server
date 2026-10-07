using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Main.Console;
using Vortex.Main.Hosting;
using Vortex.Primitives.Console;
using Vortex.Primitives.Permissions;
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
    /// <c>Capabilities.Server.All</c> exists to prevent.
    /// </summary>
    [Fact]
    public void EveryCommandCapability_IsADeclaredDashboardCapability()
    {
        IEnumerable<string> declared = Dispatcher()
            .Dispatcher.Commands.Select(c => c.RequiredCapability)
            .OfType<string>();

        declared
            .Should()
            .OnlyContain(capability => Capabilities.Server.All.Contains(capability));
    }

    [Fact]
    public void Quit_RequiresTheServerControlCapability() =>
        Dispatcher()
            .Dispatcher.Find("quit")!
            ?.RequiredCapability.Should()
            .Be(Capabilities.Server.Control);

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
            .Be(Capabilities.Server.Control);

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
    [InlineData("announce 5", 5, 2)]
    [InlineData("announce 5 10", 5, 10)]
    [InlineData("announce 0", 0, 2)]
    public async Task Announce_PassesOnTheMinutesAndTheDowntime(
        string input,
        int minutes,
        int downtime
    )
    {
        RecordingAnnouncer announcer = new(reached: 3);
        (ConsoleCommandDispatcher dispatcher, List<string> output, _) = Dispatcher(announcer);

        await dispatcher.ExecuteAsync(input, output.Add, CancellationToken.None);

        announcer.Calls.Should().ContainSingle().Which.Should().Be((minutes, downtime));
        output.Should().ContainSingle().Which.Should().Contain("Warned 3 player(s)");
    }

    [Fact]
    public async Task Announce_OnAnEmptyHotel_SaysSoRatherThanClaimingASweep()
    {
        (ConsoleCommandDispatcher dispatcher, List<string> output, _) = Dispatcher();

        await dispatcher.ExecuteAsync("announce 5", output.Add, CancellationToken.None);

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
    ) Dispatcher(RecordingAnnouncer? announcer = null)
    {
        FakeLifetime lifetime = new();

        ServiceCollection services = new();
        services.AddSingleton<IHostApplicationLifetime>(lifetime);
        services.AddSingleton<IMaintenanceAnnouncer>(announcer ?? new RecordingAnnouncer());

        return (new ConsoleCommandDispatcher(services.BuildServiceProvider()), [], lifetime);
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
