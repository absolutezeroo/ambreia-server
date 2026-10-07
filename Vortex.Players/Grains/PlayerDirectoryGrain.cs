using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Context;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;

namespace Vortex.Players.Grains;

[KeepAlive]
internal class PlayerDirectoryGrain(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    ILogger<PlayerDirectoryGrain> logger
) : Grain, IPlayerDirectoryGrain
{
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly ILogger<PlayerDirectoryGrain> _logger = logger;

    private readonly Dictionary<PlayerId, string> _idToName = [];

    public async Task<string> GetPlayerNameAsync(PlayerId playerId, CancellationToken ct)
    {
        if (_idToName.TryGetValue(playerId, out string? x))
        {
            return x;
        }

        string? dbName;

        try
        {
            await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

            // An id nobody holds is an answer -- no name -- not a fault. FirstAsync made it an
            // InvalidOperationException, logged at error level and thrown at whoever asked, for a
            // lookup the many-ids path below answers by simply leaving the id out.
            dbName = await dbCtx
                .Players.AsNoTracking()
                .Where(x => x.Id == (int)playerId)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve name for player {PlayerId}.", playerId);

            throw;
        }

        if (string.IsNullOrWhiteSpace(dbName))
        {
            return string.Empty;
        }

        SetNameCache(playerId, dbName);

        return dbName;
    }

    public async Task<ImmutableDictionary<PlayerId, string>> GetPlayerNamesAsync(
        List<PlayerId> playerIds,
        CancellationToken ct
    )
    {
        Dictionary<PlayerId, string> names = new Dictionary<PlayerId, string>();

        if (playerIds.Count == 1)
        {
            PlayerId singleId = playerIds[0];
            string singleName = await GetPlayerNameAsync(singleId, ct);

            if (singleName.Length > 0)
            {
                names.TryAdd(singleId, singleName);
            }
        }
        else
        {
            List<PlayerId> ids = playerIds.Distinct().ToList();
            List<PlayerId> notFound = new List<PlayerId>();

            foreach (PlayerId playerId in ids)
            {
                if (_idToName.TryGetValue(playerId, out string? name))
                {
                    names.TryAdd(playerId, name);
                }
                else
                {
                    notFound.Add(playerId);
                }
            }

            if (notFound.Count > 0)
            {
                Dictionary<int, string> players;

                try
                {
                    await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(
                        ct
                    );

                    players = await dbCtx
                        .Players.AsNoTracking()
                        .Where(x => notFound.Select(x => (int)x).Contains(x.Id))
                        .Select(x => new { x.Id, x.Name })
                        .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to bulk-resolve names for {Count} players.",
                        notFound.Count
                    );

                    throw;
                }

                foreach (KeyValuePair<int, string> player in players)
                {
                    SetNameCache(player.Key, player.Value);

                    names.TryAdd(player.Key, player.Value);
                }
            }
        }

        return names.ToImmutableDictionary();
    }

    public Task SetPlayerNameAsync(PlayerId playerId, string name, CancellationToken ct)
    {
        SetNameCache(playerId, name);

        return Task.CompletedTask;
    }

    private void SetNameCache(PlayerId playerId, string name)
    {
        _idToName[playerId] = name;
    }
}
