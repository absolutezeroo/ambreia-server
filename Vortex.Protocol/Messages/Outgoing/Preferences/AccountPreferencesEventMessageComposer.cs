using Orleans;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players.Enums;

namespace Vortex.Protocol.Messages.Outgoing.Preferences;

[GenerateSerializer, Immutable]
public sealed record AccountPreferencesEventMessageComposer : IComposer
{
    [Id(0)]
    public required int UIVolume { get; init; }

    [Id(1)]
    public required int FurniVolume { get; init; }

    [Id(2)]
    public required int TraxVolume { get; init; }

    [Id(3)]
    public required bool FreeFlowChatDisabled { get; init; }

    [Id(4)]
    public required bool RoomInvitesIgnored { get; init; }

    [Id(5)]
    public required bool RoomCameraFollowDisabled { get; init; }

    [Id(6)]
    public required UIFlags UIFlags { get; init; }

    // [Id(7)] was PreferedChatStyle, retired with chat styles (M0 bis): never reuse it. The wire
    // field is written as 0 by the serializer.

    [Id(8)]
    public required bool WiredMenuButton { get; init; }

    [Id(9)]
    public required bool WiredInspectButton { get; init; }

    [Id(10)]
    public required bool PlayTestMode { get; init; }

    [Id(11)]
    public required int VariableSyntaxMode { get; init; }

    [Id(12)]
    public required bool WiredWhisperDisabled { get; init; }

    [Id(13)]
    public required bool ShowAllNotifications { get; init; }

    [Id(14)]
    public required string UiStyle { get; init; }

    // The client's four trailing optional reads (_SafePkg_1927/_SafeCls_1926.as:179-210). It guards
    // each with `bytesAvailable > 0` and falls back to its own defaults, which is why they were
    // survivable to omit — and why omitting them meant the chat dialog forgot itself every login.
    [Id(15)]
    public required int ChatSizePreference { get; init; }

    [Id(16)]
    public required ChatModeType ChatMode { get; init; }

    [Id(17)]
    public required ChatBubbleWidthType ChatBubbleWidth { get; init; }

    [Id(18)]
    public required ChatScrollSpeedType ChatScrollSpeed { get; init; }
}
