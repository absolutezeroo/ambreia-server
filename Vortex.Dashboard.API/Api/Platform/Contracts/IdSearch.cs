using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Platform.Contracts;

/// <summary>
/// Everything known about one number, read as a player id and as an item id at once.
/// </summary>
/// <remarks>
/// <paramref name="PlayerProfile"/> is null when no account carries the id, and the rest still
/// answers: the same number is also an item id and a room id, and the history attached to it is
/// what the search was opened for.
/// </remarks>
public sealed record IdSearch(
    string Term,
    int Page,
    int Limit,
    int Offset,
    IReadOnlyList<PlayerAuditRow> AsActor,
    PlayerProfile? PlayerProfile,
    IReadOnlyList<PlayerLedgerRow> Ledger,
    IReadOnlyList<PlayerItemRow> ItemHistory,
    IReadOnlyList<PlayerChatRow> Chats
) : DirectorySearch;
