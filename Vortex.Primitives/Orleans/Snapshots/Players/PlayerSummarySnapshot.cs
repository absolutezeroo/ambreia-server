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

    // [Id(7)]-[Id(10)] are retired (favourite group, hotel mute, safety lock; the cut of
    // 2026-10-07). Never reuse them: a silo still running the old shape would read them back.

    /// <summary>
    /// Whether the player holds <c>room.moderate.any</c>. It rides the summary because the room
    /// avatar block carries it on the wire, and both readers of it are room-side: the chat-flood
    /// exemption and the client's Builders Club "a guest is watching" check.
    /// </summary>
    [Id(11)]
    public bool IsModerator { get; init; }
}
