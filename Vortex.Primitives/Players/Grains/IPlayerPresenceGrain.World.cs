using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Players.Grains;

public partial interface IPlayerPresenceGrain
{
    /// <summary>
    /// The player enters the world, its socket attached (spec F3a §5.4): AuthenticationOK,
    /// UserRights, a forward to where it lands — a pending editor test entry, else the room held in
    /// grace, else the character's last published zone, else the start zone — then the character's
    /// state for the HUD.
    /// </summary>
    public Task EnterWorldAsync(CancellationToken ct);
}
