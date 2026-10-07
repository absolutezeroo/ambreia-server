using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Action;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Snapshots.Avatars;

namespace Vortex.Primitives.Rooms.Grains;

/// <summary>Avatars present in the room: their lifecycle, movement, posture and chat.</summary>
[Alias("Vortex.Primitives.Rooms.Grains.IRoomAvatars")]
public interface IRoomAvatars : IGrainWithIntegerKey
{
    public Task<bool> CreateAvatarFromPlayerAsync(
        ActionContext ctx,
        PlayerSummarySnapshot snapshot,
        CancellationToken ct
    );
    public Task<bool> RemoveAvatarFromPlayerAsync(
        ActionContext ctx,
        PlayerId playerId,
        CancellationToken ct
    );
    public Task<bool> WalkAvatarToAsync(
        ActionContext ctx,
        int targetX,
        int targetY,
        int? targetZKey,
        CancellationToken ct
    );
    public Task<bool> UpdateAvatarWithPlayerAsync(
        PlayerSummarySnapshot snapshot,
        CancellationToken ct
    );
    public Task<bool> SetAvatarDanceAsync(
        ActionContext ctx,
        AvatarDanceType danceType,
        CancellationToken ct
    );
    public Task<bool> SetAvatarExpressionAsync(
        ActionContext ctx,
        AvatarExpressionType expressionType,
        CancellationToken ct
    );

    /// <summary>Sets the acting player's worn avatar effect (0 = none) and broadcasts it to the room.</summary>
    public Task<bool> SetAvatarEffectAsync(ActionContext ctx, int effectId, CancellationToken ct);

    /// <summary>
    /// Puts a hand item in the acting player's hand for a while. Nothing is persisted: a hand item
    /// is shown and then gone, so a player who reconnects is empty-handed.
    /// </summary>
    public Task<bool> GiveCarryItemAsync(PlayerId playerId, int itemId, CancellationToken ct);

    /// <summary>Empties the acting player's hand. False when it was already empty.</summary>
    public Task<bool> DropCarryItemAsync(ActionContext ctx, CancellationToken ct);

    /// <summary>
    /// Hands what the acting player is holding to somebody standing beside them. Refused across a
    /// room, and refused when their hand is empty.
    /// </summary>
    public Task<bool> PassCarryItemAsync(
        ActionContext ctx,
        PlayerId targetPlayerId,
        CancellationToken ct
    );

    /// <summary>
    /// Gives what the acting player is holding to one of the room's pets, which consumes it. False
    /// when the pet is out of reach, or the item is nothing a pet will take.
    /// </summary>
    public Task<bool> PassCarryItemToPetAsync(ActionContext ctx, int petId, CancellationToken ct);
    public Task SendChatFromPlayerAsync(
        PlayerId playerId,
        string text,
        AvatarGestureType gesture,
        List<(string, string, bool)> links,
        int trackingId,
        PlayerId? targetPlayerId = null,
        RoomChatType chatType = RoomChatType.Chat
    );

    public Task<ImmutableArray<RoomAvatarSnapshot>> GetAllAvatarSnapshotsAsync(
        CancellationToken ct
    );

    /// <summary>Puts the acting player's avatar into the posture the client asked for; sitting also
    /// cancels any dance, as it does on Habbo.</summary>
    public Task<bool> SetAvatarPostureAsync(
        ActionContext ctx,
        AvatarPostureType postureType,
        CancellationToken ct
    );

    public Task<bool> SetAvatarSignAsync(ActionContext ctx, int signId, CancellationToken ct);

    public Task<bool> LookToAvatarAsync(
        ActionContext ctx,
        int targetX,
        int targetY,
        CancellationToken ct
    );

    public Task SetAvatarTypingAsync(ActionContext ctx, bool isTyping, CancellationToken ct);

    /// <summary>The acting player (<paramref name="ctx"/>) clicked the avatar with room object id
    /// <paramref name="targetObjectId"/>; raises the wired USER_CLICKS_USER trigger when that avatar
    /// is another player.</summary>
    public Task ClickCharacterAsync(ActionContext ctx, int targetObjectId, CancellationToken ct);

    /// <summary>The acting player (<paramref name="ctx"/>) gives a respect point to
    /// <paramref name="targetPlayerId"/> if present in the room and within the daily budget.</summary>
    public Task RespectPlayerAsync(ActionContext ctx, int targetPlayerId, CancellationToken ct);
}
