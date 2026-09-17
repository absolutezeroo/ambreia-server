using System;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Events;
using Vortex.Rooms.Games.Events;

namespace Vortex.Rooms.Games.Progression;

/// <summary>
/// Turns a match's scoring into progression. Every point any game awards passes through the room's
/// scoring funnel already carrying who caused it and which scoring act it was, so one sink over
/// <see cref="GameScoreChangedEvent"/> covers every game there is and every game there will be —
/// including one a plugin contributes, whose scoring vocabulary this never has to know.
/// </summary>
/// <remarks>
/// <para>
/// Published detached, like the furniture events: the room must not wait on the scorer's quest,
/// achievement and daily-task grains before the next blast can be served, and at Freeze's rate
/// that is not a theoretical concern.
/// </para>
/// <para>
/// A score with no player is a team award nobody caused — a wired box, a rule's own bookkeeping —
/// and a negative one is Freeze's friendly fire. Neither is progression.
/// </para>
/// </remarks>
public sealed class GameProgressSink(int roomId, Action<IEvent> publish) : IGameEventSink
{
    public Task OnGameEventAsync(GameEvent evt, CancellationToken ct)
    {
        if (
            evt is GameScoreChangedEvent { Score: { Player.Value: > 0, Amount: > 0 } score }
            && !string.IsNullOrEmpty(evt.Game.Value)
        )
        {
            publish(
                new GamePointsScoredEvent(
                    score.Player.Value,
                    roomId,
                    evt.Game.Value,
                    score.Reason.Value,
                    score.Amount
                )
            );
        }

        return Task.CompletedTask;
    }
}
