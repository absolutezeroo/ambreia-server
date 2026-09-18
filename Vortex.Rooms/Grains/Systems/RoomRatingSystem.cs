using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Room;
using Vortex.Primitives.Events;
using Vortex.Primitives.Players;

namespace Vortex.Rooms.Grains.Systems;

/// <summary>
/// The room's like count, and who is still allowed to add to it.
/// </summary>
/// <remarks>
/// <para>
/// One vote per player per room, and never from the owner. <c>RoomEntity.Score</c> is the running
/// total; <c>room_ratings</c> exists only so a second vote can be told apart from a first. The pair
/// of questions has to agree — whatever <see cref="CanRateAsync" /> offers,
/// <see cref="RateRoomAsync" /> has to accept — because the client shows its like button off the
/// first answer and never withdraws it on its own.
/// </para>
/// <para>
/// On the room grain because the counter is shared by everyone in the room: the grain's single turn
/// is what stops two simultaneous votes from both reading the old score and writing the same new one.
/// </para>
/// </remarks>
public sealed class RoomRatingSystem(RoomGrain roomGrain)
{
    private readonly RoomGrain _roomGrain = roomGrain;

    /// <summary>
    /// Whether <paramref name="actor" /> may still rate this room. A failed lookup answers no: it is
    /// better to hide the button than to offer a vote that is about to be refused.
    /// </summary>
    public async Task<bool> CanRateAsync(PlayerId actor, CancellationToken ct)
    {
        if (_roomGrain._state.RoomSnapshot.OwnerId == actor)
        {
            return false;
        }

        try
        {
            await using VortexDbContext dbCtx = await _roomGrain
                ._dbCtxFactory.CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            return !await HasVotedAsync(dbCtx, actor, ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogError(
                ex,
                "Failed to read whether player {Actor} may rate room {RoomId}.",
                actor,
                _roomGrain._state.RoomId
            );

            return false;
        }
    }

    /// <summary>
    /// Records a vote and returns the room's new score, or null if it was refused. The score comes
    /// back rather than a bare success flag because the caller has to send it to the client, whose
    /// displayed rating only ever moves when a RoomRating arrives.
    /// </summary>
    public async Task<int?> RateRoomAsync(PlayerId actor, int points, CancellationToken ct)
    {
        int sign = Math.Sign(points);

        if (sign == 0 || _roomGrain._state.RoomSnapshot.OwnerId == actor)
        {
            return null;
        }

        try
        {
            await using VortexDbContext dbCtx = await _roomGrain
                ._dbCtxFactory.CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            if (await HasVotedAsync(dbCtx, actor, ct).ConfigureAwait(true))
            {
                return null;
            }

            RoomEntity? entity = await dbCtx
                .Rooms.FirstOrDefaultAsync(r => r.Id == _roomGrain._state.RoomId.Value, ct)
                .ConfigureAwait(true);

            if (entity is null)
            {
                return null;
            }

            dbCtx.RoomRatings.Add(
                new RoomRatingEntity
                {
                    RoomEntityId = _roomGrain._state.RoomId.Value,
                    PlayerEntityId = actor.Value,
                    RoomEntity = null!,
                    PlayerEntity = null!,
                }
            );

            entity.Score += sign;

            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

            _roomGrain._state.RoomSnapshot = _roomGrain._state.RoomSnapshot with
            {
                Score = entity.Score,
                LastUpdatedUtc = DateTime.UtcNow,
            };

            await _roomGrain
                ._events.PublishAsync(
                    new RoomRatedEvent(actor, _roomGrain._state.RoomId.Value, sign),
                    ct
                )
                .ConfigureAwait(true);

            return entity.Score;
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogError(
                ex,
                "Failed to rate room {RoomId}.",
                _roomGrain._state.RoomId
            );

            return null;
        }
    }

    /// <summary>The unique index on (room, player) is what makes this one indexed lookup.</summary>
    private Task<bool> HasVotedAsync(VortexDbContext dbCtx, PlayerId actor, CancellationToken ct) =>
        dbCtx.RoomRatings.AnyAsync(
            r =>
                r.RoomEntityId == _roomGrain._state.RoomId.Value && r.PlayerEntityId == actor.Value,
            ct
        );
}
