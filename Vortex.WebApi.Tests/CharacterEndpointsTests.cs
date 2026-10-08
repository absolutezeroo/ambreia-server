using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Rpg.Characters;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>Spec Personnage §8: classes listed, a class at creation, chosen once, read back.</summary>
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

    [Fact]
    public async Task ANamedAvatarWithoutAClass_IsABadRequest()
    {
        await using WebApiTestFactory f = new();

        HttpResponseMessage r = await PostAsync(f, "/api/user/avatars", """{ "name": "Bob" }""");

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain("avatar.class_required");
    }

    [Fact]
    public async Task AnUnknownClass_CreatesNoAvatar()
    {
        await using WebApiTestFactory f = new();

        HttpResponseMessage r = await PostAsync(
            f,
            "/api/user/avatars",
            """{ "name": "Bob", "classId": "class.paladin" }"""
        );

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain("avatar.unknown_class");
        f.Players.Created.Should().Be(0);
    }

    [Fact]
    public async Task AnAvatarWithAClass_IsCreatedWithIt()
    {
        await using WebApiTestFactory f = new();

        HttpResponseMessage r = await PostAsync(
            f,
            "/api/user/avatars",
            """{ "name": "Bob", "classId": "class.warrior" }"""
        );

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        f.Characters.Choices.Should().Equal((101L, "class.warrior"));
    }

    /// <summary>The client creates the avatar of a registration without a name, then names it:
    /// that one chooses its class later (spec Personnage §2.4).</summary>
    [Fact]
    public async Task ARegistrationAvatar_NeedsNoClassYet()
    {
        await using WebApiTestFactory f = new();

        (await PostAsync(f, "/api/user/avatars", "{}")).StatusCode.Should().Be(HttpStatusCode.OK);
        f.Characters.Choices.Should().BeEmpty();
    }

    [Fact]
    public async Task AClass_IsChosenOnce()
    {
        await using WebApiTestFactory f = new();
        string url = $"/api/user/avatars/{FakePlayerService.OwnedUniqueId}/class";

        (await PostAsync(f, url, """{ "classId": "class.warrior" }"""))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
        HttpResponseMessage again = await PostAsync(f, url, """{ "classId": "class.warrior" }""");

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.Content.ReadAsStringAsync()).Should().Contain("avatar.class_already_chosen");
    }

    [Fact]
    public async Task ChoosingAnUnknownClass_IsABadRequest()
    {
        await using WebApiTestFactory f = new();

        (
            await PostAsync(
                f,
                $"/api/user/avatars/{FakePlayerService.OwnedUniqueId}/class",
                """{ "classId": "class.paladin" }"""
            )
        )
            .StatusCode.Should()
            .Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChoosingForSomeoneElsesAvatar_IsForbidden()
    {
        await using WebApiTestFactory f = new();

        (await PostAsync(f, "/api/user/avatars/999/class", """{ "classId": "class.warrior" }"""))
            .StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ChoosingWithoutASession_IsUnauthorized()
    {
        await using WebApiTestFactory f = new();
        using StringContent body = new(
            """{ "classId": "class.warrior" }""",
            Encoding.UTF8,
            "application/json"
        );

        (
            await f.Client.PostAsync(
                $"/api/user/avatars/{FakePlayerService.OwnedUniqueId}/class",
                body
            )
        )
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TheAvatarList_CarriesClassAndLevel()
    {
        await using WebApiTestFactory f = new();
        f.Characters.ByPlayer[long.Parse(FakePlayerService.OwnedUniqueId)] = new CharacterSummary
        {
            ClassId = "class.warrior",
            SpecializationId = "spec.warrior.guardian",
            Level = 3,
            Xp = 10,
        };

        string json = await (
            await f.CreateAuthenticatedClient().GetAsync("/api/user/avatars")
        ).Content.ReadAsStringAsync();

        json.Should().Contain("\"classId\":\"class.warrior\"").And.Contain("\"level\":3");
    }

    [Fact]
    public async Task AnAvatarWithoutACharacter_IsListedWithoutAClass()
    {
        await using WebApiTestFactory f = new();

        string json = await (
            await f.CreateAuthenticatedClient().GetAsync("/api/user/avatars")
        ).Content.ReadAsStringAsync();

        json.Should().Contain("\"classId\":null").And.Contain("\"level\":null");
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
