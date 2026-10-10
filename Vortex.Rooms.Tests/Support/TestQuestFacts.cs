using System.Collections.Immutable;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rooms.Tests.Support;

/// <summary>A part no shipped module has — a quest log — to prove a module can bring its own.</summary>
internal sealed record TestQuestFacts : ZoneFacts
{
    public required ImmutableDictionary<string, string> States { get; init; }
}
