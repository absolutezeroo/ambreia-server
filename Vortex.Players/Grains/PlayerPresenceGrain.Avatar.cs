using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Rooms.Grains;
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
