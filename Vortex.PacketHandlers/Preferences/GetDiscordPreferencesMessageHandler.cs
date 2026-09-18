using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Protocol.Messages.Incoming.Preferences;

namespace Vortex.PacketHandlers.Preferences;

/// <summary>
/// Hands back the four Discord Rich Presence toggles the way the player left them.
/// </summary>
/// <remarks>
/// <para>
/// The server's whole part in Rich Presence is storage. The client decides what to publish to
/// Discord and does the publishing itself; it only needs the toggles back on every login, before it
/// will open its own settings dialog at all.
/// </para>
/// <para>
/// This is a request, not a push: the official server answers it rather than volunteering the
/// preferences after authentication (evidence/captures/login-official-001.json, GetDiscordPreferences
/// -> DiscordPreferences), which is what this handler does.
/// </para>
/// </remarks>
public class GetDiscordPreferencesMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<GetDiscordPreferencesMessage>
{
    public async ValueTask HandleAsync(
        GetDiscordPreferencesMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        PlayerAccountPreferencesSnapshot prefs = await grainFactory
            .GetPlayerGrain(ctx.PlayerId)
            .GetAccountPreferencesAsync(ct)
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(DiscordPreferences.Build(prefs), ct).ConfigureAwait(false);
    }
}
