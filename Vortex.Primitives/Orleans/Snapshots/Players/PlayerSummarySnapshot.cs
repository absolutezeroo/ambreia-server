using System;
using Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Primitives.Orleans.Snapshots.Players;

[GenerateSerializer, Immutable]
public sealed record PlayerSummarySnapshot
{
    [Id(0)]
    public required PlayerId PlayerId { get; init; }

    [Id(1)]
    public required string Name { get; init; }

    [Id(2)]
    public required string Motto { get; init; }

    [Id(3)]
    public required string Figure { get; init; }

    [Id(4)]
    public required AvatarGenderType Gender { get; init; }

    [Id(5)]
    public required int AchievementScore { get; init; }

    [Id(6)]
    public required DateTime CreatedAt { get; init; }

    /// <summary>Guild whose badge the avatar displays, or 0 when the player has no favourite.</summary>
    [Id(7)]
    public int FavouriteGroupId { get; init; }

    [Id(8)]
    public string FavouriteGroupName { get; init; } = string.Empty;

    /// <summary>
    /// Hotel-wide mute expiry, or null when not muted. Carried on the entry snapshot the room
    /// already fetches so a room can enforce a staff mute without a grain call per chat line.
    /// </summary>
    [Id(9)]
    public DateTime? MutedUntilUtc { get; init; }

    /// <summary>
    /// The owning account's safety lock. It rides the summary because the user object is built from
    /// it: the client is told at login, and again by its own message when the lock moves
    /// mid-session.
    /// </summary>
    [Id(10)]
    public bool SafetyLocked { get; init; }

    /// <summary>
    /// Whether the player holds <c>room.moderate.any</c>. It rides the summary because the room
    /// avatar block carries it on the wire, and both readers of it are room-side: the chat-flood
    /// exemption and the client's Builders Club "a guest is watching" check.
    /// </summary>
    [Id(11)]
    public bool IsModerator { get; init; }
}
