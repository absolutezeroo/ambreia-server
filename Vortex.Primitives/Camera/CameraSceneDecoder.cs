using System;
using System.Buffers;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace Vortex.Primitives.Camera;

/// <summary>
/// Turns the one zlib blob <c>RenderRoom</c> carries into a <see cref="CameraScene"/>, or refuses it.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is hostile input. The blob is built by the client, and the client's own
/// <c>isSendable()</c> returns <c>true</c> without looking at it — nothing upstream of this class has
/// bounded anything. Two rules follow and neither is negotiable: the payload is inflated
/// <em>through</em> a ceiling rather than inflated and then measured, and every name it carries is
/// classified before it reaches the renderer.
/// </para>
/// <para>
/// Pure and static on purpose. It takes bytes and limits and returns a result; it opens no socket,
/// touches no disk and asks no grain. That is what lets the ugly cases — a compression bomb, a name
/// pointing at the loopback interface, a plane with three corners — be written down as tests.
/// </para>
/// </remarks>
public static class CameraSceneDecoder
{
    /// <summary>
    /// The document the client assembles by string concatenation in
    /// <c>RenderRoomMessageComposer.compressData</c>. The separators decode to these keys.
    /// </summary>
    private const string PlanesProperty = "planes";
    private const string SpritesProperty = "sprites";
    private const string FiltersProperty = "filters";
    private const string RoomIdProperty = "roomid";
    private const string ZoomProperty = "zoom";
    private const string TimestampProperty = "timestamp";

    /// <summary>Four, always. A plane is a quad and the client writes exactly four corners.</summary>
    private const int CornerPointCount = 4;

    /// <summary>
    /// Longest asset key entertained. Real keys are short; a megabyte-long one is somebody probing.
    /// </summary>
    private const int MaxAssetNameLength = 512;

    /// <summary>
    /// Nesting past this is not a scene. <c>planes[] -> masks[] -> point{}</c> is the deepest the
    /// client ever writes, so this leaves generous room and still stops a document whose only content
    /// is ten thousand open brackets.
    /// </summary>
    private const int MaxJsonDepth = 16;

    /// <summary>Only zoom levels the camera widget can actually produce.</summary>
    private static readonly ImmutableArray<int> AllowedZoomLevels = [1, 2];

    public static CameraSceneDecodeResult Decode(
        ReadOnlySpan<byte> payload,
        int expectedRoomId,
        DateTime nowUtc,
        CameraSceneLimits limits
    )
    {
        if (payload.IsEmpty)
        {
            return CameraSceneDecodeResult.Refused(CameraRenderRefusal.Empty);
        }

        if (payload.Length > limits.MaxPayloadBytes)
        {
            return CameraSceneDecodeResult.Refused(CameraRenderRefusal.PayloadTooLarge);
        }

        byte[]? inflated = null;

        try
        {
            if (
                !TryInflate(
                    payload,
                    limits.MaxSceneBytes,
                    out inflated,
                    out int length,
                    out CameraRenderRefusal inflateRefusal
                )
            )
            {
                return CameraSceneDecodeResult.Refused(inflateRefusal);
            }

            return Parse(inflated.AsSpan(0, length), expectedRoomId, nowUtc, limits);
        }
        finally
        {
            if (inflated is not null)
            {
                ArrayPool<byte>.Shared.Return(inflated);
            }
        }
    }

    /// <summary>
    /// Inflates into a rented buffer, stopping the moment the ceiling is passed.
    /// </summary>
    /// <remarks>
    /// The ceiling is checked as bytes arrive, not once the stream has been drained: a zlib stream
    /// that expands to gigabytes costs nothing to send and everything to decompress, and a length
    /// check written after <c>CopyTo</c> runs after the damage.
    /// </remarks>
    private static bool TryInflate(
        ReadOnlySpan<byte> payload,
        int ceiling,
        out byte[]? buffer,
        out int length,
        out CameraRenderRefusal refusal
    )
    {
        // One byte past the ceiling is enough to know the ceiling was passed.
        buffer = ArrayPool<byte>.Shared.Rent(ceiling + 1);
        length = 0;
        refusal = CameraRenderRefusal.None;

        try
        {
            using MemoryStream source = new(payload.ToArray(), writable: false);
            using ZLibStream zlib = new(source, CompressionMode.Decompress);

            int total = 0;

            while (total <= ceiling)
            {
                int read = zlib.Read(buffer, total, buffer.Length - total);

                if (read == 0)
                {
                    length = total;
                    return true;
                }

                total += read;
            }

            refusal = CameraRenderRefusal.SceneTooLarge;
            return false;
        }
        catch (InvalidDataException)
        {
            // Not zlib, or truncated mid-stream. Both are "the client sent rubbish", not a fault.
            refusal = CameraRenderRefusal.CorruptPayload;
            return false;
        }
    }

    private static CameraSceneDecodeResult Parse(
        ReadOnlySpan<byte> scene,
        int expectedRoomId,
        DateTime nowUtc,
        CameraSceneLimits limits
    )
    {
        JsonDocument? document = null;

        try
        {
            Utf8JsonReader reader = new(scene, new JsonReaderOptions { MaxDepth = MaxJsonDepth });

            if (!JsonDocument.TryParseValue(ref reader, out document))
            {
                return CameraSceneDecodeResult.Refused(CameraRenderRefusal.MalformedScene);
            }

            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return CameraSceneDecodeResult.Refused(CameraRenderRefusal.MalformedScene);
            }

            if (
                !root.TryGetProperty(RoomIdProperty, out JsonElement roomIdElement)
                || !roomIdElement.TryGetInt32(out int roomId)
            )
            {
                return CameraSceneDecodeResult.Refused(CameraRenderRefusal.IncompleteScene);
            }

            // The scene names the room it was shot in, and the session says which room the player is
            // actually standing in. Disagreement means a photo of somewhere this player is not.
            if (roomId != expectedRoomId)
            {
                return CameraSceneDecodeResult.Refused(CameraRenderRefusal.WrongRoom);
            }

            if (
                !root.TryGetProperty(TimestampProperty, out JsonElement timestampElement)
                || !timestampElement.TryGetInt64(out long timestamp)
            )
            {
                return CameraSceneDecodeResult.Refused(CameraRenderRefusal.IncompleteScene);
            }

            if (
                !TryReadTakenAt(
                    timestamp,
                    nowUtc,
                    limits.TimestampSkewSeconds,
                    out DateTime takenAt
                )
            )
            {
                return CameraSceneDecodeResult.Refused(CameraRenderRefusal.Stale);
            }

            int zoom = ReadZoom(root);

            if (
                !TryReadPlanes(
                    root,
                    limits,
                    out ImmutableArray<CameraScenePlane> planes,
                    out CameraRenderRefusal planeRefusal
                )
            )
            {
                return CameraSceneDecodeResult.Refused(planeRefusal);
            }

            if (
                !TryReadSprites(
                    root,
                    limits,
                    out ImmutableArray<CameraSceneSprite> sprites,
                    out int rejectedExternal,
                    out CameraRenderRefusal spriteRefusal
                )
            )
            {
                return CameraSceneDecodeResult.Refused(spriteRefusal);
            }

            return CameraSceneDecodeResult.Ok(
                new CameraScene
                {
                    RoomId = roomId,
                    Zoom = zoom,
                    Planes = planes,
                    Sprites = sprites,
                    EffectsJson = ReadEffectsJson(root),
                    TakenAtUtc = takenAt,
                    RejectedExternalSprites = rejectedExternal,
                }
            );
        }
        catch (JsonException)
        {
            // A depth or token violation surfaces here rather than at TryParseValue.
            return CameraSceneDecodeResult.Refused(CameraRenderRefusal.MalformedScene);
        }
        finally
        {
            document?.Dispose();
        }
    }

    /// <summary>
    /// The freshness window, and the only thing the client's seal can honestly buy.
    /// </summary>
    /// <remarks>
    /// <c>timestamp</c> is the shot's epoch milliseconds (truncated to 100ms) plus a fletcher100
    /// checksum, which is under 100 — so the value is the shot time to within a tenth of a second.
    /// No secret goes into it, so a modified client recomputes it freely and it authenticates
    /// nothing. Rejecting what is too old still stops the same scene being replayed all afternoon,
    /// and that is worth the four lines.
    /// </remarks>
    private static bool TryReadTakenAt(
        long timestamp,
        DateTime nowUtc,
        int skewSeconds,
        out DateTime takenAt
    )
    {
        takenAt = default;

        if (timestamp <= 0)
        {
            return false;
        }

        try
        {
            takenAt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        // Both directions: a clock ahead of the server is as good a sign of a hand-built payload as
        // one a week behind.
        return Math.Abs((nowUtc - takenAt).TotalSeconds) <= skewSeconds;
    }

    private static int ReadZoom(JsonElement root)
    {
        if (
            root.TryGetProperty(ZoomProperty, out JsonElement zoomElement)
            && zoomElement.TryGetInt32(out int zoom)
            && AllowedZoomLevels.Contains(zoom)
        )
        {
            return zoom;
        }

        // Absent is the client's way of saying 1, and anything the widget cannot produce is treated
        // the same way rather than refused: a bad zoom is not worth losing the photo over.
        return 1;
    }

    private static string ReadEffectsJson(JsonElement root) =>
        root.TryGetProperty(FiltersProperty, out JsonElement filters)
        && filters.ValueKind == JsonValueKind.Array
            ? filters.GetRawText()
            : "[]";

    private static bool TryReadPlanes(
        JsonElement root,
        CameraSceneLimits limits,
        out ImmutableArray<CameraScenePlane> planes,
        out CameraRenderRefusal refusal
    )
    {
        planes = [];
        refusal = CameraRenderRefusal.None;

        if (
            !root.TryGetProperty(PlanesProperty, out JsonElement element)
            || element.ValueKind != JsonValueKind.Array
        )
        {
            refusal = CameraRenderRefusal.IncompleteScene;
            return false;
        }

        if (element.GetArrayLength() > limits.MaxPlanes)
        {
            refusal = CameraRenderRefusal.TooComplex;
            return false;
        }

        ImmutableArray<CameraScenePlane>.Builder builder =
            ImmutableArray.CreateBuilder<CameraScenePlane>(element.GetArrayLength());

        foreach (JsonElement plane in element.EnumerateArray())
        {
            if (plane.ValueKind != JsonValueKind.Object)
            {
                refusal = CameraRenderRefusal.MalformedScene;
                return false;
            }

            if (!TryReadCorners(plane, out ImmutableArray<CameraScenePoint> corners))
            {
                refusal = CameraRenderRefusal.MalformedScene;
                return false;
            }

            if (
                !TryReadMasks(plane, limits, out ImmutableArray<CameraSceneMask> masks, out refusal)
            )
            {
                return false;
            }

            if (
                !TryReadTextureColumns(
                    plane,
                    limits,
                    out ImmutableArray<ImmutableArray<string>> columns,
                    out refusal
                )
            )
            {
                return false;
            }

            builder.Add(
                new CameraScenePlane
                {
                    Z = ReadDouble(plane, "z", 0),
                    CornerPoints = corners,
                    Color = ReadInt(plane, "color", 0),
                    Masks = masks,
                    TextureColumns = columns,
                    BottomAligned = ReadBool(plane, "bottomAligned", false),
                }
            );
        }

        planes = builder.ToImmutable();
        return true;
    }

    private static bool TryReadCorners(
        JsonElement plane,
        out ImmutableArray<CameraScenePoint> corners
    )
    {
        corners = [];

        if (
            !plane.TryGetProperty("cornerPoints", out JsonElement element)
            || element.ValueKind != JsonValueKind.Array
            || element.GetArrayLength() != CornerPointCount
        )
        {
            return false;
        }

        ImmutableArray<CameraScenePoint>.Builder builder =
            ImmutableArray.CreateBuilder<CameraScenePoint>(CornerPointCount);

        foreach (JsonElement point in element.EnumerateArray())
        {
            if (point.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            builder.Add(new CameraScenePoint(ReadDouble(point, "x", 0), ReadDouble(point, "y", 0)));
        }

        corners = builder.MoveToImmutable();
        return true;
    }

    private static bool TryReadMasks(
        JsonElement plane,
        CameraSceneLimits limits,
        out ImmutableArray<CameraSceneMask> masks,
        out CameraRenderRefusal refusal
    )
    {
        masks = [];
        refusal = CameraRenderRefusal.None;

        if (
            !plane.TryGetProperty("masks", out JsonElement element)
            || element.ValueKind != JsonValueKind.Array
        )
        {
            // The client leaves the key out entirely when a plane has no masks.
            return true;
        }

        if (element.GetArrayLength() > limits.MaxMasksPerPlane)
        {
            refusal = CameraRenderRefusal.TooComplex;
            return false;
        }

        ImmutableArray<CameraSceneMask>.Builder builder =
            ImmutableArray.CreateBuilder<CameraSceneMask>(element.GetArrayLength());

        foreach (JsonElement mask in element.EnumerateArray())
        {
            if (mask.ValueKind != JsonValueKind.Object)
            {
                refusal = CameraRenderRefusal.MalformedScene;
                return false;
            }

            string? name =
                mask.TryGetProperty("name", out JsonElement maskName)
                && maskName.ValueKind == JsonValueKind.String
                && IsUsableAssetKey(maskName.GetString())
                    ? maskName.GetString()
                    : null;

            if (name is null)
            {
                // A mask nobody can resolve is a hole that will not be cut; the plane is still a
                // plane, so it is dropped rather than taking the photo down with it.
                continue;
            }

            double x = 0;
            double y = 0;

            if (
                mask.TryGetProperty("point", out JsonElement point)
                && point.ValueKind == JsonValueKind.Object
            )
            {
                x = ReadDouble(point, "x", 0);
                y = ReadDouble(point, "y", 0);
            }

            builder.Add(
                new CameraSceneMask
                {
                    AssetName = name,
                    Location = new CameraScenePoint(x, y),
                    FlipH = ReadBool(mask, "flipH", false),
                    FlipV = ReadBool(mask, "flipV", false),
                }
            );
        }

        masks = builder.ToImmutable();
        return true;
    }

    private static bool TryReadTextureColumns(
        JsonElement plane,
        CameraSceneLimits limits,
        out ImmutableArray<ImmutableArray<string>> columns,
        out CameraRenderRefusal refusal
    )
    {
        columns = [];
        refusal = CameraRenderRefusal.None;

        if (
            !plane.TryGetProperty("texCols", out JsonElement element)
            || element.ValueKind != JsonValueKind.Array
        )
        {
            return true;
        }

        if (element.GetArrayLength() > limits.MaxTextureColumnsPerPlane)
        {
            refusal = CameraRenderRefusal.TooComplex;
            return false;
        }

        ImmutableArray<ImmutableArray<string>>.Builder builder = ImmutableArray.CreateBuilder<
            ImmutableArray<string>
        >(element.GetArrayLength());

        foreach (JsonElement column in element.EnumerateArray())
        {
            if (
                !column.TryGetProperty("assetNames", out JsonElement names)
                || names.ValueKind != JsonValueKind.Array
            )
            {
                continue;
            }

            if (names.GetArrayLength() > limits.MaxTextureColumnsPerPlane)
            {
                refusal = CameraRenderRefusal.TooComplex;
                return false;
            }

            ImmutableArray<string>.Builder assets = ImmutableArray.CreateBuilder<string>(
                names.GetArrayLength()
            );

            foreach (JsonElement name in names.EnumerateArray())
            {
                if (name.ValueKind == JsonValueKind.String && IsUsableAssetKey(name.GetString()))
                {
                    assets.Add(name.GetString()!);
                }
            }

            builder.Add(assets.ToImmutable());
        }

        columns = builder.ToImmutable();
        return true;
    }

    private static bool TryReadSprites(
        JsonElement root,
        CameraSceneLimits limits,
        out ImmutableArray<CameraSceneSprite> sprites,
        out int rejectedExternal,
        out CameraRenderRefusal refusal
    )
    {
        sprites = [];
        rejectedExternal = 0;
        refusal = CameraRenderRefusal.None;

        if (
            !root.TryGetProperty(SpritesProperty, out JsonElement element)
            || element.ValueKind != JsonValueKind.Array
        )
        {
            refusal = CameraRenderRefusal.IncompleteScene;
            return false;
        }

        if (element.GetArrayLength() > limits.MaxSprites)
        {
            refusal = CameraRenderRefusal.TooComplex;
            return false;
        }

        ImmutableArray<CameraSceneSprite>.Builder builder =
            ImmutableArray.CreateBuilder<CameraSceneSprite>(element.GetArrayLength());

        foreach (JsonElement sprite in element.EnumerateArray())
        {
            if (sprite.ValueKind != JsonValueKind.Object)
            {
                refusal = CameraRenderRefusal.MalformedScene;
                return false;
            }

            string? rawName =
                sprite.TryGetProperty("name", out JsonElement nameElement)
                && nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString()
                    : null;

            if (string.IsNullOrEmpty(rawName) || rawName.Length > MaxAssetNameLength)
            {
                continue;
            }

            CameraSpriteSource source;

            if (LooksLikeUrl(rawName))
            {
                if (!CameraExternalImagePolicy.IsAllowed(rawName, limits.ExternalImagePrefixes))
                {
                    // Dropped, not refused. The client does the same thing to its own sprites past
                    // thirty external images, so a photo missing one badge is a shape it already
                    // produces — and refusing the whole scene would let one bad name deny the
                    // camera to a room.
                    rejectedExternal++;
                    continue;
                }

                source = CameraSpriteSource.ExternalImage;
            }
            else
            {
                if (!IsUsableAssetKey(rawName))
                {
                    continue;
                }

                source = CameraSpriteSource.LocalAsset;
            }

            builder.Add(
                new CameraSceneSprite
                {
                    Name = rawName,
                    Source = source,
                    X = ReadDouble(sprite, "x", 0),
                    Y = ReadDouble(sprite, "y", 0),
                    Z = ReadDouble(sprite, "z", 0),
                    Alpha = Math.Clamp(ReadInt(sprite, "alpha", 255), 0, 255),
                    FlipH = ReadBool(sprite, "flipH", false),
                    Skew = ReadDouble(sprite, "skew", 0),
                    Frame = ReadInt(sprite, "frame", 0),
                    Color =
                        sprite.TryGetProperty("color", out JsonElement color)
                        && color.TryGetInt32(out int colorValue)
                            ? colorValue
                            : null,
                    BlendMode =
                        sprite.TryGetProperty("blendMode", out JsonElement blend)
                        && blend.ValueKind == JsonValueKind.String
                            ? blend.GetString()
                            : null,
                    Width = Math.Max(0, ReadInt(sprite, "width", 0)),
                    Height = Math.Max(0, ReadInt(sprite, "height", 0)),
                    PaletteSourceName =
                        sprite.TryGetProperty("paletteSourceName", out JsonElement palette)
                        && palette.ValueKind == JsonValueKind.String
                        && IsUsableAssetKey(palette.GetString())
                            ? palette.GetString()
                            : null,
                }
            );
        }

        sprites = builder.ToImmutable();
        return true;
    }

    private static bool LooksLikeUrl(string name) =>
        name.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a name may be used as a key into the asset index.
    /// </summary>
    /// <remarks>
    /// Nothing concatenates this into a path — the renderer looks it up in a dictionary — so this is
    /// the second lock rather than the first. It is here because the day somebody writes the obvious
    /// <c>Path.Combine(assetRoot, name)</c> is the day the first lock stops existing, and a key with
    /// a separator in it has never been legitimate.
    /// </remarks>
    private static bool IsUsableAssetKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxAssetNameLength)
        {
            return false;
        }

        if (name.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (char c in name)
        {
            if (c is '/' or '\\' or ':' or '\0')
            {
                return false;
            }

            if (char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }

    private static double ReadDouble(JsonElement parent, string property, double fallback) =>
        parent.TryGetProperty(property, out JsonElement element)
        && element.ValueKind == JsonValueKind.Number
        && element.TryGetDouble(out double value)
        && double.IsFinite(value)
            ? value
            : fallback;

    private static int ReadInt(JsonElement parent, string property, int fallback) =>
        parent.TryGetProperty(property, out JsonElement element)
        && element.ValueKind == JsonValueKind.Number
        && element.TryGetInt32(out int value)
            ? value
            : fallback;

    private static bool ReadBool(JsonElement parent, string property, bool fallback) =>
        parent.TryGetProperty(property, out JsonElement element)
            ? element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => element.TryGetInt32(out int number) && number != 0,
                _ => fallback,
            }
            : fallback;
}
