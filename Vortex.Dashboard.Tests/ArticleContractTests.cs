using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vortex.Dashboard.API.Api.Hotel;
using Vortex.Dashboard.API.Infrastructure;
using Vortex.Database.Context;
using Vortex.Database.Entities.Web;
using Vortex.Observability.Configuration;
using Xunit;

namespace Vortex.Dashboard.Tests;

/// <summary>
/// The JSON the articles read surface puts on the wire, field by field.
/// </summary>
/// <remarks>
/// <para>
/// Written before giving those responses explicit types: the contract the editor depends on exists
/// only as the shape some anonymous literals happen to produce, and nothing declares or checks it.
/// </para>
/// <para>
/// The image browser is the interesting one. It answers three different shapes today — a refusal
/// carrying <c>error</c>, an empty answer with no paging fields at all, and a full page — and the
/// front end papers over the difference with <c>?? 0</c> and <c>?? 60</c> at each read. This pins
/// all three, so unifying them can be shown to leave every value the page computes unchanged.
/// </para>
/// </remarks>
public sealed class ArticleContractTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task The_list_names_every_field_the_page_reads()
    {
        JsonElement list = Serialize(
            await Reads(await SeededAsync())
                .ArticlesAsync(new NameValueCollection(), CancellationToken.None)
        );

        Names(list).Should().BeEquivalentTo(["total", "page", "pageSize", "count", "items"]);

        Names(list.GetProperty("items")[0])
            .Should()
            .BeEquivalentTo([
                "id",
                "slug",
                "category",
                "status",
                "scheduled",
                "publishAt",
                "pinned",
                "author",
                "title",
                "languages",
            ]);
    }

    [Fact]
    public async Task The_detail_names_every_field_the_editor_reads()
    {
        JsonElement detail = Serialize(
            await Reads(await SeededAsync()).ArticleDetailAsync(1, CancellationToken.None)
        );

        Names(detail)
            .Should()
            .BeEquivalentTo([
                "id",
                "slug",
                "category",
                "status",
                "publishAt",
                "pinned",
                "author",
                "translations",
            ]);

        Names(detail.GetProperty("translations")[0])
            .Should()
            .BeEquivalentTo(["lang", "title", "summary", "body", "headerImage", "thumbnail"]);
    }

    [Fact]
    public async Task The_form_metadata_names_every_field_the_form_reads()
    {
        JsonElement meta = Serialize(
            await Reads(await SeededAsync()).ArticleFormMetaAsync(CancellationToken.None)
        );

        Names(meta)
            .Should()
            .BeEquivalentTo([
                "categories",
                "languages",
                "imageBase",
                "imageDirectories",
                "blockTypes",
            ]);

        Names(meta.GetProperty("categories")[0])
            .Should()
            .BeEquivalentTo(["id", "code", "labels", "sortOrder", "enabled"]);

        Names(meta.GetProperty("languages")[0])
            .Should()
            .BeEquivalentTo(["id", "code", "label", "isDefault", "enabled", "sortOrder"]);
    }

    [Fact]
    public void The_image_browser_answers_what_the_picker_computes_from()
    {
        // No asset root configured, an unknown directory, and a page of a real directory are three
        // different outcomes, and today they carry different fields: only the last has page and
        // pageSize at all. The picker reads `total ?? 0`, `items ?? []` and `pageSize ?? 60`, so
        // what it must be able to rely on is the VALUES those reads produce, not which keys are
        // present. That is what this pins, and it stays true whether the shapes are unified or not.
        foreach (NameValueCollection query in Queries())
        {
            JsonElement images = JsonSerializer.SerializeToElement(
                Reads(NewOptions()).ArticleImages(query),
                Wire
            );

            Total(images).Should().Be(0, "no asset root is configured in a test");
            Items(images).Should().Be(0);
            PageSize(images).Should().Be(60, "the picker falls back to exactly this");
        }
    }

    /// <summary>What the page's <c>images.data?.total ?? 0</c> evaluates to.</summary>
    private static int Total(JsonElement images) =>
        images.TryGetProperty("total", out JsonElement total) ? total.GetInt32() : 0;

    private static int Items(JsonElement images) =>
        images.TryGetProperty("items", out JsonElement items) ? items.GetArrayLength() : 0;

    private static int PageSize(JsonElement images) =>
        images.TryGetProperty("pageSize", out JsonElement size) ? size.GetInt32() : 60;

    private static IEnumerable<NameValueCollection> Queries() =>
        [
            new(),
            new() { ["dir"] = "not-a-directory" },
            new() { ["dir"] = "web_promo", ["page"] = "3" },
        ];

    private static IEnumerable<string> Names(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name);

    /// <summary>
    /// Takes the value, not the task that produces it. Awaiting a caller's task inside a helper is
    /// what VSTHRD003 is about, and there was nothing to gain from it: the caller has to await the
    /// read anyway, and the serialisation itself is synchronous.
    /// </summary>
    private static JsonElement Serialize<T>(T read) =>
        JsonSerializer.SerializeToElement(read, Wire);

    /// <summary>One article with one translation, one category and one language: enough to reach
    /// every branch of the three database reads.</summary>
    private static async Task<DbContextOptions<VortexDbContext>> SeededAsync()
    {
        DbContextOptions<VortexDbContext> options = NewOptions();
        await using VortexDbContext db = new(options);

        db.WebArticleCategories.Add(
            new WebArticleCategoryEntity
            {
                Id = 5,
                Code = "news",
                LabelJson = "{}",
            }
        );
        db.WebLanguages.Add(
            new WebLanguageEntity
            {
                Id = 7,
                Code = "en",
                Label = "English",
                IsDefault = true,
            }
        );
        db.WebArticles.Add(
            new WebArticleEntity
            {
                Id = 1,
                Slug = "hello",
                CategoryId = 5,
                Status = WebArticleStatus.Published,
                AuthorName = "staff",
            }
        );
        db.WebArticleTranslations.Add(
            new WebArticleTranslationEntity
            {
                Id = 2,
                ArticleId = 1,
                LanguageCode = "en",
                Title = "Hello",
                Summary = "A summary",
                BodyJson = "[]",
            }
        );

        await db.SaveChangesAsync();
        return options;
    }

    private static ArticleReads Reads(DbContextOptions<VortexDbContext> options) =>
        new(
            new TestContextFactory(options),
            new DashboardAssetUrls(Options.Create(new ObservabilityConfig())),
            Options.Create(new ObservabilityConfig())
        );

    private static DbContextOptions<VortexDbContext> NewOptions() =>
        new DbContextOptionsBuilder<VortexDbContext>()
            .UseInMemoryDatabase($"article-contract-{Guid.NewGuid():N}")
            .Options;

    private sealed class TestContextFactory(DbContextOptions<VortexDbContext> options)
        : IDbContextFactory<VortexDbContext>
    {
        public VortexDbContext CreateDbContext() => new(options);
    }
}
