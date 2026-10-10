using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Observability;
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
            """{ "amount": 250, "grantKey": "ops-1" }"""
        );

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadAsStringAsync()).Should().Contain("granted");
        f.Characters.XpGrants.Should().Equal((100L, "ops-1", 250));
    }

    /// <summary>A retry is the same request: without the caller's key the server cannot tell it
    /// from a second grant, so the key is not optional.</summary>
    [Fact]
    public async Task GrantingXp_WithoutAKey_IsRejected()
    {
        await using WebApiTestFactory f = new(capabilities: [Capabilities.Rpg.GrantXp]);
        f.Characters.ByPlayer[100] = Warrior();

        (await PostAsync(f, "/api/admin/characters/100/xp", """{ "amount": 250 }"""))
            .StatusCode.Should()
            .Be(HttpStatusCode.BadRequest);
        f.Characters.XpGrants.Should().BeEmpty();
    }

    [Fact]
    public async Task GrantingXp_Replayed_AnswersDuplicate_AndGrantsOnce()
    {
        await using WebApiTestFactory f = new(capabilities: [Capabilities.Rpg.GrantXp]);
        f.Characters.ByPlayer[100] = Warrior();
        const string grant = """{ "amount": 250, "grantKey": "ops-2" }""";

        await PostAsync(f, "/api/admin/characters/100/xp", grant);
        HttpResponseMessage replay = await PostAsync(f, "/api/admin/characters/100/xp", grant);

        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadAsStringAsync()).Should().Contain("duplicate");
        f.Characters.XpGrants.Should().ContainSingle();
    }

    /// <summary>Who gave what to whom: the audit row names the operator's account, not "admin".</summary>
    [Fact]
    public async Task GrantingXp_IsAudited_WithTheActorAccount()
    {
        await using WebApiTestFactory f = new(capabilities: [Capabilities.Rpg.GrantXp]);
        f.Characters.ByPlayer[100] = Warrior();

        await PostAsync(
            f,
            "/api/admin/characters/100/xp",
            """{ "amount": 250, "grantKey": "ops-3" }"""
        );

        AuditEvent audit = f.Audit.Events.Should().ContainSingle().Which;
        audit.Action.Should().Be("admin.xp.granted");
        audit.Category.Should().Be(AuditCategory.Progression);
        audit.Result.Should().Be(AuditResult.Success);
        audit.TargetPlayerId.Should().Be(100);
        audit.Amount.Should().Be(250);
        audit.Data.Should().Contain($"\"actorAccountId\":{FakeAuthService.AccountId}");
        audit.Data.Should().Contain("\"grantKey\":\"ops-3\"");
    }

    // The item grant (spec Objets §10): the caller's key makes a replay grant nothing more.

    private const string SwordGrant = """
        { "grantKey": "admin-1", "definitionId": "item.weapon.rusted_sword", "quantity": 1 }
        """;

    [Fact]
    public async Task GrantingItems_WithoutTheCapability_IsForbidden()
    {
        await using WebApiTestFactory f = new(capabilities: []);
        f.Characters.ByPlayer[100] = Warrior();

        (await PostAsync(f, "/api/admin/characters/100/items", SwordGrant))
            .StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
        f.Characters.ItemGrants.Should().BeEmpty();
    }

    [Fact]
    public async Task GrantingItems_ReachesTheCharacterWithTheCallersKey()
    {
        await using WebApiTestFactory f = new(capabilities: [Capabilities.Economy.GrantItem]);
        f.Characters.ByPlayer[100] = Warrior();

        HttpResponseMessage r = await PostAsync(f, "/api/admin/characters/100/items", SwordGrant);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadAsStringAsync())
            .Should()
            .Contain("\"result\":\"ok\"")
            .And.Contain("\"toBag\":1");
        f.Characters.ItemGrants.Should().Equal((100L, "admin-1", "item.weapon.rusted_sword", 1));
    }

    [Fact]
    public async Task AReplayedGrant_AnswersDuplicateAndGrantsOnce()
    {
        await using WebApiTestFactory f = new(capabilities: [Capabilities.Economy.GrantItem]);
        f.Characters.ByPlayer[100] = Warrior();
        await PostAsync(f, "/api/admin/characters/100/items", SwordGrant);

        HttpResponseMessage again = await PostAsync(
            f,
            "/api/admin/characters/100/items",
            SwordGrant
        );

        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await again.Content.ReadAsStringAsync())
            .Should()
            .Contain("\"result\":\"duplicate_grant\"");
        f.Characters.ItemGrants.Should().ContainSingle();
    }

    [Theory]
    [InlineData(
        """{ "grantKey": "", "definitionId": "item.weapon.rusted_sword", "quantity": 1 }"""
    )]
    [InlineData("""{ "definitionId": "item.weapon.rusted_sword", "quantity": 1 }""")]
    [InlineData("""{ "grantKey": "k", "definitionId": "", "quantity": 1 }""")]
    [InlineData(
        """{ "grantKey": "k", "definitionId": "item.weapon.rusted_sword", "quantity": 0 }"""
    )]
    public async Task AMalformedGrant_IsABadRequest(string body)
    {
        await using WebApiTestFactory f = new(capabilities: [Capabilities.Economy.GrantItem]);
        f.Characters.ByPlayer[100] = Warrior();

        (await PostAsync(f, "/api/admin/characters/100/items", body))
            .StatusCode.Should()
            .Be(HttpStatusCode.BadRequest);
        f.Characters.ItemGrants.Should().BeEmpty();
    }

    [Fact]
    public async Task AGrantKeyLongerThanTheColumn_IsABadRequest()
    {
        await using WebApiTestFactory f = new(capabilities: [Capabilities.Economy.GrantItem]);
        f.Characters.ByPlayer[100] = Warrior();
        string body = $$"""
            { "grantKey": "{{new string(
                'k',
                129
            )}}", "definitionId": "item.weapon.rusted_sword", "quantity": 1 }
            """;

        (await PostAsync(f, "/api/admin/characters/100/items", body))
            .StatusCode.Should()
            .Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AGrantToAnAvatarWithoutACharacter_IsNotFound()
    {
        await using WebApiTestFactory f = new(capabilities: [Capabilities.Economy.GrantItem]);

        (await PostAsync(f, "/api/admin/characters/100/items", SwordGrant))
            .StatusCode.Should()
            .Be(HttpStatusCode.NotFound);
    }

    private static CharacterSummary Warrior() =>
        new()
        {
            ClassId = "class.warrior",
            SpecializationId = "spec.warrior.guardian",
            Level = 1,
            Xp = 0,
        };

    [Fact]
    public async Task GrantingXp_ToAnAvatarWithoutACharacter_IsNotFound()
    {
        await using WebApiTestFactory f = new(capabilities: [Capabilities.Rpg.GrantXp]);

        (
            await PostAsync(
                f,
                "/api/admin/characters/100/xp",
                """{ "amount": 250, "grantKey": "ops-9" }"""
            )
        )
            .StatusCode.Should()
            .Be(HttpStatusCode.NotFound);
    }
}
