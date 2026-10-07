namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>What made the draw happen, and how often. Empty for a payload that did not say.</summary>
public sealed record LootSourceDraws(string Source, int Draws);
