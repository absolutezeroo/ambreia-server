using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Action;

namespace Vortex.Rooms.Grains;

/// <summary>The zone's NPCs, as players reach them (spec M2a §3.3): the dialogue module does the work.</summary>
public sealed partial class RoomGrain
{
    public Task TalkToNpcAsync(ActionContext ctx, int objectId, CancellationToken ct)
    {
        // Actor guard: a real player with an avatar in this room.
        if (ctx.PlayerId <= 0 || !_state.AvatarsByPlayerId.ContainsKey(ctx.PlayerId))
        {
            return Task.CompletedTask;
        }

        return DialogueModule.TalkAsync(ctx.PlayerId, objectId, ct);
    }

    public Task ChooseDialogueAsync(ActionContext ctx, int choice, CancellationToken ct)
    {
        // Actor guard: a real player with an avatar in this room.
        if (ctx.PlayerId <= 0 || !_state.AvatarsByPlayerId.ContainsKey(ctx.PlayerId))
        {
            return Task.CompletedTask;
        }

        return DialogueModule.ChooseAsync(ctx.PlayerId, choice, ct);
    }
}
