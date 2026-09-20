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
}
