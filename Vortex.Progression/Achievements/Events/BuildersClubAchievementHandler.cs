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
/// each login.
/// </summary>
/// <remarks>
/// At login because nothing in the emulator grants or extends a Builders Club subscription — rows
/// are written by staff — so there is no subscription event to hang it on. Raised, never added, so
/// logging in twice a day counts nothing twice.
/// </remarks>
public sealed class BuildersClubAchievementHandler(
    IGrainFactory grainFactory,
    IBuildersClubService buildersClubService
) : IEventHandler<PlayerLoggedInEvent>
{
    public async ValueTask HandleAsync(
        PlayerLoggedInEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        if (e.PlayerId <= 0)
        {
            return;
        }

        int days = await buildersClubService
            .GetMembershipDaysAsync(e.PlayerId, ct)
            .ConfigureAwait(false);

        if (days <= 0)
        {
            return;
        }

        await grainFactory
            .GetPlayerAchievementGrain(e.PlayerId)
            .ProgressToAsync(AchievementNames.BuildersClub, days, ct)
            .ConfigureAwait(false);
    }
}
