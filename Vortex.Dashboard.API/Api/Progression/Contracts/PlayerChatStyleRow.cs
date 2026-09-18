namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>
/// One chat style a player owns.
/// </summary>
/// <remarks>
/// <paramref name="ClientStyleId"/> is the number that means anything outside this table: it is what
/// the client's own style library calls the bubble, what an operator types to grant one, and what
/// goes on the wire. <paramref name="Id"/> is the ownership row, kept only so a list has a key.
/// </remarks>
public sealed record PlayerChatStyleRow(int Id, int ClientStyleId);
