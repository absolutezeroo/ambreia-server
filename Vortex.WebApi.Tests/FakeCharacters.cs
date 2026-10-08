using System.Collections.Generic;
using System.Threading.Tasks;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.Rpg.Characters.Grains;
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
                _ => null,
            }
        );

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
