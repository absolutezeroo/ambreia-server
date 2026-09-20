using System;
using System.IO;
using FluentAssertions;
using Vortex.Furniture.Assets;
using Xunit;

namespace Vortex.Rooms.Tests.Furniture;

/// <summary>
/// A pet's colour, which is a palette and never a tint.
/// </summary>
/// <remarks>
/// <para>
/// Every photo drew pets flat and washed out, and the obvious explanation — a lost tint — was wrong
/// twice over. A pet's tint <i>is</i> white, by design: <c>pet.Color</c> defaults to <c>ffffff</c>
/// and there is nothing for a multiply to do. Its artwork is drawn once and coloured by swapping.
/// </para>
/// <para>
/// The real dog bundle says so plainly: 952 assets whose names carry a part, a direction and a
/// frame and <b>no breed</b>, 856 of them flagged <c>usesPalette</c>, and twenty-five palettes —
/// one per breed in the hotel's own <c>pet_palettes</c> seed.
/// </para>
/// </remarks>
public sealed class NitroPaletteTests
{
    private static NitroAssetManifest Dog()
    {
        byte[] bytes = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Furniture/Fixtures/dog.nitro")
        );

        NitroBundle.TryRead(bytes, out NitroBundle? bundle).Should().BeTrue();

        NitroAssetManifest
            .TryRead(bundle!.Manifest, out NitroAssetManifest? manifest)
            .Should()
            .BeTrue();

        return manifest!;
    }

    [Fact]
    public void ADogCarriesOnePalettePerBreed()
    {
        Dog().PaletteCount.Should().Be(25);
    }

    /// <summary>
    /// Keyed as the client keys them — <c>String(pet_palette_index)</c>, which is the breed — and
    /// each is the full 256 entries a green channel can index.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("24")]
    public void APaletteIsIndexedByTheBreedAndCoversEveryGreenValue(string breed)
    {
        Dog().TryPalette(breed, out int[]? rgb).Should().BeTrue();

        rgb!.Length.Should().Be(256);
    }

    /// <summary>
    /// Two breeds of the same animal differ in their palette and in nothing else. If they came back
    /// equal the swap would be reading the wrong table and every dog in the hotel would be the same
    /// colour, which is indistinguishable from not swapping at all.
    /// </summary>
    [Fact]
    public void TwoBreedsAreNotTheSamePalette()
    {
        NitroAssetManifest dog = Dog();

        dog.TryPalette("0", out int[]? first).Should().BeTrue();
        dog.TryPalette("3", out int[]? second).Should().BeTrue();

        first!.Should().NotEqual(second!);
    }

    [Fact]
    public void ABreedNobodyOwns_HasNoPalette()
    {
        Dog().TryPalette("999", out _).Should().BeFalse();
    }

    /// <summary>
    /// The asset itself is breedless — the name carries part, direction and frame, and the colour is
    /// not in it. This is the fact the whole mechanism rests on.
    /// </summary>
    [Fact]
    public void TheArtworkIsTheSameWhicheverBreedWearsIt()
    {
        Dog().TryResolve("dog_64_a_0_100", out NitroSpriteFrame frame).Should().BeTrue();

        frame.Width.Should().BeGreaterThan(0);
        frame.Height.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Half the pet's directions are drawn and half are the mirror of another: <c>dog.nitro</c>
    /// declares 0, 1, 2, 3 and 7 as their own frames, and 4, 5, 6 as <c>source</c> +
    /// <c>flipH</c> of 2, 1 and 0.
    /// </summary>
    /// <remarks>
    /// Which matters because the mirror is described twice on the way here. The client resolves the
    /// alias itself — <c>GraphicAssetCollection.defineAssets()</c> keeps the alias as the asset name
    /// and the <b>source</b> as the library name — so the payload carries the source and its own
    /// <c>flipH</c>, and the canvas turns it. If the manifest's flag were carried on the source too,
    /// the two would compose and a mirrored direction would come out facing the wrong way: the exact
    /// shape of "the pet is wrong in some directions". So: the sources are not flipped, the aliases
    /// are, and the payload never names an alias.
    /// </remarks>
    [Theory]
    [InlineData(4, 2)]
    [InlineData(5, 1)]
    [InlineData(6, 0)]
    public void AMirroredDirectionIsTurnedOnce(int direction, int source)
    {
        NitroAssetManifest dog = Dog();

        dog.TryResolve($"dog_64_a_{direction}_0", out NitroSpriteFrame alias).Should().BeTrue();
        alias.FlipH.Should().BeTrue("the manifest mirrors this direction rather than drawing it");

        dog.TryResolve($"dog_64_a_{source}_0", out NitroSpriteFrame drawn).Should().BeTrue();
        drawn
            .FlipH.Should()
            .BeFalse("this one is drawn, and it is the name the payload carries for both");

        alias.Width.Should().Be(drawn.Width);
        alias.Height.Should().Be(drawn.Height);
    }

    /// <summary>
    /// The directions that are drawn rather than mirrored, so a change to the pack that starts
    /// aliasing one of them is caught here rather than in a photo.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    public void ADrawnDirectionCarriesNoMirror(int direction)
    {
        Dog().TryResolve($"dog_64_a_{direction}_0", out NitroSpriteFrame frame).Should().BeTrue();

        frame.FlipH.Should().BeFalse();
        frame.Width.Should().BeGreaterThan(0);
    }
}
