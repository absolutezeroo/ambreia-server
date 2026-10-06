using System;
using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Advertisement;
using Vortex.Protocol.Messages.Outgoing.Availability;
using Vortex.Protocol.Messages.Outgoing.Callforhelp;
using Vortex.Protocol.Messages.Outgoing.Campaign;
using Vortex.Protocol.Messages.Outgoing.Catalog;
using Vortex.Protocol.Messages.Outgoing.Handshake;
using Vortex.Protocol.Messages.Outgoing.Inventory.Trading;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Protocol.Messages.Outgoing.Quest;
using Vortex.Protocol.Messages.Outgoing.Room.Chat;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Protocol.Messages.Outgoing.Room.Furniture;
using Vortex.Protocol.Messages.Outgoing.Room.Session;
using Vortex.Protocol.Messages.Outgoing.Users;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Room;

/// <summary>
///     The rest of the serializers that were registered with an empty <c>Serialize</c> body while
///     the client's parser reads off them.
///
///     Every shape here was taken from the WIN63 parser named in each test, not from the client's
///     TypeScript port of it. That distinction earned itself: <c>SanctionStatus</c>'s port was
///     written against a thirteen-field message that exists only in the <c>win63_version</c> dump,
///     and had it been trusted, this file would have pinned the wrong layout in a passing test.
///
///     None of these is built by a handler yet. <c>node scripts/unlistened-server-messages.mjs</c>
///     in the client repo re-measures what is left.
/// </summary>
public sealed class EmptySerializerBatchWireTests
{
    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    private static ClientPacket Body(Type composerType, IComposer composer)
    {
        byte[] bytes = Revision.Serializers[composerType].Serialize(composer).ToArray();
        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);
        return new ClientPacket(0, body);
    }

    /// <summary>WIN63 unknowns/_SafePkg_1719/_SafeCls_1718.as (header 3898).</summary>
    [Fact]
    public void Interstitial_WritesSingleBoolean()
    {
        ClientPacket packet = Body(
            typeof(InterstitialMessageComposer),
            new InterstitialMessageComposer { CanShowInterstitial = true }
        );

        packet.PopBoolean().Should().BeTrue();
    }

    /// <summary>WIN63 unknowns/_SafePkg_2152/_SafeCls_2483.as (header 184).</summary>
    [Fact]
    public void InfoHotelClosing_WritesMinutes()
    {
        ClientPacket packet = Body(
            typeof(InfoHotelClosingMessageComposer),
            new InfoHotelClosingMessageComposer { MinutesUntilClosing = 12 }
        );

        packet.PopInt().Should().Be(12);
    }

    /// <summary>
    ///     WIN63 unknowns/_SafePkg_2152/_SafeCls_2990.as (header 698). Two fields, where the
    ///     hotel-closed notification below carries three - they are separate parser classes and the
    ///     shorter one must not grow to match.
    /// </summary>
    [Fact]
    public void LoginFailedHotelClosed_WritesOpeningTimeOnly()
    {
        ClientPacket packet = Body(
            typeof(LoginFailedHotelClosedMessageComposer),
            new LoginFailedHotelClosedMessageComposer { OpenHour = 7, OpenMinute = 30 }
        );

        packet.PopInt().Should().Be(7);
        packet.PopInt().Should().Be(30);
        packet.Remaining.Should().Be(0);
    }

    /// <summary>WIN63 unknowns/_SafePkg_2152/_SafeCls_3608.as (header 3058).</summary>
    [Fact]
    public void InfoHotelClosed_WritesOpeningTimeAndThrownOutFlag()
    {
        ClientPacket packet = Body(
            typeof(InfoHotelClosedMessageComposer),
            new InfoHotelClosedMessageComposer
            {
                OpenHour = 7,
                OpenMinute = 30,
                UserThrownOutAtClose = true,
            }
        );

        packet.PopInt().Should().Be(7);
        packet.PopInt().Should().Be(30);
        packet.PopBoolean().Should().BeTrue();
    }

    /// <summary>
    ///     WIN63 unknowns/_SafePkg_2152/_SafeCls_3162.as (header 1737). The duration is read behind
    ///     a bytes-available guard on the client, which defaults it to 15; this serializer always
    ///     writes it, so the default never applies.
    /// </summary>
    [Fact]
    public void MaintenanceStatus_WritesDurationEvenThoughTheClientGuardsIt()
    {
        ClientPacket packet = Body(
            typeof(MaintenanceStatusMessageComposer),
            new MaintenanceStatusMessageComposer
            {
                IsInMaintenance = false,
                MinutesUntilMaintenance = 45,
            }
        );

        packet.PopBoolean().Should().BeFalse();
        packet.PopInt().Should().Be(45);
        packet.PopInt().Should().Be(15);
    }

    /// <summary>WIN63 unknowns/_SafePkg_1714/_SafeCls_3506.as (header 773).</summary>
    [Fact]
    public void CatalogPublished_WritesFlagThenHash()
    {
        ClientPacket packet = Body(
            typeof(CatalogPublishedMessageComposer),
            new CatalogPublishedMessageComposer
            {
                InstantlyRefreshCatalogue = true,
                NewFurniDataHash = "abc123",
            }
        );

        packet.PopBoolean().Should().BeTrue();
        packet.PopString().Should().Be("abc123");
    }

    /// <summary>WIN63 unknowns/_SafePkg_3317/_SafeCls_3336.as (header 2164).</summary>
    [Fact]
    public void CampaignCalendarDoorOpened_WritesAllThreeStringsEvenWhenRefused()
    {
        ClientPacket packet = Body(
            typeof(CampaignCalendarDoorOpenedMessageComposer),
            new CampaignCalendarDoorOpenedMessageComposer
            {
                DoorOpened = false,
                ProductName = string.Empty,
                CustomImage = string.Empty,
                FurnitureClassName = string.Empty,
            }
        );

        packet.PopBoolean().Should().BeFalse();
        packet.PopString().Should().BeEmpty();
        packet.PopString().Should().BeEmpty();
        packet.PopString().Should().BeEmpty();
    }

    /// <summary>WIN63 unknowns/_SafePkg_2546/_SafeCls_4291.as (header 3497).</summary>
    [Fact]
    public void TradeSilverFee_WritesFee()
    {
        ClientPacket packet = Body(
            typeof(TradeSilverFeeMessageComposer),
            new TradeSilverFeeMessageComposer { SilverFee = 25 }
        );

        packet.PopInt().Should().Be(25);
    }

    /// <summary>WIN63 unknowns/_SafePkg_2546/_SafeCls_2855.as (header 1490).</summary>
    [Fact]
    public void TradeSilverSet_WritesOwnStakeFirst()
    {
        ClientPacket packet = Body(
            typeof(TradeSilverSetMessageComposer),
            new TradeSilverSetMessageComposer { PlayerSilver = 10, OtherPlayerSilver = 5 }
        );

        packet.PopInt().Should().Be(10);
        packet.PopInt().Should().Be(5);
    }

    /// <summary>WIN63 unknowns/_SafePkg_1810/_SafeCls_4146.as (header 1807).</summary>
    [Fact]
    public void ElementPointer_WritesKey()
    {
        ClientPacket packet = Body(
            typeof(ElementPointerMessageComposer),
            new ElementPointerMessageComposer { Key = "navigator" }
        );

        packet.PopString().Should().Be("navigator");
    }

    /// <summary>WIN63 unknowns/_SafePkg_1810/_SafeCls_2614.as (header 334).</summary>
    [Fact]
    public void HabboBroadcast_WritesText()
    {
        ClientPacket packet = Body(
            typeof(HabboBroadcastMessageComposer),
            new HabboBroadcastMessageComposer { MessageText = "Server restarting" }
        );

        packet.PopString().Should().Be("Server restarting");
    }

    /// <summary>
    ///     WIN63 unknowns/_SafePkg_1810/_SafeCls_2693.as (header 3059). This one hid behind a wrong
    ///     map entry: the type was registered against the account-preferences serializer, which
    ///     does write bytes, so the audit that finds empty bodies never flagged it. Repairing the
    ///     pairing is what made it visible.
    /// </summary>
    [Fact]
    public void UnseenItems_WritesCategoriesEachWithItsOwnItemCount()
    {
        ClientPacket packet = Body(
            typeof(UnseenItemsEventMessageComposer),
            new UnseenItemsEventMessageComposer
            {
                Categories =
                [
                    new UnseenItemCategory { CategoryId = 1, ItemIds = [10, 11] },
                    new UnseenItemCategory { CategoryId = 3, ItemIds = [] },
                ],
            }
        );

        packet.PopInt().Should().Be(2);

        packet.PopInt().Should().Be(1);
        packet.PopInt().Should().Be(2);
        packet.PopInt().Should().Be(10);
        packet.PopInt().Should().Be(11);

        packet.PopInt().Should().Be(3);
        packet.PopInt().Should().Be(0);
        packet.Remaining.Should().Be(0);
    }

    /// <summary>WIN63 unknowns/_SafePkg_1810/_SafeCls_2688.as (header 2243).</summary>
    [Fact]
    public void NotificationDialog_WritesTypeThenCountedPairs()
    {
        ClientPacket packet = Body(
            typeof(NotificationDialogMessageComposer),
            new NotificationDialogMessageComposer
            {
                Type = "furni_placement_error",
                Parameters =
                [
                    new NotificationDialogParameter { Key = "message", Value = "no_rights" },
                    new NotificationDialogParameter { Key = "image", Value = string.Empty },
                ],
            }
        );

        packet.PopString().Should().Be("furni_placement_error");
        packet.PopInt().Should().Be(2);
        packet.PopString().Should().Be("message");
        packet.PopString().Should().Be("no_rights");
        packet.PopString().Should().Be("image");
        packet.PopString().Should().BeEmpty();
    }

    /// <summary>WIN63 unknowns/_SafePkg_2942/_SafeCls_3790.as (header 1740).</summary>
    [Fact]
    public void RoomMessageNotification_WritesRoomThenCount()
    {
        ClientPacket packet = Body(
            typeof(RoomMessageNotificationMessageComposer),
            new RoomMessageNotificationMessageComposer
            {
                RoomId = 4711,
                RoomName = "My room",
                MessageCount = 3,
            }
        );

        packet.PopInt().Should().Be(4711);
        packet.PopString().Should().Be("My room");
        packet.PopInt().Should().Be(3);
    }

    /// <summary>WIN63 unknowns/_SafePkg_2918/_SafeCls_3620.as (header 3208).</summary>
    [Fact]
    public void RoomFilterSettings_WritesCountedWordList()
    {
        ClientPacket packet = Body(
            typeof(RoomFilterSettingsMessageComposer),
            new RoomFilterSettingsMessageComposer { BadWords = ["foo", "bar"] }
        );

        packet.PopInt().Should().Be(2);
        packet.PopString().Should().Be("foo");
        packet.PopString().Should().Be("bar");
    }

    /// <summary>WIN63 unknowns/_SafePkg_1891/_SafeCls_2001.as (header 3913).</summary>
    [Fact]
    public void AccountSafetyLockStatusChange_WritesStatus()
    {
        ClientPacket packet = Body(
            typeof(AccountSafetyLockStatusChangeMessageComposer),
            new AccountSafetyLockStatusChangeMessageComposer { Status = 1 }
        );

        packet.PopInt().Should().Be(1);
    }

    /// <summary>WIN63 unknowns/_SafePkg_1891/_SafeCls_4028.as (header 2050).</summary>
    [Fact]
    public void ChangeEmailResult_WritesResult()
    {
        ClientPacket packet = Body(
            typeof(ChangeEmailResultEventMessageComposer),
            new ChangeEmailResultEventMessageComposer { Result = 0 }
        );

        packet.PopInt().Should().Be(0);
    }

    /// <summary>WIN63 unknowns/_SafePkg_1891/_SafeCls_1994.as (header 2343).</summary>
    [Fact]
    public void EmailStatusResult_WritesAddressThenBothFlags()
    {
        ClientPacket packet = Body(
            typeof(EmailStatusResultEventMessageComposer),
            new EmailStatusResultEventMessageComposer
            {
                Email = "player@example.com",
                IsVerified = true,
                AllowChange = false,
            }
        );

        packet.PopString().Should().Be("player@example.com");
        packet.PopBoolean().Should().BeTrue();
        packet.PopBoolean().Should().BeFalse();
    }

    /// <summary>WIN63 unknowns/_SafePkg_1820/_SafeCls_4080.as (header 1973).</summary>
    [Fact]
    public void UniqueMachineId_WritesMachineId()
    {
        ClientPacket packet = Body(
            typeof(UniqueMachineIdMessage),
            new UniqueMachineIdMessage { MachineID = "abc-def" }
        );

        packet.PopString().Should().Be("abc-def");
    }

    /// <summary>
    ///     WIN63 unknowns/_SafePkg_1976/_SafeCls_4488.as via _SafeCls_4504.as (header 363).
    /// </summary>
    [Fact]
    public void CommunityGoalHallOfFame_WritesGoalThenCountedEntries()
    {
        ClientPacket packet = Body(
            typeof(CommunityGoalHallOfFameMessageComposer),
            new CommunityGoalHallOfFameMessageComposer
            {
                GoalCode = "summer_2026",
                Entries =
                [
                    new CommunityGoalHallOfFameEntry
                    {
                        UserId = 1,
                        UserName = "Alice",
                        Figure = "hd-180-1",
                        Rank = 1,
                        CurrentScore = 900,
                    },
                ],
            }
        );

        packet.PopString().Should().Be("summer_2026");
        packet.PopInt().Should().Be(1);
        packet.PopInt().Should().Be(1);
        packet.PopString().Should().Be("Alice");
        packet.PopString().Should().Be("hd-180-1");
        packet.PopInt().Should().Be(1);
        packet.PopInt().Should().Be(900);
    }

    /// <summary>
    ///     WIN63 unknowns/_SafePkg_1744/_SafeCls_3217.as (header 530). Nested counts: each queue set
    ///     carries its own list, and the client takes the first set's target as the active one.
    /// </summary>
    [Fact]
    public void RoomQueueStatus_WritesNestedQueueCounts()
    {
        ClientPacket packet = Body(
            typeof(RoomQueueStatusMessageComposer),
            new RoomQueueStatusMessageComposer
            {
                FlatId = 4711,
                QueueSets =
                [
                    new RoomQueueSet
                    {
                        Name = "visitors",
                        Target = 1,
                        Queues = [new RoomQueueEntry { Name = "spectators", Count = 4 }],
                    },
                    new RoomQueueSet
                    {
                        Name = "game",
                        Target = 2,
                        Queues = ImmutableArray<RoomQueueEntry>.Empty,
                    },
                ],
            }
        );

        packet.PopInt().Should().Be(4711);
        packet.PopInt().Should().Be(2);

        packet.PopString().Should().Be("visitors");
        packet.PopInt().Should().Be(1);
        packet.PopInt().Should().Be(1);
        packet.PopString().Should().Be("spectators");
        packet.PopInt().Should().Be(4);

        packet.PopString().Should().Be("game");
        packet.PopInt().Should().Be(2);
        packet.PopInt().Should().Be(0);
        packet.Remaining.Should().Be(0);
    }

    /// <summary>
    ///     WIN63 unknowns/_SafePkg_2184/_SafeCls_2463.as (header 2458), floor branch: type code 0 is
    ///     followed by three ints.
    /// </summary>
    [Fact]
    public void BuildersClubPlacementWarning_FloorWritesCoordinates()
    {
        ClientPacket packet = Body(
            typeof(BuildersClubPlacementWarningMessageComposer),
            new BuildersClubPlacementWarningMessageComposer
            {
                TypeCode = 0,
                PageId = 12,
                OfferId = 34,
                ExtraParam = string.Empty,
                X = 3,
                Y = 4,
                Direction = 2,
                WallLocation = "ignored",
            }
        );

        packet.PopInt().Should().Be(0);
        packet.PopInt().Should().Be(12);
        packet.PopInt().Should().Be(34);
        packet.PopString().Should().BeEmpty();
        packet.PopInt().Should().Be(3);
        packet.PopInt().Should().Be(4);
        packet.PopInt().Should().Be(2);
        packet.Remaining.Should().Be(0);
    }

    /// <summary>
    ///     The other branch: a non-zero type code is followed by one string instead, and the
    ///     coordinates are not written at all.
    /// </summary>
    [Fact]
    public void BuildersClubPlacementWarning_WallWritesLocationInstead()
    {
        ClientPacket packet = Body(
            typeof(BuildersClubPlacementWarningMessageComposer),
            new BuildersClubPlacementWarningMessageComposer
            {
                TypeCode = 1,
                PageId = 12,
                OfferId = 34,
                ExtraParam = string.Empty,
                X = 3,
                Y = 4,
                Direction = 2,
                WallLocation = ":w=1,2 l=3,4 r",
            }
        );

        packet.PopInt().Should().Be(1);
        packet.PopInt().Should().Be(12);
        packet.PopInt().Should().Be(34);
        packet.PopString().Should().BeEmpty();
        packet.PopString().Should().Be(":w=1,2 l=3,4 r");
        packet.Remaining.Should().Be(0);
    }

    /// <summary>
    ///     WIN63 unknowns/_SafePkg_2056/_SafeCls_2564.as (header 1746). Two nested sanction types
    ///     per record, and they bracket the scalars rather than sitting side by side.
    /// </summary>
    [Fact]
    public void SanctionStatus_WritesRecordsWithBothNestedTypes()
    {
        ClientPacket packet = Body(
            typeof(SanctionStatusEventMessageComposer),
            new SanctionStatusEventMessageComposer
            {
                Sanctions =
                [
                    new SanctionRecord
                    {
                        SanctionType = new SanctionType
                        {
                            Name = "MUTE",
                            DurationHours = 2,
                            ProbationHours = 48,
                        },
                        Description = "Bad language",
                        ShowsProbationDetails = true,
                        ProbationHoursLeft = 20,
                        NextSanctionType = new SanctionType
                        {
                            Name = "BAN_PERMANENT",
                            DurationHours = 0,
                            ProbationHours = 0,
                        },
                    },
                ],
            }
        );

        packet.PopInt().Should().Be(1);

        packet.PopString().Should().Be("MUTE");
        packet.PopInt().Should().Be(2);
        packet.PopInt().Should().Be(48);

        packet.PopString().Should().Be("Bad language");
        packet.PopBoolean().Should().BeTrue();
        packet.PopInt().Should().Be(20);

        packet.PopString().Should().Be("BAN_PERMANENT");
        packet.PopInt().Should().Be(0);
        packet.PopInt().Should().Be(0);
        packet.Remaining.Should().Be(0);
    }
}
