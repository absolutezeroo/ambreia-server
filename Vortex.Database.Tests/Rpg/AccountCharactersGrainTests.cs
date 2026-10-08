using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Database.Tests.Support;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.Rpg.Characters.Grains;
using Vortex.Protocol.Messages.Outgoing.Rpg.Characters;
using Vortex.Rpg.Characters.Configuration;
using Vortex.Rpg.Characters.Grains;
using Vortex.Rpg.Content.Definitions;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Database.Tests.Rpg;

/// <summary>The characters of an account on the character screen (spec F3a §5.3, §9).</summary>
public sealed class AccountCharactersGrainTests : IAsyncLifetime
{
    private const int ACCOUNT_ID = 3;
    private const int OTHER_ACCOUNT_ID = 99;
    private const string WARRIOR = "class.warrior";
    private const string CLASSLESS = "class.classless";

    private readonly List<IComposer> _sent = [];
    private readonly Dictionary<long, CharacterSummary> _characters = [];
    private readonly List<(long Player, string ClassId)> _choices = [];
    private readonly Dictionary<string, ContentDefinition> _content = new()
    {
        [WARRIOR] = CharacterContent.Warrior,
        [CLASSLESS] = CharacterContent.Classless,
        ["spec.warrior.guardian"] = CharacterContent.Guardian,
    };

    private SqliteConnection _conn = null!;
    private DbContextOptions<VortexDbContext> _options = null!;

    public async Task InitializeAsync() => (_conn, _options) = await SqliteTestDb.OpenAsync();

    public async Task DisposeAsync() => await _conn.DisposeAsync();

    [Fact]
    public async Task Opening_SendsTheListThenTheClasses()
    {
        int warrior = await SeedAsync("Ann");
        _characters[warrior] = Summary(WARRIOR, 3);

        await Build().OpenAsync();

        _sent
            .Select(c => c.GetType())
            .Should()
            .Equal(typeof(RpgCharacterListMessageComposer), typeof(RpgClassListMessageComposer));

        RpgCharacterListMessageComposer list = (RpgCharacterListMessageComposer)_sent[0];
        list.MaxCharacters.Should().Be(5);
        list.Characters.Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new
                {
                    PlayerId = warrior,
                    Name = "Ann",
                    ClassId = WARRIOR,
                    Level = 3,
                    Starter = false,
                }
            );

        RpgClassListMessageComposer classes = (RpgClassListMessageComposer)_sent[1];
        classes
            .Classes.Select(c => (c.Id, c.Role, c.PrimaryStat, c.Starter))
            .Should()
            .Equal((CLASSLESS, "damage", "strength", true), (WARRIOR, "tank", "strength", false));
    }

    [Fact]
    public async Task AnOldAvatarWithoutACharacter_IsGivenTheStarterOnOpening()
    {
        int old = await SeedAsync("Old");

        await Build().OpenAsync();

        _choices.Should().Equal((old, CLASSLESS));
        ((RpgCharacterListMessageComposer)_sent[0]).Characters.Single().Starter.Should().BeTrue();
    }

    [Fact]
    public async Task ACreate_IsBornClassless()
    {
        await Build().CreateAsync("Bob", "", "M");

        PlayerEntity bob = await PlayerAsync("Bob");
        bob.Figure.Should().Be(new CharacterConfig().DefaultFigure);
        bob.PlayerAccountEntityId.Should().Be(ACCOUNT_ID);
        _choices.Should().Equal((bob.Id, CLASSLESS));

        ExpectResult(RpgCharacterResultMessageComposer.ActionCreate, "ok", bob.Id);
        _sent[1]
            .Should()
            .BeOfType<RpgCharacterListMessageComposer>()
            .Which.Characters.Single()
            .Starter.Should()
            .BeTrue();
    }

    [Fact]
    public async Task ABadName_IsRefused()
    {
        await Build().CreateAsync("<b>", "", "M");

        ExpectResult(RpgCharacterResultMessageComposer.ActionCreate, "character.name_invalid", 0);
        _sent.Should().ContainSingle();
        (await CountAsync(ACCOUNT_ID)).Should().Be(0);
    }

    [Fact]
    public async Task AFigureTooLong_IsRefused()
    {
        await Build().CreateAsync("Bob", new string('a', 256), "M");

        ExpectResult(RpgCharacterResultMessageComposer.ActionCreate, "character.figure_invalid", 0);
        (await CountAsync(ACCOUNT_ID)).Should().Be(0);
    }

    [Fact]
    public async Task ATakenName_IsRefused()
    {
        await SeedAsync("Bob", OTHER_ACCOUNT_ID);

        await Build().CreateAsync("Bob", "", "M");

        ExpectResult(RpgCharacterResultMessageComposer.ActionCreate, "character.name_taken", 0);
        (await CountAsync(ACCOUNT_ID)).Should().Be(0);
    }

    [Fact]
    public async Task TheFifthCharacter_IsTheLast()
    {
        await SeedManyAsync(4);
        IAccountCharactersGrain grain = Build();

        await grain.CreateAsync("Fifth", "", "M");
        await grain.CreateAsync("Sixth", "", "M");

        (await CountAsync(ACCOUNT_ID)).Should().Be(5);
        _sent
            .OfType<RpgCharacterResultMessageComposer>()
            .Select(r => r.Code)
            .Should()
            .Equal("ok", "character.limit_reached");
    }

    /// <summary>Review Focus 4: two creates at four characters, in flight together.</summary>
    [Fact]
    public async Task TwoCreatesAtTheLimit_LetOnlyOneThrough()
    {
        await SeedManyAsync(4);
        // The first save is held, so the second create counts four characters before the fifth
        // is written.
        HeldSaveInterceptor saves = new();
        IAccountCharactersGrain grain = Build(
            new DbContextOptionsBuilder<VortexDbContext>(_options).AddInterceptors(saves).Options
        );

        Task first = grain.CreateAsync("Fifth", "", "M");
        Task second = grain.CreateAsync("Sixth", "", "M");
        saves.Release();
        await Task.WhenAll(first, second);

        (await CountAsync(ACCOUNT_ID)).Should().Be(5);
        _sent
            .OfType<RpgCharacterResultMessageComposer>()
            .Select(r => r.Code)
            .Should()
            .BeEquivalentTo(["ok", "character.limit_reached"]);
    }

    [Fact]
    public async Task ChoosingAClassForSomeoneElse_IsNotOwned()
    {
        int theirs = await SeedAsync("Theirs", OTHER_ACCOUNT_ID);

        await Build().ChooseClassAsync(theirs, WARRIOR);

        ExpectResult(
            RpgCharacterResultMessageComposer.ActionChooseClass,
            "character.not_owned",
            theirs
        );
        _choices.Should().BeEmpty();
    }

    [Fact]
    public async Task ChoosingAClass_SendsOkThenTheList()
    {
        int mine = await SeedAsync("Mine");
        _characters[mine] = Summary(CLASSLESS, 2);

        await Build().ChooseClassAsync(mine, WARRIOR);

        _choices.Should().Equal((mine, WARRIOR));
        ExpectResult(RpgCharacterResultMessageComposer.ActionChooseClass, "ok", mine);
        _sent[1]
            .Should()
            .BeOfType<RpgCharacterListMessageComposer>()
            .Which.Characters.Single()
            .ClassId.Should()
            .Be(WARRIOR);
    }

    [Fact]
    public async Task ChoosingASecondRealClass_IsRefused()
    {
        int mine = await SeedAsync("Mine");
        _characters[mine] = Summary(WARRIOR, 2);

        await Build().ChooseClassAsync(mine, CLASSLESS);

        ExpectResult(
            RpgCharacterResultMessageComposer.ActionChooseClass,
            "character.class_already_chosen",
            mine
        );
        _sent.Should().ContainSingle();
    }

    [Fact]
    public async Task OwnsAsync_RefusesAnotherAccountsCharacter()
    {
        int mine = await SeedAsync("Mine");
        int theirs = await SeedAsync("Theirs", OTHER_ACCOUNT_ID);
        IAccountCharactersGrain grain = Build();

        (await grain.OwnsAsync(mine)).Should().BeTrue();
        _sent.Should().BeEmpty();

        (await grain.OwnsAsync(theirs)).Should().BeFalse();
        ExpectResult(
            RpgCharacterResultMessageComposer.ActionEnterWorld,
            "character.not_owned",
            theirs
        );
    }

    private void ExpectResult(int action, string code, int playerId) =>
        _sent[0]
            .Should()
            .BeOfType<RpgCharacterResultMessageComposer>()
            .Which.Should()
            .BeEquivalentTo(
                new
                {
                    Action = action,
                    Code = code,
                    PlayerId = playerId,
                }
            );

    private static CharacterSummary Summary(string classId, int level) =>
        new()
        {
            ClassId = classId,
            SpecializationId =
                classId == WARRIOR ? "spec.warrior.guardian" : "spec.classless.adventurer",
            Level = level,
            Xp = 0,
        };

    private IAccountCharactersGrain Build(DbContextOptions<VortexDbContext>? options = null) =>
        GrainActivationContext.CreateWithIntegerKey<AccountCharactersGrain>(
            ACCOUNT_ID,
            new OptionsDbContextFactory(options ?? _options),
            CharacterContent.Registry(_content, () => 1),
            Grains(),
            Options.Create(new CharacterConfig()),
            NullLogger<AccountCharactersGrain>.Instance
        );

    private IGrainFactory Grains() =>
        FakeProxy.Create<IGrainFactory>(call =>
            call.Method.Name == nameof(IGrainFactory.GetGrain)
                ? call.Method.GetGenericArguments()[0] == typeof(IAccountSessionGrain)
                    ? AccountSession()
                    : Character((long)call.Args![0]!)
                : null
        );

    private IAccountSessionGrain AccountSession() =>
        FakeProxy.Create<IAccountSessionGrain>(call =>
        {
            if (call.Method.Name == nameof(IAccountSessionGrain.SendComposerAsync))
            {
                lock (_sent)
                {
                    _sent.AddRange((IComposer[])call.Args![0]!);
                }
            }

            return null;
        });

    /// <summary>The character grain's rule: the starter class is left once for a real class.</summary>
    private IPlayerCharacterGrain Character(long player) =>
        FakeProxy.Create<IPlayerCharacterGrain>(call =>
            call.Method.Name switch
            {
                nameof(IPlayerCharacterGrain.GetSummaryAsync) => Task.FromResult(
                    _characters.GetValueOrDefault(player)
                ),
                nameof(IPlayerCharacterGrain.ChooseClassAsync) => Task.FromResult(
                    Choose(player, (string)call.Args![0]!)
                ),
                _ => null,
            }
        );

    private ChooseClassOutcome Choose(long player, string classId)
    {
        if (
            _characters.TryGetValue(player, out CharacterSummary? current)
            && (current.ClassId != CLASSLESS || classId == CLASSLESS)
        )
        {
            return ChooseClassOutcome.AlreadyChosen;
        }

        if (!_content.ContainsKey(classId))
        {
            return ChooseClassOutcome.UnknownClass;
        }

        _choices.Add((player, classId));
        _characters[player] = Summary(classId, current?.Level ?? 1);

        return ChooseClassOutcome.Chosen;
    }

    private async Task<int> SeedAsync(string name, int accountId = ACCOUNT_ID)
    {
        await using VortexDbContext db = new(_options);
        PlayerEntity player = new()
        {
            PlayerAccountEntityId = accountId,
            Name = name,
            Figure = "hd-180-1",
            Gender = AvatarGenderType.Male,
            PlayerStatus = PlayerStatusType.Offline,
            PlayerPerks = PlayerPerkFlags.None,
        };
        db.Players.Add(player);
        await db.SaveChangesAsync();

        return player.Id;
    }

    private async Task SeedManyAsync(int count)
    {
        for (int i = 0; i < count; i++)
        {
            int id = await SeedAsync($"Seed{i}");
            _characters[id] = Summary(CLASSLESS, 1);
        }
    }

    private async Task<PlayerEntity> PlayerAsync(string name)
    {
        await using VortexDbContext db = new(_options);

        return await db.Players.AsNoTracking().SingleAsync(p => p.Name == name);
    }

    private async Task<int> CountAsync(int accountId)
    {
        await using VortexDbContext db = new(_options);

        return await db.Players.CountAsync(p =>
            p.PlayerAccountEntityId == accountId && p.DeletedAt == null
        );
    }
}
