using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Networking;

namespace Vortex.Primitives.Players.Sessions;

/// <summary>
/// A socket becoming a player in the world: attached to the player, then the player's presence
/// sends the sign-in. The one path for a player ticket and for a character chosen on the character
/// screen (spec F3a §5.4).
/// </summary>
public interface IWorldEntry
{
    public Task EnterAsync(SessionKey key, PlayerId playerId, CancellationToken ct);
}
