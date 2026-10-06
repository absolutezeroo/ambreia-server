using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Sound.Snapshots;

namespace Vortex.Protocol.Messages.Outgoing.Sound;

/// <summary>
/// The sound machine's playlist — the Trax composer's own list. Carries the songs themselves rather
/// than the disks holding them.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record PlayListMessageComposer : IComposer
{
    [Id(0)]
    public required ImmutableArray<SongSnapshot> Songs { get; init; }

    /// <summary>How far into the list playback stands, in milliseconds.</summary>
    [Id(1)]
    public required int SynchronizationCountMs { get; init; }
}
