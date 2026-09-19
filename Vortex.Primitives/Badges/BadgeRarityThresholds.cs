using Orleans;

namespace Vortex.Primitives.Badges;

/// <summary>
/// Where one tier ends and the next begins, as a share of the hotel's registered players.
/// </summary>
/// <remarks>
/// Shares rather than counts, because "rare" means rare <em>here</em>: 50 owners is most of a small
/// hotel and nothing on a large one. <see cref="UniqueMaxOwners"/> is the exception and is an
/// absolute count — a badge one player holds is unique at any hotel size.
/// <para>
/// <see cref="MinimumPlayers"/> is the floor that stops a hotel with eleven accounts from declaring
/// its welcome badge legendary. Below it every badge is <see cref="BadgeRarityTier.Common"/>.
/// </para>
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record BadgeRarityThresholds
{
    [Id(0)]
    public required int MinimumPlayers { get; init; }

    [Id(1)]
    public required int UniqueMaxOwners { get; init; }

    [Id(2)]
    public required double LegendaryMaxShare { get; init; }

    [Id(3)]
    public required double MythicalMaxShare { get; init; }

    [Id(4)]
    public required double EpicMaxShare { get; init; }

    [Id(5)]
    public required double RareMaxShare { get; init; }

    [Id(6)]
    public required double UncommonMaxShare { get; init; }
}
