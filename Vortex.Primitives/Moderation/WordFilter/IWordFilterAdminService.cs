using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Moderation.WordFilter;

/// <summary>What an edit of the hotel-wide word list came to.</summary>
public enum WordFilterAdminOutcome
{
    Done = 0,

    /// <summary>No letters to match on, or a word or replacement longer than its column.</summary>
    Invalid = 1,

    /// <summary>Another live word normalises to the same key ("CON" and "c.0.n" are one word).</summary>
    Duplicate = 2,

    NotFound = 3,
}

/// <summary>
/// Staff's edits to the hotel-wide word list. Reachable only through dashboard routes carrying
/// <c>dashboard.ops.wordfilter.manage</c>; no capability check in here, because the route has it
/// and a second copy of the rule would be a second thing to get wrong.
/// </summary>
/// <remarks>
/// Category C of the dashboard architecture (§11): the rows feed the compiled set inside
/// <see cref="IWordFilter"/>. Every successful write ends with <see cref="IWordFilter.ReloadAsync"/>,
/// and an edit is functionally done only when that returns — the hotel filters with the new list
/// from the next line on.
/// </remarks>
public interface IWordFilterAdminService
{
    Task<WordFilterAdminOutcome> AddAsync(
        string word,
        WordFilterAction action,
        WordFilterMatch match,
        string? replacement,
        string actor,
        CancellationToken ct
    );

    Task<WordFilterAdminOutcome> UpdateAsync(
        int id,
        string word,
        WordFilterAction action,
        WordFilterMatch match,
        string? replacement,
        CancellationToken ct
    );

    Task<WordFilterAdminOutcome> RemoveAsync(int id, CancellationToken ct);
}
