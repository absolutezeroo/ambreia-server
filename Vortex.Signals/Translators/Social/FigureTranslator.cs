using System.Collections.Immutable;
using Vortex.Primitives.Events;
using Vortex.Primitives.Players.Avatar;
using Vortex.Primitives.Signals;

namespace Vortex.Signals.Translators;

/// <summary>
/// Figure changes. The new look is carried, so "put on something with X in it" — and, with
/// <c>NotContains</c>, "take something off" — is writable.
/// </summary>
/// <remarks>
/// The event has always carried the figure; this translator used to drop it, which left
/// <see cref="Facts.Figure"/> declared in the vocabulary with <c>save_outfit</c> as its only
/// emitter. A filter on it here was refused by the content validator, so the gap showed up as a
/// fact an operator could see and never use.
/// </remarks>
public sealed class FigureTranslator : ISignalTranslator<PlayerFigureChangedEvent>
{
    public static ImmutableArray<SignalShape> Shapes { get; } =
    [new(SignalActions.ChangeFigure, [Facts.Figure, Facts.FigureSet])];

    public ImmutableArray<ProgressSignal> Translate(PlayerFigureChangedEvent e) =>
        [
            new(
                e.PlayerId.Value,
                SignalActions.ChangeFigure,
                1,
                Target: null,
                SignalFacts
                    .Build()
                    .Text(Facts.Figure, e.Figure)
                    .Ids(Facts.FigureSet, FigureString.SetIdsOf(e.Figure))
            ),
        ];
}
