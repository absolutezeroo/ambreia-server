using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.World.Layers;
using Vortex.Protocol.Messages.Outgoing.Rpg.Interaction;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support.World;
using Xunit;
using static Vortex.Rooms.Tests.Zones.ZoneVisits;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>Spec F3b §5: a zone's entry tells the client what it can click.</summary>
public sealed class ZoneEntryClickablesTests
{
    /// <summary>A zone without decor: the burst's floor snapshots need real furni logic, which the
    /// harness does not build. What the list holds is CompiledZone.Interactives, pinned by
    /// ZoneCompilerTests; the burst copies it as is.</summary>
    [Fact]
    public async Task AZoneEntry_SendsItsClickables()
    {
        RoomHarness harness = await ZoneWithDecorAsync(ZoneSamples.Minimal());

        RoomAdmissionSnapshot admission = await harness.Grain.AdmitAsync(
            harness.ContextFor(Walker),
            string.Empty,
            false,
            CancellationToken.None
        );

        admission
            .Burst.OfType<RpgZoneInteractivesMessageComposer>()
            .Should()
            .ContainSingle()
            .Which.Objects.Should()
            .BeEquivalentTo(
                harness.Grain._state.ZoneRuntime!.Compiled.Interactives.Select(c => new
                {
                    c.ObjectId,
                    c.Interaction,
                })
            );
    }

    [Fact]
    public async Task ARoomThatIsNotAZone_SendsNoClickables()
    {
        RoomHarness harness = await RoomHarness.CreateAsync();

        RoomAdmissionSnapshot admission = await harness.Grain.AdmitAsync(
            harness.ContextFor(Walker),
            string.Empty,
            false,
            CancellationToken.None
        );

        admission.Burst.OfType<RpgZoneInteractivesMessageComposer>().Should().BeEmpty();
    }
}
