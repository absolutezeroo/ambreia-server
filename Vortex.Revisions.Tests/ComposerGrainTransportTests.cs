using System;
using System.Linq;
using FluentAssertions;
using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Protocol.Messages.Outgoing.Room.Session;
using Xunit;

namespace Vortex.Revisions.Tests;

/// <summary>
/// Everything a signed-in player receives goes through <c>PlayerPresenceGrain.SendComposerAsync</c>,
/// so every composer crosses a grain call and Orleans has to copy it. The handshake composers were
/// written straight to the socket and had no serializer; routing the SSO burst through presence then
/// failed at runtime with <c>CodecNotFoundException</c>, and every login with it. A unit test with a
/// fake presence grain never serializes anything, so this checks the declaration itself.
/// </summary>
public sealed class ComposerGrainTransportTests
{
    [Fact]
    public void EveryComposer_CanCrossAGrainCall()
    {
        Type[] missing =
        [
            .. typeof(RoomForwardMessageComposer)
                .Assembly.GetTypes()
                .Where(t =>
                    typeof(IComposer).IsAssignableFrom(t)
                    && t is { IsClass: true, IsAbstract: false }
                )
                .Where(t => !t.IsDefined(typeof(GenerateSerializerAttribute), inherit: false)),
        ];

        missing.Should().BeEmpty("a composer sent through presence must be copyable by Orleans");
    }
}
