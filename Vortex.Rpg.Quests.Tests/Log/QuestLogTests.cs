using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Vortex.Primitives.Rpg.Quests;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Rules;
using Vortex.Rpg.Quests.Log;
using Vortex.Rpg.Quests.Objectives;
using Xunit;
using static Vortex.Rpg.Quests.Tests.Support.Pieces;

namespace Vortex.Rpg.Quests.Tests.Log;

/// <summary>The log's rules (spec M2a §3.3): states, counters, the cap, and what it ignores.</summary>
public sealed class QuestLogTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    private static QuestDefinition Quest(string id, params QuestObjectiveDefinition[] objectives) =>
        new()
        {
            Id = id,
            Kind = "quest",
            Level = 1,
            Objectives = [.. objectives],
        };

    private static readonly QuestDefinition Wolves = Quest(
        "quest.wolves",
        Objective("kill", 3, ("monster", "monster.forest.wolf"))
    );
    private static readonly QuestDefinition Shards = Quest(
        "quest.shards",
        Objective("collect", 5, ("item", "item.material.amber_shard"))
    );

    private static readonly Dictionary<string, QuestDefinition> Content = new()
    {
        [Wolves.Id] = Wolves,
        [Shards.Id] = Shards,
    };

    private static readonly Dictionary<string, IQuestObjectiveRule> Kinds = new()
    {
        ["kill"] = new KillObjective(),
        ["collect"] = new CollectObjective(),
    };

    private static QuestLog Log(params QuestEntry[] entries) =>
        new(
            new QuestBook(
                id => Content.GetValueOrDefault(id),
                kind => Kinds.GetValueOrDefault(kind)
            ),
            entries
        );

    private static QuestSignal Wolf() =>
        Signal(
            QuestSignals.MonsterKilled,
            "camp",
            ("monster", "monster.forest.wolf"),
            ("rank", "normal")
        );

    private static QuestHoldings Holding(int shards) =>
        new(ImmutableDictionary<string, int>.Empty.Add("item.material.amber_shard", shards), 1);

    [Fact]
    public void Accepting_PutsTheQuestActive_WithZeroProgress()
    {
        QuestLog log = Log();

        log.Accept(Wolves.Id, Now).Should().Be(QuestCodes.Ok);

        log.StatusOf(Wolves.Id).Should().Be(QuestStatus.Active);
        log.Entries.Single().Progress.Should().Equal(0);
    }

    [Fact]
    public void AQuestAlreadyTaken_OrUnknown_IsNotOfferable()
    {
        QuestLog log = Log();
        log.Accept(Wolves.Id, Now);

        log.Accept(Wolves.Id, Now).Should().Be(QuestCodes.NotOfferable);
        log.Accept("quest.nope", Now).Should().Be(QuestCodes.NotOfferable);
    }

    [Fact]
    public void TheLog_HoldsTwentyQuestsAtMost()
    {
        Dictionary<string, QuestDefinition> many = Enumerable
            .Range(0, 21)
            .ToDictionary(
                i => $"quest.q{i}",
                i => Quest($"quest.q{i}", Objective("kill", 1, ("monster", "monster.forest.wolf")))
            );
        QuestLog log = new(
            new QuestBook(id => many.GetValueOrDefault(id), kind => Kinds.GetValueOrDefault(kind)),
            []
        );

        for (int i = 0; i < QuestLog.MaxActive; i++)
        {
            log.Accept($"quest.q{i}", Now).Should().Be(QuestCodes.Ok);
        }

        log.Accept("quest.q20", Now).Should().Be(QuestCodes.LogFull);
    }

    [Fact]
    public void ACounter_StopsAtItsCount_AndTheQuestTurnsReady()
    {
        QuestLog log = Log();
        log.Accept(Wolves.Id, Now);

        for (int i = 0; i < 5; i++)
        {
            log.Count(Wolf());
        }

        log.Entries.Single().Progress.Should().Equal(3);
        log.StatusOf(Wolves.Id).Should().Be(QuestStatus.Ready);
        log.CanComplete(Wolves.Id).Should().Be(QuestCodes.Ok);
    }

    [Fact]
    public void ASignalThatMatchesNothing_ChangesNothing() =>
        Log(
                new QuestEntry
                {
                    QuestId = Wolves.Id,
                    Status = QuestStatus.Active,
                    Progress = [0],
                    AcceptedAt = Now,
                }
            )
            .Count(Signal(QuestSignals.AreaEntered, "camp", ("area", "clearing")))
            .Should()
            .BeFalse();

    [Fact]
    public void ADoneQuest_IsNotCounted()
    {
        QuestLog log = Log(
            new QuestEntry
            {
                QuestId = Wolves.Id,
                Status = QuestStatus.Done,
                Progress = [3],
                AcceptedAt = Now,
                CompletedAt = Now,
            }
        );

        log.Count(Wolf()).Should().BeFalse();
        log.CanComplete(Wolves.Id).Should().Be(QuestCodes.NotReady);
    }

    [Fact]
    public void AHeldObjectiveDropping_SendsTheQuestBackToActive()
    {
        QuestLog log = Log();
        log.Accept(Shards.Id, Now);

        log.Hold(Holding(5)).Should().BeTrue();
        log.StatusOf(Shards.Id).Should().Be(QuestStatus.Ready);

        log.Hold(Holding(2)).Should().BeTrue();
        log.StatusOf(Shards.Id).Should().Be(QuestStatus.Active);
        log.Entries.Single().Progress.Should().Equal(2);
        log.HasHeld.Should().BeTrue();
    }

    [Fact]
    public void MarkingDone_ThenAbandoning()
    {
        QuestLog log = Log();
        log.Accept(Wolves.Id, Now);

        log.Abandon(Wolves.Id).Should().Be(QuestCodes.Ok);
        log.StatusOf(Wolves.Id).Should().Be(QuestStatus.None);
        log.Abandon(Wolves.Id).Should().Be(QuestCodes.NotInLog);

        log.Accept(Wolves.Id, Now);
        for (int i = 0; i < 3; i++)
        {
            log.Count(Wolf());
        }

        log.MarkDone(Wolves.Id, Now);
        log.StatusOf(Wolves.Id).Should().Be(QuestStatus.Done);
        log.Abandon(Wolves.Id).Should().Be(QuestCodes.NotInLog, "a done quest stays done");
        log.Accept(Wolves.Id, Now).Should().Be(QuestCodes.NotOfferable, "not repeatable in M2a");
    }

    [Fact]
    public void AQuestMissingFromTheContent_IsKeptAndIgnored()
    {
        QuestLog log = Log(
            new QuestEntry
            {
                QuestId = "quest.gone",
                Status = QuestStatus.Active,
                Progress = [1],
                AcceptedAt = Now,
            }
        );

        log.Count(Wolf()).Should().BeFalse();
        log.Hold(Holding(5)).Should().BeFalse();
        log.CanComplete("quest.gone").Should().Be(QuestCodes.NotReady);
        log.StatusOf("quest.gone").Should().Be(QuestStatus.Active);
    }

    [Fact]
    public void ProgressSavedWithFewerObjectives_IsWidenedToTheContent()
    {
        QuestLog log = Log(
            new QuestEntry
            {
                QuestId = Wolves.Id,
                Status = QuestStatus.Active,
                Progress = [],
                AcceptedAt = Now,
            }
        );

        log.Count(Wolf()).Should().BeTrue();
        log.Entries.Single().Progress.Should().Equal(1);
    }
}
