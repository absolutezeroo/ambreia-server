using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Vortex.Dashboard.API.Admin.Progression;
using Vortex.Database.Context;
using Vortex.Database.Entities.RewardTracks;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.RewardTracks;
using Vortex.Primitives.RewardTracks.Admin;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Dashboard.Tests;

/// <summary>
/// What a saved sequence looks like in the table the engine reads.
/// </summary>
/// <remarks>
/// A condition tree is stored as rows pointing at each other, and a row pointing at the wrong one
/// is the failure this whole area is most exposed to: nothing throws, nothing is logged, the
/// branch is simply never evaluated and the task quietly stops advancing. So these assert on the
/// rows themselves rather than on a round trip through the service, which would agree with its own
/// mistake.
/// </remarks>
public sealed class RewardTrackAdminServiceTests
{
    private static DbContextOptions<VortexDbContext> NewOptions() =>
        new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"reward-track-admin-{Guid.NewGuid():N}")
            .Options;

    private sealed class TestContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }

    private static RewardTrackAdminService NewService(DbContextOptions<VortexDbContext> options) =>
        new(
            new TestContextFactory(options),
            FakeProxy.Create<IRewardTrackCatalog>(_ => null),
            FakeProxy.Create<IReferenceDataReloader>(_ => null),
            FakeProxy.Create<IGrainFactory>(_ => null),
            SignalVocabularyFixture.Real,
            NullLogger<RewardTrackAdminService>.Instance
        );

    private static RewardTrackSpec Track(string trackId) =>
        new(
            trackId,
            "theme",
            RewardTrackStatus.Draft,
            0,
            null,
            null,
            null,
            RewardTrackUnlockKind.Always,
            string.Empty,
            RewardTrackCompletionPolicy.AllFreePrizesClaimed,
            false,
            0,
            0,
            0,
            0,
            false,
            string.Empty
        );

    private static RewardTrackStepNodeSpec Leaf(string factKey, string value) =>
        new(
            PredicateKind.Test,
            new RewardTrackStepFilterSpec(factKey, StepFilterOperator.Equals, value)
        );

    private static RewardTrackStepNodeSpec Group(
        PredicateKind kind,
        params RewardTrackStepNodeSpec[] children
    ) => new(kind, null, children);

    /// <summary>
    /// "That sofa, and either not that other one or in room 12" — one test at the root, one group
    /// beside it, and a negation inside that. Three levels is the smallest shape where a parent
    /// link can be wrong without being obviously wrong.
    /// </summary>
    private static RewardTrackTaskSpec TaskWithATree() =>
        new(
            "task",
            RewardTrackActions.WalkOnFurni,
            string.Empty,
            TaskProgressMode.Counter,
            false,
            0,
            [new RewardTrackTaskLevelSpec(1, 10, false)],
            [
                new RewardTrackTaskStepSpec(
                    RewardTrackActions.WalkOnFurni,
                    [],
                    [
                        Leaf(RewardTrackFacts.Definition, "500"),
                        Group(
                            PredicateKind.Any,
                            Group(PredicateKind.Not, Leaf(RewardTrackFacts.Definition, "900")),
                            Leaf(RewardTrackFacts.Room, "12")
                        ),
                    ]
                ),
            ]
        );

    private static async Task<List<RewardTrackStepFilterEntity>> RowsOfAsync(
        DbContextOptions<VortexDbContext> options,
        string trackId
    )
    {
        await using VortexDbContext db = new(options);

        List<int> stepIds = await db
            .RewardTrackTaskSteps.Where(s =>
                db.RewardTrackTasks.Any(t =>
                    t.Id == s.RewardTrackTaskEntityId
                    && db.RewardTracks.Any(r =>
                        r.Id == t.RewardTrackEntityId && r.TrackId == trackId
                    )
                )
            )
            .Select(s => s.Id)
            .ToListAsync();

        return await db
            .RewardTrackStepFilters.Where(f => stepIds.Contains(f.RewardTrackTaskStepEntityId))
            .OrderBy(f => f.Id)
            .ToListAsync();
    }

    [Fact]
    public async Task A_saved_tree_is_stored_as_rows_that_point_at_each_other()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        RewardTrackAdminService service = NewService(options);

        RewardTrackAdminResult track = await service.CreateTrackAsync(
            Track("nested"),
            CancellationToken.None
        );

        track.Success.Should().BeTrue();

        RewardTrackAdminResult task = await service.UpsertTaskAsync(
            track.Id!.Value,
            TaskWithATree(),
            CancellationToken.None
        );

        task.Success.Should().BeTrue(task.ErrorCode);

        List<RewardTrackStepFilterEntity> rows = await RowsOfAsync(options, "nested");

        rows.Should().HaveCount(5);

        RewardTrackStepFilterEntity any = rows.Single(r => r.Kind == PredicateKind.Any);
        RewardTrackStepFilterEntity not = rows.Single(r => r.Kind == PredicateKind.Not);

        // The two rows nobody can see from a flat reading: a group under the root, and a group
        // under that one.
        any.ParentId.Should().BeNull();
        any.SortOrder.Should().Be(1);
        not.ParentId.Should().Be(any.Id);

        rows.Single(r => r.Value == "900").ParentId.Should().Be(not.Id);
        rows.Single(r => r.Value == "12").ParentId.Should().Be(any.Id);

        // The one at the root keeps the shape a flat list has always had, which is what makes the
        // two readings the same question when nothing is nested.
        RewardTrackStepFilterEntity first = rows.Single(r => r.Value == "500");
        first.Kind.Should().Be(PredicateKind.Test);
        first.ParentId.Should().BeNull();
        first.SortOrder.Should().Be(0);
    }

    [Fact]
    public async Task A_cloned_tree_points_at_its_own_rows_and_not_the_ones_it_was_copied_from()
    {
        // The copy of a parent is a different row, so a clone that kept the stored parent id would
        // hand every branch to a step it does not belong to. The branch is then never read, and a
        // campaign duplicated for next month silently asks less than the one it came from.
        DbContextOptions<VortexDbContext> options = NewOptions();
        RewardTrackAdminService service = NewService(options);

        RewardTrackAdminResult track = await service.CreateTrackAsync(
            Track("source"),
            CancellationToken.None
        );

        await service.UpsertTaskAsync(track.Id!.Value, TaskWithATree(), CancellationToken.None);

        RewardTrackAdminResult clone = await service.CloneTrackAsync(
            track.Id!.Value,
            "copy",
            CancellationToken.None
        );

        clone.Success.Should().BeTrue(clone.ErrorCode);

        List<RewardTrackStepFilterEntity> source = await RowsOfAsync(options, "source");
        List<RewardTrackStepFilterEntity> copied = await RowsOfAsync(options, "copy");

        copied.Should().HaveCount(source.Count);

        RewardTrackStepFilterEntity any = copied.Single(r => r.Kind == PredicateKind.Any);
        RewardTrackStepFilterEntity not = copied.Single(r => r.Kind == PredicateKind.Not);

        not.ParentId.Should().Be(any.Id);
        copied.Single(r => r.Value == "900").ParentId.Should().Be(not.Id);
        copied.Single(r => r.Value == "12").ParentId.Should().Be(any.Id);

        // Said explicitly because it is the bug: every parent named by the copy is a row of the
        // copy.
        List<int> ids = [.. copied.Select(r => r.Id)];

        copied
            .Where(r => r.ParentId is not null)
            .Select(r => r.ParentId!.Value)
            .Should()
            .OnlyContain(id => ids.Contains(id));
    }

    [Fact]
    public async Task A_task_saved_without_a_tree_is_stored_exactly_as_it_always_was()
    {
        // The compatibility claim as a test: a caller that sends a list writes rows with no kind
        // and no parent, which is what every row in this table is today.
        DbContextOptions<VortexDbContext> options = NewOptions();
        RewardTrackAdminService service = NewService(options);

        RewardTrackAdminResult track = await service.CreateTrackAsync(
            Track("flat"),
            CancellationToken.None
        );

        RewardTrackAdminResult task = await service.UpsertTaskAsync(
            track.Id!.Value,
            TaskWithATree() with
            {
                Steps =
                [
                    new RewardTrackTaskStepSpec(
                        RewardTrackActions.WalkOnFurni,
                        [
                            new RewardTrackStepFilterSpec(
                                RewardTrackFacts.Definition,
                                StepFilterOperator.Equals,
                                "500"
                            ),
                        ]
                    ),
                ],
            },
            CancellationToken.None
        );

        task.Success.Should().BeTrue(task.ErrorCode);

        List<RewardTrackStepFilterEntity> rows = await RowsOfAsync(options, "flat");

        rows.Should().ContainSingle();
        rows[0].Kind.Should().Be(PredicateKind.Test);
        rows[0].ParentId.Should().BeNull();
    }
}
