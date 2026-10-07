using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Streams;
using Vortex.Database.Context;
using Vortex.Primitives.Action;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Pets.Providers;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Providers;
using Vortex.Primitives.Rooms.Snapshots;
using Vortex.Rooms.Configuration;
using Vortex.Rooms.Grains;
using Vortex.Rooms.Grains.Systems;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Pets;

/// <summary>
/// Caring for a pet — giving it a command — funnels into the experience grant. Pet respects
/// (scratching) were removed in M0 bis.
/// </summary>
public sealed class PetCareTests
{
    private const int RoomId = 10;
    private const int OwnerId = 1;
    private const int PetId = 500;
    private const int EnergyCap = 100;

    [Fact]
    public async Task ACommand_PaysItsOwnExperience()
    {
        Harness harness = new(Pet());

        PetSnapshot? updated = await harness
            .System.GrantPetCommandXpAsync(harness.Context, PetId, CancellationToken.None)
            .ConfigureAwait(true);

        updated!.Experience.Should().Be(new PetConfig().CommandXpReward);
    }

    [Fact]
    public async Task EnoughExperience_LevelsThePetUp()
    {
        // The stub levels a pet every ten points, so one command away from the threshold crosses it.
        int commandXp = new PetConfig().CommandXpReward;
        Harness harness = new(Pet(experience: 10 - commandXp + 1));

        PetSnapshot? updated = await harness
            .System.GrantPetCommandXpAsync(harness.Context, PetId, CancellationToken.None)
            .ConfigureAwait(true);

        updated!.Experience.Should().Be(11);
        updated.Level.Should().Be(2);
    }

    private static PetSnapshot Pet(int experience = 0, int respect = 0) =>
        new()
        {
            PetId = PetId,
            OwnerId = new PlayerId(OwnerId),
            RoomId = RoomId,
            Name = "Biscuit",
            Type = 0,
            Race = 1,
            Color = "FFFFFF",
            Gender = AvatarGenderType.Male,
            Level = 1,
            Experience = experience,
            Energy = 100,
            Nutrition = 100,
            Respect = respect,
            X = 1,
            Y = 1,
            Z = 0,
            Direction = Rotation.South,
        };

    /// <summary>
    /// A room grain built outside a silo with the pet already resident, so the care paths run
    /// without a database or a room tick. Pets are pushed straight into the live state and marked
    /// loaded — the loading path itself is covered by the feeding tests, which do use a database.
    /// </summary>
    private sealed class Harness
    {
        public Harness(PetSnapshot pet)
        {
            RoomGrain grain = GrainActivationContext.CreateWithIntegerKey<RoomGrain>(
                RoomId,
                FakeProxy.Create<IDbContextFactory<VortexDbContext>>(_ => null),
                FakeProxy.Create<IFurnitureDefinitionProvider>(_ => null),
                FakeProxy.Create<IStuffDataFactory>(_ => null),
                Options.Create(new RoomConfig()),
                NullLogger<IRoomGrain>.Instance,
                FakeProxy.Create<IRoomModelProvider>(_ => null),
                FakeProxy.Create<IRoomItemsProvider>(_ => null),
                FakeProxy.Create<IRoomObjectLogicProvider>(_ => null),
                FakeProxy.Create<IRoomAvatarProvider>(_ => null),
                RoomGrainStubs.NoListeners(),
                PlayerGrainFactory(),
                FakeProxy.Create<IEventPublisher>(_ => null),
                RoomGrainStubs.NeverCancels(),
                FakeProxy.Create<IPermissionService>(_ => null),
                FakeProxy.Create<IVortexMetrics>(_ => null),
                FakeProxy.Create<IRoomModerationStore>(_ => null),
                BuildPetLevelProvider(),
                FakeProxy.Create<IPetCommandProvider>(_ => null),
                FakeProxy.Create<IPetVocalProvider>(_ => null),
                TestRoomGames.Provider(),
                FakeProxy.Create<ICommerceJournal>(_ => Task.CompletedTask),
                RoomGrainStubs.PassThroughWordFilter()
            );

            // Every care path broadcasts; the stream is armed during activation, which never runs
            // here, so it is stubbed rather than left null.
            grain._roomOutbound = FakeProxy.Create<IAsyncStream<RoomOutbound>>(_ => null);

            grain._state.IsPetsLoaded = true;
            grain._state.PetsById[pet.PetId] = pet;

            // The avatar push labels the pet with its owner's name, which the room caches after one
            // lookup through the player directory. Seeding the cache keeps that lookup out of a test
            // about respect and experience.
            grain._state.OwnerNamesById[pet.OwnerId] = "Owner";

            System = grain.PetSystem;
        }

        public RoomPetSystem System { get; }

        public ActionContext Context { get; } =
            ActionContext.CreateForPlayer(new PlayerId(OwnerId), new RoomId(RoomId));

        /// <summary>A grain factory whose player grains answer nothing.</summary>
        private static IGrainFactory PlayerGrainFactory() =>
            FakeProxy.Create<IGrainFactory>(call =>
                call.Method.IsGenericMethod
                && call.Method.GetGenericArguments()[0] == typeof(IPlayerGrain)
                    ? FakeProxy.Create<IPlayerGrain>(_ => null)
                    : null
            );

        private static IPetLevelProvider BuildPetLevelProvider() =>
            FakeProxy.Create<IPetLevelProvider>(call =>
                call.Method.Name switch
                {
                    nameof(IPetLevelProvider.GetEnergyCapForLevel) => EnergyCap,
                    nameof(IPetLevelProvider.GetNutritionCapForLevel) => 100,
                    nameof(IPetLevelProvider.GetMaxLevel) => 20,
                    // A level every ten points, so a test can name an experience total and know the
                    // level it implies without importing the real progression table.
                    nameof(IPetLevelProvider.GetLevelForExperience) => 1
                        + ((int)call.Args![1]! / 10),
                    _ => null,
                }
            );
    }
}
