using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Vortex.Dashboard.API.Infrastructure;

/// <summary>
/// The client's own language registry, as it lives inside <c>external_variables.json</c>.
/// </summary>
/// <remarks>
/// <c>HabboLocalizationManager.configureLocalizationLocations()</c> walks <c>localization.1</c>,
/// <c>localization.2</c>, … reading <c>.code</c>, <c>.name</c> and <c>.url</c> for each, and registers
/// one definition per entry. A player switches with the chat command <c>:lang &lt;id&gt;</c>, whose
/// argument is the <b>id</b> — the value of <c>localization.&lt;k&gt;</c> itself, not the code. So the
/// id is written as the code: otherwise the feature ships and no one can name a language to reach it.
/// <para>
/// The loop stops at the first missing index. A gap does not skip one language, it hides every
/// language after it — which is why the block is always rewritten contiguously from 1 rather than
/// patched in place.
/// </para>
/// <para>
/// Verified against the target client (WIN63-2026): <c>configureLocalizationLocations</c>,
/// <c>registerLocalizationDefinition</c> and <c>localization.1</c> are all present. Two mechanisms of
/// the older source are NOT, and nothing here may rely on them: <c>external.override.texts.txt</c>
/// (the second, overriding layer) and <c>language_selection.enabled</c>, a dead key in the current
/// dump.
/// </para>
/// </remarks>
internal static class GamedataLanguageRegistry
{
    private const string Prefix = "localization.";

    /// <summary>
    /// The property holding the URL of the default language's texts — the file at the root of
    /// <c>gamedata/</c>, loaded before any switch and used as the fallback for missing keys.
    /// </summary>
    public const string BaseTextsKey = "external.texts.txt";

    /// <summary>The languages currently declared to the client, in the order it reads them.</summary>
    public static IReadOnlyList<GamedataLanguage> Read(JsonObject variables)
    {
        List<GamedataLanguage> languages = [];

        for (int index = 1; ; index++)
        {
            string id = Prefix + index.ToString();

            if (!variables.TryGetPropertyValue(id, out JsonNode? value) || value is null)
            {
                // The client stops here too. Anything further down the file is unreachable.
                break;
            }

            languages.Add(
                new GamedataLanguage(
                    value.GetValue<string>(),
                    ReadString(variables, id + ".code"),
                    ReadString(variables, id + ".name"),
                    ReadString(variables, id + ".url")
                )
            );
        }

        return languages;
    }

    /// <summary>
    /// Rewrites the whole block so it declares exactly <paramref name="languages"/>, numbered 1..N
    /// with no gap, and drops every <c>localization.*</c> key that is no longer part of it.
    /// </summary>
    /// <remarks>
    /// Rewriting rather than patching is what guarantees contiguity. Removing the middle language of
    /// three by deleting its four keys would leave <c>localization.3</c> stranded behind a hole, and
    /// the client would silently stop after the first.
    /// </remarks>
    public static void Write(JsonObject variables, IReadOnlyList<GamedataLanguage> languages)
    {
        foreach (string key in variables.Select(pair => pair.Key).Where(IsRegistryKey).ToArray())
        {
            variables.Remove(key);
        }

        for (int index = 0; index < languages.Count; index++)
        {
            GamedataLanguage language = languages[index];
            string id = Prefix + (index + 1).ToString();

            variables[id] = language.Id;
            variables[id + ".code"] = language.Code;
            variables[id + ".name"] = language.Name;
            variables[id + ".url"] = language.Url;
        }
    }

    /// <summary>
    /// The URL the client must fetch a language's texts from, built off whatever prefix the rest of
    /// the file already uses so a hotel that moves its asset host does not have to fix these by hand.
    /// </summary>
    /// <remarks>
    /// The path mirrors what <c>external.texts.txt</c> points at, with the language code inserted as
    /// a folder — which is what the empty <c>gamedata/en/</c> and <c>gamedata/fr/</c> directories in
    /// the asset pack were always for.
    /// <para>
    /// It ends at the language's <b>hashes index</b>, not at a texts file. The client hands this URL
    /// to <c>loadLocalizationFromURL()</c>, which parses it as a manifest and follows its
    /// <c>external_texts</c> entry — pointing it straight at a texts file yields a manifest parse
    /// with no entries, every key resolving to the empty string, and no error anywhere. The index is
    /// also what carries the <c>external_text_*</c> files, so the texts split by domain can only be
    /// found through it. The <c>hashes.json</c> spelling is the client's own, from
    /// <c>gamedata.hashes.url</c> in <c>common_configuration_txt.txt</c>.
    /// </para>
    /// </remarks>
    public static string BuildTextsUrl(JsonObject variables, string code)
    {
        string basis = ReadString(variables, BaseTextsKey);

        if (string.IsNullOrWhiteSpace(basis))
        {
            basis = "${url.prefix}/gamedata/external_flash_texts/1";
        }

        int marker = basis.LastIndexOf("/gamedata/", StringComparison.OrdinalIgnoreCase);

        if (marker < 0)
        {
            return basis;
        }

        string prefix = basis[..(marker + "/gamedata/".Length)];

        return prefix + code + "/hashes.json";
    }

    // "localization.1" and "localization.1.code" are the block; a hypothetical "localization.mode"
    // is somebody else's key and is left alone.
    private static bool IsRegistryKey(string key) =>
        key.Length > Prefix.Length
        && key.StartsWith(Prefix, StringComparison.Ordinal)
        && char.IsAsciiDigit(key[Prefix.Length]);

    private static string ReadString(JsonObject variables, string key) =>
        variables.TryGetPropertyValue(key, out JsonNode? value) && value is not null
            ? value.GetValue<string>()
            : string.Empty;
}

/// <summary>One language as the client sees it. <paramref name="Id"/> is what <c>:lang</c> takes.</summary>
internal readonly record struct GamedataLanguage(string Id, string Code, string Name, string Url);
