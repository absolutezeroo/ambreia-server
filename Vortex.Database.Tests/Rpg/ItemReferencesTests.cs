using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Rpg;
using Vortex.Database.Tests.Support;
using Vortex.Primitives.Rpg.Items.Enums;
using Vortex.Rpg.Characters.Content;
using Xunit;

namespace Vortex.Database.Tests.Rpg;

/// <summary>Characters hold item definitions (spec Objets §11): a removed definition needs a
/// replace rule, and the rule moves every copy.</summary>
public sealed class ItemReferencesTests : IAsyncLifetime
{
    private const string Sword = "item.weapon.rusted_sword";

    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;

    public async Task InitializeAsync() => (_conn, _options) = await SqliteTestDb.OpenAsync();

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public async Task AHeldItem_IsReferenced()
    {
        await StoreAsync(Sword, deleted: false);
        await using VortexDbContext db = new(_options);

        (await new ItemReferences().IsReferencedAsync(db, Sword, CancellationToken.None))
            .Should()
            .BeTrue();
        new ItemReferences().Kind.Should().Be("item");
    }

    [Fact]
    public async Task ADeletedCopy_IsNot()
    {
        await StoreAsync(Sword, deleted: true);
        await using VortexDbContext db = new(_options);

        (await new ItemReferences().IsReferencedAsync(db, Sword, CancellationToken.None))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task AReplace_RewritesTheDefinitionOfEveryCopy()
    {
        await StoreAsync(Sword, deleted: false);
        await StoreAsync(Sword, deleted: false);
        await StoreAsync("item.ring.copper_ring", deleted: false);
        await using VortexDbContext db = new(_options);

        await new ItemReferences().ReplaceAsync(
            db,
            Sword,
            "item.weapon.oak_mace",
            CancellationToken.None
        );

        await using VortexDbContext read = new(_options);
        (await read.RpgItems.Select(i => i.DefinitionId).OrderBy(d => d).ToListAsync())
            .Should()
            .Equal("item.ring.copper_ring", "item.weapon.oak_mace", "item.weapon.oak_mace");
    }

    private async Task StoreAsync(string definitionId, bool deleted)
    {
        await using VortexDbContext db = new(_options);
        db.RpgItems.Add(
            new RpgItemEntity
            {
                PlayerEntityId = 7,
                DefinitionId = definitionId,
                Quantity = 1,
                Container = ItemContainer.Bag,
                DeletedAt = deleted ? DateTime.UtcNow : null,
            }
        );
        await db.SaveChangesAsync();
    }
}
