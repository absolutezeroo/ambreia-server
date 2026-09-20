using System.Collections.Immutable;
using Vortex.Primitives.Events;
using Vortex.Primitives.Players.Avatar;
using Vortex.Primitives.Signals;

namespace Vortex.Signals.Translators;

/// <summary>Saved an outfit. The figure is carried, so "wear something with X in it" is writable.</summary>
public sealed class WardrobeTranslator : ISignalTranslator<WardrobeOutfitSavedEvent>
{
    public static ImmutableArray<SignalShape> Shapes { get; } =
    [new(SignalActions.SaveOutfit, [Facts.Figure, Facts.FigureSet])];

    public ImmutableArray<ProgressSignal> Translate(WardrobeOutfitSavedEvent e) =>
        [
            new(
                e.PlayerId.Value,
                SignalActions.SaveOutfit,
                1,
                Target: null,
                SignalFacts
                    .Build()
                    .Text(Facts.Figure, e.Figure)
                    .Ids(Facts.FigureSet, FigureString.SetIdsOf(e.Figure))
            ),
        ];
}
