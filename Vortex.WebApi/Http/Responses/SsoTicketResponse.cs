namespace Vortex.WebApi.Http.Responses;

/// <summary>The short-lived ticket the client trades for a connection.</summary>
public sealed record SsoTicketResponse(string SsoToken);
