using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Vortex.Furniture.Assets;

/// <summary>
/// A bundle's manifest: which names exist, and where each one is on the sheet.
/// </summary>
/// <remarks>
/// <para>
/// Three tables matter, and a name is looked up through all three in order:
/// </para>
/// <list type="number">
/// <item><c>aliases</c> — <c>{ "link": "other_asset", "flipH": true }</c>. One name standing in for
/// another, usually a mirrored direction.</item>
/// <item><c>assets</c> — <c>{ "source": "other_asset", "flipH": true }</c>. The same idea by a
/// different route: an entry with a <c>source</c> borrows that asset's pixels.</item>
/// <item><c>spritesheet.frames</c> — the crop rectangle, keyed by the name the chain ends on.</item>
/// </list>
/// <para>
/// Both indirections can chain, and a malformed bundle can make them chain into a loop, so the walk
/// is bounded. Flips accumulate by exclusive-or along the way: an asset that mirrors a mirror is the
/// original, and the format says so by expecting exactly that arithmetic.
/// </para>
/// <para>
/// Nothing here throws. The manifest is JSON from a file fetched over HTTP, and every branch that
/// cannot answer answers false — a name that will not resolve costs one sprite in one photo.
/// </para>
/// </remarks>
public sealed class NitroAssetManifest
{
    /// <summary>An alias pointing at an alias is normal; ten deep is a broken bundle.</summary>
    private const int MaxIndirections = 10;

    private readonly Dictionary<string, Link> _links;
    private readonly Dictionary<string, NitroSpriteFrame> _frames;

    private NitroAssetManifest(
        Dictionary<string, Link> links,
        Dictionary<string, NitroSpriteFrame> frames
    )
    {
        _links = links;
        _frames = frames;
    }

    /// <summary>How many crop rectangles the sheet declares.</summary>
    public int FrameCount => _frames.Count;

    public static bool TryRead(ReadOnlySpan<byte> manifest, out NitroAssetManifest? read)
    {
        read = null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(manifest.ToArray());

            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            Dictionary<string, Link> links = new(StringComparer.Ordinal);

            ReadLinks(root, "aliases", "link", links);
            ReadLinks(root, "assets", "source", links);

            read = new NitroAssetManifest(links, ReadFrames(root));

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>The crop for <paramref name="name"/>, following aliases and sources to reach it.</summary>
    public bool TryResolve(string name, out NitroSpriteFrame frame)
    {
        frame = default;

        string current = name;
        bool flipH = false;
        bool flipV = false;

        for (int hop = 0; hop <= MaxIndirections; hop++)
        {
            if (_frames.TryGetValue(current, out NitroSpriteFrame found))
            {
                frame = found with { FlipH = flipH, FlipV = flipV };
                return true;
            }

            if (!_links.TryGetValue(current, out Link link) || link.Target == current)
            {
                return false;
            }

            flipH ^= link.FlipH;
            flipV ^= link.FlipV;
            current = link.Target;
        }

        // Ten hops without landing on a frame is a cycle, and following it further is how a photo
        // becomes a hang rather than a missing sprite.
        return false;
    }

    private static void ReadLinks(
        JsonElement root,
        string section,
        string targetKey,
        Dictionary<string, Link> links
    )
    {
        if (
            !root.TryGetProperty(section, out JsonElement element)
            || element.ValueKind != JsonValueKind.Object
        )
        {
            return;
        }

        foreach (JsonProperty entry in element.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (
                !entry.Value.TryGetProperty(targetKey, out JsonElement target)
                || target.ValueKind != JsonValueKind.String
            )
            {
                // An `assets` entry with no `source` is the ordinary case: it names itself, and its
                // frame is under its own name.
                continue;
            }

            string? name = target.GetString();

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            // aliases is read first and assets second, so an asset's own source wins over an alias
            // of the same name -- the client resolves an asset before it looks at the alias table.
            links[entry.Name] = new Link(
                name,
                Flag(entry.Value, "flipH"),
                Flag(entry.Value, "flipV")
            );
        }
    }

    private static Dictionary<string, NitroSpriteFrame> ReadFrames(JsonElement root)
    {
        Dictionary<string, NitroSpriteFrame> frames = new(StringComparer.Ordinal);

        if (
            !root.TryGetProperty("spritesheet", out JsonElement spritesheet)
            || spritesheet.ValueKind != JsonValueKind.Object
            || !spritesheet.TryGetProperty("frames", out JsonElement element)
            || element.ValueKind != JsonValueKind.Object
        )
        {
            return frames;
        }

        foreach (JsonProperty entry in element.EnumerateObject())
        {
            if (
                entry.Value.ValueKind != JsonValueKind.Object
                || !entry.Value.TryGetProperty("frame", out JsonElement rect)
                || rect.ValueKind != JsonValueKind.Object
            )
            {
                continue;
            }

            int width = Number(rect, "w");
            int height = Number(rect, "h");

            // A zero-sized frame is in the packs -- an empty animation step -- and cropping to it
            // would be an image nobody can allocate.
            if (width <= 0 || height <= 0)
            {
                continue;
            }

            frames[entry.Name] = new NitroSpriteFrame(
                Number(rect, "x"),
                Number(rect, "y"),
                width,
                height,
                FlipH: false,
                FlipV: false
            );
        }

        return frames;
    }

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.True;

    /// <summary>
    /// A coordinate, whether the packer wrote it as a number or as a string. The two clients already
    /// disagree about that on the camera wire, so the assumption is not worth making twice.
    /// </summary>
    private static int Number(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out int number) ? number : 0,
            JsonValueKind.String => int.TryParse(value.GetString(), out int parsed) ? parsed : 0,
            _ => 0,
        };
    }

    private readonly record struct Link(string Target, bool FlipH, bool FlipV);
}
