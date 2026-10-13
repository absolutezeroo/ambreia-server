using Vortex.Primitives.Rooms.Object.Logic.Avatars;

namespace Vortex.Primitives.Rooms.Object.Avatars;

/// <summary>A zone's NPC in its room (spec M2a §3.3): a Habbo bot that never walks and is no combatant.</summary>
public interface IRoomNpc : IRoomAvatar<IRoomNpc, IRoomNpcLogic, IRoomNpcContext>
{
    new IRoomNpcLogic Logic { get; }

    /// <summary>Its id in the zone document (<c>elder</c>).</summary>
    public string NpcId { get; }

    /// <summary>The dialogue it opens, or null.</summary>
    public string? Dialogue { get; }
}
