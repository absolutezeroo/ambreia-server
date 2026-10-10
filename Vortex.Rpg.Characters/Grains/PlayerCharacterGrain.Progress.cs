using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
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
            // This activation may never have read its bag (the grain went idle and came back while
            // its player stayed in the zone): the facts need it.
            await TellTheZoneFactsLoadingAsync();
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

    public async Task<XpGrantOutcome> GrantXpAsync(string grantKey, int amount, string source)
    {
        if (
            string.IsNullOrWhiteSpace(grantKey)
            || grantKey.Length > RpgXpGrantEntity.GrantKeyLength
        )
        {
            throw new ArgumentException(
                $"A grant key is 1 to {RpgXpGrantEntity.GrantKeyLength} characters.",
                nameof(grantKey)
            );
        }

        await RefreshIdsAsync();

        if (_character is not { } c)
        {
            return XpGrantOutcome.NoCharacter;
        }

        if (amount < 1 || amount > _config.MaxXpPerGrant)
        {
            return Refuse(amount, source);
        }

        await using (VortexDbContext db = await dbFactory.CreateDbContextAsync())
        {
            if (await db.RpgXpGrants.AnyAsync(g => g.GrantKey == grantKey))
            {
                return XpGrantOutcome.Duplicate;
            }

            DateTime now = Time.GetUtcNow().UtcDateTime;
            int givenToday = await db
                .RpgXpGrants.Where(g =>
                    g.PlayerEntityId == c.PlayerEntityId && g.CreatedAt >= now.Date
                )
                .SumAsync(g => g.Amount);

            if (givenToday + amount > _config.MaxXpPerDay)
            {
                logger.LogWarning(
                    "XP grant of {Amount} from {Source} to player {PlayerId} refused: {Given} given today, {Max} per day.",
                    amount,
                    source,
                    _playerId.Value,
                    givenToday,
                    _config.MaxXpPerDay
                );

                return XpGrantOutcome.DailyCapReached;
            }

            // The key before the XP: a replay that lands between the two finds it and gives nothing.
            db.RpgXpGrants.Add(
                new RpgXpGrantEntity
                {
                    GrantKey = grantKey,
                    PlayerEntityId = c.PlayerEntityId,
                    Amount = amount,
                    Source =
                        source.Length > RpgXpGrantEntity.SourceLength
                            ? source[..RpgXpGrantEntity.SourceLength]
                            : source,
                    CreatedAt = now,
                }
            );

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                // The unique index: the same key reached another activation first. Any other
                // failure (connection, timeout) propagates: the operator must not read "duplicate"
                // for a grant that was never recorded.
                db.ChangeTracker.Clear();

                if (!await db.RpgXpGrants.AnyAsync(g => g.GrantKey == grantKey))
                {
                    throw;
                }

                logger.LogInformation(
                    ex,
                    "XP grant key {GrantKey} for player {PlayerId} was taken meanwhile.",
                    grantKey,
                    _playerId.Value
                );

                return XpGrantOutcome.Duplicate;
            }
        }

        XpGrantOutcome outcome = await GainXpAsync(amount, source);

        if (outcome == XpGrantOutcome.Granted)
        {
            // An operator's grant is written at once, like a level: its key already is. At most
            // once: a process lost between the key and this write keeps the key and loses the XP,
            // and a write that fails here stays dirty for the timer.
            await FlushAsync(CancellationToken.None);
        }
        else
        {
            // Nothing was given (top level, a behaviour cancelled it): the key is handed back, so
            // the day's sum and a later use of the same key tell the truth.
            await using VortexDbContext db = await dbFactory.CreateDbContextAsync();
            await db.RpgXpGrants.Where(g => g.GrantKey == grantKey).ExecuteDeleteAsync();
        }

        return outcome;
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
