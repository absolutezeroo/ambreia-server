using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Quests;

/// <summary>The dialogue node to show (spec M2a §3.2, 9033): content text keys.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgDialogueMessageComposer : IComposer
{
    [Id(0)]
    public required int NpcObjectId { get; init; }

    [Id(1)]
    public required string Speaker { get; init; }

    [Id(2)]
    public required string Text { get; init; }

    [Id(3)]
    public required ImmutableArray<RpgDialogueChoice> Choices { get; init; }
}
