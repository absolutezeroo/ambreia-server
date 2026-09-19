using Orleans;

namespace Vortex.Primitives.Snapshots.FriendList;

[GenerateSerializer, Immutable]
public record RelationshipStatusEntrySnapshot
{
    [Id(0)]
    public required short RelationType { get; init; }

    [Id(1)]
    public required int Count { get; init; }

    [Id(2)]
    public required string Name { get; init; }

    [Id(3)]
    public required string Figure { get; init; }

    /// <summary>
    ///     Id of the friend whose <see cref="Name" /> and <see cref="Figure" /> represent the group.
    ///     The client reads it as <c>randomFriendId</c> and uses it to open that friend's profile.
    /// </summary>
    [Id(4)]
    public required int RandomFriendId { get; init; }
}
