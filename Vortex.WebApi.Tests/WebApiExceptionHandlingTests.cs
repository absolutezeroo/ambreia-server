using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// An unhandled exception in a route is a bug to log, not a page to serve: the answer is a plain
/// 500 problem document, the same in every environment, and never the stack.
/// </summary>
public sealed class WebApiExceptionHandlingTests
{
    [Fact]
    public async Task AnUnhandledException_Answers500_WithoutTheStack()
    {
        await using WebApiTestFactory factory = new(mapExtra: app =>
            app.MapGet("/api/test/boom", Boom)
        );

        HttpResponseMessage response = await factory.Client.GetAsync("/api/test/boom");
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        body.Should().NotContain("secret-detail").And.NotContain("   at ");
    }

    private static string Boom() =>
        throw new InvalidOperationException("secret-detail-the-client-must-not-see");
}
