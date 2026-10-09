using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Logging.Extensions;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Protocol.Messages.Outgoing.Rpg.Characters;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Characters.Grains;

/// <summary>What the player's HUD is told: the whole state, the stats after a change, the progress
/// after XP. A view only: a failure to send is logged and never undoes what changed.</summary>
internal sealed partial class PlayerCharacterGrain
{
    public async Task PushStateAsync()
    {
        if (await GetStatsAsync() is not { } stats || _character is not { } c)
        {
            return;
        }

        ProgressionDefinition? progression = content.Get<ProgressionDefinition>(ProgressionId);
        RpgCharacterStateMessageComposer state = new()
        {
            PlayerId = _playerId.Value,
            ClassId = c.ClassId,
            SpecializationId = c.SpecializationId,
            Level = c.Level,
            MaxLevel = progression?.MaxLevel ?? c.Level,
            Xp = c.Xp,
            XpToNext = XpToNext(c, progression),
            Stats = stats,
        };

        // The bag with the state (spec Objets §12): loaded by the stats read above.
        await PushAsync(
            _inventory is { } inventory ? [state, InventoryComposer(inventory)] : [state]
        );
    }

    /// <summary>The stats after a change already written; a failure to read them is logged.</summary>
    private async Task PushStatsAsync()
    {
        try
        {
            if (await GetStatsAsync() is { } stats)
            {
                await PushAsync(new RpgCharacterStatsMessageComposer { Stats = stats });
                TellTheZone(stats);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read the stats of player {PlayerId}", _playerId.Value);
        }
    }

    /// <summary>New stats reach the zone the player fights in (spec M1a §4.4), through his presence.
    /// Never awaited: the zone may be waiting on this very grain (an XP grant during its tick).</summary>
    private void TellTheZone(CharacterStatsSnapshot stats) =>
        grainFactory
            .GetPlayerPresenceGrain(_playerId)
            .OnCharacterStatsChangedAsync(stats, CancellationToken.None)
            .LogAndForget(
                logger,
                "Passing new stats of player {PlayerId} to its zone",
                _playerId.Value
            );

    /// <summary>Progress, after the new stats on a level up. The level is already written: reading
    /// the stats may fail here, and that must not fail the grant.</summary>
    private async Task PushProgressAsync(
        bool levelledUp,
        RpgCharacterProgressMessageComposer progress
    )
    {
        IComposer[] composers = [progress];

        try
        {
            if (levelledUp && await GetStatsAsync() is { } stats)
            {
                composers = [new RpgCharacterStatsMessageComposer { Stats = stats }, progress];
                TellTheZone(stats);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read the stats of player {PlayerId}", _playerId.Value);
        }

        await PushAsync(composers);
    }

    /// <summary>To the player's socket through its presence; the HUD is a view, so a failure is
    /// logged and never undoes what was granted.</summary>
    private async Task PushAsync(params IComposer[] composers)
    {
        try
        {
            await grainFactory.GetPlayerPresenceGrain(_playerId).SendComposerAsync(composers);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not push the state of player {PlayerId}", _playerId.Value);
        }
    }
}
