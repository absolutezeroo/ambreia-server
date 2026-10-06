using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vortex.Dashboard.API.Api.Catalogue.Contracts;
using Vortex.Dashboard.API.Api.Hotel.Contracts;
using Vortex.Dashboard.API.Api.Platform.Contracts;
using Vortex.Dashboard.API.Api.Progression.Contracts;
using Vortex.Dashboard.API.Api.Safety.Contracts;
using Vortex.Dashboard.API.Hosting;
using Vortex.Dashboard.API.Operations.Hotel;
using Vortex.Dashboard.API.Operations.Platform.Contracts;
using Vortex.Observability.Configuration;
using Vortex.Observability.Runtime;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Moderation;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Xunit;

namespace Vortex.Dashboard.Tests.Hosting;

/// <summary>
/// Which routes tell OpenAPI what they answer with.
/// </summary>
/// <remarks>
/// <para>
/// Every dashboard handler returns <see cref="IResult" />, which carries no type. So a route's
/// response schema exists only if the mapping declared one, and a document without schemas
/// generates front-end types that are all <c>unknown</c> — the whole point of §11 lost with nothing
/// failing anywhere. This test is what makes "the API contract is the front end's source of truth"
/// a checkable claim rather than an intention.
/// </para>
/// <para>
/// It pins the subjects that have been converted, not a total. Declaring a response type is only
/// possible once a subject's reads return real types instead of <c>object</c>, so this list grows
/// one subject at a time and each addition is a deliberate line here.
/// </para>
/// </remarks>
public sealed class DashboardResponseSchemaTests
{
    /// <summary>Route to the type it must publish. One entry per converted read.</summary>
    private static readonly (string Route, Type Response)[] Declared =
    [
        ("/api/v1/polls", typeof(PollListResponse)),
        ("/api/v1/polls/question-types", typeof(PollQuestionTypeOptions)),
        ("/api/v1/polls/{pollId:int}", typeof(PollDetail)),
        ("/api/v1/polls/{pollId:int}/results", typeof(PollResults)),
        ("/api/v1/articles", typeof(ArticleListResponse)),
        ("/api/v1/articles/meta", typeof(ArticleFormMeta)),
        ("/api/v1/articles/images", typeof(ArticleImageBrowse)),
        ("/api/v1/articles/{articleId:int}", typeof(ArticleDetail)),
        ("/api/v1/wired/stats", typeof(WiredStats)),
        ("/api/v1/pets/stats", typeof(PetStats)),
        ("/api/v1/cfh/stats", typeof(CfhStats)),
        ("/api/v1/chatlogs", typeof(ChatlogPage)),
        ("/api/v1/songs", typeof(SongListResponse)),
        ("/api/v1/groups/stats", typeof(GroupStats)),
        ("/api/v1/social/stats", typeof(SocialStats)),
        ("/api/v1/bots", typeof(BotListResponse)),
        ("/api/v1/bots/stats", typeof(BotStats)),
        ("/api/v1/bots/{botId:int}", typeof(BotDetail)),
        ("/api/v1/hand-items", typeof(HandItemList)),
        ("/api/v1/forensics/audit", typeof(AuditPage)),
        ("/api/v1/forensics/moderation/stats", typeof(ModerationStats)),
        ("/api/v1/forensics/item-anomalies", typeof(ItemAnomalyScan)),
        ("/api/v1/catalog/purchases/stats", typeof(CatalogPurchaseStats)),
        ("/api/v1/economy/trends", typeof(EconomyTrends)),
        ("/api/v1/economy/ledger", typeof(EconomyLedgerPage)),
        ("/api/v1/economy/marketplace", typeof(MarketplaceSummary)),
        ("/api/v1/economy/subscriptions", typeof(ClubSubscriptions)),
        ("/api/v1/economy/extras", typeof(EconomyExtras)),
        ("/api/v1/rentable-spaces/activity", typeof(RentableSpaceAuditPage)),
        ("/api/v1/directory/groups", typeof(DirectoryPage)),
        ("/api/v1/directory/badges", typeof(CodeDirectoryPage)),
        ("/api/v1/directory/quest-campaigns", typeof(CodeDirectoryPage)),
        ("/api/v1/directory/players", typeof(PlayerDirectoryPage)),
        ("/api/v1/directory/rooms", typeof(RoomDirectoryPage)),
        ("/api/v1/directory/furniture", typeof(FurnitureDirectoryPage)),
        ("/api/v1/directory/avatars", typeof(AvatarBatch)),
        ("/api/v1/directory/search", typeof(DirectorySearch)),
        ("/api/v1/directory/entity/{id}", typeof(ItemProfile)),
        ("/api/v1/directory/players/{playerId:int}/profile", typeof(PlayerProfile)),
        ("/api/v1/directory/rooms/{roomId:int}", typeof(RoomTimeline)),
        ("/api/v1/monitoring/overview", typeof(DashboardOverview)),
        ("/api/v1/monitoring/packet-stats", typeof(PacketStats)),
        ("/api/v1/monitoring/infrastructure", typeof(InfrastructureHealthSnapshot)),
        ("/api/v1/monitoring/incidents", typeof(IncidentDetectionSnapshot)),
        ("/api/v1/monitoring/room-performance", typeof(RoomPerformanceSnapshot)),
        ("/api/v1/database/backups", typeof(BackupList)),
        ("/api/v1/gamedata", typeof(GamedataFileList)),
        ("/api/v1/gamedata/entries", typeof(GamedataEntryPage)),
        ("/api/v1/gamedata/languages", typeof(GamedataLanguageList)),
        ("/api/v1/mystery-box/colors", typeof(MysteryBoxColorOptions)),
        ("/api/v1/meta/endpoints", typeof(ApiRouteCatalog)),
        ("/api/v1/directory/rooms/active", typeof(ImmutableArray<RoomSummaryDto>)),
        (
            "/api/v1/directory/rooms/{roomId:int}/occupants",
            typeof(ImmutableArray<RoomOccupantSnapshot>)
        ),
        ("/api/v1/operations/console/commands", typeof(IReadOnlyList<ConsoleCommandInfo>)),
        ("/api/v1/operations/cfh/queue", typeof(ImmutableArray<CfhIssueQueueEntrySnapshot>)),
        ("/api/v1/operations/vouchers/{code}", typeof(VoucherSnapshot)),
        ("/api/v1/achievements", typeof(AchievementListResponse)),
        ("/api/v1/achievements/stats", typeof(AchievementStats)),
        ("/api/v1/achievements/resolutions", typeof(AchievementResolutions)),
        ("/api/v1/achievements/{achievementId:int}", typeof(AchievementDetail)),
        ("/api/v1/fishing", typeof(FishingContent)),
        ("/api/v1/fishing/activity", typeof(FishingActivity)),
        ("/api/v1/prize-pools", typeof(PrizePoolContent)),
        ("/api/v1/prize-pools/stats", typeof(PrizePoolStats)),
        ("/api/v1/player-rewards", typeof(PlayerRewardStats)),
        ("/api/v1/player-rewards/{playerId:int}", typeof(PlayerRewardDetail)),
        ("/api/v1/staff", typeof(StaffOverview)),
        ("/api/v1/staff/accounts", typeof(StaffAccountSearch)),
        ("/api/v1/targeted-offers", typeof(TargetedOfferList)),
        ("/api/v1/targeted-offers/stats", typeof(TargetedOfferStats)),
        ("/api/v1/targeted-offers/form-meta", typeof(TargetedOfferFormMeta)),
        ("/api/v1/targeted-offers/images", typeof(TargetedOfferImageList)),
        ("/api/v1/targeted-offers/{offerId:int}", typeof(TargetedOfferDetail)),
        ("/api/v1/config", typeof(ConfigList)),
        ("/api/v1/navigator/config", typeof(NavigatorSetup)),
        ("/api/v1/monitoring/inventory", typeof(DashboardInventory)),
        ("/api/v1/furniture/definitions", typeof(FurnitureDefinitionPage)),
        ("/api/v1/mystery-box", typeof(MysteryBoxContent)),
        ("/api/v1/mystery-box/stats", typeof(MysteryBoxStats)),
        ("/api/v1/quests", typeof(QuestList)),
        ("/api/v1/quests/stats", typeof(QuestStats)),
        ("/api/v1/quests/types", typeof(QuestTypeOptions)),
        ("/api/v1/quests/{questId:int}", typeof(QuestDetail)),
        ("/api/v1/community-goals", typeof(CommunityGoalList)),
        ("/api/v1/daily-tasks", typeof(DailyTaskList)),
        ("/api/v1/catalog/pages", typeof(CatalogPageList)),
        ("/api/v1/catalog/pages/{pageId:int}", typeof(CatalogPageDetail)),
        ("/api/v1/catalog/offers/{offerId:int}", typeof(CatalogOfferDetail)),
        ("/api/v1/catalog/currency-types", typeof(CatalogCurrencyList)),
        ("/api/v1/catalog/icon-template", typeof(CatalogIconTemplate)),
    ];

    [Fact]
    public void EveryConvertedReadPublishesItsResponseSchema()
    {
        IReadOnlyDictionary<string, Type[]> published = PublishedResponseTypes();

        foreach ((string route, Type response) in Declared)
        {
            published
                .Should()
                .ContainKey(route, "the route must still exist for its schema to mean anything");

            published[route]
                .Should()
                .Contain(
                    response,
                    "{0} answers with {1}, and a generator can only know that from the document",
                    route,
                    response.Name
                );
        }
    }

    [Fact]
    public void ALookupThatCanAnswerNothingSaysSoInTheDocument()
    {
        // Both poll lookups return 404 for an id that is nobody. A generated client that does not
        // know this treats the 404 body as the poll itself.
        IReadOnlyDictionary<string, int[]> statuses = PublishedStatusCodes();

        statuses["/api/v1/polls/{pollId:int}"].Should().Contain(StatusCodes.Status404NotFound);
        statuses["/api/v1/polls/{pollId:int}/results"]
            .Should()
            .Contain(StatusCodes.Status404NotFound);
        statuses["/api/v1/articles/{articleId:int}"]
            .Should()
            .Contain(StatusCodes.Status404NotFound);
    }

    private static IReadOnlyDictionary<string, Type[]> PublishedResponseTypes() =>
        Endpoints()
            .ToDictionary(
                endpoint => Pattern(endpoint),
                endpoint =>
                    endpoint
                        .Metadata.OfType<IProducesResponseTypeMetadata>()
                        .Select(metadata => metadata.Type)
                        .Where(type => type is not null)
                        .Select(type => type!)
                        .ToArray(),
                StringComparer.Ordinal
            );

    private static IReadOnlyDictionary<string, int[]> PublishedStatusCodes() =>
        Endpoints()
            .ToDictionary(
                endpoint => Pattern(endpoint),
                endpoint =>
                    endpoint
                        .Metadata.OfType<IProducesResponseTypeMetadata>()
                        .Select(metadata => metadata.StatusCode)
                        .ToArray(),
                StringComparer.Ordinal
            );

    private static string Pattern(Endpoint endpoint) =>
        "/" + ((RouteEndpoint)endpoint).RoutePattern.RawText!.TrimStart('/');

    private static List<Endpoint> Endpoints()
    {
        WebApplication app = BuildApp();

        DashboardEndpoints.MapReadApi(app, () => DateTime.UnixEpoch);
        // Two more entry points, because three of the reads this test pins are not under
        // MapReadApi: a handful of GETs live beside the writes they belong to (the CFH queue, a
        // voucher lookup), and /meta/endpoints describes the routes rather than reading the hotel.
        // They publish a schema like any other read, and a page types itself from each.
        DashboardEndpoints.MapOperations(app);
        DashboardEndpoints.MapMeta(app);

        return
        [
            .. ((IEndpointRouteBuilder)app)
                .DataSources.SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>(),
        ];
    }

    /// <summary>
    /// The same container the host builds, with factories that throw: route building never resolves
    /// a service, it only asks whether the container knows the type.
    /// </summary>
    private static WebApplication BuildApp()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

        foreach (Type serviceType in DashboardWebHost.ForwardedServiceTypes)
        {
            builder.Services.AddSingleton(
                serviceType,
                _ =>
                    throw new InvalidOperationException(
                        $"{serviceType.Name} was resolved; this test only builds routes."
                    )
            );
        }

        builder.Services.AddSingleton<IOptions<ObservabilityConfig>>(
            Options.Create(new ObservabilityConfig())
        );

        return builder.Build();
    }
}
