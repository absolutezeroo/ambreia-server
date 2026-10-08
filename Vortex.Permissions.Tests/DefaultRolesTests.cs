using System.Linq;
using FluentAssertions;
using Vortex.Primitives.Permissions;
using Xunit;

namespace Vortex.Permissions.Tests;

/// <summary>What a fresh database gives each role. The administrator builds the world: without
/// the editor, XP and content rights it could moderate a hotel but not make the game.</summary>
public sealed class DefaultRolesTests
{
    [Fact]
    public void TheAdmin_CanBuildPublishAndRunTheRpg() =>
        DefaultRoles
            .All.Single(r => r.Key == "admin")
            .Capabilities.Should()
            .Contain([
                Capabilities.World.ZoneEdit,
                Capabilities.World.ZonePublish,
                Capabilities.Rpg.GrantXp,
                Capabilities.Content.Publish,
            ]);
}
