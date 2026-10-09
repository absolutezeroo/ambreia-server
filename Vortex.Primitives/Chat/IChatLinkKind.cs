using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Chat;

/// <summary>
/// A kind of link a chat text may carry (spec F3c §6, §14.1), as a <c>[prefix:id]</c> token. A token
/// whose id exists is kept, any other one of this kind removed. Declared by DI by its module.
/// </summary>
public interface IChatLinkKind
{
    /// <summary><c>item</c> in <c>[item:item.material.amber_shard]</c>.</summary>
    public string Prefix { get; }

    public ValueTask<bool> ExistsAsync(string id, CancellationToken ct);
}
