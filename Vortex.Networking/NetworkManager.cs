using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using SuperSocket.ProtoBase;
using SuperSocket.Server.Abstractions;
using SuperSocket.Server.Abstractions.Host;
using SuperSocket.Server.Host;
using SuperSocket.WebSocket.Server;
using Vortex.Messages;
using Vortex.Networking.Configuration;
using Vortex.Networking.Extensions;
using Vortex.Networking.Package;
using Vortex.Networking.Session;
using Vortex.Networking.Tcp;
using Vortex.Networking.Ws;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Networking.Revisions;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Outgoing.Handshake;

namespace Vortex.Networking;

public sealed class NetworkManager(
    IOptions<NetworkingConfig> config,
    ISessionGateway sessionGateway,
    IRevisionManager revisionManager,
    MessageSystem messageSystem,
    ILoggerFactory loggerFactory,
    IGrainFactory grainFactory,
    IVortexContextAccessor contextAccessor,
    IErrorGroupingSink errorSink,
    IVortexMetrics metrics
) : INetworkManager
{
    private readonly NetworkingConfig _config = config.Value;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly ILogger<NetworkManager> _logger = loggerFactory.CreateLogger<NetworkManager>();
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
    private readonly MessageSystem _messageSystem = messageSystem;
    private readonly IVortexContextAccessor _contextAccessor = contextAccessor;
    private readonly IErrorGroupingSink _errorSink = errorSink;
    private readonly IVortexMetrics _metrics = metrics;
    private readonly IPackageEncoder<OutgoingPackage> _packageEncoder = new PackageEncoder(
        revisionManager,
        loggerFactory.CreateLogger<PackageEncoder>(),
        metrics
    );
    private readonly IRevisionManager _revisionManager = revisionManager;
    private readonly ISessionGateway _sessionGateway = sessionGateway;

    private readonly Lock _tcpGate = new();
    private readonly Lock _wsGate = new();

    private readonly ConcurrentDictionary<string, WebSocketSessionContext> _wsSessions = new();

    private IHost? _tcpHost;
    private IHost? _wsHost;

    public async Task StartAsync(CancellationToken ct)
    {
        bool needTcpStart = false;
        bool needsWsStart = false;

        lock (_tcpGate)
        {
            if (_tcpHost is null)
            {
                CreateTcpSocket();
                needTcpStart = true;
            }
        }

        lock (_wsGate)
        {
            if (_wsHost is null)
            {
                CreateWsSocket();
                needsWsStart = true;
            }
        }

        if (needTcpStart && _tcpHost is not null)
        {
            await _tcpHost.StartAsync(ct).ConfigureAwait(false);
        }

        if (needsWsStart && _wsHost is not null)
        {
            await _wsHost.StartAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task StopAsync()
    {
        IHost? tcpHostToStop = null;
        IHost? wsHostToStop = null;

        lock (_tcpGate)
        {
            tcpHostToStop = _tcpHost;
            _tcpHost = null;
        }

        lock (_wsGate)
        {
            wsHostToStop = _wsHost;
            _wsHost = null;
        }

        Task stopTcp = tcpHostToStop?.StopAsync(TimeSpan.FromSeconds(5)) ?? Task.CompletedTask;
        Task stopWs = wsHostToStop?.StopAsync(TimeSpan.FromSeconds(5)) ?? Task.CompletedTask;

        await Task.WhenAll(stopTcp, stopWs).ConfigureAwait(false);
    }

    private void CreateTcpSocket()
    {
        ISuperSocketHostBuilder<IClientPacket>? builder =
            SuperSocketHostBuilder.Create<IClientPacket>();

        builder.ConfigureServerOptions((ctx, config) => config.GetSection("TcpServer"));
        builder.ConfigureLogging((ctx, logging) => logging.ClearProviders());
        builder.ConfigureServices((ctx, services) => ConfigureCommonServices(services));
        builder.UseSession<SessionContext>();
        builder.UsePipelineFilter<TcpFilter>();
        builder.UseSessionGateway();

        _tcpHost = builder.Build();
    }

    private void CreateWsSocket()
    {
        WebSocketHostBuilder? builder = WebSocketHostBuilder.Create();

        builder.ConfigureServerOptions((ctx, config) => config.GetSection("WebSocketServer"));
        builder.ConfigureLogging((ctx, logging) => logging.ClearProviders());
        builder.ConfigureServices((ctx, services) => ConfigureCommonServices(services));

        ClientPacketDecoder decoder = new(_config.MaxPacketBodyBytes);
        PackageHandler packageHandler = new(
            _revisionManager,
            _messageSystem,
            _loggerFactory.CreateLogger<PackageHandler>(),
            _contextAccessor,
            _errorSink,
            _config
        );
        WsPackageHandler wsPackageHandler = new(
            decoder,
            packageHandler,
            _loggerFactory.CreateLogger<WsPackageHandler>()
        );

        builder.UseSessionHandler(
            async session =>
            {
                WebSocketSession wsSession = (WebSocketSession)session;
                WebSocketSessionContext context = new(
                    wsSession,
                    _packageEncoder,
                    _loggerFactory.CreateLogger<WebSocketSessionContext>()
                );

                context.SetRevisionId(_revisionManager.DefaultRevisionId);

                if (
                    !await _sessionGateway
                        .AddSessionAsync(context.SessionKey, context)
                        .ConfigureAwait(false)
                )
                {
                    // Over a session cap (SEC-10). Registered in neither map -- note this runs
                    // BEFORE _wsSessions would have held it, so the close is the whole cleanup and
                    // no heartbeat was started to cancel.
                    await context.CloseSessionAsync().ConfigureAwait(false);

                    return;
                }

                _wsSessions[session.SessionID] = context;

                context.Touch();

                _ = RunWsHeartbeatAsync(context, context.HeartbeatCts.Token);

                _logger.LogInformation(
                    "WebSocket session connected: {SessionId}",
                    session.SessionID
                );
            },
            async (session, e) =>
            {
                if (_wsSessions.TryRemove(session.SessionID, out WebSocketSessionContext? context))
                {
                    await context.HeartbeatCts.CancelAsync().ConfigureAwait(false);

                    await _sessionGateway
                        .RemoveSessionAsync(context.SessionKey, CancellationToken.None)
                        .ConfigureAwait(false);
                    context.Dispose();
                }

                _logger.LogInformation(
                    "WebSocket session disconnected: {SessionId}",
                    session.SessionID
                );
            }
        );

        builder.UseWebSocketMessageHandler(
            async (session, message) =>
            {
                if (
                    !_wsSessions.TryGetValue(
                        session.SessionID,
                        out WebSocketSessionContext? context
                    )
                )
                {
                    return;
                }

                await wsPackageHandler
                    .HandleManualAsync(context, message, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        );

        _wsHost = builder.Build();
    }

    /// <summary>
    ///     Keeps a WebSocket session's link warm while it is otherwise idle.
    ///
    ///     The browser client cannot do this itself and stay faithful to the Flash client: its only
    ///     periodic traffic is <c>LatencyTracker</c>, which AS3 drives from the frame loop, and a
    ///     browser pauses requestAnimationFrame outright for a backgrounded tab. With no server
    ///     heartbeat either, a hidden tab carried zero bytes in both directions. PING/PONG (composer
    ///     1407 / event 362) is the AS3 protocol's own answer, and both halves were already
    ///     implemented; only this timer was missing.
    ///
    ///     It does NOT reap silent sessions by default — see <c>NetworkingConfig.PongTimeout</c>.
    ///     A tab answers PINGs for the first few minutes in the background and then stops dead when
    ///     Chrome freezes the page; it is not gone, and closing it there is precisely the disconnect
    ///     this was meant to prevent.
    ///
    ///     Note this lives here rather than in <c>UsePingPong()</c>: that extension only covers the
    ///     TCP listener (and is disabled at its call site), while the WebSocket listener builds its
    ///     sessions through <c>UseSessionHandler</c> and never went through it.
    /// </summary>
    private async Task RunWsHeartbeatAsync(WebSocketSessionContext session, CancellationToken ct)
    {
        using PeriodicTimer timer = new(
            TimeSpan.FromMilliseconds(_config.PingIntervalMilliseconds)
        );

        DateTime lastPingUtc = DateTime.MinValue;

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                // The closed event is what normally ends this loop, and it is raised a moment after
                // the connection actually goes. Sending into that gap throws instead of returning,
                // so a heartbeat that ignored it would rediscover the same dead connection on every
                // tick -- one logged exception each time -- for as long as the gap lasted.
                if (!session.IsConnected)
                {
                    return;
                }

                TimeSpan silence = DateTime.UtcNow - session.LastActivityUtc;

                // Off by default — see NetworkingConfig.PongTimeout. A silent session is almost
                // always a frozen background tab, which cannot answer and is not dead.
                if (_config.PongTimeout > TimeSpan.Zero && silence >= _config.PongTimeout)
                {
                    _logger.LogInformation(
                        "Closing WebSocket session {SessionKey}: silent for {Silence}",
                        session.SessionKey,
                        silence
                    );

                    await session.CloseSessionAsync().ConfigureAwait(false);

                    return;
                }

                if (silence < _config.IdleOkActivityWindow)
                {
                    continue;
                }

                // A session that never answers keeps `silence` above the window forever, so without
                // this it would be pinged on every tick — six a minute, for as long as the tab stays
                // frozen. Space them by the same window instead.
                if (DateTime.UtcNow - lastPingUtc < _config.IdleOkActivityWindow)
                {
                    continue;
                }

                lastPingUtc = DateTime.UtcNow;

                await session.SendComposerAsync(new PingMessage(), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Session closed — expected.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "WebSocket heartbeat stopped for session {SessionKey}",
                session.SessionKey
            );
        }
    }

    private void ConfigureCommonServices(IServiceCollection services)
    {
        services.AddSingleton(_sessionGateway);
        services.AddSingleton(_revisionManager);
        services.AddSingleton(_messageSystem);
        services.AddSingleton(_loggerFactory);
        services.AddSingleton(_grainFactory);
        // Forwarded from the parent container so PackageHandler/PackageEncoder resolve these as
        // required dependencies in the TCP host's own child container too, instead of silently
        // getting null (see NET-01): a missing observability sink must be a startup error, not an
        // invisible gap in the dashboard's error grouping.
        services.AddSingleton(_contextAccessor);
        services.AddSingleton(_errorSink);
        services.AddSingleton(_metrics);
        // PackageHandler reads LogPacketPayloadOnError from this. Forwarded resolved rather than as
        // IOptions<> for the same reason as the rest: this container only holds what is handed to it.
        services.AddSingleton(_config);
        services.AddSingleton<IPackageEncoder<OutgoingPackage>, PackageEncoder>();
        services.AddSingleton<IPackageHandler<IClientPacket>, PackageHandler>();
        services.AddSingleton<IClientPacketDecoder>(_ => new ClientPacketDecoder(
            _config.MaxPacketBodyBytes
        ));
    }
}
