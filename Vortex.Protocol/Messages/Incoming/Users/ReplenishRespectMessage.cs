using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Users;

/// <summary>
/// "Refill my respects" (header 426), sent by the infostand's <c>replenish_respect</c> button after
/// its confirmation dialog. No body: the client already knows which counters it is spending.
/// </summary>
/// <remarks>
/// The button is only drawn while <c>respectLeft</c> is exhausted and <c>respectReplenishesLeft</c>
/// is above zero, both of which come from the user object — so nothing reaches here unless the
/// server said there was an allowance in the first place. It re-checks anyway.
/// </remarks>
public record ReplenishRespectMessage : IMessageEvent;
