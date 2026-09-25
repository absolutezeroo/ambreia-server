using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Players;

public interface IBuildersClubService
{
    Task<BuildersClubSubscriptionSnapshot> GetSubscriptionAsync(
        int playerId,
        CancellationToken ct = default
    );

    /// <summary>Total furniture the player currently owns, account-wide (placed in rooms + in
    /// inventory) -- matches the WIN63 client's own BuildersClubFurniCountMessageEvent, which
    /// carries only the raw count (the client already knows the limit from the subscription-status
    /// push).</summary>
    Task<int> GetOwnedFurnitureCountAsync(int playerId, CancellationToken ct = default);

    /// <summary>Days the player has been a Builders Club member, a started day counting as one;
    /// 0 if they never were.</summary>
    Task<int> GetMembershipDaysAsync(int playerId, CancellationToken ct = default);

    /// <summary>
    /// Whether the room is sealed because Builders Club furniture is standing in it while its
    /// owner's subscription has lapsed. Such a room is hidden from every navigator listing and
    /// refuses entry to everyone but its owner and staff.
    /// </summary>
    /// <remarks>
    /// Both halves are required, and that is the whole rule: a room with no Builders Club
    /// furniture is never sealed however long the owner has been unsubscribed, and a subscriber's
    /// room is never sealed however much Builders Club furniture it holds. Removing the furniture
    /// unseals the room, which is why the answer is computed rather than stored on the room.
    /// </remarks>
    Task<bool> IsRoomSealedAsync(int roomId, CancellationToken ct = default);
}
