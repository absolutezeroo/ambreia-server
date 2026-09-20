using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>
/// One task's progress.
/// </summary>
/// <param name="HighestPaidLevelIndex">The last rung already paid for, so re-running the level
/// walk cannot pay twice.</param>
/// <param name="CurrentStep">
/// Where the player stands in the task's sequence, zero-based. Always zero for a plain task, which
/// is a sequence of one and so is never mid-flight.
/// </param>
/// <param name="StepCount">
/// How many steps the task has today, so <paramref name="CurrentStep"/> reads as a position rather
/// than a number. Today's, not the one the player started under: content can be rewritten beneath
/// somebody mid-sequence, and the honest answer is where they stand in what exists now.
/// </param>
/// <param name="Captures">
/// What the steps they have satisfied recorded, decoded from the row's stored form.
/// </param>
/// <remarks>
/// The step and the captures are stored and, until now, shown nowhere. That made a player stuck
/// halfway through a four-step task indistinguishable from one who never started it — and "why is
/// this not advancing" the one question this screen could not answer. The captures are what a
/// later step's "from action 1" resolves against, so they are the difference between a task that
/// is waiting and one whose reference can never match.
/// </remarks>
public sealed record PlayerRewardTrackTaskRow(
    string TaskId,
    int ProgressCount,
    int HighestPaidLevelIndex,
    int CurrentStep,
    int StepCount,
    IReadOnlyList<PlayerRewardTrackCaptureRow> Captures
);
