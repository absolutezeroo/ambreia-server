using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Avatar;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Server.Grains;
using Vortex.Protocol.Messages.Incoming.Register;

namespace Vortex.PacketHandlers.Register;

/// <summary>
/// Saving a look.
/// </summary>
/// <remarks>
/// <para>
/// The avatar editor already refuses to offer a sellable set the player has not unlocked, but that
/// is the client judging itself against a list it holds. This is the untrusted edge, so the same
/// rule is applied here: a look wearing something the account does not own is refused whole rather
/// than quietly stripped, because a half-applied figure is worse than an unchanged one.
/// </para>
/// <para>
/// The check fails open by construction — a hotel that has not seeded the sellable list finds
/// nothing to refuse — and <c>clothing.enforce_ownership</c> turns it off outright. That switch
/// exists because this sits on the path every look change takes: if the seeded data and the hotel's
/// figuredata ever disagree, the symptom is players unable to change clothes at all, and waiting for
/// a rebuild to undo that would be the wrong trade.
/// </para>
/// <para>
/// This is also how a whole-avatar costume comes off. The editor sends this packet when the look was
/// its own rather than one of the NFT tab's avatars, and sends nothing else to say so — so a saved
/// look is the signal, and the worn avatar is dropped here. Nothing is restored: the figure the
/// player just chose <em>is</em> what they want to look like.
/// </para>
/// </remarks>
public class UpdateFigureDataMessageHandler(
    IGrainFactory grainFactory,
    ILogger<UpdateFigureDataMessageHandler> logger
) : IMessageHandler<UpdateFigureDataMessage>
{
    private const string EnforceOwnershipKey = "clothing.enforce_ownership";

    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly ILogger<UpdateFigureDataMessageHandler> _logger = logger;

    public async ValueTask HandleAsync(
        UpdateFigureDataMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId < 0)
        {
            return;
        }

        if (message.Figure.Length > FigureString.MaxLength)
        {
            // Refused here rather than in the persistence layer, where it surfaced as an unhandled
            // MySQL "data too long" three call stacks from the packet that caused it.
            _logger.LogWarning(
                "Player {PlayerId} sent a {Length}-character figure; the limit is {Max}. Look unchanged.",
                ctx.PlayerId,
                message.Figure.Length,
                FigureString.MaxLength
            );

            return;
        }

        if (!await MayWearAsync(message.Figure, ctx.PlayerId, ct).ConfigureAwait(false))
        {
            return;
        }

        IPlayerGrain player = _grainFactory.GetPlayerGrain(ctx.PlayerId);

        await player
            .SetFigureAsync(
                message.Figure,
                AvatarGenderTypeExtensions.FromLegacyString(message.Gender),
                ct
            )
            .ConfigureAwait(false);
    }

    private async Task<bool> MayWearAsync(string figure, int playerId, CancellationToken ct)
    {
        bool enforce = await _grainFactory
            .GetServerConfigGrain()
            .GetBoolAsync(EnforceOwnershipKey, true)
            .ConfigureAwait(false);

        if (!enforce)
        {
            return true;
        }

        ImmutableArray<int> unowned = await _grainFactory
            .GetPlayerClothingGrain(new PlayerId(playerId))
            .FindUnownedSellableAsync(FigureString.SetIdsOf(figure), ct)
            .ConfigureAwait(false);

        if (unowned.IsEmpty)
        {
            return true;
        }

        // Named, because the client shows nothing: the look simply does not change. Without this
        // line a refusal is indistinguishable from a packet that never arrived.
        _logger.LogWarning(
            "Player {PlayerId} tried to wear figure set(s) {Sets} they have not unlocked; look unchanged.",
            playerId,
            string.Join(", ", unowned)
        );

        return false;
    }
}
