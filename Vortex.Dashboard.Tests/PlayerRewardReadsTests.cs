using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vortex.Dashboard.API.Api.Progression;
using Vortex.Dashboard.API.Api.Progression.Contracts;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Observability.Configuration;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;
using Xunit;

namespace Vortex.Dashboard.Tests;

/// <summary>
/// The player-rewards detail page, run against a real relational provider.
/// </summary>
/// <remarks>
/// It answered 500 for every player: the chat-style query ordered on a record built by its own
/// <c>Select</c>, which no relational provider can translate. The in-memory provider evaluates it in
/// memory and passes, so this runs on SQLite.
/// </remarks>
public sealed class PlayerRewardReadsTests : IAsyncLifetime
{
    private readonly SqliteConnection _conn = new("Filename=:memory:");
    private DbContextOptions<VortexDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _conn.OpenAsync();
        _options = new DbContextOptionsBuilder<VortexDbContext>().UseSqlite(_conn).Options;

        await using VortexDbContext db = new(_options);

        // Same repair as the other SQLite suites: EF never writes created_at/updated_at and the
        // derived schema gives them no default.
        await db.Database.ExecuteSqlRawAsync(
            TimestampDefault.Replace(
                db.Database.GenerateCreateScript(),
                "$1 NOT NULL DEFAULT CURRENT_TIMESTAMP"
            )
        );
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        db.Players.Add(
            new PlayerEntity
            {
                Id = 7,
                Name = "Someone",
                Motto = "",
                Figure = "hd-180-1",
                Gender = AvatarGenderType.Male,
                PlayerStatus = PlayerStatusType.Offline,
                PlayerPerks = PlayerPerkFlags.None,
            }
        );
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO player_chat_styles (id, client_style_id) VALUES (1, 30), (2, 4);
            INSERT INTO player_chat_styles_owned (id, player_id, chat_style_id) VALUES (10, 7, 1), (11, 7, 2);
            """
        );
    }

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public async Task The_detail_lists_owned_chat_styles_by_client_id()
    {
        PlayerRewardDetail? detail = await new PlayerRewardReads(
            new TestContextFactory(_options),
            new DashboardAssetUrls(Options.Create(new ObservabilityConfig()))
        ).PlayerRewardDetailAsync(7, CancellationToken.None);

        detail!.ChatStyles.Select(c => c.ClientStyleId).Should().Equal(4, 30);
    }

    private static readonly Regex TimestampDefault = new(
        """("(?:created_at|updated_at)"\s+\w+)\s+NOT NULL""",
        RegexOptions.Compiled
    );

    private sealed class TestContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }
}
