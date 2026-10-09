using System;

namespace Vortex.Database.Tests.Support;

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
