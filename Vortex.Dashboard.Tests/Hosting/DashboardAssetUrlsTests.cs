using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Observability.Configuration;
using Xunit;

namespace Vortex.Dashboard.Tests.Hosting;

/// <summary>
/// Resolving the image templates against <see cref="ObservabilityConfig.AssetBaseUrl"/>.
/// </summary>
/// <remarks>
/// The templates used to name a host each, which meant nine copies of the same origin and a
/// deployment that had to restate all nine. They are relative now and one key says where the hotel's
/// pictures live — and every way that can go wrong is silent: an unprefixed template asks the
/// dashboard's own origin and 404s, a doubled slash 404s, and a CSP built from the raw templates
/// blocks pictures the page has already built the URLs for. None of it fails a build.
/// </remarks>
public sealed class DashboardAssetUrlsTests
{
    private const string Figure = "hd-180-1.ch-255-66";

    [Fact]
    public void ARelativeTemplate_ResolvesAgainstTheBase()
    {
        DashboardAssetUrls urls = Build(
            "https://client.vortex-hotel.online",
            furni: "/dcr/hof_furni/icons/{name}_icon.png"
        );

        urls.FurniIcon("chair")
            .Should()
            .Be("https://client.vortex-hotel.online/dcr/hof_furni/icons/chair_icon.png");
    }

    [Fact]
    public void NoBase_LeavesTheTemplateRelative()
    {
        // Local development: the dashboard serves the asset pack itself off this same origin, so a
        // relative template is already correct and must not be rewritten.
        DashboardAssetUrls urls = Build("", furni: "/hotel-assets/dcr/icons/{name}_icon.png");

        urls.FurniIcon("chair").Should().Be("/hotel-assets/dcr/icons/chair_icon.png");
    }

    [Fact]
    public void AnAbsoluteTemplate_IsLeftAlone()
    {
        // The escape hatch: one picture served from somewhere else stays served from there.
        DashboardAssetUrls urls = Build(
            "https://client.vortex-hotel.online",
            furni: "https://cdn.example.com/icons/{name}_icon.png"
        );

        urls.FurniIcon("chair").Should().Be("https://cdn.example.com/icons/chair_icon.png");
    }

    [Fact]
    public void ATrailingSlashOnTheBase_DoesNotDoubleUp()
    {
        DashboardAssetUrls urls = Build(
            "https://client.vortex-hotel.online/",
            furni: "/dcr/{name}_icon.png"
        );

        urls.FurniIcon("chair")
            .Should()
            .Be("https://client.vortex-hotel.online/dcr/chair_icon.png");
    }

    [Fact]
    public void TheCsp_AllowsTheBaseOrigin()
    {
        // Built from the RAW templates this list would be empty, and the browser would block every
        // image whose URL the same class had just built.
        DashboardAssetUrls urls = Build(
            "https://client.vortex-hotel.online",
            furni: "/dcr/{name}_icon.png",
            avatar: "/habbo-imaging/avatarimage?figure={figure}"
        );

        urls.ImgSrcOrigins.Should()
            .ContainSingle()
            .Which.Should()
            .Be("https://client.vortex-hotel.online");
    }

    [Fact]
    public void NoBase_NeedsNoCspOrigin()
    {
        // Relative templates are same-origin, which 'self' already covers.
        DashboardAssetUrls urls = Build("", furni: "/hotel-assets/dcr/{name}_icon.png");

        urls.ImgSrcOrigins.Should().BeEmpty();
    }

    [Fact]
    public void TheAvatarTemplate_IsResolvedToo()
    {
        // The imager templates carry a query string, which is the half a naive prefix breaks.
        DashboardAssetUrls urls = Build(
            "https://client.vortex-hotel.online",
            avatar: "/habbo-imaging/avatarimage?figure={figure}&headonly=1"
        );

        urls.AvatarImage(Figure)
            .Should()
            .Be(
                "https://client.vortex-hotel.online/habbo-imaging/avatarimage?figure=hd-180-1.ch-255-66&headonly=1"
            );
    }

    [Fact]
    public void AGarment_IsAskedForAsOneSegment_WithNobodyWearingIt()
    {
        // The whole point of the garment picker: a figure of one part, which the imaging server
        // draws alone and crops. Lending a model here -- as the hand-item preview rightly does --
        // would put the same mannequin down three thousand rows and hide the thing being picked.
        DashboardAssetUrls urls = Build(
            "https://client.vortex-hotel.online",
            figure: "/habbo-imaging/clothing/{figure}.png"
        );

        urls.FigureSetImage("sh", 290)
            .Should()
            .Be("https://client.vortex-hotel.online/habbo-imaging/clothing/sh-290.png");
    }

    [Fact]
    public void NoGarmentTemplate_IsNoPicture_RatherThanAFabricatedUrl()
    {
        // The picker stays usable without it: a set id is a number an operator can still type.
        Build("https://client.vortex-hotel.online").FigureSetImage("sh", 290).Should().BeNull();
    }

    private static DashboardAssetUrls Build(
        string baseUrl,
        string furni = "",
        string avatar = "",
        string figure = ""
    ) =>
        new(
            Options.Create(
                new ObservabilityConfig
                {
                    AssetBaseUrl = baseUrl,
                    FurniIconUrlTemplate = furni,
                    AvatarImageUrlTemplate = avatar,
                    FigureImageUrlTemplate = figure,
                }
            )
        );
}
