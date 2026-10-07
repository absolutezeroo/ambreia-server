using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vortex.Messages;
using Vortex.Networking.Configuration;
using Vortex.Networking.Package;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Networking.Revisions;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Packets;
using Vortex.Revisions.Configuration;
using Vortex.Revisions.Revision20260701;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Hosting.Tests.Networking;

/// <summary>
/// Removing a handler together with its parser is only safe for the client in the field because a
/// header with no parser is dropped with a warning: it is not a parse failure, so it neither counts
/// against the session's parse budget nor closes the connection.
/// </summary>
public sealed class UnknownHeaderTests
{
    // GetTalentTrackMessageEvent: the client still sends it, the server no longer parses it.
    private const int REMOVED_HEADER = 3757;

    // GetSelectedBadgesMessageEvent: its parser reads an int, so an empty body is a parse failure.
    private const int PARSED_HEADER = 3726;

    private static readonly Revision20260701 Revision = new(
        Options.Create(new ProtocolLimitsConfig())
    );

    [Fact]
    public async Task AHeaderWithNoParser_IsNotAParseFailure_AndKeepsTheSession()
    {
        Revision.Parsers.Should().NotContainKey(REMOVED_HEADER);
        SessionProbe probe = new();

        await Handler()
            .HandleAsync(
                probe.Session,
                new ClientPacket(REMOVED_HEADER, ReadOnlyMemory<byte>.Empty),
                default
            );

        probe.ParseFailures.Should().Be(0);
        probe.Closed.Should().BeFalse();
    }

    /// <summary>Guards the guard: the probe does see a parse failure when one happens.</summary>
    [Fact]
    public async Task AParserThatThrows_IsCountedAndClosesTheSession()
    {
        Revision.Parsers.Should().ContainKey(PARSED_HEADER);
        SessionProbe probe = new();

        await Handler()
            .HandleAsync(
                probe.Session,
                new ClientPacket(PARSED_HEADER, ReadOnlyMemory<byte>.Empty),
                default
            );

        probe.ParseFailures.Should().Be(1);
        probe.Closed.Should().BeTrue();
    }

    private static PackageHandler Handler() =>
        new(
            FakeProxy.Create<IRevisionManager>(call =>
                call.Method.Name == nameof(IRevisionManager.GetRevision) ? Revision : null
            ),
            // Never reached: neither packet gets as far as dispatch.
            new MessageSystem(
                null!,
                null!,
                null!,
                null!,
                null!,
                NullLogger<MessageSystem>.Instance
            ),
            NullLogger<PackageHandler>.Instance,
            FakeProxy.Create<IVortexContextAccessor>(_ => null),
            FakeProxy.Create<IErrorGroupingSink>(_ => null),
            // A budget of one: any parse failure at all closes the session.
            new NetworkingConfig { MaxParseFailuresPerSession = 1 }
        );

    private sealed class SessionProbe
    {
        public int ParseFailures;
        public bool Closed;

        public ISessionContext Session { get; }

        public SessionProbe()
        {
            Session = FakeProxy.Create<ISessionContext>(call =>
                call.Method.Name switch
                {
                    nameof(ISessionContext.RecordParseFailure) => ++ParseFailures,
                    nameof(ISessionContext.CloseSessionAsync) => CloseAsync(),
                    "get_" + nameof(ISessionContext.RevisionId) => Revision.Revision,
                    _ => null,
                }
            );
        }

        private Task CloseAsync()
        {
            Closed = true;
            return Task.CompletedTask;
        }
    }
}
