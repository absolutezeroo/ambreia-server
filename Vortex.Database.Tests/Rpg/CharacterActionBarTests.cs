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
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Rpg;
using Vortex.Database.Tests.Support;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Vortex.Rpg.Characters.Configuration;
using Vortex.Rpg.Characters.Grains;
using Vortex.Rpg.Characters.Stats;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Stats;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Database.Tests.Rpg;

/// <summary>Spec M1b §9: the action bar is the character's — filled when a class is chosen and when
/// a skill is learned, set by the player within his known skills, kept in <c>action_bar</c>. The
/// profile a zone reads carries the class and the statuses handed back by the last zone.</summary>
public sealed class CharacterActionBarTests : IAsyncLifetime
{
    private const int Player = 7;
    private const string Strike = "skill:skill.warrior.heroic_strike";
    private const string Shout = "skill:skill.warrior.challenging_shout";

    private readonly List<object> _sent = [];
    private readonly ManualClock _clock = new(
        new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero)
    );
    private readonly Dictionary<string, ContentDefinition> _content = new()
    {
        ["class.warrior"] = CharacterContent.Warrior,
        ["class.classless"] = CharacterContent.Classless,
        ["spec.warrior.guardian"] = CharacterContent.Guardian,
        ["progression.default"] = CharacterContent.Progression,
        ["skill.warrior.heroic_strike"] = Skill("skill.warrior.heroic_strike", "class.warrior", 1),
        ["skill.warrior.challenging_shout"] = Skill(
            "skill.warrior.challenging_shout",
            "class.warrior",
            3
        ),
        ["skill.archer.aimed_shot"] = Skill("skill.archer.aimed_shot", "class.archer", 1),
    };

    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;

    public async Task InitializeAsync() => (_conn, _options) = await SqliteTestDb.OpenAsync();

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public async Task ChoosingAClass_FillsTheBarWithItsKnownSkills()
    {
        PlayerCharacterGrain grain = await ActivateAsync("class.classless", bar: null);

        await grain.ChooseClassAsync("class.warrior");

        (await StoredBarAsync()).Should().Equal(Strike, "", "", "", "", "");
    }

    [Fact]
    public async Task ChoosingAClass_PushesTheSkills()
    {
        PlayerCharacterGrain grain = await ActivateAsync("class.classless", bar: null);

        await grain.ChooseClassAsync("class.warrior");

        RpgSkillsMessageComposer skills = _sent.OfType<RpgSkillsMessageComposer>().Single();
        skills.Known.Should().Equal("skill.warrior.heroic_strike");
        skills.Bar.Should().Equal(Strike, "", "", "", "", "");
    }

    [Fact]
    public async Task ALevelUp_PlacesTheNewSkillInTheFirstEmptySlot()
    {
        PlayerCharacterGrain grain = await ActivateAsync("class.warrior", [Strike]);

        await grain.GainXpAsync(250, "test");

        _sent
            .OfType<RpgSkillsMessageComposer>()
            .Last()
            .Bar.Should()
            .Equal(Strike, Shout, "", "", "", "");
        await DeactivateAsync(grain);
        (await StoredBarAsync()).Should().Equal(Strike, Shout, "", "", "", "");
    }

    [Fact]
    public async Task SettingASlot_Persists_AndMovesASkillAlreadyPlaced()
    {
        PlayerCharacterGrain grain = await ActivateAsync("class.warrior", [Strike]);

        await grain.SetActionBarAsync(3, Strike);

        _sent
            .OfType<RpgSkillsMessageComposer>()
            .Single()
            .Bar.Should()
            .Equal("", "", "", Strike, "", "");
        await DeactivateAsync(grain);
        (await StoredBarAsync()).Should().Equal("", "", "", Strike, "", "");
    }

    [Theory]
    [InlineData("skill:skill.archer.aimed_shot", "not_learned")]
    [InlineData(Shout, "not_learned")]
    [InlineData("skill:skill.nope", "unknown_skill")]
    [InlineData("item:item.potion", "unknown_skill")]
    public async Task AnActionTheCharacterCannotUse_IsRefused(string action, string code)
    {
        PlayerCharacterGrain grain = await ActivateAsync("class.warrior", [Strike]);

        await grain.SetActionBarAsync(1, action);

        _sent
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<RpgSkillResultMessageComposer>()
            .Which.Code.Should()
            .Be(code);
        await DeactivateAsync(grain);
        (await StoredBarAsync()).Should().Equal(Strike);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public async Task ASlotOutOfRange_IsRefused_WithBadSlot(int slot)
    {
        PlayerCharacterGrain grain = await ActivateAsync("class.warrior", [Strike]);

        await grain.SetActionBarAsync(slot, Strike);

        _sent.OfType<RpgSkillResultMessageComposer>().Single().Code.Should().Be("bad_slot");
    }

    [Fact]
    public async Task AnEmptyAction_ClearsTheSlot()
    {
        PlayerCharacterGrain grain = await ActivateAsync("class.warrior", [Strike]);

        await grain.SetActionBarAsync(0, "");

        _sent.OfType<RpgSkillsMessageComposer>().Single().Bar.Should().OnlyContain(a => a == "");
    }

    /// <summary>A bar written wrong by hand must not cost the player his HUD at login.</summary>
    [Fact]
    public async Task AMalformedStoredBar_ReadsAsEmpty()
    {
        PlayerCharacterGrain grain = await ActivateAsync("class.warrior", bar: null);
        await using (VortexDbContext db = new(_options))
        {
            await db.PlayerCharacters.ExecuteUpdateAsync(up =>
                up.SetProperty(x => x.ActionBar, "[\"skill:x\", null")
            );
        }
        await DeactivateAsync(grain);
        grain = await ActivateExistingAsync();

        await grain.PushStateAsync();

        _sent.OfType<RpgSkillsMessageComposer>().Single().Bar.Should().OnlyContain(a => a == "");
    }

    [Fact]
    public async Task PushState_SendsTheSkills()
    {
        PlayerCharacterGrain grain = await ActivateAsync("class.warrior", [Strike]);

        await grain.PushStateAsync();

        _sent
            .OfType<RpgSkillsMessageComposer>()
            .Single()
            .Known.Should()
            .Equal("skill.warrior.heroic_strike");
    }

    [Fact]
    public async Task TheProfile_CarriesClassAndSpecialization()
    {
        PlayerCharacterGrain grain = await ActivateAsync("class.warrior", [Strike]);

        CharacterCombatProfile profile = (await grain.GetCombatProfileAsync())!;

        (profile.ClassId, profile.SpecializationId)
            .Should()
            .Be(("class.warrior", "spec.warrior.guardian"));
    }

    [Fact]
    public async Task CarriedStatuses_ComeBackOnce_WithTheirTimeLeft()
    {
        PlayerCharacterGrain grain = await ActivateAsync("class.warrior", [Strike]);
        await grain.SetVitalsAsync(
            50,
            10,
            [
                new CarriedStatus
                {
                    StatusId = "status.long",
                    RemainingMs = 10_000,
                    Stacks = 2,
                    Amount = 4,
                },
                new CarriedStatus
                {
                    StatusId = "status.short",
                    RemainingMs = 3_000,
                    Stacks = 1,
                    Amount = 0,
                },
            ]
        );

        _clock.Now = _clock.Now.AddSeconds(4);

        (await grain.GetCombatProfileAsync())!
            .Statuses.Should()
            .Equal(
                new CarriedStatus
                {
                    StatusId = "status.long",
                    RemainingMs = 6_000,
                    Stacks = 2,
                    Amount = 4,
                }
            );
        (await grain.GetCombatProfileAsync())!.Statuses.Should().BeEmpty("they come back once");
    }

    private static SkillDefinition Skill(string id, string cls, int level) =>
        new()
        {
            Id = id,
            Kind = "skill",
            Classes = [cls],
            Level = level,
            Type = SkillType.Instant,
            Range = 1,
            Target = SkillTarget.Enemy,
            Effects = [new SkillEffectDefinition { Kind = "damage", Base = 1 }],
        };

    private async Task<string[]> StoredBarAsync()
    {
        await using VortexDbContext db = new(_options);
        string? json = (await db.PlayerCharacters.SingleAsync()).ActionBar;

        return json is null ? [] : JsonSerializer.Deserialize<string[]>(json)!;
    }

    /// <summary>A level 1 character of <paramref name="classId"/> whose stored bar is
    /// <paramref name="bar"/>; what the activation pushed is forgotten.</summary>
    private async Task<PlayerCharacterGrain> ActivateAsync(string classId, string[]? bar)
    {
        await using (VortexDbContext db = new(_options))
        {
            db.PlayerCharacters.Add(
                new PlayerCharacterEntity
                {
                    PlayerEntityId = Player,
                    ClassId = classId,
                    SpecializationId =
                        classId == "class.warrior"
                            ? "spec.warrior.guardian"
                            : "spec.classless.adventurer",
                    ActionBar = bar is null ? null : JsonSerializer.Serialize(bar),
                }
            );
            await db.SaveChangesAsync();
        }

        return await ActivateExistingAsync();
    }

    /// <summary>The grain over the row already stored; what the activation pushed is forgotten.</summary>
    private async Task<PlayerCharacterGrain> ActivateExistingAsync()
    {
        PlayerCharacterGrain grain =
            GrainActivationContext.CreateWithIntegerKey<PlayerCharacterGrain>(
                Player,
                new OptionsDbContextFactory(_options),
                CharacterContent.Registry(_content, () => 1),
                new IStatSource[] { new ClassLevelStatSource(), new EquipmentStatSource() },
                FakeProxy.Create<IEventPublisher>(_ => null),
                FakeProxy.Create<ICancellableEventPublisher>(_ =>
                    Task.FromResult(new EventContext { CorrelationId = "test" })
                ),
                Options.Create(new CharacterConfig()),
                FakeProxy.Create<IVortexMetrics>(_ => null),
                NullLogger<PlayerCharacterGrain>.Instance,
                Presence(),
                Options.Create(new ItemConfig())
            );
        grain.Time = _clock;
        await grain.OnActivateAsync(CancellationToken.None);
        _sent.Clear();

        return grain;
    }

    private static Task DeactivateAsync(PlayerCharacterGrain grain) =>
        grain.OnDeactivateAsync(
            new DeactivationReason(DeactivationReasonCode.ShuttingDown, "test"),
            CancellationToken.None
        );

    private IGrainFactory Presence() =>
        FakeProxy.Create<IGrainFactory>(call =>
            call.Method.Name == nameof(IGrainFactory.GetGrain)
                ? FakeProxy.Create<IPlayerPresenceGrain>(inner =>
                {
                    if (inner.Method.Name == nameof(IPlayerPresenceGrain.SendComposerAsync))
                    {
                        _sent.AddRange(inner.Args![0] is object[] many ? many : [inner.Args[0]!]);
                    }

                    return null;
                })
                : null
        );
}
