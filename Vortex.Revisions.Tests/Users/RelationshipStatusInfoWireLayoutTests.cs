using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Snapshots.FriendList;
using Vortex.Protocol.Messages.Outgoing.Users;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Users;

/// <summary>
///     Locks RelationshipStatusInfo against the client class that parses one entry -- WIN63
///     unknowns/_SafePkg_1731/_SafeCls_3266.as:22, whose constructor reads int, int, int, string,
///     string and exposes them as relationshipStatusType, friendCount, randomFriendId,
///     randomFriendName, randomFriendFigure. The serializer used to write a short for the type and
///     omit the friend id entirely, so the first non-empty response desynchronised the read.
/// </summary>
public sealed class RelationshipStatusInfoWireLayoutTests
{
    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    private static ClientPacket SerializeAndReadBody(Type composerType, IComposer composer)
    {
        byte[] bytes = Revision.Serializers[composerType].Serialize(composer).ToArray();
        byte[] body = new byte[bytes.Length - 6];
        Array.Copy(bytes, 6, body, 0, body.Length);
        return new ClientPacket(0, body);
    }

    [Fact]
    public void Serializer_WritesFiveIntOrStringFieldsPerRelation()
    {
        RelationshipStatusInfoEventMessageComposer composer = new()
        {
            UserId = 4711,
            Relations =
            [
                new RelationshipStatusEntrySnapshot
                {
                    RelationType = 1,
                    Count = 3,
                    Name = "Frank",
                    Figure = "hd-180-1",
                    RandomFriendId = 92,
                },
                new RelationshipStatusEntrySnapshot
                {
                    RelationType = 2,
                    Count = 1,
                    Name = "Alice",
                    Figure = "hd-185-2",
                    RandomFriendId = 41,
                },
            ],
        };

        ClientPacket body = SerializeAndReadBody(
            typeof(RelationshipStatusInfoEventMessageComposer),
            composer
        );

        body.PopInt().Should().Be(4711); // userId
        body.PopInt().Should().Be(2); // relation count

        body.PopInt().Should().Be(1); // relationshipStatusType, an int32 and not a short
        body.PopInt().Should().Be(3); // friendCount
        body.PopInt().Should().Be(92); // randomFriendId
        body.PopString().Should().Be("Frank");
        body.PopString().Should().Be("hd-180-1");

        body.PopInt().Should().Be(2);
        body.PopInt().Should().Be(1);
        body.PopInt().Should().Be(41);
        body.PopString().Should().Be("Alice");
        body.PopString().Should().Be("hd-185-2");

        body.End.Should().BeTrue("the layout must consume the whole packet");
    }

    [Fact]
    public void Serializer_WritesAnEmptyListAsUserIdAndZero()
    {
        // What the official hotel answers for an account with no relationship decorators, captured
        // on AIR-1.0.31: {i:userId}{i:0} and nothing else.
        RelationshipStatusInfoEventMessageComposer composer = new()
        {
            UserId = 26020248,
            Relations = new List<RelationshipStatusEntrySnapshot>(),
        };

        ClientPacket body = SerializeAndReadBody(
            typeof(RelationshipStatusInfoEventMessageComposer),
            composer
        );

        body.PopInt().Should().Be(26020248);
        body.PopInt().Should().Be(0);
        body.End.Should().BeTrue();
    }
}
