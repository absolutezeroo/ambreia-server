using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;

namespace Vortex.Progression.Achievements.Events;

/// <summary>
/// "For having %limit% days of Builders Club membership": raised to the membership's length at
/// each login and after each purchase of membership time.
/// </summary>
/// <remarks>
/// Raised, never added, so logging in twice a day counts nothing twice. The login keeps it moving
/// while a member simply plays; the purchase makes the first level land the moment it is bought.
/// </remarks>
public sealed class BuildersClubAchievementHandler(
    IGrainFactory grainFactory,
    IBuildersClubService buildersClubService
) : IEventHandler<PlayerLoggedInEvent>, IEventHandler<BuildersClubMembershipGrantedEvent>
{
    public ValueTask HandleAsync(PlayerLoggedInEvent e, EventContext ctx, CancellationToken ct) =>
        RaiseAsync(e.PlayerId, ct);

    public ValueTask HandleAsync(
        BuildersClubMembershipGrantedEvent e,
        EventContext ctx,
        CancellationToken ct
    ) => RaiseAsync(e.PlayerId.Value, ct);

    private async ValueTask RaiseAsync(int playerId, CancellationToken ct)
    {
        if (playerId <= 0)
        {
            return;
        }

        int days = await buildersClubService
            .GetMembershipDaysAsync(playerId, ct)
            .ConfigureAwait(false);

        if (days <= 0)
        {
            return;
        }

        await grainFactory
            .GetPlayerAchievementGrain(playerId)
            .ProgressToAsync(AchievementNames.BuildersClub, days, ct)
            .ConfigureAwait(false);
    }
}
