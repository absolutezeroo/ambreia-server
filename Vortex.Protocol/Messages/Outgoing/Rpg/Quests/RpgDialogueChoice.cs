using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Quests;

/// <summary>A choice: its text key and kind (next, accept, complete, close).</summary>
[GenerateSerializer, Immutable]
public sealed record RpgDialogueChoice
{
    [Id(0)]
    public required string Text { get; init; }

    [Id(1)]
    public required string Kind { get; init; }
}
