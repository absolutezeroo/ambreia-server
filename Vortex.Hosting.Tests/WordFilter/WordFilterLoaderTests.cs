using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Observability.WordFilter;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Hosting.Tests.WordFilter;

/// <summary>
/// The first load at boot. A database still waking up after a restart must not leave the filter off
/// until somebody happens to edit the list.
/// </summary>
public sealed class WordFilterLoaderTests
{
    [Fact]
    public async Task AFailingFirstLoad_IsRetried_UntilItSucceeds()
    {
        int calls = 0;
        IWordFilter filter = FakeProxy.Create<IWordFilter>(call =>
            call.Method.Name == nameof(IWordFilter.ReloadAsync)
                ? ++calls < 3
                    ? Task.FromException(new InvalidOperationException("database waking up"))
                    : Task.CompletedTask
                : null
        );

        (
            await WordFilterLoader.LoadWithRetryAsync(
                filter,
                NullLogger.Instance,
                attempts: 5,
                firstDelay: TimeSpan.FromMilliseconds(1),
                CancellationToken.None
            )
        )
            .Should()
            .BeTrue();

        calls.Should().Be(3);
    }

    [Fact]
    public async Task ALoadThatNeverSucceeds_GivesUpAfterItsAttempts()
    {
        int calls = 0;
        IWordFilter filter = FakeProxy.Create<IWordFilter>(call =>
            call.Method.Name == nameof(IWordFilter.ReloadAsync)
                ? Task.FromException(new InvalidOperationException($"attempt {++calls}"))
                : null
        );

        (
            await WordFilterLoader.LoadWithRetryAsync(
                filter,
                NullLogger.Instance,
                attempts: 4,
                firstDelay: TimeSpan.FromMilliseconds(1),
                CancellationToken.None
            )
        )
            .Should()
            .BeFalse();

        calls.Should().Be(4);
    }
}
