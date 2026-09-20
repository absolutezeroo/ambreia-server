using System.Collections.Immutable;
using Vortex.Primitives.Events;
using Vortex.Primitives.Signals;

namespace Vortex.Signals.Translators;

/// <summary>Dances and waves, each on its own action.</summary>
public sealed class GestureTranslator : ISignalTranslator<PlayerGesturedEvent>
{
    public static ImmutableArray<SignalShape> Shapes { get; } =
    [
        // A wave has no variants, so only the dance declares which one it was.
        new(SignalActions.Dance, [Facts.Room, Facts.Dance]),
        new(SignalActions.Wave, [Facts.Room]),
    ];

    public ImmutableArray<ProgressSignal> Translate(PlayerGesturedEvent e)
    {
        string? action = e.Gesture switch
        {
            "dance" => SignalActions.Dance,
            "wave" => SignalActions.Wave,
            _ => null,
        };

        if (action is null)
        {
            return [];
        }

        SignalFacts facts = SignalFacts.Build().Id(Facts.Room, e.RoomId);

        // Only when the room said which. A style the room did not name would be a fact declared
        // and not emitted, and a task filtering on it would never advance.
        if (action == SignalActions.Dance && e.Style.Length > 0)
        {
            facts = facts.Enum(Facts.Dance, e.Style);
        }

        return [new(e.PlayerId.Value, action, 1, Target: null, facts)];
    }
}
