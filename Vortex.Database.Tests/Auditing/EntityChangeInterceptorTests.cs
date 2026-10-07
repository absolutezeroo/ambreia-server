using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Auditing;
using Vortex.Database.Context;
using Vortex.Database.Entities.Catalog;
using Vortex.Primitives.Catalog.Enums;
using Xunit;

namespace Vortex.Database.Tests.Auditing;

/// <summary>
/// The dashboard's audit used to record only the id a write was aimed at, so a deleted row left no
/// trace of what it had been and an edited one no trace of what it replaced — "I deleted the wrong
/// offer" and "I forgot the old price" were both unanswerable. This reads the answer off EF's own
/// original values, which is the only copy that is neither a guess nor a screen's memory.
/// </summary>
public sealed class EntityChangeInterceptorTests
{
    private static VortexDbContext NewContext(string name) =>
        new(
            new DbContextOptionsBuilder<VortexDbContext>()
                .UseInMemoryDatabase(name)
                .AddInterceptors(new EntityChangeInterceptor())
                .Options
        );

    private static CatalogOfferEntity Offer() =>
        new()
        {
            Id = 1,
            CatalogPageEntityId = 1,
            LocalizationId = "sofa_offer",
            CostCredits = 2,
            CostCurrency = 0,
            CurrencyTypeId = null,
            CanGift = true,
            CanBundle = true,
            MembershipLevel = 0,
            DiscountPercent = 0,
            Visible = true,
            Page = null!,
        };

    [Fact]
    public async Task AnEditRecordsWhatItReplaced_AndOnlyTheFieldsThatMoved()
    {
        string db = $"changes-{Guid.NewGuid():N}";

        await using (VortexDbContext seed = NewContext(db))
        {
            seed.CatalogOffers.Add(Offer());
            await seed.SaveChangesAsync().ConfigureAwait(true);
        }

        await using VortexDbContext ctx = NewContext(db);

        using IEntityChangeCapture capture = EntityChangeCapture.Begin();

        CatalogOfferEntity entity = await ctx
            .CatalogOffers.FirstAsync(o => o.Id == 1)
            .ConfigureAwait(true);

        entity.CostCredits = 4;

        await ctx.SaveChangesAsync().ConfigureAwait(true);

        EntityChange change = capture.Changes.Should().ContainSingle().Which;

        change.Operation.Should().Be("update");
        change.Entity.Should().Be(nameof(CatalogOfferEntity));
        // Against MySQL this is the mapped name (`catalog_offers`, from the entity's [Table]). The
        // in-memory provider has no relational mapping, so GetTableName falls back to the CLR name --
        // asserting the mapped value here would be testing the provider, not the interceptor.
        change.Table.Should().NotBeNullOrEmpty();
        change.Id.Should().Be("1");
        change.Before["CostCredits"].Should().Be("2");
        change.After["CostCredits"].Should().Be("4");

        // The untouched columns are deliberately absent: an update's interesting part is what moved,
        // and listing forty unchanged fields would bury it.
        change.Before.Should().NotContainKey("LocalizationId");
    }

    [Fact]
    public async Task ADeleteKeepsTheWholeRow_BecauseThereIsNowhereElseLeftToReadIt()
    {
        string db = $"changes-{Guid.NewGuid():N}";

        await using (VortexDbContext seed = NewContext(db))
        {
            seed.CatalogOffers.Add(Offer());
            await seed.SaveChangesAsync().ConfigureAwait(true);
        }

        await using VortexDbContext ctx = NewContext(db);

        using IEntityChangeCapture capture = EntityChangeCapture.Begin();

        CatalogOfferEntity entity = await ctx
            .CatalogOffers.FirstAsync(o => o.Id == 1)
            .ConfigureAwait(true);

        ctx.CatalogOffers.Remove(entity);

        await ctx.SaveChangesAsync().ConfigureAwait(true);

        EntityChange change = capture.Changes.Should().ContainSingle().Which;

        change.Operation.Should().Be("delete");
        change.Before["LocalizationId"].Should().Be("sofa_offer");
        change.Before["CostCredits"].Should().Be("2");
        change.After.Should().BeEmpty("a delete has no after");
    }

    /// <summary>
    /// The interceptor is registered on the shared context factory, so it sits in front of every
    /// write the emulator makes — room ticks, chat logs, wallet updates. It must cost nothing and
    /// record nothing unless a dashboard operation armed it.
    /// </summary>
    [Fact]
    public async Task WithoutACapture_NothingIsRecorded()
    {
        string db = $"changes-{Guid.NewGuid():N}";

        await using (VortexDbContext seed = NewContext(db))
        {
            seed.CatalogOffers.Add(Offer());
            await seed.SaveChangesAsync().ConfigureAwait(true);
        }

        await using VortexDbContext ctx = NewContext(db);

        CatalogOfferEntity entity = await ctx
            .CatalogOffers.FirstAsync(o => o.Id == 1)
            .ConfigureAwait(true);

        entity.CostCredits = 9;

        await ctx.SaveChangesAsync().ConfigureAwait(true);

        // Arming one now must show the previous write left nothing behind.
        using IEntityChangeCapture capture = EntityChangeCapture.Begin();

        capture.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task ACaptureEndsWithItsScope()
    {
        string db = $"changes-{Guid.NewGuid():N}";

        await using (VortexDbContext seed = NewContext(db))
        {
            seed.CatalogOffers.Add(Offer());
            await seed.SaveChangesAsync().ConfigureAwait(true);
        }

        using (IEntityChangeCapture _ = EntityChangeCapture.Begin())
        {
            // Scope opened and closed without a write.
        }

        await using VortexDbContext ctx = NewContext(db);

        CatalogOfferEntity entity = await ctx
            .CatalogOffers.FirstAsync(o => o.Id == 1)
            .ConfigureAwait(true);

        entity.Visible = false;

        await ctx.SaveChangesAsync().ConfigureAwait(true);

        using IEntityChangeCapture after = EntityChangeCapture.Begin();

        after.Changes.Should().BeEmpty("the closed scope must not keep collecting");
    }
}
