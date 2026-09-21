using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Vortex.Dashboard.API.Infrastructure;

/// <summary>
/// The gamedata files the client downloads, read and written from one place.
/// </summary>
/// <remarks>
/// These files are not configuration: they are what the game client fetches at boot. A malformed one
/// is not a failed save, it is a hotel that will not start. So every write here goes
/// backup → temp → <b>parse the temp back</b> → atomic replace, and the real file is never touched
/// until the bytes about to replace it have been proven to parse.
/// <para>
/// The asset host serves them through <c>hashes.php</c>, which publishes <c>md5_file()</c> of each
/// one; the client requests <c>&lt;url&gt;/&lt;hash&gt;</c>. Cache invalidation is therefore free —
/// changing a file changes its hash — but only for files <c>hashes.php</c> knows about.
/// </para>
/// <para>
/// furnidata is 38 MB and 55 836 entries, so it is parsed once and kept. Search and paging happen
/// here, against the parsed tree; the browser never receives the file.
/// </para>
/// </remarks>
internal sealed class GamedataDocumentStore(
    DashboardAssetUrls assets,
    ILogger<GamedataDocumentStore> logger
)
{
    /// <summary>
    /// The files an operator may edit, and nothing else. A closed map, keyed by the token the HTTP
    /// layer accepts: the value comes off a URL, and a file name built from it by concatenation is
    /// the traversal bug rather than the guard against it.
    /// </summary>
    public static readonly FrozenDictionary<string, string> Files = new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["variables"] = "external_variables.json",
        ["texts"] = "external_flash_texts.json",
        ["catalog-texts"] = "external_catalog_text.json",
        ["badges-texts"] = "external_badges_text.json",
        ["furnidata"] = "furnidata_json.json",
        ["productdata"] = "productdata_json.json",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The files that exist once per language, under <c>gamedata/&lt;code&gt;/</c>.
    /// </summary>
    /// <remarks>
    /// The split ones are not a second layer the client falls back to: every one of them is loaded
    /// into the same key store, so a key defined in two of them resolves to whichever loaded last
    /// and nothing says so. They are a way to edit texts per domain, not a way to override.
    /// </remarks>
    public static bool IsLocalised(string file) => LocalisedFiles.Contains(file);

    private static readonly FrozenSet<string> LocalisedFiles = new[]
    {
        "texts",
        "catalog-texts",
        "badges-texts",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, CachedDocument> _cache = new(
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>Whether the asset root is configured at all. Without it nothing here can work.</summary>
    public bool Available => !string.IsNullOrWhiteSpace(assets.LocalRoot);

    /// <summary>
    /// The absolute path of one gamedata file, for the default language or a specific one.
    /// </summary>
    /// <remarks>
    /// <paramref name="file"/> is looked up in <see cref="Files"/> and <paramref name="language"/> is
    /// checked to be a bare code, so neither can carry a path segment. The default language is the
    /// file at the root of <c>gamedata/</c> — that is the one <c>external.texts.txt</c> points at,
    /// and the client's fallback.
    /// </remarks>
    public bool TryResolve(string file, string? language, out string path)
    {
        path = string.Empty;

        if (!Available || !Files.TryGetValue(file, out string? name))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(language))
        {
            if (!IsLanguageCode(language) || !IsLocalised(file))
            {
                return false;
            }

            path = Path.Combine(assets.LocalRoot, "gamedata", language, name);
            return true;
        }

        path = Path.Combine(assets.LocalRoot, "gamedata", name);
        return true;
    }

    /// <summary>
    /// A language code as it may appear in a path: letters, digits and dashes, nothing else. Rejects
    /// the separators and the dots that would let a code climb out of <c>gamedata/</c>.
    /// </summary>
    public static bool IsLanguageCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 16)
        {
            return false;
        }

        foreach (char c in code)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The parsed document, from cache when the file has not changed underneath us.
    /// </summary>
    /// <remarks>
    /// The cache is keyed by path and validated against the file's write time, so an edit made
    /// outside the dashboard — by hand, or by another process — is picked up rather than served
    /// stale from a previous read.
    /// </remarks>
    public JsonNode? Read(string file, string? language, out DateTime modifiedUtc)
    {
        modifiedUtc = default;

        if (!TryResolve(file, language, out string path) || !File.Exists(path))
        {
            return null;
        }

        DateTime stamp = File.GetLastWriteTimeUtc(path);
        modifiedUtc = stamp;

        lock (_gate)
        {
            if (_cache.TryGetValue(path, out CachedDocument cached) && cached.ModifiedUtc == stamp)
            {
                return cached.Root;
            }
        }

        JsonNode? root;

        try
        {
            root = JsonNode.Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            // A file already broken on disk. Reported rather than thrown: the operator opening the
            // page needs to be told which file, not handed a 500.
            logger.LogError(ex, "Gamedata file {Path} does not parse", path);
            return null;
        }

        lock (_gate)
        {
            _cache[path] = new CachedDocument(root, stamp);
        }

        return root;
    }

    /// <summary>
    /// Replaces one gamedata file with <paramref name="mutate"/>'s result, or refuses and leaves it
    /// exactly as it was.
    /// </summary>
    /// <param name="expectedModifiedUtc">
    /// What the caller believed the file's write time to be. A mismatch means somebody else wrote it
    /// since the page was loaded, and finishing would silently drop their edit — several people
    /// touch a hotel, and a lost write in a file of 55 836 entries is invisible.
    /// Pass <see langword="null"/> only for a write that is creating the file.
    /// </param>
    public GamedataWriteResult Write(
        string file,
        string? language,
        DateTime? expectedModifiedUtc,
        Func<JsonNode, JsonNode?> mutate
    )
    {
        if (!TryResolve(file, language, out string path))
        {
            return GamedataWriteResult.Fail("unknown_file");
        }

        lock (_gate)
        {
            if (!File.Exists(path))
            {
                return GamedataWriteResult.Fail("missing_file");
            }

            DateTime stamp = File.GetLastWriteTimeUtc(path);

            if (expectedModifiedUtc is { } expected && expected != stamp)
            {
                return GamedataWriteResult.Fail("stale_file");
            }

            JsonNode? root;

            try
            {
                root = JsonNode.Parse(File.ReadAllText(path));
            }
            catch (JsonException)
            {
                return GamedataWriteResult.Fail("unparseable_file");
            }

            if (root is null)
            {
                return GamedataWriteResult.Fail("unparseable_file");
            }

            JsonNode? updated = mutate(root);

            if (updated is null)
            {
                return GamedataWriteResult.Fail("no_change");
            }

            return Replace(path, updated);
        }
    }

    /// <summary>
    /// Writes <paramref name="content"/> over <paramref name="path"/> the only way this class ever
    /// does: a dated copy kept, the new bytes written beside the file and parsed back, and only then
    /// moved into place.
    /// </summary>
    /// <remarks>
    /// Parsing the temp file rather than trusting the serializer is the point. Serialization can
    /// succeed on a tree that no longer means what the client expects, the disk can be full, and a
    /// half-written 38 MB furnidata is indistinguishable from a whole one until something reads it.
    /// What is proven here is that the bytes now on disk parse.
    /// </remarks>
    private GamedataWriteResult Replace(string path, JsonNode content)
    {
        string temp = path + ".tmp";

        try
        {
            string backup = Backup(path);

            using (FileStream stream = File.Create(temp))
            using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
            {
                content.WriteTo(writer);
            }

            try
            {
                using FileStream verify = File.OpenRead(temp);
                using JsonDocument _ = JsonDocument.Parse(verify);
            }
            catch (JsonException)
            {
                File.Delete(temp);
                return GamedataWriteResult.Fail("would_write_invalid_json");
            }

            File.Move(temp, path, overwrite: true);
            _cache.Remove(path);

            logger.LogInformation(
                "Gamedata {Path} written, previous copy kept at {Backup}",
                path,
                backup
            );

            return GamedataWriteResult.Ok(File.GetLastWriteTimeUtc(path), backup);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Gamedata write to {Path} failed", path);
            TryDelete(temp);
            return GamedataWriteResult.Fail("write_failed");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Gamedata write to {Path} refused", path);
            TryDelete(temp);
            return GamedataWriteResult.Fail("write_failed");
        }
    }

    /// <summary>
    /// Keeps a dated copy of the file about to be replaced.
    /// </summary>
    /// <remarks>
    /// Deliberately a sibling of the asset root's <c>gamedata</c>, never inside it:
    /// <c>DashboardEndpoints.HotelAssets</c> serves that folder to anyone, and backups placed under
    /// it would be downloadable by the same anonymous route that serves furni icons.
    /// </remarks>
    private string Backup(string path)
    {
        string root = Path.Combine(assets.LocalRoot, "gamedata_backups");
        Directory.CreateDirectory(root);

        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        string backup = Path.Combine(
            root,
            $"{Path.GetFileNameWithoutExtension(path)}.{stamp}.json"
        );

        File.Copy(path, backup, overwrite: false);

        return backup;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // The temp file is already the failure path; losing it changes nothing for the caller.
        }
    }

    private readonly record struct CachedDocument(JsonNode? Root, DateTime ModifiedUtc);
}

/// <summary>The outcome of a gamedata write, and where the previous copy went.</summary>
internal readonly record struct GamedataWriteResult(
    bool Success,
    string Error,
    DateTime ModifiedUtc,
    string Backup
)
{
    public static GamedataWriteResult Ok(DateTime modifiedUtc, string backup) =>
        new(true, string.Empty, modifiedUtc, backup);

    public static GamedataWriteResult Fail(string error) =>
        new(false, error, default, string.Empty);
}
