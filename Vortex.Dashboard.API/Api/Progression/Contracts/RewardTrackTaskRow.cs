using System;
using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>
/// One task on a track.
/// </summary>
/// <param name="Wired">Whether anything raises this task's action today. A task on an unwired
/// action is configured, visible to players, and can never advance.</param>
/// <param name="Players">
/// How many players this task has ever advanced for. Zero on a published task is the one number
/// worth looking at: <paramref name="Wired"/> says the action is raised by something, and this says
/// whether the task — with its filters, its parameter and its mode — has ever actually been
/// satisfied by anybody.
/// </param>
/// <param name="LastMatchedAt">The most recent time it advanced for anyone, or null for never.</param>
/// <param name="Steps">Empty for a plain task: the engine builds its single step from the action
/// above, and the editor pre-fills the same way.</param>
public sealed record RewardTrackTaskRow(
    int Id,
    string TaskId,
    string LocalizationKey,
    string ActionCode,
    bool Wired,
    string Parameter,
    string Mode,
    bool Premium,
    int SortOrder,
    int Players,
    DateTime? LastMatchedAt,
    IReadOnlyList<RewardTrackStepRow> Steps,
    IReadOnlyList<RewardTrackLevelRow> Levels
);
