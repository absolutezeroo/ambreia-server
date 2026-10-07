using System.Collections.Generic;
using Vortex.Permissions;

namespace Vortex.Permissions.Moderation;

/// <summary>
/// Default CFH category/topic bootstrap, seeded only into a fresh (empty) catalog — see
/// <see cref="CfhCatalogSeederService"/>. <see cref="TopicSeed.BanPresetIndex"/> links a topic to
/// one of the <see cref="Vortex.Primitives.Permissions.SanctionPresetKind.Ban"/> presets seeded by
/// <see cref="SanctionPresetSeederService"/>, giving
/// CloseIssueDefaultActionMessageHandler/DefaultSanctionMessageHandler a duration to apply — the
/// admin dashboard is expected to be where this gets tuned to match real moderation policy.
/// </summary>
internal static class DefaultCfhCatalog
{
    /// <summary>Category holding the four topics the client's emergency report form hardcodes.</summary>
    public const string ClientEmergencyTopics = "Urgent";

    /// <param name="Id">
    /// Forced primary key, or null to let the database assign one. Only the four topics the client's
    /// own <c>emergency_help_request</c> layout hardcodes need this — see
    /// <see cref="ClientEmergencyTopics"/>.
    /// </param>
    public sealed record TopicSeed(
        string Name,
        string? Consequence,
        int? BanPresetIndex,
        int? Id = null
    );

    public sealed record CategorySeed(string Name, IReadOnlyList<TopicSeed> Topics);

    public static readonly IReadOnlyList<CategorySeed> All =
    [
        new CategorySeed(
            "Bullying",
            [
                new TopicSeed("Harassment", "Ban 1 day", BanPresetIndex: 1),
                new TopicSeed("Hate speech", "Ban 3 days", BanPresetIndex: 2),
            ]
        ),
        new CategorySeed(
            "Bad behaviour",
            [
                new TopicSeed("Inappropriate language", "Ban 2 hours", BanPresetIndex: 0),
                new TopicSeed("Inappropriate room or look", "Ban 1 day", BanPresetIndex: 1),
            ]
        ),
        new CategorySeed(
            "Scamming",
            [
                new TopicSeed("Trading scam", "Ban 1 week", BanPresetIndex: 3),
                new TopicSeed("Impersonation", "Ban 3 days", BanPresetIndex: 2),
            ]
        ),
        // The client's classic "Make a help request" form does not read its topics off the wire: the
        // shipped window layout hardcodes four radiobuttons whose names ARE the topic ids it will
        // send (121, 122, 123, 124), captioned from help.emergency.main.step.one.topic.<id>. A hotel
        // without rows at those ids has a report form that silently posts to a topic the server
        // cannot resolve, so these four are seeded by id rather than by autoincrement. The wording
        // mirrors the client's own captions so a moderator reads the same thing the reporter did.
        new CategorySeed(
            ClientEmergencyTopics,
            [
                new TopicSeed(
                    "Someone is being sexually explicit",
                    "Ban 1 week",
                    BanPresetIndex: 3,
                    Id: 121
                ),
                new TopicSeed(
                    "Someone is sharing personal details",
                    "Ban 3 days",
                    BanPresetIndex: 2,
                    Id: 122
                ),
                new TopicSeed(
                    "Someone is bullying another Habbo",
                    "Ban 3 days",
                    BanPresetIndex: 2,
                    Id: 123
                ),
                new TopicSeed(
                    "Someone is being threatening or dangerous",
                    "Ban 1 week",
                    BanPresetIndex: 3,
                    Id: 124
                ),
            ]
        ),
        new CategorySeed(
            "Other",
            [
                new TopicSeed("Bug abuse", "Ban 1 week", BanPresetIndex: 3),
                new TopicSeed("Spam or advertising", "Ban 2 hours", BanPresetIndex: 0),
                new TopicSeed("Other", Consequence: null, BanPresetIndex: null),
            ]
        ),
    ];
}
