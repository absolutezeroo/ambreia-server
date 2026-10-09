using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Rpg;
using Vortex.Logging.Extensions;
using Vortex.Primitives.Events.Rpg;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Characters.Grains;

/// <summary>A character's class: chosen once at creation, and left once from the starter class.</summary>
internal sealed partial class PlayerCharacterGrain
{
    public async Task<ChooseClassOutcome> ChooseClassAsync(string classId)
    {
        await RefreshIdsAsync();

        if (_character is { } current)
        {
            ChooseClassOutcome outcome = await LeaveTheStarterClassAsync(current, classId);

            // Chosen on the character screen of another tab while this character plays: its HUD
            // would keep the starter's stats until the next zone (spec F3a §5.6).
            if (outcome == ChooseClassOutcome.Chosen)
            {
                await PushStatsAsync();
                await PushAsync(SkillsComposer(current));
            }

            return outcome;
        }

        if (content.Get<ClassDefinition>(classId) is not { Retired: false } chosen)
        {
            return ChooseClassOutcome.UnknownClass;
        }

        PlayerCharacterEntity created = new()
        {
            PlayerEntityId = _playerId.Value,
            ClassId = chosen.Id,
            // One specialization per class in V1, given at once (spec Personnage §2.4).
            SpecializationId = chosen.Specializations[0],
            ActionBar = BarOfKnownSkills(chosen.Id, chosen.Specializations[0], 1),
        };

        await using VortexDbContext db = await dbFactory.CreateDbContextAsync();
        db.PlayerCharacters.Add(created);
        await db.SaveChangesAsync();

        _character = created;
        _stats = null;

        logger.LogInformation(
            "Character created for player {PlayerId}: {ClassId} ({SpecializationId}).",
            _playerId.Value,
            created.ClassId,
            created.SpecializationId
        );
        events
            .PublishAsync(
                new CharacterCreatedEvent(_playerId, created.ClassId, created.SpecializationId)
            )
            .LogAndForget(logger, "Publishing CharacterCreated for {PlayerId}", _playerId.Value);

        return ChooseClassOutcome.Chosen;
    }

    /// <summary>
    /// The one class change there is (owner decision, 2026-10-08): from the starter class « Sans
    /// classe » to a real class, keeping level and XP. A real class is permanent. The ids are
    /// written here, at once — the choice is the player's, not a copy from memory.
    /// </summary>
    private async Task<ChooseClassOutcome> LeaveTheStarterClassAsync(
        PlayerCharacterEntity current,
        string classId
    )
    {
        if (content.Get<ClassDefinition>(current.ClassId) is not { Starter: true })
        {
            return ChooseClassOutcome.AlreadyChosen;
        }

        if (content.Get<ClassDefinition>(classId) is not { Retired: false } chosen)
        {
            return ChooseClassOutcome.UnknownClass;
        }

        if (chosen.Starter)
        {
            return ChooseClassOutcome.AlreadyChosen;
        }

        string oldClass = current.ClassId;
        string specialization = chosen.Specializations[0];
        // The starter class has no skill: the bar becomes the new class's (spec M1b §9).
        string bar = BarOfKnownSkills(chosen.Id, specialization, current.Level);

        await using VortexDbContext db = await dbFactory.CreateDbContextAsync();
        await db
            .PlayerCharacters.Where(x => x.PlayerEntityId == current.PlayerEntityId)
            .ExecuteUpdateAsync(up =>
                up.SetProperty(x => x.ClassId, chosen.Id)
                    .SetProperty(x => x.SpecializationId, specialization)
                    .SetProperty(x => x.ActionBar, bar)
            );

        current.ClassId = chosen.Id;
        current.SpecializationId = specialization;
        current.ActionBar = bar;
        _stats = null;

        logger.LogInformation(
            "Player {PlayerId} left {OldClassId} for {ClassId} at level {Level}.",
            _playerId.Value,
            oldClass,
            chosen.Id,
            current.Level
        );
        Publish(new CharacterClassChangedEvent(_playerId, oldClass, chosen.Id));
        Publish(new CharacterStatsChangedEvent(_playerId));

        return ChooseClassOutcome.Chosen;
    }
}
