using System;
using System.Linq;
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

    /// <summary>
    /// A catalogue purchase's extra parameter, whose first line is the name a player gives what
    /// they buy — a pet (<c>PetFulfillmentStrategy</c>), or a bot whose product names none
    /// (<c>BotProductReader</c>). The race and colour lines after it are left alone, and so is a
    /// first line of digits only: that is an id (a group's for guild furni, a poster's number), and
    /// the leet folding would read "455" as "ass".
    /// </summary>
    /// <remarks>
    /// Filtered as <see cref="WordFilterSource.PetName"/>, strict, because in both cases it is a name.
    /// <c>ponytail:</c> the handler does not know the product type, so an all-digit pet name goes
    /// unfiltered; the purchase grain is where the type is known if that ever matters.
    /// </remarks>
    public static async ValueTask<string?> PassPurchaseExtraAsync(
        this IWordFilter filter,
        MessageContext ctx,
        string? extraParam,
        CancellationToken ct
    )
    {
        string extra = extraParam ?? string.Empty;
        int end = extra.IndexOf('\n', StringComparison.Ordinal);
        string name = end < 0 ? extra : extra[..end];

        if (name.Trim().Length == 0 || name.Trim().All(char.IsAsciiDigit))
        {
            return extra;
        }

        string? passed = await filter
            .PassAsync(ctx, name, WordFilterSource.PetName, ct)
            .ConfigureAwait(false);

        return passed is null ? null
            : end < 0 ? passed
            : passed + extra[end..];
    }
}
