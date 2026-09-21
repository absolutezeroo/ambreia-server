using System.Collections.Generic;
using System.Text.Json.Nodes;
using FluentAssertions;
using Vortex.Dashboard.API.Infrastructure;
using Xunit;

namespace Vortex.Dashboard.Tests.Hosting;

/// <summary>
/// The one invariant of the client's language registry: it is read by walking
/// <c>localization.1</c>, <c>localization.2</c>, … and the walk stops at the first missing index.
/// </summary>
/// <remarks>
/// A gap does not hide one language, it hides every language after it — and nothing reports that.
/// The block is therefore rewritten whole; these tests are what say so out loud.
/// </remarks>
public sealed class GamedataLanguageRegistryTests
{
    [Fact]
    public void Read_StopsAtTheFirstGap_LikeTheClientDoes()
    {
        JsonObject variables = new()
        {
            ["localization.1"] = "en",
            ["localization.1.code"] = "en",
            ["localization.1.name"] = "English",
            ["localization.1.url"] = "http://a/gamedata/en/external_flash_texts/1",
            // No localization.2 — so localization.3 is unreachable, for the client and for us.
            ["localization.3"] = "de",
            ["localization.3.code"] = "de",
        };

        GamedataLanguageRegistry.Read(variables).Should().HaveCount(1);
    }

    [Fact]
    public void Write_RenumbersFromOne_LeavingNoGap()
    {
        JsonObject variables = new()
        {
            ["localization.1"] = "en",
            ["localization.1.code"] = "en",
            ["localization.2"] = "fr",
            ["localization.2.code"] = "fr",
            ["localization.3"] = "de",
            ["localization.3.code"] = "de",
        };

        // The middle one goes. Deleting its keys in place would strand the third behind a hole.
        GamedataLanguageRegistry.Write(
            variables,
            [
                new GamedataLanguage(
                    "en",
                    "en",
                    "English",
                    "http://a/gamedata/external_flash_texts/1"
                ),
                new GamedataLanguage(
                    "de",
                    "de",
                    "Deutsch",
                    "http://a/gamedata/de/external_flash_texts/1"
                ),
            ]
        );

        variables["localization.1"]!.GetValue<string>().Should().Be("en");
        variables["localization.2"]!.GetValue<string>().Should().Be("de");
        variables.ContainsKey("localization.3").Should().BeFalse();
        variables.ContainsKey("localization.3.code").Should().BeFalse();

        GamedataLanguageRegistry.Read(variables).Should().HaveCount(2);
    }

    [Fact]
    public void Write_LeavesUnrelatedKeysAlone()
    {
        JsonObject variables = new()
        {
            ["imager.prefix"] = "http://a/imager",
            // Not part of the block: what follows the prefix is not a digit.
            ["localization.mode"] = "strict",
            ["localization.1"] = "en",
        };

        GamedataLanguageRegistry.Write(variables, []);

        variables.ContainsKey("imager.prefix").Should().BeTrue();
        variables.ContainsKey("localization.mode").Should().BeTrue();
        variables.ContainsKey("localization.1").Should().BeFalse();
    }

    [Fact]
    public void TheIdIsTheCode_BecauseThatIsWhatTheChatCommandTakes()
    {
        // `:lang <id>` passes localization.<k>'s own value to activateLocalizationDefinition. If the
        // id were an opaque number, a player would have no name to type.
        JsonObject variables = [];

        GamedataLanguageRegistry.Write(
            variables,
            [
                new GamedataLanguage(
                    "fr",
                    "fr",
                    "Français",
                    "http://a/gamedata/fr/external_flash_texts/1"
                ),
            ]
        );

        variables["localization.1"]!.GetValue<string>().Should().Be("fr");
    }

    [Fact]
    public void BuildTextsUrl_InsertsTheCodeIntoWhateverPrefixTheHotelUses()
    {
        JsonObject variables = new()
        {
            ["external.texts.txt"] = "${url.prefix}/gamedata/external_flash_texts/1",
        };

        GamedataLanguageRegistry
            .BuildTextsUrl(variables, "fr")
            .Should()
            .Be("${url.prefix}/gamedata/fr/hashes.json");
    }

    /// <remarks>
    /// The client hands this URL to <c>loadLocalizationFromURL()</c>, which parses it as a manifest.
    /// Pointing it at a texts file parses as a manifest with no entries: every key then resolves to
    /// the empty string and nothing is logged. The index is also the only place the split
    /// <c>external_text_*</c> files are named, so a language whose URL skips it has no split texts
    /// either.
    /// </remarks>
    [Fact]
    public void BuildTextsUrl_PointsAtTheLanguageManifestAndNotAtATextsFile()
    {
        JsonObject variables = new()
        {
            ["external.texts.txt"] = "${url.prefix}/gamedata/external_flash_texts/1",
        };

        string url = GamedataLanguageRegistry.BuildTextsUrl(variables, "de");

        url.Should().EndWith("/hashes.json");
        url.Should().NotContain("external_flash_texts");
    }

    [Fact]
    public void BuildTextsUrl_FallsBackWhenTheHotelHasNoBaseTextsKey()
    {
        GamedataLanguageRegistry
            .BuildTextsUrl([], "de")
            .Should()
            .Be("${url.prefix}/gamedata/de/hashes.json");
    }
}
