using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Primitives.Rooms.Snapshots.Wired;
using Vortex.Primitives.Rooms.Snapshots.Wired.Variables;
using Vortex.Primitives.Rooms.Wired;
using Vortex.Primitives.Rooms.Wired.Variable;
using Vortex.Rooms.Wired;
using Vortex.Rooms.Wired.Projectile;
using Vortex.Rooms.Wired.Rules;
using Vortex.Rooms.Wired.Variables;

namespace Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Addons;

/// <summary>
/// "WIRED Add-on: Projectile" (addon code 21) — changes how the pile's furni moves look: which way
/// the projectile faces, how long it flies, how far past its target it carries on, and how high it
/// arcs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here is Habbo's own behaviour, because nobody has it.</b> The client carries only the
/// dialog (<c>_SafeCls_4053.as</c>, ported as <c>Projectile.ts</c>) and the animation, and
/// <c>Vortex.Specs.Cli analyze</c> knows nothing about the box. What is built on:
/// </para>
/// <list type="bullet">
/// <item>the nineteen ints of <c>readIntParamsFromForm()</c> and their order — exact;</item>
/// <item>the dialog's texts in <c>external_flash_texts</c> ("Overshoot by X tiles", "Always shoot X
/// tiles", "Scale animation time with distance", "Speed increase (ms)");</item>
/// <item>the four pictures the dialog shows for the direction systems
/// (<c>wired_misc_directional_system_{0..3}</c>), which fix <see cref="Direction"/>;</item>
/// <item>the client's <c>MovingObjectLogic</c>, which fixes the units: the wire's
/// <c>overshootingDistance</c> is a time in ms (<c>fixDeltaAndIntervalForOvershooting()</c>
/// stretches the slide by <c>(T + o) / T</c>), and <c>curveStrength</c> lifts the midpoint of the
/// slide by <c>strength / 100</c> of a quarter of its length.</item>
/// </list>
/// <para>
/// The internal variables (param 11, a bit per reading) hang off the furni variable box the add-on
/// is stacked with, as the level-up readings do: each shaped move is kept as a
/// <see cref="ProjectileFlight"/>, and the readings interpolate it on the room clock
/// (<see cref="WiredProjectileSubVariable"/>).
/// </para>
/// <para>
/// Not done: the "bunny hop" checkbox (param 13). A hop in place cannot be drawn — the client's
/// jump arc is proportional to the distance slid, so an avatar jumping on its own tile never leaves
/// it. The param is stored and ignored.
/// </para>
/// <para>
/// The logic key is derived: no furnidata in any tree has a projectile add-on, so there is no
/// classname to bind to. A definition that should use this box needs <c>logic</c> set to it.
/// </para>
/// </remarks>
[RoomObjectLogic("wf_xtra_projectile")]
public class WiredAddonProjectile(
    IGrainFactory grainFactory,
    IStuffDataFactory stuffDataFactory,
    IRoomFloorItemContext ctx
) : FurnitureWiredAddonLogic(grainFactory, stuffDataFactory, ctx), IWiredSubVariableSource
{
    public override int WiredCode => (int)WiredAddonType.PROJECTILE;

    /// <summary>
    /// The internal variables, by the bit the dialog's checkbox sets in param 11: the name suffix,
    /// and whether the dialog calls it "interceptable" (<c>SubVariableParam</c>'s third argument).
    /// </summary>
    private static readonly (string Name, bool Interceptable)[] Readings =
    [
        ("animation.tiles_travelled", true),
        ("animation.user_collisions", true),
        ("animation.furni_collisions", true),
        ("animation.position.x", false),
        ("animation.position.y", false),
        ("animation.position.altitude", false),
        ("animation.is_travelling", true),
    ];

    private const int InternalVariables = 11;

    /// <summary>The last flight shaped for each furni. Replaced by the next one, never needed
    /// longer: every reading is about the most recent shot.</summary>
    private readonly Dictionary<int, ProjectileFlight> _flights = [];

    // readIntParamsFromForm(), in order.
    private const int RotateToMovement = 0;
    private const int DirectionSystem = 1;
    private const int ScaleTimeWithDistance = 2;
    private const int TimePerTileOption = 3;
    private const int TimePerTileValue = 4;
    private const int TimePerTileTarget = 5;
    private const int DistanceUsesX = 6;
    private const int DistanceUsesY = 7;
    private const int DistanceUsesZ = 8;
    private const int SpeedIncrease = 9;
    private const int RotationOffset = 10;
    private const int TurnShooter = 12;
    private const int DistanceMode = 14;
    private const int DistanceOption = 15;
    private const int DistanceValue = 16;
    private const int DistanceTarget = 17;
    private const int CurveStrength = 18;

    /// <summary>A value-or-variable section's radio: 0 is the typed number, 1 a variable.</summary>
    private const int FromVariable = 1;

    public override List<IWiredParamRule> GetIntParamRules() =>
        [
            new WiredBoolParamRule(false), // 0 rotate the projectile to its movement
            new WiredRangeParamRule(0, 3, 0), // 1 direction system
            new WiredBoolParamRule(false), // 2 scale the animation time with distance
            new WiredRangeParamRule(0, 1, 0), // 3 time per tile: number or variable
            new WiredRangeParamRule(1, 100000, 100), // 4 time per tile, ms
            new WiredParamRule(0), // 5 time-per-tile variable's target
            new WiredBoolParamRule(true), // 6 distance counts X
            new WiredBoolParamRule(true), // 7 distance counts Y
            new WiredBoolParamRule(false), // 8 distance counts height
            new WiredRangeParamRule(0, 100000, 0), // 9 speed increase, ms
            new WiredRangeParamRule(0, 7, 0), // 10 rotation offset
            new WiredParamRule(0), // 11 internal variables, a bit per reading
            new WiredBoolParamRule(false), // 12 turn the shooter
            new WiredBoolParamRule(false), // 13 bunny hop (stored, not implemented)
            new WiredRangeParamRule(0, 2, 0), // 14 distance: normal / overshoot X / always X
            new WiredRangeParamRule(0, 1, 0), // 15 tiles: number or variable
            new WiredRangeParamRule(-64, 64, 0), // 16 tiles
            new WiredParamRule(0), // 17 tiles variable's target
            new WiredRangeParamRule(-1000, 1000, 0), // 18 curve strength, 0 = straight
        ];

    public override int GetMaxVariableIds() => 2;

    /// <summary>The projectile. When this resolves to nothing, every furni the pile moves is
    /// one.</summary>
    public override List<WiredFurniSourceType[]> GetAllowedFurniSources() =>
        [
            [
                WiredFurniSourceType.SelectedItems,
                WiredFurniSourceType.SelectorItems,
                WiredFurniSourceType.SignalItems,
                WiredFurniSourceType.TriggeredItem,
            ],
        ];

    /// <summary>The shooter.</summary>
    public override List<WiredPlayerSourceType[]> GetAllowedPlayerSources() =>
        [
            [
                WiredPlayerSourceType.TriggeredUser,
                WiredPlayerSourceType.SelectorUsers,
                WiredPlayerSourceType.SignalUsers,
                WiredPlayerSourceType.ClickedUser,
            ],
        ];

    public override async Task<Rotation?> RotateFloorItemMoveAsync(
        IRoomFloorItem item,
        int targetX,
        int targetY,
        IWiredExecutionContext ctx,
        CancellationToken ct
    )
    {
        if (!Flag(RotateToMovement))
        {
            return null;
        }

        IWiredSelectionSet selection = await ctx.GetEffectiveSelectionAsync(this, ct);

        if (!IsProjectile(selection, item))
        {
            return null;
        }

        Rotation? heading = Direction(Param(DirectionSystem), targetX - item.X, targetY - item.Y);

        return heading?.Rotate(Param(RotationOffset));
    }

    public override async Task<WiredFloorItemMovementSnapshot> ShapeFloorItemMoveAsync(
        IRoomFloorItem item,
        WiredFloorItemMovementSnapshot move,
        IWiredExecutionContext ctx,
        CancellationToken ct
    )
    {
        IWiredSelectionSet selection = await ctx.GetEffectiveSelectionAsync(this, ct);

        // Instant mode (the no-animation add-on) sends 0: there is no slide to shape.
        if (!IsProjectile(selection, item) || move.AnimationTime <= 0)
        {
            return move;
        }

        int dx = move.TargetX - move.SourceX;
        int dy = move.TargetY - move.SourceY;
        int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));

        WiredSelectionSet readFrom = new();
        readFrom.SelectedFurniIds.Add(item.ObjectId.Value);
        readFrom.SelectedPlayerIds.UnionWith(selection.SelectedPlayerIds);

        int time = move.AnimationTime;

        if (Flag(ScaleTimeWithDistance))
        {
            int perTile = ValueOrVariable(
                TimePerTileOption,
                TimePerTileValue,
                TimePerTileTarget,
                0,
                readFrom
            );
            double distance = TimeDistance(
                Flag(DistanceUsesX) ? Math.Abs(dx) : 0,
                Flag(DistanceUsesY) ? Math.Abs(dy) : 0,
                Flag(DistanceUsesZ) ? Math.Abs(move.TargetZ.Value - move.SourceZ.Value) : 0,
                steps
            );

            time = ScaledTime(distance, Math.Max(1, perTile), Param(SpeedIncrease), time);
        }

        int tiles = ValueOrVariable(DistanceOption, DistanceValue, DistanceTarget, 1, readFrom);
        int? overshoot = OvershootMs(Param(DistanceMode), tiles, steps, time);
        int curve = Param(CurveStrength);

        if (Flag(RotateToMovement) && Flag(TurnShooter))
        {
            await TurnShootersAsync(selection, dx, dy, ctx);
        }

        // The client stretches the slide by (T + o) / T in x and y, and not in z.
        double stretch = (time + (overshoot ?? 0)) / (double)time;

        _flights[item.ObjectId.Value] = new ProjectileFlight(
            StartMs: _ctx.NowMs(),
            DurationMs: time + (overshoot ?? 0),
            SourceX: move.SourceX,
            SourceY: move.SourceY,
            SourceZ: move.SourceZ.Value,
            VisualDeltaX: dx * stretch,
            VisualDeltaY: dy * stretch,
            DeltaZ: move.TargetZ.Value - move.SourceZ.Value,
            VisualSteps: (int)Math.Round(steps * stretch),
            PathDeltaX: dx,
            PathDeltaY: dy
        );

        return move with
        {
            AnimationTime = time,
            OvershootingDistance = overshoot ?? move.OvershootingDistance,
            CurveStrength = curve != 0 ? curve : move.CurveStrength,
        };
    }

    public IReadOnlyList<IWiredVariable> CreateSubVariables(IWiredVariable parent)
    {
        WiredVariableSnapshot parentSnapshot = parent.GetVarSnapshot();
        int mask = Param(InternalVariables);

        // A projectile is a furni; its readings mean nothing on a user or room variable.
        if (
            mask == 0
            || parentSnapshot.TargetType != WiredVariableTargetType.Furni
            || string.IsNullOrEmpty(parentSnapshot.VariableName)
        )
        {
            return [];
        }

        List<IWiredVariable> derived = [];

        for (int reading = 0; reading < Readings.Length; reading++)
        {
            if ((mask & (1 << reading)) == 0)
            {
                continue;
            }

            derived.Add(
                new WiredProjectileSubVariable(
                    this,
                    parent,
                    WiredVariableIdBuilder.CreateFromBoxSubId(_ctx.ObjectId.Value, reading),
                    $"{parentSnapshot.VariableName}.{Readings[reading].Name}",
                    reading,
                    Readings[reading].Interceptable
                )
            );
        }

        return derived;
    }

    internal RoomId RoomId => _ctx.RoomId;

    internal long NowMs() => _ctx.NowMs();

    internal IEnumerable<int> FlownFurniIds => _flights.Keys;

    /// <summary>
    /// A reading of the last flight shaped for <paramref name="furniId"/>, at
    /// <paramref name="nowMs"/>; false for a furni this add-on never shot.
    /// </summary>
    /// <remarks>
    /// Collisions count what stands on the real path the projectile has crossed so far, its source
    /// tile excluded (the shooter stands there) and itself excluded — the overshoot is drawn, not
    /// travelled, and may run through a wall.
    /// </remarks>
    internal bool TryRead(int reading, int furniId, long nowMs, out int value)
    {
        value = 0;

        if (!_flights.TryGetValue(furniId, out ProjectileFlight? flight))
        {
            return false;
        }

        value = reading switch
        {
            0 => flight.TilesTravelled(nowMs),
            1 => Collisions(flight, nowMs, furniId, users: true),
            2 => Collisions(flight, nowMs, furniId, users: false),
            3 => flight.X(nowMs),
            4 => flight.Y(nowMs),
            5 => flight.Altitude(nowMs),
            _ => flight.IsTravelling(nowMs) ? 1 : 0,
        };

        return true;
    }

    private int Collisions(ProjectileFlight flight, long nowMs, int furniId, bool users)
    {
        HashSet<RoomObjectId> hit = [];

        for (int step = 1; step <= flight.PathStepsCrossed(nowMs); step++)
        {
            (int x, int y) = flight.PathTile(step);
            int tile = _ctx.Map.ToIdx(x, y);

            hit.UnionWith(users ? _ctx.Map.AvatarStackAt(tile) : _ctx.Map.FloorStackAt(tile));
        }

        hit.Remove(new RoomObjectId(furniId));

        return hit.Count;
    }

    /// <summary>
    /// Which way a move of (<paramref name="dx"/>, <paramref name="dy"/>) points, under each of the
    /// dialog's four systems, or null for a move that goes nowhere.
    /// </summary>
    /// <remarks>
    /// Read off the dialog's own pictures. 0, "8-directional, straight": the four axes and nothing
    /// else are straight, every other tile is a diagonal. 1, "diffuse": each direction owns the
    /// 45° around it. 2 and 3, "4-directional": the longer axis wins; the two options only disagree on
    /// the exact diagonals, which 2 gives to X and 3 to Y.
    /// </remarks>
    internal static Rotation? Direction(int system, int dx, int dy)
    {
        if (dx == 0 && dy == 0)
        {
            return null;
        }

        int ax = Math.Abs(dx);
        int ay = Math.Abs(dy);

        (int x, int y) = system switch
        {
            // tan(22.5°): inside it the move is along the axis, not the diagonal.
            1 when ay <= ax * 0.41421356 => (dx, 0),
            1 when ax <= ay * 0.41421356 => (0, dy),
            2 => ax >= ay ? (dx, 0) : (0, dy),
            3 => ay >= ax ? (0, dy) : (dx, 0),
            _ => (dx, dy),
        };

        return RotationExtensions.FromDelta(x, y);
    }

    /// <summary>The distance the flight time is counted over: the longest of the components the
    /// dialog ticked, or the plain step count when it ticked none.</summary>
    internal static double TimeDistance(double x, double y, double z, int steps)
    {
        double distance = Math.Max(x, Math.Max(y, z));

        return distance > 0 ? distance : steps;
    }

    /// <summary>
    /// The flight time for <paramref name="distance"/> tiles at <paramref name="perTile"/> ms each,
    /// every tile after the first <paramref name="increase"/> ms quicker than the one before and none
    /// quicker than 1 ms. A part tile (a height difference) counts as a whole one. A move that covers
    /// no distance keeps <paramref name="fallback"/>.
    /// </summary>
    internal static int ScaledTime(double distance, int perTile, int increase, int fallback)
    {
        if (distance <= 0)
        {
            return fallback;
        }

        long total = 0;

        for (int tile = 0; tile < Math.Ceiling(distance); tile++)
        {
            total += Math.Max(1L, perTile - ((long)increase * tile));
        }

        return (int)Math.Min(total, int.MaxValue);
    }

    /// <summary>
    /// The overshoot to send, in ms, for the dialog's distance mode — null when there is none.
    /// </summary>
    /// <remarks>
    /// The client stretches the slide to <c>(T + o) / T</c> of its length, so carrying on for X more
    /// tiles at the same speed is <c>X · T / steps</c>. "Always shoot X tiles" is the same with
    /// X − steps, and may be negative: a projectile told to fly 2 tiles and moved 5 stops short. The
    /// slide never shrinks below 1 ms, and a move of no steps has no direction to overshoot along.
    /// </remarks>
    internal static int? OvershootMs(int mode, int tiles, int steps, int time)
    {
        if (steps <= 0 || time <= 0)
        {
            return null;
        }

        int extra = mode switch
        {
            1 => tiles,
            2 => tiles - steps,
            _ => 0,
        };

        if (extra == 0)
        {
            return null;
        }

        long overshoot = (long)extra * time / steps;

        return (int)Math.Clamp(overshoot, 1 - time, int.MaxValue - time);
    }

    private static bool IsProjectile(IWiredSelectionSet selection, IRoomFloorItem item) =>
        !selection.HasFurni || selection.SelectedFurniIds.Contains(item.ObjectId.Value);

    /// <summary>Faces every shooter the way the projectile left — cosmetic, as the dialog says.
    /// The rotation offset is the projectile's sprite, not the shot, so it is not applied here.</summary>
    private async Task TurnShootersAsync(
        IWiredSelectionSet selection,
        int dx,
        int dy,
        IWiredExecutionContext ctx
    )
    {
        if (Direction(Param(DirectionSystem), dx, dy) is not Rotation heading)
        {
            return;
        }

        foreach (int playerId in selection.SelectedPlayerIds)
        {
            if (_ctx.Lookup.TryFindAvatarByPlayer(playerId, out IRoomAvatar? shooter))
            {
                await ctx.ProcessUserDirectionAsync(shooter, heading, heading);
            }
        }
    }

    /// <summary>
    /// A value-or-variable section's number: typed, or read from its variable.
    /// </summary>
    /// <remarks>
    /// The client sends both variable slots, time first, and the save drops the empty ones
    /// (<c>GetValidVariableIds</c>), so a lone distance variable arrives at index 0. Which slot a
    /// stored id belongs to is therefore read off which sections chose a variable. A variable that
    /// holds nothing falls back to the typed number rather than to zero, which would stop the
    /// projectile dead.
    /// </remarks>
    private int ValueOrVariable(
        int option,
        int value,
        int target,
        int slot,
        IWiredSelectionSet readFrom
    )
    {
        if (Param(option) != FromVariable)
        {
            return Param(value);
        }

        int index = slot == 1 && Param(TimePerTileOption) != FromVariable ? 0 : slot;

        if (
            index < _wiredData.VariableIds.Count
            && WiredVariableAccess.TryRead(
                _ctx.Furni,
                _wiredData.VariableIds[index],
                (WiredVariableTargetType)Param(target),
                readFrom,
                out WiredVariableValue read
            )
        )
        {
            return read.Value;
        }

        return Param(value);
    }

    private bool Flag(int index) => Param(index) != 0;

    private int Param(int index) =>
        index < _wiredData.IntParams.Count ? _wiredData.IntParams[index] : 0;
}
