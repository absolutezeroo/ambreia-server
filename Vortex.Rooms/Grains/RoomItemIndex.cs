using System;
using System.Collections.Generic;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Logic;

namespace Vortex.Rooms.Grains;

/// <summary>
/// Incrementally maintained index of the room's items by the type of their attached logic — the
/// concrete type, every base class, and every interface it implements — so
/// <c>ItemsOf&lt;SomeFamilyBaseLogic&gt;()</c> finds a whole family and
/// <c>ItemsOf&lt;IArenaTileComponent&gt;()</c> finds everything playing that role, whichever class
/// happens to provide it. It replaces the "walk all of <c>ItemsById</c> and pattern-match the logic"
/// scans that every game system used to carry — those ran on the hot path (the game-timer one on
/// every 50 ms frame).
/// <para>
/// Interfaces are indexed because that is what capability-based game furniture asks for: a game
/// wants "the goals in this room", not "every subclass of a goal base class". The extra buckets are
/// a handful of references per item and are built once, at attach.
/// </para>
/// <para>
/// Invariant: an item is in the index if and only if it is in <see cref="RoomLiveState.ItemsById"/>
/// with its logic attached. The places an item leaves the live state (pickup/removal in
/// <c>RoomObjectModule.RemoveObjectAsync</c>, the definition swap in <c>RoomGrain.Furni.Edit.cs</c>)
/// and the one place logic attaches
/// (<c>RoomObjectModule.AttatchLogicAsync</c>) all notify it — a new mutation path for
/// <c>ItemsById</c> MUST do the same or the index silently goes stale.
/// </para>
/// All access happens inside the room grain's single-threaded turn, so no locking.
/// </summary>
public sealed class RoomItemIndex
{
    private readonly Dictionary<Type, HashSet<IRoomItem>> _itemsByLogicType = [];

    /// <summary>The index keys of a logic type, worked out once per room. Reflection runs at attach
    /// time only, never on a gameplay path. Per-instance rather than static so it needs no lock —
    /// the room grain's turn is the only thread that touches it.</summary>
    private readonly Dictionary<Type, Type[]> _keysByLogicType = [];

    /// <summary>Registers the item under its logic's concrete type, every base class (exclusive of
    /// <see cref="object"/>) and every interface it implements.</summary>
    public void OnLogicAttached(IRoomItem item)
    {
        if (item.Logic is null)
        {
            return;
        }

        foreach (Type key in KeysOf(item.Logic.GetType()))
        {
            if (!_itemsByLogicType.TryGetValue(key, out HashSet<IRoomItem>? bucket))
            {
                bucket = [];
                _itemsByLogicType[key] = bucket;
            }

            bucket.Add(item);
        }
    }

    /// <summary>Drops the item from every bucket it was registered under. A no-op for an item whose
    /// logic never attached (it was never indexed).</summary>
    public void OnItemDetached(IRoomItem item)
    {
        if (item.Logic is null)
        {
            return;
        }

        foreach (Type key in KeysOf(item.Logic.GetType()))
        {
            if (_itemsByLogicType.TryGetValue(key, out HashSet<IRoomItem>? bucket))
            {
                bucket.Remove(item);

                if (bucket.Count == 0)
                {
                    _itemsByLogicType.Remove(key);
                }
            }
        }
    }

    /// <summary>The live bucket for <typeparamref name="TLogic"/> — do not enumerate this across an
    /// <c>await</c> that can add or remove items; use <see cref="LogicsOf{TLogic}"/> for that.</summary>
    public IReadOnlyCollection<IRoomItem> ItemsOf<TLogic>()
        where TLogic : class, IRoomObjectLogic =>
        _itemsByLogicType.TryGetValue(typeof(TLogic), out HashSet<IRoomItem>? bucket) ? bucket : [];

    /// <summary>How many items play <typeparamref name="TLogic"/>'s role. O(1) — what arena
    /// validation counts with instead of materialising a list it throws away.</summary>
    public int CountOf<TLogic>()
        where TLogic : class, IRoomObjectLogic =>
        _itemsByLogicType.TryGetValue(typeof(TLogic), out HashSet<IRoomItem>? bucket)
            ? bucket.Count
            : 0;

    /// <summary>A materialised snapshot of every <typeparamref name="TLogic"/> in the room — safe to
    /// iterate while awaiting state changes (which can detach items mid-loop).</summary>
    public List<TLogic> LogicsOf<TLogic>()
        where TLogic : class, IRoomObjectLogic
    {
        List<TLogic> logics = [];

        if (_itemsByLogicType.TryGetValue(typeof(TLogic), out HashSet<IRoomItem>? bucket))
        {
            foreach (IRoomItem item in bucket)
            {
                if (item.Logic is TLogic logic)
                {
                    logics.Add(logic);
                }
            }
        }

        return logics;
    }

    /// <summary>
    /// Every item the index currently holds, once each however many buckets it sits in.
    /// </summary>
    /// <remarks>
    /// For checking the invariant in the class summary rather than for gameplay: nothing on a hot
    /// path wants "all indexed items", and the answer is only meaningful next to
    /// <see cref="RoomLiveState.ItemsById"/> — an item in here and not in there is a bucket that
    /// went stale, which is exactly what this cannot notice on its own.
    /// </remarks>
    public IEnumerable<IRoomItem> IndexedItems()
    {
        HashSet<IRoomItem> seen = [];

        foreach (HashSet<IRoomItem> bucket in _itemsByLogicType.Values)
        {
            foreach (IRoomItem item in bucket)
            {
                if (seen.Add(item))
                {
                    yield return item;
                }
            }
        }
    }

    private Type[] KeysOf(Type logicType)
    {
        if (_keysByLogicType.TryGetValue(logicType, out Type[]? cached))
        {
            return cached;
        }

        List<Type> keys = [];

        for (
            Type? type = logicType;
            type is not null && type != typeof(object);
            type = type.BaseType
        )
        {
            keys.Add(type);
        }

        keys.AddRange(logicType.GetInterfaces());

        Type[] built = [.. keys];
        _keysByLogicType[logicType] = built;

        return built;
    }
}
