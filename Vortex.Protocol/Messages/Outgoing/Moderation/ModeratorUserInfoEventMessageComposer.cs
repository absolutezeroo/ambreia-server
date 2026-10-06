using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Moderation;

/// <summary>
/// The mod tool's user card. The last two fields are an optional tail the client only reads while
/// bytes remain, so a hotel that keeps no sanction history simply leaves
/// <see cref="HasSanctionHistory"/> false and writes nothing.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record ModeratorUserInfoEventMessageComposer : IComposer
{
    [Id(0)]
    public required int UserId { get; init; }

    [Id(1)]
    public required string UserName { get; init; }

    [Id(2)]
    public required string Figure { get; init; }

    [Id(3)]
    public required int RegistrationAgeInMinutes { get; init; }

    [Id(4)]
    public required int MinutesSinceLastLogin { get; init; }

    [Id(5)]
    public required bool Online { get; init; }

    [Id(6)]
    public int CfhCount { get; init; }

    [Id(7)]
    public int AbusiveCfhCount { get; init; }

    [Id(8)]
    public int CautionCount { get; init; }

    [Id(9)]
    public int BanCount { get; init; }

    [Id(10)]
    public int TradingLockCount { get; init; }

    [Id(11)]
    public string TradingExpiryDate { get; init; } = string.Empty;

    [Id(12)]
    public string LastPurchaseDate { get; init; } = string.Empty;

    [Id(13)]
    public int IdentityId { get; init; }

    [Id(14)]
    public int IdentityRelatedBanCount { get; init; }

    [Id(15)]
    public string PrimaryEmailAddress { get; init; } = string.Empty;

    // Id 16 was the user classification, removed in M0 with the :uc commands; the serializer writes
    // an empty string in its place. Retired, not reused.

    /// <summary>Emits the optional tail the client guards behind <c>bytesAvailable</c>.</summary>
    [Id(17)]
    public bool HasSanctionHistory { get; init; }

    [Id(18)]
    public string LastSanctionTime { get; init; } = string.Empty;

    [Id(19)]
    public int SanctionAgeHours { get; init; }
}
