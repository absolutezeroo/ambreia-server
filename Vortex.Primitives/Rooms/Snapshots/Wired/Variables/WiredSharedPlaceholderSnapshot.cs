using System.Collections.Generic;
using Orleans;

namespace Vortex.Primitives.Rooms.Snapshots.Wired.Variables;

/// <summary>One global placeholder a room offers the rest of its owner's rooms.</summary>
/// <remarks>
/// The client's <c>SharedGlobalPlaceholder</c>: room id, room name, placeholder name — flat, and
/// grouped by room on the client (<c>GlobalPlaceholderAddon.findRooms()</c>).
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record WiredSharedPlaceholderSnapshot
{
    [Id(0)]
    public required int RoomId { get; init; }

    [Id(1)]
    public required string RoomName { get; init; }

    [Id(2)]
    public required string PlaceholderName { get; init; }
}

/// <summary>
/// Every global placeholder a global-placeholder box may take its value from: the ones set to a
/// value in the other rooms of the player who opened it.
/// </summary>
/// <remarks>
/// Its own wired-context section (<see cref="Enums.Wired.WiredContextType.SharedGlobalPlaceholders"/>),
/// and like the shared-variable list it is sent empty rather than left out: the client disables
/// "from another room" when the section is missing.
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record WiredSharedPlaceholderListSnapshot : WiredVariableContextSnapshot
{
    [Id(1)]
    public required List<WiredSharedPlaceholderSnapshot> Elements { get; init; }
}
