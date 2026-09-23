using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Rooms.Grains;

public partial interface IRoomWired
{
    /// <summary>
    /// Every global placeholder the room defines, as (token, value) — what each text the room's
    /// wired says is put through.
    /// </summary>
    public Task<ImmutableArray<KeyValuePair<string, string>>> GetGlobalPlaceholdersAsync(
        CancellationToken ct
    );

    /// <summary>
    /// The value room <paramref name="roomId"/> gives its global placeholder
    /// <paramref name="name"/>, read from storage without loading that room; null when it defines
    /// no such placeholder any more.
    /// </summary>
    public Task<string?> ResolveSharedPlaceholderAsync(
        int roomId,
        string name,
        CancellationToken ct
    );
}
