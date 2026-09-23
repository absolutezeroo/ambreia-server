using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Players.Grains;
using Vortex.Protocol.Messages.Incoming.Avatar;
using Vortex.Protocol.Messages.Outgoing.Avatar;

namespace Vortex.PacketHandlers.Avatar;

/// <summary>
/// Claims a name for the player.
/// </summary>
/// <remarks>
/// Reached from two client paths on two ids — the onboarding dialog (879) and the paid rename
/// (1703), see <c>Headers.cs</c>. The rules are the same for both; what differs is only what the
/// client does with the answer.
///
/// The name is re-validated here rather than trusted from the preceding check: the check is
/// advisory, and the name can have been taken in between.
/// </remarks>
public class ChangeUserNameMessageHandler(
    IGrainFactory grainFactory,
    IConfiguration configuration,
    IWordFilter wordFilter
) : IMessageHandler<ChangeUserNameMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IConfiguration _configuration = configuration;
    private readonly IWordFilter _wordFilter = wordFilter;

    public async ValueTask HandleAsync(
        ChangeUserNameMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();

        if (ctx.PlayerId <= 0)
        {
            return;
        }

        int minLength = _configuration.GetValue("Vortex:Players:NameMinLength", 3);
        int maxLength = _configuration.GetValue("Vortex:Players:NameMaxLength", 15);
        int suggestionCount = _configuration.GetValue("Vortex:Players:NameSuggestionCount", 3);
        string name = message.Name ?? string.Empty;

        NameChangeResultCode result = NameChangePolicy.Validate(name, minLength, maxLength);
        ImmutableArray<string> suggestions = [];

        // Apply, not Test: this is the claim, so it journals. A Username refuses on any hit.
        if (
            result == NameChangeResultCode.Ok
            && _wordFilter.Apply(name, WordFilterSource.Username, ctx.PlayerId).Refused
        )
        {
            result = NameChangeResultCode.NameNotValid;
        }

        if (result == NameChangeResultCode.Ok)
        {
            PlayerId? owner = await _grainFactory
                .GetPlayerDirectoryGrain()
                .GetPlayerIdAsync(name, ct)
                .ConfigureAwait(false);

            if (owner is not null && owner.Value.Value != ctx.PlayerId)
            {
                result = NameChangeResultCode.NameInUse;
                suggestions =
                [
                    .. NameChangePolicy.BuildSuggestions(name, maxLength, suggestionCount),
                ];
            }
        }

        if (result == NameChangeResultCode.Ok)
        {
            IPlayerGrain player = _grainFactory.GetPlayerGrain(PlayerId.Parse(ctx.PlayerId));

            // The grain owns the write: it persists, refreshes the directory's forward/reverse
            // mappings, and pushes the updated summary to presence.
            await player.SetNameAsync(name, ct).ConfigureAwait(false);

            // Claiming a name is what ends the onboarding's first step; without this stamp the
            // client is handed the AVATAR_NAME_CHANGE action again on the next login.
            await player.MarkNuxCompletedAsync(ct).ConfigureAwait(false);
        }

        await ctx.SendComposerAsync(
                new ChangeUserNameResultMessageComposer
                {
                    ResultCode = (int)result,
                    Name = name,
                    NameSuggestions = suggestions,
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
