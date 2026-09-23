using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Protocol.Messages.Outgoing.Notifications;

namespace Vortex.PacketHandlers.Moderation;

/// <summary>
/// The hotel-wide word filter as a packet handler uses it: one text in, the text to use out — or
/// null when it was refused, in which case the player has already been told why.
/// </summary>
/// <remarks>
/// A handler with several fields passes them one at a time and stops at the first null, so a
/// refusal costs the player one bubble, not one per field.
/// </remarks>
internal static class WordFilterGate
{
    public static async ValueTask<string?> PassAsync(
        this IWordFilter filter,
        MessageContext ctx,
        string text,
        WordFilterSource source,
        CancellationToken ct
    )
    {
        WordFilterResult result = filter.Apply(text, source, ctx.PlayerId);

        if (!result.Refused)
        {
            return result.Text;
        }

        await ctx.SendComposerAsync(WordFilterNotice.Create(filter.RefusedMessage), ct)
            .ConfigureAwait(false);

        return null;
    }
}
