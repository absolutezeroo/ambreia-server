using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Configuration;
using Vortex.PacketHandlers.Handshake;
using Vortex.PacketHandlers.Rpg;
using Vortex.PacketHandlers.Rpg.Characters;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rpg.Characters.Grains;
using Vortex.Primitives.Server.Grains;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Grains;
using Vortex.Protocol.Messages.Incoming.Rpg.Characters;
using Vortex.Protocol.Messages.Outgoing.Handshake;
using Vortex.Protocol.Messages.Outgoing.Room.Session;
using Vortex.Protocol.Messages.Outgoing.Rpg.Characters;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.PacketHandlers.Tests.Rpg;

/// <summary>
/// The character screen's packets (spec F3a §5.4, §5.5): entering the world as the SSO does, only
/// one's own character, only in the account phase, and not faster than each intent's interval.
/// </summary>
public sealed class AccountPhaseTests
{
    private const int ACCOUNT_ID = 3;
    private const int PLAYER_ID = 7;
    private const int START_ROOM_ID = 42;

    private static readonly SessionKey Key = new("socket");

    private readonly List<object> _toPlayer = [];
    private readonly List<object> _toAccount = [];
    private readonly List<(SessionKey Key, int PlayerId)> _attached = [];
    private readonly List<string> _refused = [];
    private readonly List<int> _logins = [];
    private bool _owns = true;
    private bool _accountSendFails;

    [Fact]
    public async Task EnteringTheWorld_SendsTheSameBurstAsTheSso()
    {
        await EnterWorldAsync();

        _attached.Should().Equal((Key, PLAYER_ID));
        _logins.Should().Equal([PLAYER_ID], "the login audit a player ticket writes");
        _toPlayer
            .Select(c => c.GetType())
            .Should()
            .Equal(
                typeof(AuthenticationOKMessage),
                typeof(UserRightsMessage),
                typeof(RoomForwardMessageComposer)
            );
        _toPlayer
            .OfType<RoomForwardMessageComposer>()
            .Single()
            .RoomId.Should()
            .Be(new RoomId(START_ROOM_ID));
    }

    /// <summary>Review Focus 3: the account phase continues, nothing is attached.</summary>
    [Fact]
    public async Task EnteringAnotherAccountsCharacter_IsRefused()
    {
        _owns = false;

        await EnterWorldAsync();

        _attached.Should().BeEmpty();
        _toPlayer.Should().BeEmpty();
    }

    /// <summary>Review Focus 1: a character-screen packet sent in the world never reaches its handler.</summary>
    [Fact]
    public async Task AnAccountPhasePacketInTheWorld_IsDropped()
    {
        AccountPhaseGateBehavior gate = new(
            Metrics(),
            NullLogger<AccountPhaseGateBehavior>.Instance
        );
        int passed = 0;

        await gate.InvokeAsync(
            new RpgCharacterListRequestMessage(),
            Context(playerId: PLAYER_ID, accountId: 0),
            () => PassAsync(ref passed),
            CancellationToken.None
        );
        passed.Should().Be(0);
        _refused.Should().Equal("phase");

        await gate.InvokeAsync(
            new RpgCharacterListRequestMessage(),
            Context(playerId: -1, accountId: ACCOUNT_ID),
            () => PassAsync(ref passed),
            CancellationToken.None
        );
        passed.Should().Be(1, "on the character screen it goes through");
    }

    [Fact]
    public async Task ASecondCreateWithinTwoSeconds_IsRateLimited()
    {
        RpgIntentLimitBehavior limit = Limit();
        int passed = 0;
        RpgCharacterCreateMessage create = new()
        {
            Name = "Bob",
            Figure = "",
            Gender = "M",
        };

        await limit.InvokeAsync(
            create,
            Context(),
            () => PassAsync(ref passed),
            CancellationToken.None
        );
        await limit.InvokeAsync(
            create,
            Context(),
            () => PassAsync(ref passed),
            CancellationToken.None
        );

        passed.Should().Be(1);
        _refused.Should().Equal("rate_limited");
        _toAccount
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new RpgCharacterResultMessageComposer
                {
                    Action = RpgCharacterResultMessageComposer.ActionCreate,
                    Code = "rate_limited",
                    PlayerId = 0,
                }
            );
    }

    /// <summary>
    /// The pipeline runs the next step when a behavior throws: a refusal that could not be sent must
    /// not become a pass.
    /// </summary>
    [Fact]
    public async Task ARefusalThatCannotBeSent_IsStillARefusal()
    {
        RpgIntentLimitBehavior limit = Limit();
        int passed = 0;
        RpgCharacterCreateMessage create = new()
        {
            Name = "Bob",
            Figure = "",
            Gender = "M",
        };

        await limit.InvokeAsync(
            create,
            Context(),
            () => PassAsync(ref passed),
            CancellationToken.None
        );
        _accountSendFails = true;
        Func<Task> second = async () =>
            await limit.InvokeAsync(
                create,
                Context(),
                () => PassAsync(ref passed),
                CancellationToken.None
            );

        await second.Should().NotThrowAsync();
        passed.Should().Be(1);
    }

    [Fact]
    public async Task TheLimiter_KeepsIntentsApart()
    {
        RpgIntentLimitBehavior limit = Limit();
        int passed = 0;

        await limit.InvokeAsync(
            new RpgCharacterCreateMessage
            {
                Name = "Bob",
                Figure = "",
                Gender = "M",
            },
            Context(),
            () => PassAsync(ref passed),
            CancellationToken.None
        );
        await limit.InvokeAsync(
            new RpgCharacterListRequestMessage(),
            Context(),
            () => PassAsync(ref passed),
            CancellationToken.None
        );

        passed.Should().Be(2, "a create does not use up the list's allowance");
        _refused.Should().BeEmpty();
    }

    private static ValueTask PassAsync(ref int passed)
    {
        passed++;

        return ValueTask.CompletedTask;
    }

    private async Task EnterWorldAsync()
    {
        IGrainFactory grains = Grains();
        RpgEnterWorldMessageHandler handler = new(
            grains,
            FakeProxy.Create<IAuthenticationService>(call =>
            {
                if (call.Method.Name == nameof(IAuthenticationService.RecordLoginAsync))
                {
                    _logins.Add((int)call.Args![0]!);
                }

                return null;
            }),
            new WorldEntry(
                FakeProxy.Create<ISessionGateway>(call =>
                {
                    if (call.Method.Name == nameof(ISessionGateway.AddSessionToPlayerAsync))
                    {
                        _attached.Add(
                            ((SessionKey)call.Args![0]!, ((PlayerId)call.Args[1]!).Value)
                        );
                    }

                    return null;
                }),
                grains,
                FakeProxy.Create<IPermissionService>(call =>
                    call.Method.Name == nameof(IPermissionService.ResolveForPlayerAsync)
                        ? Task.FromResult(PermissionSet.Empty)
                        : null
                ),
                FakeProxy.Create<IZoneRoomSource>(_ => null),
                NullLogger<WorldEntry>.Instance
            ),
            NullLogger<RpgEnterWorldMessageHandler>.Instance
        );

        await handler.HandleAsync(
            new RpgEnterWorldMessage { PlayerId = PLAYER_ID },
            Context(),
            CancellationToken.None
        );
    }

    private RpgIntentLimitBehavior Limit() =>
        new(
            new RpgIntentLimiter(Options.Create(new RpgIntentLimitConfig())),
            Grains(),
            Metrics(),
            NullLogger<RpgIntentLimitBehavior>.Instance
        );

    private IVortexMetrics Metrics() =>
        FakeProxy.Create<IVortexMetrics>(call =>
        {
            if (call.Method.Name == nameof(IVortexMetrics.RpgPacketRefused))
            {
                _refused.Add((string)call.Args![0]!);
            }

            return null;
        });

    private static MessageContext Context(int playerId = -1, int accountId = ACCOUNT_ID) =>
        new(
            FakeProxy.Create<ISessionContext>(call =>
                call.Method.Name == "get_SessionKey" ? Key : (object?)null
            ),
            playerId,
            -1,
            accountId
        );

    private IGrainFactory Grains() =>
        FakeProxy.Create<IGrainFactory>(call =>
        {
            if (call.Method.Name != nameof(IGrainFactory.GetGrain))
            {
                return null;
            }

            Type grain = call.Method.GetGenericArguments()[0];

            return grain == typeof(IAccountCharactersGrain)
                    ? FakeProxy.Create<IAccountCharactersGrain>(inner =>
                        inner.Method.Name == nameof(IAccountCharactersGrain.OwnsAsync)
                            ? Task.FromResult(_owns)
                            : null
                    )
                : grain == typeof(IAccountSessionGrain)
                    ? FakeProxy.Create<IAccountSessionGrain>(inner =>
                    {
                        if (inner.Method.Name != nameof(IAccountSessionGrain.SendComposerAsync))
                        {
                            return null;
                        }

                        if (_accountSendFails)
                        {
                            return Task.FromException(new TimeoutException("silo busy"));
                        }

                        _toAccount.AddRange(((object[])inner.Args![0]!).Cast<object>());

                        return null;
                    })
                : grain == typeof(IServerConfigGrain)
                    ? FakeProxy.Create<IServerConfigGrain>(inner =>
                        inner.Method.Name == nameof(IServerConfigGrain.GetIntAsync)
                            ? Task.FromResult(START_ROOM_ID)
                            : null
                    )
                : grain == typeof(IZoneTestGrain)
                    ? FakeProxy.Create<IZoneTestGrain>(_ => Task.FromResult<RoomId?>(null))
                : grain == typeof(IPlayerPresenceGrain)
                    ? FakeProxy.Create<IPlayerPresenceGrain>(inner =>
                    {
                        if (inner.Method.Name == nameof(IPlayerPresenceGrain.SendComposerAsync))
                        {
                            _toPlayer.AddRange(((object[])inner.Args![0]!).Cast<object>());
                        }

                        return null;
                    })
                : null;
        });
}
