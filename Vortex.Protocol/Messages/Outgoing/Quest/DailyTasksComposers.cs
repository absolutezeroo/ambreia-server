using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Quests;
using Vortex.Primitives.Quests.Snapshots;

namespace Vortex.Protocol.Messages.Outgoing.Quest;

/// <summary>
/// The player's whole daily-task board (header 1824). The client clears its list before applying
/// this, so it must carry everything currently assigned, not a delta.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record DailyTasksActiveListMessageComposer : IComposer
{
    [Id(0)]
    public required ImmutableArray<DailyTaskSnapshot> Tasks { get; init; }
}

/// <summary>
/// One task's progress changed (header 1065). Carries only the four fields the client patches in
/// place; anything else it already has stays untouched.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record DailyTasksTaskUpdateMessageComposer : IComposer
{
    [Id(0)]
    public required long TaskId { get; init; }

    [Id(1)]
    public required int Repeats { get; init; }

    [Id(2)]
    public required DailyTaskStatus Status { get; init; }

    [Id(3)]
    public required int SecondsLeft { get; init; }
}
