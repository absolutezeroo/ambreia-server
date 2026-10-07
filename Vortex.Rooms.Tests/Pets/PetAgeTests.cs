using System;
using FluentAssertions;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Xunit;

namespace Vortex.Rooms.Tests.Pets;

/// <summary>
/// Age, which the info panel got wrong: it was never computed at all.
/// </summary>
public sealed class PetAgeTests
{
    private static readonly DateTime Now = new(2026, 8, 3, 22, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, 1, "bought an hour ago: day one, not day zero")]
    [InlineData(1, 2, "yesterday")]
    [InlineData(30, 31, "a month old")]
    public void AgeInDays_CountsTheDayItWasCreatedAsTheFirst(
        int daysAgo,
        int expected,
        string because
    )
    {
        PetSnapshot pet = PetWith(createdAt: Now.AddDays(-daysAgo));

        pet.AgeInDays(Now).Should().Be(expected, because);
    }

    [Fact]
    public void AgeInDays_ClockSkew_NeverGoesBelowOne()
    {
        PetSnapshot pet = PetWith(createdAt: Now.AddDays(2));

        pet.AgeInDays(Now).Should().Be(1, "a pet is never zero or minus two days old");
    }

    private static PetSnapshot PetWith(DateTime createdAt) =>
        new()
        {
            PetId = 1,
            OwnerId = new PlayerId(1),
            RoomId = 10,
            Name = "Rex",
            Type = 0,
            Race = 0,
            Color = "FFFFFF",
            Gender = AvatarGenderType.Male,
            Level = 1,
            Experience = 0,
            X = 0,
            Y = 0,
            Z = 0,
            Direction = Rotation.South,
            CreatedAt = createdAt,
        };
}
