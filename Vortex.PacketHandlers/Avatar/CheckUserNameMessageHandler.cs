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
using Vortex.Protocol.Messages.Incoming.Avatar;
using Vortex.Protocol.Messages.Outgoing.Avatar;

namespace Vortex.PacketHandlers.Avatar;

/// <summary>
/// Answers the "is this name free?" probe the onboarding dialog sends while the user types.
/// </summary>
/// <remarks>
/// The name is echoed back because the client drops answers that no longer match what is typed —
/// see <see cref="CheckUserNameResultMessageComposer"/>. An unanswered probe leaves the dialog
/// spinning its wait indicator forever, so every path here has to send exactly one result.
/// </remarks>
public class CheckUserNameMessageHandler(
    IGrainFactory grainFactory,
    IConfiguration configuration,
    IWordFilter wordFilter
) : IMessageHandler<CheckUserNameMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IConfiguration _configuration = configuration;
    private readonly IWordFilter _wordFilter = wordFilter;

    public async ValueTask HandleAsync(
        CheckUserNameMessage message,
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

        // Test, not Apply: this probe fires on every pause in typing, and journaling each one would
        // bury the journal. The claim (ChangeUserName) is what journals. Any hit counts for a name.
        if (result == NameChangeResultCode.Ok && _wordFilter.Test(name).Hit)
        {
            result = NameChangeResultCode.NameNotValid;
        }

        if (result == NameChangeResultCode.Ok)
        {
            PlayerId? owner = await _grainFactory
                .GetPlayerDirectoryGrain()
                .GetPlayerIdAsync(name, ct)
                .ConfigureAwait(false);

            // The player's own name is not "in use" against themselves — re-checking the name you
            // already hold has to come back OK, or the dialog can never be submitted again.
            if (owner is not null && owner.Value.Value != ctx.PlayerId)
            {
                result = NameChangeResultCode.NameInUse;
                suggestions =
                [
                    .. NameChangePolicy.BuildSuggestions(name, maxLength, suggestionCount),
                ];
            }
        }

        await ctx.SendComposerAsync(
                new CheckUserNameResultMessageComposer
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
