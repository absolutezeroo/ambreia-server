using System.Collections.Immutable;
using Vortex.Primitives.Events;
using Vortex.Primitives.Signals;

namespace Vortex.Signals.Translators;

/// <summary>
/// Points scored in a room game.
/// </summary>
/// <remarks>
/// <para>
/// Two facts come out of one score. The points themselves, as an amount, because "gaining %limit%
/// participation points" is a sum; and, for the scoring acts a player counts one by one — freezing
/// a Habbo, picking up a power-up — the act, as a tally of one.
/// </para>
/// <para>
/// The action names the game rather than a <c>game</c> fact naming it, because the systems that
/// read these cannot filter on facts: an achievement is chosen by the action alone, and Freeze's
/// participation points and Battle Banzai's are two different achievements. A third game gaining
/// content adds a constant here; a game with none emits nothing, which is why Football and anything
/// a plugin contributes fall through.
/// </para>
/// </remarks>
public sealed class GamePointsTranslator : ISignalTranslator<GamePointsScoredEvent>
{
    /// <summary>The game ids, as <c>GameId</c> declares them.</summary>
    private const string Freeze = "freeze";
    private const string Banzai = "banzai";

    /// <summary>Scoring acts a player counts one by one, in the games' own vocabulary.</summary>
    private const string PlayerFrozen = "freeze.player_frozen";
    private const string PowerUpCollected = "freeze.powerup_collected";
    private const string FootballGoal = "football.goal";

    public static ImmutableArray<SignalShape> Shapes { get; } =
    [
        new(SignalActions.FreezePoints, [Facts.Room]),
        new(SignalActions.BanzaiPoints, [Facts.Room]),
        new(SignalActions.FreezePlayerFrozen, [Facts.Room]),
        new(SignalActions.FreezePowerUpCollected, [Facts.Room]),
        new(SignalActions.FootballGoalScored, [Facts.Room]),
    ];

    public ImmutableArray<ProgressSignal> Translate(GamePointsScoredEvent e)
    {
        // Football has a counted act and no points action: the hotel measures goals, never the
        // points a goal is worth. A game can have either half, or neither and raise nothing.
        string? points = e.Game switch
        {
            Freeze => SignalActions.FreezePoints,
            Banzai => SignalActions.BanzaiPoints,
            _ => null,
        };

        string? act = e.Reason switch
        {
            PlayerFrozen => SignalActions.FreezePlayerFrozen,
            PowerUpCollected => SignalActions.FreezePowerUpCollected,
            FootballGoal => SignalActions.FootballGoalScored,
            _ => null,
        };

        if (points is null && act is null)
        {
            return [];
        }

        ImmutableArray<ProgressSignal>.Builder signals =
            ImmutableArray.CreateBuilder<ProgressSignal>(2);

        if (points is not null)
        {
            signals.Add(Signal(e, points, e.Amount));
        }

        if (act is not null)
        {
            signals.Add(Signal(e, act, 1));
        }

        return signals.ToImmutable();
    }

    private static ProgressSignal Signal(GamePointsScoredEvent e, string action, int amount) =>
        new(e.PlayerId, action, amount, Target: null, SignalFacts.Build().Id(Facts.Room, e.RoomId));
}
