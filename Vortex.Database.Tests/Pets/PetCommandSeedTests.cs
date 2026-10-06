using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Vortex.Database.Seeds;
using Xunit;

namespace Vortex.Database.Tests.Pets;

/// <summary>
/// A spoken order is resolved to a command id from <c>pet_command_names</c> -- which is the client's
/// own numbering -- and then looked up in <c>pet_commands</c> by that same id. The two therefore have
/// to agree, and they did not: commands were seeded 0=Sit, 1=Stand against a bundle that says
/// 0=Free, 1=Sit, 8=Stand, so telling a pet to sit made it stand and Nest matched nothing at all.
/// </summary>
public sealed class PetCommandSeedTests
{
    private const int Monsterplant = 16;

    /// <summary>The ids a pet asset declares. An unknown posture resolves to standing, silently.</summary>
    private static readonly string[] DeclaredPostures =
    [
        "std",
        "beg",
        "bnd",
        "ded",
        "eat",
        "jmp",
        "lay",
        "pla",
        "rdy",
        "scr",
        "sit",
        "snf",
        "spk",
        "mv",
    ];

    private sealed record Command(int Id, string Posture);

    private static readonly IReadOnlyDictionary<int, string> Names = ParseNames();
    private static readonly IReadOnlyList<Command> Commands = ParseCommands();

    private static IReadOnlyDictionary<int, string> ParseNames() =>
        Regex
            .Matches(SeedScripts.Read("pet_command_names.sql"), @"\((\d+),\s*'([^']+)'\)")
            .ToDictionary(m => int.Parse(m.Groups[1].Value), m => m.Groups[2].Value);

    private static IReadOnlyList<Command> ParseCommands() =>
        [
            .. Regex
                .Matches(
                    SeedScripts.Read("pet_commands.sql"),
                    @"^\s*SELECT\s+(\d+)(?:\s+AS command)?,\s*\d+(?:\s+AS level_required)?,\s*'([a-z]*)'",
                    RegexOptions.Multiline
                )
                .Select(m => new Command(int.Parse(m.Groups[1].Value), m.Groups[2].Value)),
        ];

    [Fact]
    public void BothSeedsParse()
    {
        Names.Should().NotBeEmpty();
        Commands.Should().NotBeEmpty();
    }

    [Fact]
    public void EveryCommandIdIsOneTheClientKnows()
    {
        Commands
            .Select(c => c.Id)
            .Should()
            .OnlyContain(
                id => Names.ContainsKey(id),
                "the id is resolved from the client's own word list, so an id it never sends is dead config"
            );
    }

    /// <summary>
    /// The anchors of the collision. If these three drift again, sitting makes a pet stand.
    /// </summary>
    [Theory]
    [InlineData(1, "Sit", "sit")]
    [InlineData(8, "Stand", "std")]
    [InlineData(2, "Down", "lay")]
    public void TheWordAndThePostureAgree(int id, string name, string posture)
    {
        Names[id].Should().Be(name);
        Commands.Should().Contain(c => c.Id == id && c.Posture == posture);
    }

    [Fact]
    public void EveryPostureIsOneThePetAssetsDeclare()
    {
        Commands
            .Where(c => c.Posture.Length > 0)
            .Select(c => c.Posture)
            .Should()
            .OnlyContain(
                p => DeclaredPostures.Contains(p),
                "an unknown posture falls back to standing and the trick is never seen"
            );
    }

    [Fact]
    public void TheNestAndEatErrandsAreSeeded()
    {
        Commands.Should().Contain(c => c.Id == 13, "Nest walks the pet to its nest");
        Commands.Should().Contain(c => c.Id == 43, "Eat stays a posture command");
    }

    [Fact]
    public void TheMonsterplantObeysNothing()
    {
        SeedScripts
            .Read("pet_commands.sql")
            .Should()
            .NotMatchRegex(
                @"SELECT\s+16\s+UNION",
                "a monsterplant is rooted -- it cannot carry out an order"
            );

        Monsterplant.Should().Be(16);
    }
}
