namespace Vortex.Dashboard.API.Operations.Safety.Contracts;

/// <summary>
/// Settle one or more pending appeals. <paramref name="Upheld"/> true means the second look found
/// the report justified after all — the ticket becomes a sanctioned one, and the reporter's window
/// reads "action taken". False leaves the original decision standing.
/// </summary>
/// <remarks>
/// Upholding marks the ticket sanctioned; it does not itself ban the reported player. That stays a
/// separate, deliberate action, exactly as closing a ticket does.
/// </remarks>
public sealed record ResolveCfhAppealsRequest(int[] IssueIds, bool Upheld);
