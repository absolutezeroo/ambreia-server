using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Vortex.Primitives.Permissions;
using Xunit;

namespace Vortex.Permissions.Tests;

/// <summary>
/// Guards the capability declaration itself. A capability is a string that has to appear in several
/// places at once to work end to end, and until now nothing in the build or the test suite noticed
/// when one of them was missed: the dashboard registered one authorization policy per entry of a
/// hand-copied list, so a capability declared as a constant and used by an endpoint but absent from
/// that list compiled green and then threw
/// <c>AuthorizationPolicy named '&lt;capability&gt;' was not found</c> the first time an operator
/// opened the page. The duplicate lists are gone — these tests keep the remaining single list honest.
/// </summary>
public sealed class CapabilityDeclarationTests
{
    [Fact]
    public void DashboardAll_ContainsEveryDeclaredDashboardCapability()
    {
        IReadOnlyList<string> declared = DeclaredConstants(typeof(Capabilities.Dashboard));

        Capabilities
            .Dashboard.All.Should()
            .BeEquivalentTo(
                declared,
                "every dashboard.* constant needs an authorization policy, and Capabilities.Dashboard.All is what registers them"
            );
    }

    [Fact]
    public void DashboardCapabilities_AreNamespacedAndUnique()
    {
        Capabilities.Dashboard.All.Should().OnlyHaveUniqueItems();
        Capabilities
            .Dashboard.All.Should()
            .OnlyContain(c => c.StartsWith("dashboard.", StringComparison.Ordinal));
    }

    [Fact]
    public void All_ContainsEveryDeclaredCapability()
    {
        IEnumerable<string> declared = typeof(Capabilities)
            .GetNestedTypes(BindingFlags.Public)
            .SelectMany(DeclaredConstants)
            .Concat(DeclaredConstants(typeof(Capabilities)));

        Capabilities.All.Should().BeEquivalentTo(declared);
    }

    [Fact]
    public void All_HasNoDuplicates()
    {
        Capabilities.All.Should().OnlyHaveUniqueItems();
    }

    private static IReadOnlyList<string> DeclaredConstants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f =>
                f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string)
            )
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();
}
