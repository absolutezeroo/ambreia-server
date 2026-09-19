using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Camera;
using Vortex.Primitives.Camera;
using Vortex.Primitives.Camera.Grains;
using Vortex.Primitives.Inventory.Grains;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Server;

namespace Vortex.Players.Grains;

/// <summary>
/// One player's camera. See <see cref="IPlayerCameraGrain"/> for why it has to exist at all.
/// </summary>
/// <remarks>
/// The only thing kept in memory is which photo the player is looking at, because that genuinely is
/// session state — the client's three confirmations carry no arguments and mean "the one you are
/// holding". Every allowance is counted from <c>camera_photos</c> instead, so a silo restart does
/// not hand every player a fresh publish cooldown.
/// </remarks>
internal sealed class PlayerCameraGrain(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IGrainFactory grainFactory,
    ICameraPhotoStore photoStore,
    ILogger<PlayerCameraGrain> logger
) : Grain, IPlayerCameraGrain
{
    /// <summary>Duckets, in the numbering the wallet and the catalog already use.</summary>
    private const int DucketActivityPointType = 0;

    private PlayerId SelfId => PlayerId.Parse((int)this.GetPrimaryKeyLong());

    /// <summary>The photo the three argument-less confirmations refer to. Null until one is taken.</summary>
    private string? _heldPhotoKey;

    public async Task<CameraRenderOutcome> RenderAsync(
        int roomId,
        ReadOnlyMemory<byte> payload,
        CancellationToken ct
    )
    {
        ImmutableDictionary<string, string> config = await grainFactory
            .GetServerConfigGrain()
            .GetManyAsync(CameraConfig.AllKeys)
            .ConfigureAwait(true);

        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);

        // The rate limit first, before a byte is inflated. Refusing early is the point of a rate
        // limit: a player past their allowance must not be able to make this server do the
        // expensive part anyway.
        int rendersPerHour = ServerConfigValues.GetInt(
            config,
            CameraConfig.RendersPerHourKey,
            CameraConfig.DefaultRendersPerHour
        );

        DateTime now = DateTime.UtcNow;
        DateTime hourAgo = now.AddHours(-1);

        int recent = await dbCtx
            .CameraPhotos.AsNoTracking()
            .CountAsync(
                p =>
                    p.PlayerEntityId == (int)SelfId
                    && p.CreatedAt >= hourAgo
                    && p.DeletedAt == null,
                ct
            )
            .ConfigureAwait(true);

        if (recent >= rendersPerHour)
        {
            return Refuse(CameraRenderRefusal.RateLimited);
        }

        CameraSceneDecodeResult decoded = CameraSceneDecoder.Decode(
            payload.Span,
            roomId,
            now,
            ReadLimits(config)
        );

        if (!decoded.Accepted)
        {
            return Refuse(decoded.Refusal);
        }

        CameraScene scene = decoded.Scene!;
        string photoKey = photoStore.NewPhotoKey();

        if (!await photoStore.SaveSceneAsync(photoKey, payload, ct).ConfigureAwait(true))
        {
            return Refuse(CameraRenderRefusal.StorageFailed);
        }

        dbCtx.CameraPhotos.Add(
            new CameraPhotoEntity
            {
                PhotoKey = photoKey,
                PlayerEntityId = SelfId,
                RoomEntityId = roomId > 0 ? roomId : null,
                SceneBytes = payload.Length,
                RejectedExternalSprites = scene.RejectedExternalSprites,
            }
        );

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

        if (scene.RejectedExternalSprites > 0)
        {
            // An honest client never produces one. Worth saying out loud, not worth refusing over.
            logger.LogWarning(
                "Camera photo {PhotoKey} for player {PlayerId} named {Count} image url(s) outside the allowlist.",
                photoKey,
                (int)SelfId,
                scene.RejectedExternalSprites
            );
        }

        _heldPhotoKey = photoKey;

        return CameraRenderOutcome.Accepted(
            photoStore.PublicPath(
                photoKey,
                ServerConfigValues.GetString(
                    config,
                    CameraConfig.UrlPathPrefixKey,
                    CameraConfig.DefaultUrlPathPrefix
                )
            )
        );
    }

    public async Task<bool> PurchaseHeldPhotoAsync(CancellationToken ct)
    {
        if (_heldPhotoKey is null)
        {
            return false;
        }

        ImmutableDictionary<string, string> config = await grainFactory
            .GetServerConfigGrain()
            .GetManyAsync(CameraConfig.AllKeys)
            .ConfigureAwait(true);

        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);

        CameraPhotoEntity? photo = await LoadHeldAsync(dbCtx, ct).ConfigureAwait(true);

        // Already bought is not a failure worth charging twice for. The client has no reply for it
        // either way, so the quiet no-op is also the honest one.
        if (photo is null || photo.PurchasedAt is not null)
        {
            return false;
        }

        int definitionId = await ResolvePhotoDefinitionIdAsync(dbCtx, config, ct)
            .ConfigureAwait(true);

        if (definitionId <= 0)
        {
            logger.LogError(
                "A photo was bought and no furniture definition is named by {Key}; nothing was charged.",
                CameraConfig.PhotoFurniClassNameKey
            );
            return false;
        }

        List<WalletDebitRequest> debits = BuildDebits(
            ServerConfigValues.GetInt(
                config,
                CameraConfig.CreditPriceKey,
                CameraConfig.DefaultCreditPrice
            ),
            ServerConfigValues.GetInt(
                config,
                CameraConfig.DucketPriceKey,
                CameraConfig.DefaultDucketPrice
            )
        );

        string stuffData = BuildPhotoStuffData(photo, config);

        WalletPurchaseResult<bool> result = await grainFactory
            .GetPlayerWalletGrain(SelfId)
            .ExecutePurchaseAsync(
                debits,
                async inner =>
                {
                    await grainFactory
                        .GetInventoryGrain(SelfId)
                        .GrantFurnitureWithLegacyStuffDataAsync(definitionId, stuffData, inner)
                        .ConfigureAwait(true);

                    return true;
                },
                logger,
                ct
            )
            .ConfigureAwait(true);

        if (!result.Succeeded)
        {
            return false;
        }

        photo.PurchasedAt = DateTime.UtcNow;
        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

        return true;
    }

    public async Task<CameraPublishOutcome> PublishHeldPhotoAsync(CancellationToken ct)
    {
        ImmutableDictionary<string, string> config = await grainFactory
            .GetServerConfigGrain()
            .GetManyAsync(CameraConfig.AllKeys)
            .ConfigureAwait(true);

        int cooldown = ServerConfigValues.GetInt(
            config,
            CameraConfig.PublishCooldownSecondsKey,
            CameraConfig.DefaultPublishCooldownSeconds
        );

        if (_heldPhotoKey is null)
        {
            // The client's only failure branch is "wait N minutes", so a refusal with nothing to
            // publish still has to name a number. The cooldown is the honest one.
            return CameraPublishOutcome.Wait(cooldown);
        }

        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);

        CameraPhotoEntity? photo = await LoadHeldAsync(dbCtx, ct).ConfigureAwait(true);

        if (photo is null)
        {
            return CameraPublishOutcome.Wait(cooldown);
        }

        // Publishing the same photo twice is the player pressing a button the client already hid.
        // Answering with the id they were given is idempotent and keeps their share link working.
        if (photo.PublishedAt is not null)
        {
            return CameraPublishOutcome.Published(photo.PhotoKey);
        }

        DateTime now = DateTime.UtcNow;
        DateTime threshold = now.AddSeconds(-cooldown);

        DateTime? lastPublished = await dbCtx
            .CameraPhotos.AsNoTracking()
            .Where(p =>
                p.PlayerEntityId == (int)SelfId && p.PublishedAt != null && p.DeletedAt == null
            )
            .OrderByDescending(p => p.PublishedAt)
            .Select(p => p.PublishedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(true);

        if (lastPublished is { } last && last > threshold)
        {
            return CameraPublishOutcome.Wait((int)Math.Ceiling((last - threshold).TotalSeconds));
        }

        int publishPrice = ServerConfigValues.GetInt(
            config,
            CameraConfig.PublishDucketPriceKey,
            CameraConfig.DefaultPublishDucketPrice
        );

        if (publishPrice > 0)
        {
            WalletPurchaseResult<bool> paid = await grainFactory
                .GetPlayerWalletGrain(SelfId)
                .ExecutePurchaseAsync(
                    BuildDebits(credits: 0, duckets: publishPrice),
                    _ => Task.FromResult(true),
                    logger,
                    ct
                )
                .ConfigureAwait(true);

            if (!paid.Succeeded)
            {
                return CameraPublishOutcome.Wait(cooldown);
            }
        }

        photo.PublishedAt = now;
        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

        return CameraPublishOutcome.Published(photo.PhotoKey);
    }

    public async Task<CameraCompetitionOutcome> SubmitHeldPhotoToCompetitionAsync(
        CancellationToken ct
    )
    {
        if (_heldPhotoKey is null)
        {
            return CameraCompetitionOutcome.Refused(string.Empty);
        }

        ImmutableDictionary<string, string> config = await grainFactory
            .GetServerConfigGrain()
            .GetManyAsync(CameraConfig.AllKeys)
            .ConfigureAwait(true);

        await using VortexDbContext dbCtx = await dbCtxFactory.CreateDbContextAsync(ct);

        CameraPhotoEntity? photo = await LoadHeldAsync(dbCtx, ct).ConfigureAwait(true);

        if (photo is null)
        {
            return CameraCompetitionOutcome.Refused(string.Empty);
        }

        if (photo.CompetitionSubmittedAt is not null)
        {
            // Already in. Saying so as a success keeps the screen consistent with what happened.
            return CameraCompetitionOutcome.Submitted();
        }

        DateTime now = DateTime.UtcNow;
        DateTime dayAgo = now.AddDays(-1);

        int entriesPerDay = ServerConfigValues.GetInt(
            config,
            CameraConfig.CompetitionEntriesPerDayKey,
            CameraConfig.DefaultCompetitionEntriesPerDay
        );

        int today = await dbCtx
            .CameraPhotos.AsNoTracking()
            .CountAsync(
                p =>
                    p.PlayerEntityId == (int)SelfId
                    && p.CompetitionSubmittedAt != null
                    && p.CompetitionSubmittedAt >= dayAgo
                    && p.DeletedAt == null,
                ct
            )
            .ConfigureAwait(true);

        if (today >= entriesPerDay)
        {
            return CameraCompetitionOutcome.Refused(CameraCompetitionOutcome.TooManySubmits);
        }

        photo.CompetitionSubmittedAt = now;
        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

        return CameraCompetitionOutcome.Submitted();
    }

    private Task<CameraPhotoEntity?> LoadHeldAsync(VortexDbContext dbCtx, CancellationToken ct) =>
        dbCtx
            .CameraPhotos.Where(p =>
                p.PhotoKey == _heldPhotoKey
                && p.PlayerEntityId == (int)SelfId
                && p.DeletedAt == null
            )
            .FirstOrDefaultAsync(ct);

    private CameraRenderOutcome Refuse(CameraRenderRefusal refusal)
    {
        // The client will only ever see an empty url. Everything that separates "too big" from "too
        // fast" from "this server's disk is full" lives here or nowhere.
        logger.LogDebug(
            "Camera render refused for player {PlayerId}: {Refusal}.",
            (int)SelfId,
            refusal
        );

        return CameraRenderOutcome.Refused(refusal);
    }

    private static CameraSceneLimits ReadLimits(IReadOnlyDictionary<string, string> config) =>
        new()
        {
            MaxPayloadBytes = ServerConfigValues.GetInt(
                config,
                CameraConfig.MaxPayloadBytesKey,
                CameraConfig.DefaultMaxPayloadBytes
            ),
            MaxSceneBytes = ServerConfigValues.GetInt(
                config,
                CameraConfig.MaxSceneBytesKey,
                CameraConfig.DefaultMaxSceneBytes
            ),
            MaxPlanes = ServerConfigValues.GetInt(
                config,
                CameraConfig.MaxPlanesKey,
                CameraConfig.DefaultMaxPlanes
            ),
            MaxSprites = ServerConfigValues.GetInt(
                config,
                CameraConfig.MaxSpritesKey,
                CameraConfig.DefaultMaxSprites
            ),
            MaxMasksPerPlane = ServerConfigValues.GetInt(
                config,
                CameraConfig.MaxMasksPerPlaneKey,
                CameraConfig.DefaultMaxMasksPerPlane
            ),
            MaxTextureColumnsPerPlane = ServerConfigValues.GetInt(
                config,
                CameraConfig.MaxTextureColumnsPerPlaneKey,
                CameraConfig.DefaultMaxTextureColumnsPerPlane
            ),
            TimestampSkewSeconds = ServerConfigValues.GetInt(
                config,
                CameraConfig.TimestampSkewSecondsKey,
                CameraConfig.DefaultTimestampSkewSeconds
            ),
            ExternalImagePrefixes = CameraExternalImagePolicy.ParsePrefixes(
                ServerConfigValues.GetString(
                    config,
                    CameraConfig.ExternalImagePrefixesKey,
                    CameraConfig.DefaultExternalImagePrefixes
                )
            ),
        };

    private static List<WalletDebitRequest> BuildDebits(int credits, int duckets)
    {
        List<WalletDebitRequest> debits = [];

        if (credits > 0)
        {
            debits.Add(
                new WalletDebitRequest
                {
                    CurrencyKind = new CurrencyKind { CurrencyType = CurrencyType.Credits },
                    Amount = credits,
                }
            );
        }

        if (duckets > 0)
        {
            debits.Add(
                new WalletDebitRequest
                {
                    CurrencyKind = new CurrencyKind
                    {
                        CurrencyType = CurrencyType.ActivityPoints,
                        // Activity point type 0 is duckets, the same number the catalog debits them
                        // by. The camera's widget calls them duckets and so does this.
                        ActivityPointType = DucketActivityPointType,
                    },
                    Amount = duckets,
                }
            );
        }

        return debits;
    }

    /// <summary>
    /// The stuff data a photo furniture carries.
    /// </summary>
    /// <remarks>
    /// <c>FurnitureExternalImageVisualization</c> parses this field as JSON and reads the image from
    /// <c>w</c>, falling back to <c>url</c>. The rest is what the photo widget shows around it: who
    /// took it and when.
    /// </remarks>
    private string BuildPhotoStuffData(
        CameraPhotoEntity photo,
        IReadOnlyDictionary<string, string> config
    )
    {
        string path = photoStore.PublicPath(
            photo.PhotoKey,
            ServerConfigValues.GetString(
                config,
                CameraConfig.UrlPathPrefixKey,
                CameraConfig.DefaultUrlPathPrefix
            )
        );

        long takenAt = new DateTimeOffset(photo.CreatedAt, TimeSpan.Zero).ToUnixTimeMilliseconds();

        return System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, string>
            {
                ["w"] = path,
                ["n"] = ((int)SelfId).ToString(CultureInfo.InvariantCulture),
                ["t"] = takenAt.ToString(CultureInfo.InvariantCulture),
            }
        );
    }

    private static async Task<int> ResolvePhotoDefinitionIdAsync(
        VortexDbContext dbCtx,
        IReadOnlyDictionary<string, string> config,
        CancellationToken ct
    )
    {
        string className = ServerConfigValues.GetString(
            config,
            CameraConfig.PhotoFurniClassNameKey,
            CameraConfig.DefaultPhotoFurniClassName
        );

        // Lowest id wins. furniture_definitions.name is not unique — the pack ships thousands of
        // duplicates — so this picks one deterministically rather than letting the row order decide
        // which photo frame a hotel gets today.
        return await dbCtx
            .FurnitureDefinitions.AsNoTracking()
            .Where(d => d.Name == className && d.DeletedAt == null)
            .OrderBy(d => d.Id)
            .Select(d => d.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(true);
    }
}
