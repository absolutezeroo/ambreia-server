using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vortex.WebApi.Configuration;
using Vortex.WebApi.Session;

namespace Vortex.WebApi.Hosting;

/// <summary>
/// Adapts ASP.NET Core request/response state onto the web API's cookie-backed session model,
/// replacing the manual <c>HttpListener</c> cookie parsing. The session cookie name is preserved so
/// the existing Flash onboarding client keeps working unchanged.
/// </summary>
internal static class WebApiHttpContextExtensions
{
    public const string SessionCookieName = "habbo-web-session";

    /// <summary>The opaque session id carried by the request cookie, or null when absent.</summary>
    public static string? SessionId(this HttpContext ctx)
    {
        string? value = ctx.Request.Cookies[SessionCookieName];

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>The authenticated account id for the current session, or null when unauthenticated.</summary>
    public static int? AccountId(this HttpContext ctx, WebApiSessionStore sessions) =>
        sessions.GetAccountId(ctx.SessionId());

    /// <summary>
    /// The raw <c>Accept-Language</c> header, unparsed, or null when absent. No endpoint reads it any
    /// more.
    /// </summary>
    public static string? AcceptedLanguages(this HttpContext ctx)
    {
        string value = ctx.Request.Headers.AcceptLanguage.ToString();

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>The caller's remote IP, defaulting to loopback when it cannot be resolved.</summary>
    public static string RemoteIp(this HttpContext ctx) =>
        ctx.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

    /// <summary>
    /// The raw <c>User-Agent</c>, unparsed. No endpoint reads it any more.
    /// </summary>
    public static string UserAgent(this HttpContext ctx) =>
        ctx.Request.Headers.UserAgent.ToString();

    /// <summary>
    /// Issues the session cookie (HttpOnly, Lax, Secure).
    ///
    /// <para>
    /// <c>Secure</c> is unconditional rather than derived from <c>ctx.Request.IsHttps</c>. The
    /// deployment <see cref="Vortex.Primitives.Hosting.ListenerSecurity" /> itself recommends
    /// terminates TLS upstream and forwards to Kestrel over plain http, so the scheme test came out
    /// false in exactly the deployment it was meant to protect — and stripped the flag off the one
    /// credential guarding every authenticated route. Loopback needs no exception: browsers treat
    /// <c>http://localhost</c> as a secure context and keep the cookie.
    /// </para>
    ///
    /// <para>
    /// The single opt-out is the operator's own <c>Vortex:WebApi:AllowInsecureRemoteHttp</c>, which
    /// already says "I am serving credentials in cleartext on purpose". Anyone who has not set it
    /// and is not on TLS finds that login does not stick, which is the correct failure.
    /// </para>
    /// </summary>
    public static void IssueSessionCookie(this HttpContext ctx, string sessionId) =>
        ctx.Response.Cookies.Append(
            SessionCookieName,
            sessionId,
            new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                IsEssential = true,
                Secure = !ctx
                    .RequestServices.GetRequiredService<IOptions<WebApiConfig>>()
                    .Value.AllowInsecureRemoteHttp,
            }
        );

    /// <summary>
    /// Drops the session cookie once logout has made the token stop resolving.
    /// </summary>
    public static void ClearSessionCookie(this HttpContext ctx) =>
        ctx.Response.Cookies.Delete(SessionCookieName, new CookieOptions { Path = "/" });
}
