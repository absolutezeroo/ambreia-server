using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Vortex.Hosting.Tests;

/// <summary>
/// The host must name the shared frameworks its plugins run on.
/// </summary>
/// <remarks>
/// The dashboard and the web API are byte-loaded plugins; every Microsoft.AspNetCore.* assembly
/// they need is resolved by the default context, which holds only the frameworks listed in
/// Vortex.Main.runtimeconfig.json. When the web API stopped being a project reference, nothing in
/// the host's graph named Microsoft.AspNetCore.App any more, and production started with
/// "Loaded 0 plugins" and no port 8080. No other test sees it: every test host carries its own
/// FrameworkReference, this one included, so the plugins load fine everywhere but in the real host.
/// </remarks>
public sealed class PluginHostFrameworkTests
{
    [Fact]
    public void The_host_runtimeconfig_names_AspNetCore()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Vortex.Main.runtimeconfig.json");

        File.Exists(path).Should().BeTrue("the referenced host's runtimeconfig is copied here");

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement options = doc.RootElement.GetProperty("runtimeOptions");

        // One framework is written as "framework", several as "frameworks".
        string[] frameworks = options.TryGetProperty("frameworks", out JsonElement many)
            ?
            [
                .. many.EnumerateArray()
                    .Select(f => f.GetProperty("name").GetString() ?? string.Empty),
            ]
            : [options.GetProperty("framework").GetProperty("name").GetString() ?? string.Empty];

        frameworks.Should().Contain("Microsoft.AspNetCore.App");
    }
}
