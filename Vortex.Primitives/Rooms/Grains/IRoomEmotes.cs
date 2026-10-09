using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Action;

namespace Vortex.Primitives.Rooms.Grains;

/// <summary>RP actions of an avatar (wave, laugh, dance…), shown to everyone in the room; nothing is
/// kept.</summary>
[Alias("Vortex.Primitives.Rooms.Grains.IRoomEmotes")]
public interface IRoomEmotes : IGrainWithIntegerKey
{
    /// <summary>The acting player's avatar does <paramref name="key"/> — one of the configured
    /// keys, at most one per interval; anything else shows nothing.</summary>
    public Task EmoteAsync(ActionContext ctx, string key, CancellationToken ct);
}
