using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Handshake;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Sessions;
using Vortex.Primitives.Rpg.Characters.Grains;
using Vortex.Protocol.Messages.Incoming.Handshake;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.PacketHandlers.Tests;

/// <summary>
/// Which way a ticket sends the socket (spec F3a §4): a player ticket into the world, an account
/// ticket onto the character screen, anything else closed. What the world entry sends is the
/// presence grain's (<c>WorldEntryBurstTests</c>).
/// </summary>
public sealed class SsoTicketMessageHandlerTests
{
    private const int PLAYER_ID = 501;
    private const int ACCOUNT_ID = 3;

    private static readonly SessionKey Key = new("socket");

    private readonly List<string> _calls = [];

    [Fact]
    public async Task APlayerTicket_EntersTheWorld()
    {
        await SignInAsync(new TicketOwner(PLAYER_ID, 0));

        _calls.Should().Equal($"world:{PLAYER_ID}");
    }

    /// <summary>An account ticket opens the character screen, not the world.</summary>
    [Fact]
    public async Task AnAccountTicket_OpensTheAccountPhase()
    {
        await SignInAsync(new TicketOwner(0, ACCOUNT_ID));

        _calls.Should().Equal($"account:{ACCOUNT_ID}", "open");
    }

    [Fact]
    public async Task AnUnknownTicket_ClosesTheSocket()
    {
        await SignInAsync(TicketOwner.None);

        _calls.Should().Equal("close");
    }

    private async Task SignInAsync(TicketOwner owner)
    {
        SSOTicketMessageHandler handler = new(
            FakeProxy.Create<IAuthenticationService>(call =>
                call.Method.Name == nameof(IAuthenticationService.ResolveTicketAsync)
                    ? Task.FromResult(owner)
                    : null
            ),
            FakeProxy.Create<ISessionGateway>(call =>
            {
                if (call.Method.Name == nameof(ISessionGateway.AddSessionToAccountAsync))
                {
                    _calls.Add($"account:{call.Args![1]}");
                }

                return null;
            }),
            FakeProxy.Create<IGrainFactory>(call =>
                call.Method.Name == nameof(IGrainFactory.GetGrain)
                    ? FakeProxy.Create<IAccountCharactersGrain>(inner =>
                    {
                        if (inner.Method.Name == nameof(IAccountCharactersGrain.OpenAsync))
                        {
                            _calls.Add("open");
                        }

                        return null;
                    })
                    : null
            ),
            FakeProxy.Create<IWorldEntry>(call =>
            {
                _calls.Add($"world:{((PlayerId)call.Args![1]!).Value}");

                return null;
            }),
            NullLogger<SSOTicketMessageHandler>.Instance
        );

        await handler.HandleAsync(
            new SSOTicketMessage { SSO = "ticket" },
            new MessageContext(
                FakeProxy.Create<ISessionContext>(call =>
                {
                    if (call.Method.Name == nameof(ISessionContext.CloseSessionAsync))
                    {
                        _calls.Add("close");
                    }

                    return call.Method.Name == "get_SessionKey" ? Key : (object?)null;
                }),
                default,
                default
            ),
            CancellationToken.None
        );
    }
}
