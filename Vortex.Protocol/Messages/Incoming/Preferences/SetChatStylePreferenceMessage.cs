using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Preferences;

public record SetChatStylePreferenceMessage : IMessageEvent
{
    // HabboFreeFlowChat::set preferedChatStyle() sends (chatStyle, fontSizeMode) — header 2634.
    // The chat style is read and ignored by the parser (figé, retiré avec le client).
    public required int FontSizeMode { get; init; }
}
