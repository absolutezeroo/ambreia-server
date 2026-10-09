using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Vortex.Database.Entities.Rpg;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Learning;

namespace Vortex.Rpg.Characters.Grains;

/// <summary>
/// The skills a character knows and its action bar (spec M1b §9). Known skills come from the class,
/// the specialization and the level, never stored; the bar is a JSON array of action ids in
/// <c>action_bar</c>, written with the progress.
/// </summary>
internal sealed partial class PlayerCharacterGrain
{
    private const string SkillPrefix = "skill:";

    public async Task SetActionBarAsync(int slot, string actionId)
    {
        if (_character is not { } c)
        {
            return;
        }

        string? refusal =
            slot < 0 || slot >= _config.ActionBarSize ? RpgSkillResultMessageComposer.BadSlot
            : actionId.Length == 0 ? null
            : RefusalFor(c, actionId);

        if (refusal is not null)
        {
            await PushAsync(
                new RpgSkillResultMessageComposer { SkillId = SkillIdOf(actionId), Code = refusal }
            );

            return;
        }

        string[] bar = Bar(c);
        int already = Array.IndexOf(bar, actionId);

        if (actionId.Length > 0 && already >= 0)
        {
            bar[already] = string.Empty;
        }

        bar[slot] = actionId;
        Store(c, bar);

        await PushAsync(SkillsComposer(c));
    }

    /// <summary>Null when the character may put this action on its bar.</summary>
    private string? RefusalFor(PlayerCharacterEntity c, string actionId)
    {
        if (
            !actionId.StartsWith(SkillPrefix, StringComparison.Ordinal)
            || content.Get<SkillDefinition>(SkillIdOf(actionId)) is not { Retired: false }
        )
        {
            return RpgSkillResultMessageComposer.UnknownSkill;
        }

        return Known(c).Any(s => s.Id == SkillIdOf(actionId))
            ? null
            : RpgSkillResultMessageComposer.NotLearned;
    }

    private static string SkillIdOf(string actionId) =>
        actionId.StartsWith(SkillPrefix, StringComparison.Ordinal)
            ? actionId[SkillPrefix.Length..]
            : actionId;

    private IReadOnlyList<SkillDefinition> Known(PlayerCharacterEntity c) =>
        KnownSkills.For(content, c.ClassId, c.SpecializationId, c.Level);

    /// <summary>The stored bar, padded or cut to the configured size.</summary>
    private string[] Bar(PlayerCharacterEntity c)
    {
        string[] stored = c.ActionBar is { } json
            ? JsonSerializer.Deserialize<string[]>(json) ?? []
            : [];
        string[] bar = new string[_config.ActionBarSize];

        for (int i = 0; i < bar.Length; i++)
        {
            bar[i] = i < stored.Length ? stored[i] : string.Empty;
        }

        return bar;
    }

    private void Store(PlayerCharacterEntity c, string[] bar)
    {
        c.ActionBar = JsonSerializer.Serialize(bar);
        _dirty = true;
    }

    /// <summary>The bar of a class just chosen: its known skills, in the order they are learned, as
    /// stored JSON.</summary>
    private string BarOfKnownSkills(string classId, string specializationId, int level)
    {
        string[] bar = new string[_config.ActionBarSize];
        Array.Fill(bar, string.Empty);
        PlaceLearned(bar, KnownSkills.For(content, classId, specializationId, level));

        return JsonSerializer.Serialize(bar);
    }

    /// <summary>Skills learned from <paramref name="fromLevel"/> (excluded) go into the first empty
    /// slots; one already on the bar stays where it is (spec M1b §9).</summary>
    private void PlaceSkillsLearnedSince(PlayerCharacterEntity c, int fromLevel)
    {
        string[] bar = Bar(c);

        if (PlaceLearned(bar, Known(c).Where(s => s.Level > fromLevel)))
        {
            Store(c, bar);
        }
    }

    private static bool PlaceLearned(string[] bar, IEnumerable<SkillDefinition> skills)
    {
        bool placed = false;

        foreach (string action in skills.Select(s => SkillPrefix + s.Id))
        {
            int empty = Array.IndexOf(bar, string.Empty);

            if (Array.IndexOf(bar, action) >= 0 || empty < 0)
            {
                continue;
            }

            bar[empty] = action;
            placed = true;
        }

        return placed;
    }

    private RpgSkillsMessageComposer SkillsComposer(PlayerCharacterEntity c) =>
        new() { Known = [.. Known(c).Select(s => s.Id)], Bar = [.. Bar(c)] };
}
