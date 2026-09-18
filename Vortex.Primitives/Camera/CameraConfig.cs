using System.Collections.Immutable;

namespace Vortex.Primitives.Camera;

/// <summary>
/// What the room camera charges, served live from <c>IServerConfigGrain</c>.
/// </summary>
/// <remarks>
/// <para>
/// The client asks once per room entry (<c>RequestCameraConfiguration</c>) and keeps the three
/// numbers on its camera widget for the visit — <c>CameraWidgetHandler.onInitCameraEvent</c> assigns
/// them and nothing else ever sets them. An unanswered request therefore leaves the widget on its
/// own defaults rather than on the hotel's prices.
/// </para>
/// <para>
/// The defaults are the values an official hotel was observed serving: two credits to take a photo,
/// no duckets, ten duckets to publish. They are prices, not content, which is what makes them config
/// rather than a table.
/// </para>
/// </remarks>
public static class CameraConfig
{
    public const string CreditPriceKey = "camera.credit_price";
    public const string DucketPriceKey = "camera.ducket_price";
    public const string PublishDucketPriceKey = "camera.publish_ducket_price";

    public const int DefaultCreditPrice = 2;
    public const int DefaultDucketPrice = 0;
    public const int DefaultPublishDucketPrice = 10;

    /// <summary>Every key of the group, for the one-round-trip batch resolve.</summary>
    public static readonly ImmutableArray<string> AllKeys =
    [
        CreditPriceKey,
        DucketPriceKey,
        PublishDucketPriceKey,
    ];
}
