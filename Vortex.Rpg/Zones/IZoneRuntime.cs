using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rpg.Zones;

/// <summary>
/// What a zone gives combat and monsters (spec M1a §4.2, spec §3.3): the grid, the avatars, walking,
/// sending, the clock. V1: <c>RoomGrain</c> implements it. Synchronous, since it is called inside the
/// zone's own turn; only sending is asynchronous.
/// </summary>
public interface IZoneRuntime
{
    /// <summary>The zone's room, for the events it publishes.</summary>
    public RoomId ZoneId { get; }

    /// <summary>The instant of the tick being run, in milliseconds.</summary>
    public long NowMs { get; }

    public Random Random { get; }

    public bool IsWalkable(int x, int y);

    /// <summary>Walkable and nobody stands there: where a monster may be born or wander to.</summary>
    public bool IsFree(int x, int y);

    /// <summary>No wall, closed or disabled tile between the two cells (the ends excluded).</summary>
    public bool HasLineOfSight(int fromX, int fromY, int toX, int toY);

    public ZoneCellRules RulesAt(int x, int y);

    /// <summary>Where an avatar of this zone stands, or null.</summary>
    public (int X, int Y)? PositionOf(int objectId);

    public bool IsWalking(int objectId);

    /// <summary>Puts a monster avatar (a Habbo pet) on a free cell; its room object id.</summary>
    public int SpawnMonster(MonsterAppearance appearance, int x, int y);

    public void RemoveMonster(int objectId);

    /// <summary>
    /// Walks an avatar to the nearest free cell within <paramref name="within"/> tiles (Chebyshev) of
    /// (x, y), in sight of it — 0: onto the cell itself. The zone applies each avatar's own rules: a
    /// monster never steps on a safe cell. Monster paths count against the zone's budget per tick.
    /// </summary>
    public WalkResult WalkNear(int objectId, int x, int y, int within);

    public void StopWalking(int objectId);

    public void Teleport(int objectId, int x, int y);

    /// <summary>"lay" while a ghost, "ded" for a killed monster's body; null to stand again. Either
    /// one keeps the avatar from walking.</summary>
    public void SetPosture(int objectId, string? posture);

    /// <summary>Turns the avatar, body and head, toward (x, y): a fighter faces what it strikes.</summary>
    public void Face(int objectId, int x, int y);

    /// <summary>How fast the avatar walks, in percent of normal (a slow, spec M1b §7.2).</summary>
    public void SetMoveSpeed(int objectId, int percent);

    /// <summary>Rooted: the walk stops and none starts (a stun, spec M1b §7.2); a ghost or a body
    /// stays unable to walk when freed.</summary>
    public void SetRooted(int objectId, bool rooted);

    /// <summary>The zone's <c>respawn</c> entry, else its default arrival; null without either.</summary>
    public (int X, int Y)? RespawnCell();

    /// <summary>
    /// The zone's triggers of this type and target run for the player once the tick is over (spec
    /// Z1 §5): a monster killed, or whatever else the simulation sees happen. Nothing for a player
    /// who left. For the simulation's tick only: raised from a trigger's own action, it would run
    /// outside that trigger's chain and its limits.
    /// </summary>
    public void RaiseTrigger(PlayerId player, ZoneTriggerType type, string target);

    /// <summary>Whether every condition holds for nobody in particular (a spawn's, spec Z1 §6.3):
    /// asked for <see cref="PlayerId.Invalid"/> with no facts, a rule that needs a player is false.</summary>
    public bool ConditionsHold(IReadOnlyList<ZoneCondition> conditions);

    /// <summary>To everyone in the zone.</summary>
    public Task BroadcastAsync(IComposer composer);

    public Task SendToPlayerAsync(PlayerId player, IComposer composer);
}
