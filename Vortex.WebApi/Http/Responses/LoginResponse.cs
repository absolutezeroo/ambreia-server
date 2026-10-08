namespace Vortex.WebApi.Http.Responses;

/// <summary>
/// <c>requiresOnboarding</c> is "this account owns no avatar yet", which is what sends the visitor
/// to the avatar creation rather than to the hotel.
/// </summary>
public sealed record LoginResponse(bool RequiresOnboarding);
