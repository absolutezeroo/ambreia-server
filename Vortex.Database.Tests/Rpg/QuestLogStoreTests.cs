using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Tests.Support;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Quests;
using Vortex.Rpg.Quests.Log;
using Vortex.Rpg.Quests.Persistence;
using Xunit;

namespace Vortex.Database.Tests.Rpg;

/// <summary>The quest log's rows (spec M2a §3.3): the table is exactly the log after each save.</summary>
public sealed class QuestLogStoreTests : IAsyncLifetime
{
    private static readonly PlayerId Player = new(7);
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;

    public async Task InitializeAsync() => (_conn, _options) = await SqliteTestDb.OpenAsync();

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    private QuestLogStore Store() => new(new OptionsDbContextFactory(_options));

    [Fact]
    public async Task ALog_RoundTrips()
    {
        await Store()
            .SaveAsync(
                Player,
                [
                    new QuestEntry
                    {
                        QuestId = "quest.a",
                        Status = QuestStatus.Ready,
                        Progress = [3, 1],
                        AcceptedAt = Now,
                    },
                    new QuestEntry
                    {
                        QuestId = "quest.b",
                        Status = QuestStatus.Done,
                        Progress = [1],
                        AcceptedAt = Now,
                        CompletedAt = Now,
                    },
                ],
                CancellationToken.None
            );

        List<QuestEntry> loaded = await Store().LoadAsync(Player, CancellationToken.None);

        loaded
            .Select(e => (e.QuestId, e.Status, string.Join(",", e.Progress), e.CompletedAt))
            .Should()
            .BeEquivalentTo([
                ("quest.a", QuestStatus.Ready, "3,1", (DateTime?)null),
                ("quest.b", QuestStatus.Done, "1", (DateTime?)Now),
            ]);
    }

    [Fact]
    public async Task ASave_UpdatesAndDeletesRows()
    {
        await Store()
            .SaveAsync(
                Player,
                [
                    new QuestEntry
                    {
                        QuestId = "quest.a",
                        Status = QuestStatus.Active,
                        Progress = [0],
                        AcceptedAt = Now,
                    },
                    new QuestEntry
                    {
                        QuestId = "quest.b",
                        Status = QuestStatus.Active,
                        Progress = [0],
                        AcceptedAt = Now,
                    },
                ],
                CancellationToken.None
            );

        await Store()
            .SaveAsync(
                Player,
                [
                    new QuestEntry
                    {
                        QuestId = "quest.a",
                        Status = QuestStatus.Ready,
                        Progress = [2],
                        AcceptedAt = Now,
                    },
                ],
                CancellationToken.None
            );

        List<QuestEntry> loaded = await Store().LoadAsync(Player, CancellationToken.None);

        loaded.Should().ContainSingle().Which.Progress.Should().Equal(2);
    }

    [Fact]
    public async Task AnotherPlayersRows_AreNotTouched()
    {
        await Store()
            .SaveAsync(
                new PlayerId(8),
                [
                    new QuestEntry
                    {
                        QuestId = "quest.a",
                        Status = QuestStatus.Active,
                        Progress = [0],
                        AcceptedAt = Now,
                    },
                ],
                CancellationToken.None
            );

        await Store().SaveAsync(Player, [], CancellationToken.None);

        (await Store().LoadAsync(new PlayerId(8), CancellationToken.None)).Should().ContainSingle();
    }
}
