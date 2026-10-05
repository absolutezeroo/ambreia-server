using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Logging;
using Vortex.Primitives;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Snapshots.Wired;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Wired;

/// <summary>
/// Who is allowed to rewrite a room's wired.
/// </summary>
/// <remarks>
/// <para>
/// The six wired saves the client can send — action, addon, condition, selector, trigger, variable —
/// are six handlers that all funnel into <c>RoomActionModule.ApplyWiredUpdateAsync</c>, and it
/// checked two things: that the item exists, and that it is a wired box. Neither is a permission.
/// Any visitor standing in a room could rewrite its owner's wired configuration, and be told it
/// saved.
/// </para>
/// <para>
/// Rights is the same floor the place and move paths already use. The refusal is silent because
/// what the official server answers to a refused wired save is unknown; returning false takes the same route as
/// the guards that were already here, and the handler withholds <c>WiredSaveSuccess</c>.
/// </para>
/// </remarks>
public sealed class WiredConfigurationRightsTests
{
    private static readonly RoomObjectId Box = new(4242);

    [Fact]
    public async Task AVisitorWithoutRights_CannotSaveAWiredConfiguration()
    {
        RoomHarness harness = await RoomHarness
            .CreateAsync(canManipulate: false)
            .ConfigureAwait(true);

        bool saved = await harness
            .Grain.ActionModule.ApplyWiredUpdateAsync(
                harness.ContextFor(RoomHarness.Stranger),
                Box,
                Update(),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        saved.Should().BeFalse();
    }

    /// <summary>
    /// The other half, and the reason the first test is not just "everything returns false": a
    /// rights-holder gets past the gate and the room goes looking for the box, which this test never
    /// placed — <c>FloorItemNotFound</c>. A gate that refused everyone would return false here too.
    /// </summary>
    [Fact]
    public async Task ARightsHolder_ReachesTheItemLookup()
    {
        RoomHarness harness = await RoomHarness
            .CreateAsync(canManipulate: true)
            .ConfigureAwait(true);

        Func<Task> save = () =>
            harness.Grain.ActionModule.ApplyWiredUpdateAsync(
                harness.ContextFor(RoomHarness.Stranger),
                Box,
                Update(),
                CancellationToken.None
            );

        (await save.Should().ThrowAsync<VortexException>().ConfigureAwait(true))
            .Which.ErrorCode.Should()
            .Be(VortexErrorCodeEnum.FloorItemNotFound);
    }

    /// <summary>An empty but well-formed save. What it carries never gets read here.</summary>
    private static WiredUpdateRequest Update() =>
        new()
        {
            Id = Box.Value,
            IntParams = [],
            StringParam = "",
            StuffIds = [],
            StuffIds2 = [],
            DefinitionSpecifics = [],
            FurniSources = [],
            PlayerSources = [],
            VariableIds = [],
            TypeSpecifics = [],
        };
}
