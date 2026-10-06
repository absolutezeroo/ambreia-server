using Vortex.Primitives.Rooms.Enums;

namespace Vortex.PacketHandlers;

/// <summary>
/// Parses the classic Habbo wall-item location string (e.g. ":w=X,Y l=WallOffset,Z r=side"),
/// as Room.Engine.PlaceObjectMessageHandler receives it.
/// </summary>
internal static class WallLocationParser
{
    public static bool TryParse(
        string location,
        out int x,
        out int y,
        out double z,
        out int wallOffset,
        out Rotation rotation
    )
    {
        x = 0;
        y = 0;
        z = 0;
        wallOffset = 0;
        rotation = Rotation.West;

        if (!location.StartsWith(':'))
        {
            return false;
        }

        string[] position = location.Split(' ');

        if (position.Length != 3)
        {
            return false;
        }

        string[] coords = position[0][3..].Split(',');
        string[] loc = position[1][2..].Split(',');

        if (coords.Length != 2 || loc.Length != 2)
        {
            return false;
        }

        rotation = position[2].Equals("l") ? Rotation.South : Rotation.West;

        return int.TryParse(coords[0], out x)
            && int.TryParse(coords[1], out y)
            && int.TryParse(loc[0], out wallOffset)
            && double.TryParse(loc[1], out z);
    }
}
