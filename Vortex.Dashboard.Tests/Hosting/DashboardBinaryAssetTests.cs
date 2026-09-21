using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Vortex.Dashboard.Tests.Hosting;

/// <summary>
/// A font or an image served from this assembly must be the bytes that went into it.
/// </summary>
/// <remarks>
/// It was not. The byte reader decoded every resource as UTF-8 text and re-encoded it, which
/// round-trips for HTML, CSS and JavaScript and destroys everything else: a byte that is not valid
/// UTF-8 comes back as U+FFFD. Every font the dashboard shipped arrived corrupt, the browser fell
/// back to a system typeface, and nothing in a build log said a word — the only witness was an OTS
/// parsing error in an operator's console, months later.
/// </remarks>
public sealed class DashboardBinaryAssetTests
{
    private static readonly Assembly Dashboard =
        typeof(Vortex.Dashboard.API.Infrastructure.DashboardAssetUrls).Assembly;

    /// <summary>Every embedded resource whose bytes are not text.</summary>
    private static string[] BinaryResources() =>
        [
            .. Dashboard
                .GetManifestResourceNames()
                .Where(name =>
                    name.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".woff", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ),
        ];

    [Fact]
    public void A_binary_asset_is_served_byte_for_byte()
    {
        string[] resources = BinaryResources();

        resources
            .Should()
            .NotBeEmpty(
                "the dashboard embeds fonts and images; with none present this test proves nothing"
            );

        foreach (string resource in resources)
        {
            using Stream stream = Dashboard.GetManifestResourceStream(resource)!;
            using MemoryStream expected = new();

            stream.CopyTo(expected);

            // The file name is what the store is asked for, and the resource name is the assembly's
            // dotted path to it.
            string fileName = resource[
                (resource.LastIndexOf("Assets.", StringComparison.Ordinal) + "Assets.".Length)..
            ];

            Vortex
                .Dashboard.API.DashboardPageResources.GetBytes(fileName)
                .Should()
                .Equal(
                    expected.ToArray(),
                    "{0} must arrive as the bytes it was embedded as",
                    fileName
                );
        }
    }

    [Fact]
    public void A_woff_font_is_embedded_beside_its_woff2()
    {
        // Vite emits both and the stylesheet asks for both. Embedding only one half 404s the other,
        // which a build log cannot show and a browser console shouts about.
        string[] names = Dashboard.GetManifestResourceNames();

        if (!names.Any(n => n.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        names
            .Any(n => n.EndsWith(".woff", StringComparison.OrdinalIgnoreCase))
            .Should()
            .BeTrue("the asset pack ships .woff alongside .woff2");
    }
}
