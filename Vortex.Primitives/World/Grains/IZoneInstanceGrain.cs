using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Rooms;

namespace Vortex.Primitives.World.Grains;

/// <summary>
/// The dungeon copies (plan D16, D19–D23): which copy of which dungeon each group plays. One
/// activation for the silo, in memory: a copy lives as long as its room does.
/// </summary>
[Alias("Vortex.Primitives.World.Grains.IZoneInstanceGrain")]
public interface IZoneInstanceGrain : IGrainWithIntegerKey
{
    /// <summary>The group's copy of <paramref name="template"/>, made if needed; null when every copy is taken.</summary>
    public Task<RoomId?> EnterAsync(RoomId template, string group);

    /// <summary>Whether <paramref name="group"/> was given this copy; false for a copy nobody holds.</summary>
    public Task<bool> IsMemberAsync(RoomId instance, string group);

    /// <summary>The copy's room closed: its group gets a fresh one next time.</summary>
    public Task ReleaseAsync(RoomId instance);
}
