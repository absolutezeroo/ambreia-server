using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Fishing;
using Vortex.Primitives.Fishing;
using Vortex.Primitives.Fishing.Grains;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Outgoing.Fishing;

namespace Vortex.Fishing.Grains;

/// <summary>
/// The hotel's fishing definitions, cached for the lifetime of the kept-alive singleton.
/// </summary>
/// <remarks>
/// <para>
/// Cached once and held: these change when an operator
/// edits them, not while anybody is playing. What differs is that an edit has to reach players
/// <em>mid-session</em> — so <see cref="ReloadAsync"/> is a real operation rather than a restart,
/// and the version it bumps is what lets a client tell a real change from a redundant push.
/// </para>
/// <para>
/// Reconstructed from Habbo Origins, which has no client dump. Every number in these tables is an
/// operator-editable guess; see the client's <c>docs/vortex-original/fishing.md</c>.
/// </para>
/// </remarks>
[KeepAlive]
internal sealed class FishingDefinitionsGrain(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IGrainFactory grainFactory,
    ISessionGateway sessions,
    ILogger<FishingDefinitionsGrain> logger
) : Grain, IFishingDefinitionsGrain
{
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly ISessionGateway _sessions = sessions;
    private readonly ILogger<FishingDefinitionsGrain> _logger = logger;

    private FishingDefinitionsSnapshot? _definitions;

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await LoadAsync(ct).ConfigureAwait(true);
        await base.OnActivateAsync(ct).ConfigureAwait(true);
    }

    public async Task<FishingDefinitionsSnapshot> GetDefinitionsAsync(CancellationToken ct)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(true);

        return _definitions!;
    }

    public async Task<int> ReloadAsync(CancellationToken ct)
    {
        await LoadAsync(ct).ConfigureAwait(true);

        VortexFishingDefinitionsMessageComposer composer = BuildComposer(_definitions!);
        int reached = 0;

        // The grain owns its own outbound communication (AGENTS.md, "Grains orchestrate their own
        // outbound communication"), so the push belongs here rather than in whatever triggered the
        // reload. Without it an edit would reach a client only on its next login, which is exactly
        // the behaviour this whole design exists to avoid.
        foreach (PlayerId playerId in _sessions.GetOnlinePlayerIds())
        {
            try
            {
                await _grainFactory
                    .GetPlayerPresenceGrain(playerId)
                    .SendComposerAsync(composer)
                    .ConfigureAwait(true);

                reached++;
            }
            catch (Exception ex)
            {
                // One player disconnecting mid-broadcast must not cost the rest their update, so the
                // loop absorbs a failed send rather than abandoning the sweep.
                _logger.LogDebug(
                    ex,
                    "Could not push fishing definitions to player {PlayerId}",
                    playerId
                );
            }
        }

        _logger.LogInformation(
            "Fishing definitions reloaded to version {Version}: {Species} species, {Zones} zones, pushed to {Reached} sessions",
            _definitions!.Version,
            _definitions.Species.Length,
            _definitions.Zones.Length,
            reached
        );

        return _definitions.Version;
    }

    public async Task<FishingSettingsSnapshot> GetSettingsAsync(CancellationToken ct)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(true);

        return _definitions!.Settings;
    }

    /// <summary>
    /// Shared by the reload broadcast and by the login push, so a player joining mid-session and a
    /// player already standing at a pond receive byte-identical tables.
    /// </summary>
    public static VortexFishingDefinitionsMessageComposer BuildComposer(
        FishingDefinitionsSnapshot definitions
    ) =>
        new()
        {
            Version = definitions.Version,
            Species = definitions.Species,
            RodLevels = definitions.RodTiers,
            FishingLevels = definitions.Levels,
            Zones = definitions.Zones,
        };

    public async Task<FishingZoneSnapshot?> GetZoneForFurniClassAsync(
        string furniClass,
        CancellationToken ct
    )
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(true);

        // Answering null is the ordinary case — most furniture is not a pond — so this is a lookup,
        // not a failure path.
        foreach (FishingZoneSnapshot zone in _definitions!.Zones)
        {
            if (string.Equals(zone.FurniClass, furniClass, StringComparison.Ordinal))
            {
                return zone;
            }
        }

        return null;
    }

    public async Task<ImmutableArray<FishSpeciesSnapshot>> GetSpeciesForZoneAsync(
        int zoneId,
        CancellationToken ct
    )
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(true);

        return [.. _definitions!.Species.Where(species => species.ZoneId == zoneId)];
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_definitions is not null)
        {
            return;
        }

        await LoadAsync(ct).ConfigureAwait(true);
    }

    /// <summary>
    /// Reads all four tables in one context.
    /// </summary>
    /// <remarks>
    /// The version is the previous one plus one rather than a timestamp or a row count: it only has
    /// to be monotonic for the client's "is this newer" test, and a row count would not change when
    /// a value is edited in place — which is the most common edit an operator makes.
    /// </remarks>
    private async Task LoadAsync(CancellationToken ct)
    {
        int nextVersion = (_definitions?.Version ?? 0) + 1;

        // The tunables are NOT table rows. Admin-editable gameplay config belongs to
        // IServerConfigGrain, whose writes are write-through — an operator's edit is live on the next
        // read, with no reload and no restart. Resolved before the content and outside the try, so a
        // failing table read still leaves the knobs correct.
        FishingSettingsSnapshot settings = await FishingConfig
            .ResolveAsync(_grainFactory.GetServerConfigGrain())
            .ConfigureAwait(true);

        try
        {
            await using VortexDbContext dbCtx = await _dbCtxFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            FishingSpeciesEntity[] species = await dbCtx
                .FishingSpecies.AsNoTracking()
                .ToArrayAsync(ct)
                .ConfigureAwait(true);

            FishingZoneEntity[] zones = await dbCtx
                .FishingZones.AsNoTracking()
                .ToArrayAsync(ct)
                .ConfigureAwait(true);

            FishingRodTierEntity[] rodTiers = await dbCtx
                .FishingRodTiers.AsNoTracking()
                .OrderBy(tier => tier.XpThreshold)
                .ToArrayAsync(ct)
                .ConfigureAwait(true);

            FishingLevelEntity[] levels = await dbCtx
                .FishingLevels.AsNoTracking()
                .OrderBy(level => level.XpThreshold)
                .ToArrayAsync(ct)
                .ConfigureAwait(true);

            _definitions = new FishingDefinitionsSnapshot
            {
                Version = nextVersion,
                Species = [.. species.Select(ToSnapshot)],
                Zones = [.. zones.Select(ToSnapshot)],
                RodTiers = [.. rodTiers.Select(ToSnapshot)],
                Levels = [.. levels.Select(ToSnapshot)],
                Settings = settings,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load fishing definitions");

            // An empty table set is a hotel where nothing bites: visibly wrong, and recoverable by a
            // reload. Leaving the field null would instead make every caller retry the failing query
            // on every request.
            _definitions ??= new FishingDefinitionsSnapshot
            {
                Version = nextVersion,
                Species = [],
                Zones = [],
                RodTiers = [],
                Levels = [],
                Settings = settings,
            };
        }
    }

    private static FishSpeciesSnapshot ToSnapshot(FishingSpeciesEntity entity) =>
        new()
        {
            Id = entity.Id,
            NameKey = entity.NameKey,
            ZoneId = entity.ZoneId,
            RequiredLevel = entity.RequiredLevel,
            RarityStars = entity.RarityStars,
            CatchRate = entity.CatchRate,
            RarityWeight = entity.RarityWeight,
            MinWeight = entity.MinWeight,
            MaxWeight = entity.MaxWeight,
            XpReward = entity.XpReward,
            GoldenXpBonus = entity.GoldenXpBonus,
            CurrencyReward = entity.CurrencyReward,
            ActiveHours = entity.ActiveHours,
            ActiveWeekdays = entity.ActiveWeekdays,
            ActiveSeasons = entity.ActiveSeasons,
        };

    private static FishingZoneSnapshot ToSnapshot(FishingZoneEntity entity) =>
        new()
        {
            Id = entity.Id,
            NameKey = entity.NameKey,
            FurniClass = entity.FurniClass,
            RequiredLevel = entity.RequiredLevel,
            MinCatches = entity.MinCatches,
            MaxCatches = entity.MaxCatches,
        };

    private static FishingRodLevelSnapshot ToSnapshot(FishingRodTierEntity entity) =>
        new()
        {
            Quality = entity.Quality,
            XpThreshold = entity.XpThreshold,
            NameKey = entity.NameKey,
            HandItemId = entity.HandItemId,
            CatchMultiplier = entity.CatchMultiplier,
            GoldenMultiplier = entity.GoldenMultiplier,
            HookHavocChance = entity.HookHavocChance,
        };

    private static FishingLevelSnapshot ToSnapshot(FishingLevelEntity entity) =>
        new() { Level = entity.Level, XpThreshold = entity.XpThreshold };
}
