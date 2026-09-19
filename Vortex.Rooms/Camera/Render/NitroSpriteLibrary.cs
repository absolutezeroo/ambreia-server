using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Vortex.Furniture.Assets;
using Vortex.Primitives.Camera;

namespace Vortex.Rooms.Camera.Render;

/// <summary>
/// Turns the asset name a photo carries into pixels, out of the hotel's own <c>.nitro</c> pack.
/// </summary>
/// <remarks>
/// <para>
/// The payload names <c>duck_64_a_0_0</c> and nothing else — no size, no bundle, no url. The client
/// knows where that lives because it loaded the bundle for that furniture; the server has to work it
/// out, and the chain is: classname out of the name, bundle off the asset host, frame out of the
/// manifest, crop off the sheet.
/// </para>
/// <para>
/// <b>The classname index does two jobs and the second one is the important one.</b> It says which
/// bundle a name belongs to, and it is also what stops a payload turning this into a request
/// generator: a scene may name thousands of sprites, and without an index every unknown name would
/// be a fetch at the asset host. A name that matches no classname this hotel sells never leaves the
/// process.
/// </para>
/// <para>
/// Everything is cached, misses included. A bundle that is not there is not there on the next photo
/// either, and re-asking would turn one missing furniture into a request per photo forever.
/// </para>
/// </remarks>
/// <param name="classNames">
/// Every furniture classname this hotel sells. A delegate rather than a <c>DbContext</c>: what this
/// needs is a set of strings, and taking the database instead would make the whole library
/// untestable over the one thing worth testing — that a real bundle resolves to real pixels.
/// </param>
/// <param name="http">
/// Shared with <see cref="CameraSpriteResolver"/> and owned by it. One client for every fetch the
/// camera makes: a second would be a second connection pool for the same host, and a second thing to
/// remember to dispose.
/// </param>
internal sealed class NitroSpriteLibrary(
    Func<CancellationToken, Task<IReadOnlyCollection<string>>> classNames,
    HttpClient http,
    ILogger logger
)
{
    private readonly ConcurrentDictionary<string, Task<Bundle?>> _bundles = new(
        StringComparer.Ordinal
    );

    private readonly ConcurrentDictionary<string, byte[]?> _sprites = new(StringComparer.Ordinal);

    private Task<ClassNameIndex>? _index;

    private Task<FiguremapIndex>? _figuremap;

    /// <summary>The sprite's own PNG, or null when this hotel's pack cannot produce it.</summary>
    public async Task<byte[]?> GetSpriteAsync(
        string assetName,
        CameraRenderLimits limits,
        CancellationToken ct
    )
    {
        if (limits.FurnitureAssetUrl.Length == 0 && limits.AvatarAssetUrl.Length == 0)
        {
            return null;
        }

        if (_sprites.TryGetValue(assetName, out byte[]? cached))
        {
            return cached;
        }

        byte[]? sprite = await ResolveAsync(assetName, limits, ct).ConfigureAwait(false);

        // Cached either way. A furniture whose bundle is missing must not be looked for once per
        // photo for the rest of the process's life.
        _sprites[assetName] = sprite;

        return sprite;
    }

    private async Task<byte[]?> ResolveAsync(
        string assetName,
        CameraRenderLimits limits,
        CancellationToken ct
    )
    {
        NitroBundleAddress? address = await AddressAsync(assetName, limits, ct)
            .ConfigureAwait(false);

        if (address is not { } found)
        {
            return null;
        }

        Bundle? bundle = await BundleAsync(found, limits, ct).ConfigureAwait(false);

        if (bundle is null || !bundle.Manifest.TryResolve(assetName, out NitroSpriteFrame frame))
        {
            return null;
        }

        return Crop(bundle.Sheet, frame);
    }

    /// <summary>
    /// Which bundle a sprite name belongs to. Three families, tried in the order that costs least.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Furniture</b> is checked against this hotel's own catalogue, so an invented name is refused
    /// without a request ever leaving the process — which is why it goes first and needs no budget.
    /// </para>
    /// <para>
    /// <b>An avatar part</b> names a part and not a library (<c>h_std_bd_1_2_0</c> is part type
    /// <c>bd</c>, id <c>1</c>), so the figuremap answers it — also without a request, since the map
    /// is already in hand.
    /// </para>
    /// <para>
    /// <b>A pet</b> is the one that cannot be checked: its bundle name is simply the first segment of
    /// the sprite name (<c>dog_64_a_0_100</c> lives in <c>dog.nitro</c>), so any name is an address.
    /// That is what the caller's fetch budget is for, and why this is tried last.
    /// </para>
    /// </remarks>
    private async Task<NitroBundleAddress?> AddressAsync(
        string assetName,
        CameraRenderLimits limits,
        CancellationToken ct
    )
    {
        ClassNameIndex index = await IndexAsync(ct).ConfigureAwait(false);

        if (index.TryClassName(assetName, out string? className) && className is not null)
        {
            return Address(limits.FurnitureAssetUrl, className);
        }

        if (limits.AvatarAssetUrl.Length == 0)
        {
            return null;
        }

        FiguremapIndex figuremap = await FiguremapAsync(limits, ct).ConfigureAwait(false);

        if (figuremap.TryLibrary(assetName, out string? library) && library is not null)
        {
            return Address(limits.AvatarAssetUrl, library);
        }

        int cut = assetName.IndexOf('_', StringComparison.Ordinal);

        return cut > 0 ? Address(limits.AvatarAssetUrl, assetName[..cut]) : null;
    }

    private static NitroBundleAddress? Address(string baseUrl, string bundle)
    {
        if (baseUrl.Length == 0 || bundle.Length == 0)
        {
            return null;
        }

        // The bundle name is derived from a payload-supplied string in two of the three cases, so it
        // is escaped rather than trusted: this is the only place a name becomes a url.
        return new NitroBundleAddress($"{baseUrl}{Uri.EscapeDataString(bundle)}.nitro", bundle);
    }

    private Task<FiguremapIndex> FiguremapAsync(CameraRenderLimits limits, CancellationToken ct) =>
        _figuremap ??= LoadFiguremapAsync(limits, ct);

    private async Task<FiguremapIndex> LoadFiguremapAsync(
        CameraRenderLimits limits,
        CancellationToken ct
    )
    {
        if (limits.FiguremapUrl.Length == 0)
        {
            return FiguremapIndex.Empty;
        }

        try
        {
            string xml = await http.GetStringAsync(limits.FiguremapUrl, ct).ConfigureAwait(false);
            FiguremapIndex index = FiguremapIndex.Read(xml);

            logger.LogInformation("Camera: {Count} avatar parts indexed.", index.Count);

            return index;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(
                ex,
                "Camera: the figuremap could not be fetched; avatars will be absent."
            );
            return FiguremapIndex.Empty;
        }
    }

    private Task<Bundle?> BundleAsync(
        NitroBundleAddress address,
        CameraRenderLimits limits,
        CancellationToken ct
    ) => _bundles.GetOrAdd(address.Url, _ => FetchBundleAsync(address, limits, ct));

    private async Task<Bundle?> FetchBundleAsync(
        NitroBundleAddress address,
        CameraRenderLimits limits,
        CancellationToken ct
    )
    {
        (string url, string className) = address;

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(limits.ExternalTimeout);

        try
        {
            using HttpResponseMessage response = await http.GetAsync(
                    url,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token
                )
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug(
                    "Camera: no bundle for {ClassName} ({Status}).",
                    className,
                    (int)response.StatusCode
                );
                return null;
            }

            byte[] bytes = await response
                .Content.ReadAsByteArrayAsync(timeout.Token)
                .ConfigureAwait(false);

            if (
                !NitroBundle.TryRead(bytes, out NitroBundle? read)
                || read!.Sheet is not { } sheet
                || !NitroAssetManifest.TryRead(read.Manifest, out NitroAssetManifest? manifest)
            )
            {
                logger.LogWarning("Camera: {ClassName}.nitro did not decode.", className);
                return null;
            }

            logger.LogDebug(
                "Camera: {ClassName}.nitro decoded, {Frames} frame(s).",
                className,
                manifest!.FrameCount
            );

            return new Bundle(manifest, sheet);
        }
        catch (HttpRequestException ex)
        {
            logger.LogDebug(ex, "Camera: {ClassName}.nitro could not be fetched.", className);
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogDebug("Camera: {ClassName}.nitro timed out.", className);
            return null;
        }
    }

    private Task<ClassNameIndex> IndexAsync(CancellationToken ct) => _index ??= LoadIndexAsync(ct);

    private async Task<ClassNameIndex> LoadIndexAsync(CancellationToken ct)
    {
        IReadOnlyCollection<string> names = await classNames(ct).ConfigureAwait(false);

        logger.LogInformation("Camera: {Count} furniture classnames indexed.", names.Count);

        return new ClassNameIndex(names);
    }

    /// <summary>
    /// The sheet is decoded per sprite rather than kept decoded.
    /// </summary>
    /// <remarks>
    /// A furniture sheet is a few kilobytes and a couple of hundred pixels across, so decoding it
    /// again costs less than holding an <c>SKBitmap</c> per classname alive for the life of the
    /// process. If a hotel ever ships sheets where that stops being true, the cache to add is of the
    /// decoded bitmap, bounded — not of more sprites.
    /// </remarks>
    private static byte[]? Crop(byte[] sheet, NitroSpriteFrame frame)
    {
        using SKBitmap decoded = SKBitmap.Decode(sheet);

        if (decoded is null)
        {
            return null;
        }

        SKRectI source = SKRectI.Create(frame.X, frame.Y, frame.Width, frame.Height);

        // The manifest is a file; the sheet is another file. Nothing guarantees the rectangle is
        // inside the image, and ExtractSubset on a rectangle that is not simply fails.
        if (!source.IntersectsWith(SKRectI.Create(decoded.Width, decoded.Height)))
        {
            return null;
        }

        source.Intersect(SKRectI.Create(decoded.Width, decoded.Height));

        using SKBitmap cropped = new(source.Width, source.Height);

        if (!decoded.ExtractSubset(cropped, source))
        {
            return null;
        }

        using SKImage image = Turn(cropped, frame);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        return encoded.ToArray();
    }

    /// <summary>
    /// Applies the mirror the manifest asked for, which is most of the pack: furniture ships four
    /// directions and mirrors the other four rather than drawing them.
    /// </summary>
    /// <remarks>
    /// The payload's own <c>flipH</c> is a different flip and is applied by the canvas. The two
    /// compose — an asset that is a mirror, drawn mirrored, is the original again — so baking this
    /// one here and leaving that one there is not a duplication.
    /// </remarks>
    private static SKImage Turn(SKBitmap cropped, NitroSpriteFrame frame)
    {
        if (!frame.FlipH && !frame.FlipV)
        {
            return SKImage.FromBitmap(cropped);
        }

        using SKSurface surface = SKSurface.Create(
            new SKImageInfo(cropped.Width, cropped.Height, SKColorType.Rgba8888, SKAlphaType.Premul)
        );

        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.Scale(frame.FlipH ? -1 : 1, frame.FlipV ? -1 : 1);
        surface.Canvas.Translate(
            frame.FlipH ? -cropped.Width : 0,
            frame.FlipV ? -cropped.Height : 0
        );
        surface.Canvas.DrawBitmap(cropped, 0, 0);

        return surface.Snapshot();
    }

    private sealed record Bundle(NitroAssetManifest Manifest, byte[] Sheet);

    /// <summary>
    /// Which classname an asset name belongs to.
    /// </summary>
    /// <remarks>
    /// Longest match wins, and it has to: <c>chair_basic</c> and <c>chair</c> can both be real, and
    /// <c>chair_basic_64_a_0_0</c> belongs to the first. Matching is on <c>classname_</c> rather than
    /// on the bare prefix so that <c>chair</c> does not claim <c>chairman_64_a_0_0</c>.
    /// </remarks>
    private sealed class ClassNameIndex
    {
        private readonly HashSet<string> _names;
        private readonly int _longest;

        public ClassNameIndex(IReadOnlyCollection<string> names)
        {
            _names = new HashSet<string>(names, StringComparer.Ordinal);
            _longest = names.Count == 0 ? 0 : names.Max(name => name.Length);
        }

        public bool TryClassName(string assetName, out string? className)
        {
            className = null;

            // Walks the separators from the right, so the first hit is the longest classname that
            // fits. A furniture asset name is short; this is a handful of hash lookups.
            for (
                int cut = Math.Min(assetName.Length - 1, _longest);
                cut > 0;
                cut = assetName.LastIndexOf('_', cut - 1)
            )
            {
                if (assetName[cut] != '_')
                {
                    continue;
                }

                string candidate = assetName[..cut];

                if (_names.Contains(candidate))
                {
                    className = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}
