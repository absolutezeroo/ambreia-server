using System;
using Microsoft.EntityFrameworkCore.Storage;

namespace Vortex.Database.Tests.Support;

/// <summary>
/// A retrying strategy that does retry: on <see cref="TimeoutException"/>, the transient failure
/// <see cref="CommitFaults"/> raises. <see cref="RetryingStrategy"/> never retries, so a code path
/// that is only wrong on the second attempt passes under it.
/// </summary>
internal sealed class RetryOnTimeoutStrategy(ExecutionStrategyDependencies dependencies)
    : ExecutionStrategy(dependencies, 3, TimeSpan.FromMilliseconds(1))
{
    protected override bool ShouldRetryOn(Exception exception) => exception is TimeoutException;
}
