using System;
using System.IO;
using FluentAssertions;
using Vortex.Furniture.Assets;
using Xunit;

namespace Vortex.Rooms.Tests.Furniture;

/// <summary>
/// The decoder against a bundle nobody here wrote.
/// </summary>
/// <remarks>
/// <para>
/// Every other test in this pair builds its own pack to the format as documented, which means they
/// all agree with the same reading of it — including a wrong one. This one is <c>duck.nitro</c> off
/// the hotel's own asset host, 1.6 KB, and it caught the reading that the documentation does not
/// mention: a frame is keyed by the bundle's name <i>and</i> the asset's, so the rectangle for
/// <c>duck_64_a_4_0</c> is filed under <c>duck_duck_64_a_4_0</c>. The payload only ever names the
/// asset, so every lookup missed and every furniture would have been absent from every photo, with
/// the synthetic tests green.
/// </para>
/// <para>
/// It also carries the two things the format allows and a hand-written fixture would not think to:
/// mirrored directions (five of this bundle's nine assets have no pixels of their own), and a real
/// packer's choice of compression.
/// </para>
/// </remarks>
public sealed class NitroRealBundleTests
{
    private static byte[] Duck() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Furniture/Fixtures/duck.nitro"));

    [Fact]
    public void ARealBundleParses()
    {
        NitroBundle.TryRead(Duck(), out NitroBundle? bundle).Should().BeTrue();

        bundle!.Manifest.Should().NotBeNull();
        bundle.Sheet.Should().NotBeNull("the sheet is a PNG and the signature is checked");
    }

    /// <summary>
    /// The asset the payload names, resolved to pixels. <c>duck_64_a_0_0</c> owns nothing: its
    /// manifest entry is <c>{"source":"duck_64_a_6_0","flipH":true}</c>, so this asserts the whole
    /// chain — indirection, the bundle-name prefix on the frame key, and the mirror.
    /// </summary>
    [Fact]
    public void AMirroredAssetResolvesToTheFrameItBorrows()
    {
        NitroAssetManifest manifest = Manifest();

        manifest.TryResolve("duck_64_a_0_0", out NitroSpriteFrame frame).Should().BeTrue();

        frame.Should().Be(new NitroSpriteFrame(0, 23, 22, 23, FlipH: true, FlipV: false));
    }

    /// <summary>And the one it borrows from, which owns its pixels and is not mirrored.</summary>
    [Fact]
    public void TheAssetBehindTheMirrorIsTheSameRectangle()
    {
        NitroAssetManifest manifest = Manifest();

        manifest.TryResolve("duck_64_a_6_0", out NitroSpriteFrame frame).Should().BeTrue();

        frame.Should().Be(new NitroSpriteFrame(0, 23, 22, 23, FlipH: false, FlipV: false));
    }

    /// <summary>
    /// Four rectangles on the sheet, each indexed twice — under the packer's key and under the asset
    /// name a payload would use.
    /// </summary>
    [Fact]
    public void EveryFrameIsReachableByTheNameThePayloadUses()
    {
        Manifest().FrameCount.Should().Be(8);
    }

    private static NitroAssetManifest Manifest()
    {
        NitroBundle.TryRead(Duck(), out NitroBundle? bundle).Should().BeTrue();

        NitroAssetManifest
            .TryRead(bundle!.Manifest, out NitroAssetManifest? manifest)
            .Should()
            .BeTrue();

        return manifest!;
    }
}
