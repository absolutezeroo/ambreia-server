using System;
using Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Primitives.Pets.Snapshots;

[GenerateSerializer, Immutable]
public sealed record PetSnapshot
{
    [Id(0)]
    public required int PetId { get; init; }

    [Id(1)]
    public required PlayerId OwnerId { get; init; }

    [Id(2)]
    public required int? RoomId { get; init; }

    [Id(3)]
    public required string Name { get; init; }

    [Id(4)]
    public required int Type { get; init; }

    [Id(5)]
    public required int Race { get; init; }

    [Id(6)]
    public required string Color { get; init; }

    [Id(7)]
    public required AvatarGenderType Gender { get; init; }

    [Id(8)]
    public required int Level { get; init; }

    [Id(9)]
    public required int Experience { get; init; }

    [Id(10)]
    public required int Energy { get; init; }

    [Id(11)]
    public required int Nutrition { get; init; }

    [Id(12)]
    public required int Respect { get; init; }

    [Id(13)]
    public required int X { get; init; }

    [Id(14)]
    public required int Y { get; init; }

    [Id(15)]
    public required double Z { get; init; }

    [Id(16)]
    public required Rotation Direction { get; init; }

    // 17 and 18 were the per-pet daily respect counters. Scratching is limited by the scratcher's
    // own allowance alone, which is the only budget the client models; the ids stay retired rather
    // than reused, because an old serialised snapshot must not be read back as something else.

    // 19 to 23 were the two parents, the breeding flag, the monsterplant rarity and its watering
    // stamp, removed in M0 with breeding and monsterplants. Retired for the same reason.

    /// <summary>A saddle has been fitted; without one the pet cannot be ridden.</summary>
    [Id(24)]
    public bool HasSaddle { get; init; }

    /// <summary>The owner lets other players ride it. The owner always may.</summary>
    [Id(25)]
    public bool RidingPermission { get; init; }

    /// <summary>Who is on its back right now. Runtime only -- nobody is riding after a restart.</summary>
    [Id(26)]
    public PlayerId? RiderId { get; init; }

    /// <summary>
    /// Mood, 0-100. Its own stat: it drains slowly whatever else the pet is doing, comes back while
    /// the pet rests, and jumps when the pet is played with. The info panel's happiness bar reads
    /// this and nothing else.
    /// </summary>
    [Id(27)]
    public int Happiness { get; init; } = 100;

    /// <summary>When the pet was created -- the info panel shows its age in days.</summary>
    [Id(28)]
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Water, 0-100. Pets have no needs since M0: this, <see cref="Nutrition"/> and
    /// <see cref="Energy"/> are kept because they are stored and sent, and stay at their maximum.
    /// </summary>
    [Id(29)]
    public int Thirst { get; init; } = 100;

    /// <summary>The fitted saddle's furniture definition, handed back when it comes off.</summary>
    [Id(30)]
    public int? SaddleDefinitionId { get; init; }

    /// <summary>
    /// Age in whole days, counting the day it was created as day one: a pet bought this morning is
    /// one day old, not zero. The panel showed 0 for every pet in the hotel, because the field was
    /// left at its default and nothing ever filled it in.
    /// </summary>
    public int AgeInDays(DateTime nowUtc) =>
        Math.Max(1, (int)Math.Floor((nowUtc - CreatedAt).TotalDays) + 1);
}
