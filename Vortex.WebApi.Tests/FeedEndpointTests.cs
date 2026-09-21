using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>The feed's routes, through the real routing pipeline.</summary>
public sealed class FeedEndpointTests
{
    [Fact]
    public async Task Feed_IsAnonymous()
    {
        await using WebApiTestFactory factory = new();

        using HttpResponseMessage response = await factory.Client.GetAsync("/api/public/feed");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement page = JsonDocument
            .Parse(await response.Content.ReadAsStringAsync())
            .RootElement;

        page.GetProperty("total").GetInt32().Should().Be(0);
        page.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Post_AnswersNotFoundWithACode()
    {
        await using WebApiTestFactory factory = new();

        using HttpResponseMessage response = await factory.Client.GetAsync("/api/public/feed/404");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        JsonDocument
            .Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("error")
            .GetString()
            .Should()
            .Be("feed_post_not_found");
    }
}
