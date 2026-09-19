using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Vortex.Furniture.Assets;

/// <summary>
/// The files inside one <c>.nitro</c> bundle.
/// </summary>
/// <remarks>
/// <para>
/// The container is the client's, and it is deliberately plain:
/// </para>
/// <code>
/// int16 be   file count
/// per file:
///   int16 be   name length
///   bytes      name, utf-8
///   int32 be   deflated length
///   bytes      deflated data
/// </code>
/// <para>
/// A furniture bundle holds two of them: a <c>.json</c> manifest naming every asset and where it
/// sits on the sheet, and the <c>.png</c> sheet itself. Nothing in the header says which is which,
/// so they are found by extension exactly as the client finds them.
/// </para>
/// <para>
/// Every length here is a number read out of the file, and the file arrives over HTTP from a host
/// named in configuration. That is not a player-controlled payload, but it is not this process
/// either: a truncated download, a proxy's error page or a half-written cache entry all arrive as
/// plausible bytes. So each length is checked against what remains rather than trusted, and the
/// whole read answers false instead of throwing — a bundle that will not parse costs a sprite, not
/// a photo.
/// </para>
/// </remarks>
public sealed class NitroBundle
{
    /// <summary>No bundle in the pack comes near this; it stops a corrupt count allocating.</summary>
    private const int MaxFiles = 64;

    private const int MaxNameLength = 1024;

    private readonly Dictionary<string, byte[]> _files;

    private NitroBundle(Dictionary<string, byte[]> files) => _files = files;

    /// <summary>The manifest, still as bytes: naming what is in it is the reader's job, not this one's.</summary>
    public byte[]? Manifest => Find(".json");

    /// <summary>The sprite sheet, or null when what was found is not one.</summary>
    /// <remarks>
    /// The signature is checked because raw deflate cannot be. It has no header, so
    /// <see cref="NitroCompression"/> can only try it last and never knows whether it succeeded —
    /// arbitrary bytes sometimes decode to arbitrary bytes without throwing, which is pako's
    /// behaviour too and therefore the client's. Eight bytes here is where that stops being someone
    /// else's problem.
    /// </remarks>
    public byte[]? Sheet
    {
        get
        {
            byte[]? found = Find(".png");

            return found is not null && found.AsSpan().StartsWith(PngSignature) ? found : null;
        }
    }

    private static ReadOnlySpan<byte> PngSignature =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool TryRead(ReadOnlySpan<byte> bundle, out NitroBundle? read)
    {
        read = null;

        if (bundle.Length < sizeof(short))
        {
            return false;
        }

        // A missing .nitro does not arrive as a 404 everywhere: Apache answers with its error
        // document and a dev server answers with the SPA shell, and two characters of markup read as
        // a plausible file count. Rejecting text up front turns "name length out of range" back into
        // "the file is not there".
        if (LooksLikeText(bundle))
        {
            return false;
        }

        int count = BinaryPrimitives.ReadInt16BigEndian(bundle);

        if (count is <= 0 or > MaxFiles)
        {
            return false;
        }

        Dictionary<string, byte[]> files = new(count, StringComparer.OrdinalIgnoreCase);
        int offset = sizeof(short);

        for (int i = 0; i < count; i++)
        {
            if (!TryReadFile(bundle, ref offset, files))
            {
                return false;
            }
        }

        read = new NitroBundle(files);
        return true;
    }

    private static bool TryReadFile(
        ReadOnlySpan<byte> bundle,
        ref int offset,
        Dictionary<string, byte[]> files
    )
    {
        if (!TryTake(bundle, ref offset, sizeof(short), out ReadOnlySpan<byte> nameLengthBytes))
        {
            return false;
        }

        int nameLength = BinaryPrimitives.ReadInt16BigEndian(nameLengthBytes);

        if (nameLength is <= 0 or > MaxNameLength)
        {
            return false;
        }

        if (!TryTake(bundle, ref offset, nameLength, out ReadOnlySpan<byte> nameBytes))
        {
            return false;
        }

        if (!TryTake(bundle, ref offset, sizeof(int), out ReadOnlySpan<byte> dataLengthBytes))
        {
            return false;
        }

        int dataLength = BinaryPrimitives.ReadInt32BigEndian(dataLengthBytes);

        if (dataLength <= 0)
        {
            return false;
        }

        if (!TryTake(bundle, ref offset, dataLength, out ReadOnlySpan<byte> deflated))
        {
            return false;
        }

        if (!NitroCompression.TryInflate(deflated, out byte[] inflated))
        {
            return false;
        }

        // Last writer wins, like the client's own map. A bundle naming one file twice is malformed,
        // not an attack surface -- there is nothing here to shadow.
        files[Encoding.UTF8.GetString(nameBytes)] = inflated;

        return true;
    }

    /// <summary>Advances past <paramref name="length"/> bytes, or refuses if they are not there.</summary>
    private static bool TryTake(
        ReadOnlySpan<byte> bundle,
        ref int offset,
        int length,
        out ReadOnlySpan<byte> taken
    )
    {
        taken = default;

        if (length < 0 || offset > bundle.Length - length)
        {
            return false;
        }

        taken = bundle.Slice(offset, length);
        offset += length;

        return true;
    }

    private static bool LooksLikeText(ReadOnlySpan<byte> bundle) =>
        bundle[0] is (byte)'<' or (byte)'{' or (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t';

    private byte[]? Find(string extension)
    {
        foreach ((string name, byte[] data) in _files)
        {
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return data;
            }
        }

        return null;
    }
}
