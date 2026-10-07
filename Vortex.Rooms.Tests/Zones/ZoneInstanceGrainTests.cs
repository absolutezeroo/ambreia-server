using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.World;
using Vortex.Rooms.Configuration;
using Vortex.Rooms.Grains;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>
/// The dungeon copies (plan D16, D19–D23): one per group and per dungeon, kept while its room
/// lives, a fresh one once it has closed, none when every copy is taken.
/// </summary>
public sealed class ZoneInstanceGrainTests
{
    private static readonly RoomId Crypt = new(42);

    private static ZoneInstanceGrain Grain(int max = 50) =>
        GrainActivationContext.CreateWithIntegerKey<ZoneInstanceGrain>(
            0,
            Options.Create(new RoomConfig { MaxInstancesPerZone = max }),
            NullLogger<ZoneInstanceGrain>.Instance
        );

    [Fact]
    public async Task AGroup_GetsTheSameCopyEachTime()
    {
        ZoneInstanceGrain grain = Grain();

        RoomId? first = await grain.EnterAsync(Crypt, "player:7");
        RoomId? again = await grain.EnterAsync(Crypt, "player:7");

        again.Should().Be(first);
        ZoneInstanceIds.TemplateOf(first!.Value).Should().Be(Crypt);
    }

    [Fact]
    public async Task TwoGroups_GetTwoCopies()
    {
        ZoneInstanceGrain grain = Grain();

        RoomId? mine = await grain.EnterAsync(Crypt, "player:7");
        RoomId? yours = await grain.EnterAsync(Crypt, "player:8");

        yours.Should().NotBe(mine);
    }

    [Fact]
    public async Task AClosedCopy_GivesTheGroupAFreshOne()
    {
        ZoneInstanceGrain grain = Grain(max: 1);
        RoomId first = (await grain.EnterAsync(Crypt, "player:7"))!.Value;

        await grain.ReleaseAsync(first);

        (await grain.EnterAsync(Crypt, "player:8"))
            .Should()
            .NotBeNull("the only slot is free again");
    }

    [Fact]
    public async Task WhenEveryCopyIsTaken_ThereIsNone()
    {
        ZoneInstanceGrain grain = Grain(max: 1);
        await grain.EnterAsync(Crypt, "player:7");

        (await grain.EnterAsync(Crypt, "player:8")).Should().BeNull();
    }

    [Fact]
    public async Task OnlyItsGroup_BelongsToACopy()
    {
        ZoneInstanceGrain grain = Grain();
        RoomId copy = (await grain.EnterAsync(Crypt, "player:7"))!.Value;

        (await grain.IsMemberAsync(copy, "player:7")).Should().BeTrue();
        (await grain.IsMemberAsync(copy, "player:8")).Should().BeFalse();
        (await grain.IsMemberAsync(ZoneInstanceIds.Of(Crypt, 9)!.Value, "player:7"))
            .Should()
            .BeFalse("a copy nobody was given does not exist");
    }
}
