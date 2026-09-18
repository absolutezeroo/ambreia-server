using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Nft;

/// <summary>
/// One purchasable chat style arriving or leaving, after the list has already been sent.
/// </summary>
/// <remarks>
/// The only incremental message in the chat-style family: <c>SessionDataManager</c> pushes or
/// removes the single id rather than replacing its vector, then raises
/// <c>SDTWE_PURCHASABLE_STYLES_UPDATED</c>, which is what redraws the selector without a reconnect.
/// The unlock band (1000..9999) has no equivalent, so it is refreshed by re-sending its whole list.
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record UserPurchasableChatStyleChangedMessageComposer : IComposer
{
    /// <summary>True when the player gained it; false takes it back out of their selector.</summary>
    [Id(0)]
    public required bool Added { get; init; }

    /// <summary>The id in the client's own style library.</summary>
    [Id(1)]
    public required int ChatStyleId { get; init; }
}
