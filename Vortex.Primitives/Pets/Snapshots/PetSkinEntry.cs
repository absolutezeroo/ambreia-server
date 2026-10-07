namespace Vortex.Primitives.Pets.Snapshots;

public sealed record PetSkinEntry
{
    public required int PetType { get; init; }
    public required int SkinIndex { get; init; }
    public required int Color { get; init; }
    public required bool Sellable { get; init; }
    public required bool Rare { get; init; }
}
