using System.Collections.Immutable;
using Vortex.Primitives.Events;
using Vortex.Primitives.Signals;

namespace Vortex.Signals.Translators;

/// <summary>Dances, waves and signs, each on its own action.</summary>
public sealed class GestureTranslator : ISignalTranslator<PlayerGesturedEvent>
{
    public static ImmutableArray<SignalShape> Shapes { get; } =
    [
        // A wave has one form, so it declares no variant. The other two do.
        new(SignalActions.Dance, [Facts.Room, Facts.Dance]),
        new(SignalActions.Wave, [Facts.Room]),
        new(SignalActions.ShowSign, [Facts.Room, Facts.Sign]),
    ];

    public ImmutableArray<ProgressSignal> Translate(PlayerGesturedEvent e)
    {
        string? action = e.Gesture switch
        {
            "dance" => SignalActions.Dance,
            "wave" => SignalActions.Wave,
            "sign" => SignalActions.ShowSign,
            _ => null,
        };

        if (action is null)
        {
            return [];
        }

        SignalFacts facts = SignalFacts.Build().Id(Facts.Room, e.RoomId);

        // Only when the room said which. A variant the room did not name would be a fact declared
        // and not emitted, and a task filtering on it would never advance.
        if (e.Style.Length > 0)
        {
            facts = action switch
            {
                SignalActions.Dance => facts.Enum(Facts.Dance, e.Style),
                SignalActions.ShowSign => facts.Text(Facts.Sign, e.Style),
                _ => facts,
            };
        }

        return [new(e.PlayerId.Value, action, 1, Target: null, facts)];
    }
}
