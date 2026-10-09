using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Vortex.Primitives.Observability;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.Rpg.Characters.Grains;
using Vortex.Rpg.Characters.Configuration;
using Vortex.Rpg.Characters.Grains.Persistence;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Stats;

namespace Vortex.Rpg.Characters.Grains;

/// <summary>
/// The RPG state of one avatar (spec Personnage §7): hydrated from <c>player_characters</c> on
/// activation, progress written on a timer and on deactivation. Class ids are never written back
/// from memory: an import's replace rule owns that column once the row exists. Its parts: the class
/// (<c>PlayerCharacterGrain.Class.cs</c>), XP and levels (<c>.Progress.cs</c>), the HUD
/// (<c>.Hud.cs</c>), the last zone (<c>.Rooms.cs</c>) and the items (<c>.Items.cs</c>).
/// </summary>
internal sealed partial class PlayerCharacterGrain(
    IDbContextFactory<VortexDbContext> dbFactory,
    IRpgContent content,
    IEnumerable<IStatSource> sources,
    IEventPublisher events,
    ICancellableEventPublisher cancellableEvents,
    IOptions<CharacterConfig> config,
    IVortexMetrics metrics,
    ILogger<PlayerCharacterGrain> logger,
    IGrainFactory grainFactory,
    IOptions<ItemConfig> itemConfig
) : Grain, IPlayerCharacterGrain
{
    internal const string ProgressionId = "progression.default";

    private readonly IStatSource[] _sources = [.. sources];
    private readonly CharacterConfig _config = config.Value;
    private readonly ItemConfig _itemConfig = itemConfig.Value;
    private readonly CharacterItemStore _itemStore = new(dbFactory);

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
        metrics.CharacterActivated(1);

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
        _itemFlushTimer = this.RegisterGrainTimer<object?>(
            static async (self, ct) => await ((PlayerCharacterGrain)self!).FlushItemsAsync(ct),
            this,
            TimeSpan.FromMilliseconds(_itemConfig.FlushIntervalMs),
            TimeSpan.FromMilliseconds(_itemConfig.FlushIntervalMs)
        );
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
    {
        _flushTimer?.Dispose();
        _itemFlushTimer?.Dispose();
        metrics.CharacterActivated(-1);

        await FlushAsync(ct);
        await FlushItemsAsync(ct);
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

    public async Task<CharacterStatsSnapshot?> GetStatsAsync()
    {
        // The equipment is part of the stats: loaded (and its ids re-read) before they are computed.
        await InventoryAsync();

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

        StatSourceContext context = new(_playerId, cls, character.Level)
        {
            Equipment = _inventory?.EquippedStats() ?? [],
        };
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

        long started = Stopwatch.GetTimestamp();

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
            metrics.CharacterFlushCompleted(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                false
            );
        }
        catch (Exception ex)
        {
            metrics.CharacterFlushCompleted(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                true
            );
            logger.LogWarning(
                ex,
                "Writing character {PlayerId} failed; it stays dirty for the next flush.",
                _playerId.Value
            );
        }
    }
}
