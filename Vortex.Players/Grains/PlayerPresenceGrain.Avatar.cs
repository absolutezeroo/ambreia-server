using System.Threading;
using System.Threading.Tasks;
using Vortex.Logging.Extensions;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rpg.Characters;
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

    public Task OnCharacterStatsChangedAsync(CharacterStatsSnapshot stats, CancellationToken ct)
    {
        // Not awaited: the room may be in a tick that is itself waiting on this character.
        if (_state.ActiveRoomId > 0)
        {
            _grainFactory
                .GetRoomCombat(_state.ActiveRoomId)
                .UpdateCombatantStatsAsync(PlayerId, stats, ct)
                .LogAndForget(
                    _logger,
                    "Passing new stats of player {PlayerId} to its room",
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
