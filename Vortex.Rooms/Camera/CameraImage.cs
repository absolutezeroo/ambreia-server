using System;
using SkiaSharp;

namespace Vortex.Rooms.Camera;

/// <summary>
/// Making a photo smaller.
/// </summary>
/// <remarks>
/// Its own class rather than a method on the store, because the store's job is where bytes live and
/// this one's is what they are. Nothing here throws: a photo that cannot be scaled is answered with
/// null and served whole, which is worse-looking and better than a frame with nothing in it.
/// </remarks>
internal static class CameraImage
{
    /// <summary>
    /// Re-encodes <paramref name="png"/> with its longest side at <paramref name="maxSide"/>.
    /// </summary>
    /// <remarks>
    /// An image already that small is returned unchanged rather than enlarged — the point is a
    /// picture that fits in a frame, and blowing a small one up would only cost bytes.
    /// </remarks>
    /// <returns>The scaled PNG, or null when the input is not a decodable image.</returns>
    public static byte[]? Downscale(ReadOnlySpan<byte> png, int maxSide)
    {
        if (maxSide <= 0)
        {
            return null;
        }

        // Two steps, because SKBitmap.Decode(byte[]) does not answer "this is not an image" — it
        // creates a null codec and then throws an ArgumentNullException naming that codec, which
        // reads like a bug in the caller. SKCodec.Create returns null, which is the question asked.
        using SKData encoded = SKData.CreateCopy(png.ToArray());
        using SKCodec? codec = SKCodec.Create(encoded);

        if (codec is null)
        {
            return null;
        }

        using SKBitmap? source = SKBitmap.Decode(codec);

        if (source is null || source.Width <= 0 || source.Height <= 0)
        {
            return null;
        }

        int longest = Math.Max(source.Width, source.Height);

        if (longest <= maxSide)
        {
            return png.ToArray();
        }

        double scale = (double)maxSide / longest;

        // At least one pixel each way. A photo one pixel tall and sixteen hundred wide scales to a
        // height of zero, and a zero-sized surface is an exception rather than a small picture.
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));

        using SKBitmap scaled = new(width, height, source.ColorType, source.AlphaType);

        // Mitchell, not nearest: a room photo is a dense pixel-art scene and dropping three
        // quarters of its pixels by sampling produces a picture of the aliasing rather than of the
        // room.
        if (!source.ScalePixels(scaled, new SKSamplingOptions(SKCubicResampler.Mitchell)))
        {
            return null;
        }

        using SKImage image = SKImage.FromBitmap(scaled);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);

        return data.ToArray();
    }
}
