using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Vortex.Database.Context;
using Vortex.Players.Grains;
using Vortex.Primitives.Events;
using Vortex.Primitives.Permissions;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Players.Tests.Grains;

/// <summary>
/// The name is uniquely indexed, so its write is the one that can be refused. The grain used to
/// change its in-memory name first: a refused write left every room and lookup showing a name the
/// database never took, until the grain deactivated and wrote it again.
/// </summary>
public sealed class PlayerGrainNameTests
{
    [Fact]
    public async Task ARefusedNameWrite_LeavesTheNameAsItWas()
    {
        PlayerGrain grain = GrainActivationContext.CreateWithIntegerKey<PlayerGrain>(
            7,
            FakeProxy.Create<IDbContextFactory<VortexDbContext>>(_ =>
                throw new InvalidOperationException("duplicate entry for players.name")
            ),
            FakeProxy.Create<IGrainFactory>(_ => null),
            FakeProxy.Create<IEventPublisher>(_ => null),
            NullLogger<PlayerGrain>.Instance,
            FakeProxy.Create<IPermissionService>(_ => null)
        );
        string before = (await grain.GetSummaryAsync(CancellationToken.None)).Name;

        Func<Task> rename = () => grain.SetNameAsync("Taken", CancellationToken.None);

        await rename.Should().ThrowAsync<InvalidOperationException>();
        (await grain.GetSummaryAsync(CancellationToken.None)).Name.Should().Be(before);
    }
}
