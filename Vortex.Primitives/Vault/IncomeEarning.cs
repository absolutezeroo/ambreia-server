using Orleans;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Vault.Enums;

namespace Vortex.Primitives.Vault;

/// <summary>
/// One thing a player earned, as the source that paid it describes it: which Earnings row it belongs
/// to, in the wallet's own currency vocabulary. The source never decides whether it waits in the
/// vault or lands in the wallet — <c>IPlayerVaultGrain.EarnAsync</c> does.
/// </summary>
/// <remarks>
/// <see cref="SourceKey"/> names the event that earned it (<c>club-payday:2026-09-01</c>) and is what
/// makes a retried deposit a no-op. It is also used as a commerce receipt step key, hence the length
/// cap.
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record IncomeEarning
{
    public const int MaxSourceKeyLength = 64;

    [Id(0)]
    public required VaultRewardCategoryType Category { get; init; }

    [Id(1)]
    public required CurrencyKind Currency { get; init; }

    [Id(2)]
    public required int Amount { get; init; }

    [Id(3)]
    public required string SourceKey { get; init; }
}
