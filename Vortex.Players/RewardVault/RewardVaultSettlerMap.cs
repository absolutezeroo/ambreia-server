using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Vortex.Primitives.RewardVault.Enums;

namespace Vortex.Players.RewardVault;

/// <summary>
/// The registered settlers, by the reward type each handles. Two settlers for one type is a wiring
/// mistake that would pay a row by whichever registered last, so it refuses instead.
/// </summary>
internal sealed class RewardVaultSettlerMap
{
    private readonly Dictionary<RewardVaultType, IRewardVaultSettler> _byType = [];

    public RewardVaultSettlerMap(IEnumerable<IRewardVaultSettler> settlers)
    {
        foreach (IRewardVaultSettler settler in settlers)
        {
            foreach (RewardVaultType type in settler.Handles)
            {
                if (!_byType.TryAdd(type, settler))
                {
                    throw new InvalidOperationException(
                        $"Two vault settlers handle {type}: {_byType[type].GetType().Name} and {settler.GetType().Name}."
                    );
                }
            }
        }
    }

    public bool TryGet(
        RewardVaultType type,
        [NotNullWhen(true)] out IRewardVaultSettler? settler
    ) => _byType.TryGetValue(type, out settler);
}
