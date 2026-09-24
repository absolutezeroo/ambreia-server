using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Protocol.Messages.Incoming.Users;
using Vortex.Protocol.Messages.Outgoing.Users;

namespace Vortex.PacketHandlers.Users;

public partial class ApproveNameMessageHandler(IWordFilter wordFilter)
    : IMessageHandler<ApproveNameMessage>
{
    private const int MinLength = 1;
    private const int MaxLength = 15;

    /// <summary>
    /// The client's own "forbidden words" answer: PetsCatalogWidget switches on <c>result - 1</c>
    /// and shows <c>catalog.alert.petname.bobba</c> for 4. Any other code above 3 falls into its
    /// default branch, which goes ahead with the purchase — so it is exactly 4.
    /// </summary>
    private const int ForbiddenWords = 4;

    public async ValueTask HandleAsync(
        ApproveNameMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        int result = Validate(message.Name);

        // Test, not Apply: this is the advisory check the widget runs before buying, and the
        // purchase itself is what journals (PurchaseFromCatalog's extra parameter).
        if (result == 0 && wordFilter.Test(message.Name).Hit)
        {
            result = ForbiddenWords;
        }

        await ctx.SendComposerAsync(
                new ApproveNameMessageComposer { Result = result, ValidationInfo = string.Empty },
                ct
            )
            .ConfigureAwait(false);
    }

    private static int Validate(string name)
    {
        if (name.Length > MaxLength)
        {
            return 1;
        }

        if (name.Length < MinLength)
        {
            return 2;
        }

        if (!ValidCharsRegex().IsMatch(name))
        {
            return 3;
        }

        return 0;
    }

    [GeneratedRegex(@"^[\p{L}\p{N} \-]+$")]
    private static partial Regex ValidCharsRegex();
}
