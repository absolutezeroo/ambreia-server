using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Orleans.Snapshots.Navigator;
using Vortex.Primitives.Players.Grains;

namespace Vortex.Players.Grains;

internal sealed class PlayerNavigatorGrain(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    ILogger<PlayerNavigatorGrain> logger
) : Grain, IPlayerNavigatorGrain
{
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly ILogger<PlayerNavigatorGrain> _logger = logger;

    private NavigatorWindowPreferencesSnapshot _preferences = new();
    private int _homeRoomId;

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await HydrateAsync(ct);
    }

    public Task<NavigatorWindowPreferencesSnapshot> GetWindowPreferencesAsync(
        CancellationToken ct
    ) => Task.FromResult(_preferences);

    public async Task UpdateWindowPreferencesAsync(
        int x,
        int y,
        int width,
        int height,
        bool leftPaneHidden,
        int resultsMode,
        CancellationToken ct
    )
    {
        _preferences = new NavigatorWindowPreferencesSnapshot
        {
            WindowX = x,
            WindowY = y,
            WindowWidth = width,
            WindowHeight = height,
            LeftPaneHidden = leftPaneHidden,
            ResultsMode = resultsMode,
        };

        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        int playerId = (int)this.GetPrimaryKeyLong();

        PlayerNavigatorPreferencesEntity? existing = await dbCtx
            .PlayerNavigatorPreferences.FirstOrDefaultAsync(e => e.PlayerEntityId == playerId, ct)
            .ConfigureAwait(true);

        if (existing is null)
        {
            dbCtx.PlayerNavigatorPreferences.Add(
                new PlayerNavigatorPreferencesEntity
                {
                    PlayerEntityId = playerId,
                    WindowX = x,
                    WindowY = y,
                    WindowWidth = width,
                    WindowHeight = height,
                    LeftPaneHidden = leftPaneHidden,
                    ResultsMode = resultsMode,
                }
            );
        }
        else
        {
            existing.WindowX = x;
            existing.WindowY = y;
            existing.WindowWidth = width;
            existing.WindowHeight = height;
            existing.LeftPaneHidden = leftPaneHidden;
            existing.ResultsMode = resultsMode;
        }

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
    }

    public Task<int> GetHomeRoomIdAsync(CancellationToken ct) => Task.FromResult(_homeRoomId);

    public async Task SetHomeRoomIdAsync(int roomId, CancellationToken ct)
    {
        _homeRoomId = roomId;

        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        int playerId = (int)this.GetPrimaryKeyLong();

        PlayerNavigatorPreferencesEntity? existing = await dbCtx
            .PlayerNavigatorPreferences.FirstOrDefaultAsync(e => e.PlayerEntityId == playerId, ct)
            .ConfigureAwait(true);

        if (existing is null)
        {
            dbCtx.PlayerNavigatorPreferences.Add(
                new PlayerNavigatorPreferencesEntity
                {
                    PlayerEntityId = playerId,
                    HomeRoomId = roomId,
                }
            );
        }
        else
        {
            existing.HomeRoomId = roomId;
        }

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
    }

    private async Task HydrateAsync(CancellationToken ct)
    {
        try
        {
            await HydrateCoreAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to hydrate navigator preferences for player {PlayerId}.",
                this.GetPrimaryKeyLong()
            );

            throw;
        }
    }

    private async Task HydrateCoreAsync(CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        int playerId = (int)this.GetPrimaryKeyLong();

        PlayerNavigatorPreferencesEntity? prefsEntity = await dbCtx
            .PlayerNavigatorPreferences.AsNoTracking()
            .FirstOrDefaultAsync(e => e.PlayerEntityId == playerId, ct)
            .ConfigureAwait(true);

        _preferences = prefsEntity is null
            ? new NavigatorWindowPreferencesSnapshot()
            : new NavigatorWindowPreferencesSnapshot
            {
                WindowX = prefsEntity.WindowX,
                WindowY = prefsEntity.WindowY,
                WindowWidth = prefsEntity.WindowWidth,
                WindowHeight = prefsEntity.WindowHeight,
                LeftPaneHidden = prefsEntity.LeftPaneHidden,
                ResultsMode = prefsEntity.ResultsMode,
            };

        _homeRoomId = prefsEntity?.HomeRoomId ?? 0;
    }
}
