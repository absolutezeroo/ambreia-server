using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Rpg.Characters;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>Spec Personnage §8: the classes listed, and the test XP grant. A character is created
/// and given its class on the game socket (spec F3a §4), not through these routes.</summary>
public sealed class CharacterEndpointsTests
{
    [Fact]
    public async Task TheClasses_AreListed()
    {
        await using WebApiTestFactory f = new();

        HttpResponseMessage r = await f.Client.GetAsync("/api/public/content/classes");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadAsStringAsync())
            .Should()
            .Contain("\"id\":\"class.warrior\"")
            .And.Contain("\"role\":\"tank\"");
    }

    private static async Task<HttpResponseMessage> PostAsync(
        WebApiTestFactory f,
        string url,
        string json
    )
    {
        using StringContent body = new(json, Encoding.UTF8, "application/json");

        return await f.CreateAuthenticatedClient().PostAsync(url, body);
    }

    // The test grant (plan Personnage, décision 3): XP for the live proof, behind a capability.

    [Fact]
    public async Task GrantingXp_WithoutTheCapability_IsForbidden()
    {
        await using WebApiTestFactory f = new(capabilities: []);

        (await PostAsync(f, "/api/admin/characters/100/xp", """{ "amount": 250 }"""))
            .StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GrantingXp_ReachesTheCharacter()
    {
        await using WebApiTestFactory f = new(capabilities: [Capabilities.Rpg.GrantXp]);
        f.Characters.ByPlayer[100] = new CharacterSummary
        {
            ClassId = "class.warrior",
            SpecializationId = "spec.warrior.guardian",
            Level = 1,
            Xp = 0,
        };

        HttpResponseMessage r = await PostAsync(
            f,
            "/api/admin/characters/100/xp",
            """{ "amount": 250 }"""
        );

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadAsStringAsync()).Should().Contain("granted");
        f.Characters.XpGrants.Should().Equal((100L, 250));
    }

    [Fact]
    public async Task GrantingXp_ToAnAvatarWithoutACharacter_IsNotFound()
    {
        await using WebApiTestFactory f = new(capabilities: [Capabilities.Rpg.GrantXp]);

        (await PostAsync(f, "/api/admin/characters/100/xp", """{ "amount": 250 }"""))
            .StatusCode.Should()
            .Be(HttpStatusCode.NotFound);
    }
}
