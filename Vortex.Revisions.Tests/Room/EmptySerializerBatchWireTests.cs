using System;
using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Advertisement;
using Vortex.Protocol.Messages.Outgoing.Availability;
using Vortex.Protocol.Messages.Outgoing.Callforhelp;
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
