using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Vortex.Primitives.Permissions;
using Xunit;

namespace Vortex.Permissions.Tests;

/// <summary>
/// Guards the capability declaration itself. A capability is a constant that also has to appear in
/// its <c>All</c> list, and nothing else in the build notices when one is missed: a capability left
/// out of the list compiles green and is simply unknown to whatever enumerates it. These tests keep
/// that single list honest.
/// </summary>
public sealed class CapabilityDeclarationTests
{
    [Fact]
    public void ServerAll_ContainsEveryDeclaredServerCapability()
    {
        IReadOnlyList<string> declared = DeclaredConstants(typeof(Capabilities.Server));

        Capabilities.Server.All.Should().BeEquivalentTo(declared);
        Capabilities
            .Server.All.Should()
            .OnlyContain(c => c.StartsWith("server.", StringComparison.Ordinal));
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
