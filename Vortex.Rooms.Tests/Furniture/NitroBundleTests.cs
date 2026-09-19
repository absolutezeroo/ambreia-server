using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Vortex.Furniture.Assets;
using Xunit;

namespace Vortex.Rooms.Tests.Furniture;

/// <summary>
/// Reading a <c>.nitro</c> bundle.
/// </summary>
/// <remarks>
/// The bundles are built by whatever packed the hotel's asset set, and the client inflates them with
/// pako, which accepts zlib, gzip and raw deflate without being told which. So a packer can mix them
/// inside one pack and nothing downstream notices — and one in this hotel does: the guild furniture
/// bundles carry gzip and raw zlib side by side, and a gzip-only decoder read half of them and
/// skipped the rest in silence. Hence a test per wrapper, and one with two wrappers in the same
/// bundle.
/// </remarks>
public sealed class NitroBundleTests
{
    [Theory]
    [InlineData(Wrap.ZLib)]
    [InlineData(Wrap.GZip)]
    [InlineData(Wrap.Raw)]
    public void ABundleIsReadWhicheverWayItsFilesWereDeflated(Wrap wrap)
    {
        byte[] sheet = Png(1, 2, 3);

        byte[] bundle = Pack([
            ("furni.json", Utf8("""{"name":"chair"}"""), wrap),
            ("furni.png", sheet, wrap),
        ]);

        NitroBundle.TryRead(bundle, out NitroBundle? read).Should().BeTrue();

        Utf8(read!.Manifest!).Should().Be("""{"name":"chair"}""");
        read.Sheet.Should().Equal(sheet);
    }

    /// <summary>A PNG signature and then whatever the caller wants; nothing here decodes an image.</summary>
    private static byte[] Png(params byte[] body) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. body];

    /// <summary>The case that shipped: one pack, two wrappers.</summary>
    [Fact]
    public void OneBundleMayMixWrappers()
    {
        byte[] sheet = Png(9, 9);

        byte[] bundle = Pack([("a.json", Utf8("{}"), Wrap.GZip), ("a.png", sheet, Wrap.Raw)]);

        NitroBundle.TryRead(bundle, out NitroBundle? read).Should().BeTrue();

        Utf8(read!.Manifest!).Should().Be("{}");
        read.Sheet.Should().Equal(sheet);
    }

    /// <summary>
    /// A missing bundle does not arrive as a 404 the reader can see — Apache serves its error
    /// document and a dev server serves the SPA shell. Two characters of markup read as a plausible
    /// file count, and the failure then reports a length out of range instead of a missing file.
    /// </summary>
    [Theory]
    [InlineData("<!doctype html><html><body>Not Found</body></html>")]
    [InlineData("{\"error\":\"not found\"}")]
    public void APageInsteadOfABundle_IsRefused(string body)
    {
        NitroBundle.TryRead(Utf8(body), out NitroBundle? read).Should().BeFalse();
        read.Should().BeNull();
    }

    [Fact]
    public void ATruncatedBundle_IsRefusedRatherThanRead()
    {
        byte[] bundle = Pack([("furni.json", Utf8("{}"), Wrap.ZLib)]);

        for (int cut = 1; cut < bundle.Length; cut++)
        {
            NitroBundle
                .TryRead(bundle.AsSpan(0, cut), out _)
                .Should()
                .BeFalse($"a bundle cut to {cut} of {bundle.Length} bytes is not readable");
        }
    }

    /// <summary>
    /// Every length in the container is a number out of the file. A count nothing follows must not
    /// become an allocation.
    /// </summary>
    [Fact]
    public void AFileCountTheBundleCannotHonour_IsRefused()
    {
        byte[] bundle = new byte[2];

        BinaryPrimitives.WriteInt16BigEndian(bundle, short.MaxValue);

        NitroBundle.TryRead(bundle, out _).Should().BeFalse();
    }

    /// <summary>
    /// Raw deflate has no header, so it cannot be detected — only attempted, last. Arbitrary bytes
    /// therefore sometimes inflate to arbitrary bytes without throwing, which is pako's behaviour and
    /// so the client's. The container cannot catch that; what catches it is that the result is then
    /// not a PNG.
    /// </summary>
    [Fact]
    public void AFileThatIsNotDeflatedAtAll_YieldsNoSheet()
    {
        byte[] bundle = Pack([("furni.png", Utf8("this was never deflated"), Wrap.None)]);

        // The read may well succeed: that is the format's doing, not a decision made here.
        NitroBundle.TryRead(bundle, out NitroBundle? read);

        read?.Sheet.Should().BeNull("garbage that inflates is still not a sprite sheet");
    }

    [Fact]
    public void ASheetIsOnlyASheetIfItIsAPng()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 1, 2];

        byte[] bundle = Pack([("furni.png", png, Wrap.ZLib)]);

        NitroBundle.TryRead(bundle, out NitroBundle? read).Should().BeTrue();

        read!.Sheet.Should().Equal(png);
    }

    public enum Wrap
    {
        ZLib,
        GZip,
        Raw,
        None,
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static string Utf8(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    /// <summary>Builds the container by hand, so the test owns the format rather than the reader.</summary>
    private static byte[] Pack(IReadOnlyList<(string Name, byte[] Data, Wrap Wrap)> files)
    {
        using MemoryStream output = new();

        Span<byte> number = stackalloc byte[4];

        BinaryPrimitives.WriteInt16BigEndian(number, (short)files.Count);
        output.Write(number[..2]);

        foreach ((string name, byte[] data, Wrap wrap) in files)
        {
            byte[] nameBytes = Utf8(name);
            byte[] deflated = Deflate(data, wrap);

            BinaryPrimitives.WriteInt16BigEndian(number, (short)nameBytes.Length);
            output.Write(number[..2]);
            output.Write(nameBytes);

            BinaryPrimitives.WriteInt32BigEndian(number, deflated.Length);
            output.Write(number[..4]);
            output.Write(deflated);
        }

        return output.ToArray();
    }

    private static byte[] Deflate(byte[] data, Wrap wrap)
    {
        if (wrap == Wrap.None)
        {
            return data;
        }

        using MemoryStream output = new();

        using (Stream compressor = Compressor(output, wrap))
        {
            compressor.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    private static Stream Compressor(Stream output, Wrap wrap) =>
        wrap switch
        {
            Wrap.GZip => new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true),
            Wrap.Raw => new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true),
            _ => new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true),
        };
}
