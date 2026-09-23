using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using FluentAssertions;
using Vortex.Dashboard.API.Api.Catalogue.Contracts;
using Vortex.Dashboard.API.Api.Hotel.Contracts;
using Vortex.Dashboard.API.Api.Platform.Contracts;
using Vortex.Dashboard.API.Api.Progression.Contracts;
using Vortex.Dashboard.API.Api.Safety.Contracts;
using Vortex.Dashboard.API.Operations.Hotel;
using Vortex.Dashboard.API.Operations.Platform.Contracts;
using Vortex.Observability.Runtime;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Moderation;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Xunit;

namespace Vortex.Dashboard.Tests.Hosting;

/// <summary>
/// The front end's view of the API contract, derived from the C# types rather than typed again.
/// </summary>
/// <remarks>
/// <para>
/// The architecture rules ask that the API contract become the front end's source of truth, and
/// name the failure it replaces: a C# response and a JavaScript page each describing the same shape
/// by hand, agreeing until one of them changes. This generates <c>apiTypes.d.ts</c> from the
/// response records and fails when the committed file no longer matches — so a renamed field is a
/// red test here rather than an undefined at runtime there.
/// </para>
/// <para>
/// Generated, never hand-edited, in the same shape as <c>authorization-matrix.txt</c>: on failure it
/// writes the current version beside the expected one and names both paths, and the fix is to copy
/// it over and read the diff.
/// </para>
/// <para>
/// Only subjects whose reads return real types appear. A subject still returning <c>object</c> has
/// no shape to derive, which is why §10 comes before §11 and not the other way round.
/// </para>
/// </remarks>
public sealed class ApiTypeScriptContractTests
{
    private const string GENERATED_FILE = "Vortex.Dashboard.Web/src/lib/apiTypes.d.ts";

    /// <summary>
    /// One type per converted subject: the roots. Everything they reference is pulled in with them,
    /// so a nested record never has to be listed twice.
    /// </summary>
    private static readonly Type[] Roots =
    [
        typeof(PollListResponse),
        typeof(PollDetail),
        typeof(PollResults),
        typeof(PollQuestionTypeOptions),
        typeof(ArticleListResponse),
        typeof(ArticleDetail),
        typeof(ArticleFormMeta),
        typeof(ArticleImageBrowse),
        typeof(WiredStats),
        typeof(PetStats),
        typeof(CfhStats),
        typeof(ChatlogPage),
        typeof(SongListResponse),
        typeof(GroupStats),
        typeof(GuildDirectoryPage),
        typeof(GuildModeration),
        typeof(GuildThreadDetail),
        typeof(SocialStats),
        typeof(WordFilterWordList),
        typeof(WordFilterHitPage),
        typeof(WordFilterTestResult),
        typeof(BotListResponse),
        typeof(BotDetail),
        typeof(BotStats),
        typeof(HandItemList),
        typeof(AuditPage),
        typeof(ModerationStats),
        typeof(ItemAnomalyScan),
        typeof(CatalogPurchaseStats),
        typeof(EconomyTrends),
        typeof(EconomyLedgerPage),
        typeof(MarketplaceSummary),
        typeof(ClubSubscriptions),
        typeof(RentableSpaceAuditPage),
        typeof(EconomyExtras),
        typeof(DirectoryPage),
        typeof(CodeDirectoryPage),
        typeof(PlayerDirectoryPage),
        typeof(RoomDirectoryPage),
        typeof(FurnitureDirectoryPage),
        typeof(AvatarBatch),
        typeof(AchievementListResponse),
        typeof(AchievementDetail),
        typeof(AchievementStats),
        typeof(AchievementResolutions),
        typeof(HabbiconCollectionList),
        typeof(PlayerHabbicons),
        typeof(HabbiconSourceOptions),
        typeof(FishingContent),
        typeof(FishingActivity),
        typeof(PrizePoolContent),
        typeof(PrizePoolStats),
        typeof(PlayerRewardStats),
        typeof(PlayerRewardDetail),
        typeof(RewardTrackList),
        typeof(PlayerRewardTracks),
        typeof(RewardTrackActionOptions),
        typeof(RewardKindOptions),
        typeof(StaffOverview),
        typeof(StaffAccountSearch),
        typeof(CollectiblesOverview),
        typeof(TargetedOfferList),
        typeof(TargetedOfferDetail),
        typeof(TargetedOfferFormMeta),
        typeof(TargetedOfferImageList),
        typeof(TargetedOfferStats),
        typeof(ConfigList),
        typeof(BenchmarkState),
        typeof(NavigatorSetup),
        typeof(DashboardInventory),
        typeof(FurnitureDefinitionPage),
        typeof(MysteryBoxContent),
        typeof(MysteryBoxStats),
        typeof(QuestList),
        typeof(QuestDetail),
        typeof(QuestStats),
        typeof(QuestTypeOptions),
        typeof(CommunityGoalList),
        typeof(DailyTaskList),
        typeof(CatalogPageList),
        typeof(CatalogPageDetail),
        typeof(CatalogOfferDetail),
        typeof(CatalogCurrencyList),
        typeof(CatalogIconTemplate),
        typeof(DirectorySearch),
        typeof(PlayerProfile),
        typeof(ItemProfile),
        typeof(RoomTimeline),
        typeof(DashboardOverview),
        typeof(PacketStats),
        typeof(InfrastructureHealthSnapshot),
        typeof(IncidentDetectionSnapshot),
        typeof(RoomPerformanceSnapshot),
        typeof(BackupList),
        typeof(GamedataFileList),
        typeof(GamedataEntryPage),
        typeof(GamedataLanguageList),
        typeof(HotelViewConfig),
        typeof(MysteryBoxColorOptions),
        typeof(ApiRouteCatalog),
        typeof(RoomSummaryDto),
        typeof(RoomOccupantSnapshot),
        typeof(ConsoleCommandInfo),
        typeof(RunConsoleCommandResponse),
        typeof(CfhIssueQueueEntrySnapshot),
        typeof(CfhAppealSnapshot),
        typeof(VoucherSnapshot),
    ];

    [Fact]
    public void TheGeneratedFrontEndTypesAreWhatTheContractSays()
    {
        string expectedPath = Path.Combine(RepositoryRoot(), GENERATED_FILE);
        string actual = Generate();
        string expected = File.ReadAllText(expectedPath);

        if (Normalize(actual) == Normalize(expected))
        {
            return;
        }

        string actualPath = Path.Combine(AppContext.BaseDirectory, "apiTypes.actual.d.ts");
        File.WriteAllText(actualPath, actual);

        Assert.Fail(
            $"The C# response contracts and {GENERATED_FILE} disagree.\n"
                + $"  expected: {expectedPath}\n"
                + $"  actual:   {actualPath}\n"
                + "Copy the actual over the expected and read the diff: every line that moved is a "
                + "change the front end has to follow."
        );
    }

    private static string Generate()
    {
        StringBuilder output = new();
        output.AppendLine(
            "// Generated from the C# response contracts by ApiTypeScriptContractTests."
        );
        output.AppendLine("// Do not edit: change the records, run the test, copy what it writes.");
        output.AppendLine();

        foreach (Type type in Reachable().OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            if (Derived(type) is { Length: > 0 } derived)
            {
                string union = string.Join(" | ", derived.Select(a => a.DerivedType.Name));
                output.AppendLine($"export type {type.Name} = {union};");
                output.AppendLine();
                continue;
            }

            output.AppendLine($"export interface {type.Name} {{");

            if (Discriminator(type) is { } tag)
            {
                output.AppendLine(tag);
            }

            foreach (PropertyInfo property in type.GetProperties())
            {
                output.AppendLine($"  {Camel(property.Name)}: {TypeScriptFor(property)};");
            }

            output.AppendLine("}");
            output.AppendLine();
        }

        return output.ToString();
    }

    /// <summary>Every contract reachable from a root, so nesting is followed once and only once.</summary>
    private static HashSet<Type> Reachable()
    {
        HashSet<Type> found = [];
        Queue<Type> pending = new(Roots);

        while (pending.Count > 0)
        {
            Type type = pending.Dequeue();

            if (!IsContract(type) || !found.Add(type))
            {
                continue;
            }

            foreach (JsonDerivedTypeAttribute derived in Derived(type))
            {
                pending.Enqueue(derived.DerivedType);
            }

            foreach (PropertyInfo property in type.GetProperties())
            {
                pending.Enqueue(Unwrap(property.PropertyType));
            }
        }

        return found;
    }

    /// <summary>The shapes a polymorphic contract can take, or nothing when it is not one.</summary>
    private static JsonDerivedTypeAttribute[] Derived(Type type) =>
        [.. type.GetCustomAttributes<JsonDerivedTypeAttribute>(inherit: false)];

    /// <summary>
    /// The literal tag member a member of a union carries, or null for a type that is not one.
    /// </summary>
    /// <remarks>
    /// Read off the base rather than declared on the record, because that is where the serializer
    /// reads it too. Emitting it as a string literal is what makes the union discriminated on the
    /// front end: a page that has checked <c>kind</c> is narrowed to one shape by the compiler.
    /// </remarks>
    private static string? Discriminator(Type type)
    {
        Type? baseType = type.BaseType;

        if (
            baseType is null
            || Derived(baseType).FirstOrDefault(a => a.DerivedType == type)?.TypeDiscriminator
                is not string value
        )
        {
            return null;
        }

        string property =
            baseType.GetCustomAttribute<JsonPolymorphicAttribute>()?.TypeDiscriminatorPropertyName
            ?? "$type";

        return $"  {property}: \"{value}\";";
    }

    /// <summary>Where a type has to live to be part of the published contract.</summary>
    /// <remarks>
    /// <para>
    /// The namespaces outside <c>Api</c> hold live state the dashboard forwards rather than
    /// projects: the observability snapshots ARE the answer for <c>/infrastructure</c>,
    /// <c>/incidents</c> and <c>/room-performance</c>, and the grain snapshots are the answer for
    /// the room, moderation and voucher live reads. Copying them into records here would be a
    /// second description of one shape, which is the failure this file exists to prevent.
    /// </para>
    /// <para>
    /// Everything read out of the database still projects into a record under <c>Contracts</c>. A
    /// contract is not an entity, and an entity on the wire carries columns nobody asked for.
    /// </para>
    /// </remarks>
    private static readonly string[] ContractNamespaces =
    [
        "Vortex.Dashboard.API.Api",
        "Vortex.Dashboard.API.Operations",
        "Vortex.Observability.Runtime",
        "Vortex.Primitives.Catalog.Snapshots",
        "Vortex.Primitives.Moderation",
        "Vortex.Primitives.Orleans.Snapshots",
    ];

    private static bool IsContract(Type type) =>
        ContractNamespaces.Any(space =>
            type.Namespace?.StartsWith(space, StringComparison.Ordinal) == true
        );

    /// <summary>The type a collection or a nullable is carrying, or the type itself.</summary>
    private static Type Unwrap(Type type)
    {
        Type inner = Nullable.GetUnderlyingType(type) ?? type;

        if (inner.IsArray)
        {
            return inner.GetElementType()!;
        }

        // A dictionary is an IEnumerable of KeyValuePair, so it has to be answered before the
        // collection case below -- otherwise its element type is the pair and the front end gets a
        // KeyValuePair`2[] where the server sends an object.
        if (DictionaryValue(inner) is { } value)
        {
            return value;
        }

        if (inner.IsGenericType && typeof(IEnumerable).IsAssignableFrom(inner))
        {
            return inner.GetGenericArguments()[0];
        }

        return inner;
    }

    /// <summary>
    /// What a string-keyed dictionary carries, or null when the type is not one.
    /// </summary>
    /// <remarks>
    /// Only string keys: those are the ones System.Text.Json writes as an object, which is the only
    /// shape TypeScript's Record can describe. A dictionary keyed by anything else would serialise
    /// differently and is a shape no read here has needed.
    /// </remarks>
    private static Type? DictionaryValue(Type type)
    {
        if (!type.IsGenericType)
        {
            return null;
        }

        Type definition = type.GetGenericTypeDefinition();
        bool isDictionary =
            definition == typeof(Dictionary<,>)
            || definition == typeof(IDictionary<,>)
            || definition == typeof(IReadOnlyDictionary<,>);

        if (!isDictionary)
        {
            return null;
        }

        Type[] arguments = type.GetGenericArguments();

        return arguments[0] == typeof(string) ? arguments[1] : null;
    }

    private static string TypeScriptFor(PropertyInfo property)
    {
        Type type = property.PropertyType;
        bool nullable =
            Nullable.GetUnderlyingType(type) is not null
            || new NullabilityInfoContext().Create(property).ReadState == NullabilityState.Nullable;

        Type inner = Unwrap(type);
        bool dictionary = DictionaryValue(Nullable.GetUnderlyingType(type) ?? type) is not null;
        bool collection =
            !dictionary
            && (
                type.IsArray
                || (
                    type.IsGenericType
                    && typeof(IEnumerable).IsAssignableFrom(type)
                    && type != typeof(string)
                )
            );

        string name = Scalar(inner) ?? NameOfContract(inner, property);
        string shape =
            dictionary ? $"Record<string, {name}>"
            : collection ? $"{name}[]"
            : name;

        return nullable ? $"{shape} | null" : shape;
    }

    /// <summary>
    /// The interface name for a nested contract, or a failure naming what escaped.
    /// </summary>
    /// <remarks>
    /// Without this a type outside <see cref="ContractNamespaces"/> is written into the file as a
    /// bare name that nothing declares, and the front end gets a .d.ts that does not compile --
    /// from a test that passed. Failing here says which property reached out of the contract, which
    /// is the one thing needed to decide whether it should be projected or the namespace listed.
    /// </remarks>
    private static string NameOfContract(Type type, PropertyInfo property) =>
        IsContract(type)
            ? type.Name
            : throw new InvalidOperationException(
                $"{property.DeclaringType?.Name}.{property.Name} is a {type.Name}, which is not a "
                    + "contract: project it into a record under Contracts, or add its namespace to "
                    + "ContractNamespaces if it is live state the dashboard forwards as it is."
            );

    private static string? Scalar(Type type) =>
        type switch
        {
            _ when type == typeof(string) => "string",
            _ when type == typeof(bool) => "boolean",
            _ when type == typeof(int) || type == typeof(long) => "number",
            _ when type == typeof(double) || type == typeof(decimal) || type == typeof(float) =>
                "number",
            // An instant reaches the browser as the ISO string System.Text.Json writes.
            _ when type == typeof(DateTime) || type == typeof(DateTimeOffset) => "string",
            // No string-enum converter is configured, so an enum reaches the browser as its number.
            _ when type.IsEnum => "number",
            _ => null,
        };

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    private static string Normalize(string text) => text.ReplaceLineEndings("\n").TrimEnd() + "\n";

    /// <summary>Walks up from the test binaries until the solution file says this is the root.</summary>
    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (
            directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "Vortex.Cloud.sln"))
        )
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                "Vortex.Cloud.sln not found above the test output."
            );
    }
}
