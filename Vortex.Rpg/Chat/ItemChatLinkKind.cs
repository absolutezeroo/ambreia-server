using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Chat;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Chat;

/// <summary><c>[item:&lt;id&gt;]</c> in a chat line (spec F3c §6): kept when the id is an item
/// definition of the content, retired or not. Nobody links an item that does not exist.</summary>
internal sealed class ItemChatLinkKind(IRpgContent content) : IChatLinkKind
{
    public string Prefix => "item";

    public ValueTask<bool> ExistsAsync(string id, CancellationToken ct) =>
        ValueTask.FromResult(content.Get<ItemDefinition>(id) is not null);
}
