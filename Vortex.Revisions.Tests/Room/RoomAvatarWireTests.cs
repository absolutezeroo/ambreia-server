using System;
using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Room;

/// <summary>
///     The Users payload packs every avatar in the room back to back with no length prefix between
///     them, so a block that is one field short does not degrade — it shifts everything after it.
///     The player block was once missing its trailing badge rank, which means the client was reading
///     the next avatar's id as the previous one's rank and mis-framing the rest of the room; the last
///     avatar in the list read past the end of the buffer entirely. The RPG block has no badge rank
///     at all now, on either side.
/// </summary>
public sealed class RoomAvatarWireTests
{
    private static readonly Rev Revision = new();

    /// <summary>Goes through the registered composer rather than the internal writer, so the test
    /// exercises exactly what a connected client would receive.</summary>
    private static ClientPacket Serialize(params RoomAvatarSnapshot[] avatars)
    {
        UsersMessageComposer composer = new() { Avatars = [.. avatars] };

        byte[] bytes = Revision
            .Serializers[typeof(UsersMessageComposer)]
            .Serialize(composer)
            .ToArray();

        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);

        ClientPacket packet = new(0, body);

        packet.PopInt(); // avatar count

        return packet;
    }

    private static RoomPlayerAvatarSnapshot NewPlayer(int id) =>
        new()
        {
            AvatarType = RoomObjectType.Player,
            WebId = id,
            Name = $"Player{id}",
            Motto = "hi",
            Figure = "hd-180-1",
            ObjectId = id,
            X = 1,
            Y = 2,
            Z = default,
            BodyRotation = Rotation.North,
            HeadRotation = Rotation.North,
            Status = "/",
            Gender = AvatarGenderType.Male,
            DanceType = AvatarDanceType.None,
            ActivityPoints = 5,
            IsModerator = true,
        };

    private static void SkipCommonHead(ClientPacket body)
    {
        body.PopInt(); // webId
        body.PopString(); // name
        body.PopString(); // motto
        body.PopString(); // figure
        body.PopInt(); // objectId
        body.PopInt(); // x
        body.PopInt(); // y
        body.PopString(); // z
        body.PopInt(); // bodyRotation
        body.PopInt(); // avatarType
    }

    [Fact]
    public void PlayerBlock_EndsWithTheModeratorFlag()
    {
        ClientPacket body = Serialize(NewPlayer(1));

        SkipCommonHead(body);

        body.PopString().Should().Be("M");
        body.PopInt().Should().Be(5); // achievementScore
        body.PopBoolean().Should().BeTrue(); // isModerator

        body.End.Should().BeTrue("the block must consume exactly what the client reads");
    }

    [Fact]
    public void TwoPlayersInARow_StayAlignedWithEachOther()
    {
        // The real failure mode: with a short block, the second avatar's id is eaten by the first.
        ClientPacket body = Serialize(NewPlayer(1), NewPlayer(2));

        SkipCommonHead(body);

        body.PopString();
        body.PopInt();
        body.PopBoolean();

        body.PopInt().Should().Be(2, "the second avatar must start exactly where the first ended");
    }

    [Fact]
    public void PetBlock_HasNoRarityNorBreedingFlags()
    {
        RoomPetAvatarSnapshot pet = new()
        {
            AvatarType = RoomObjectType.Pet,
            WebId = 11,
            Name = "Rex",
            Motto = string.Empty,
            Figure = "0 0 FFFFFF",
            ObjectId = 901,
            X = 0,
            Y = 0,
            Z = default,
            BodyRotation = Rotation.North,
            HeadRotation = Rotation.North,
            Status = "/",
            SubType = 3,
            OwnerId = 42,
            OwnerName = "Frank",
            HasSaddle = true,
            IsRiding = false,
            PetLevel = 7,
            PetPosture = "std",
        };

        ClientPacket body = Serialize(pet);

        SkipCommonHead(body);

        body.PopInt().Should().Be(3); // subType
        body.PopInt().Should().Be(42); // ownerId
        body.PopString().Should().Be("Frank");
        body.PopBoolean().Should().BeTrue(); // hasSaddle
        body.PopBoolean().Should().BeFalse(); // isRiding
        body.PopInt().Should().Be(7); // petLevel
        body.PopString().Should().Be("std");

        body.End.Should().BeTrue("the block must consume exactly what the client reads");
    }

    [Fact]
    public void BotBlock_WritesGenderOwnerAndSkillsAsShorts()
    {
        RoomBotAvatarSnapshot bot = new()
        {
            AvatarType = RoomObjectType.Bot,
            WebId = 7,
            Name = "Bartender",
            Motto = "what will it be",
            Figure = "hd-180-1",
            ObjectId = 900,
            X = 3,
            Y = 4,
            Z = default,
            BodyRotation = Rotation.South,
            HeadRotation = Rotation.South,
            Status = "/",
            Gender = AvatarGenderType.Female,
            OwnerId = 42,
            OwnerName = "Frank",
            SkillIds = [1, 5],
        };

        ClientPacket body = Serialize(bot);

        SkipCommonHead(body);

        body.PopString().Should().Be("F");
        body.PopInt().Should().Be(42);
        body.PopString().Should().Be("Frank");
        body.PopInt().Should().Be(2);
        body.PopShort().Should().Be(1);
        body.PopShort().Should().Be(5);

        body.End.Should().BeTrue("the block must consume exactly what the client reads");
    }

    [Fact]
    public void BotBlock_StillWritesTheCountWhenTheBotHasNoSkills()
    {
        RoomBotAvatarSnapshot bot = new()
        {
            AvatarType = RoomObjectType.Bot,
            WebId = 7,
            Name = "Bartender",
            Motto = string.Empty,
            Figure = "hd-180-1",
            ObjectId = 900,
            X = 0,
            Y = 0,
            Z = default,
            BodyRotation = Rotation.North,
            HeadRotation = Rotation.North,
            Status = "/",
            Gender = AvatarGenderType.Male,
            OwnerId = 42,
            OwnerName = "Frank",
        };

        ClientPacket body = Serialize(bot);

        SkipCommonHead(body);

        body.PopString();
        body.PopInt();
        body.PopString();
        body.PopInt()
            .Should()
            .Be(0, "the count is read unconditionally, the loop is what is guarded");

        body.End.Should().BeTrue("the block must consume exactly what the client reads");
    }

    /// <summary>
    ///     UserUpdate has the same repeated-block failure mode: WIN63 reads a jumpingPower int
    ///     between the body rotation and the status string
    ///     (_SafePkg_2184/_SafeCls_2826.parse → _SafeCls_3690.jumpingPower). Without it the status
    ///     string lands in that int and every avatar after the first is garbage.
    /// </summary>
    [Fact]
    public void UserUpdateBlock_HasNoJumpingPowerBecauseTheServedClientReadsNone()
    {
        // The January WIN63 build the hotel serves reads status straight after the body rotation
        // (_SafePkg_2072/_SafeCls_3051.parse:82-84). The July build reads a jumpingPower int in
        // between; writing it for the January client shifted the status string and cost the walk
        // animation, so the block stays at eight fields and JumpPower rides along unused.
        UserUpdateMessageComposer composer = new()
        {
            Avatars = [NewPlayer(1), NewPlayer(2) with { JumpPower = 7 }],
        };

        byte[] bytes = Revision
            .Serializers[typeof(UserUpdateMessageComposer)]
            .Serialize(composer)
            .ToArray();

        byte[] payload = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, payload, 0, payload.Length);

        ClientPacket body = new(0, payload);

        body.PopInt().Should().Be(2); // avatar count

        foreach (int objectId in new[] { 1, 2 })
        {
            body.PopInt()
                .Should()
                .Be(objectId, "the block before this one must have ended exactly here");
            body.PopInt(); // x
            body.PopInt(); // y
            body.PopString(); // z
            body.PopInt(); // headRotation
            body.PopInt(); // bodyRotation
            body.PopString()
                .Should()
                .Be("/", "the status follows the body rotation with nothing in between");
        }

        body.End.Should().BeTrue("the block must consume exactly what the client reads");
    }
}
