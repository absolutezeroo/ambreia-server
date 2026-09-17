using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Vortex.Database.Seeds;
using Vortex.Progression.Achievements;
using Xunit;

namespace Vortex.Database.Tests.Achievements;

/// <summary>
/// How many levels an achievement may be seeded with, and under which name.
/// </summary>
/// <remarks>
/// <para>
/// The achievement window captions a badge through HabboLocalizationManager.getAchievementName,
/// which falls back from the per-level key <c>badge_name_ACH_&lt;Name&gt;&lt;Level&gt;</c> to the
/// level-less <c>badge_name_ACH_&lt;Name&gt;</c> whose value carries <c>%roman%</c>. A family with
/// the level-less key renders at any level; a family that only has <c>&lt;Name&gt;1</c> renders
/// level 2 as the literal key. Both seeds gave all 35 achievements five levels, so six families
/// shipped four unreachable levels each — <c>badge_name_ACH_AvatarLooks5</c>, on screen.
/// </para>
/// <para>
/// Nothing else catches this: the rows insert, the wire is well-formed, the badge is granted, and
/// the client draws a missing image under a raw key. The client's localization file is not in this
/// repository, so the six are frozen here instead, from
/// <c>external_flash_texts</c> (WIN63/AIR).
/// </para>
/// </remarks>
public sealed class AchievementSeedLevelTests
{
    /// <summary>Families whose only <c>badge_name_</c> key is level 1. No level-less key exists.</summary>
    private static readonly string[] SingleLevelFamilies =
    [
        "Motto",
        "AvatarLooks",
        "Name",
        "AvatarTags",
        "EmailVerification",
        "HappyHour",
    ];

    /// <summary>Badge codes the seed migrations insert, as (family, level).</summary>
    private static readonly IReadOnlyList<(string Family, int Level)> SeededBadges = ParseBadges();

    private static IReadOnlyList<(string, int)> ParseBadges()
    {
        string migrations = Path.Combine(RepositoryRoot(), "Vortex.Database", "Migrations");

        return
        [
            .. Directory
                .EnumerateFiles(migrations, "*_AddAchievements.cs")
                .Concat(
                    Directory.EnumerateFiles(migrations, "*_SeedMoreAchievementsFixCategories.cs")
                )
                .SelectMany(file =>
                    Regex.Matches(File.ReadAllText(file), @"""ACH_([A-Za-z]+)(\d+)""")
                )
                .Select(m => (m.Groups[1].Value, int.Parse(m.Groups[2].Value))),
        ];
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Vortex.Cloud.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Repository root (Vortex.Cloud.sln) not found.");
    }

    [Fact]
    public void TheSeedMigrationsParse()
    {
        SeededBadges.Should().HaveCountGreaterThan(100);
        SeededBadges.Select(b => b.Family).Should().Contain("AvatarLooks");
    }

    [Fact]
    public void EveryLevelPastTheFirstOfASingleLevelFamilyIsTakenBackByTheCapSeed()
    {
        string cap = SeedScripts.Read("achievement_client_level_caps.sql");

        // What the seeds inserted and what the cap takes back have to be the same six names: a
        // seventh family reduced to one level in the client would sit here unfixed.
        foreach (string family in SingleLevelFamilies)
        {
            SeededBadges
                .Where(b => b.Family == family)
                .Should()
                .NotBeEmpty($"'{family}' is a seeded achievement");

            cap.Should()
                .Contain(
                    $"'{family}'",
                    $"'{family}' has no badge_name key past level 1, so its extra levels must be dropped"
                );
        }
    }

    [Fact]
    public void EveryNameTheConsumersCreditIsASeededAchievement()
    {
        // The name is the only link between a trigger and a row: PlayerAchievementGrain resolves it
        // by name and logs a warning on a miss, so a mismatch is a bar that never moves and nothing
        // that fails. 'FriendCount' was seeded, 'FriendListSize' was credited, and accepting a
        // friend advanced nothing for two months.
        HashSet<string> seeded =
        [
            .. SeededBadges.Select(b => b.Family),
            // achievement_client_level_caps.sql renames it, and the badge codes follow.
            "FriendListSize",
        ];

        string[] credited =
        [
            AchievementNames.Login,
            AchievementNames.RoomEntry,
            AchievementNames.Motto,
            AchievementNames.AvatarLooks,
            AchievementNames.FriendListSize,
            AchievementNames.RoomDecoFurniCount,
            AchievementNames.RespectGiven,
            AchievementNames.RespectEarned,
        ];

        credited.Should().OnlyContain(name => seeded.Contains(name));
    }
}
