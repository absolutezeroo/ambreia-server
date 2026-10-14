using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Action;

namespace Vortex.Primitives.Rooms.Grains;

/// <summary>A zone room's NPCs, as a player reaches them (spec M2a §3.1, §3.3).</summary>
[Alias("Vortex.Primitives.Rooms.Grains.IRoomNpcs")]
public interface IRoomNpcs : IGrainWithIntegerKey
{
    /// <summary>The player talks to this NPC: from next to it, else after walking up to it.</summary>
    public Task TalkToNpcAsync(ActionContext ctx, int objectId, CancellationToken ct);

    /// <summary>A choice of the dialogue last shown to the player, by its position in that list.</summary>
    public Task ChooseDialogueAsync(ActionContext ctx, int choice, CancellationToken ct);
}
