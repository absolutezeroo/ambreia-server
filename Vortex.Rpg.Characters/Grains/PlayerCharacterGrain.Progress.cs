using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Database.Entities.Rpg;
using Vortex.Primitives.Events.Rpg;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Protocol.Messages.Outgoing.Rpg.Characters;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Characters.Grains;

/// <summary>XP and levels: a grant checked, cancellable, capped, and a level never lost.</summary>
internal sealed partial class PlayerCharacterGrain
{
    public async Task<XpGrantOutcome> GainXpAsync(int amount, string source)
    {
        await RefreshIdsAsync();

        if (_character is not { } c)
        {
            return XpGrantOutcome.NoCharacter;
        }

        if (amount < 1 || amount > _config.MaxXpPerGrant)
        {
            return Refuse(amount, source);
        }

        if (content.Get<ProgressionDefinition>(ProgressionId) is not { } progression)
        {
            logger.LogWarning(
                "XP grant to player {PlayerId} refused: the content has no {ProgressionId}.",
                _playerId.Value,
                ProgressionId
            );

            return XpGrantOutcome.Refused;
        }

        if (c.Level >= progression.MaxLevel)
        {
            return XpGrantOutcome.AtMaxLevel;
        }

        XpGainingEvent gaining = new(_playerId, source) { Amount = amount };

        if ((await cancellableEvents.PublishCancellableAsync(gaining)).Cancel)
        {
            return XpGrantOutcome.Cancelled;
        }

        // The cap holds after a behaviour changed the amount too: no bonus goes past it.
        if (gaining.Amount < 1 || gaining.Amount > _config.MaxXpPerGrant)
        {
            return Refuse(gaining.Amount, source);
        }

        int oldLevel = c.Level;
        c.Xp += gaining.Amount;

        while (c.Level < progression.MaxLevel && c.Xp >= progression.XpToNext[c.Level - 1])
        {
            c.Xp -= progression.XpToNext[c.Level - 1];
            c.Level++;

            logger.LogInformation(
                "Level up: player {PlayerId}, {ClassId}, level {Level}.",
                _playerId.Value,
                c.ClassId,
                c.Level
            );
        }

        if (c.Level >= progression.MaxLevel)
        {
            c.Xp = 0;
        }

        _dirty = true;

        if (c.Level != oldLevel)
        {
            // Never lose a level (spec Personnage §7): written now, and announced only then, so a
            // reward given on LevelUp is never given for a level a crash could take back.
            _stats = null;
            // A skill learned on the way goes on the bar, written with the level (spec M1b §9).
            PlaceSkillsLearnedSince(c, oldLevel);
            await FlushAsync(CancellationToken.None);

            for (int level = oldLevel + 1; level <= c.Level; level++)
            {
                Publish(new LevelUpEvent(_playerId, c.ClassId, level - 1, level));
            }

            Publish(new CharacterStatsChangedEvent(_playerId));
        }

        Publish(new XpGainedEvent(_playerId, gaining.Amount, source, c.Level, c.Xp));

        RpgCharacterProgressMessageComposer progress = new()
        {
            Level = c.Level,
            Xp = c.Xp,
            XpToNext = XpToNext(c, progression),
        };

        await PushProgressAsync(c.Level != oldLevel, progress);

        return XpGrantOutcome.Granted;
    }

    private static int XpToNext(PlayerCharacterEntity c, ProgressionDefinition? progression) =>
        progression is null || c.Level >= progression.MaxLevel
            ? 0
            : progression.XpToNext[c.Level - 1];

    private XpGrantOutcome Refuse(int amount, string source)
    {
        logger.LogWarning(
            "XP grant of {Amount} from {Source} to player {PlayerId} refused: 1 to {Max} per grant.",
            amount,
            source,
            _playerId.Value,
            _config.MaxXpPerGrant
        );

        return XpGrantOutcome.Refused;
    }
}
