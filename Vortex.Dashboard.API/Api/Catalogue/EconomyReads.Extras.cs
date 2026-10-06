using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Dashboard.API.Api.Catalogue.Contracts;
using Vortex.Database.Context;

namespace Vortex.Dashboard.API.Api.Catalogue;

/// <summary>
/// The corners of the economy that have their own tables but no surface of their own: rentable
/// spaces and the currency catalogue.
/// <para>
/// Each is small enough that a single read serves the whole page; what matters is the pairing — a
/// rentable space is meaningless without whether anyone is renting it.
/// </para>
/// </summary>
internal sealed partial class EconomyReads
{
    public Task<EconomyExtras> EconomyExtrasAsync(CancellationToken ct) =>
        QueryAsync<EconomyExtras>(
            async db =>
            {
                DateTime now = DateTime.UtcNow;

                List<RentableSpaceTermRow> rentableTerms = await db
                    .RentableSpaceTerms.AsNoTracking()
                    .Select(t => new RentableSpaceTermRow(
                        t.Id,
                        t.FurnitureEntityId,
                        t.Price,
                        t.CurrencyTypeEntityId,
                        t.RentDurationSeconds,
                        t.RequiresHc
                    ))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                var rentals = await db
                    .RoomRentableSpaces.AsNoTracking()
                    .Select(r => new
                    {
                        r.Id,
                        r.FurnitureEntityId,
                        r.RenterPlayerEntityId,
                        r.RentedUntil,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                // A rentable space is a placed furni; showing "#412" makes the operator go and look
                // the id up somewhere else, so the definition's name and icon come along.
                List<int> rentedFurnitureIds = rentals.ConvertAll(r => r.FurnitureEntityId);

                Dictionary<int, string> rentedFurnitureNames = await db
                    .Furnitures.AsNoTracking()
                    .Where(f => rentedFurnitureIds.Contains(f.Id))
                    .Join(
                        db.FurnitureDefinitions.AsNoTracking(),
                        furniture => furniture.FurnitureDefinitionEntityId,
                        definition => definition.Id,
                        (furniture, definition) => new { furniture.Id, definition.Name }
                    )
                    .ToDictionaryAsync(x => x.Id, x => x.Name, ct)
                    .ConfigureAwait(false);

                Dictionary<int, string> renterNames = await db.PlayerNamesAsync(
                        DisplayNameQueries.NormalizeIds(
                            rentals.Select(r => r.RenterPlayerEntityId)
                        ),
                        ct
                    )
                    .ConfigureAwait(false);

                Dictionary<int, int> termsByFurniture = rentableTerms
                    .GroupBy(t => t.FurnitureEntityId)
                    .ToDictionary(g => g.Key, g => g.First().Id);

                List<RentableSpaceRow> rentableItems = rentals
                    .Select(r => new RentableSpaceRow(
                        r.Id,
                        r.FurnitureEntityId,
                        rentedFurnitureNames.GetValueOrDefault(r.FurnitureEntityId),
                        rentedFurnitureNames.TryGetValue(
                            r.FurnitureEntityId,
                            out string? rentedName
                        )
                            ? _assetUrls.FurniIcon(rentedName)
                            : null,
                        r.RenterPlayerEntityId,
                        r.RenterPlayerEntityId is { } renter
                            ? DisplayNameQueries.ResolvePlayerName(renterNames, renter)
                            : null,
                        r.RentedUntil,
                        r.RenterPlayerEntityId is not null
                            && r.RentedUntil is { } until
                            && until > now,
                        termsByFurniture.ContainsKey(r.FurnitureEntityId)
                    ))
                    .ToList();

                List<CurrencyTypeRow> currencies = await db
                    .CurrencyTypes.AsNoTracking()
                    .OrderBy(c => c.Id)
                    .Select(c => new CurrencyTypeRow(
                        c.Id,
                        c.Name,
                        c.CurrencyType.ToString(),
                        c.ActivityPointType,
                        c.Enabled,
                        c.StartingAmount,
                        db.PlayerCurrencies.Count(p => p.CurrencyTypeEntityId == c.Id),
                        db.PlayerCurrencies.Where(p => p.CurrencyTypeEntityId == c.Id)
                            .Sum(p => (long?)p.Amount)
                            ?? 0L
                    ))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                return new EconomyExtras(
                    new EconomyExtrasTotals(
                        rentableItems.Count,
                        rentableItems.Count(r => r.Rented),
                        rentableTerms.Count,
                        currencies.Count
                    ),
                    rentableItems,
                    rentableTerms,
                    currencies
                );
            },
            ct
        );
}
