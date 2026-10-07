using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Logging;
using Vortex.Primitives;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Players.Grains;

/// <summary>A player's card — name, motto, look — loaded from its row and written back on change.</summary>
internal sealed class PlayerGrain : Grain, IPlayerGrain
{
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory;
    private readonly IEventPublisher _events;
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<PlayerGrain> _logger;
    private readonly IPermissionService _permissions;

    private readonly PlayerLiveState _state;

    public PlayerGrain(
        IDbContextFactory<VortexDbContext> dbCtxFactory,
        IGrainFactory grainFactory,
        IEventPublisher events,
        ILogger<PlayerGrain> logger,
        IPermissionService permissions
    )
    {
        _dbCtxFactory = dbCtxFactory;
        _grainFactory = grainFactory;
        _events = events;
        _logger = logger;
        _permissions = permissions;

        _state = new PlayerLiveState { PlayerId = PlayerId.Parse((int)this.GetPrimaryKeyLong()) };
    }

    public async Task SetFigureAsync(string figure, AvatarGenderType gender, CancellationToken ct)
    {
        _state.Figure = figure;
        _state.Gender = gender;

        await WriteToDatabaseAsync(ct);

        await _grainFactory
            .GetPlayerPresenceGrain((int)_state.PlayerId)
            .OnFigureUpdatedAsync(await GetSummaryAsync(ct), ct);

        await _events
            .PublishAsync(new PlayerFigureChangedEvent(_state.PlayerId, figure), ct)
            .ConfigureAwait(true);
    }

    public async Task SetNameAsync(string name, CancellationToken ct)
    {
        // Captured before the write: the old name is what a report, a chatlog or a ban appeal
        // actually names, and after this line nothing else remembers it.
        string previousName = _state.Name;
        _state.Name = name;

        await WriteToDatabaseAsync(ct);

        await _grainFactory.GetPlayerDirectoryGrain().SetPlayerNameAsync(_state.PlayerId, name, ct);

        await _grainFactory
            .GetPlayerPresenceGrain((int)_state.PlayerId)
            .OnPlayerUpdatedAsync(await GetSummaryAsync(ct), ct);

        await _events
            .PublishAsync(new PlayerNameChangedEvent(_state.PlayerId, previousName, name), ct)
            .ConfigureAwait(true);
    }

    public async Task SetMottoAsync(string text, CancellationToken ct)
    {
        _state.Motto = text;

        await WriteToDatabaseAsync(ct);

        await _grainFactory
            .GetPlayerPresenceGrain((int)_state.PlayerId)
            .OnPlayerUpdatedAsync(await GetSummaryAsync(ct), ct);

        await _events
            .PublishAsync(new PlayerMottoChangedEvent(_state.PlayerId, text), ct)
            .ConfigureAwait(true);
    }

    public Task<PlayerSummarySnapshot> GetSummaryAsync(CancellationToken ct) =>
        Task.FromResult(
            new PlayerSummarySnapshot
            {
                PlayerId = _state.PlayerId,
                Name = _state.Name,
                Motto = _state.Motto,
                Figure = _state.Figure,
                Gender = _state.Gender,
                AchievementScore = _state.AchievementScore,
                CreatedAt = _state.CreatedAt,
                IsModerator = _state.IsModerator,
            }
        );

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await HydrateAsync(ct);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
    {
        await WriteToDatabaseAsync(ct);
    }

    private async Task HydrateAsync(CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        PlayerEntity entity =
            await dbCtx
                .Players.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == (int)_state.PlayerId, ct)
            ?? throw new VortexException(VortexErrorCodeEnum.PlayerNotFound);

        _state.Name = entity.Name;
        _state.Motto = entity.Motto ?? string.Empty;
        _state.Figure = entity.Figure;
        _state.Gender = entity.Gender;
        _state.AchievementScore = entity.AchievementScore;
        _state.CreatedAt = entity.CreatedAt;
        _state.LastUpdated = entity.UpdatedAt;

        // Staff-ness as a room sees it. Resolved once here rather than per summary: the summary is
        // fetched on every room entry and every look change, and a role change mid-session reaching
        // the next activation is an acceptable lag.
        _state.IsModerator = (
            await _permissions.ResolveForPlayerAsync((int)_state.PlayerId, ct).ConfigureAwait(true)
        ).HasAny(Capabilities.Room.ModerateAny);

        await _grainFactory
            .GetPlayerDirectoryGrain()
            .SetPlayerNameAsync(_state.PlayerId, _state.Name, ct);
    }

    private async Task WriteToDatabaseAsync(CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory.CreateDbContextAsync(ct);

        await dbCtx
            .Players.Where(x => x.Id == (int)_state.PlayerId)
            .ExecuteUpdateAsync(
                up =>
                    up.SetProperty(p => p.Name, _state.Name)
                        .SetProperty(p => p.Motto, _state.Motto)
                        .SetProperty(p => p.Figure, _state.Figure)
                        .SetProperty(p => p.Gender, _state.Gender),
                ct
            );

        _state.LastUpdated = DateTime.UtcNow;
    }
}
