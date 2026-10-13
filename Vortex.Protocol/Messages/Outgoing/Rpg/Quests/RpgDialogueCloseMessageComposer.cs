using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Quests;

/// <summary>The server ended the dialogue (spec M2a §3.2, 9034).</summary>
[GenerateSerializer, Immutable]
public sealed record RpgDialogueCloseMessageComposer : IComposer;
