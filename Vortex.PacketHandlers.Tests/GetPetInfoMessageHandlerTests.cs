using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.PacketHandlers.Room.Pets;
using Vortex.Primitives.Pets.Providers;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Protocol.Messages.Incoming.Room.Pets;
using Vortex.Protocol.Messages.Outgoing.Room.Pets;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.PacketHandlers.Tests;

/// <summary>
/// The pet menu is built from PetInfo alone: the client picks its mode from <c>hasFreeSaddle</c> and
/// <c>isRiding</c> (OwnPetMenuView / PetMenuView: "saddled up" when the saddle is free and the viewer
/// is not riding, "riding" when the viewer is). Both used to go out false, so a saddled pet never
/// offered mount, saddle off, riding permission or dismount.
/// </summary>
public sealed class GetPetInfoMessageHandlerTests : WordFilterHandlerFixture
{
    private const int RoomId = 9;
    private const int PetId = 77;

    [Fact]
    public async Task ASaddledPetNobodyRides_OffersItsFreeSaddle()
    {
        PetInfoMessageComposer info = await PetInfoForAsync(Pet() with { HasSaddle = true });

        info.HasFreeSaddle.Should().BeTrue();
        info.IsRiding.Should().BeFalse();
    }

    [Fact]
    public async Task TheViewerRidingThePet_IsToldSo()
    {
        PetInfoMessageComposer info = await PetInfoForAsync(
            Pet() with
            {
                HasSaddle = true,
                RiderId = new PlayerId(PlayerId),
            }
        );

        info.HasFreeSaddle.Should().BeFalse("the saddle is taken");
        info.IsRiding.Should().BeTrue();
    }

    [Fact]
    public async Task SomebodyElseRidingThePet_LeavesNothingToMountOrDismount()
    {
        PetInfoMessageComposer info = await PetInfoForAsync(
            Pet() with
            {
                HasSaddle = true,
                RiderId = new PlayerId(PlayerId + 1),
            }
        );

        info.HasFreeSaddle.Should().BeFalse();
        info.IsRiding.Should().BeFalse();
    }

    [Fact]
    public async Task AnUnsaddledPet_HasNoSaddleToOffer()
    {
        PetInfoMessageComposer info = await PetInfoForAsync(Pet());

        info.HasFreeSaddle.Should().BeFalse();
        info.IsRiding.Should().BeFalse();
    }

    [Fact]
    public async Task TheRidingPermission_IsTheCheckboxState()
    {
        // OwnPetMenuView ticks "toggle_riding_permission" when accessRights == 1.
        PetInfoMessageComposer info = await PetInfoForAsync(
            Pet() with
            {
                HasSaddle = true,
                RidingPermission = true,
            }
        );

        info.AccessRights.Should().Be(1);
    }

    private async Task<PetInfoMessageComposer> PetInfoForAsync(PetSnapshot pet)
    {
        GetPetInfoMessageHandler handler = new(
            Grains(call =>
                call.Method.Name switch
                {
                    "GetPlacedPetSnapshotAsync" => Task.FromResult<PetSnapshot?>(pet),
                    "GetPlayerNameAsync" => Task.FromResult("Owner"),
                    _ => null,
                }
            ),
            FakeProxy.Create<IPetLevelProvider>(call =>
                call.Method.ReturnType == typeof(int) ? 100 : null
            )
        );

        await handler.HandleAsync(
            new GetPetInfoMessage { PetId = PetId },
            Context(RoomId),
            CancellationToken.None
        );

        return GrainCalls
            .Where(c => c.Method.Name == "SendComposerAsync")
            .Select(c => c.Args![0])
            .OfType<PetInfoMessageComposer>()
            .Single();
    }

    private static PetSnapshot Pet() =>
        new()
        {
            PetId = PetId,
            OwnerId = new PlayerId(PlayerId),
            RoomId = RoomId,
            Name = "Comet",
            Type = 15,
            Skin = 0,
            Color = "FFFFFF",
            Gender = AvatarGenderType.Male,
            Level = 1,
            Experience = 0,
            X = 0,
            Y = 0,
            Z = 0,
            Direction = Rotation.South,
        };
}
