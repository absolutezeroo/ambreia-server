using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Action;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;

namespace Vortex.Dashboard.API.Operations;

/// <summary>
/// The in-game player a dashboard operator acts as, resolved once and remembered.
/// </summary>
/// <remarks>
/// <para>
/// Room-scoped moderation grain methods (<c>MuteUserAsync</c>, <c>KickUserAsync</c>) require a real
/// <see cref="PlayerId"/> as the acting player and reject <see cref="ActionContext.System"/>, but a
/// web session has no in-game player of its own. The <c>seed_dashboard_staff_actor.sql</c> seed
/// (applied by <c>InitialCreate</c>) inserts a reserved, account-less player row for exactly this,
/// and this resolves its id.
/// </para>
/// <para>
/// A singleton with a cache and a lock, because it is one directory lookup whose answer never
/// changes for the life of the process, and the two subjects that need it would otherwise each do
/// it on every moderation action. Its own class rather than a method on an operations class: it is
/// state, and shared state on one feature's class is how the next feature ends up depending on that
/// feature.
/// </para>
/// </remarks>
internal sealed class StaffActorAccount(IGrainFactory grainFactory)
{
    /// <summary>Name of the reserved player row inserted by <c>seed_dashboard_staff_actor.sql</c>.</summary>
    private const string StaffActorName = "__dashboard_staff__";

    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private PlayerId? _playerId;

    /// <summary>Throws <c>dashboard_staff_actor_missing</c> when the seeded row is absent — an
    /// operation attributed to nobody is worse than one that fails loudly.</summary>
    public async Task<PlayerId> PlayerIdAsync(CancellationToken ct)
    {
        if (_playerId is { } cached)
        {
            return cached;
        }

        await _lock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (_playerId is { } cachedAfterLock)
            {
                return cachedAfterLock;
            }

            PlayerId? resolved = await _grainFactory
                .GetPlayerDirectoryGrain()
                .GetPlayerIdAsync(StaffActorName, ct)
                .ConfigureAwait(false);

            if (resolved is null)
            {
                throw new InvalidOperationException("dashboard_staff_actor_missing");
            }

            _playerId = resolved.Value;

            return resolved.Value;
        }
        finally
        {
            _lock.Release();
        }
    }
}
