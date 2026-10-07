using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Vortex.WebApi.Configuration;
using Vortex.WebApi.Services;

namespace Vortex.WebApi.Hosting;

/// <summary>
/// Serves the website off this same listener when <see cref="WebApiConfig.SiteRoot"/> points at its
/// built <c>dist/</c>: static files, plus an <c>index.html</c> fallback for the SPA's routes.
/// </summary>
/// <remarks>
/// Same origin as <c>/api</c> is the whole point. The session is an HttpOnly cookie; a site served
/// from another origin would need CORS and would still not carry it on a plain fetch.
/// </remarks>
internal static class WebApiSiteHosting
{
    private const string IndexFile = "index.html";

    public static void Map(WebApplication app, WebApiConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.SiteRoot))
        {
            return;
        }

        string root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(config.SiteRoot));
        PhysicalFileProvider provider = new(root);

        app.UseStaticFiles(
            new StaticFileOptions { FileProvider = provider, RequestPath = PathString.Empty }
        );

        // Every other path is one of the SPA's own hash-less entry points; the router sorts it out
        // once index.html is loaded. Registered last so it never shadows /api.
        app.MapFallback(async ctx =>
        {
            string? index = await ReadIndexAsync(root, ctx.RequestAborted).ConfigureAwait(false);

            if (index is null)
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await WriteHtmlAsync(ctx, index).ConfigureAwait(false);
        });
    }

    private static async Task<string?> ReadIndexAsync(string root, CancellationToken ct)
    {
        string path = Path.Combine(root, IndexFile);

        // Read per request rather than cached: the file only changes on a redeploy, and a stale
        // index served until the next restart is a confusing bug to chase for a saving nobody would
        // measure on a route only crawlers and first loads hit.
        return File.Exists(path)
            ? await File.ReadAllTextAsync(path, ct).ConfigureAwait(false)
            : null;
    }

    private static Task WriteHtmlAsync(HttpContext ctx, string html)
    {
        ctx.Response.ContentType = "text/html; charset=utf-8";

        // index.html names hashed asset files, so caching it would pin a browser to a deployment
        // that no longer exists.
        ctx.Response.Headers.CacheControl = "no-cache";

        return ctx.Response.WriteAsync(html, ctx.RequestAborted);
    }
}
