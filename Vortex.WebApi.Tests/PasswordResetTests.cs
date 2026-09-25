using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.WebApi.Configuration;
using Vortex.WebApi.Services;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// habbo.com's forgotten-password flow, end to end through the real service and a recording mailer:
/// the link it mails, and every way a token has to stop working.
/// </summary>
public sealed class PasswordResetTests
{
    private const string EMAIL = "player@example.com";
    private const string SITE = "https://hotel.test";

    [Fact]
    public async Task WithNoMailServer_TheRequestSaysSo()
    {
        await using WebApiTestFactory factory = new(c => c.PublicSiteUrl = SITE);

        using HttpResponseMessage response = await SendAsync(factory, EMAIL);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task WithNoSiteUrl_NoLinkIsMailed()
    {
        // The request's own Host is never used to build the link — see WebApiPasswordResetService.
        await using WebApiTestFactory factory = new(c => c.Smtp = Smtp());
        await SeedAsync(factory);

        using HttpResponseMessage response = await SendAsync(factory, EMAIL);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        factory.Mailer.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task AKnownAndAnUnknownAddress_GetTheSameAnswer_AndOnlyOneGetsMail()
    {
        await using WebApiTestFactory factory = Configured();
        await SeedAsync(factory);

        using HttpResponseMessage known = await SendAsync(factory, "Player@Example.com ");
        using HttpResponseMessage unknown = await SendAsync(factory, "nobody@example.com");

        known.StatusCode.Should().Be(HttpStatusCode.OK);
        unknown.StatusCode.Should().Be(HttpStatusCode.OK);

        factory.Mailer.Sent.Should().ContainSingle();
        (string to, string link) = factory.Mailer.Sent.Single();
        to.Should().Be(EMAIL);
        link.Should().StartWith($"{SITE}/reset-password/");
    }

    [Fact]
    public async Task TheMailedToken_SetsANewPassword()
    {
        await using WebApiTestFactory factory = Configured();
        await SeedAsync(factory);

        string token = await RequestTokenAsync(factory);

        using HttpResponseMessage tooShort = await ChangeAsync(factory, token, "short");
        using HttpResponseMessage changed = await ChangeAsync(factory, token, "a-new-password");

        tooShort.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        changed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AToken_StopsWorkingOnceThePasswordChanged()
    {
        await using WebApiTestFactory factory = Configured();
        int accountId = await SeedAsync(factory);

        string token = await RequestTokenAsync(factory);

        // What any password change does to the row — including the one this token just made.
        await using (VortexDbContext db = await factory.DbContexts.CreateDbContextAsync())
        {
            PlayerAccountEntity account = await db.PlayerAccounts.SingleAsync(a =>
                a.Id == accountId
            );
            account.PasswordHash = "$2a$12$another-hash";
            await db.SaveChangesAsync();
        }

        using HttpResponseMessage response = await ChangeAsync(factory, token, "a-new-password");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AnExpiredOrForgedToken_IsRefused()
    {
        await using WebApiTestFactory factory = Configured();
        int accountId = await SeedAsync(factory);

        string expired = WebApiPasswordResetService.CreateToken(
            accountId,
            HASH,
            DateTimeOffset.UtcNow.AddMinutes(-1)
        );
        string forged = WebApiPasswordResetService.CreateToken(
            accountId,
            "not-the-hash",
            DateTimeOffset.UtcNow.AddHours(1)
        );

        foreach (string token in new[] { expired, forged, "garbage", "a.b.c", "." })
        {
            using HttpResponseMessage response = await ChangeAsync(
                factory,
                token,
                "a-new-password"
            );

            response.StatusCode.Should().Be(HttpStatusCode.NotFound, token);
        }
    }

    private const string HASH = "$2a$12$abcdefghijklmnopqrstuuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ012";

    private static WebApiConfig.SmtpOptions Smtp() =>
        new() { Host = "smtp.test", From = "no-reply@hotel.test" };

    /// <summary>Mail configured, and a limiter loose enough for a test that calls six times.</summary>
    private static WebApiTestFactory Configured() =>
        new(c =>
        {
            c.Smtp = Smtp();
            c.PublicSiteUrl = SITE;
            c.LoginRateLimit = new WebApiConfig.RateLimitOptions { PermitLimit = 100 };
        });

    private static async Task<int> SeedAsync(WebApiTestFactory factory)
    {
        await using VortexDbContext db = await factory.DbContexts.CreateDbContextAsync();

        PlayerAccountEntity account = new() { Email = EMAIL, PasswordHash = HASH };
        db.PlayerAccounts.Add(account);
        await db.SaveChangesAsync();

        return account.Id;
    }

    private static async Task<string> RequestTokenAsync(WebApiTestFactory factory)
    {
        using HttpResponseMessage response = await SendAsync(factory, EMAIL);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return factory.Mailer.Sent.Single().Link.Split('/').Last();
    }

    private static Task<HttpResponseMessage> SendAsync(WebApiTestFactory factory, string email) =>
        factory.Client.PostAsJsonAsync("/api/public/forgotPassword/send", new { email });

    private static Task<HttpResponseMessage> ChangeAsync(
        WebApiTestFactory factory,
        string token,
        string password
    ) =>
        factory.Client.PostAsJsonAsync(
            "/api/public/forgotPassword/changePassword",
            new { token, password }
        );
}
