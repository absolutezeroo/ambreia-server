using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Rpg.Skills;

namespace Vortex.PacketHandlers.Rpg.Skills;

/// <summary>A skill cast (spec M1b §10.1); the player's room checks and answers.</summary>
public class RpgSkillCastMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<RpgSkillCastMessage>
{
    public async ValueTask HandleAsync(
        RpgSkillCastMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0)
        {
            return;
        }

        await grainFactory
            .GetRoomCombat(ctx.RoomId)
            .CastSkillAsync(
                ctx.AsActionContext(),
                message.SkillId,
                message.TargetObjectId,
                message.X,
                message.Y,
                ct
            )
            .ConfigureAwait(false);
    }
}
