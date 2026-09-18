using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vortex.Database.Context;
using Vortex.Database.Entities.Collectibles;
using Vortex.WebApi.Configuration;
using Vortex.WebApi.Http;

namespace Vortex.WebApi.Hosting;

/// <summary>
/// What a wallet, an explorer or a marketplace reads about one Relic.
/// </summary>
/// <remarks>
/// <para>
/// This is the other end of <c>VortexCollectible</c>'s <c>tokenURI</c>: the contract answers
/// <c>&lt;base&gt;&lt;tokenId&gt;</c> and the token id IS the hotel's asset id, so this route is
/// reached by concatenation and nothing else. Without it a Relic is a real NFT with no name and no
/// picture in every wallet that holds one.
/// </para>
/// <para>
/// Anonymous, and it has to be: whoever ends up holding a Relic may have no account here at all, and
/// a marketplace rendering it has no session by definition. Nothing it exposes is private — the
/// classname, the serial and the edition size are printed in the Collectors Guild to everyone.
/// </para>
/// </remarks>
internal static class WebApiEndpointsCollectibles
{
    /// <summary>
    /// The ERC-721 metadata schema, whose field names are NOT this hotel's to choose.
    /// </summary>
    /// <remarks>
    /// Every property is named explicitly because the API serialises camelCase by default and this
    /// document is read by software that expects the schema's own spelling — <c>trait_type</c> would
    /// ship as <c>traitType</c> and every attribute would be silently ignored by the wallet showing
    /// it. Nothing throws; the token just appears blank.
    /// </remarks>
    public sealed record TokenMetadata(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("image")] string? Image,
        [property: JsonPropertyName("attributes")] IReadOnlyList<TokenAttribute> Attributes,
        // The schema's link back to wherever the thing comes from, and the only one a Relic gets:
        // without it a wallet holding one shows a name, a picture and no way to find the hotel it
        // belongs to. Omitted rather than emitted empty when no site URL is configured — a null
        // `external_url` is a document with no link, an empty one is a document with a broken link.
        [property: JsonPropertyName("external_url")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            string? ExternalUrl
    );

    public sealed record TokenAttribute(
        [property: JsonPropertyName("trait_type")] string TraitType,
        [property: JsonPropertyName("value")] object Value
    );

    public static void MapCollectibles(WebApplication app, string tag)
    {
        app.MapGet(
                "/api/public/collectibles/{assetId:int}",
                async Task<Results<Ok<TokenMetadata>, NotFound<ApiErrorResponse>>> (
                    int assetId,
                    [FromServices] IDbContextFactory<VortexDbContext> dbContexts,
                    [FromServices] IOptions<WebApiConfig> options,
                    CancellationToken ct
                ) =>
                {
                    await using VortexDbContext db = await dbContexts
                        .CreateDbContextAsync(ct)
                        .ConfigureAwait(false);

                    NftAssetEntity? asset = await db
                        .NftAssets.AsNoTracking()
                        .FirstOrDefaultAsync(row => row.Id == assetId && row.DeletedAt == null, ct)
                        .ConfigureAwait(false);

                    if (asset is null)
                    {
                        return TypedResults.NotFound(new ApiErrorResponse("asset_not_found"));
                    }

                    // Served whether or not the Relic is currently on the chain. tokenURI only
                    // resolves for a token that exists, so in practice the caller is holding one —
                    // and refusing a Relic that has come home would blank out its history on every
                    // marketplace that ever listed it.
                    return TypedResults.Ok(Describe(asset, options.Value));
                }
            )
            .WithName("CollectibleMetadata")
            .WithSummary("ERC-721 metadata for one Relic, by its hotel asset id.")
            .WithTags(tag);
    }

    private static TokenMetadata Describe(NftAssetEntity asset, WebApiConfig config)
    {
        List<TokenAttribute> attributes =
        [
            new("Item", asset.ProductCode),
            new("Edition", asset.SerialNumber),
        ];

        // Zero is an open edition, and "0 of 0" says nothing. An absent attribute is how the schema
        // expresses "this one has no cap".
        if (asset.EditionSize > 0)
        {
            attributes.Add(new TokenAttribute("Edition size", asset.EditionSize));
        }

        string edition =
            asset.EditionSize > 0
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"#{asset.SerialNumber} of {asset.EditionSize}"
                )
                : string.Create(CultureInfo.InvariantCulture, $"#{asset.SerialNumber}");

        return new TokenMetadata(
            string.Create(CultureInfo.InvariantCulture, $"{asset.ProductCode} {edition}"),
            "A Relic minted in the Collectors Guild, converted from the piece of furniture it is "
                + "named after.",
            FurniIcon(asset.ProductCode, config),
            attributes,
            RelicPage(asset.Id, config)
        );
    }

    /// <summary>
    /// The Relic's page on the website, or <c>null</c> when no site URL is configured.
    /// </summary>
    /// <remarks>
    /// The hash is not a mistake: the site is a <c>svelte-spa-router</c> application and routes on
    /// the fragment, so <c>/habbo-nft/relic/3</c> with no <c>#</c> is a path its server has no route
    /// for. It is built here rather than with the id appended by the caller because the id it needs
    /// is the asset's, which is also the ERC-721 token id — one numbering, which is what lets both
    /// this URL and the metadata one be reached by concatenation.
    /// </remarks>
    private static string? RelicPage(int assetId, WebApiConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.PublicSiteUrl))
        {
            return null;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{config.PublicSiteUrl.TrimEnd('/')}/#/habbo-nft/relic/{assetId}"
        );
    }

    /// <summary>
    /// The furni's catalogue icon on the asset host, or <c>null</c> when no asset host is
    /// configured.
    /// </summary>
    /// <remarks>
    /// Null rather than a relative path: this URL is read by software that has never heard of this
    /// hotel and cannot resolve one. A metadata document with no image renders as a token with no
    /// picture; one with a broken image renders as a token with a broken picture, which is worse.
    ///
    /// <para>
    /// The <c>*count</c> suffix is dropped. Habbo names a multi-quantity item <c>waterbowl*4</c>
    /// while the icon asset is <c>waterbowl_icon.png</c>, so leaving it in resolves to nothing —
    /// the same rule <c>DashboardAssetUrls.FurniIcon</c> applies for the same reason.
    /// </para>
    /// </remarks>
    private static string? FurniIcon(string productCode, WebApiConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.AssetBaseUrl))
        {
            return null;
        }

        int star = productCode.IndexOf('*', System.StringComparison.Ordinal);
        string baseName = star >= 0 ? productCode[..star] : productCode;

        return $"{config.AssetBaseUrl.TrimEnd('/')}/dcr/hof_furni/icons/{baseName}_icon.png";
    }
}
