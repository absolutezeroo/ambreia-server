using System;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Protocol.Messages.Outgoing.Room.Pets;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Pets;

/// <summary>
/// The info panel's happiness bar and age counter both read from fixed slots in this message, and
/// both were wrong in a way nothing could catch: the happiness pair carried the pet's nutrition,
/// because pets had no mood stat at all, and age was never filled in so every pet in the hotel was
/// zero days old.
/// </summary>
/// <remarks>
/// A companion has no needs (M0 bis): the energy and happiness pairs are still on the wire, both
/// drawn full from the level caps, until the client drops them.
/// </remarks>
public sealed class PetInfoWireLayoutTests
{
    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    private const int MaxEnergy = 260;
    private const int MaxHappiness = 100;
    private const int Age = 13;

    [Fact]
    public void TheNeedBarsAreDrawnFull()
    {
        ClientPacket body = Serialize();

        SkipToHappiness(body);

        body.PopInt().Should().Be(MaxHappiness, "happiness, frozen at its cap");
        body.PopInt().Should().Be(MaxHappiness, "and its maximum");
    }

    [Fact]
    public void TheAgeSlotCarriesTheAgeInDays()
    {
        ClientPacket body = Serialize();

        SkipToHappiness(body);
        body.PopInt();
        body.PopInt();
        body.PopInt().Should().Be(0, "respect");
        body.PopInt().Should().Be(1, "owner id");

        body.PopInt().Should().Be(Age);
    }

    /// <summary>petId, name, level, maxLevel, experience, experienceGoal, energy, maxEnergy.</summary>
    private static void SkipToHappiness(ClientPacket body)
    {
        body.PopInt();
        body.PopString();
        body.PopInt();
        body.PopInt();
        body.PopInt();
        body.PopInt();
        body.PopInt().Should().Be(MaxEnergy, "energy, frozen at its cap");
        body.PopInt().Should().Be(MaxEnergy);
    }

    private static ClientPacket Serialize()
    {
        byte[] bytes = Revision
            .Serializers[typeof(PetInfoMessageComposer)]
            .Serialize(
                new PetInfoMessageComposer
                {
                    Pet = Pet(),
                    OwnerName = "Owner",
                    Age = Age,
                    MaxEnergy = MaxEnergy,
                    MaxHappiness = MaxHappiness,
                }
            )
            .ToArray();

        byte[] payload = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, payload, 0, payload.Length);

        return new ClientPacket(0, payload);
    }

    private static PetSnapshot Pet() =>
        new()
        {
            PetId = 500,
            OwnerId = new PlayerId(1),
            RoomId = 10,
            Name = "Rex",
            Type = 0,
            Race = 3,
            Color = "FFFFFF",
            Gender = AvatarGenderType.Male,
            Level = 4,
            Experience = 30,
            X = 0,
            Y = 0,
            Z = 0,
            Direction = Rotation.South,
        };
}
