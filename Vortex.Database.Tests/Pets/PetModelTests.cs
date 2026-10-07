using System;
using System.Linq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Vortex.Database.Context;
using Vortex.Database.Entities.Pets;
using Vortex.Primitives.Rooms.Enums;
using Xunit;

namespace Vortex.Database.Tests.Pets;

public sealed class PetModelTests
{
    [Fact]
    public void PetEntity_HasInventoryPlacementIndexesAndDefaults()
    {
        using VortexDbContext context = NewContext();

        IEntityType pet = context.Model.FindEntityType(typeof(PetEntity))!;

        pet.FindProperty(nameof(PetEntity.RoomEntityId))!
            .IsNullable.Should()
            .BeTrue("room_id is nullable so null means inventory");

        pet.GetIndexes()
            .Should()
            .Contain(i => i.Properties.Any(p => p.Name == nameof(PetEntity.OwnerPlayerEntityId)));
        pet.GetIndexes()
            .Should()
            .Contain(i => i.Properties.Any(p => p.Name == nameof(PetEntity.RoomEntityId)));

        pet.FindProperty(nameof(PetEntity.Gender))!
            .GetDefaultValue()
            .Should()
            .Be(AvatarGenderType.Male);
        pet.FindProperty(nameof(PetEntity.Level))!.GetDefaultValue().Should().Be(1);
        pet.FindProperty(nameof(PetEntity.Experience))!.GetDefaultValue().Should().Be(0);
    }

    private static VortexDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<VortexDbContext>()
                .UseInMemoryDatabase($"pet-model-{Guid.NewGuid():N}")
                .Options
        );
}
