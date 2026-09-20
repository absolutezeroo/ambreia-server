using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Dashboard.API.Api.Progression.Contracts;
using Vortex.Database.Context;
using Vortex.Database.Entities.RewardTracks;
using Vortex.Primitives.RewardTracks;
using Vortex.Primitives.Signals;

namespace Vortex.Dashboard.API.Api.Progression;

/// <summary>
/// Read surface for reward-track content and player progression. The CRUD lives in
/// <see cref="Operations.RewardOperations"/>; here we only read.
/// </summary>
internal sealed class RewardTrackReads(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    ISignalVocabulary signalVocabulary
) : DashboardReads(dbContextFactory)
{
    private readonly ISignalVocabulary _signalVocabulary = signalVocabulary;

    /// <summary>
    /// The action codes a task can be defined on, and — the part that matters — whether anything
    /// actually raises each one today.
    /// </summary>
    /// <remarks>
    /// A task on a code with no producer never advances, and a bar that never moves is the worst
    /// thing this content system can ship. The flag is computed from the handlers that exist in
    /// <c>RewardTrackEventHandlers</c>, so it stops being a lie the moment one is added.
    /// </remarks>
    public RewardTrackActionOptions RewardTrackActionOptions()
    {
        // The action list is declared, not derived from the translators that exist: two actions
        // have no producer today and content may already name them, so deriving would make a task
        // written on one disappear from the editor instead of being flagged inert.
        List<RewardTrackActionOption> items =
        [
            .. SignalActions
                .All.OrderBy(name => name, StringComparer.Ordinal)
                .Select(name => new RewardTrackActionOption(
                    name,
                    // Whether anything actually raises this action. Read from the loaded
                    // translators, so it is a fact rather than a hand-kept list.
                    _signalVocabulary.ShapesFor(name).Length > 0,
                    // What a step on this action can filter on, and what a later step can point
                    // back at, typed so the editor knows which control to draw. The shapes come
                    // from the translators themselves: a fact the action never emits cannot be
                    // offered here.
                    FactsOf(name)
                )),
        ];

        return new RewardTrackActionOptions(items.Count, items);
    }

    /// <summary>
    /// Every fact a step on this action can be filtered on, typed.
    /// </summary>
    /// <remarks>
    /// The kind is what lets one filter editor serve every action: it decides whether the operator
    /// gets a picker, a select of declared values or a text box, and which operators are offered.
    /// Before it, that mapping was hardcoded per page, which is why only this page had pickers.
    /// <para>
    /// <c>target</c> is included when the action has one, and typed by the action rather than
    /// globally: the target of <c>create_room</c> is a room and the target of <c>give_respect</c> is
    /// a player. It is a real fact -- the signal host republishes it -- it is simply not declared by
    /// a translator among its facts.
    /// </para>
    /// </remarks>
    private List<FactOption> FactsOf(string action)
    {
        Dictionary<string, FactOption> byKey = new(StringComparer.Ordinal);

        foreach (SignalShape shape in _signalVocabulary.ShapesFor(action))
        {
            if (shape.TargetKind is FactKind targetKind)
            {
                byKey[Facts.TargetKey] = Describe(
                    new FactKey(Facts.TargetKey, targetKind, "rewardTracks.fact_target", "Target")
                );
            }

            foreach (FactKey fact in shape.Facts)
            {
                byKey[fact.Key] = Describe(fact);
            }
        }

        return [.. byKey.Values];
    }

    private static FactOption Describe(FactKey fact) =>
        new(
            fact.Key,
            fact.Kind.ToString(),
            fact.LabelKey,
            fact.FallbackLabel,
            // Which operators mean anything here. Sent rather than inferred client-side: the
            // validator refuses the others, and an editor that offered them would be inviting a
            // save it knows will fail.
            [.. FactOperators.For(fact.Kind)],
            fact.EnumValues.IsDefaultOrEmpty
                ? []
                :
                [
                    .. fact.EnumValues.Select(v => new FactOptionValue(
                        v.Value,
                        v.LabelKey,
                        v.FallbackLabel
                    )),
                ]
        );

    /// <summary>The reward kinds, with the client's own product-type id and what the target field means.</summary>
    public RewardKindOptions RewardTrackRewardKindOptions()
    {
        List<RewardKindOption> items =
        [
            .. Enum.GetValues<RewardKind>()
                .Select(k => new RewardKindOption(k.ToString(), (int)k, RewardTargetHint(k))),
        ];

        return new RewardKindOptions(items.Count, items);
    }

    /// <summary>Every track, its content, and how the hotel is doing on it.</summary>
    public Task<RewardTrackList> RewardTracksAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        QueryAsync<RewardTrackList>(
            async db =>
            {
                string search = (query["search"] ?? string.Empty).Trim();

                List<RewardTrackEntity> tracks = await db
                    .RewardTracks.AsNoTracking()
                    .Where(t => t.DeletedAt == null)
                    .OrderBy(t => t.SortOrder)
                    .ThenBy(t => t.Id)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                if (search.Length > 0)
                {
                    tracks =
                    [
                        .. tracks.Where(t =>
                            t.TrackId.Contains(search, StringComparison.OrdinalIgnoreCase)
                            || t.CampaignCode.Contains(search, StringComparison.OrdinalIgnoreCase)
                        ),
                    ];
                }

                // Five queries for the whole list rather than five per track.
                List<RewardTrackTaskEntity> tasks = await db
                    .RewardTrackTasks.AsNoTracking()
                    .Where(t => t.DeletedAt == null)
                    .OrderBy(t => t.SortOrder)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<RewardTrackTaskLevelEntity> levels = await db
                    .RewardTrackTaskLevels.AsNoTracking()
                    .Where(l => l.DeletedAt == null)
                    .OrderBy(l => l.LevelIndex)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<RewardTrackTaskStepEntity> steps = await db
                    .RewardTrackTaskSteps.AsNoTracking()
                    .Where(s => s.DeletedAt == null)
                    .OrderBy(s => s.StepIndex)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<RewardTrackStepFilterEntity> stepFilters = await db
                    .RewardTrackStepFilters.AsNoTracking()
                    .Where(f => f.DeletedAt == null)
                    .OrderBy(f => f.SortOrder)
                    .ThenBy(f => f.Id)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<RewardTrackPrizeEntity> prizes = await db
                    .RewardTrackPrizes.AsNoTracking()
                    .Where(p => p.DeletedAt == null)
                    .OrderBy(p => p.RequiredPoints)
                    .ThenBy(p => p.SortOrder)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<RewardTrackPrizeRewardEntity> rewards = await db
                    .RewardTrackPrizeRewards.AsNoTracking()
                    .Where(r => r.DeletedAt == null)
                    .OrderBy(r => r.SortOrder)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                Dictionary<string, (int Participants, int Completions, int Premium)> stats =
                    await db
                        .PlayerRewardTracks.Where(t => t.DeletedAt == null)
                        .GroupBy(t => t.TrackId)
                        .Select(g => new
                        {
                            TrackId = g.Key,
                            Participants = g.Count(),
                            Completions = g.Count(x => x.CompletedAt != null),
                            Premium = g.Count(x => x.PremiumUnlocked),
                        })
                        .ToDictionaryAsync(
                            x => x.TrackId,
                            x => (x.Participants, x.Completions, x.Premium),
                            ct
                        )
                        .ConfigureAwait(false);

                Dictionary<string, int> claims = await db
                    .PlayerRewardTrackClaims.Where(c => c.DeletedAt == null)
                    .GroupBy(c => c.TrackId)
                    .Select(g => new { TrackId = g.Key, Claims = g.Count() })
                    .ToDictionaryAsync(x => x.TrackId, x => x.Claims, ct)
                    .ConfigureAwait(false);

                // Whether a task has ever actually matched, and when it last did.
                //
                // Read rather than recorded: a row here is only written when a task advances, and
                // its UpdatedAt is therefore already the answer. A last_matched_at column on the
                // task would have meant a write to one content row on every advance by every
                // player, which is contention on the hottest thing the subsystem does, for a
                // number only an operator ever reads.
                //
                // This is the half the Wired flag cannot cover. That one says the action is raised
                // by something; this one says the task, with its filters, its parameter and its
                // mode, has been satisfied by a real player at least once. A task that is wired and
                // has never matched is the silent failure the whole subsystem is built against, and
                // until now nothing on any screen could tell it from one nobody had got to yet.
                Dictionary<(string Track, string Task), (int Players, DateTime Last)> matched =
                    await db
                        .PlayerRewardTrackTasks.Where(t => t.DeletedAt == null)
                        .GroupBy(t => new { t.TrackId, t.TaskId })
                        .Select(g => new
                        {
                            g.Key.TrackId,
                            g.Key.TaskId,
                            Players = g.Count(),
                            Last = g.Max(x => x.UpdatedAt),
                        })
                        .ToDictionaryAsync(x => (x.TrackId, x.TaskId), x => (x.Players, x.Last), ct)
                        .ConfigureAwait(false);

                List<RewardTrackRow> items = [];

                foreach (RewardTrackEntity track in tracks)
                {
                    List<RewardTrackTaskEntity> trackTasks =
                    [
                        .. tasks.Where(t => t.RewardTrackEntityId == track.Id),
                    ];
                    List<RewardTrackPrizeEntity> trackPrizes =
                    [
                        .. prizes.Where(p => p.RewardTrackEntityId == track.Id),
                    ];

                    (int participants, int completions, int premium) = stats.GetValueOrDefault(
                        track.TrackId
                    );

                    // The ceilings the content validator measures milestones against, shown here so
                    // an operator can see why a prize is out of reach without publishing to find out.
                    int freePoints = 0;
                    int premiumPoints = track.PremiumEnabled ? track.PremiumInstantPoints : 0;

                    foreach (RewardTrackTaskEntity task in trackTasks)
                    {
                        foreach (
                            RewardTrackTaskLevelEntity level in levels.Where(l =>
                                l.RewardTrackTaskEntityId == task.Id
                            )
                        )
                        {
                            premiumPoints += level.PointsReward;

                            if (!task.Premium && !level.Premium)
                            {
                                freePoints += level.PointsReward;
                            }
                        }
                    }

                    items.Add(
                        new RewardTrackRow(
                            track.Id,
                            track.TrackId,
                            $"reward_track.{track.TrackId}.name",
                            track.Theme,
                            track.Status.ToString(),
                            track.SortOrder,
                            track.StartsAt,
                            track.ProgressEndsAt,
                            track.ClaimEndsAt,
                            track.UnlockKind.ToString(),
                            track.UnlockValue,
                            track.CompletionPolicy.ToString(),
                            track.PremiumEnabled,
                            track.PremiumBoostPerMille,
                            track.PremiumInstantPoints,
                            track.PremiumCostCredits,
                            track.PremiumCostDiamonds,
                            track.ContentVersion,
                            track.Hidden,
                            track.CampaignCode,
                            freePoints,
                            premiumPoints,
                            participants,
                            completions,
                            premium,
                            claims.GetValueOrDefault(track.TrackId),
                            trackTasks
                                .Select(t => new RewardTrackTaskRow(
                                    t.Id,
                                    t.TaskId,
                                    $"reward_track.{track.TrackId}.task.{t.TaskId}.name",
                                    t.ActionCode,
                                    WiredRewardTrackActions.Contains(t.ActionCode),
                                    t.Parameter,
                                    t.Mode.ToString(),
                                    t.Premium,
                                    t.SortOrder,
                                    matched.TryGetValue(
                                        (track.TrackId, t.TaskId),
                                        out (int Players, DateTime Last) seen
                                    )
                                        ? seen.Players
                                        : 0,
                                    seen.Players > 0 ? seen.Last : null,
                                    // Empty for a plain task: the engine builds its single step
                                    // from the action above, and the editor pre-fills the same way.
                                    steps
                                        .Where(s => s.RewardTrackTaskEntityId == t.Id)
                                        .Select(s => new RewardTrackStepRow(
                                            s.StepIndex,
                                            s.ActionCode,
                                            Flat(
                                                stepFilters.Where(f =>
                                                    f.RewardTrackTaskStepEntityId == s.Id
                                                )
                                            ),
                                            Tree(
                                                stepFilters.Where(f =>
                                                    f.RewardTrackTaskStepEntityId == s.Id
                                                )
                                            )
                                        ))
                                        .ToList(),
                                    levels
                                        .Where(l => l.RewardTrackTaskEntityId == t.Id)
                                        .Select(l => new RewardTrackLevelRow(
                                            l.LevelIndex,
                                            l.RequiredCount,
                                            l.PointsReward,
                                            l.Premium
                                        ))
                                        .ToList()
                                ))
                                .ToList(),
                            trackPrizes
                                .Select(p => new RewardTrackPrizeRow(
                                    p.Id,
                                    p.PrizeId,
                                    p.RequiredPoints,
                                    p.Premium,
                                    p.SortOrder,
                                    p.RequiredPoints <= (p.Premium ? premiumPoints : freePoints),
                                    rewards
                                        .Where(r => r.RewardTrackPrizeEntityId == p.Id)
                                        .Select(r => new RewardTrackRewardRow(
                                            r.Id,
                                            r.Kind.ToString(),
                                            (int)r.Kind,
                                            r.RewardTypeId,
                                            r.Amount,
                                            r.ExtraParams,
                                            r.SortOrder
                                        ))
                                        .ToList()
                                ))
                                .ToList()
                        )
                    );
                }

                return new RewardTrackList(items.Count, items);
            },
            ct
        );

    /// <summary>One player's standing on every track they have touched, plus their task progress.</summary>
    public Task<PlayerRewardTracks> PlayerRewardTracksAsync(int playerId, CancellationToken ct) =>
        QueryAsync<PlayerRewardTracks>(
            async db =>
            {
                List<PlayerRewardTrackEntity> rows = await db
                    .PlayerRewardTracks.AsNoTracking()
                    .Where(t => t.PlayerEntityId == playerId && t.DeletedAt == null)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<PlayerRewardTrackTaskEntity> tasks = await db
                    .PlayerRewardTrackTasks.AsNoTracking()
                    .Where(t => t.PlayerEntityId == playerId && t.DeletedAt == null)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<PlayerRewardTrackClaimEntity> claims = await db
                    .PlayerRewardTrackClaims.AsNoTracking()
                    .Where(c => c.PlayerEntityId == playerId && c.DeletedAt == null)
                    .OrderByDescending(c => c.ClaimedAt)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                // How long each of this player's tasks is, so a step number reads as a position.
                // Keyed by track and task because a task id is only unique inside its track.
                Dictionary<(string Track, string Task), int> stepCounts = await db
                    .RewardTrackTaskSteps.AsNoTracking()
                    .Where(s => s.DeletedAt == null && s.Task != null && s.Task.DeletedAt == null)
                    .Select(s => new { s.Task!.RewardTrack!.TrackId, s.Task.TaskId })
                    .GroupBy(s => new { s.TrackId, s.TaskId })
                    .ToDictionaryAsync(g => (g.Key.TrackId, g.Key.TaskId), g => g.Count(), ct)
                    .ConfigureAwait(false);

                return new PlayerRewardTracks(
                    playerId,
                    rows.Count,
                    rows.Select(r => new PlayerRewardTrackRow(
                            r.TrackId,
                            r.Points,
                            r.PremiumUnlocked,
                            r.PremiumUnlockedAt,
                            r.CompletedAt,
                            r.ContentVersion,
                            tasks
                                .Where(t => t.TrackId == r.TrackId)
                                .Select(t => new PlayerRewardTrackTaskRow(
                                    t.TaskId,
                                    t.ProgressCount,
                                    t.HighestPaidLevelIndex,
                                    t.CurrentStep,
                                    stepCounts.GetValueOrDefault((r.TrackId, t.TaskId)),
                                    t.CapturedFacts
                                ))
                                .ToList(),
                            claims
                                .Where(c => c.TrackId == r.TrackId)
                                .Select(c => new PlayerRewardTrackClaimRow(
                                    c.PrizeId,
                                    c.ClaimedAt,
                                    c.PointsAtClaim,
                                    // What was actually handed over, rendered at claim time. The
                                    // prize definition can be rewritten afterwards; this cannot,
                                    // which is what makes "why does this player have that?"
                                    // answerable a year later.
                                    c.GrantedSummary
                                ))
                                .ToList()
                        ))
                        .ToList()
                );
            },
            ct
        );

    /// <summary>
    /// One condition, with both its sides spelled out.
    /// </summary>
    /// <remarks>
    /// A row that never named a side is sent as the two it has always meant — its fact on the
    /// left, its value on the right — so the editor reads one shape rather than branching on
    /// whether a condition predates them. A step reference is unpacked from the stored
    /// <c>N:fact_key</c> here, for the same reason: the caller names a step as a number and never
    /// has to know how the column holds it.
    /// </remarks>
    private static RewardTrackFilterRow Condition(RewardTrackStepFilterEntity row) =>
        new(
            row.FactKey,
            (int)row.Operator,
            row.Value,
            Side(row.LeftKind, row.LeftArg, StepValueKind.Fact, row.FactKey),
            Side(row.RightKind, row.RightArg, StepValueKind.Literal, row.Value)
        );

    /// <summary>One side, or what the row meant before it could name one.</summary>
    private static RewardTrackValueRow Side(
        StepValueKind kind,
        string? arg,
        StepValueKind fallbackKind,
        string fallbackArg
    )
    {
        if (arg is null)
        {
            return new RewardTrackValueRow((int)fallbackKind, fallbackArg, -1);
        }

        if (kind != StepValueKind.StepFact)
        {
            return new RewardTrackValueRow((int)kind, arg, -1);
        }

        int colon = arg.IndexOf(':');

        return
            colon > 0
            && int.TryParse(
                arg.AsSpan(0, colon),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int step
            )
            ? new RewardTrackValueRow((int)kind, arg[(colon + 1)..], step)
            : new RewardTrackValueRow((int)kind, arg, -1);
    }

    /// <summary>
    /// A step's conditions the way they have always been read: one flat list that ANDs.
    /// </summary>
    /// <remarks>
    /// Empty once a step nests, which is what the engine reads too — a tree flattened into a list
    /// is not the same question, and an editor that showed one would offer to save the wrong thing.
    /// </remarks>
    private static IReadOnlyList<RewardTrackFilterRow> Flat(
        IEnumerable<RewardTrackStepFilterEntity> rows
    )
    {
        List<RewardTrackFilterRow> flat = [];

        foreach (RewardTrackStepFilterEntity row in rows)
        {
            if (row.Kind != PredicateKind.Test || row.ParentId is not null)
            {
                return [];
            }

            flat.Add(Condition(row));
        }

        return flat;
    }

    /// <summary>
    /// A step's rows as the tree they describe, under the implicit <c>All</c> that a step is.
    /// </summary>
    /// <remarks>
    /// The same assembly the catalog does for the engine, and it has to stay the same: a tree the
    /// editor draws differently from the one that decides who is paid is worse than no editor.
    /// A row whose parent chain loops is dropped rather than followed — it can only come from the
    /// operations API or a hand-edited row, and the engine's assembler guards it the same way.
    /// </remarks>
    private static IReadOnlyList<RewardTrackNodeRow> Tree(
        IEnumerable<RewardTrackStepFilterEntity> rows
    )
    {
        Dictionary<int, List<RewardTrackStepFilterEntity>> byParent = [];

        foreach (RewardTrackStepFilterEntity row in rows)
        {
            // Everything with no parent hangs off the root, which is implicit and has no row.
            int parent = row.ParentId ?? 0;

            if (!byParent.TryGetValue(parent, out List<RewardTrackStepFilterEntity>? siblings))
            {
                byParent[parent] = siblings = [];
            }

            siblings.Add(row);
        }

        return Children(byParent, 0, []);
    }

    private static List<RewardTrackNodeRow> Children(
        Dictionary<int, List<RewardTrackStepFilterEntity>> byParent,
        int parentId,
        HashSet<int> seen
    )
    {
        if (!byParent.TryGetValue(parentId, out List<RewardTrackStepFilterEntity>? rows))
        {
            return [];
        }

        List<RewardTrackNodeRow> built = [];

        foreach (RewardTrackStepFilterEntity row in rows)
        {
            if (!seen.Add(row.Id))
            {
                continue;
            }

            built.Add(
                new RewardTrackNodeRow(
                    (int)row.Kind,
                    row.Kind == PredicateKind.Test ? Condition(row) : null,
                    row.Kind == PredicateKind.Test ? [] : Children(byParent, row.Id, seen)
                )
            );

            seen.Remove(row.Id);
        }

        return built;
    }

    /// <summary>
    /// The action codes something actually raises today, from the handlers in
    /// <c>Vortex.RewardTracks/Events/RewardTrackEventHandlers.cs</c>. Kept here rather than
    /// discovered by reflection because this project does not reference that assembly — the
    /// duplication is deliberate and small, and the walkthrough says to update both together.
    /// </summary>
    private static readonly HashSet<string> WiredRewardTrackActions = new(StringComparer.Ordinal)
    {
        RewardTrackActions.EnterOtherUsersRoom,
        RewardTrackActions.ChatWithSomeone,
        RewardTrackActions.Dance,
        RewardTrackActions.Wave,
        RewardTrackActions.ShowSign,
        RewardTrackActions.RequestFriend,
        RewardTrackActions.GiveRespect,
        RewardTrackActions.ChangeFigure,
        RewardTrackActions.ChangeMotto,
        RewardTrackActions.WearBadge,
        RewardTrackActions.CreateRoom,
        RewardTrackActions.PlaceItem,
        RewardTrackActions.MoveItem,
        RewardTrackActions.RotateItem,
        RewardTrackActions.PickUpItem,
        RewardTrackActions.WalkOnFurni,
        RewardTrackActions.PetLevel,
        RewardTrackActions.BuyFromCatalogue,
        RewardTrackActions.SpendCredits,
        RewardTrackActions.CompleteTrade,
        RewardTrackActions.SendMessengerMessage,
        RewardTrackActions.UseHabbicon,
        RewardTrackActions.CompleteHabbiconCollection,
        RewardTrackActions.CompleteQuest,
        RewardTrackActions.AchievementLevel,
    };

    private static string RewardTargetHint(RewardKind kind) =>
        kind switch
        {
            RewardKind.WallItem => "wall item type id",
            RewardKind.FloorItem => "furniture definition id",
            RewardKind.AvatarEffect => "effect id",
            RewardKind.Badge => "badge code",
            RewardKind.Bot => "bot name (extra params = figure)",
            RewardKind.Currency => "activity point type: -1 credits, 0 duckets, 5 diamonds",
            RewardKind.ChatStyle => "chat style id",
            RewardKind.Pet => "pet type (extra params = figure)",
            RewardKind.Habbicon => "habbicon id",
            RewardKind.Entitlement => "perk code, e.g. TRADE",
            _ => string.Empty,
        };
}
