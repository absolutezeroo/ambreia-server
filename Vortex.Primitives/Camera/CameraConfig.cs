using System.Collections.Immutable;

namespace Vortex.Primitives.Camera;

/// <summary>
/// What the room camera charges and what it will accept, served live from <c>IServerConfigGrain</c>.
/// </summary>
/// <remarks>
/// <para>
/// The client asks for the prices once per room entry (<c>RequestCameraConfiguration</c>) and keeps
/// the three numbers on its camera widget for the visit — <c>CameraWidgetHandler.onInitCameraEvent</c>
/// assigns them and nothing else ever sets them. An unanswered request therefore leaves the widget on
/// its own defaults rather than on the hotel's prices.
/// </para>
/// <para>
/// The defaults are the values an official hotel was observed serving: two credits to take a photo,
/// no duckets, ten duckets to publish. They are prices, not content, which is what makes them config
/// rather than a table.
/// </para>
/// <para>
/// The limits below are the camera's whole defence. <c>RenderRoom</c> carries one zlib blob built by
/// the client, and the client's own <c>isSendable()</c> returns <c>true</c> unconditionally — it caps
/// nothing, so every ceiling on that payload is this server's to impose. They are config rather than
/// constants because the right numbers depend on the hotel's room sizes and hardware, and an operator
/// tightening them under load should not need a deploy.
/// </para>
/// </remarks>
public static class CameraConfig
{
    public const string CreditPriceKey = "camera.credit_price";
    public const string DucketPriceKey = "camera.ducket_price";
    public const string PublishDucketPriceKey = "camera.publish_ducket_price";

    /// <summary>Ceiling on the compressed blob as it arrives, refused before a byte is inflated.</summary>
    public const string MaxPayloadBytesKey = "camera.max_payload_bytes";

    /// <summary>Ceiling on the <em>inflated</em> scene. Enforced while inflating, never after: a zip
    /// bomb is only cheap to refuse before it has been expanded.</summary>
    public const string MaxSceneBytesKey = "camera.max_scene_bytes";

    public const string MaxPlanesKey = "camera.max_planes";
    public const string MaxSpritesKey = "camera.max_sprites";
    public const string MaxMasksPerPlaneKey = "camera.max_masks_per_plane";
    public const string MaxTextureColumnsPerPlaneKey = "camera.max_texture_columns_per_plane";

    /// <summary>How many photos one player may render per hour. The rate limit, not the price, is
    /// what stops a scripted client turning the renderer into the hotel's most expensive loop.</summary>
    public const string RendersPerHourKey = "camera.renders_per_hour";

    /// <summary>Seconds between two publishes by the same player. The client renders this as
    /// "wait N minutes" from the <c>secondsToWait</c> it is sent.</summary>
    public const string PublishCooldownSecondsKey = "camera.publish_cooldown_seconds";

    /// <summary>Competition entries per player per day. Exceeded, the client is told
    /// <c>too-many-submits</c>, which it has its own wording for.</summary>
    public const string CompetitionEntriesPerDayKey = "camera.competition_entries_per_day";

    /// <summary>
    /// How far the scene's own timestamp may sit from server time before it is refused. The seal on
    /// that timestamp carries no secret and is not authentication (see
    /// <see cref="CameraSceneSeal"/>); a freshness window is the one thing it can honestly buy.
    /// </summary>
    public const string TimestampSkewSecondsKey = "camera.timestamp_skew_seconds";

    /// <summary>
    /// The only url prefixes a sprite may name, separated by <c>;</c>.
    /// </summary>
    /// <remarks>
    /// A sprite's name is not always an asset key: the client substitutes <c>%image.library.url%</c>
    /// and <c>%group.badge.url%</c> into it, so a name beginning <c>http</c> is an image the renderer
    /// is expected to fetch — at an address the payload chose. That is a server-side request forgery
    /// unless the address is checked against a list the operator wrote, which is what this is.
    /// Empty by default: no external image is fetched until a hotel says which host serves them, and
    /// a sprite that fails the check is dropped exactly as the client drops its own past thirty.
    /// </remarks>
    public const string ExternalImagePrefixesKey = "camera.external_image_prefixes";

    /// <summary>
    /// Path prefix of the url handed back in <c>CameraStorageUrl</c>. The client prepends its own
    /// <c>stories.image_url_base</c> external variable to whatever it is given, so this is a relative
    /// path and the two have to agree.
    /// </summary>
    public const string UrlPathPrefixKey = "camera.url_path_prefix";

    /// <summary>
    /// Classname of the furniture a bought photo becomes. The hotel's own asset pack decides what
    /// that is called, so it is config: a hotel whose photo item is named differently should not
    /// need this rebuilt.
    /// </summary>
    public const string PhotoFurniClassNameKey = "camera.photo_furni_classname";

    /// <summary>
    /// Where the hotel's furniture bundles are served from, ending in a slash. The renderer joins a
    /// classname and <c>.nitro</c> to it, which is the url the client builds from
    /// <c>flash.dynamic.download.url</c> — there is no revision in that template, so the classname
    /// alone addresses a bundle. Empty means no furniture is drawn in a photo.
    /// </summary>
    public const string FurnitureAssetUrlKey = "camera.furniture_asset_url";

    /// <summary>
    /// Where pet and avatar bundles are served, ending in a slash — the client's
    /// <c>flash.client.url</c>, the versioned <c>gordon</c> directory. Pets are addressed by their
    /// type (<c>dog.nitro</c>) and avatar parts by their library (<c>hh_human_body.nitro</c>), which
    /// is why one url serves both.
    /// </summary>
    public const string AvatarAssetUrlKey = "camera.avatar_asset_url";

    /// <summary>
    /// The client's <c>figuremap.xml</c>. An avatar sprite names a part and not a library —
    /// <c>h_std_bd_1_2_0</c> is part type <c>bd</c>, part id <c>1</c> — and this is the only thing
    /// that says which bundle those live in.
    /// </summary>
    public const string FiguremapUrlKey = "camera.figuremap_url";

    /// <summary>
    /// How many asset bundles one photo may fetch.
    /// </summary>
    /// <remarks>
    /// A furniture name is checked against this hotel's own catalogue before anything is fetched, so
    /// it needs no budget. A pet and an avatar part cannot be: their bundle name comes out of the
    /// sprite name itself, so an invented name is an address. This is what stops a scene full of them
    /// becoming a burst against the asset host. A real room is a few dozen bundles.
    /// </remarks>
    public const string MaxBundleFetchesKey = "camera.max_bundle_fetches";

    /// <summary>Ceiling on either side of a rendered photo. Nothing in the payload bounds a
    /// coordinate, so this is what stands between a corner at a billion and a failed allocation.</summary>
    public const string MaxPhotoDimensionKey = "camera.max_photo_dimension";

    /// <summary>How many images one photo may fetch. The allowlist says where, this says how often.</summary>
    public const string MaxExternalFetchesKey = "camera.max_external_fetches";

    public const string ExternalTimeoutSecondsKey = "camera.external_timeout_seconds";

    public const string MaxExternalBytesKey = "camera.max_external_bytes";

    public const int DefaultCreditPrice = 2;
    public const int DefaultDucketPrice = 0;
    public const int DefaultPublishDucketPrice = 10;

    public const int DefaultMaxPayloadBytes = 512 * 1024;
    public const int DefaultMaxSceneBytes = 4 * 1024 * 1024;
    public const int DefaultMaxPlanes = 256;
    public const int DefaultMaxSprites = 4096;
    public const int DefaultMaxMasksPerPlane = 64;
    public const int DefaultMaxTextureColumnsPerPlane = 128;

    public const int DefaultRendersPerHour = 30;
    public const int DefaultPublishCooldownSeconds = 1800;
    public const int DefaultCompetitionEntriesPerDay = 3;
    public const int DefaultTimestampSkewSeconds = 900;

    public const string DefaultUrlPathPrefix = "photos/";

    /// <summary>The classname the official pack uses, and the one the logic binding pass found.</summary>
    public const string DefaultPhotoFurniClassName = "external_image_wallitem_photo";

    public const int DefaultMaxPhotoDimension = 1600;

    /// <summary>Thirty, the same number the client stops at before replacing the rest with a box.</summary>
    public const int DefaultMaxExternalFetches = 30;

    public const int DefaultExternalTimeoutSeconds = 5;
    public const int DefaultMaxExternalBytes = 1024 * 1024;

    /// <summary>Nothing. An operator opts external images in; they are not on by accident.</summary>
    public const string DefaultExternalImagePrefixes = "";

    /// <summary>Nothing. Every hotel serves its pack somewhere different.</summary>
    public const string DefaultFurnitureAssetUrl = "";

    public const string DefaultAvatarAssetUrl = "";

    public const string DefaultFiguremapUrl = "";

    /// <summary>Generous for a room, small enough that a crafted scene is not a burst.</summary>
    public const int DefaultMaxBundleFetches = 64;

    /// <summary>Every key of the group, for the one-round-trip batch resolve.</summary>
    public static readonly ImmutableArray<string> AllKeys =
    [
        CreditPriceKey,
        DucketPriceKey,
        PublishDucketPriceKey,
        MaxPayloadBytesKey,
        MaxSceneBytesKey,
        MaxPlanesKey,
        MaxSpritesKey,
        MaxMasksPerPlaneKey,
        MaxTextureColumnsPerPlaneKey,
        RendersPerHourKey,
        PublishCooldownSecondsKey,
        CompetitionEntriesPerDayKey,
        TimestampSkewSecondsKey,
        ExternalImagePrefixesKey,
        UrlPathPrefixKey,
        PhotoFurniClassNameKey,
        MaxPhotoDimensionKey,
        MaxExternalFetchesKey,
        ExternalTimeoutSecondsKey,
        MaxExternalBytesKey,
        FurnitureAssetUrlKey,
        AvatarAssetUrlKey,
        FiguremapUrlKey,
        MaxBundleFetchesKey,
    ];

    /// <summary>The three prices the client is told at room entry, and nothing else.</summary>
    public static readonly ImmutableArray<string> PriceKeys =
    [
        CreditPriceKey,
        DucketPriceKey,
        PublishDucketPriceKey,
    ];
}
