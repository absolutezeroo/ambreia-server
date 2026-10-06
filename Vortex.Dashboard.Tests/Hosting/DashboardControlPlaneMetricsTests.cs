using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vortex.Dashboard.API.Hosting;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Observability;
using Xunit;

namespace Vortex.Dashboard.Tests.Hosting;

/// <summary>
/// The control-plane counters, driven by real requests through the middleware that emits them.
/// </summary>
/// <remarks>
/// <para>
/// The rule under test is small and every part of it is a decision that can be silently wrong: which
/// statuses count, which paths, and — the one that matters — that a 403 is still counted at all.
/// Authorization short-circuits, so a middleware registered one line too low sees a clean pipeline
/// and reports nothing while an operator walks the API. Nothing else in the system would notice.
/// </para>
/// <para>
/// Driven on a bare test server rather than the real host: the pipeline this lives in needs Orleans
/// and a database to build, and none of that is what makes this rule right or wrong.
/// </para>
/// </remarks>
public sealed class DashboardControlPlaneMetricsTests
{
    private const string CAPABILITY = "rooms.read";

    [Fact]
    public async Task ASuccessfulApiCallCountsNothing()
    {
        RecordingMetrics metrics = new();

        await using WebApplication app = await BuildAppAsync(metrics);
        using HttpClient client = app.GetTestClient();

        (await client.GetAsync("/api/ok")).StatusCode.Should().Be(HttpStatusCode.OK);

        metrics.HttpErrors.Should().BeEmpty();
        metrics.Denials.Should().BeEmpty();
    }

    [Fact]
    public async Task AnErrorStatusIsCountedUnderItsCode()
    {
        RecordingMetrics metrics = new();

        await using WebApplication app = await BuildAppAsync(metrics);
        using HttpClient client = app.GetTestClient();

        await client.GetAsync("/api/boom");

        metrics.HttpErrors.Should().Equal([StatusCodes.Status500InternalServerError]);
    }

    /// <summary>
    /// The one the placement exists for. Authorization refuses before the endpoint runs, so this only
    /// passes while the middleware sits above <c>UseAuthorization</c>.
    /// </summary>
    [Fact]
    public async Task ARefusedCapabilityIsCountedAndNamed()
    {
        RecordingMetrics metrics = new();

        await using WebApplication app = await BuildAppAsync(metrics);
        using HttpClient client = app.GetTestClient();

        (await client.GetAsync("/api/guarded")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        metrics.Denials.Should().Equal([CAPABILITY]);
        metrics.HttpErrors.Should().Equal([StatusCodes.Status403Forbidden]);
    }

    /// <summary>
    /// A 403 on a route that names no capability still counts, under a placeholder rather than an
    /// empty tag — an empty string in a tag is indistinguishable from a missing dimension.
    /// </summary>
    [Fact]
    public async Task ARefusalWithNoNamedCapabilityIsCountedUnderAPlaceholder()
    {
        RecordingMetrics metrics = new();

        await using WebApplication app = await BuildAppAsync(metrics);
        using HttpClient client = app.GetTestClient();

        await client.GetAsync("/api/forbidden-plain");

        metrics.Denials.Should().Equal([DashboardControlPlaneMetrics.NO_CAPABILITY]);
    }

    /// <summary>
    /// A failing frontend asset is not a control-plane event. Counting it would put the SPA's own
    /// 404s on the same curve an operator is meant to read as "someone is probing the API".
    /// </summary>
    [Fact]
    public async Task AFailureOutsideTheApiIsNotCounted()
    {
        RecordingMetrics metrics = new();

        await using WebApplication app = await BuildAppAsync(metrics);
        using HttpClient client = app.GetTestClient();

        (await client.GetAsync("/assets/missing.js"))
            .StatusCode.Should()
            .Be(HttpStatusCode.NotFound);

        metrics.HttpErrors.Should().BeEmpty();
    }

    private static async Task<WebApplication> BuildAppAsync(IVortexMetrics metrics)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder
            .Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, AlwaysAuthenticated>("Test", null);

        // One policy that nobody can satisfy: the subject here is what happens *after* a refusal,
        // so the refusal itself is made unconditional.
        builder
            .Services.AddAuthorizationBuilder()
            .AddPolicy(CAPABILITY, policy => policy.RequireAssertion(_ => false));

        WebApplication app = builder.Build();

        DashboardControlPlaneMetrics.Use(app, metrics);

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/api/ok", () => Results.Ok());
        app.MapGet("/api/boom", () => Results.StatusCode(StatusCodes.Status500InternalServerError));
        app.MapGet("/api/guarded", () => Results.Ok()).RequireAuthorization(CAPABILITY);
        app.MapGet("/api/forbidden-plain", () => Results.Forbid());

        await app.StartAsync();

        return app;
    }

    /// <summary>Authenticates every request, so authorization is the only thing that can refuse one.</summary>
    private sealed class AlwaysAuthenticated(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            System.Security.Claims.ClaimsPrincipal principal = new(
                new System.Security.Claims.ClaimsIdentity(
                    [
                        new System.Security.Claims.Claim(
                            System.Security.Claims.ClaimTypes.Name,
                            "test-operator"
                        ),
                    ],
                    "Test"
                )
            );

            return Task.FromResult(
                AuthenticateResult.Success(new AuthenticationTicket(principal, "Test"))
            );
        }
    }

    /// <summary>Keeps the two counters this middleware emits and ignores the rest of the façade.</summary>
    private sealed class RecordingMetrics : IVortexMetrics
    {
        public List<int> HttpErrors { get; } = [];

        public List<string> Denials { get; } = [];

        public bool Enabled => true;

        public void DashboardHttpError(int statusCode) => HttpErrors.Add(statusCode);

        public void DashboardAuthorizationDenied(string capability) => Denials.Add(capability);

        public void PacketReceived(string operation, long? actorId = null, int? roomId = null) { }

        public void PacketCompleted(
            string operation,
            double elapsedMilliseconds,
            long? actorId = null,
            int? roomId = null
        ) { }

        public void PacketFailed(string operation, long? actorId = null, int? roomId = null) { }

        public void PacketDropped(string reason) { }

        public void RoomTickStepCompleted(string step, double elapsedMilliseconds) { }

        public void RoomTickCompleted(double elapsedMilliseconds) { }

        public void CommerceOperationTransitioned(
            CommerceOperationKind kind,
            CommerceOperationState state
        ) { }

        public void CommerceStepReplayed(string stepKey) { }

        public void ReferenceDataPublished(string provider, int version) { }

        public void FurnitureLogicFallback(string logicName, string family) { }

        public void RoomDirectoryCallCompleted(string method, double elapsedMilliseconds) { }

        public void DashboardAuthAttempt(string outcome) { }

        public void DashboardOperationCompleted(
            string action,
            string outcome,
            double elapsedMilliseconds
        ) { }

        public void AuditWriteFailed(string stage) { }
    }
}
