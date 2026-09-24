using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Moderation;
using Vortex.Primitives.Action;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Rooms.Snapshots.Wired;
using Vortex.Protocol.Messages.Incoming.Userdefinedroomevents.Wiredtrading.Contracts;
using Vortex.Protocol.Messages.Outgoing.Userdefinedroomevents.Wiredtrading.Contracts;

namespace Vortex.PacketHandlers.UserDefinedRoomEvents.Wiredtrading;

/// <summary>
/// The contract editor's save button.
/// </summary>
/// <remarks>
/// Answered with what was stored rather than with an acknowledgement, so the window redraws from
/// the server's reading of the save — a field the server declined to keep leaves the screen instead
/// of lingering there until the next open.
/// </remarks>
public class SaveWiredContractMessageHandler(IGrainFactory grainFactory, IWordFilter wordFilter)
    : IMessageHandler<SaveWiredContractMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IWordFilter _wordFilter = wordFilter;

    public async ValueTask HandleAsync(
        SaveWiredContractMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx is null || ctx.PlayerId <= 0 || ctx.RoomId <= 0)
        {
            return;
        }

        // The two texts the trade window shows the other party. A refusal answers like any refused
        // save (the editor stays open), and the bubble beside it gives the real reason — the
        // client's only fail text reads "Invalid or empty requirements", which would mislead alone.
        string? receiveText = await _wordFilter
            .PassAsync(ctx, message.Contract.ReceiveText, WordFilterSource.Wired, ct)
            .ConfigureAwait(false);
        string? rewardText = receiveText is null
            ? null
            : await _wordFilter
                .PassAsync(ctx, message.Contract.RewardText, WordFilterSource.Wired, ct)
                .ConfigureAwait(false);

        WiredContractSnapshot? saved =
            receiveText is null || rewardText is null
                ? null
                : await _grainFactory
                    .GetRoomWired(ctx.RoomId)
                    .SaveWiredContractAsync(
                        ActionContext.CreateForPlayer(ctx.PlayerId, ctx.RoomId),
                        message.Contract with
                        {
                            ReceiveText = receiveText,
                            RewardText = rewardText,
                        },
                        ct
                    )
                    .ConfigureAwait(false);

        if (saved is null)
        {
            // The editor stays open on a refusal and says why; "invalid_rules" is the one reason
            // the texts carry — "Invalid or empty requirements".
            await ctx.SendComposerAsync(
                    new WiredContractUpdateResultMessageComposer
                    {
                        ContractId = message.Contract.ContractId,
                        IsSuccess = false,
                        FailCode = InvalidRules,
                    },
                    ct
                )
                .ConfigureAwait(false);

            return;
        }

        await ctx.SendComposerAsync(
                new WiredContractContentsMessageComposer { Contract = saved },
                ct
            )
            .ConfigureAwait(false);

        // And this is what closes it. The contents reply redraws the window; only the result
        // dismisses it, so a save answered with contents alone leaves the editor open on a contract
        // that was already stored.
        await ctx.SendComposerAsync(
                new WiredContractUpdateResultMessageComposer
                {
                    ContractId = saved.ContractId,
                    IsSuccess = true,
                    FailCode = string.Empty,
                },
                ct
            )
            .ConfigureAwait(false);
    }

    /// <summary>The one refusal the client has a text for.</summary>
    private const string InvalidRules = "invalid_rules";
}
