using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Orleans.Snapshots.Room;

namespace Vortex.Primitives.Orleans.Snapshots.Navigator;

[GenerateSerializer, Immutable]
public record NavigatorSearchResultBlockSnapshot
{
    [Id(0)]
    public required string SearchCode { get; init; }

    [Id(1)]
    public required string Text { get; init; }

    [Id(2)]
    public required NavigatorActionAllowedType ActionAllowed { get; init; }

    [Id(3)]
    public required string Localization { get; init; }

    [Id(4)]
    public required bool ForceClosed { get; init; }

    [Id(5)]
    public required NavigatorViewModeType ViewMode { get; init; }

    /// <summary>The rooms in this block, carried as the room snapshot itself rather than as a
    /// navigator-shaped copy of it. The copy was an empty subclass, and rebuilding it field by field
    /// silently dropped whatever the room had gained since — the guild badge once, the room ad
    /// after it.</summary>
    [Id(6)]
    public required ImmutableArray<RoomInfoSnapshot> Results { get; init; }
}
