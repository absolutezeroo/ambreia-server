namespace Vortex.WebApi.Http;

/// <summary>What the grant came to: <c>granted</c>, <c>refused</c>, <c>cancelled</c>, <c>at_max_level</c>,
/// and the character's level afterwards.</summary>
public sealed record GrantXpResponse(string Outcome, int Level);
