using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Furniture;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Primitives.Rooms.Snapshots.Wired.Variables;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Addons;

namespace Vortex.Rooms.Wired;

/// <summary>
/// Global placeholders read straight off stored furni rows, for the ones that live in other rooms.
/// </summary>
/// <remarks>
/// The same trade <see cref="Variables.WiredSharedVariableIndex"/> makes: another room is usually
/// unloaded, and waking it to ask what it defines is not an option, so the box's configuration is
/// read from the JSON on its row. That lags a loaded room's unsaved edits by one save pass. Only
/// rooms the player owns are offered, as with shared variables, and only placeholders set to a value
/// — one that itself points elsewhere is not a source.
/// </remarks>
public static class WiredGlobalPlaceholderIndex
{
    private static readonly string Logic = typeof(WiredAddonGlobalPlaceholder)
        .GetCustomAttributes<RoomObjectLogicAttribute>()
        .Select(a => a.Key)
        .First();

    /// <summary>The placeholders the owner's other rooms define, by room name then name.</summary>
    public static async Task<List<WiredSharedPlaceholderSnapshot>> LoadForOwnerAsync(
        VortexDbContext dbCtx,
        int ownerId,
        int excludeRoomId,
        CancellationToken ct
    )
    {
        var rows = await dbCtx
            .Furnitures.AsNoTracking()
            .Where(f =>
                f.RoomEntityId != null
                && f.RoomEntityId != excludeRoomId
                && f.DeletedAt == null
                && f.RoomEntity!.PlayerEntityId == ownerId
                && f.FurnitureDefinitionEntity!.Logic == Logic
            )
            .Select(f => new
            {
                f.ExtraData,
                RoomId = f.RoomEntityId!.Value,
                RoomName = f.RoomEntity!.Name,
            })
            .ToListAsync(ct);

        List<WiredSharedPlaceholderSnapshot> placeholders = [];

        foreach (var row in rows)
        {
            if (TryReadValued(row.ExtraData, out string name, out _))
            {
                placeholders.Add(
                    new WiredSharedPlaceholderSnapshot
                    {
                        RoomId = row.RoomId,
                        RoomName = row.RoomName,
                        PlaceholderName = name,
                    }
                );
            }
        }

        return
        [
            .. placeholders
                .OrderBy(p => p.RoomName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.PlaceholderName, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>The value room <paramref name="roomId"/> gives placeholder <paramref name="name"/>,
    /// or null when it no longer defines one.</summary>
    public static async Task<string?> ResolveAsync(
        VortexDbContext dbCtx,
        int roomId,
        string name,
        CancellationToken ct
    )
    {
        List<string?> rows = await dbCtx
            .Furnitures.AsNoTracking()
            .Where(f =>
                f.RoomEntityId == roomId
                && f.DeletedAt == null
                && f.FurnitureDefinitionEntity!.Logic == Logic
            )
            .Select(f => f.ExtraData)
            .ToListAsync(ct);

        foreach (string? extraData in rows)
        {
            if (
                TryReadValued(extraData, out string candidate, out string value)
                && string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)
            )
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>A stored box set to a value: its name and the value.</summary>
    private static bool TryReadValued(string? extraData, out string name, out string value)
    {
        name = value = string.Empty;

        if (
            !new ExtraData(extraData).TryGetSection(
                ExtraDataSectionType.WIRED,
                out JsonElement wiredElement
            )
        )
        {
            return false;
        }

        WiredData? data = wiredElement.Deserialize<WiredData>();

        if (data is null || (data.IntParams.Count > 0 && data.IntParams[0] != 0))
        {
            return false;
        }

        (name, value) = WiredAddonGlobalPlaceholder.ParseConfiguration(data.StringParam);

        return name.Length > 0;
    }
}
