using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vortex.Main;
using Vortex.Main.Configuration;
using Xunit;

namespace Vortex.Hosting.Tests;

/// <summary>
/// The shutdown notice holds the whole process open, so the two things worth proving are that it
/// stays out of the way when it is off — which is the default, and every deployment that has ever
/// run — and that it does not hold a hotel open for an audience of nobody.
/// </summary>
public sealed class ShutdownNoticeTests
{
    [Fact]
    public async Task WithNoNoticeConfigured_NothingIsAnnouncedAndNothingWaits()
    {
        RecordingAnnouncer announcer = new(reached: 12);
        VortexEmulator emulator = Emulator(announcer, new ShutdownConfig());

        await emulator.StoppingAsync(CancellationToken.None);

        announcer.Calls.Should().BeEmpty();
    }

    /// <summary>
    /// A deploy at four in the morning must not cost five minutes for nobody. The announcer is still
    /// asked — that is how the hotel finds out it is empty — but the wait is skipped.
    /// </summary>
    [Fact]
    public async Task WithNobodyConnected_TheShutdownIsNotHeld()
    {
        RecordingAnnouncer announcer = new(reached: 0);
        VortexEmulator emulator = Emulator(announcer, new ShutdownConfig { NoticeMinutes = 5 });

        Stopwatch elapsed = Stopwatch.StartNew();
        await emulator.StoppingAsync(CancellationToken.None);
        elapsed.Stop();

        announcer.Calls.Should().ContainSingle().Which.Should().Be((5, 2));
        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// A cancelled token is what a grace period shorter than the notice looks like from in here. The
    /// shutdown has to carry on regardless: this is a courtesy, not a barrier.
    /// </summary>
    [Fact]
    public async Task WhenTheNoticeIsCutShort_TheShutdownStillProceeds()
    {
        RecordingAnnouncer announcer = new(reached: 4);
        VortexEmulator emulator = Emulator(announcer, new ShutdownConfig { NoticeMinutes = 5 });

        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        Func<Task> stopping = () => emulator.StoppingAsync(cancelled.Token);

        await stopping.Should().NotThrowAsync();
    }

    /// <summary>
    /// Only the notice is exercised here, and it runs before anything else is stopped — so the
    /// collaborators the rest of the lifecycle needs are deliberately absent rather than faked. A
    /// null that is never touched says more about what this path uses than a stub would.
    /// </summary>
    private static VortexEmulator Emulator(RecordingAnnouncer announcer, ShutdownConfig config) =>
        new(
            NullLogger<VortexEmulator>.Instance,
            [],
            networkManager: null!,
            grainFactory: null!,
            Options.Create(new OrleansHostConfig()),
            announcer,
            Options.Create(config)
        );
}
