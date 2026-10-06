using System;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Quests;
using Vortex.Primitives.Quests.Snapshots;
using Vortex.Protocol.Messages.Outgoing.Quest;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Quests;

/// <summary>
///     The daily-task board, pinned against the client's own structs
///     (<c>unknowns/_SafePkg_2992/_SafeCls_2991.as</c> and its reward <c>_SafeCls_4416.as</c>).
///
///     Three field widths in here are not the obvious ones, and each would desynchronise everything
///     after it: the task id is a <b>long</b>, the status is a <b>byte</b>, and a reward's product
///     item type is a <b>short</b>.
/// </summary>
public sealed class DailyTaskWireLayoutTests
{
    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    [Fact]
    public void ActiveList_WritesACountThenEachTaskWithTheClientsFieldWidths()
    {
        ClientPacket packet = Serialize(
            new DailyTasksActiveListMessageComposer
            {
                Tasks =
                [
                    new DailyTaskSnapshot
                    {
                        TaskId = 8_000_000_001,
                        TaskCode = "visit_rooms",
                        QuestTypeCode = "RoomEntry",
                        IsBonus = false,
                        ImageVersion = "v3",
                        CatalogName = "top_page",
                        RequiredRepeats = 5,
                        Repeats = 2,
                        Status = DailyTaskStatus.Available,
                        SecondsLeft = 3600,
                        Rewards =
                        [
                            new DailyTaskRewardSnapshot
                            {
                                ProductItemTypeId = 7,
                                RewardTypeId = "credits",
                                ExtraParams = "",
                                Amount = 50,
                            },
                        ],
                    },
                ],
            }
        );

        packet.PopInt().Should().Be(1);

        // A long, not an int: an id past 2^31 has to survive the round trip.
        packet.PopLong().Should().Be(8_000_000_001);
        packet.PopString().Should().Be("visit_rooms");
        packet.PopString().Should().Be("RoomEntry");
        packet.PopBoolean().Should().BeFalse();
        packet.PopString().Should().Be("v3");
        packet.PopString().Should().Be("top_page");
        packet.PopInt().Should().Be(5);
        packet.PopInt().Should().Be(2);
        packet.PopByte().Should().Be(0); // status is one byte
        packet.PopInt().Should().Be(3600);

        packet.PopInt().Should().Be(1);
        packet.PopShort().Should().Be(7); // product item type is a short
        packet.PopString().Should().Be("credits");
        packet.PopString().Should().BeEmpty();
        packet.PopInt().Should().Be(50);
        packet.Remaining.Should().Be(0);
    }

    [Fact]
    public void ActiveList_WritesAnEmptyBoardAsAZeroCount()
    {
        ClientPacket packet = Serialize(new DailyTasksActiveListMessageComposer { Tasks = [] });

        packet.PopInt().Should().Be(0);
        packet.Remaining.Should().Be(0);
    }

    [Fact]
    public void TaskUpdate_WritesIdRepeatsStatusSecondsLeft()
    {
        ClientPacket packet = Serialize(
            new DailyTasksTaskUpdateMessageComposer
            {
                TaskId = 42,
                Repeats = 5,
                Status = DailyTaskStatus.Completed,
                SecondsLeft = 120,
            }
        );

        packet.PopLong().Should().Be(42);
        packet.PopInt().Should().Be(5);
        packet.PopByte().Should().Be(1);
        packet.PopInt().Should().Be(120);
        packet.Remaining.Should().Be(0);
    }

    [Fact]
    public void TaskUpdate_KeepsANegativeSecondsLeft()
    {
        // The client has no expiry flag: its isExpired is "secondsLeft < 0 and not still available".
        // Clamping this at zero would leave a lapsed task looking live forever.
        ClientPacket packet = Serialize(
            new DailyTasksTaskUpdateMessageComposer
            {
                TaskId = 42,
                Repeats = 1,
                Status = DailyTaskStatus.Claimed,
                SecondsLeft = -30,
            }
        );

        packet.PopLong().Should().Be(42);
        packet.PopInt().Should().Be(1);
        packet.PopByte().Should().Be(2);
        packet.PopInt().Should().Be(-30);
    }

    private static ClientPacket Serialize<T>(T composer)
        where T : IComposer
    {
        byte[] bytes = Revision.Serializers[typeof(T)].Serialize(composer).ToArray();

        byte[] payload = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, payload, 0, payload.Length);

        return new ClientPacket(0, payload);
    }
}
