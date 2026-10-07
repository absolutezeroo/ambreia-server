using System;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Protocol.Messages.Outgoing.Room.Pets;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Pets;

/// <summary>
///     Pinned to a capture of the live hotel (2026-09-19): opening the panel of the dog "filou"
///     answered with this body. Twenty-eight fields, read by WIN63's <c>_SafeCls_3619.parse</c>,
///     and the six booleans among them are one byte each — the whole body is 34 bytes past the
///     owner name, which only works if they are.
///
///     Every value here is the live hotel's answer for an ordinary animal, including the three
///     monsterplant timers it sends as -1 rather than 0, the rarity it sends as 0, and the
///     <c>canBreed</c>/<c>hasBreedingPermission</c> pair it sends as false/true.
/// </summary>
public sealed class PetInfoWireTests
{
    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    [Fact]
    public void AnimalPanel_MatchesTheLiveHotelBytes()
    {
        PetInfoMessageComposer composer = new()
        {
            Pet = new PetSnapshot
            {
                PetId = 353_056,
                OwnerId = new PlayerId(12_111_328),
                RoomId = 25_267_807,
                Name = "filou",
                Type = 4,
                Race = 1,
                Color = "E4FEFF",
                Gender = AvatarGenderType.Male,
                Level = 8,
                Experience = 1_830,
                Energy = 260,
                Nutrition = 100,
                Happiness = 26,
                Respect = 60,
                X = 1,
                Y = 15,
                Z = 0,
                Direction = Rotation.North,
            },
            OwnerName = "claytonxy",
            Age = 5_919,
            MaxLevel = 20,
            ExperienceRequiredToLevel = 2_400,
            MaxEnergy = 260,
            MaxHappiness = 100,
            CanBreed = false,
            CanHarvest = false,
            CanRevive = false,
            HasBreedingPermission = true,
            RarityLevel = 0,
            MaxWellBeingSeconds = -1,
            RemainingWellBeingSeconds = -1,
            RemainingGrowingSeconds = -1,
        };

        byte[] bytes = Revision
            .Serializers[typeof(PetInfoMessageComposer)]
            .Serialize(composer)
            .ToArray();

        // Body only: the captured hotel is a later build and announces this on 3930 where
        // WIN63-202607011411 binds 3192.
        Convert
            .ToHexString(bytes.AsSpan(6))
            .Should()
            .Be(
                "00056320000566696C6F750000000800000014000007260000096000000104"
                    // respect (60 in the capture) is frozen at 0 since respects were removed (M0 bis).
                    + "000001040000001A000000640000000000B8CDE00000171F0009636C6179746F"
                    + "6E7879000000010000000000000000000000000000000000FFFFFFFFFFFFFFFF"
                    + "FFFFFFFF01"
            );
    }
}
