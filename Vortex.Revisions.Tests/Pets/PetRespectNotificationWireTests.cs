using System;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Protocol.Messages.Outgoing.Users;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Pets;

/// <summary>
///     Pinned to a capture of the live hotel (2026-09-19): scratching the pet "joefrey" in a public
///     room answered header 2316 with these exact bytes. WIN63 reads them with
///     <c>_SafeCls_2577</c> over <c>_SafeCls_3943</c> — respect, owner, then id, name, the figure
///     block (type, palette, colour, breed, custom-part count), level, rarity.
///
///     The palette is the point: it used to be a composer default nobody set, so every pet was
///     announced in breed 1's colours. joefrey is breed 7, which is what makes these bytes able to
///     tell the difference.
/// </summary>
public sealed class PetRespectNotificationWireTests
{
    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    [Fact]
    public void Scratch_MatchesTheLiveHotelBytes()
    {
        PetRespectNotificationEventMessageComposer composer = new()
        {
            PetRespect = 72,
            PetOwnerId = 12_111_328,
            Pet = new PetSnapshot
            {
                PetId = 353_067,
                OwnerId = new PlayerId(12_111_328),
                RoomId = 25_267_807,
                Name = "joefrey",
                Type = 5,
                Race = 7,
                Color = "DDDDDD",
                Gender = AvatarGenderType.Male,
                Level = 7,
                Experience = 1_404,
                Energy = 240,
                Nutrition = 1,
                Respect = 72,
                X = 6,
                Y = 10,
                Z = 0,
                Direction = Rotation.North,
            },
        };

        byte[] bytes = Revision
            .Serializers[typeof(PetRespectNotificationEventMessageComposer)]
            .Serialize(composer)
            .ToArray();

        // Body only. The hotel that was captured is a later build than the one this revision
        // targets and announces this message on 2316 where WIN63-202607011411 binds 1784, so the
        // six framing bytes are the one part of the capture that says nothing about us.
        Convert
            .ToHexString(bytes.AsSpan(6))
            .Should()
            .Be(
                "0000004800B8CDE00005632B00076A6F656672657900000005000000070006444444444444000000070000000000000007FFFFFFFF"
            );
    }
}
