using System.Collections.Generic;
using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Nft;

/// <summary>
/// The chat-bubble styles the player has bought, as opposed to the ones they unlocked.
/// </summary>
/// <remarks>
/// The client never asks for this one — there is no request header for it, only the answer — so it
/// is sent unprompted, alongside the reply to <c>GetUserNftChatStyles</c>, which the client does
/// send and sends at session init.
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record UserPurchasableChatStylesMessageComposer : IComposer
{
    [Id(0)]
    public required List<int> ChatStyleIds { get; init; }
}
