using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Vortex.Primitives.Vault.Enums;

namespace Vortex.Players.Vault;

/// <summary>
/// The registered settlers, by the reward type each handles. Two settlers for one type is a wiring
/// mistake that would pay a row by whichever registered last, so it refuses instead.
/// </summary>
internal sealed class VaultRewardSettlerMap
{
    private readonly Dictionary<VaultRewardType, IVaultRewardSettler> _byType = [];

    public VaultRewardSettlerMap(IEnumerable<IVaultRewardSettler> settlers)
    {
        foreach (IVaultRewardSettler settler in settlers)
        {
            foreach (VaultRewardType type in settler.Handles)
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
        VaultRewardType type,
        [NotNullWhen(true)] out IVaultRewardSettler? settler
    ) => _byType.TryGetValue(type, out settler);
}
