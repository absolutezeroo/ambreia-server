using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Options;
using Vortex.Observability.Configuration;
using Vortex.Primitives.Furniture.Enums;

namespace Vortex.Dashboard.API.Infrastructure;

/// <summary>
/// Single source of truth for building dashboard asset URLs — furniture/catalog icons, avatar heads
/// (player inspector) and guild badges — from the configurable templates in
/// <see cref="ObservabilityConfig"/>. Every dashboard surface that renders an asset goes through here
/// instead of re-deriving URLs, and <see cref="ImgSrcOrigins"/> feeds the dashboard CSP so those
/// images are allowed to load. A template left empty yields a <c>null</c> URL (the UI shows a generic
/// fallback icon) — nothing is ever fabricated.
/// </summary>
internal sealed class DashboardAssetUrls(IOptions<ObservabilityConfig> options)
{
    private readonly ObservabilityConfig _config = options.Value;

    /// <summary>
    /// Furniture icon by definition name (<c>{name}</c>). Habbo multi-quantity items are named
    /// <c>basename*count</c> (e.g. <c>waterbowl*4</c>), but the icon asset is the base name
    /// (<c>waterbowl_icon.png</c>) — so the <c>*count</c> suffix is dropped before resolving. Clean
    /// names are unaffected.
    /// </summary>
    public string? FurniIcon(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        int star = name.IndexOf('*', StringComparison.Ordinal);
        string baseName = star >= 0 ? name[..star] : name;

        return Build(_config.FurniIconUrlTemplate, "{name}", baseName);
    }

    /// <summary>Catalog page icon by icon id (<c>{id}</c>).</summary>
    public string? CatalogIcon(int iconId) =>
        Build(
            _config.CatalogIconUrlTemplate,
            "{id}",
            iconId.ToString(CultureInfo.InvariantCulture)
        );

    /// <summary>Avatar image (head) rendered from a player's figure string (<c>{figure}</c>).</summary>
    public string? AvatarImage(string? figure) =>
        Build(_config.AvatarImageUrlTemplate, "{figure}", figure);

    /// <summary>
    /// One garment on its own, with nothing wearing it.
    /// </summary>
    /// <remarks>
    /// A figure of a single segment, which the imaging server draws as that part alone on a
    /// transparent avatar-sized canvas. No model is lent, unlike <see cref="HandItemImage"/>: a hand
    /// item only exists in somebody's hand, but a garment is a picture in its own right, and a
    /// mannequin behind it would be the same body repeated down every row of the picker.
    /// <para>
    /// No colour is named. A set is coloured against a palette this does not read, and the filter
    /// this picture fills in is about the set, never the coloring.
    /// </para>
    /// </remarks>
    public string? FigureSetImage(string type, int setId) =>
        Build(
            _config.FigureImageUrlTemplate,
            "{figure}",
            $"{type}-{setId.ToString(CultureInfo.InvariantCulture)}"
        );

    /// <summary>Guild/group badge rendered from its badge code (<c>{badge}</c>).</summary>
    public string? GroupBadge(string? badge) =>
        Build(_config.GroupBadgeUrlTemplate, "{badge}", badge);

    /// <summary>
    /// Achievement/player badge image by badge code (<c>{badge}</c>) — the static file the client
    /// ships, not the composed guild badge <see cref="GroupBadge"/> renders.
    /// </summary>
    public string? BadgeImage(string? badgeCode) =>
        Build(_config.BadgeImageUrlTemplate, "{badge}", badgeCode);

    /// <summary>
    /// A hand item, drawn the only way the client ever draws one: an avatar holding it
    /// (<c>{item}</c>). <paramref name="figure"/> is whichever avatar the caller wants to lend —
    /// the dashboard uses a neutral default so the picture is about the item, not the model.
    /// </summary>
    public string? HandItemImage(int handItemId, string? figure = null)
    {
        string? withItem = Build(
            _config.HandItemImageUrlTemplate,
            "{item}",
            handItemId.ToString(CultureInfo.InvariantCulture)
        );

        return withItem is null ? null : SubstituteFigure(withItem, figure);
    }

    /// <summary>
    /// The effect template with the model already lent, so a form can preview an id <em>nobody owns
    /// yet</em> — which is exactly the id being granted. Same trick as
    /// <see cref="TargetedOfferImageTemplate"/>: the page substitutes the last placeholder itself.
    /// </summary>
    public string? EffectImageTemplate =>
        string.IsNullOrWhiteSpace(_config.AvatarEffectImageUrlTemplate)
            ? null
            : SubstituteFigure(Template(_config.AvatarEffectImageUrlTemplate)!, null);

    /// <summary>Same, for a hand item id that has no row yet.</summary>
    public string? HandItemImageTemplate =>
        string.IsNullOrWhiteSpace(_config.HandItemImageUrlTemplate)
            ? null
            : SubstituteFigure(Template(_config.HandItemImageUrlTemplate)!, null);

    /// <summary>
    /// The catalog icon template, for the icon picker that probes candidate ids with <c>&lt;img&gt;</c>
    /// load/error events because no manifest says which icons exist.
    /// </summary>
    public string? CatalogIconTemplate =>
        string.IsNullOrWhiteSpace(_config.CatalogIconUrlTemplate)
            ? null
            : Template(_config.CatalogIconUrlTemplate);

    /// <summary>The badge template, so a code typed before it is granted still previews.</summary>
    public string? BadgeImageTemplate =>
        string.IsNullOrWhiteSpace(_config.BadgeImageUrlTemplate)
            ? null
            : Template(_config.BadgeImageUrlTemplate);

    /// <summary>Fills in <c>{figure}</c>, lending the neutral model when the caller has none.</summary>
    private static string SubstituteFigure(string url, string? figure) =>
        url.Replace(
            "{figure}",
            Uri.EscapeDataString(
                string.IsNullOrWhiteSpace(figure) ? DefaultHandItemFigure : figure
            ),
            StringComparison.Ordinal
        );

    /// <summary>
    /// An avatar effect, drawn the only way it can be: on an avatar wearing it (<c>{effect}</c>).
    /// Pass the owner's own <paramref name="figure"/> where there is one — an effect on the player
    /// it belongs to is what the operator is actually checking.
    /// </summary>
    public string? EffectImage(int effectId, string? figure = null)
    {
        string? withEffect = Build(
            _config.AvatarEffectImageUrlTemplate,
            "{effect}",
            effectId.ToString(CultureInfo.InvariantCulture)
        );

        return withEffect is null ? null : SubstituteFigure(withEffect, figure);
    }

    /// <summary>Quest image by its <c>image_version</c> (<c>{version}</c>), which is the asset's own
    /// filename — an empty version means the client shows no picture either.</summary>
    public string? QuestImage(string? imageVersion) =>
        Build(_config.QuestImageUrlTemplate, "{version}", imageVersion);

    /// <summary>The model lent to hand-item previews: a plain avatar, so nothing about the figure
    /// competes with the item being held.</summary>
    private const string DefaultHandItemFigure =
        "hd-180-1.ch-255-66.lg-280-110.sh-305-62.ha-1012-110.hr-828-61";

    /// <summary>
    /// The raw targeted-offer image template (or null when unset) so the admin form can show the
    /// configured base and let the operator supply just a filename with a live preview, instead of
    /// pasting a whole URL. Storage stays a full URL on the wire — this only drives the form.
    /// </summary>
    public string? TargetedOfferImageTemplate =>
        string.IsNullOrWhiteSpace(_config.TargetedOfferImageUrlTemplate)
            ? null
            : Template(_config.TargetedOfferImageUrlTemplate);

    /// <summary>
    /// Where the hotel's asset pack sits on this machine, for the routes that serve it. Empty when
    /// unconfigured, which those routes read as "there is nothing to serve".
    /// </summary>
    public string LocalRoot => _config.AssetsLocalRoot;

    /// <summary>
    /// The asset host's <c>c_images</c> root, for the article editor's picture previews.
    /// </summary>
    /// <remarks>
    /// Derived from the targeted-offer template rather than configured separately, for two reasons:
    /// there is only ever one asset host, and a second key would be a second thing to get wrong; and
    /// deriving it means the origin is already in <see cref="ImgSrcOrigins"/>, so the CSP allows the
    /// previews without another edit. Null when no template is configured — the editor then takes a
    /// typed path and shows no preview.
    /// </remarks>
    public string? ArticleImageBase
    {
        get
        {
            string? template = TargetedOfferImageTemplate;
            int marker = template?.IndexOf("/c_images/", StringComparison.OrdinalIgnoreCase) ?? -1;

            return marker < 0 ? null : template![..(marker + "/c_images".Length)];
        }
    }

    /// <summary>
    /// Distinct http(s) host origins of every configured template, for the dashboard CSP
    /// <c>img-src</c>. Without this the browser would block cross-origin asset images.
    /// </summary>
    /// <remarks>
    /// Resolved through <see cref="Template"/> first, for the reason the whole class exists: with a
    /// base configured the origin is the base's, and the raw templates would contribute nothing —
    /// a CSP that allows only the dashboard's own origin and blocks every picture it just built.
    /// A template still relative after resolution yields no origin, and needs none: it is same-origin.
    /// </remarks>
    public IReadOnlyList<string> ImgSrcOrigins =>
        new[]
        {
            _config.FurniIconUrlTemplate,
            _config.CatalogIconUrlTemplate,
            _config.TargetedOfferImageUrlTemplate,
            _config.AvatarImageUrlTemplate,
            _config.GroupBadgeUrlTemplate,
            _config.BadgeImageUrlTemplate,
            _config.HandItemImageUrlTemplate,
            _config.QuestImageUrlTemplate,
            _config.AvatarEffectImageUrlTemplate,
            _config.FigureImageUrlTemplate,
        }
            .Select(Template)
            .Select(OriginOf)
            .Where(origin => origin is not null)
            .Select(origin => origin!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// A configured template with <see cref="ObservabilityConfig.AssetBaseUrl"/> applied: the single
    /// place a relative template becomes the URL a browser can actually fetch. Every read of a
    /// template goes through here — a raw <c>_config.*UrlTemplate</c> anywhere else is a picture that
    /// resolves against the dashboard's own origin and 404s.
    /// </summary>
    /// <remarks>
    /// Only templates starting with <c>/</c> are prefixed. An absolute one is already a complete URL,
    /// and an empty base leaves everything relative, which is the local-development case: the
    /// dashboard serves the asset pack itself from <see cref="LocalRoot"/> on this same origin.
    /// </remarks>
    private string? Template(string? template) =>
        string.IsNullOrWhiteSpace(_config.AssetBaseUrl)
        || string.IsNullOrWhiteSpace(template)
        || !template.StartsWith('/')
            ? template
            : _config.AssetBaseUrl.TrimEnd('/') + template;

    private string? Build(string? template, string placeholder, string? value)
    {
        if (string.IsNullOrWhiteSpace(template) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Escape the substituted value: placeholders sit in both path segments (icon name) and query
        // values (avatar figure), and a raw '&'/space would break the URL. Figure/badge/name chars
        // (alphanumeric, '.', '-', '_') are unreserved so this is a no-op for the common case.
        return Template(template)!
            .Replace(placeholder, Uri.EscapeDataString(value), StringComparison.Ordinal);
    }

    /// <summary>Host origin of a template, found by substituting a benign probe for every known
    /// placeholder so the URL parses, then taking its authority. Null if the template is empty or
    /// not an absolute http(s) URL.</summary>
    private static string? OriginOf(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return null;
        }

        string probe = template
            .Replace("{name}", "x", StringComparison.Ordinal)
            .Replace("{id}", "1", StringComparison.Ordinal)
            .Replace("{file}", "x", StringComparison.Ordinal)
            .Replace("{figure}", "x", StringComparison.Ordinal)
            .Replace("{badge}", "x", StringComparison.Ordinal)
            .Replace("{item}", "1", StringComparison.Ordinal)
            .Replace("{version}", "x", StringComparison.Ordinal)
            .Replace("{effect}", "1", StringComparison.Ordinal);

        return
            Uri.TryCreate(probe, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.GetLeftPart(UriPartial.Authority)
            : null;
    }

    /// <summary>
    /// The image for a catalogue product, which is a different asset per product type.
    /// </summary>
    /// <remarks>
    /// Lived as a private helper on the read service, and was wanted by two subjects the moment that
    /// service started being split — so it belongs here, on the class whose whole job is turning a
    /// thing into the URL of its picture, rather than being copied or pushed into a base class the
    /// two would then share for one method.
    /// </remarks>
    public string? ProductImage(int productType, string? furnitureName, string? extraParam)
    {
        if (productType == (int)ProductType.Badge)
        {
            return string.IsNullOrWhiteSpace(extraParam) || extraParam == "0"
                ? null
                : BadgeImage(extraParam);
        }

        if (
            productType == (int)ProductType.Effect
            && int.TryParse(extraParam, out int effectId)
            && effectId > 0
        )
        {
            return EffectImage(effectId);
        }

        return furnitureName is null ? null : FurniIcon(furnitureName);
    }
}
