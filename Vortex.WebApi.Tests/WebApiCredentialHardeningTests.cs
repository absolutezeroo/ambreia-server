using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vortex.Database.Context;
using Vortex.Primitives.Authentication;
using Vortex.WebApi.Configuration;
using Vortex.WebApi.Hosting;
using Vortex.WebApi.Http;
using Vortex.WebApi.Services;
using Vortex.WebApi.Session;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// Three credential rules the public web API used to state only on the paths that happened to run
/// through the game client: the shape of a player name, the minimum length of a password, and
/// whether the session cookie insists on TLS. Each had a second, unguarded HTTP door onto the same
/// state, so each is asserted here at the door rather than at the route that already behaved.
/// </summary>
public sealed class WebApiCredentialHardeningTests
{
    private sealed class TestDbContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);

        public Task<VortexDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
            Task.FromResult(CreateDbContext());
    }

    /// <summary>Never consulted: every case here is refused before a credential is verified.</summary>
    private sealed class UnusedAuthenticator : IAccountAuthenticator
    {
        public Task<AccountVerification> VerifyCredentialsAsync(
            string email,
            string password,
            CancellationToken ct = default
        ) =>
            throw new InvalidOperationException("the request should have been refused before this");
    }

    private static (WebApiAuthService Service, TestDbContextFactory Factory) BuildAuth()
    {
        DbContextOptions<VortexDbContext> options = new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        TestDbContextFactory factory = new(options);
        IOptions<WebApiConfig> config = Options.Create(new WebApiConfig());

        return (
            new WebApiAuthService(
                factory,
                new UnusedAuthenticator(),
                new WebApiSessionStore(config),
                config,
                NullLogger<WebApiAuthService>.Instance
            ),
            factory
        );
    }

    private static async Task<int> AccountCountAsync(TestDbContextFactory factory)
    {
        await using VortexDbContext db = factory.CreateDbContext();

        return await db.PlayerAccounts.CountAsync();
    }

    // --- the password rule the change path already had, and registration did not -------------------

    [Theory]
    [InlineData("a")]
    [InlineData("short")]
    [InlineData("1234567")]
    public async Task Register_RefusesAPasswordShorterThanTheChangePathWouldAccept(string password)
    {
        password.Length.Should().BeLessThan(AccountPasswordRules.MINIMUM_LENGTH);

        (WebApiAuthService service, TestDbContextFactory factory) = BuildAuth();

        (bool success, int _, string? error) = await service.RegisterAsync(
            "new@example.com",
            password,
            CancellationToken.None
        );

        success.Should().BeFalse();
        error.Should().Be("pocket.auth.password_too_short");

        // Nothing half-written: a refused registration leaves no account behind for the email.
        (await AccountCountAsync(factory))
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task Register_RefusesAPasswordPastTheLengthBCryptActuallyHashes()
    {
        (WebApiAuthService service, TestDbContextFactory factory) = BuildAuth();

        (bool success, int _, string? error) = await service.RegisterAsync(
            "new@example.com",
            new string('p', 73),
            CancellationToken.None
        );

        success.Should().BeFalse();
        error.Should().Be("pocket.auth.password_too_long");
        (await AccountCountAsync(factory)).Should().Be(0);
    }

    [Fact]
    public async Task Register_AcceptsAPasswordThatMeetsTheRule()
    {
        (WebApiAuthService service, TestDbContextFactory factory) = BuildAuth();

        (bool success, int accountId, string? error) = await service.RegisterAsync(
            "new@example.com",
            new string('p', AccountPasswordRules.MINIMUM_LENGTH),
            CancellationToken.None
        );

        success.Should().BeTrue();
        error.Should().BeNull();
        accountId.Should().BeGreaterThan(0);
        (await AccountCountAsync(factory)).Should().Be(1);
    }

    // --- the name shape the in-game rename enforces, on the HTTP routes that bypassed it -----------

    [Theory]
    [InlineData("Habbo")]
    [InlineData("user_1")]
    [InlineData("a-b.c")]
    public void NameShape_AcceptsWhatTheInGameRenameAccepts(string name) =>
        NameShape.IsWellFormed(name).Should().BeTrue();

    [Theory]
    [InlineData("Admin ")] // trailing space -- a staff lookalike that resolves as its own name
    [InlineData("Admin​")] // zero-width space, invisible in a room
    [InlineData("bad!")]
    [InlineData("ab")] // under the minimum
    [InlineData("sixteen characters")] // over the maximum
    [InlineData(null)]
    [InlineData("")]
    public void NameShape_RefusesWhatTheInGameRenameRefuses(string? name) =>
        NameShape.IsWellFormed(name).Should().BeFalse();

    [Fact]
    public void NameShape_RefusesANameUpToTheColumnWidth() =>
        NameShape.IsWellFormed(new string('a', 512)).Should().BeFalse();

    [Fact]
    public void NameSelectRequest_IsInvalidForANameTheClientCouldNotHaveChosen()
    {
        new NameSelectRequest("Admin​", 1).IsValid.Should().BeFalse();
        new NameSelectRequest("Habbo", 1).IsValid.Should().BeTrue();

        // The owned-avatar id is still its own condition, unchanged by the name rule.
        new NameSelectRequest("Habbo", 0)
            .IsValid.Should()
            .BeFalse();
    }

    [Fact]
    public void CreateAvatarRequest_LeavesTheBlankRegistrationNameValid()
    {
        // Blank is the registration path: WebApiPlayerService assigns a placeholder that the
        // onboarding rename replaces. Refusing it here would break account creation.
        new CreateAvatarRequest(null, null, null)
            .IsValid.Should()
            .BeTrue();
        new CreateAvatarRequest("  ", null, null).IsValid.Should().BeTrue();

        new CreateAvatarRequest("Habbo", null, null).IsValid.Should().BeTrue();
        new CreateAvatarRequest("bad name", null, null).IsValid.Should().BeFalse();
    }

    // --- the session cookie's Secure flag, which the proxy deployment used to strip ----------------

    private static CookieOptions? IssuedCookie(bool allowInsecureRemoteHttp)
    {
        ServiceCollection services = new();
        services.AddSingleton(
            Options.Create(new WebApiConfig { AllowInsecureRemoteHttp = allowInsecureRemoteHttp })
        );

        DefaultHttpContext ctx = new() { RequestServices = services.BuildServiceProvider() };

        // Plain http, which is exactly what Kestrel sees behind a TLS-terminating proxy -- the
        // condition the old ctx.Request.IsHttps test failed on.
        ctx.Request.Scheme = "http";
        ctx.IssueSessionCookie("session-id");

        string? header = ctx.Response.Headers.SetCookie;

        header.Should().NotBeNull();
        header.Should().Contain(WebApiHttpContextExtensions.SessionCookieName);

        return new CookieOptions
        {
            Secure = header!.Contains("secure", StringComparison.OrdinalIgnoreCase),
            HttpOnly = header.Contains("httponly", StringComparison.OrdinalIgnoreCase),
        };
    }

    [Fact]
    public void SessionCookie_IsSecureOverPlainHttpBehindAProxy() =>
        IssuedCookie(allowInsecureRemoteHttp: false)!.Secure.Should().BeTrue();

    [Fact]
    public void SessionCookie_DropsSecureOnlyForTheOperatorsExplicitCleartextOptIn() =>
        IssuedCookie(allowInsecureRemoteHttp: true)!.Secure.Should().BeFalse();

    [Fact]
    public void SessionCookie_StaysHttpOnlyEitherWay()
    {
        IssuedCookie(allowInsecureRemoteHttp: false)!.HttpOnly.Should().BeTrue();
        IssuedCookie(allowInsecureRemoteHttp: true)!.HttpOnly.Should().BeTrue();
    }
}
