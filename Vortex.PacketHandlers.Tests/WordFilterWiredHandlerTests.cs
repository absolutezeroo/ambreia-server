using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.PacketHandlers.UserDefinedRoomEvents.Wiredtrading;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Rooms.Snapshots.Wired;
using Vortex.Protocol.Messages.Incoming.Userdefinedroomevents.Wiredtrading;
using Vortex.Protocol.Messages.Incoming.Userdefinedroomevents.Wiredtrading.Contracts;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Protocol.Messages.Outgoing.Userdefinedroomevents.Wiredtrading;
using Vortex.Protocol.Messages.Outgoing.Userdefinedroomevents.Wiredtrading.Contracts;
using Xunit;

namespace Vortex.PacketHandlers.Tests;

/// <summary>The hotel-wide word filter on the wired trade screens: chests and contracts.</summary>
public sealed class WordFilterWiredHandlerTests : WordFilterHandlerFixture
{
    /// <summary>The success reply is what closes the dialog, so a refusal must not send it.</summary>
    [Fact]
    public async Task ARefusedChest_IsNeverSaved_AndItsDialogStaysOpen()
    {
        await new SaveWiredChestSettingsMessageHandler(Grains(), RefusingOnly("con")).HandleAsync(
            Chest(name: "ok", description: "con"),
            Context(roomId: 4),
            CancellationToken.None
        );

        GrainCalls.Should().NotContain(c => c.Method.Name == "SaveWiredChestSettingsAsync");
        Sent.Should().ContainSingle().Which.Should().BeOfType<NotificationDialogMessageComposer>();
        Sent.Should().NotContain(s => s is WiredChestUpdateSuccessMessageComposer);
    }

    [Fact]
    public async Task AReplacedChest_IsSavedReplaced()
    {
        await new SaveWiredChestSettingsMessageHandler(Grains(), Replacing("bobba")).HandleAsync(
            Chest(name: "con", description: "con"),
            Context(roomId: 4),
            CancellationToken.None
        );

        object?[] args = GrainCall("SaveWiredChestSettingsAsync");
        args[2].Should().Be("bobba");
        args[3].Should().Be("bobba");
        AppliedSources.Should().OnlyContain(s => s == WordFilterSource.Wired);
    }

    [Fact]
    public async Task ARefusedContract_IsNeverSaved_AndTheEditorIsToldWhy()
    {
        await new SaveWiredContractMessageHandler(Grains(), RefusingOnly("con")).HandleAsync(
            Contract(receive: "con", reward: "ok"),
            Context(roomId: 4),
            CancellationToken.None
        );

        GrainCalls.Should().NotContain(c => c.Method.Name == "SaveWiredContractAsync");
        Sent.Should().ContainSingle(s => s is NotificationDialogMessageComposer);
        Sent.Should()
            .ContainSingle(s => s is WiredContractUpdateResultMessageComposer)
            .Which.As<WiredContractUpdateResultMessageComposer>()
            .IsSuccess.Should()
            .BeFalse();
    }

    [Fact]
    public async Task AReplacedContract_IsSavedReplaced()
    {
        await new SaveWiredContractMessageHandler(Grains(), Replacing("bobba")).HandleAsync(
            Contract(receive: "con", reward: "con"),
            Context(roomId: 4),
            CancellationToken.None
        );

        WiredContractSnapshot saved = (WiredContractSnapshot)
            GrainCall("SaveWiredContractAsync")[1]!;
        saved.ReceiveText.Should().Be("bobba");
        saved.RewardText.Should().Be("bobba");
    }

    private static SaveWiredChestSettingsMessage Chest(string name, string description) =>
        new()
        {
            ChestId = 5,
            Name = name,
            Description = description,
            EveryoneCanOpen = false,
            EveryoneCanDonate = false,
            ChestState = 0,
            PreviewItems = 0,
            PreviewAmount = 0,
            UpgradeButtonDisabled = false,
        };

    private static SaveWiredContractMessage Contract(string receive, string reward) =>
        new()
        {
            Contract = new WiredContractSnapshot
            {
                ContractId = 6,
                ContractType = 0,
                ReceiveText = receive,
                RewardText = reward,
            },
        };
}
