using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.Protocol.Messages.Incoming.Competition;
using Vortex.Protocol.Messages.Outgoing.Competition;

namespace Vortex.PacketHandlers.Competition;

/// <summary>
/// The client sends one of its own `landing.view.*` schedule strings and asks which campaign code is
/// active right now; the code it gets back selects the widget a landing-view slot renders
/// (`landing.view.&lt;code&gt;.widget`) and the campaign background set. An empty reply is the
/// client's "nothing scheduled" case and leaves the slot blank -- which is what every dynamic slot
/// did while this handler answered `string.Empty` unconditionally.
///
/// Official server behaviour is unknown; the schedule format is not. Every shipped value is `&lt;yyyy-MM-dd HH:mm&gt;,&lt;code&gt;`
/// pairs separated by `;`, in ascending date order, and `competition.timing` ends on a pair with an
/// empty code -- i.e. each entry means "from this moment, this code", and the empty tail switches the
/// campaign off. Arcturus is no help here: it ignores the dates, echoes back only the first entry as
/// the schedule string, and so never matches the client's own `_schedulingStr` for a multi-entry
/// schedule (WidgetContainerWidget.onTimingCode drops the reply).
/// </summary>
public class GetCurrentTimingCodeMessageHandler : IMessageHandler<GetCurrentTimingCodeMessage>
{
    public async ValueTask HandleAsync(
        GetCurrentTimingCodeMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        await ctx.SendComposerAsync(
                new CurrentTimingCodeMessageComposer
                {
                    SlotConfig = message.SlotConfig,
                    Code = ResolveCode(message.SlotConfig, DateTime.Now),
                },
                ct
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The code of the latest `&lt;date&gt;,&lt;code&gt;` entry whose date has passed, or empty when
    /// none has. Entries that do not parse are skipped rather than aborting the whole schedule.
    /// </summary>
    public static string ResolveCode(string slotConfig, DateTime now)
    {
        string code = string.Empty;
        DateTime latest = DateTime.MinValue;

        foreach (string entry in slotConfig.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int comma = entry.IndexOf(',', StringComparison.Ordinal);

            if (comma < 0)
            {
                continue;
            }

            if (
                !DateTime.TryParseExact(
                    entry[..comma].Trim(),
                    "yyyy-MM-dd HH:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime startsAt
                )
            )
            {
                continue;
            }

            if (startsAt > now || startsAt < latest)
            {
                continue;
            }

            latest = startsAt;
            code = entry[(comma + 1)..].Trim();
        }

        return code;
    }
}
