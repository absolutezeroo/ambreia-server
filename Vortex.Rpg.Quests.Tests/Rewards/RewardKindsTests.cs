using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.Rpg.Characters.Grains;
using Vortex.Primitives.Rpg.Items;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Validation;
using Vortex.Rpg.Quests.Rewards;
using Vortex.Rpg.Quests.Tests.Support;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Quests.Tests.Rewards;

/// <summary>Each shipped reward kind alone (spec M2a §3.3): what it asks the character for.</summary>
public sealed class RewardKindsTests
{
    private readonly List<string> _calls = [];

    private IPlayerCharacterGrain Character() =>
        FakeProxy.Create<IPlayerCharacterGrain>(call =>
        {
            switch (call.Method.Name)
            {
                case nameof(IPlayerCharacterGrain.GainXpAsync):
                    _calls.Add($"xp {call.Args![0]} {call.Args[1]}");

                    return Task.FromResult(XpGrantOutcome.Granted);
                case nameof(IPlayerCharacterGrain.GrantItemsAsync):
                    ItemGrantLine[] lines = (ItemGrantLine[])call.Args![1]!;
                    _calls.Add(
                        $"items {call.Args[0]} {string.Join(",", lines.Select(l => $"{l.DefinitionId}x{l.Quantity}"))}"
                    );

                    return Task.FromResult<ItemGrantResult>(null!);
                default:
                    return null;
            }
        });

    private static QuestRewardDefinition Reward(
        string kind,
        params (string Key, object Value)[] fields
    ) =>
        new()
        {
            Kind = kind,
            Fields = fields.ToDictionary(
                f => f.Key,
                f => JsonSerializer.SerializeToElement(f.Value)
            ),
        };

    [Fact]
    public async Task Xp_GoesThroughTheGain_NotTheOperatorGrant()
    {
        await new XpReward().GrantAsync(
            Reward("xp", ("amount", 120)),
            new QuestGrant(Character(), "quest.camp.wolves", "quest:quest.camp.wolves:7:0")
        );

        _calls.Should().Equal("xp 120 quest:quest.camp.wolves");
    }

    [Fact]
    public async Task Items_GoWithTheGrantKey()
    {
        QuestRewardDefinition reward = Reward(
            "items",
            ("items", new[] { new { item = "item.weapon.rusted_sword", quantity = 1 } })
        );

        await new ItemsReward().GrantAsync(
            reward,
            new QuestGrant(Character(), "quest.camp.wolves", "quest:quest.camp.wolves:7:1")
        );

        _calls.Should().Equal("items quest:quest.camp.wolves:7:1 item.weapon.rusted_swordx1");
    }

    [Fact]
    public void TheirFields_AreChecked()
    {
        ContentCheck check = new(
            new Dictionary<string, ContentDefinition>
            {
                ["item.material.amber_shard"] = TestContent.Shard,
            },
            _ => false
        );

        new XpReward().Problems(Reward("xp", ("amount", 0)), check).Should().NotBeEmpty();
        new XpReward().Problems(Reward("xp", ("amount", 50)), check).Should().BeEmpty();
        new ItemsReward()
            .Problems(
                Reward("items", ("items", new[] { new { item = "item.nope", quantity = 1 } })),
                check
            )
            .Should()
            .NotBeEmpty();
        new ItemsReward()
            .Problems(
                Reward(
                    "items",
                    ("items", new[] { new { item = "item.material.amber_shard", quantity = 0 } })
                ),
                check
            )
            .Should()
            .NotBeEmpty();
        new ItemsReward()
            .Problems(
                Reward(
                    "items",
                    ("items", new[] { new { item = "item.material.amber_shard", quantity = 2 } })
                ),
                check
            )
            .Should()
            .BeEmpty();
    }
}
