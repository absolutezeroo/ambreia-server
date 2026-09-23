using System.Collections.Generic;
using System.Threading.Tasks;
using Vortex.Primitives.Action;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Primitives.Rooms.Events;
using Vortex.Primitives.Rooms.Snapshots.Wired.Variables;
using Vortex.Primitives.Rooms.Wired;
using Vortex.Primitives.Rooms.Wired.Variable;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Addons;
using Vortex.Rooms.Wired.Variables;

namespace Vortex.Rooms.Wired.Projectile;

/// <summary>
/// One of the projectile add-on's internal variables — tiles travelled, collisions, position,
/// altitude, whether it is in flight — read off the flight the add-on last shaped for that furni.
/// </summary>
/// <remarks>
/// <para>
/// Hung off the furni variable box the add-on is stacked with, like the level-up readings
/// (<see cref="IWiredSubVariableSource"/>), and named after it: <c>@shot</c> gives
/// <c>@shot.animation.tiles_travelled</c>. Only a furni that has flown holds a value, so the wired
/// menu's holder list shows projectiles and not the whole room.
/// </para>
/// <para>
/// The four the dialog marks "interceptable" move while nothing writes them, so the room polls them
/// every wired tick (<see cref="IWiredPolledVariable"/>) and announces each change like a written
/// one — which is what lets a "variable changed" trigger catch a projectile mid-flight. None of them
/// can be written, given or removed.
/// </para>
/// </remarks>
internal sealed class WiredProjectileSubVariable(
    WiredAddonProjectile source,
    IWiredVariable parent,
    WiredVariableId variableId,
    string variableName,
    int reading,
    bool interceptable
) : IWiredVariable, IWiredPolledVariable
{
    private readonly Dictionary<int, int> _announced = [];

    private WiredVariableSnapshot? _snapshot;

    public bool CanBind(in WiredVariableKey key) =>
        key.VariableId == variableId && key.TargetType == WiredVariableTargetType.Furni;

    public bool TryGetValue(in WiredVariableKey key, out WiredVariableValue value)
    {
        if (!CanBind(key) || !source.TryRead(reading, key.TargetId, source.NowMs(), out int read))
        {
            value = WiredVariableValue.Default;

            return false;
        }

        value = new WiredVariableValue(read);

        return true;
    }

    public IEnumerable<WiredVariableChangedEvent> Poll(long nowMs)
    {
        if (!interceptable)
        {
            yield break;
        }

        foreach (int furniId in source.FlownFurniIds)
        {
            if (!source.TryRead(reading, furniId, nowMs, out int current))
            {
                continue;
            }

            bool known = _announced.TryGetValue(furniId, out int previous);

            if (known && previous == current)
            {
                continue;
            }

            _announced[furniId] = current;

            yield return new WiredVariableChangedEvent
            {
                RoomId = source.RoomId,
                CausedBy = ActionContext.CreateForWired(source.RoomId),
                Key = new WiredVariableKey(variableId, WiredVariableTargetType.Furni, furniId),
                Kind = known
                    ? WiredVariableChangeKind.ValueChanged
                    : WiredVariableChangeKind.Created,
                Previous = previous,
                Current = current,
            };
        }
    }

    public bool TryGetTimestamps(
        in WiredVariableKey key,
        out long createdAtMs,
        out long updatedAtMs
    )
    {
        createdAtMs = updatedAtMs = 0;

        return false;
    }

    public Task<bool> GiveValueAsync(
        WiredVariableKey key,
        WiredVariableValue value,
        bool replace = false
    ) => Task.FromResult(false);

    public Task<bool> SetValueAsync(
        IWiredExecutionContext ctx,
        WiredVariableKey key,
        WiredVariableValue value
    ) => Task.FromResult(false);

    public bool RemoveValue(WiredVariableKey key) => false;

    public WiredVariableSnapshot GetVarSnapshot() => _snapshot ??= BuildSnapshot();

    private WiredVariableSnapshot BuildSnapshot()
    {
        WiredVariableSnapshot parentSnapshot = parent.GetVarSnapshot();
        Dictionary<WiredVariableValue, string> textConnectors = [];
        WiredVariableFlags flags =
            WiredVariableFlags.HasValue
            | (interceptable ? WiredVariableFlags.CanInterceptChanges : WiredVariableFlags.None);

        return new()
        {
            VariableId = variableId,
            VariableName = variableName,
            VariableType = WiredVariableType.Created,
            VariableHash = WiredVariableHashBuilder.HashValues(
                variableName,
                parentSnapshot.AvailabilityType,
                WiredVariableTargetType.Furni,
                flags,
                textConnectors
            ),
            AvailabilityType = parentSnapshot.AvailabilityType,
            TargetType = WiredVariableTargetType.Furni,
            Flags = flags,
            TextConnectors = textConnectors,
        };
    }
}
