using System.Threading;
using System.Threading.Tasks;
using Vortex.Logging.Extensions;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.World.Rules;
using Vortex.Protocol.Messages.Outgoing.Avatar;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;

namespace Vortex.Players.Grains;

internal sealed partial class PlayerPresenceGrain
{
    public async Task OnPlayerUpdatedAsync(PlayerSummarySnapshot snapshot, CancellationToken ct)
    {
        _chatName = snapshot.Name;

        if (_state.ActiveRoomId > 0)
        {
            await SendComposerAsync(
                new UserChangeMessageComposer
                {
                    ObjectId = -1,
                    Figure = snapshot.Figure,
                    Gender = snapshot.Gender,
                    CustomInfo = snapshot.Motto,
                    AchievementScore = snapshot.AchievementScore,
                }
            );

            IRoomAvatars room = _grainFactory.GetRoomAvatars(_state.ActiveRoomId);

            await room.UpdateAvatarWithPlayerAsync(snapshot, ct);
        }
    }

    public Task OnCharacterStatsChangedAsync(CharacterCombatProfile profile, CancellationToken ct)
    {
        // Not awaited: the room may be in a tick that is itself waiting on this character.
        if (_state.ActiveRoomId > 0)
        {
            _grainFactory
                .GetRoomCombat(_state.ActiveRoomId)
                .UpdateCombatantStatsAsync(PlayerId, profile, ct)
                .LogAndForget(
                    _logger,
                    "Passing new stats of player {PlayerId} to its room",
                    PlayerId.Value
                );
        }

        return Task.CompletedTask;
    }

    public Task OnZoneFactsChangedAsync(ZoneFacts facts, CancellationToken ct)
    {
        // Not awaited, as the stats: the room may be in a tick waiting on this character.
        if (_state.ActiveRoomId > 0)
        {
            _grainFactory
                .GetRoomZone(_state.ActiveRoomId)
                .UpdatePlayerFactsAsync(PlayerId, facts)
                .LogAndForget(
                    _logger,
                    "Passing the zone facts of player {PlayerId} to its room",
                    PlayerId.Value
                );
        }

        return Task.CompletedTask;
    }

    public async Task OnFigureUpdatedAsync(PlayerSummarySnapshot snapshot, CancellationToken ct)
    {
        await SendComposerAsync(
            new FigureUpdateEventMessageComposer
            {
                Figure = snapshot.Figure,
                Gender = snapshot.Gender,
            }
        );

        await OnPlayerUpdatedAsync(snapshot, ct);
    }
}
