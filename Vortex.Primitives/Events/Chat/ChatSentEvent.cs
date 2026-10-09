using Vortex.Primitives.Players;

namespace Vortex.Primitives.Events.Chat;

/// <summary>A chat line delivered (spec F3c §14.2): quest "say" objectives, statistics. Published
/// after the delivery; its failure is only logged.</summary>
public sealed record ChatSentEvent(PlayerId Sender, string Channel, string Target, string Text)
    : IEvent;
