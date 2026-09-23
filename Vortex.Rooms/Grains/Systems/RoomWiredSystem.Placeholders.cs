using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Addons;

namespace Vortex.Rooms.Grains.Systems;

public sealed partial class RoomWiredSystem
{
    /// <summary>
    /// The room's global placeholders as (token, value), first definition of a token winning.
    /// </summary>
    /// <remarks>
    /// A walk over the room's items, asked only for a text that carries a <c>$(</c> at all — see
    /// <c>FurnitureWiredActionLogic.ApplyTextAddonsAsync</c>.
    /// </remarks>
    public async Task<ImmutableArray<KeyValuePair<string, string>>> GetGlobalPlaceholdersAsync(
        CancellationToken ct
    )
    {
        ImmutableArray<KeyValuePair<string, string>>.Builder placeholders =
            ImmutableArray.CreateBuilder<KeyValuePair<string, string>>();
        HashSet<string> seen = [];

        foreach (IRoomItem item in Room.AllItems())
        {
            if (
                item.Logic is WiredAddonGlobalPlaceholder placeholder
                && await placeholder.ResolveAsync(ct) is (string token, string value)
                && seen.Add(token)
            )
            {
                placeholders.Add(new(token, value));
            }
        }

        return placeholders.ToImmutable();
    }
}
