using FluentAssertions;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Rooms.Grains.Systems;
using Xunit;

namespace Vortex.Rooms.Tests.Pets;

/// <summary>
/// The four flags that decide which buttons a pet offers. They ride on the room's avatar list and on
/// the pet status push, and <see cref="RoomPetRuntime"/> answers both from the same functions so the
/// two can never disagree — a pet that offers "harvest" in the list and refuses it on click is worse
/// than one that never offered.
/// </summary>
public sealed class PetActionFlagsTests
{
    private const int MonsterplantType = 16;
    private const int MonsterplantMaxLevel = 7;
    private const int DogType = 0;

    /// <summary>
    /// The two breeding bits are not the same bit. A capture of the live hotel (2026-09-19) caught
    /// two animals whose owner had breeding switched on: both were announced with the permission
    /// true and <c>canBreed</c> false. <c>canBreed</c> is the plant's "grown enough to breed", and
    /// reading it as "this animal may be paired" is what this used to do.
    /// </summary>
    [Fact]
    public void AnOrdinaryPetThatMayBreed_CarriesThePermissionButNotTheGrownFlag()
    {
        PetSnapshot dog = Pet(DogType, canBreed: true);

        RoomPetRuntime.CanBreed(dog).Should().BeFalse();
        RoomPetRuntime.HasBreedingPermission(dog).Should().BeTrue();
    }

    [Fact]
    public void APetAlreadyBred_OffersNothingFurther() =>
        RoomPetRuntime.HasBreedingPermission(Pet(DogType, canBreed: false)).Should().BeFalse();

    [Theory]
    [InlineData(1, false)]
    [InlineData(MonsterplantMaxLevel - 1, false)]
    [InlineData(MonsterplantMaxLevel, true)]
    public void AMonsterplantBreedsOnlyOnceFullyGrown(int level, bool expected) =>
        RoomPetRuntime
            .CanBreed(Pet(MonsterplantType, level: level, canBreed: true))
            .Should()
            .Be(expected);

    [Fact]
    public void AMonsterplantThatSpentItsSeed_StopsOfferingToBreed() =>
        RoomPetRuntime
            .CanBreed(Pet(MonsterplantType, level: MonsterplantMaxLevel, canBreed: false))
            .Should()
            .BeFalse();

    [Theory]
    [InlineData(1, false)]
    [InlineData(MonsterplantMaxLevel - 1, false)]
    [InlineData(MonsterplantMaxLevel, true)]
    public void AMonsterplantIsHarvestableOnlyOnceFullyGrown(int level, bool expected) =>
        RoomPetRuntime.CanHarvest(Pet(MonsterplantType, level: level)).Should().Be(expected);

    [Fact]
    public void AnOrdinaryPetIsNeverHarvestable() =>
        RoomPetRuntime.CanHarvest(Pet(DogType, level: MonsterplantMaxLevel)).Should().BeFalse();

    [Fact]
    public void AWitheredMonsterplantOffersRevival() =>
        RoomPetRuntime.CanRevive(Pet(MonsterplantType, energy: 0)).Should().BeTrue();

    [Fact]
    public void AWateredMonsterplantDoesNot() =>
        RoomPetRuntime.CanRevive(Pet(MonsterplantType, energy: 1)).Should().BeFalse();

    [Fact]
    public void AnExhaustedPetIsAsleep_NotDead() =>
        // Same zero energy, entirely different meaning: an ordinary pet naps and wakes up on its own.
        RoomPetRuntime.CanRevive(Pet(DogType, energy: 0)).Should().BeFalse();

    private static PetSnapshot Pet(
        int type,
        int energy = 100,
        int level = 1,
        bool canBreed = true
    ) =>
        new()
        {
            PetId = 1,
            OwnerId = new PlayerId(1),
            RoomId = 10,
            Name = "Rex",
            Type = type,
            Race = 1,
            Color = "FFFFFF",
            Gender = AvatarGenderType.Male,
            Level = level,
            Experience = 0,
            Energy = energy,
            Nutrition = 100,
            Respect = 0,
            X = 0,
            Y = 0,
            Z = 0,
            Direction = Rotation.South,
            CanBreed = canBreed,
        };
}
