using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Dashboard.API.Api.Catalogue.Contracts;
using Vortex.Database.Context;
using Vortex.Primitives.Sound;

namespace Vortex.Dashboard.API.Api.Catalogue;

/// <summary>
/// The song catalogue, read for the page that edits it. The writes live in
/// <see cref="Operations.SongOperations"/>.
/// </summary>
/// <remarks>
/// Every song is joined to two counts the operator would otherwise have to guess at: how many disks
/// in the hotel carry it, and how many of those are loaded into a jukebox right now. A song with no
/// disks is one nobody can hear, which is the usual reason a freshly added song "does not work".
/// </remarks>
internal sealed class SongReads(IDbContextFactory<VortexDbContext> dbContextFactory)
    : DashboardReads(dbContextFactory)
{
    private const int SongsPageSize = 50;

    public Task<SongListResponse> SongsAsync(NameValueCollection query, CancellationToken ct) =>
        QueryAsync<SongListResponse>(
            async db =>
            {
                string search = (query["search"] ?? string.Empty).Trim();
                int page = Math.Max(1, QueryValues.Int(query["page"], 1));

                IQueryable<Database.Entities.Gamedata.SongEntity> rows = db
                    .Songs.AsNoTracking()
                    .Where(s => s.DeletedAt == null);

                if (search.Length > 0)
                {
                    rows = rows.Where(s =>
                        s.Name.Contains(search)
                        || s.Creator.Contains(search)
                        || s.OfficialSongId.Contains(search)
                    );
                }

                int total = await rows.CountAsync(ct).ConfigureAwait(false);

                var songs = await rows.OrderBy(s => s.Id)
                    .Skip((page - 1) * SongsPageSize)
                    .Take(SongsPageSize)
                    .Select(s => new
                    {
                        s.Id,
                        s.Name,
                        s.Creator,
                        s.LengthMs,
                        s.OfficialSongId,
                        s.Data,
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                // A disk is a furniture row whose definition runs the song-disk logic and whose
                // extra data names the song. The id lives inside the stuff-data blob, so it cannot
                // be grouped in SQL: the disks are read and counted here. The set is bounded by how
                // many song disks the hotel has minted, not by the furniture table -- and a hotel
                // with enough of them for this to hurt has a page worth paginating differently.
                var disks = await db
                    .Furnitures.AsNoTracking()
                    .Where(f =>
                        f.DeletedAt == null
                        && f.FurnitureDefinitionEntity != null
                        && f.FurnitureDefinitionEntity.Logic == SoundLogicNames.SongDisk
                    )
                    .Select(f => f.ExtraData)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                Dictionary<int, int> counts = [];

                foreach (string? disk in disks)
                {
                    int songId = SongIdFromExtraData(disk);

                    if (songId <= 0)
                    {
                        continue;
                    }

                    counts[songId] = counts.GetValueOrDefault(songId) + 1;
                }

                return new SongListResponse(
                    total,
                    page,
                    SongsPageSize,
                    [
                        .. songs.Select(s => new SongListItem(
                            s.Id,
                            s.Name,
                            s.Creator,
                            s.LengthMs,
                            Math.Round(s.LengthMs / 1000.0, 1),
                            s.OfficialSongId,
                            s.Data,
                            counts.GetValueOrDefault(s.Id),
                            // Jukeboxes left with M0; the field stays until the dashboard drops it.
                            0
                        )),
                    ]
                );
            },
            ct
        );

    /// <summary>
    /// The song id inside a disk's extra-data blob. The blob is JSON with a stuff section holding
    /// the legacy string, and the id is that string as a number — the same thing the client reads.
    /// </summary>
    private static int SongIdFromExtraData(string? extraData)
    {
        if (string.IsNullOrEmpty(extraData))
        {
            return 0;
        }

        try
        {
            System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(extraData);

            foreach (
                System.Text.Json.JsonProperty section in document.RootElement.EnumerateObject()
            )
            {
                if (
                    section.Value.ValueKind == System.Text.Json.JsonValueKind.Object
                    && section.Value.TryGetProperty("Data", out System.Text.Json.JsonElement data)
                    && data.ValueKind == System.Text.Json.JsonValueKind.String
                )
                {
                    return SongDiskExtraData.ReadSongId(data.GetString());
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // A blob that does not parse is a disk carrying nothing this reads; it is counted
            // against no song rather than failing the page.
        }

        return 0;
    }
}
