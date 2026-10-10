using Vortex.Rpg.Content.Rules;

namespace Vortex.Rpg.Tests.Support;

/// <summary>A behaviour a module would declare, by its key only.</summary>
internal sealed record BehaviorKey(string Key) : IMonsterBehaviorKey;
