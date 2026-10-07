using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Logging.Extensions;
using Vortex.Primitives.Action;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.StuffData;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Events.RoomItem;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Logic.Furniture;

namespace Vortex.Rooms.Object.Logic.Furniture;

public abstract class FurnitureLogic<TObject, TSelf, TContext>
    : RoomObjectLogic<TObject, TSelf, TContext>,
        IFurnitureLogic<TObject, TSelf, TContext>
    where TObject : IRoomItem<TObject, TSelf, TContext>
    where TContext : IRoomItemContext<TObject, TSelf, TContext>
    where TSelf : IFurnitureLogic<TObject, TSelf, TContext>
{
    protected readonly IStuffDataFactory _stuffDataFactory;

    protected virtual StuffPersistanceType _stuffPersistanceType => StuffPersistanceType.Persistent;

    /// <summary>
    /// The stuff data format this furniture stores its state in, from its definition.
    /// <para>
    /// This used to be a hardcoded <see cref="StuffDataType.LegacyKey"/> that nothing ever
    /// overrode, so a definition's <c>stuff_data_type</c> decided the format everywhere except in
    /// the room: the inventory built a crackable's data from the column while the room handed the
    /// same furniture a legacy bag, and the crackable logic then found no counters to write to.
    /// Note how close this reads to <see cref="_stuffPersistanceType"/> above -- overriding that one
    /// and believing the format had been set with it is exactly how the two came apart.
    /// </para>
    /// </summary>
    protected virtual StuffDataType _stuffDataType => _ctx.Definition.StuffDataType;

    public IStuffData StuffData { get; private set; }

    IRoomItemContext IFurnitureLogic.Context => Context;

    public FurnitureLogic(IStuffDataFactory stuffDataFactory, TContext ctx)
        : base(ctx)
    {
        _stuffDataFactory = stuffDataFactory;

        StuffData = _stuffDataFactory.CreateStuffDataFromExtraData(
            _stuffDataType,
            ctx.RoomObject.ExtraData
        );
    }

    public virtual FurnitureUsageType GetUsagePolicy() =>
        _ctx.Definition.TotalStates == 0 ? FurnitureUsageType.Nobody : _ctx.Definition.UsagePolicy;

    public virtual bool CanToggle() => false;

    public virtual Altitude GetStackHeight() => 0;

    public virtual int GetState() => StuffData.GetState();

    public virtual string GetLegacyString() => StuffData.GetLegacyString();

    /// <summary>
    /// Whether this furni's stuff data is a state, rather than a payload that happens to live in the
    /// same field.
    /// </summary>
    /// <remarks>
    /// <see cref="SetStateAsync"/> writes the new state <i>over</i> the legacy string, so cycling a
    /// furni whose data is not a state does not merely misread it — it destroys it. A photo's json, a
    /// sticky's text and colour: one click and the wall item is the string "1", persisted and
    /// broadcast, with the original gone. Empty counts as a state on purpose, because a multistate
    /// furni nobody has used yet is exactly that and still has to toggle to 1.
    /// </remarks>
    protected bool CarriesAState()
    {
        if (StuffData is null)
        {
            return false;
        }

        string legacy = StuffData.GetLegacyString();

        return legacy.Length == 0
            || int.TryParse(legacy, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }

    public virtual int GetNextToggleableState()
    {
        int totalStates = _ctx.RoomObject.Definition.TotalStates;

        if (totalStates == 0 || StuffData is null)
        {
            return 0;
        }

        return (StuffData.GetState() + 1) % totalStates;
    }

    public virtual int GetPrevToggleableState()
    {
        int totalStates = _ctx.RoomObject.Definition.TotalStates;

        if (totalStates == 0 || StuffData is null)
        {
            return 0;
        }

        return (StuffData.GetState() - 1 + totalStates) % totalStates;
    }

    public virtual int GetRandomToggleableState()
    {
        int totalStates = _ctx.RoomObject.Definition.TotalStates;

        if (totalStates <= 1 || StuffData is null)
        {
            return 0;
        }

        return System.Random.Shared.Next(totalStates);
    }

    public virtual async Task SetStateAsync(int state, bool refresh = true)
    {
        StuffData.SetState(state.ToString());

        await PersistStuffDataAsync(refresh);

        await OnStateChangedAsync(CancellationToken.None);
    }

    /// <remarks>
    /// The first line is not a formality. <see cref="StuffDataBase.GetSnapshot" /> caches, and
    /// <c>IMapStuffData.Data</c> / <c>INumberStuffData.Data</c> hand out the live collection — so
    /// every caller that writes a key and then persists (the wired chest's settings, a mannequin's
    /// figure and name, a toner's colours) saved the new value to the database and broadcast the
    /// *previous* snapshot. The furni came back one edit behind, for everyone, and nothing threw.
    /// Marking dirty here rather than in each of those callers is the point: this method is what
    /// "the stuff data changed" already means, and the next writer gets it for free.
    /// </remarks>
    public virtual Task PersistStuffDataAsync(bool refresh = true)
    {
        StuffData.MarkDirty();

        if (_stuffPersistanceType == StuffPersistanceType.Persistent)
        {
            _ctx.RoomObject.ExtraData.UpdateSection(
                ExtraDataSectionType.STUFF,
                JsonSerializer.SerializeToNode(StuffData, StuffData.GetType())
            );
        }

        if (refresh)
        {
            _ctx.RefreshStuffDataAsync().LogAndForget(_logger, "Failed to refresh stuff data.");
        }

        return Task.CompletedTask;
    }

    public override Task OnAttachAsync(CancellationToken ct) =>
        _ctx.PublishRoomEventAsync(
            new RoomItemAttatchedEvent
            {
                RoomId = _ctx.RoomId,
                CausedBy = ActionContext.System,
                ObjectId = _ctx.ObjectId,
            },
            ct
        );

    public override Task OnDetachAsync(CancellationToken ct) =>
        _ctx.PublishRoomEventAsync(
            new RoomItemDetachedEvent
            {
                RoomId = _ctx.RoomId,
                CausedBy = ActionContext.System,
                ObjectId = _ctx.ObjectId,
            },
            ct
        );

    public virtual Task OnStateChangedAsync(CancellationToken ct) =>
        _ctx.PublishRoomEventAsync(
            new RoomItemStateChangedEvent
            {
                RoomId = _ctx.RoomId,
                CausedBy = ActionContext.System,
                ObjectId = _ctx.ObjectId,
            },
            ct
        );

    public virtual Task OnMoveAsync(ActionContext ctx, int prevIdx, CancellationToken ct) =>
        _ctx.PublishRoomEventAsync(
            new RoomItemMovedEvent
            {
                RoomId = _ctx.RoomId,
                CausedBy = ctx,
                ObjectId = _ctx.ObjectId,
                PrevIdx = prevIdx,
            },
            ct
        );

    public virtual Task OnPlaceAsync(ActionContext ctx, CancellationToken ct) =>
        _ctx.PublishRoomEventAsync(
            new RoomItemPlacedEvent
            {
                RoomId = _ctx.RoomId,
                CausedBy = ctx,
                ObjectId = _ctx.ObjectId,
            },
            ct
        );

    public virtual Task OnPickupAsync(ActionContext ctx, CancellationToken ct) =>
        _ctx.PublishRoomEventAsync(
            new RoomItemPickupEvent
            {
                RoomId = _ctx.RoomId,
                CausedBy = ctx,
                ObjectId = _ctx.ObjectId,
            },
            ct
        );

    public virtual async Task OnUseAsync(ActionContext ctx, int param, CancellationToken ct)
    {
        // A definition that declares states is not a promise that this particular item holds one:
        // furniture_external_image_wallitem and furniture_stickie are bound to no logic of their own,
        // so a photo and a note arrive here on the generic toggle path carrying a payload instead.
        if (!CarriesAState())
        {
            return;
        }

        await SetStateAsync(GetNextToggleableState());
    }

    public virtual Task OnClickAsync(ActionContext ctx, int param, CancellationToken ct) =>
        _ctx.PublishRoomEventAsync(
            new RoomItemClickedEvent
            {
                RoomId = _ctx.RoomId,
                CausedBy = ctx,
                ObjectId = _ctx.ObjectId,
            },
            ct
        );
}
