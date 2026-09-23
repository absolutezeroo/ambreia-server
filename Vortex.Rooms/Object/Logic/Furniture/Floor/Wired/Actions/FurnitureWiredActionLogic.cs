using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Wired;

namespace Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Actions;

public abstract class FurnitureWiredActionLogic(
    IGrainFactory grainFactory,
    IStuffDataFactory stuffDataFactory,
    IRoomFloorItemContext ctx
) : FurnitureWiredLogic(grainFactory, stuffDataFactory, ctx), IWiredAction
{
    public override WiredType WiredType => WiredType.Action;

    private int _delayMs = 0;

    public override List<Type> GetDefinitionSpecificTypes() =>
        [.. base.GetDefinitionSpecificTypes(), typeof(int)];

    /// <summary>
    /// How long this action waits before running, from its own configuration.
    /// </summary>
    /// <remarks>
    /// Virtual because the delay is what makes a chain a chain: the engine re-schedules on it and
    /// re-validates co-location across it, and neither rule could be exercised while every action
    /// read the delay out of a private field only hydration could set.
    /// </remarks>
    public virtual int GetDelayMs() => _delayMs;

    public virtual bool IsNegative() => false;

    public virtual Task<bool> ExecuteAsync(IWiredExecutionContext ctx, CancellationToken ct) =>
        Task.FromResult(true);

    /// <summary>
    /// Runs a text the action is about to say through the pile's text add-ons, which is what turns
    /// <c>$(name)</c> into a username, a furni's name or a variable's value. Every action that says
    /// something goes through here, so a placeholder works the same in a chat bubble, a bot's line
    /// and a kick message.
    /// </summary>
    protected async Task<string> ApplyTextAddonsAsync(
        string text,
        IWiredExecutionContext ctx,
        CancellationToken ct
    )
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        foreach (IWiredAddon addon in ctx.Addons)
        {
            try
            {
                text = await addon.ApplyToTextAsync(text, ctx, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Wired text add-on {AddonType} failed in room {RoomId}; the text is said as written.",
                    addon.GetType().Name,
                    _ctx.RoomId
                );
            }
        }

        return await ApplyGlobalPlaceholdersAsync(text, ct);
    }

    /// <summary>
    /// The room's global placeholders, after the pile's own add-ons: they belong to no pile, so a
    /// pile's placeholder of the same name is the nearer one and wins. Asked only of a text that
    /// still carries a <c>$(</c>, which is almost none of them.
    /// </summary>
    private async Task<string> ApplyGlobalPlaceholdersAsync(string text, CancellationToken ct)
    {
        if (!text.Contains("$(", StringComparison.Ordinal))
        {
            return text;
        }

        try
        {
            foreach (
                (string token, string value) in await _ctx.RoomAs<IRoomWired>()
                    .GetGlobalPlaceholdersAsync(ct)
            )
            {
                text = text.Replace(token, value, StringComparison.Ordinal);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Global placeholders failed in room {RoomId}; the text is said without them.",
                _ctx.RoomId
            );
        }

        return text;
    }

    /// <summary>
    /// The tile of the first selected floor item, which is how every "send something to the furni"
    /// action reads its destination. False when the selection holds no floor item — a stack whose
    /// target furni has been picked up, which is ordinary rather than an error.
    /// </summary>
    protected bool TryResolveDestinationTile(IWiredSelectionSet selection, out int tileIdx)
    {
        foreach (int furniId in selection.SelectedFurniIds)
        {
            if (
                _ctx.Lookup.TryFindItem(furniId, out IRoomItem? item)
                && item is IRoomFloorItem floor
            )
            {
                tileIdx = _ctx.Map.ToIdx(floor.X, floor.Y);

                return true;
            }
        }

        tileIdx = 0;

        return false;
    }

    protected override async Task FillInternalDataAsync(CancellationToken ct)
    {
        await base.FillInternalDataAsync(ct);

        try
        {
            _delayMs = Math.Clamp(_wiredData.GetDefinitionParam<int>(0), 0, 20) * 500;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Malformed action delay param for wired item {ItemId}; keeping current default.",
                _ctx.ObjectId
            );
        }
    }
}
