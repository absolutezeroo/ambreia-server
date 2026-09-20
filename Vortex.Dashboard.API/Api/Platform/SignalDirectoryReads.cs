using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Dashboard.API.Api.Platform.Contracts;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Database.Context;
using Vortex.Database.Entities.Catalog;
using Vortex.Database.Entities.Furniture;
using Vortex.Database.Entities.Groups;
using Vortex.Database.Entities.Habbicons;
using Vortex.Database.Entities.Navigator;
using Vortex.Database.Entities.Pets;
using Vortex.Database.Entities.Players;

namespace Vortex.Dashboard.API.Api.Platform;

/// <summary>
/// The directories behind the filter pickers.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these is a value an operator was previously expected to type into a filter from
/// memory: a guild id, a Habbicon id, a badge code, a catalogue offer, a navigator category, a pet
/// species. A typo in any of them produces a filter that is valid, saveable, and silently never
/// matches — the one failure this whole subsystem exists to make impossible, reappearing at the
/// last step because the value was free text.
/// </para>
/// <para>
/// They share the shape the picker already speaks: <c>{ id, name, … }</c> with <c>q</c>,
/// <c>limit</c>, <c>offset</c> and <c>hasMore</c>, so no new component is needed — only a new kind.
/// </para>
/// </remarks>
internal sealed class SignalDirectoryReads(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    FiguredataIndex figuredata,
    DashboardAssetUrls assets
) : DashboardReads(dbContextFactory)
{
    /// <summary>
    /// Wearables, from the client's own figuredata.
    /// </summary>
    /// <remarks>
    /// The one directory here with no table behind it, and the only one that can legitimately answer
    /// nothing: figuredata lives in the asset pack, and a hotel with none configured has no garments
    /// to offer. That reads as an empty picker rather than an error, because the filter it fills in
    /// is still perfectly writable by hand.
    /// <para>
    /// Searched by set id rather than by name, because there are no names. The type filter is what
    /// actually narrows three thousand entries down to something an operator can look through.
    /// </para>
    /// </remarks>
    public Task<FigureSetDirectoryPage> FigureSetsDirectoryAsync(NameValueCollection query)
    {
        ImmutableArray<FigureSet> all = figuredata.Sets();
        string term = (query["q"] ?? string.Empty).Trim();
        string type = (query["type"] ?? string.Empty).Trim();
        int limit = QueryValues.Limit(query["limit"], 50, 200);
        int offset = int.TryParse(query["offset"], out int parsed) ? Math.Max(0, parsed) : 0;

        IEnumerable<FigureSet> matching = all;

        if (type.Length > 0)
        {
            matching = matching.Where(s => string.Equals(s.Type, type, StringComparison.Ordinal));
        }

        if (term.Length > 0)
        {
            matching = matching.Where(s =>
                s.Id.ToString(CultureInfo.InvariantCulture).Contains(term, StringComparison.Ordinal)
                || string.Equals(s.Type, term, StringComparison.OrdinalIgnoreCase)
            );
        }

        List<FigureSet> ordered =
        [
            .. matching.OrderBy(s => s.Type, StringComparer.Ordinal).ThenBy(s => s.Id),
        ];

        List<FigureSetDirectoryRow> items =
        [
            .. ordered
                .Skip(offset)
                .Take(limit)
                .Select(s => new FigureSetDirectoryRow(
                    s.Id,
                    s.Type,
                    s.Club,
                    s.Gender,
                    assets.FigureSetImage(s.Type, s.Id, s.Colour)
                )),
        ];

        List<string> types =
        [
            .. all.Select(s => s.Type)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        return Task.FromResult(
            new FigureSetDirectoryPage(
                items.Count,
                ordered.Count,
                offset,
                offset + items.Count < ordered.Count,
                types,
                items
            )
        );
    }

    /// <summary>Guilds, by name or id.</summary>
    public Task<DirectoryPage> GroupsDirectoryAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        PickerPageAsync(
            query,
            db => db.Groups.AsNoTracking(),
            (groups, term, id) =>
                groups.Where(g => g.Name.Contains(term) || (id != null && g.Id == id)),
            groups => groups.OrderBy(g => g.Name),
            g => new PickerRow(g.Id, g.Name, g.Description),
            ct
        );

    /// <summary>Habbicons, by code or id.</summary>
    public Task<DirectoryPage> HabbiconsDirectoryAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        PickerPageAsync(
            query,
            db => db.Habbicons.AsNoTracking().Where(h => h.Enabled),
            (icons, term, id) =>
                icons.Where(h => h.Code.Contains(term) || (id != null && h.Id == id)),
            icons => icons.OrderBy(h => h.SortOrder).ThenBy(h => h.Code),
            h => new PickerRow(h.Id, h.Code, null),
            ct
        );

    /// <summary>Habbicon collections. The value a filter stores is the code, not the id.</summary>
    public Task<DirectoryPage> HabbiconCollectionsDirectoryAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        PickerPageAsync(
            query,
            db => db.HabbiconCollections.AsNoTracking().Where(c => c.Enabled),
            (collections, term, id) =>
                collections.Where(c => c.Code.Contains(term) || (id != null && c.Id == id)),
            collections => collections.OrderBy(c => c.SortOrder).ThenBy(c => c.Code),
            // The signal carries the code, so that is what the picker must hand back as the value.
            c => new PickerRow(c.Id, c.Code, null) { Value = c.Code },
            ct
        );

    /// <summary>Catalogue offers, by localization id.</summary>
    public Task<DirectoryPage> CatalogOffersDirectoryAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        PickerPageAsync(
            query,
            db => db.CatalogOffers.AsNoTracking(),
            (offers, term, id) =>
                offers.Where(o => o.LocalizationId.Contains(term) || (id != null && o.Id == id)),
            offers => offers.OrderBy(o => o.LocalizationId),
            o => new PickerRow(o.Id, o.LocalizationId, null),
            ct
        );

    /// <summary>Navigator flat categories.</summary>
    public Task<DirectoryPage> NavigatorCategoriesDirectoryAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        PickerPageAsync(
            query,
            db => db.NavigatorFlatCategories.AsNoTracking().Where(c => c.Visible),
            (categories, term, id) =>
                categories.Where(c => c.Name.Contains(term) || (id != null && c.Id == id)),
            categories => categories.OrderBy(c => c.Name),
            c => new PickerRow(c.Id, c.Name, null),
            ct
        );

    /// <summary>
    /// Badge codes actually in circulation.
    /// </summary>
    /// <remarks>
    /// There is no badge catalogue table — a badge exists because somebody was granted one — so this
    /// reads the distinct codes players hold. It is therefore a list of badges that have been
    /// awarded at least once, which is exactly the set a task can sensibly ask for: a code nobody
    /// has ever held would be a filter nobody can satisfy.
    /// </remarks>
    public Task<CodeDirectoryPage> BadgesDirectoryAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        QueryAsync<CodeDirectoryPage>(
            async db =>
            {
                string term = (query["q"] ?? string.Empty).Trim();
                int limit = QueryValues.Limit(query["limit"], 50, 200);
                int offset = int.TryParse(query["offset"], out int parsed)
                    ? Math.Max(0, parsed)
                    : 0;

                IQueryable<string> codes = db
                    .PlayerBadges.AsNoTracking()
                    .Select(b => b.BadgeCode)
                    .Distinct();

                if (term.Length > 0)
                {
                    codes = codes.Where(c => c.Contains(term));
                }

                int total = await codes.CountAsync(ct).ConfigureAwait(false);

                List<string> page = await codes
                    .OrderBy(c => c)
                    .Skip(offset)
                    .Take(limit)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<CodeDirectoryRow> items = page.Select(code => new CodeDirectoryRow(
                        code,
                        code,
                        code,
                        null
                    ))
                    .ToList();

                return new CodeDirectoryPage(
                    items.Count,
                    total,
                    offset,
                    offset + items.Count < total,
                    items
                );
            },
            ct
        );

    /// <summary>
    /// Pet species, from the palettes that define them.
    /// </summary>
    /// <remarks>
    /// A species is not a row of its own: it is the distinct <c>PetType</c> across the palettes the
    /// hotel ships, which is what a task filtering on "a pet of species N" compares against.
    /// </remarks>
    public Task<DirectoryPage> PetSpeciesDirectoryAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        QueryAsync<DirectoryPage>(
            async db =>
            {
                string term = (query["q"] ?? string.Empty).Trim();

                List<int> types = await db
                    .PetPalettes.AsNoTracking()
                    .Select(p => p.PetType)
                    .Distinct()
                    .OrderBy(t => t)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<DirectoryRow> items = types
                    .Where(t =>
                        term.Length == 0 || t.ToString().Contains(term, StringComparison.Ordinal)
                    )
                    .Select(t => new DirectoryRow(t, t.ToString(), $"Species {t}", null))
                    .ToList();

                return new DirectoryPage(items.Count, items.Count, 0, false, items);
            },
            ct
        );

    /// <summary>Polls, by code. A filter stores the code, so that is what the row hands back.</summary>
    public Task<DirectoryPage> PollsDirectoryAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        PickerPageAsync(
            query,
            db => db.Polls.AsNoTracking(),
            (polls, term, id) =>
                polls.Where(p => p.Code.Contains(term) || (id != null && p.Id == id)),
            polls => polls.OrderBy(p => p.Code),
            p => new PickerRow(p.Id, p.Code, null) { Value = p.Code },
            ct
        );

    /// <summary>Quizzes, by code.</summary>
    public Task<DirectoryPage> QuizzesDirectoryAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        PickerPageAsync(
            query,
            db => db.Quizzes.AsNoTracking(),
            (quizzes, term, id) =>
                quizzes.Where(q => q.Code.Contains(term) || (id != null && q.Id == id)),
            quizzes => quizzes.OrderBy(q => q.Code),
            q => new PickerRow(q.Id, q.Code, null) { Value = q.Code },
            ct
        );

    /// <summary>
    /// Quest campaigns, by code.
    /// </summary>
    /// <remarks>
    /// A campaign is not a row of its own — quests carry the code — so this is the distinct set,
    /// which is exactly what a filter on "a quest from campaign X" compares against.
    /// </remarks>
    public Task<CodeDirectoryPage> QuestCampaignsDirectoryAsync(
        NameValueCollection query,
        CancellationToken ct
    ) => DistinctCodesAsync(query, db => db.Quests.AsNoTracking().Select(q => q.CampaignCode), ct);

    /// <summary>Vouchers, by code.</summary>
    public Task<DirectoryPage> VouchersDirectoryAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        PickerPageAsync(
            query,
            db => db.Vouchers.AsNoTracking(),
            (vouchers, term, id) =>
                vouchers.Where(v => v.Code.Contains(term) || (id != null && v.Id == id)),
            vouchers => vouchers.OrderBy(v => v.Code),
            v => new PickerRow(v.Id, v.Code, null) { Value = v.Code },
            ct
        );

    /// <summary>Club gifts, by product code.</summary>
    public Task<DirectoryPage> ClubGiftsDirectoryAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        PickerPageAsync(
            query,
            db => db.CatalogClubGifts.AsNoTracking(),
            (gifts, term, id) =>
                gifts.Where(g => g.ProductCode.Contains(term) || (id != null && g.Id == id)),
            gifts => gifts.OrderBy(g => g.ProductCode),
            g => new PickerRow(g.Id, g.ProductCode, null) { Value = g.ProductCode },
            ct
        );

    /// <summary>Collectibles-store offers, by product code.</summary>
    public Task<DirectoryPage> NftStoreDirectoryAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        PickerPageAsync(
            query,
            db => db.NftStoreOffers.AsNoTracking(),
            (offers, term, id) =>
                offers.Where(o => o.ProductCode.Contains(term) || (id != null && o.Id == id)),
            offers => offers.OrderBy(o => o.ProductCode),
            o => new PickerRow(o.Id, o.ProductCode, null) { Value = o.ProductCode },
            ct
        );

    /// <summary>Targeted offers, by identifier.</summary>
    public Task<DirectoryPage> TargetedOffersDirectoryAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        PickerPageAsync(
            query,
            db => db.TargetedOffers.AsNoTracking(),
            (offers, term, id) =>
                offers.Where(o =>
                    o.Identifier.Contains(term)
                    || o.Title.Contains(term)
                    || (id != null && o.Id == id)
                ),
            offers => offers.OrderBy(o => o.Identifier),
            o => new PickerRow(o.Id, o.Identifier, o.Title) { Value = o.Identifier },
            ct
        );

    /// <summary>
    /// Forum threads, by subject.
    /// </summary>
    /// <remarks>
    /// A thread id looks like a live id and is not: threads outlive the conversation, so "post on
    /// the announcements thread" is a task worth writing and a number nobody can be asked to recall.
    /// </remarks>
    public Task<DirectoryPage> ForumThreadsDirectoryAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        PickerPageAsync(
            query,
            db => db.GroupForumThreads.AsNoTracking(),
            (threads, term, id) =>
                threads.Where(t => t.Subject.Contains(term) || (id != null && t.Id == id)),
            threads => threads.OrderByDescending(t => t.PostCount).ThenBy(t => t.Subject),
            t => new PickerRow(t.Id, t.Subject, null),
            ct
        );

    /// <summary>
    /// Avatar effects in circulation.
    /// </summary>
    /// <remarks>
    /// Like badges, there is no catalogue table: an effect exists here because a player holds one.
    /// The distinct set is therefore the only honest list, and it is also the only useful one — an
    /// effect nobody has is a filter nobody satisfies.
    /// </remarks>
    public Task<DirectoryPage> AvatarEffectsDirectoryAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        DistinctNumbersAsync(
            query,
            db => db.PlayerEffects.AsNoTracking().Select(e => e.EffectId),
            id => $"Effect {id}",
            ct
        );

    /// <summary>
    /// Placed furniture, by the definition it is an instance of.
    /// </summary>
    /// <remarks>
    /// The one directory whose rows are genuinely live: this is a specific sofa in a specific room,
    /// not a kind of sofa. Useful for a hand-written task about one landmark object, and shown with
    /// its room so two identical sofas can be told apart -- which an id alone never allows.
    /// </remarks>
    public Task<DirectoryPage> PlacedFurnitureDirectoryAsync(
        NameValueCollection query,
        CancellationToken ct
    ) =>
        QueryAsync<DirectoryPage>(
            async db =>
            {
                string term = (query["q"] ?? string.Empty).Trim();
                int limit = QueryValues.Limit(query["limit"], 50, 200);
                int offset = int.TryParse(query["offset"], out int parsed)
                    ? Math.Max(0, parsed)
                    : 0;

                // Placed only: an item in somebody's hand is not somewhere a task can point at.
                IQueryable<FurnitureEntity> items = db
                    .Furnitures.AsNoTracking()
                    .Where(f => f.RoomEntityId != null);

                if (term.Length > 0)
                {
                    items = int.TryParse(term, out int id)
                        ? items.Where(f => f.Id == id || f.RoomEntityId == id)
                        : items.Where(f => f.FurnitureDefinitionEntity!.Name.Contains(term));
                }

                int total = await items.CountAsync(ct).ConfigureAwait(false);

                var page = await items
                    .OrderBy(f => f.Id)
                    .Skip(offset)
                    .Take(limit)
                    .Select(f => new
                    {
                        f.Id,
                        Name = f.FurnitureDefinitionEntity!.Name,
                        f.RoomEntityId,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<DirectoryRow> items2 = page.Select(f => new DirectoryRow(
                        f.Id,
                        f.Id.ToString(),
                        f.Name,
                        $"room #{f.RoomEntityId}"
                    ))
                    .ToList();

                return new DirectoryPage(
                    items2.Count,
                    total,
                    offset,
                    offset + items2.Count < total,
                    items2
                );
            },
            ct
        );

    /// <summary>Distinct numeric ids in circulation, named for a human.</summary>
    private Task<DirectoryPage> DistinctNumbersAsync(
        NameValueCollection query,
        Func<Database.Context.VortexDbContext, IQueryable<int>> source,
        Func<int, string> name,
        CancellationToken ct
    ) =>
        QueryAsync<DirectoryPage>(
            async db =>
            {
                string term = (query["q"] ?? string.Empty).Trim();

                List<int> ids = await source(db)
                    .Distinct()
                    .OrderBy(v => v)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<DirectoryRow> items = ids.Where(v =>
                        term.Length == 0 || v.ToString().Contains(term, StringComparison.Ordinal)
                    )
                    .Select(v => new DirectoryRow(v, v.ToString(), name(v), null))
                    .ToList();

                return new DirectoryPage(items.Count, items.Count, 0, false, items);
            },
            ct
        );

    /// <summary>What a picker row is, whatever table it came from.</summary>
    private sealed record PickerRow(int Id, string Name, string? Description)
    {
        /// <summary>What a filter stores. The id, unless the signal carries something else.</summary>
        public string? Value { get; init; }
    }

    /// <summary>
    /// A directory whose values are distinct strings rather than rows: a badge nobody was granted
    /// and a campaign no quest belongs to are both filters nobody can satisfy.
    /// </summary>
    private Task<CodeDirectoryPage> DistinctCodesAsync(
        NameValueCollection query,
        Func<Database.Context.VortexDbContext, IQueryable<string>> source,
        CancellationToken ct
    ) =>
        QueryAsync<CodeDirectoryPage>(
            async db =>
            {
                string term = (query["q"] ?? string.Empty).Trim();
                int limit = QueryValues.Limit(query["limit"], 50, 200);
                int offset = int.TryParse(query["offset"], out int parsed)
                    ? Math.Max(0, parsed)
                    : 0;

                IQueryable<string> codes = source(db).Distinct();

                if (term.Length > 0)
                {
                    codes = codes.Where(c => c.Contains(term));
                }

                int total = await codes.CountAsync(ct).ConfigureAwait(false);

                List<string> page = await codes
                    .OrderBy(c => c)
                    .Skip(offset)
                    .Take(limit)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<CodeDirectoryRow> items = page.Select(code => new CodeDirectoryRow(
                        code,
                        code,
                        code,
                        null
                    ))
                    .ToList();

                return new CodeDirectoryPage(
                    items.Count,
                    total,
                    offset,
                    offset + items.Count < total,
                    items
                );
            },
            ct
        );

    /// <summary>
    /// One paged, searchable picker query, so the row-backed directories are one expression each
    /// rather than a dozen copies of the same twenty lines.
    /// </summary>
    private Task<DirectoryPage> PickerPageAsync<TEntity>(
        NameValueCollection query,
        Func<Database.Context.VortexDbContext, IQueryable<TEntity>> source,
        Func<IQueryable<TEntity>, string, int?, IQueryable<TEntity>> search,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> order,
        Func<TEntity, PickerRow> project,
        CancellationToken ct
    ) =>
        QueryAsync<DirectoryPage>(
            async db =>
            {
                string term = (query["q"] ?? string.Empty).Trim();
                int limit = QueryValues.Limit(query["limit"], 50, 200);
                int offset = int.TryParse(query["offset"], out int parsed)
                    ? Math.Max(0, parsed)
                    : 0;

                IQueryable<TEntity> rows = source(db);

                if (term.Length > 0)
                {
                    rows = search(rows, term, int.TryParse(term, out int id) ? id : null);
                }

                int total = await rows.CountAsync(ct).ConfigureAwait(false);

                List<TEntity> page = await order(rows)
                    .Skip(offset)
                    .Take(limit)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                List<DirectoryRow> items = page.Select(project)
                    .Select(r => new DirectoryRow(
                        r.Id,
                        r.Value ?? r.Id.ToString(),
                        r.Name,
                        r.Description
                    ))
                    .ToList();

                return new DirectoryPage(
                    items.Count,
                    total,
                    offset,
                    offset + items.Count < total,
                    items
                );
            },
            ct
        );
}
