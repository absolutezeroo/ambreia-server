using System;
using System.Collections.Generic;
using Vortex.Primitives.World.Layers;

namespace Vortex.World;

/// <summary>
/// What this server runs, as its modules declared it (spec Z1 §9): the condition and action types
/// answered, the trigger types raised beyond the room's own, and the content's items — null when
/// no content is loaded, and item references are then not checked.
/// </summary>
internal sealed record ZoneRuleVocabulary(
    IReadOnlySet<Type> Declared,
    IReadOnlySet<ZoneTriggerType> Raised,
    IReadOnlySet<string>? Items
);
