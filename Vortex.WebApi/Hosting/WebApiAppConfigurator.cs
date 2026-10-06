using System;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Vortex.WebApi.Configuration;

namespace Vortex.WebApi.Hosting;

/// <summary>
///     Shared service registration and middleware pipeline for the web API's isolated ASP.NET Core app.
///     Both the production host (<see cref="WebApiWebHost" />) and the integration tests build their
///     <see cref="WebApplication" /> through these helpers, so the security surface — CORS allow-list,
///     per-endpoint rate limits, HTTPS/HSTS and hardened headers — is exercised identically in both.
/// </summary>
internal static class WebApiAppConfigurator
{
    public const string CorsPolicyName = "webapi";

    public static void ConfigureServices(IServiceCollection services, WebApiConfig config)
    {
        AddCors(services, config);
        AddRateLimiting(services, config);
        AddHttpsRedirection(services, config);
        AddSwagger(services);
        MetricsScrapingEndpoint.ConfigureServices(services, config);
    }

    public static void ConfigurePipeline(WebApplication app, WebApiConfig config)
    {
        app.Use(
            async (ctx, next) =>
            {
                ApplySecurityHeaders(ctx.Response);

                await next().ConfigureAwait(false);
            }
        );

        if (config.HttpsEnabled)
        {
            if (config.HstsEnabled)
            {
                app.UseHsts();
            }

            app.UseHttpsRedirection();
        }

        // Opt-in — see WebApiConfig.SwaggerEnabled for why a deployed hotel should not hand its
        // whole route surface to whoever finds the port. The generated document the website builds
        // its types from does not come from here; a test writes it from this same pipeline offline.
        if (config.SwaggerEnabled)
        {
            app.UseSwagger();
            app.UseSwaggerUI(ui =>
            {
                ui.SwaggerEndpoint("/swagger/v1/swagger.json", "Vortex Web API v1");
                ui.DocumentTitle = "Vortex Web API";
            });
        }

        app.UseCors(CorsPolicyName);
        app.UseRateLimiter();

        // After the shared hardening above (security headers, HTTPS redirection, CORS) so a scrape is
        // treated exactly like every other request on this listener, plus its own guard.
        MetricsScrapingEndpoint.ConfigurePipeline(app, config);
    }

    private static void AddCors(IServiceCollection services, WebApiConfig config)
    {
        services.AddCors(options =>
        {
            options.AddPolicy(
                CorsPolicyName,
                policy =>
                    policy
                        .WithOrigins(config.AllowedOrigins)
                        .AllowCredentials()
                        .AllowAnyHeader()
                        .WithMethods("GET", "POST", "OPTIONS")
            );
        });
    }

    private static void AddRateLimiting(IServiceCollection services, WebApiConfig config)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            AddFixedWindowPolicy(
                options,
                WebApiEndpoints.LoginRateLimitPolicy,
                config.LoginRateLimit
            );
            AddFixedWindowPolicy(
                options,
                WebApiEndpoints.RegistrationRateLimitPolicy,
                config.RegistrationRateLimit
            );
            AddFixedWindowPolicy(
                options,
                WebApiEndpoints.SsoTokenRateLimitPolicy,
                config.SsoTokenRateLimit
            );
            AddFixedWindowPolicy(
                options,
                WebApiEndpoints.ReportRateLimitPolicy,
                config.ReportRateLimit
            );
            AddFixedWindowPolicy(
                options,
                WebApiEndpoints.ShopOrderRateLimitPolicy,
                config.ShopOrderRateLimit
            );
            AddFixedWindowPolicy(
                options,
                WebApiEndpoints.FeedPostRateLimitPolicy,
                config.FeedPostRateLimit
            );
        });
    }

    private static void AddFixedWindowPolicy(
        RateLimiterOptions options,
        string policyName,
        WebApiConfig.RateLimitOptions limit
    )
    {
        options.AddPolicy(
            policyName,
            httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limit.PermitLimit,
                        Window = TimeSpan.FromSeconds(limit.WindowSeconds),
                        QueueLimit = limit.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true,
                    }
                )
        );
    }

    private static void AddHttpsRedirection(IServiceCollection services, WebApiConfig config)
    {
        if (!config.HttpsEnabled)
        {
            return;
        }

        services.AddHttpsRedirection(options => options.HttpsPort = config.HttpsPort);
    }

    private static void AddSwagger(IServiceCollection services)
    {
        services.Configure<RouteOptions>(routing =>
            routing.SetParameterPolicy<RegexInlineRouteConstraint>("regex")
        );

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(swagger =>
        {
            swagger.SwaggerDoc(
                "v1",
                new OpenApiInfo
                {
                    Title = "Vortex Web API",
                    Version = "v1",
                    Description =
                        "Client-facing onboarding API for the Vortex emulator: login, registration, "
                        + "avatar management and SSO ticket issuance. Sensitive endpoints are rate "
                        + "limited and the session is carried by the habbo-web-session cookie.",
                }
            );

            // Without this, Swashbuckle ignores C# nullability entirely: every property of every
            // record comes out optional and nullable, and the website's generated types then say a
            // player MIGHT not have a name. Every page would answer that with optional chaining and
            // a fallback, which is the same untyped guessing the generation was meant to end.
            // The projects are `<Nullable>enable</Nullable>`, so `string Name` is a promise the
            // document can carry.
            swagger.SupportNonNullableReferenceTypes();

            // …and the other half of it. `SupportNonNullableReferenceTypes` only says a property is
            // not nullable; it does not put it in `required`, and a generator reads a property that
            // is not required as OPTIONAL. Without this the site's types say every field MIGHT be
            // absent, which costs an `?.` and a fallback at every read of every page.
            swagger.NonNullableReferenceTypesAsRequired();
        });
    }

    private static void ApplySecurityHeaders(HttpResponse response)
    {
        IHeaderDictionary headers = response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
    }
}
