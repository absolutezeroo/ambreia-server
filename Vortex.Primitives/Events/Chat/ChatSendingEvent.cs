using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events.Chat;

/// <summary>
/// A chat message about to be routed (spec F3c §14.2): cancellable, and its <see cref="Text"/> may
/// be rewritten — by an event behaviour, which runs in a chain; handlers run in parallel and must not
/// write it. The cancel reason, if any, is the refusal code the sender gets.
/// </summary>
public sealed record ChatSendingEvent(PlayerId Sender, string Channel, string Target) : IEvent
{
    public required string Text { get; set; }
}
