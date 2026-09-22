using Orleans;

namespace Vortex.Primitives.Orleans.Snapshots.Navigator;

/// <summary>
/// One configured promoted-rooms group: its client code and which search fills it. The rooms and
/// the leader avatar are resolved per request, so nothing here goes stale.
/// </summary>
[GenerateSerializer, Immutable]
public record NavigatorPromotedCategorySnapshot
{
    [Id(0)]
    public required int Id { get; init; }

    [Id(1)]
    public required string Code { get; init; }

    [Id(2)]
    public required string SearchCode { get; init; }

    [Id(3)]
    public required string Filter { get; init; }
}
