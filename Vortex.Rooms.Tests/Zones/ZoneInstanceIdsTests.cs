using FluentAssertions;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.World;
using Xunit;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>
/// An instance's room id says which zone it copies (plan D20): the rooms row, the entry logs and
/// the zone document are all the template's, found without asking anyone.
/// </summary>
public sealed class ZoneInstanceIdsTests
{
    [Fact]
    public void AnInstanceId_NamesItsTemplate()
    {
        RoomId instance = ZoneInstanceIds.Of(new RoomId(42), 7)!.Value;

        ZoneInstanceIds.IsInstance(instance).Should().BeTrue();
        ZoneInstanceIds.TemplateOf(instance).Should().Be(new RoomId(42));
    }

    [Fact]
    public void AnOrdinaryRoom_IsItsOwnTemplate()
    {
        ZoneInstanceIds.IsInstance(new RoomId(42)).Should().BeFalse();
        ZoneInstanceIds.TemplateOf(new RoomId(42)).Should().Be(new RoomId(42));
    }

    [Fact]
    public void ATemplateBeyondTheIdSpace_HasNoInstance() =>
        ZoneInstanceIds.Of(new RoomId(ZoneInstanceIds.MaxTemplateId + 1), 1).Should().BeNull();

    [Fact]
    public void ASlotOutsideItsRange_HasNoInstance()
    {
        ZoneInstanceIds.Of(new RoomId(42), 0).Should().BeNull();
        ZoneInstanceIds.Of(new RoomId(42), ZoneInstanceIds.SlotsPerZone).Should().BeNull();
    }
}
