using Vortex.Primitives.Orleans.Snapshots.Navigator;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Packets;
using Vortex.Revisions.Revision20260701.Serializers.Navigator.Data;

namespace Vortex.Revisions.Revision20260701.Serializers.NewNavigator.Data;

internal class NavigatorSearchResultBlockSerializer
{
    public static void Serialize(IServerPacket packet, NavigatorSearchResultBlockSnapshot message)
    {
        packet
            .WriteString(message.SearchCode)
            .WriteString(message.Text)
            .WriteInteger((int)message.ActionAllowed)
            .WriteBoolean(message.ForceClosed)
            .WriteInteger((int)message.ViewMode)
            .WriteInteger(message.Results.Length);

        foreach (RoomInfoSnapshot result in message.Results)
        {
            RoomSettingsSerializer.Serialize(packet, result);
        }
    }
}
