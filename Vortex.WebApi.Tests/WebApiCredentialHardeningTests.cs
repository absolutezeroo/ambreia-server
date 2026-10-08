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
using Vortex.Primitives.Players;
using Vortex.WebApi.Configuration;
using Vortex.WebApi.Hosting;
using Vortex.WebApi.Http.Requests;
using Vortex.WebApi.Services;
using Vortex.WebApi.Session;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// Credential rules the public web API used to state only on the paths that happened to run
/// through the game client: the shape of a player name, the address a registration accepts, and
/// whether the session cookie insists on TLS. Each had a second, unguarded HTTP door onto the same
/// state, so each is asserted here at the door rather than at the route that already behaved.
/// </summary>
public sealed class WebApiCredentialHardeningTests
{
    // The password rules a registration applies are the authenticator's now, and tested there
    // (AccountAuthenticatorTests).

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("Name <a@b.c>")]
    [InlineData("a@b.c, d@e.f")]
    [InlineData("")]
    [InlineData(null)]
    public void RegisterRequest_IsInvalidForSomethingThatIsNotOneAddress(string? email) =>
        new RegisterRequest(email, "password1", null).IsValid.Should().BeFalse();

    [Fact]
    public void RegisterRequest_AcceptsAnAddress() =>
        new RegisterRequest(" a@b.co ", "password1", null).IsValid.Should().BeTrue();

    [Fact]
    public void CreateAvatarRequest_IsInvalidForALookWiderThanItsColumn() =>
        new CreateAvatarRequest(null, new string('a', 256), "M").IsValid.Should().BeFalse();

    // --- the player-name shape, on the HTTP routes that used to bypass it --------------------------

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
