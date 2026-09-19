using System;
using System.IO;
using System.IO.Compression;

namespace Vortex.Furniture.Assets;

/// <summary>
/// Inflates one file out of a <c>.nitro</c> bundle, whichever way it was deflated.
/// </summary>
/// <remarks>
/// <para>
/// The client inflates with pako, which sniffs the header and accepts zlib, gzip and raw deflate
/// alike, so a packer is free to mix them and nothing downstream ever notices. A pack in this hotel
/// already does: the guild furniture bundles carry gzip and raw zlib side by side, and a gzip-only
/// decoder read half of them and silently skipped the rest.
/// </para>
/// <para>
/// So the wrapper is detected rather than assumed, and raw deflate is the fallback rather than an
/// error — that is exactly pako's order.
/// </para>
/// </remarks>
internal static class NitroCompression
{
    /// <summary>A bundle's own files are small; this is the ceiling on one inflated file.</summary>
    internal const int MaxInflatedBytes = 32 * 1024 * 1024;

    public static bool TryInflate(ReadOnlySpan<byte> deflated, out byte[] inflated)
    {
        inflated = [];

        if (deflated.IsEmpty)
        {
            return false;
        }

        byte[] source = deflated.ToArray();

        // gzip first: its magic is unambiguous. Then zlib, whose header is a checked pair rather
        // than a magic number. Raw deflate has no header at all, so it can only be the fallback --
        // and trying it earlier would let a corrupt zlib stream decode into rubbish.
        return TryWith(source, Wrapper.GZip, out inflated)
            || (LooksLikeZLib(deflated) && TryWith(source, Wrapper.ZLib, out inflated))
            || TryWith(source, Wrapper.Raw, out inflated);
    }

    /// <summary>
    /// RFC 1950: the low nibble of the first byte is the compression method (8 for deflate) and the
    /// two header bytes together are a multiple of 31.
    /// </summary>
    private static bool LooksLikeZLib(ReadOnlySpan<byte> data) =>
        data.Length >= 2 && (data[0] & 0x0F) == 8 && (((data[0] << 8) | data[1]) % 31) == 0;

    private static bool TryWith(byte[] source, Wrapper wrapper, out byte[] inflated)
    {
        inflated = [];

        try
        {
            using MemoryStream input = new(source, writable: false);
            using Stream decompressor = Open(input, wrapper);
            using MemoryStream output = new();

            // Copied through a bounded read rather than CopyTo: the length in the bundle describes
            // the *deflated* size, so nothing upstream bounds what this expands to.
            byte[] buffer = new byte[81920];
            int total = 0;
            int read;

            while ((read = decompressor.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;

                if (total > MaxInflatedBytes)
                {
                    return false;
                }

                output.Write(buffer, 0, read);
            }

            if (output.Length == 0)
            {
                return false;
            }

            inflated = output.ToArray();
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static Stream Open(Stream input, Wrapper wrapper) =>
        wrapper switch
        {
            Wrapper.GZip => new GZipStream(input, CompressionMode.Decompress),
            Wrapper.ZLib => new ZLibStream(input, CompressionMode.Decompress),
            _ => new DeflateStream(input, CompressionMode.Decompress),
        };

    private enum Wrapper
    {
        GZip,
        ZLib,
        Raw,
    }
}
