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
/// The corners of the economy that have their own tables but no surface of their own: today the
/// currency catalogue.
/// </summary>
internal sealed partial class EconomyReads
{
    public Task<EconomyExtras> EconomyExtrasAsync(CancellationToken ct) =>
        QueryAsync<EconomyExtras>(
            async db =>
            {
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

                return new EconomyExtras(new EconomyExtrasTotals(currencies.Count), currencies);
            },
            ct
        );
}
