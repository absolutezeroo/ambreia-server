using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.RewardTracks;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.RewardTracks;
using Vortex.Primitives.RewardTracks.Snapshots;

namespace Vortex.RewardTracks;

/// <summary>
/// Every reward-track definition, held in memory, plus the action index the event bridge asks
/// before it does anything else.
/// </summary>
/// <remarks>
/// <para>
/// The index is what makes this design scale past one campaign. Room entries, chat lines and
/// furniture placements arrive constantly; without it, each one would reach a grain and scan every
/// task of every published track to find out it was of no interest.
/// <see cref="IsActionInteresting"/> answers from a hash set on the calling thread, so an action
/// no content mentions costs a lookup and stops there.
/// </para>
/// <para>
/// Loaded with the other reference caches at startup and reloaded by the admin service after a
/// content write. Never mutated by gameplay.
/// </para>
/// </remarks>
internal sealed class RewardTrackCatalog(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    ILogger<RewardTrackCatalog> logger
) : IRewardTrackCatalog, IReferenceDataProvider
{
    private ImmutableArray<RewardTrackDefinitionSnapshot> _tracks = [];
    private Index _index = Index.Empty;

    /// <summary>
    /// Whether a load has ever succeeded — which is what makes "keep the previous catalogue" a
    /// recovery rather than a way to come up with nothing. Not the same as <c>_tracks</c> being
    /// non-empty: a hotel with no campaigns loaded successfully.
    /// </summary>
    private bool _loaded;

    public int LoadStage => 0;

    public ImmutableArray<RewardTrackDefinitionSnapshot> Tracks => _tracks;

    public bool TryGetTrack(
        string trackId,
        [NotNullWhen(true)] out RewardTrackDefinitionSnapshot? track
    ) => _index.ByTrackId.TryGetValue(trackId, out track);

    public bool IsActionInteresting(string actionCode) => _index.Actions.Contains(actionCode);

    /// <inheritdoc />
    public ImmutableHashSet<string> Actions => _index.Actions;

    public ImmutableArray<RewardTrackTaskRef> TasksFor(string actionCode) =>
        _index.TasksByAction.TryGetValue(actionCode, out ImmutableArray<RewardTrackTaskRef> refs)
            ? refs
            : [];

    public async Task ReloadAsync(CancellationToken ct)
    {
        try
        {
            await using VortexDbContext db = await dbContextFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(false);

            List<RewardTrackEntity> trackRows = await db
                .RewardTracks.AsNoTracking()
                .Where(t => t.DeletedAt == null)
                .OrderBy(t => t.SortOrder)
                .ThenBy(t => t.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            // Four queries for the whole catalogue rather than four per track: a hotel running ten
            // campaigns would otherwise open forty round trips every time an operator saved a row.
            List<RewardTrackTaskEntity> taskRows = await db
                .RewardTrackTasks.AsNoTracking()
                .Where(t => t.DeletedAt == null)
                .OrderBy(t => t.SortOrder)
                .ThenBy(t => t.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            List<RewardTrackTaskLevelEntity> levelRows = await db
                .RewardTrackTaskLevels.AsNoTracking()
                .Where(l => l.DeletedAt == null)
                .OrderBy(l => l.LevelIndex)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            List<RewardTrackTaskStepEntity> stepRows = await db
                .RewardTrackTaskSteps.AsNoTracking()
                .Where(s => s.DeletedAt == null)
                .OrderBy(s => s.StepIndex)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            List<RewardTrackStepFilterEntity> filterRows = await db
                .RewardTrackStepFilters.AsNoTracking()
                .Where(f => f.DeletedAt == null)
                .OrderBy(f => f.SortOrder)
                .ThenBy(f => f.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            List<RewardTrackPrizeEntity> prizeRows = await db
                .RewardTrackPrizes.AsNoTracking()
                .Where(p => p.DeletedAt == null)
                .OrderBy(p => p.RequiredPoints)
                .ThenBy(p => p.SortOrder)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            List<RewardTrackPrizeRewardEntity> rewardRows = await db
                .RewardTrackPrizeRewards.AsNoTracking()
                .Where(r => r.DeletedAt == null)
                .OrderBy(r => r.SortOrder)
                .ThenBy(r => r.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            ILookup<int, RewardTrackTaskEntity> tasksByTrack = taskRows.ToLookup(t =>
                t.RewardTrackEntityId
            );
            ILookup<int, RewardTrackTaskLevelEntity> levelsByTask = levelRows.ToLookup(l =>
                l.RewardTrackTaskEntityId
            );
            ILookup<int, RewardTrackTaskStepEntity> stepsByTask = stepRows.ToLookup(s =>
                s.RewardTrackTaskEntityId
            );
            ILookup<int, RewardTrackStepFilterEntity> filtersByStep = filterRows.ToLookup(f =>
                f.RewardTrackTaskStepEntityId
            );
            ILookup<int, RewardTrackPrizeEntity> prizesByTrack = prizeRows.ToLookup(p =>
                p.RewardTrackEntityId
            );
            ILookup<int, RewardTrackPrizeRewardEntity> rewardsByPrize = rewardRows.ToLookup(r =>
                r.RewardTrackPrizeEntityId
            );

            ImmutableArray<RewardTrackDefinitionSnapshot> tracks =
            [
                .. trackRows.Select(t =>
                    BuildTrack(
                        t,
                        tasksByTrack[t.Id],
                        levelsByTask,
                        stepsByTask,
                        filtersByStep,
                        prizesByTrack[t.Id],
                        rewardsByPrize
                    )
                ),
            ];

            _tracks = tracks;
            _index = Index.Build(tracks);
            _loaded = true;

            logger.LogInformation(
                "Loaded {TrackCount} reward track(s); {ActionCount} action code(s) have at least one task.",
                tracks.Length,
                _index.Actions.Count
            );
        }
        catch (Exception ex) when (_loaded)
        {
            // The previous catalogue stays. Every player keeps the tracks they were already being
            // served rather than every campaign vanishing because one query timed out.
            //
            // The filter is what stops that reasoning applying to the FIRST load, where there is no
            // previous catalogue: the hotel would come up with no campaigns at all, silently, and
            // VortexEmulator's own try/catch would never see it. An unfiltered catch here made a
            // startup failure indistinguishable from a hotel that simply has no content.
            logger.LogError(
                ex,
                "Failed to load the reward-track catalog; keeping the previous one."
            );
        }
    }

    private static RewardTrackDefinitionSnapshot BuildTrack(
        RewardTrackEntity track,
        IEnumerable<RewardTrackTaskEntity> tasks,
        ILookup<int, RewardTrackTaskLevelEntity> levelsByTask,
        ILookup<int, RewardTrackTaskStepEntity> stepsByTask,
        ILookup<int, RewardTrackStepFilterEntity> filtersByStep,
        IEnumerable<RewardTrackPrizeEntity> prizes,
        ILookup<int, RewardTrackPrizeRewardEntity> rewardsByPrize
    ) =>
        new()
        {
            TrackId = track.TrackId,
            Theme = track.Theme,
            Status = track.Status,
            SortOrder = track.SortOrder,
            StartsAtUtc = track.StartsAt,
            ProgressEndsAtUtc = track.ProgressEndsAt,
            ClaimEndsAtUtc = track.ClaimEndsAt,
            UnlockKind = track.UnlockKind,
            UnlockValue = track.UnlockValue,
            CompletionPolicy = track.CompletionPolicy,
            ContentVersion = track.ContentVersion,
            Hidden = track.Hidden,
            CampaignCode = track.CampaignCode,
            Premium = track.PremiumEnabled
                ? new RewardTrackPremiumSnapshot
                {
                    BoostPerMille = track.PremiumBoostPerMille,
                    InstantPoints = track.PremiumInstantPoints,
                    CostCredits = track.PremiumCostCredits,
                    CostDiamonds = track.PremiumCostDiamonds,
                }
                : null,
            Tasks =
            [
                .. tasks.Select(t =>
                    BuildTask(t, levelsByTask[t.Id], stepsByTask[t.Id], filtersByStep)
                ),
            ],
            Prizes = [.. prizes.Select(p => BuildPrize(p, rewardsByPrize[p.Id]))],
        };

    /// <summary>
    /// A task's sequence, always with at least one step.
    /// </summary>
    /// <remarks>
    /// A task with no stored steps is a plain one, and gets a single step synthesised from its own
    /// action and parameter. That is what keeps every campaign written before sequences existed
    /// working untouched, with no data migration and no second code path in the engine: from here
    /// down, everything walks steps.
    /// </remarks>
    private static ImmutableArray<RewardTrackTaskStepSnapshot> BuildSteps(
        RewardTrackTaskEntity task,
        IEnumerable<RewardTrackTaskStepEntity> steps,
        ILookup<int, RewardTrackStepFilterEntity> filtersByStep
    )
    {
        List<RewardTrackTaskStepEntity> ordered = [.. steps.OrderBy(s => s.StepIndex)];

        if (ordered.Count == 0)
        {
            return
            [
                new RewardTrackTaskStepSnapshot
                {
                    StepIndex = 0,
                    ActionCode = task.ActionCode,
                    Filters = [],
                },
            ];
        }

        return
        [
            .. ordered.Select(
                (step, index) =>
                    new RewardTrackTaskStepSnapshot
                    {
                        // Renumbered from the order rather than trusted: a gap in the stored
                        // indexes would otherwise strand a player on a step that does not exist.
                        StepIndex = index,
                        ActionCode = step.ActionCode,
                        Filters = IsNested(filtersByStep[step.Id])
                            ? []
                            : BuildFilters(step, filtersByStep[step.Id]),
                        Root = IsNested(filtersByStep[step.Id])
                            ? BuildRoot(step, filtersByStep[step.Id])
                            : null,
                    }
            ),
        ];
    }

    /// <summary>
    /// Whether this step's rows form a tree, or the flat list they have always been.
    /// </summary>
    /// <remarks>
    /// Asked rather than assumed, so a step nobody nested is carried the way it always was and
    /// takes the same path through the engine. A single joining row, or a single row with a parent,
    /// is enough to make the whole step a tree — there is no halfway shape.
    /// </remarks>
    private static bool IsNested(IEnumerable<RewardTrackStepFilterEntity> filters)
    {
        foreach (RewardTrackStepFilterEntity filter in filters)
        {
            if (filter.Kind != PredicateKind.Test || filter.ParentId is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Assembles a step's rows into the tree they describe, under an implicit <c>All</c> root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The root is implicit and not a row: a step has always meant "all of this", and giving it a
    /// row would be one more thing an editor could delete to change what a step means without
    /// touching a condition.
    /// </para>
    /// <para>
    /// Unlike the flat path, a test whose back-reference cannot resolve is kept rather than
    /// dropped. Dropping is safe when everything ANDs — one less thing to satisfy — and is not safe
    /// here: removing a branch of an <c>Any</c> makes the step harder, removing one from under a
    /// <c>Not</c> inverts it. Kept, it simply never holds, which the evaluator already does for a
    /// reference it cannot resolve.
    /// </para>
    /// </remarks>
    private static StepPredicateSnapshot BuildRoot(
        RewardTrackTaskStepEntity step,
        IEnumerable<RewardTrackStepFilterEntity> filters
    )
    {
        List<RewardTrackStepFilterEntity> rows = [.. filters];
        Dictionary<int, List<RewardTrackStepFilterEntity>> byParent = [];

        foreach (RewardTrackStepFilterEntity row in rows.OrderBy(f => f.SortOrder))
        {
            // Everything with no parent hangs off the implicit root, which has no id of its own.
            int parent = row.ParentId ?? 0;

            if (!byParent.TryGetValue(parent, out List<RewardTrackStepFilterEntity>? siblings))
            {
                byParent[parent] = siblings = [];
            }

            siblings.Add(row);
        }

        return new StepPredicateSnapshot
        {
            Kind = PredicateKind.All,
            Children = Assemble(step, byParent, 0, []),
        };
    }

    /// <summary>
    /// The children of one node, and their children.
    /// </summary>
    /// <param name="seen">
    /// The ids already on this branch. A row whose parent chain loops back on itself would recurse
    /// for ever, and a row can be given any parent by the operations API or by hand.
    /// </param>
    private static ImmutableArray<StepPredicateSnapshot> Assemble(
        RewardTrackTaskStepEntity step,
        Dictionary<int, List<RewardTrackStepFilterEntity>> byParent,
        int parentId,
        HashSet<int> seen
    )
    {
        if (!byParent.TryGetValue(parentId, out List<RewardTrackStepFilterEntity>? children))
        {
            return [];
        }

        List<StepPredicateSnapshot> built = [];

        foreach (RewardTrackStepFilterEntity row in children)
        {
            if (!seen.Add(row.Id))
            {
                continue;
            }

            built.Add(
                row.Kind == PredicateKind.Test
                    ? new StepPredicateSnapshot
                    {
                        Kind = PredicateKind.Test,
                        Test = new RewardTrackStepFilterSnapshot
                        {
                            FactKey = row.FactKey,
                            Operator = row.Operator,
                            Value = row.Value,
                            ReferencedStep = ParseBackReference(row.Value),
                        },
                    }
                    : new StepPredicateSnapshot
                    {
                        Kind = row.Kind,
                        Children = Assemble(step, byParent, row.Id, seen),
                    }
            );

            seen.Remove(row.Id);
        }

        return [.. built];
    }

    private static ImmutableArray<RewardTrackStepFilterSnapshot> BuildFilters(
        RewardTrackTaskStepEntity step,
        IEnumerable<RewardTrackStepFilterEntity> filters
    )
    {
        List<RewardTrackStepFilterSnapshot> built = [];

        foreach (RewardTrackStepFilterEntity filter in filters.OrderBy(f => f.SortOrder))
        {
            int referenced = ParseBackReference(filter.Value);

            // A reference forward, or to itself, can never resolve: the step it names has not run.
            // Dropped here rather than left to fail every signal, so the log says so once at load
            // instead of the task silently never advancing.
            if (referenced >= step.StepIndex)
            {
                continue;
            }

            built.Add(
                new RewardTrackStepFilterSnapshot
                {
                    FactKey = filter.FactKey,
                    Operator = filter.Operator,
                    Value = filter.Value,
                    ReferencedStep = referenced,
                }
            );
        }

        return [.. built];
    }

    /// <summary>The step a <c>$N</c> value points at, or <c>-1</c> when the value is a literal.</summary>
    private static int ParseBackReference(string value) =>
        value.Length > 1
        && value[0] == '$'
        && int.TryParse(
            value.AsSpan(1),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out int step
        )
            ? step
            : -1;

    private static RewardTrackTaskDefinitionSnapshot BuildTask(
        RewardTrackTaskEntity task,
        IEnumerable<RewardTrackTaskLevelEntity> levels,
        IEnumerable<RewardTrackTaskStepEntity> steps,
        ILookup<int, RewardTrackStepFilterEntity> filtersByStep
    ) =>
        new()
        {
            TaskId = task.TaskId,
            ActionCode = task.ActionCode,
            Parameter = task.Parameter,
            Mode = task.Mode,
            Premium = task.Premium,
            SortOrder = task.SortOrder,
            Steps = BuildSteps(task, steps, filtersByStep),
            // Ordered by requirement, not by the stored index: the stage math walks them in
            // ascending order and a content edit that renumbers badly would otherwise pay stages
            // out of sequence.
            Levels =
            [
                .. levels
                    .OrderBy(l => l.RequiredCount)
                    .Select(
                        (l, i) =>
                            new RewardTrackTaskLevelSnapshot
                            {
                                LevelIndex = i,
                                RequiredCount = l.RequiredCount,
                                PointsReward = l.PointsReward,
                                Premium = l.Premium,
                            }
                    ),
            ],
        };

    private static RewardTrackPrizeDefinitionSnapshot BuildPrize(
        RewardTrackPrizeEntity prize,
        IEnumerable<RewardTrackPrizeRewardEntity> rewards
    ) =>
        new()
        {
            PrizeId = prize.PrizeId,
            RequiredPoints = prize.RequiredPoints,
            Premium = prize.Premium,
            SortOrder = prize.SortOrder,
            Rewards =
            [
                .. rewards.Select(r => new RewardGrantSnapshot
                {
                    Kind = r.Kind,
                    RewardTypeId = r.RewardTypeId,
                    Amount = r.Amount,
                    ExtraParams = r.ExtraParams,
                }),
            ],
        };

    /// <summary>
    /// The lookups, rebuilt as one unit so a reload is never observed half-applied: the field is
    /// swapped once, and a reader sees all of the old catalogue or all of the new one.
    /// </summary>
    private sealed record Index(
        IReadOnlyDictionary<string, RewardTrackDefinitionSnapshot> ByTrackId,
        ImmutableHashSet<string> Actions,
        IReadOnlyDictionary<string, ImmutableArray<RewardTrackTaskRef>> TasksByAction
    )
    {
        public static Index Empty { get; } =
            new(
                new Dictionary<string, RewardTrackDefinitionSnapshot>(StringComparer.Ordinal),
                ImmutableHashSet.Create<string>(StringComparer.Ordinal),
                new Dictionary<string, ImmutableArray<RewardTrackTaskRef>>(StringComparer.Ordinal)
            );

        public static Index Build(ImmutableArray<RewardTrackDefinitionSnapshot> tracks)
        {
            Dictionary<string, RewardTrackDefinitionSnapshot> byTrackId = new(
                StringComparer.Ordinal
            );
            Dictionary<string, List<RewardTrackTaskRef>> byAction = new(StringComparer.Ordinal);

            foreach (RewardTrackDefinitionSnapshot track in tracks)
            {
                byTrackId[track.TrackId] = track;

                // Archived tracks are indexed too. They accept no progress -- the grain checks the
                // clock and the status -- but leaving them out of the index would make the answer
                // to "which tasks care about this?" depend on a schedule, and the index is content.
                foreach (RewardTrackTaskDefinitionSnapshot task in track.Tasks)
                {
                    // Every action ANY of the task's steps names, not just the first. A sequence
                    // that was only indexed on its opening action would never hear the signal that
                    // moves it past step 0, and would sit there looking broken.
                    foreach (RewardTrackTaskStepSnapshot step in task.Steps)
                    {
                        if (
                            !byAction.TryGetValue(
                                step.ActionCode,
                                out List<RewardTrackTaskRef>? list
                            )
                        )
                        {
                            list = [];
                            byAction[step.ActionCode] = list;
                        }

                        RewardTrackTaskRef reference = new(track.TrackId, task.TaskId);

                        // A sequence may use the same action twice ("place, then place again").
                        if (!list.Contains(reference))
                        {
                            list.Add(reference);
                        }
                    }
                }
            }

            Dictionary<string, ImmutableArray<RewardTrackTaskRef>> frozen = new(
                StringComparer.Ordinal
            );

            foreach ((string action, List<RewardTrackTaskRef> refs) in byAction)
            {
                frozen[action] = [.. refs];
            }

            return new Index(
                byTrackId,
                byAction.Keys.ToImmutableHashSet(StringComparer.Ordinal),
                frozen
            );
        }
    }
}
