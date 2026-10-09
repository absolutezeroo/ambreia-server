using Vortex.Primitives.Players;

namespace Vortex.Primitives.Chat;

/// <summary>A player found by name, with the name as stored (spec F3c §8).</summary>
public sealed record ChatPlayer(PlayerId Id, string Name);
