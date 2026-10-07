namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>
/// How often one entry came up.
/// </summary>
/// <param name="EntryId">0 for a draw whose audit payload did not name an entry.</param>
public sealed record LootEntryDraws(int EntryId, int Draws);
