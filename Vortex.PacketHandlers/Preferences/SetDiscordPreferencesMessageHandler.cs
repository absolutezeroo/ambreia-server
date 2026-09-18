using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Preferences;
using Vortex.Protocol.Messages.Outgoing.Preferences;

namespace Vortex.PacketHandlers.Preferences;

/// <summary>
/// Saves the four Discord Rich Presence toggles.
/// </summary>
/// <remarks>
/// The reply is not an acknowledgement the client waits on — it already applied the change locally —
/// but sending it keeps the two sides agreed on the stored version. It echoes the message rather
/// than re-reading the grain, because the write above is the value being confirmed.
/// </remarks>
public class SetDiscordPreferencesMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<SetDiscordPreferencesMessage>
{
    public async ValueTask HandleAsync(
        SetDiscordPreferencesMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        await grainFactory
            .GetPlayerGrain(ctx.PlayerId)
            .SetDiscordPreferencesAsync(
                message.Version,
                message.ShowHabbo,
                message.ShareActivity,
                message.HideInHiddenRooms,
                message.AllowJoining,
                ct
            )
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(
                new DiscordPreferencesEventMessageComposer
                {
                    Version = message.Version,
                    ShowHabbo = message.ShowHabbo,
                    ShareActivity = message.ShareActivity,
                    HideInHiddenRooms = message.HideInHiddenRooms,
                    AllowJoining = message.AllowJoining,
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
