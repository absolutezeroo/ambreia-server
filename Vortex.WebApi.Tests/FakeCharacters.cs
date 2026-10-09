using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.Rpg.Characters.Grains;
using Vortex.Primitives.Rpg.Items;
using Vortex.Tests.Support;

namespace Vortex.WebApi.Tests;

/// <summary>
/// Stands in for the character grains behind the XP route: a character per player id, and every
/// XP grant recorded.
/// </summary>
internal sealed class FakeCharacters
{
    public Dictionary<long, CharacterSummary> ByPlayer { get; } = [];

    public List<(long Player, int Amount)> XpGrants { get; } = [];

    /// <summary>Every item grant that went through; a key seen before grants nothing.</summary>
    public List<(long Player, string Key, string Definition, int Quantity)> ItemGrants { get; } =
    [];

    private readonly HashSet<string> _grantKeys = [];

    public IPlayerCharacterGrain For(long player) =>
        FakeProxy.Create<IPlayerCharacterGrain>(call =>
            call.Method.Name switch
            {
                nameof(IPlayerCharacterGrain.GetSummaryAsync) => Task.FromResult(
                    ByPlayer.GetValueOrDefault(player)
                ),
                nameof(IPlayerCharacterGrain.GainXpAsync) => Task.FromResult(
                    Grant(player, (int)call.Args![0]!)
                ),
                nameof(IPlayerCharacterGrain.GrantItemsAsync) => Task.FromResult(
                    GrantItems(player, (string)call.Args![0]!, (ItemGrantLine[])call.Args[1]!)
                ),
                _ => null,
            }
        );

    private ItemGrantResult GrantItems(long player, string key, ItemGrantLine[] items)
    {
        if (!ByPlayer.ContainsKey(player))
        {
            return new ItemGrantResult { Outcome = ItemOutcome.NoCharacter };
        }

        if (!_grantKeys.Add(key))
        {
            return new ItemGrantResult { Outcome = ItemOutcome.DuplicateGrant };
        }

        foreach (ItemGrantLine line in items)
        {
            ItemGrants.Add((player, key, line.DefinitionId, line.Quantity));
        }

        return new ItemGrantResult { Outcome = ItemOutcome.Ok, ToBag = items.Sum(i => i.Quantity) };
    }

    private XpGrantOutcome Grant(long player, int amount)
    {
        if (!ByPlayer.ContainsKey(player))
        {
            return XpGrantOutcome.NoCharacter;
        }

        XpGrants.Add((player, amount));

        return XpGrantOutcome.Granted;
    }
}
