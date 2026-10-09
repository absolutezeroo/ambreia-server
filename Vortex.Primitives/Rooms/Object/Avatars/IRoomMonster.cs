using Vortex.Primitives.Rooms.Object.Logic.Avatars;

namespace Vortex.Primitives.Rooms.Object.Avatars;

/// <summary>A monster's avatar in a zone (spec M1a §9.1): a Habbo pet named by its content id. It
/// runs no object logic: its AI is the zone simulation's.</summary>
public interface IRoomMonster : IRoomAvatar<IRoomMonster, IRoomMonsterLogic, IRoomMonsterContext>
{
    new IRoomMonsterLogic Logic { get; }

    /// <summary>The monster's content id (<c>monster.forest.wolf</c>).</summary>
    public string DefinitionId { get; }

    public int Level { get; }
}
