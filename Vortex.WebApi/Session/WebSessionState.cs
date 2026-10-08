namespace Vortex.WebApi.Session;

/// <summary>
/// What a web session remembers besides the account it belongs to.
/// </summary>
/// <param name="SelectedPlayer">The avatar the visitor picked, or none yet.</param>
public readonly record struct WebSessionState(int? SelectedPlayer);
