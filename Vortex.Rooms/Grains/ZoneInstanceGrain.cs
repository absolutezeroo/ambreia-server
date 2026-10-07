using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Grains;
using Vortex.Rooms.Configuration;

namespace Vortex.Rooms.Grains;

/// <summary>Hands out dungeon copies by group (plan D19–D23). In memory: copies do not survive the silo.</summary>
[KeepAlive]
public sealed class ZoneInstanceGrain(
    IOptions<RoomConfig> config,
    ILogger<ZoneInstanceGrain> logger
) : Grain, IZoneInstanceGrain
{
    private readonly Dictionary<(RoomId Template, string Group), RoomId> _byGroup = [];
    private readonly Dictionary<RoomId, (RoomId Template, string Group)> _byInstance = [];

    public Task<RoomId?> EnterAsync(RoomId template, string group)
    {
        if (_byGroup.TryGetValue((template, group), out RoomId known))
        {
            return Task.FromResult<RoomId?>(known);
        }

        int slots = Math.Min(config.Value.MaxInstancesPerZone, ZoneInstanceIds.SlotsPerZone - 1);
        RoomId? free = Enumerable
            .Range(1, slots)
            .Select(slot => ZoneInstanceIds.Of(template, slot))
            .FirstOrDefault(id => id is { } candidate && !_byInstance.ContainsKey(candidate));

        if (free is not { } instance)
        {
            logger.LogWarning(
                "No free copy of dungeon room {RoomId} for group {Group} ({Slots} in use)",
                template.Value,
                group,
                slots
            );

            return Task.FromResult<RoomId?>(null);
        }

        _byGroup[(template, group)] = instance;
        _byInstance[instance] = (template, group);

        return Task.FromResult<RoomId?>(instance);
    }

    public Task<bool> IsMemberAsync(RoomId instance, string group) =>
        Task.FromResult(
            _byInstance.TryGetValue(instance, out (RoomId Template, string Group) owner)
                && owner.Group == group
        );

    public Task ReleaseAsync(RoomId instance)
    {
        if (_byInstance.Remove(instance, out (RoomId Template, string Group) owner))
        {
            _byGroup.Remove(owner);
        }

        return Task.CompletedTask;
    }
}
