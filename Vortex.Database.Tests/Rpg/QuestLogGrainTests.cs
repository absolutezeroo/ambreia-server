using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Tests.Support;
using Vortex.Primitives.Events;
using Vortex.Primitives.Events.Rpg;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.Rpg.Characters.Grains;
using Vortex.Primitives.Rpg.Items;
using Vortex.Primitives.Rpg.Items.Enums;
using Vortex.Primitives.Rpg.Quests;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Rules;
using Vortex.Rpg.Quests.Grains;
using Vortex.Rpg.Quests.Objectives;
using Vortex.Rpg.Quests.Rewards;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Database.Tests.Rpg;

/// <summary>The quest log grain (spec M2a §3.3): saved on each change, rewards once, held objectives re-read.</summary>
public sealed class QuestLogGrainTests : IAsyncLifetime
{
    private const int Player = 7;

    private readonly List<string> _character = [];
    private readonly List<object> _published = [];
    private readonly List<ZoneFacts> _facts = [];
    private int _shards;

    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;

    public async Task InitializeAsync() => (_conn, _options) = await SqliteTestDb.OpenAsync();

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    private static QuestObjectiveDefinition Objective(
        string kind,
        int count,
        params (string Key, object Value)[] fields
    ) =>
        new()
        {
            Kind = kind,
            Count = count,
            Fields = fields.ToDictionary(
                f => f.Key,
                f => JsonSerializer.SerializeToElement(f.Value)
            ),
        };

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

    private static readonly QuestDefinition Wolves = new()
    {
        Id = "quest.wolves",
        Kind = "quest",
        Level = 1,
        Objectives = [Objective("kill", 2, ("monster", "monster.forest.wolf"))],
        Rewards =
        [
            Reward("xp", ("amount", 120)),
            Reward(
                "items",
                ("items", new[] { new { item = "item.weapon.rusted_sword", quantity = 1 } })
            ),
        ],
    };

    private static readonly QuestDefinition Shards = new()
    {
        Id = "quest.shards",
        Kind = "quest",
        Level = 1,
        Objectives =
        [
            Objective("collect", 3, ("item", "item.material.amber_shard"), ("consume", true)),
        ],
    };

    private static readonly QuestDefinition Badge = new()
    {
        Id = "quest.badge",
        Kind = "quest",
        Level = 1,
        Objectives = [Objective("badge", 1)],
    };

    /// <summary>A held kind the engine has never heard of: always held, it notes its hand-in.</summary>
    private sealed class BadgeObjective(List<string> handedIn) : IHeldObjectiveKind
    {
        public string Kind => "badge";

        public int Current(QuestObjectiveDefinition objective, QuestHoldings holdings) => 1;

        public Task HandInAsync(QuestObjectiveDefinition objective, IPlayerCharacterGrain character)
        {
            handedIn.Add(objective.Kind);

            return Task.CompletedTask;
        }

        public IEnumerable<string> Problems(
            QuestObjectiveDefinition objective,
            Vortex.Rpg.Content.Validation.ContentCheck check
        ) => [];
    }

    private readonly List<string> _handedIn = [];

    private static QuestSignal Wolf() =>
        new(
            QuestSignals.MonsterKilled,
            "camp",
            ImmutableDictionary<string, string>
                .Empty.Add("monster", "monster.forest.wolf")
                .Add("rank", "normal")
        );

    private IGrainFactory Grains() =>
        FakeProxy.Create<IGrainFactory>(call =>
        {
            Type grain = call.Method.GetGenericArguments()[0];

            if (grain == typeof(IPlayerCharacterGrain))
            {
                return FakeProxy.Create<IPlayerCharacterGrain>(inner =>
                {
                    switch (inner.Method.Name)
                    {
                        case nameof(IPlayerCharacterGrain.GainXpAsync):
                            _character.Add($"xp {inner.Args![0]}");

                            return Task.FromResult(XpGrantOutcome.Granted);
                        case nameof(IPlayerCharacterGrain.GrantItemsAsync):
                            _character.Add($"items {inner.Args![0]}");

                            return Task.FromResult<ItemGrantResult>(null!);
                        case nameof(IPlayerCharacterGrain.RemoveItemsAsync):
                            _character.Add($"remove {inner.Args![0]} {inner.Args[1]}");
                            _shards -= (int)inner.Args[1]!;

                            return Task.FromResult(ItemOutcome.Ok);
                        case nameof(IPlayerCharacterGrain.GetItemsAsync):
                            return Task.FromResult<CharacterItemSnapshot[]>(
                                _shards > 0
                                    ?
                                    [
                                        new CharacterItemSnapshot
                                        {
                                            DefinitionId = "item.material.amber_shard",
                                            Quantity = _shards,
                                            Container = ItemContainer.Bag,
                                        },
                                    ]
                                    : []
                            );
                        case nameof(IPlayerCharacterGrain.GetSummaryAsync):
                            return Task.FromResult<CharacterSummary?>(
                                new CharacterSummary
                                {
                                    ClassId = "class.mage",
                                    SpecializationId = "spec.mage.fire",
                                    Level = 3,
                                    Xp = 0,
                                }
                            );
                        default:
                            return null;
                    }
                });
            }

            return FakeProxy.Create<IPlayerPresenceGrain>(inner =>
            {
                if (inner.Method.Name == nameof(IPlayerPresenceGrain.OnZoneFactsChangedAsync))
                {
                    _facts.Add((ZoneFacts)inner.Args![0]!);
                }

                return Task.CompletedTask;
            });
        });

    private async Task<QuestLogGrain> ActivateAsync()
    {
        IRpgContent content = FakeProxy.Create<IRpgContent>(call =>
            call.Method.Name == nameof(IRpgContent.Get)
                ? (string)call.Args![0]! switch
                {
                    "quest.wolves" => Wolves,
                    "quest.shards" => Shards,
                    "quest.badge" => Badge,
                    _ => null,
                }
                : null
        );

        QuestLogGrain grain = GrainActivationContext.CreateWithIntegerKey<QuestLogGrain>(
            Player,
            new OptionsDbContextFactory(_options),
            content,
            new IQuestObjectiveRule[]
            {
                new KillObjective(),
                new CollectObjective(),
                new BadgeObjective(_handedIn),
            },
            new IQuestRewardKind[] { new XpReward(), new ItemsReward() },
            Grains(),
            FakeProxy.Create<IEventPublisher>(call =>
            {
                _published.Add(call.Args![0]!);

                return null;
            }),
            NullLogger<QuestLogGrain>.Instance
        );

        await grain.OnActivateAsync(CancellationToken.None);

        return grain;
    }

    [Fact]
    public async Task Accepting_ThenCounting_IsSavedAndTold()
    {
        QuestLogGrain grain = await ActivateAsync();

        (await grain.AcceptAsync(Wolves.Id)).Should().Be(QuestCodes.Ok);
        await grain.CountAsync(Wolf());

        QuestLogGrain again = await ActivateAsync();
        QuestEntrySnapshot entry = (await again.GetLogAsync()).Entries.Single();

        (entry.QuestId, entry.Status, entry.Progress.Single())
            .Should()
            .Be((Wolves.Id, QuestStatus.Active, 1));
        _published.OfType<QuestLogChangedEvent>().Should().HaveCount(2);
        ((QuestZoneFacts)_facts.Last()).States.Should().Contain(Wolves.Id, "active");
    }

    [Fact]
    public async Task CompletingTwice_GrantsOnce()
    {
        QuestLogGrain grain = await ActivateAsync();
        await grain.AcceptAsync(Wolves.Id);
        await grain.CountAsync(Wolf());
        await grain.CountAsync(Wolf());

        (await grain.CompleteAsync(Wolves.Id)).Should().Be(QuestCodes.Ok);
        (await grain.CompleteAsync(Wolves.Id)).Should().Be(QuestCodes.NotReady);

        _character.Should().Equal("xp 120", $"items quest:{Wolves.Id}:{Player}:1");
        (await (await ActivateAsync()).GetLogAsync())
            .Entries.Single()
            .Status.Should()
            .Be(QuestStatus.Done);
    }

    [Fact]
    public async Task CompletingAnActiveQuest_IsRefused()
    {
        QuestLogGrain grain = await ActivateAsync();
        await grain.AcceptAsync(Wolves.Id);

        (await grain.CompleteAsync(Wolves.Id)).Should().Be(QuestCodes.NotReady);
        _character.Should().BeEmpty();
    }

    [Fact]
    public async Task AcceptingACollectQuest_ReadsTheBagAtOnce()
    {
        _shards = 3;
        QuestLogGrain grain = await ActivateAsync();

        await grain.AcceptAsync(Shards.Id);

        (await grain.GetLogAsync()).Entries.Single().Status.Should().Be(QuestStatus.Ready);
    }

    [Fact]
    public async Task Completing_ReReadsHeldObjectives_AndConsumes()
    {
        _shards = 3;
        QuestLogGrain grain = await ActivateAsync();
        await grain.AcceptAsync(Shards.Id);

        _shards = 1;
        (await grain.CompleteAsync(Shards.Id))
            .Should()
            .Be(QuestCodes.NotReady, "sold since: the bag is read again");

        _shards = 3;
        await grain.RefreshHeldAsync();
        (await grain.CompleteAsync(Shards.Id)).Should().Be(QuestCodes.Ok);
        _character.Should().Contain("remove item.material.amber_shard 3");
    }

    [Fact]
    public async Task Abandoning_RemovesTheRow()
    {
        QuestLogGrain grain = await ActivateAsync();
        await grain.AcceptAsync(Wolves.Id);

        (await grain.AbandonAsync(Wolves.Id)).Should().Be(QuestCodes.Ok);

        (await (await ActivateAsync()).GetLogAsync()).Entries.Should().BeEmpty();
    }

    /// <summary>The hand-in asks each held kind what it does (collect takes its items): the grain names no kind.</summary>
    [Fact]
    public async Task HandingIn_TellsEveryHeldKind_EvenOneTheEngineDoesNotKnow()
    {
        QuestLogGrain grain = await ActivateAsync();
        await grain.AcceptAsync(Badge.Id);

        (await grain.CompleteAsync(Badge.Id)).Should().Be(QuestCodes.Ok);

        _handedIn.Should().Equal("badge");
    }
}
