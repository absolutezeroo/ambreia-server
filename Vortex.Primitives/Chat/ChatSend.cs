using System;
using Vortex.Primitives.Players;

namespace Vortex.Primitives.Chat;

/// <summary>A player's message on its way through the chat pipeline (spec F3c §4).</summary>
public sealed record ChatSend(
    PlayerId Sender,
    string SenderName,
    string Channel,
    string Target,
    string Text,
    DateTimeOffset Now
);
