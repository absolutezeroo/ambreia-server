using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Action;

namespace Vortex.Primitives.Rooms;

public partial interface IRoomService
{
    public Task OpenTradeAsync(ActionContext ctx, int otherRoomObjectId, CancellationToken ct);
    public Task AddTradeItemsAsync(
        ActionContext ctx,
        IReadOnlyList<int> itemIds,
        CancellationToken ct
    );
    public Task RemoveTradeItemAsync(ActionContext ctx, int itemId, CancellationToken ct);
    public Task SetTradeAcceptAsync(ActionContext ctx, bool accepted, CancellationToken ct);
    public Task ConfirmTradeAsync(ActionContext ctx, bool confirm, CancellationToken ct);
    public Task CloseTradeAsync(ActionContext ctx, CancellationToken ct);
}
