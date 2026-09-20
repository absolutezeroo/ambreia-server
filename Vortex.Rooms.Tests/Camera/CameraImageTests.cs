using FluentAssertions;
using SkiaSharp;
using Vortex.Rooms.Camera;
using Xunit;

namespace Vortex.Rooms.Tests.Camera;

/// <summary>
/// The small copy of a photo, which is the one that ends up on a wall.
/// </summary>
public sealed class CameraImageTests
{
    [Fact]
    public void APhotoBiggerThanTheLimit_ComesBackWithItsLongestSideAtTheLimit()
    {
        byte[] scaled = CameraImage.Downscale(Png(800, 400), 110)!;

        Size(scaled).Should().Be((110, 55));
    }

    [Fact]
    public void ATallPhoto_IsMeasuredOnItsHeight()
    {
        byte[] scaled = CameraImage.Downscale(Png(200, 1000), 110)!;

        Size(scaled).Should().Be((22, 110));
    }

    /// <summary>
    /// A shape that rounds to nothing still has to be an image: a surface with a zero side is an
    /// exception, not a small picture.
    /// </summary>
    [Fact]
    public void APhotoSoWideItRoundsToNothing_KeepsOnePixel()
    {
        byte[] scaled = CameraImage.Downscale(Png(1600, 1), 110)!;

        Size(scaled).Should().Be((110, 1));
    }

    /// <summary>Enlarging it would only cost bytes; the point is a picture that fits in a frame.</summary>
    [Fact]
    public void APhotoAlreadySmallEnough_IsHandedBackUntouched()
    {
        byte[] original = Png(64, 64);

        CameraImage.Downscale(original, 110).Should().Equal(original);
    }

    [Fact]
    public void SomethingThatIsNotAnImage_IsRefusedRatherThanThrown()
    {
        CameraImage.Downscale("not a png at all"u8, 110).Should().BeNull();
    }

    private static byte[] Png(int width, int height)
    {
        using SKBitmap bitmap = new(width, height);
        using SKCanvas canvas = new(bitmap);

        canvas.Clear(SKColors.CornflowerBlue);

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);

        return data.ToArray();
    }

    private static (int Width, int Height) Size(byte[] png)
    {
        using SKBitmap bitmap = SKBitmap.Decode(png);

        return (bitmap.Width, bitmap.Height);
    }
}
