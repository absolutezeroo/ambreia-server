using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vortex.Messages;
using Vortex.Messages.RateLimit;
using Vortex.Networking.Configuration;
using Vortex.Networking.Package;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Networking.Revisions;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Packets;
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

    // SSOTicketMessageEvent: its parser reads a string, so an empty body is a parse failure.
    private const int PARSED_HEADER = 882;

    private static readonly Revision20260701 Revision = new();

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

    /// <summary>
    /// An unknown header costs a token of the session's own bucket, and a header with no token
    /// left is a parse failure: a frame of ten thousand of them is one closed connection and a
    /// bounded number of log lines, not ten thousand.
    /// </summary>
    [Fact]
    public async Task TenThousandUnknownHeaders_CloseTheSessionOnce_WithBoundedLogs()
    {
        const int burst = 100;
        const int budget = 10;
        int tokens = burst;
        int logLines = 0;
        SessionProbe probe = new();
        PackageHandler handler = Handler(
            parseBudget: budget,
            limiter: FakeProxy.Create<IRateLimiter>(_ => tokens-- > 0),
            logger: FakeProxy.Create<ILogger<PackageHandler>>(call =>
            {
                if (call.Method.Name == nameof(ILogger.Log))
                {
                    logLines++;
                }

                return call.Method.Name == nameof(ILogger.IsEnabled) ? true : null;
            })
        );
        ClientPacket unknown = new(REMOVED_HEADER, ReadOnlyMemory<byte>.Empty);

        for (int i = 0; i < 10_000; i++)
        {
            await handler.HandleAsync(probe.Session, unknown, default);
        }

        // The fake socket does not go away when closed, so the rest of the flood still arrives:
        // each of those costs a counter increment and nothing else.
        probe.CloseCalls.Should().Be(1, "closed once, on the budget's last failure");
        probe.ParseFailures.Should().BeGreaterThanOrEqualTo(budget);
        logLines
            .Should()
            .BeLessThanOrEqualTo(burst + 1, "one line per token, then one line for the close");
    }

    private static PackageHandler Handler(
        int parseBudget = 1,
        IRateLimiter? limiter = null,
        ILogger<PackageHandler>? logger = null
    ) =>
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
            logger ?? NullLogger<PackageHandler>.Instance,
            FakeProxy.Create<IVortexContextAccessor>(_ => null),
            FakeProxy.Create<IErrorGroupingSink>(_ => null),
            // A budget of one by default: any parse failure at all closes the session.
            new NetworkingConfig { MaxParseFailuresPerSession = parseBudget },
            // Tokens for everyone by default: the bucket is not what these tests are about.
            limiter ?? FakeProxy.Create<IRateLimiter>(_ => true),
            FakeProxy.Create<IVortexMetrics>(_ => null)
        );

    private sealed class SessionProbe
    {
        public int ParseFailures;
        public bool Closed;
        public int CloseCalls;

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
            CloseCalls++;
            return Task.CompletedTask;
        }
    }
}
