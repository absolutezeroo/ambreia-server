using Orleans;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Characters;

/// <summary>One character on the character screen (spec F3a §6.2).</summary>
[GenerateSerializer, Immutable]
public sealed record RpgCharacterListEntry
{
    [Id(0)]
    public required int PlayerId { get; init; }

    [Id(1)]
    public required string Name { get; init; }

    [Id(2)]
    public required string Figure { get; init; }

    [Id(3)]
    public required string Gender { get; init; }

    [Id(4)]
    public required string ClassId { get; init; }

    [Id(5)]
    public required string SpecializationId { get; init; }

    [Id(6)]
    public required int Level { get; init; }

    /// <summary>Still in the starter class: the screen offers « Choisir sa classe ».</summary>
    [Id(7)]
    public required bool Starter { get; init; }
}
