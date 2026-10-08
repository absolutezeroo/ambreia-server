namespace Vortex.WebApi.Http.Responses;

/// <summary>The editor's test mode: open the client with this ticket and it starts in that room.</summary>
public sealed record ZoneTestTicket(string SsoToken, int RoomId);
