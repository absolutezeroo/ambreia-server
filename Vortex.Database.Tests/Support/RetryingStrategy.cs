using System;
using Microsoft.EntityFrameworkCore.Storage;

namespace Vortex.Database.Tests.Support;

/// <summary>
/// A retrying execution strategy, as Pomelo configures one in production. SQLite has none of its
/// own, so code that opens a transaction by hand passes here and fails on MySQL — unless a test
/// installs this.
/// </summary>
internal sealed class RetryingStrategy(ExecutionStrategyDependencies dependencies)
    : ExecutionStrategy(dependencies, 3, TimeSpan.FromMilliseconds(10))
{
    protected override bool ShouldRetryOn(Exception exception) => false;
}
