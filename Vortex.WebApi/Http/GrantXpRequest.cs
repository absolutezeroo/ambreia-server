namespace Vortex.WebApi.Http;

/// <summary>XP to grant a character from the admin route; the grain applies its own cap.</summary>
public sealed record GrantXpRequest(int Amount);
