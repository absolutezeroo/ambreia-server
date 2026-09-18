using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Roomsettings;
using Vortex.Revisions.Configuration;
using Vortex.Revisions.Revision20260701.Parsers.RoomSettings;
using Vortex.Revisions.Revision20260701.Serializers.RoomSettings;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class RoomSettingsMap : IRevisionMap
{
    private readonly ProtocolLimitsConfig _protocolLimits;

    public RoomSettingsMap(ProtocolLimitsConfig protocolLimits)
    {
        _protocolLimits = protocolLimits;
    }

    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(MessageEvent.DeleteRoomMessageEvent, new DeleteRoomMessageParser());
        builder.MapParser(
            MessageEvent.GetBannedUsersFromRoomMessageEvent,
            new GetBannedUsersFromRoomMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetCustomRoomFilterMessageEvent,
            new GetCustomRoomFilterMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetFlatControllersMessageEvent,
            new GetFlatControllersMessageParser()
        );
        builder.MapParser(
            MessageEvent.GetRoomSettingsMessageEvent,
            new GetRoomSettingsMessageParser()
        );
        builder.MapParser(
            MessageEvent.SaveRoomSettingsMessageEvent,
            new SaveRoomSettingsMessageParser(_protocolLimits.MaxRoomTags)
        );
        builder.MapParser(
            MessageEvent.UpdateRoomCategoryAndTradeSettingsEvent,
            new UpdateRoomCategoryAndTradeSettingsMessageParser()
        );
        builder.MapParser(
            MessageEvent.UpdateRoomFilterMessageEvent,
            new UpdateRoomFilterMessageParser()
        );

        // This map registered parsers only, so every room-settings composer the server built was
        // dropped for want of a serializer — the settings dialog never received a single byte.
        // The remaining composers in this domain are still empty-bodied stubs; registering those
        // would put truncated packets on the wire, so they stay unmapped until they are written.
        builder.MapSerializer(
            typeof(RoomSettingsDataEventMessageComposer),
            new RoomSettingsDataEventMessageComposerSerializer(
                MessageComposer.RoomSettingsDataComposer
            )
        );
        builder.MapSerializer(
            typeof(RoomSettingsSavedEventMessageComposer),
            new RoomSettingsSavedEventMessageComposerSerializer(
                MessageComposer.RoomSettingsSavedComposer
            )
        );
        builder.MapSerializer(
            typeof(FlatControllersEventMessageComposer),
            new FlatControllersEventMessageComposerSerializer(
                MessageComposer.FlatControllersComposer
            )
        );
        builder.MapSerializer(
            typeof(BannedUsersFromRoomEventMessageComposer),
            new BannedUsersFromRoomEventMessageComposerSerializer(
                MessageComposer.BannedUsersFromRoomComposer
            )
        );
        builder.MapSerializer(
            typeof(UserUnbannedFromRoomEventMessageComposer),
            new UserUnbannedFromRoomEventMessageComposerSerializer(
                MessageComposer.UserUnbannedFromRoomComposer
            )
        );
        builder.MapSerializer(
            typeof(MuteAllInRoomEventMessageComposer),
            new MuteAllInRoomEventMessageComposerSerializer(MessageComposer.MuteAllInRoomComposer)
        );
    }
}
