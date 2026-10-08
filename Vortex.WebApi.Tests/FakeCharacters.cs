using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.Rpg.Characters.Grains;
using Vortex.Tests.Support;

namespace Vortex.WebApi.Tests;

/// <summary>
/// Stands in for the character grains behind the routes: a character per player id, a class choice
/// that works once and only for <c>class.warrior</c>, and every XP grant recorded.
/// </summary>
internal sealed class FakeCharacters
{
    public const string KnownClass = "class.warrior";

    public Dictionary<long, CharacterSummary> ByPlayer { get; } = [];

    public List<(long Player, string ClassId)> Choices { get; } = [];

    public List<(long Player, int Amount)> XpGrants { get; } = [];

    /// <summary>Players whose grain fails (its database read threw).</summary>
    public HashSet<long> Broken { get; } = [];

    public IPlayerCharacterGrain For(long player) =>
        FakeProxy.Create<IPlayerCharacterGrain>(call =>
            call.Method.Name switch
            {
                nameof(IPlayerCharacterGrain.GetSummaryAsync) => Broken.Contains(player)
                    ? Task.FromException<CharacterSummary?>(
                        new InvalidOperationException("database unavailable")
                    )
                    : Task.FromResult(ByPlayer.GetValueOrDefault(player)),
                nameof(IPlayerCharacterGrain.ChooseClassAsync) => Task.FromResult(
                    Choose(player, (string)call.Args![0]!)
                ),
                nameof(IPlayerCharacterGrain.GainXpAsync) => Task.FromResult(
                    Grant(player, (int)call.Args![0]!)
                ),
                _ => null,
            }
        );

    private ChooseClassOutcome Choose(long player, string classId)
    {
        if (ByPlayer.ContainsKey(player))
        {
            return ChooseClassOutcome.AlreadyChosen;
        }

        if (classId != KnownClass)
        {
            return ChooseClassOutcome.UnknownClass;
        }

        Choices.Add((player, classId));
        ByPlayer[player] = new CharacterSummary
        {
            ClassId = classId,
            SpecializationId = "spec.warrior.guardian",
            Level = 1,
            Xp = 0,
        };

        return ChooseClassOutcome.Chosen;
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
