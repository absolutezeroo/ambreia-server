using System.Collections.Generic;
using Orleans;
using Vortex.Primitives.Groups.Snapshots;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Users;

[GenerateSerializer, Immutable]
public sealed record ExtendedProfileMessageComposer : IComposer
{
    [Id(0)]
    public required int UserId { get; init; }

    [Id(1)]
    public required string UserName { get; init; }

    [Id(2)]
    public required string Figure { get; init; }

    [Id(3)]
    public required string Motto { get; init; }

    [Id(4)]
    public required string CreationDate { get; init; }

    [Id(5)]
    public required int AchievementScore { get; init; }

    [Id(6)]
    public required int FriendCount { get; init; }

    [Id(7)]
    public required bool IsFriend { get; init; }

    [Id(8)]
    public required bool IsFriendRequestSent { get; init; }

    /// <summary>Tri-state, not a flag: 0 offline, 1 online, 2 online-but-hidden. WIN63 reads it
    /// with <c>readByte()</c> into an int and switches on three constants
    /// (unknowns/_SafePkg_1731/_SafeCls_2228.as), so a bool could never express state 2.</summary>
    [Id(9)]
    public required int OnlineStatus { get; init; }

    [Id(10)]
    public required List<GuildInfoSnapshot> Guilds { get; init; }

    [Id(11)]
    public required int LastAccessSinceInSeconds { get; init; }

    [Id(12)]
    public required bool OpenProfileWindow { get; init; }

    [Id(13)]
    public required bool IsHidden { get; init; }

    [Id(15)]
    public required int IntegerField24 { get; init; }

    [Id(16)]
    public required int StarGemCount { get; init; }

    [Id(17)]
    public required bool BooleanField26 { get; init; }

    [Id(18)]
    public required bool BooleanField27 { get; init; }

    /// <summary>How many badges the player owns in total.</summary>
    [Id(19)]
    public int TotalBadges { get; init; }

    /// <summary>The player's achievement level.</summary>
    [Id(20)]
    public int AchievementLevel { get; init; }

    /// <summary>Badge count per rarity tier. The client keys its rarity breakdown off this and
    /// answers <c>getBadgeCountByRarityId()</c> from it.</summary>
    [Id(21)]
    public required List<BadgeRarityCount> BadgeRarityCounts { get; init; }

    /// <summary>The player's rank by total badges held.</summary>
    [Id(22)]
    public int TotalBadgesRank { get; init; }
}

/// <summary>One (rarity tier, count) pair inside an extended profile. The tier is written as a
/// byte and the count as an int, matching WIN63's
/// <c>unknowns/_SafePkg_1731/_SafeCls_3034.as</c>.</summary>
[GenerateSerializer, Immutable]
public sealed record BadgeRarityCount
{
    [Id(0)]
    public required int RarityId { get; init; }

    [Id(1)]
    public required int Count { get; init; }
}
