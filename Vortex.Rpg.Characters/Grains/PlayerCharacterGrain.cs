using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Rpg;
using Vortex.Events.Registry;
using Vortex.Logging.Extensions;
using Vortex.Primitives.Events;
using Vortex.Primitives.Events.Rpg;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.Rpg.Characters.Grains;
using Vortex.Primitives.World;
using Vortex.Rpg.Characters.Configuration;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Stats;

namespace Vortex.Rpg.Characters.Grains;

/// <summary>
/// The RPG state of one avatar (spec Personnage §7): hydrated from <c>player_characters</c> on
/// activation, progress written on a timer and on deactivation. Class ids are never written back
/// from memory: an import's replace rule owns that column once the row exists.
/// </summary>
internal sealed class PlayerCharacterGrain(
    IDbContextFactory<VortexDbContext> dbFactory,
    IRpgContent content,
    IEnumerable<IStatSource> sources,
    IEventPublisher events,
    ICancellableEventPublisher cancellableEvents,
    IOptions<CharacterConfig> config,
    ILogger<PlayerCharacterGrain> logger
) : Grain, IPlayerCharacterGrain
{
    internal const string ProgressionId = "progression.default";

    private readonly IStatSource[] _sources = [.. sources];
    private readonly CharacterConfig _config = config.Value;

    private PlayerId _playerId;
    private PlayerCharacterEntity? _character;
    private bool _dirty;
    private CharacterStatsSnapshot? _stats;
    private long _statsVersion = -1;

    /// <summary>The content version the class ids were last read at.</summary>
    private long _idsVersion;
    private IDisposable? _flushTimer;

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        _playerId = PlayerId.Parse((int)this.GetPrimaryKeyLong());

        _idsVersion = content.Version;

        await using VortexDbContext db = await dbFactory.CreateDbContextAsync(ct);
        _character = await db
            .PlayerCharacters.AsNoTracking()
            .SingleOrDefaultAsync(c => c.PlayerEntityId == _playerId.Value, ct);

        _flushTimer = this.RegisterGrainTimer<object?>(
            static async (self, ct) => await ((PlayerCharacterGrain)self!).FlushAsync(ct),
            this,
            TimeSpan.FromMilliseconds(_config.FlushIntervalMs),
            TimeSpan.FromMilliseconds(_config.FlushIntervalMs)
        );
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
    {
        _flushTimer?.Dispose();

        await FlushAsync(ct);
    }

    public async Task<CharacterSummary?> GetSummaryAsync()
    {
        await RefreshIdsAsync();

        return _character is { } c
            ? new CharacterSummary
            {
                ClassId = c.ClassId,
                SpecializationId = c.SpecializationId,
                Level = c.Level,
                Xp = c.Xp,
            }
            : null;
    }

    /// <summary>
    /// An import may have moved this character to another class (replace rule, written to the row
    /// while this grain was active): when the content version changed, the ids are read again.
    /// The flush never writes them, so the row's value is the truth.
    /// </summary>
    private async Task RefreshIdsAsync()
    {
        long version = content.Version;

        if (_character is not { } c || version == _idsVersion)
        {
            return;
        }

        await using VortexDbContext db = await dbFactory.CreateDbContextAsync();
        var ids = await db
            .PlayerCharacters.AsNoTracking()
            .Where(x => x.PlayerEntityId == c.PlayerEntityId)
            .Select(x => new { x.ClassId, x.SpecializationId })
            .SingleOrDefaultAsync();

        if (ids is not null)
        {
            c.ClassId = ids.ClassId;
            c.SpecializationId = ids.SpecializationId;
        }

        _idsVersion = version;
        _stats = null;
    }

    public async Task<ChooseClassOutcome> ChooseClassAsync(string classId)
    {
        if (_character is not null)
        {
            return ChooseClassOutcome.AlreadyChosen;
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

    public async Task<CharacterStatsSnapshot?> GetStatsAsync()
    {
        await RefreshIdsAsync();

        if (_character is null)
        {
            return null;
        }

        if (_stats is null || _statsVersion != content.Version)
        {
            await ComputeStatsAsync(_character);
        }

        return _stats;
    }

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
            await FlushAsync(CancellationToken.None);

            for (int level = oldLevel + 1; level <= c.Level; level++)
            {
                Publish(new LevelUpEvent(_playerId, c.ClassId, level - 1, level));
            }

            Publish(new CharacterStatsChangedEvent(_playerId));
        }

        Publish(new XpGainedEvent(_playerId, gaining.Amount, source, c.Level, c.Xp));

        return XpGrantOutcome.Granted;
    }

    public Task SetLastRoomAsync(RoomId roomId)
    {
        // A dungeon copy is not somewhere to come back to (spec Personnage §9).
        if (
            _character is { } c
            && roomId.Value > 0
            && !ZoneInstanceIds.IsInstance(roomId)
            && c.LastRoomId != roomId.Value
        )
        {
            c.LastRoomId = roomId.Value;
            _dirty = true;
        }

        return Task.CompletedTask;
    }

    public Task<RoomId?> GetLastRoomAsync() =>
        Task.FromResult(
            _character?.LastRoomId is int room && room > 0 ? (RoomId?)new RoomId(room) : null
        );

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

    /// <summary>The "-ed" events, off the critical path (spec Personnage §11).</summary>
    private void Publish(IEvent e) =>
        events.PublishAsync(e).LogAndForget(logger, "Publishing {Event}", e.GetType().Name);

    private async Task ComputeStatsAsync(PlayerCharacterEntity character)
    {
        long version = content.Version;
        ClassDefinition? cls = content.Get<ClassDefinition>(character.ClassId);

        if (cls is null)
        {
            // Readable rather than blocking the player (spec Personnage §12): no class stats.
            logger.LogWarning(
                "Character {PlayerId} has class {ClassId}, unknown to the content: its class stats are zero.",
                _playerId.Value,
                character.ClassId
            );
        }

        StatSourceContext context = new(_playerId, cls, character.Level);
        List<(string, StatBlock)> contributions = new(_sources.Length);

        foreach (IStatSource source in _sources)
        {
            contributions.Add(
                (source.Name, await source.ContributeAsync(context, CancellationToken.None))
            );
        }

        _stats = StatCalculator.Compute(
            contributions,
            content.Get<ProgressionDefinition>(ProgressionId),
            cls?.PrimaryStat
        );
        _statsVersion = version;
    }

    /// <summary>Writes the progress (level, XP, last zone) if it changed; a failure stays dirty for
    /// the next tick.</summary>
    private async Task FlushAsync(CancellationToken ct)
    {
        if (!_dirty || _character is not { } c)
        {
            return;
        }

        try
        {
            await using VortexDbContext db = await dbFactory.CreateDbContextAsync(ct);
            await db
                .PlayerCharacters.Where(x => x.PlayerEntityId == c.PlayerEntityId)
                .ExecuteUpdateAsync(
                    up =>
                        up.SetProperty(x => x.Level, c.Level)
                            .SetProperty(x => x.Xp, c.Xp)
                            .SetProperty(x => x.LastRoomId, c.LastRoomId),
                    ct
                );
            _dirty = false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Writing character {PlayerId} failed; it stays dirty for the next flush.",
                _playerId.Value
            );
        }
    }
}
