# RPG M1d — Combat vertical slice, côté serveur : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** sélectionner n'est plus attaquer ; un joueur mort est KO et choisit (attendre, relâcher, accepter une résurrection) ; la cible de la cible, la menace, les cooldowns persistants, la cible vidée à la mort et un vrai message de level-up arrivent sur le fil ; l'effet avatar Habbo 2624 disparaît.

**Architecture :**
- **`Vortex.Rpg.Combat`** : `Combatant` gagne `AutoAttacking`, `KoSinceMs`, `Waiting`, `Offer` ; `CombatZone` gagne la sélection par joueur (`_selected`), les files `_targetsChanged`, `_autoAttackChanged`, `_offers`, le diff des cibles et la menace calculés au flush ; `Respawns` devient la machine KO.
- **`Vortex.Rpg.Skills`** : effet `resurrect`, mode de ciblage `dead_ally`, `SkillEffects.Apply` laisse passer un effet qui accepte un mort, cooldowns emportés hors zone et restaurés à l'entrée, `Entry` reçoit le joueur.
- **`Vortex.Rpg.Characters`** : `RpgLevelUp` depuis le grain, `LevelledUp` dans le profil pour que la zone remplisse vie et mana, cooldowns portés comme les statuts.
- **`Vortex.Rooms` / `Vortex.Rpg.Monsters`** : `RoomTargetModule` ne lance plus l'attaque, transmet la sélection à la simulation, refuse un joueur KO ; `IZoneRuntime.TargetDied` vide la cible de salle.
- **`Vortex.Protocol` / `Vortex.Revisions`** : sept messages, un retrait.

**Tech Stack :** C# .NET 10, Orleans, xUnit + FluentAssertions, `FakeZoneRuntime`, `SkillWorld`, `CombatZoneTests`.

**Spec :** `../vortex-rpg-client/docs/superpowers/specs/2026-10-13-m1-combat-vertical-slice-design.md` (§2, §6, §7 ; lire M1a, M1b, M1c pour l'existant).

## Global Constraints

- **Ids réseau.** Composers (serveur → client) : **9027 `RpgTargets`**, **9028 `RpgThreat`**, **9029 `RpgResurrectOffer`**, **9030 `RpgLevelUp`**, **9031 `RpgAutoAttackState`**. Events (client → serveur) : **9016 `RpgAutoAttack`**, **9017 `RpgDeathChoice`**. Retiré : **2624 `AvatarEffectMessageComposer`**. Les deux enums se recouvrent (9015 existe des deux côtés) : c'est normal.
- **Ordre des champs**, tous `int` sauf mention. 9027 : nombre, puis `objectId`, `targetObjectId` (0 = aucune). 9028 : `monsterObjectId`, `percent` (0..100), `hasAggro` (bool). 9029 : `casterObjectId`, `healthPercent`, `expiresMs`. 9030 : `fromLevel`, `toLevel`, bloc de stats avant (`RpgStatBlockWriter`), bloc de stats après, nombre, puis `skillId` (string) par skill appris. 9031 : `active` (bool). 9016 : `active` (bool). 9017 : `choice` (string : `wait`, `release`, `accept`, `decline`).
- **Config.** `Vortex:Combat` : `AutoReleaseSeconds` 60, `MaxKoSeconds` 600, `ResurrectOfferSeconds` 30, `ThreatReportStep` 5. `GhostSeconds` disparaît.
- **Rafale d'entrée**, dans cet ordre, envoyée en séquence (un `await` par composer) : `RpgVitals`, `RpgStatuses`, `RpgTelegraphs`, `RpgTargets`, `RpgCooldowns`, `RpgThreat` (si cible monstre sélectionnée), `RpgDeath` puis `RpgResurrectOffer` (si KO), **`RpgSelfVitals` en dernier**.
- **Contenu** : `skill.cleric.resurrect` dans `content/skill/cleric_resurrect.json` ; champ `icon` (string, défaut `""`) sur skill et statut ; `vfx.aura` sur `status.focused`. `ContentPackInRepoTests` pince 19 skills et 9 statuts après la tâche 10.
- **Couches** : `Vortex.Rpg.Monsters` ne référence pas `Vortex.Rpg.Skills` ; `Vortex.Rooms` ne référence que le cœur. Un type par fichier, namespace = dossier.
- **Commits** en français, par chemins explicites, avec la ligne `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- **Portes** : `dotnet test <Projet> --filter "FullyQualifiedName~X"` par tâche ; à la fin `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate` (Main et Hosting.Tests en `-o …/bin/alt` si `bin/Debug` est verrouillé) et, côté client, `pnpm gen:headers --check`.

## Décisions de ce plan (écarts assumés à la spec)

1. **Un `RpgLevelUp` par gain d'XP**, `fromLevel`/`toLevel`, et non un par niveau : le grain ne calcule pas les stats d'un niveau intermédiaire. Le client n'a plus rien à fusionner. Ordre d'envoi : `RpgCharacterProgress`, `RpgCharacterStats`, `RpgSkills`, `RpgLevelUp` par le grain ; `RpgVitals`/`RpgSelfVitals` par la zone au tick suivant (vie et mana pleines).
2. **La cible de la cible se calcule par diff au flush** (`_lastTargets`), pas par un setter sur `Combatant.TargetId` : six sites écrivent cette propriété.
3. **La sélection vit dans la zone** (`_selected` par joueur) parce que la menace et l'attaque auto en dépendent ; `RoomTargetModule` la lui transmet, y compris le vidage.
4. **`IsGhost` reste le nom du « KO »** (`GhostUntilMs` = instant d'auto-release, null après `wait`) ; le renommage touchait vingt sites pour rien.
5. **Les textes d'interface sont côté client** (ses locales `hud.json`), comme les `vortex.combat.*` existants.

## Review Focus

1. **Un joueur KO change de zone ou se déconnecte** : il doit arriver vivant à 50 % de vie, sans offre pendante. Test : Tâche 5, `AKoPlayerLeaving_IsReleased`.
2. **Une offre arrive pendant qu'une autre est pendante, ou après que le joueur a relâché** : la plus récente remplace, une offre sur un vivant est ignorée. Test : Tâche 6, `ASecondOffer_ReplacesTheFirst`, `AnOfferOnTheLiving_IsDropped`.
3. **Le monstre sélectionné meurt pendant que l'attaque auto est active** : `RpgTarget` None, `RpgAutoAttackState(false)`, aucun swing suivant. Test : Tâche 4, `TheSelectedMonsterDying_ClearsTargetAndAttack`.
4. **La menace quand la table est vide ou quand le joueur n'y est pas** : `percent` 0, `hasAggro` false, pas de division par zéro. Test : Tâche 7, `ThreatOfAnUnthreatenedMonster_IsZero`.
5. **Deux niveaux d'un coup** : un seul `RpgLevelUp` avec `fromLevel` = ancien, `toLevel` = nouveau, et tous les skills appris entre les deux. Test : Tâche 9, `TwoLevelsAtOnce_MakeOneLevelUp`.

---

### Task 1 : Headers, messages et sérialisation (sept messages)

**Files :**
- Modify : `Vortex.Revisions/Revision20260701/Headers.cs` (les deux `#region Vortex-specific`)
- Create : `Vortex.Protocol/Messages/Outgoing/Rpg/Combat/{RpgTargetsMessageComposer,RpgTargetEntry,RpgThreatMessageComposer,RpgResurrectOfferMessageComposer,RpgAutoAttackStateMessageComposer}.cs`, `Vortex.Protocol/Messages/Outgoing/Rpg/Characters/RpgLevelUpMessageComposer.cs`, `Vortex.Protocol/Messages/Incoming/Rpg/Combat/{RpgAutoAttackMessage,RpgDeathChoiceMessage}.cs`
- Create : `Vortex.Revisions/Revision20260701/Serializers/Rpg/Combat/{RpgTargets,RpgThreat,RpgResurrectOffer,RpgAutoAttackState}MessageComposerSerializer.cs`, `Serializers/Rpg/Characters/RpgLevelUpMessageComposerSerializer.cs`, `Parsers/Rpg/Combat/{RpgAutoAttack,RpgDeathChoice}MessageParser.cs`
- Modify : `Vortex.Revisions/Revision20260701/Maps/RpgCombatMap.cs`, `Maps/RpgCharactersMap.cs`
- Test : `Vortex.Revisions.Tests/Rpg/RpgCombatWireTests.cs`, `Vortex.Revisions.Tests/Rpg/RpgCharactersWireTests.cs`

**Interfaces :**
- Produces : les records ci-dessous ; `RpgDeathChoiceMessage.Choice` avec les constantes `Wait`, `Release`, `Accept`, `Decline`.

- [ ] **Step 1 : Le test d'aller-retour**

Dans `RpgCombatWireTests.cs` (mêmes `Serialize`/`Revision` que `ADeath_IsReadInTheClientsOrder`) :

```csharp
    [Fact]
    public void Targets_Threat_Offer_AutoAttackState_AreReadInTheClientsOrder()
    {
        (int header, ClientPacket p) = Serialize(
            new RpgTargetsMessageComposer
            {
                Entries = [new RpgTargetEntry { ObjectId = 4, TargetObjectId = 0 }],
            }
        );
        header.Should().Be(9027);
        p.PopInt().Should().Be(1);
        p.PopInt().Should().Be(4);
        p.PopInt().Should().Be(0);
        p.Remaining.Should().Be(0);

        (header, p) = Serialize(
            new RpgThreatMessageComposer { MonsterObjectId = 9, Percent = 80, HasAggro = false }
        );
        header.Should().Be(9028);
        (p.PopInt(), p.PopInt(), p.PopBoolean()).Should().Be((9, 80, false));

        (header, p) = Serialize(
            new RpgResurrectOfferMessageComposer { CasterObjectId = 2, HealthPercent = 35, ExpiresMs = 30000 }
        );
        header.Should().Be(9029);
        (p.PopInt(), p.PopInt(), p.PopInt()).Should().Be((2, 35, 30000));

        (header, p) = Serialize(new RpgAutoAttackStateMessageComposer { Active = true });
        header.Should().Be(9031);
        p.PopBoolean().Should().BeTrue();
    }

    [Fact]
    public void AutoAttack_And_DeathChoice_ReadTheirField()
    {
        byte[] body = [1];
        ((RpgAutoAttackMessage)Revision.Parsers[9016].Parse(new ClientPacket(9016, body)))
            .Active.Should().BeTrue();

        byte[] choice = new byte[2 + 7];
        BinaryPrimitives.WriteInt16BigEndian(choice, 7);
        "release"u8.CopyTo(choice.AsSpan(2));
        ((RpgDeathChoiceMessage)Revision.Parsers[9017].Parse(new ClientPacket(9017, choice)))
            .Choice.Should().Be(RpgDeathChoiceMessage.Release);
    }
```

Dans `RpgCharactersWireTests.cs` :

```csharp
    [Fact]
    public void ALevelUp_WritesBothStatBlocksThenTheSkills()
    {
        CharacterStatsSnapshot before = new() { MaxHealth = 100, MaxMana = 50, SpeedPercent = 100, Power = 20 };
        CharacterStatsSnapshot after = before with { MaxHealth = 112, Power = 22 };
        (int header, ClientPacket p) = Serialize(
            new RpgLevelUpMessageComposer
            {
                FromLevel = 3, ToLevel = 4, StatsBefore = before, StatsAfter = after,
                SkillsLearned = ["skill.mage.frost_nova"],
            }
        );

        header.Should().Be(9030);
        (p.PopInt(), p.PopInt()).Should().Be((3, 4));
        for (int i = 0; i < 10; i++) p.PopInt(); // before
        p.PopInt(); p.PopInt(); p.PopInt();
        p.PopInt().Should().Be(112);
        for (int i = 0; i < 6; i++) p.PopInt();
        p.PopInt().Should().Be(1);
        p.PopString().Should().Be("skill.mage.frost_nova");
        p.Remaining.Should().Be(0);
    }
```

- [ ] **Step 2 : Vérifier l'échec**

Run : `dotnet test Vortex.Revisions.Tests --filter "FullyQualifiedName~RpgCombatWireTests|FullyQualifiedName~RpgCharactersWireTests"`
Expected : erreur de compilation (types inconnus).

- [ ] **Step 3 : Headers**

Dans la région des events, après `RpgCastCancelMessageEvent = 9015` :

```csharp
    public const int RpgAutoAttackMessageEvent = 9016; // RPG (M1d §2.2)
    public const int RpgDeathChoiceMessageEvent = 9017; // RPG (M1d §2.2)
```

Dans la région des composers, après `RpgMonsterYellMessageComposer = 9026` :

```csharp
    public const int RpgTargetsMessageComposer = 9027; // RPG (M1d §2.1)
    public const int RpgThreatMessageComposer = 9028; // RPG (M1d §2.1)
    public const int RpgResurrectOfferMessageComposer = 9029; // RPG (M1d §2.1)
    public const int RpgLevelUpMessageComposer = 9030; // RPG (M1d §2.1)
    public const int RpgAutoAttackStateMessageComposer = 9031; // RPG (M1d §2.1)
```

- [ ] **Step 4 : Records**

`RpgTargetEntry.cs` et `RpgTargetsMessageComposer.cs` (sur le modèle de `RpgCooldown` / `RpgCooldownsMessageComposer`) :

```csharp
[GenerateSerializer, Immutable]
public sealed record RpgTargetEntry
{
    [Id(0)] public required int ObjectId { get; init; }
    /// <summary>0 : aucune.</summary>
    [Id(1)] public required int TargetObjectId { get; init; }
}

/// <summary>Qui vise qui (M1d §2.1, 9027) : complet à l'entrée, puis les changements du tick.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgTargetsMessageComposer : IComposer
{
    [Id(0)] public required ImmutableArray<RpgTargetEntry> Entries { get; init; }
}
```

```csharp
/// <summary>La menace du joueur sur sa cible (M1d §2.1, 9028), à lui seul.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgThreatMessageComposer : IComposer
{
    [Id(0)] public required int MonsterObjectId { get; init; }
    /// <summary>floor(menace du joueur / menace du premier × 100), 0..100.</summary>
    [Id(1)] public required int Percent { get; init; }
    [Id(2)] public required bool HasAggro { get; init; }
}

/// <summary>Un soigneur propose de ranimer le joueur KO (M1d §2.3, 9029).</summary>
[GenerateSerializer, Immutable]
public sealed record RpgResurrectOfferMessageComposer : IComposer
{
    [Id(0)] public required int CasterObjectId { get; init; }
    [Id(1)] public required int HealthPercent { get; init; }
    [Id(2)] public required int ExpiresMs { get; init; }
}

/// <summary>L'attaque auto du joueur a changé d'état (M1d §2.3, 9031).</summary>
[GenerateSerializer, Immutable]
public sealed record RpgAutoAttackStateMessageComposer : IComposer
{
    [Id(0)] public required bool Active { get; init; }
}
```

`Outgoing/Rpg/Characters/RpgLevelUpMessageComposer.cs` :

```csharp
/// <summary>Le joueur a gagné un ou plusieurs niveaux d'un coup (M1d décision 1, 9030).</summary>
[GenerateSerializer, Immutable]
public sealed record RpgLevelUpMessageComposer : IComposer
{
    [Id(0)] public required int FromLevel { get; init; }
    [Id(1)] public required int ToLevel { get; init; }
    [Id(2)] public required CharacterStatsSnapshot StatsBefore { get; init; }
    [Id(3)] public required CharacterStatsSnapshot StatsAfter { get; init; }
    [Id(4)] public required ImmutableArray<string> SkillsLearned { get; init; }
}
```

`Incoming/Rpg/Combat/RpgAutoAttackMessage.cs` et `RpgDeathChoiceMessage.cs` :

```csharp
/// <summary>Démarre ou arrête l'attaque auto sur la cible courante (M1d §2.2, 9016).</summary>
public record RpgAutoAttackMessage : IMessageEvent
{
    public required bool Active { get; init; }
}

/// <summary>Le choix d'un joueur KO (M1d §2.3, 9017).</summary>
public record RpgDeathChoiceMessage : IMessageEvent
{
    public const string Wait = "wait";
    public const string Release = "release";
    public const string Accept = "accept";
    public const string Decline = "decline";

    public required string Choice { get; init; }
}
```

- [ ] **Step 5 : Sérialiseurs et parseurs**

Chaque sérialiseur suit `RpgDeathMessageComposerSerializer` ; les corps :

```csharp
// RpgTargets
packet.WriteInteger(message.Entries.Length);
foreach (RpgTargetEntry e in message.Entries) packet.WriteInteger(e.ObjectId).WriteInteger(e.TargetObjectId);
// RpgThreat
packet.WriteInteger(message.MonsterObjectId).WriteInteger(message.Percent).WriteBoolean(message.HasAggro);
// RpgResurrectOffer
packet.WriteInteger(message.CasterObjectId).WriteInteger(message.HealthPercent).WriteInteger(message.ExpiresMs);
// RpgAutoAttackState
packet.WriteBoolean(message.Active);
// RpgLevelUp
packet.WriteInteger(message.FromLevel).WriteInteger(message.ToLevel);
RpgStatBlockWriter.Write(packet, message.StatsBefore);
RpgStatBlockWriter.Write(packet, message.StatsAfter);
packet.WriteInteger(message.SkillsLearned.Length);
foreach (string id in message.SkillsLearned) packet.WriteString(id);
```

Parseurs (modèle `RpgTargetSelectMessageParser`) :

```csharp
public IMessageEvent Parse(IClientPacket packet) => new RpgAutoAttackMessage { Active = packet.PopBoolean() };
public IMessageEvent Parse(IClientPacket packet) => new RpgDeathChoiceMessage { Choice = packet.PopString() };
```

Enregistrement dans `RpgCombatMap.RegisterInto` : deux `MapParser` (9016, 9017) et quatre `MapSerializer` ; dans `RpgCharactersMap` : `RpgLevelUpMessageComposer`.

- [ ] **Step 6 : Vérifier le vert**

Run : `dotnet test Vortex.Revisions.Tests`
Expected : PASS, y compris `SerializerPairingTests` et `EmittedComposerRegistrationTests` (ce dernier échoue tant que rien n'émet les composers : il repasse vert à la tâche 4 ; si la porte le bloque avant, émettre est la tâche suivante, pas une raison de le désactiver).

- [ ] **Step 7 : Commit**

```bash
git commit -m "Réseau : les sept messages du combat M1d (cibles, menace, offre de résurrection, level-up, attaque auto, choix de mort)" -- Vortex.Revisions/Revision20260701/Headers.cs Vortex.Protocol/Messages Vortex.Revisions/Revision20260701 Vortex.Revisions.Tests/Rpg
```

---

### Task 2 : Config et état du combattant

**Files :**
- Modify : `Vortex.Rpg.Combat/Configuration/CombatConfig.cs`, `Vortex.Rpg.Combat/Combatants/Combatant.cs`
- Create : `Vortex.Rpg.Combat/Combatants/ResurrectOffer.cs`
- Test : `Vortex.Rpg.Combat.Tests/CombatConfigTests.cs` (créer)

- [ ] **Step 1 : Test des défauts**

```csharp
public sealed class CombatConfigTests
{
    [Fact]
    public void TheKoDefaults_AreTheSpecs()
    {
        CombatConfig config = new();

        (config.AutoReleaseSeconds, config.MaxKoSeconds, config.ResurrectOfferSeconds, config.ThreatReportStep)
            .Should().Be((60, 600, 30, 5));
    }
}
```

- [ ] **Step 2 : Vérifier l'échec** — `dotnet test Vortex.Rpg.Combat.Tests --filter "FullyQualifiedName~CombatConfigTests"` → erreur de compilation.

- [ ] **Step 3 : Config et combattant**

Dans `CombatConfig`, remplacer `GhostSeconds` par :

```csharp
    /// <summary>Un joueur KO qui n'a rien choisi relâche après ce délai (M1d §2.3).</summary>
    public int AutoReleaseSeconds { get; init; } = 60;

    /// <summary>Même après « attendre », au plus ce temps au sol.</summary>
    public int MaxKoSeconds { get; init; } = 600;

    public int ResurrectOfferSeconds { get; init; } = 30;

    /// <summary>La menace n'est renvoyée que si elle a bougé d'autant de points.</summary>
    public int ThreatReportStep { get; init; } = 5;
```

`ResurrectOffer.cs` :

```csharp
/// <summary>Une proposition de résurrection en attente sur un joueur KO (M1d §2.3).</summary>
public sealed record ResurrectOffer(int CasterObjectId, int HealthPercent, long ExpiresAtMs);
```

Dans `Combatant`, sous `GhostUntilMs` :

```csharp
    /// <summary>KO : couché sur place jusqu'à un choix. <see cref="GhostUntilMs"/> est l'instant de
    /// l'auto-release ; null après « attendre » (M1d §2.3).</summary>
    public long? KoSinceMs { get; set; }

    public bool Waiting { get; set; }

    public ResurrectOffer? Offer { get; set; }

    /// <summary>Un joueur qui a demandé à relâcher ou accepté une offre : la zone agit au tick.</summary>
    public string? DeathChoice { get; set; }

    /// <summary>Un joueur qui frappe sa cible automatiquement (M1d §2.3) ; un monstre toujours.</summary>
    public bool AutoAttacking { get; set; }
```

et remplacer `public bool IsGhost => GhostUntilMs is not null;` par `public bool IsGhost => KoSinceMs is not null;`.

- [ ] **Step 4 : Vérifier le vert** — même filtre ; puis `dotnet build Vortex.Rpg.Combat` (les usages de `GhostSeconds` cassent : ils sont réécrits en tâche 5, mettre `config.AutoReleaseSeconds` à leur place dès maintenant dans `PlayerDies` et `FlushAsync`).

- [ ] **Step 5 : Commit** — `git commit -m "Combat : la config KO et l'état du combattant (attente, offre, attaque auto)" -- Vortex.Rpg.Combat Vortex.Rpg.Combat.Tests/CombatConfigTests.cs`

---

### Task 3 : Sélection dans la zone, attaque auto séparée

**Files :**
- Modify : `Vortex.Rpg.Combat/Zones/CombatZone.cs` (`StartAttack`, `StopAttack`), `CombatZone.Swings.cs`, `CombatZone.Outbox.cs`, `CombatZone.Extensions.cs`
- Modify : `Vortex.Rpg/Zones/IZoneSimulation.cs`, `NoZoneSimulation.cs`, `Vortex.Rpg.Monsters/Zones/RpgZoneSimulation.cs`
- Modify : `Vortex.Rooms/Grains/Modules/RoomTargetModule.cs`, `RoomAvatarModule.Walk.cs`, `Vortex.Rooms/Grains/RoomGrain.Combat.cs`, `Vortex.Primitives/Rooms/Grains/IRoomCombat.cs`
- Create : `Vortex.PacketHandlers/Rpg/Combat/RpgAutoAttackMessageHandler.cs`
- Test : `Vortex.Rpg.Combat.Tests/CombatZoneTests.cs`, `Vortex.PacketHandlers.Tests/AutoAttackHandlerTests.cs`

**Interfaces :**
- Produces : `CombatZone.SelectTarget(PlayerId, int objectId)` (0 vide), `CombatZone.SetAutoAttack(PlayerId, bool) : string?` (code de refus), `IZoneSimulation.SelectTarget(PlayerId, int)`, `IZoneSimulation.SetAutoAttack(PlayerId, bool)`, `IRoomCombat.SetAutoAttackAsync(ActionContext, bool, CancellationToken)`.

- [ ] **Step 1 : Tests**

Dans `CombatZoneTests` :

```csharp
    [Fact]
    public async Task SelectingAMonster_DoesNotSwing_UntilAutoAttack()
    {
        await OnePlayerAsync();
        int wolf = AddWolf(6, 5);

        _combat.SelectTarget(new PlayerId(7), wolf);
        _zone.NowMs += 2000;
        await _combat.TickAsync(CancellationToken.None);
        Hits().Should().BeEmpty("sélectionner n'est pas attaquer");

        _combat.SetAutoAttack(new PlayerId(7), true).Should().BeNull();
        Sent<RpgAutoAttackStateMessageComposer>().Should().BeEmpty("envoyé au flush");
        _zone.NowMs += 2000;
        await _combat.TickAsync(CancellationToken.None);

        Hits().Should().ContainSingle(h => h.AttackerId == 1 && h.TargetId == wolf);
        Sent<RpgAutoAttackStateMessageComposer>().Single().Active.Should().BeTrue();
    }

    [Fact]
    public async Task AutoAttackWithoutAMonsterTarget_IsRefused()
    {
        await OnePlayerAsync();

        _combat.SetAutoAttack(new PlayerId(7), true).Should().Be(RpgCombatResultMessageComposer.InvalidTarget);
    }

    [Fact]
    public async Task ChangingTargetWhileAttacking_Transfers_OrStops()
    {
        await OnePlayerAsync();
        int a = AddWolf(6, 5);
        int b = AddWolf(5, 6);
        _combat.SelectTarget(new PlayerId(7), a);
        _combat.SetAutoAttack(new PlayerId(7), true);

        _combat.SelectTarget(new PlayerId(7), b);
        _combat.Combatants[1].TargetId.Should().Be(b);

        _combat.SelectTarget(new PlayerId(7), 0);
        _combat.Combatants[1].AutoAttacking.Should().BeFalse();
        await _combat.TickAsync(CancellationToken.None);
        Sent<RpgAutoAttackStateMessageComposer>().Last().Active.Should().BeFalse();
    }
```

- [ ] **Step 2 : Vérifier l'échec** — `dotnet test Vortex.Rpg.Combat.Tests --filter "FullyQualifiedName~SelectingAMonster|FullyQualifiedName~AutoAttackWithout|FullyQualifiedName~ChangingTargetWhile"` → compilation.

- [ ] **Step 3 : La zone**

`RpgCombatResultMessageComposer` gagne `public const string InvalidTarget = "invalid_target";`.

Dans `CombatZone.cs`, un champ `private readonly Dictionary<PlayerId, int> _selected = [];` et, à la place de `StartAttack`/`StopAttack` :

```csharp
    /// <summary>La cible choisie par le joueur (M1d §2.3) ; 0 la vide. L'attaque auto suit un monstre
    /// et s'arrête sur tout le reste.</summary>
    public void SelectTarget(PlayerId player, int objectId)
    {
        if (objectId <= 0) _selected.Remove(player);
        else _selected[player] = objectId;

        if (!_players.TryGetValue(player, out int id)) return;

        Combatant me = _combatants[id];

        if (!me.AutoAttacking) return;

        if (objectId > 0 && _combatants.TryGetValue(objectId, out Combatant? t) && !t.IsPlayer && !t.IsDead)
        {
            me.TargetId = objectId;
            _approachGoals.Remove(id);
        }
        else
        {
            SetAutoAttack(player, false);
        }
    }

    /// <summary>Démarre ou arrête l'attaque auto sur la cible choisie ; un code de refus, ou null.</summary>
    public string? SetAutoAttack(PlayerId player, bool active)
    {
        if (!_players.TryGetValue(player, out int id)) return RpgCombatResultMessageComposer.Dead;

        Combatant me = _combatants[id];

        if (!active)
        {
            if (me.AutoAttacking) _autoAttackChanged.Add(id);
            me.AutoAttacking = false;
            me.TargetId = null;
            _approachGoals.Remove(id);

            return null;
        }

        if (me.IsDead || me.IsGhost) return RpgCombatResultMessageComposer.Dead;
        if (!_selected.TryGetValue(player, out int monsterId)
            || !_combatants.TryGetValue(monsterId, out Combatant? target) || target.IsPlayer || target.IsDead)
        {
            return RpgCombatResultMessageComposer.InvalidTarget;
        }
        if (IsSafe(id) || IsSafe(monsterId)) return RpgCombatResultMessageComposer.SafeZone;

        if (!me.AutoAttacking) _autoAttackChanged.Add(id);
        me.AutoAttacking = true;
        me.TargetId = monsterId;
        _approachGoals.Remove(id);

        return null;
    }

    /// <summary>Le joueur a marché de lui-même : son attaque s'arrête (M1a §6.1).</summary>
    public void StopAttack(PlayerId player) => SetAutoAttack(player, false);
```

Dans `CombatZone.Outbox.cs` : `private readonly HashSet<int> _autoAttackChanged = [];`, vidé dans `ClearOutbox`, et dans `FlushAsync` après les `_results` :

```csharp
        foreach (int id in _autoAttackChanged)
        {
            if (_combatants.TryGetValue(id, out Combatant? c) && c.PlayerId is { } player)
            {
                RpgAutoAttackStateMessageComposer state = new() { Active = c.AutoAttacking };
                sends.Add(() => runtime.SendToPlayerAsync(player, state));
            }
        }
```

Dans `Swings` : quand la cible est morte ou partie, un joueur voit aussi son flag tomber :

```csharp
            if (!_combatants.TryGetValue(targetId, out Combatant? t) || t.IsDead || t.IsGhost)
            {
                a.TargetId = null;
                if (a.AutoAttacking) { a.AutoAttacking = false; _autoAttackChanged.Add(a.ObjectId); }

                continue;
            }
```

`PlayerDies` fait de même avant `Forget(c.ObjectId)`.

- [ ] **Step 4 : Simulation et salle**

`IZoneSimulation` : remplacer `SelectTargetAsync`/`StopAttack` par

```csharp
    /// <summary>La cible choisie par le joueur, ou 0 ; la simulation répond (RpgTarget) si c'est un monstre.</summary>
    public Task<bool> SelectTargetAsync(PlayerId player, int objectId, CancellationToken ct);
    public string? SetAutoAttack(PlayerId player, bool active);
    /// <summary>Le joueur a marché : son attaque auto s'arrête.</summary>
    public void StopAttack(PlayerId player);
    /// <summary>Faux pour un joueur KO : nul ne le cible (M1d §2.3).</summary>
    public bool IsTargetable(int objectId);
```

`RpgZoneSimulation.SelectTargetAsync` : appeler `combat.SelectTarget(player, objectId)` **avant** le test « est-ce un monstre » (ainsi 0 et un joueur vident la sélection), supprimer le bloc `combat.StartAttack(...)`. Ajouter :

```csharp
    public string? SetAutoAttack(PlayerId player, bool active) => combat.SetAutoAttack(player, active);
    public bool IsTargetable(int objectId) =>
        !combat.Combatants.TryGetValue(objectId, out Combatant? c) || !c.IsGhost;
```

`NoZoneSimulation` : `SetAutoAttack` renvoie null, `IsTargetable` true.

`RoomTargetModule.SelectAsync` : retirer `roomGrain.Simulation.StopAttack(ctx.PlayerId);` ; après le `if (objectId <= 0 || …avatar is not IRoomPlayer target)` ajouter `|| !roomGrain.Simulation.IsTargetable(objectId)` dans la condition qui renvoie None, et quand `objectId <= 0` appeler d'abord `await roomGrain.Simulation.SelectTargetAsync(ctx.PlayerId, 0, ct);`.

`IRoomCombat` + `RoomGrain.Combat.cs` (copie de `CancelCastAsync`) :

```csharp
    public Task SetAutoAttackAsync(ActionContext ctx, bool active, CancellationToken ct)
    {
        if (ctx.PlayerId <= 0 || !_state.AvatarsByPlayerId.ContainsKey(ctx.PlayerId)) return Task.CompletedTask;

        if (Simulation.SetAutoAttack(ctx.PlayerId, active) is { } refusal)
        {
            return SendAsync(ctx.PlayerId, new RpgCombatResultMessageComposer { Code = refusal });
        }

        return Task.CompletedTask;
    }
```

`RpgAutoAttackMessageHandler` : copie de `RpgCastCancelMessageHandler` appelant `.SetAutoAttackAsync(ctx.AsActionContext(), message.Active, ct)`.

- [ ] **Step 5 : Vérifier le vert**

Run : `dotnet test Vortex.Rpg.Combat.Tests && dotnet test Vortex.Rpg.Monsters.Tests && dotnet test Vortex.Rooms.Tests && dotnet test Vortex.PacketHandlers.Tests`
Expected : PASS. Les tests existants qui attendaient un swing après `SelectTargetAsync` (M1a) ajoutent `SetAutoAttack(player, true)` : c'est le nouveau contrat, pas une régression.

- [ ] **Step 6 : Commit** — `git commit -m "Combat : sélectionner n'est plus attaquer ; RpgAutoAttack et son état" -- Vortex.Rpg.Combat Vortex.Rpg Vortex.Rpg.Monsters Vortex.Rooms Vortex.Primitives/Rooms Vortex.PacketHandlers Vortex.Rpg.Combat.Tests Vortex.Rpg.Monsters.Tests Vortex.Rooms.Tests Vortex.PacketHandlers.Tests`

---

### Task 4 : Cibles sur le fil et cible vidée à la mort

**Files :**
- Modify : `Vortex.Rpg.Combat/Zones/CombatZone.Outbox.cs`, `CombatZone.Deaths.cs`, `CombatZone.cs` (`EntryVitals`)
- Modify : `Vortex.Rpg/Zones/IZoneRuntime.cs`, `Vortex.Rooms/Grains/Zones/Runtime/RoomZoneRuntime.cs`, les `FakeZoneRuntime` de `Vortex.Rpg.Skills.Tests/Support`, `Vortex.Rpg.Combat.Tests`, `Vortex.Rpg.Monsters.Tests`
- Test : `Vortex.Rpg.Combat.Tests/CombatZoneTests.cs`

**Interfaces :**
- Produces : `IZoneRuntime.TargetDied(int objectId)` (la salle envoie `RpgTarget` None à qui le visait et oublie l'entrée) ; `FakeZoneRuntime.TargetsDied : List<int>`.

- [ ] **Step 1 : Tests**

```csharp
    [Fact]
    public async Task TargetChanges_AreBroadcastOncePerTick_AndFullOnEntry()
    {
        await OnePlayerAsync();
        int wolf = AddWolf(6, 5);
        _combat.SetMonsterTarget(wolf, 1);
        await _combat.TickAsync(CancellationToken.None);

        _zone.Broadcasts.OfType<RpgTargetsMessageComposer>().Single().Entries
            .Should().ContainSingle(e => e.ObjectId == wolf && e.TargetObjectId == 1);

        _zone.Broadcasts.Clear();
        await _combat.TickAsync(CancellationToken.None);
        _zone.Broadcasts.OfType<RpgTargetsMessageComposer>().Should().BeEmpty("rien n'a changé");

        _combat.EntryVitals(new PlayerId(7)).OfType<RpgTargetsMessageComposer>().Single().Entries
            .Should().HaveCount(2, "un par combattant, cible ou pas");
    }

    [Fact]
    public async Task TheSelectedMonsterDying_ClearsTargetAndAttack()
    {
        await OnePlayerAsync();
        int wolf = AddWolf(6, 5);
        _combat.SelectTarget(new PlayerId(7), wolf);
        _combat.SetAutoAttack(new PlayerId(7), true);
        _combat.Combatants[wolf].Health = 0;

        await _combat.TickAsync(CancellationToken.None);

        _zone.TargetsDied.Should().Equal(wolf);
        _combat.Combatants[1].AutoAttacking.Should().BeFalse();
        Sent<RpgAutoAttackStateMessageComposer>().Last().Active.Should().BeFalse();
        _combat.EntryVitals(new PlayerId(7)).OfType<RpgThreatMessageComposer>().Should().BeEmpty("plus de cible");
    }
```

- [ ] **Step 2 : Vérifier l'échec** — filtre `TargetChanges_Are|TheSelectedMonsterDying`.

- [ ] **Step 3 : Le diff des cibles**

Dans `CombatZone.Outbox.cs` : `private readonly Dictionary<int, int?> _lastTargets = [];` et au début de `FlushAsync`, avant les systèmes :

```csharp
        // Qui vise qui, par différence (M1d décision 2) : un combattant parti disparaît de la table.
        List<RpgTargetEntry> changed = [];

        foreach (Combatant c in _combatants.Values)
        {
            if (!_lastTargets.TryGetValue(c.ObjectId, out int? last) || last != c.TargetId)
            {
                _lastTargets[c.ObjectId] = c.TargetId;
                changed.Add(new RpgTargetEntry { ObjectId = c.ObjectId, TargetObjectId = c.TargetId ?? 0 });
            }
        }

        foreach (int gone in _lastTargets.Keys.Where(id => !_combatants.ContainsKey(id)).ToArray()) _lastTargets.Remove(gone);

        if (changed.Count > 0)
        {
            RpgTargetsMessageComposer targets = new() { Entries = [.. changed] };
            sends.Add(() => runtime.BroadcastAsync(targets));
        }
```

(`sends` est déclaré avant ; déplacer sa déclaration en tête de la méthode.)

`EntryVitals` yield, après `RpgVitals` et les `Entry` des systèmes :

```csharp
        yield return new RpgTargetsMessageComposer
        {
            Entries = [.. _combatants.Values.Select(c => new RpgTargetEntry { ObjectId = c.ObjectId, TargetObjectId = c.TargetId ?? 0 })],
        };
```

et `SelfVitals` passe **en dernier** du générateur (M1d §2.3 : il termine la rafale).

- [ ] **Step 4 : Cible vidée à la mort**

`IZoneRuntime` : `/// <summary>Un combattant est mort : la salle vide la cible de qui le visait (RpgTarget None).</summary> void TargetDied(int objectId);`. `RoomZoneRuntime` :

```csharp
    public void TargetDied(int objectId)
    {
        RoomObjectId id = new(objectId);

        foreach ((PlayerId player, RoomTarget target) in room._state.Targets.ToArray())
        {
            if (target.ObjectId == id)
            {
                room._state.Targets.Remove(player);
                _pending.Add((player, RpgTargetMessageComposer.None));
            }
        }
    }
```

(`_pending` porte déjà des envois ; si sa forme est « broadcast seulement », utiliser le chemin par joueur que `RpgZoneSimulation.SelectTargetAsync` emploie, `zone.SendToPlayerAsync`.) Les trois `FakeZoneRuntime` ajoutent `public List<int> TargetsDied { get; } = []; public void TargetDied(int objectId) => TargetsDied.Add(objectId);`.

Dans `MonsterDiesAsync` et `PlayerDies`, après `Forget(c.ObjectId)` :

```csharp
        runtime.TargetDied(c.ObjectId);
        foreach (PlayerId p in _selected.Where(kv => kv.Value == c.ObjectId).Select(kv => kv.Key).ToArray()) _selected.Remove(p);
```

- [ ] **Step 5 : Vérifier le vert** — `dotnet test Vortex.Rpg.Combat.Tests && dotnet test Vortex.Rooms.Tests --filter "FullyQualifiedName~RoomZoneRuntime"`.

- [ ] **Step 6 : Commit** — `git commit -m "Combat : qui vise qui sur le fil, cible vidée à la mort" -- Vortex.Rpg.Combat Vortex.Rpg/Zones Vortex.Rooms/Grains/Zones Vortex.Rpg.Combat.Tests Vortex.Rpg.Skills.Tests/Support Vortex.Rpg.Monsters.Tests`

---

### Task 5 : KO, attendre, relâcher, auto-release

**Files :**
- Modify : `Vortex.Rpg.Combat/Zones/CombatZone.Deaths.cs` (`PlayerDies`, `Respawns`), `CombatZone.cs` (`PlayerLeftAsync`, `EntryVitals`), `CombatZone.Outbox.cs`
- Modify : `Vortex.Rpg/Zones/IZoneSimulation.cs`, `NoZoneSimulation.cs`, `Vortex.Rpg.Monsters/Zones/RpgZoneSimulation.cs`, `Vortex.Primitives/Rooms/Grains/IRoomCombat.cs`, `Vortex.Rooms/Grains/RoomGrain.Combat.cs`
- Create : `Vortex.PacketHandlers/Rpg/Combat/RpgDeathChoiceMessageHandler.cs`
- Test : `Vortex.Rpg.Combat.Tests/CombatZoneTests.cs`

**Interfaces :**
- Produces : `CombatZone.DeathChoice(PlayerId, string choice)`, `IZoneSimulation.DeathChoice(PlayerId, string)`, `IRoomCombat.DeathChoiceAsync(ActionContext, string, CancellationToken)`.

- [ ] **Step 1 : Tests** (réécrire `APlayerKilled_BecomesAGhost_ThenRespawnsAtHalfHealth_AndIsWorn` en celui-ci, et ajouter les trois autres)

```csharp
    [Fact]
    public async Task APlayerKilled_IsKo_ThenAutoReleasesAtHalfHealth_AndIsWorn()
    {
        await OnePlayerAsync();
        int wolf = AddWolf(6, 5);
        _combat.Combatants[1].Health = 4;
        _combat.SetMonsterTarget(wolf, 1);

        await _combat.TickAsync(CancellationToken.None);

        _zone.Postures[1].Should().Be("lay");
        Sent<RpgDeathMessageComposer>().Single().RespawnSeconds.Should().Be(60);
        _calls.Should().Contain("wear 7: 10");
        _combat.Combatants[1].IsGhost.Should().BeTrue();

        _zone.NowMs += 59_000;
        await _combat.TickAsync(CancellationToken.None);
        _zone.Postures[1].Should().Be("lay", "pas encore");

        _zone.NowMs += 1_000;
        await _combat.TickAsync(CancellationToken.None);

        _zone.Teleports.Should().Equal((1, 0, 0));
        _zone.Postures[1].Should().BeNull();
        _combat.Combatants[1].Health.Should().Be(50);
    }

    [Fact]
    public async Task Waiting_CancelsTheAutoRelease_UntilTheCap()
    {
        await KoAsync();
        _combat.DeathChoice(new PlayerId(7), RpgDeathChoiceMessage.Wait);

        _zone.NowMs += 120_000;
        await _combat.TickAsync(CancellationToken.None);
        _combat.Combatants[1].IsGhost.Should().BeTrue();

        _zone.NowMs += 600_000;
        await _combat.TickAsync(CancellationToken.None);
        _combat.Combatants[1].IsGhost.Should().BeFalse("MaxKoSeconds");
    }

    [Fact]
    public async Task Release_StandsUpAtTheRespawnCell_Now()
    {
        await KoAsync();
        _combat.DeathChoice(new PlayerId(7), RpgDeathChoiceMessage.Release);
        await _combat.TickAsync(CancellationToken.None);

        _zone.Teleports.Should().Equal((1, 0, 0));
        _combat.Combatants[1].Health.Should().Be(50);
    }

    [Fact]
    public async Task AKoPlayerLeaving_IsReleased()
    {
        await KoAsync();
        await _combat.PlayerLeftAsync(new PlayerId(7), CancellationToken.None);

        _calls.Should().Contain(call => call.StartsWith("vitals 7: 50/"));
    }

    private async Task KoAsync()
    {
        await OnePlayerAsync();
        int wolf = AddWolf(6, 5);
        _combat.Combatants[1].Health = 1;
        _combat.SetMonsterTarget(wolf, 1);
        await _combat.TickAsync(CancellationToken.None);
        _combat.Combatants[1].IsGhost.Should().BeTrue();
    }
```

- [ ] **Step 2 : Vérifier l'échec** — filtre `APlayerKilled_IsKo|Waiting_Cancels|Release_StandsUp|AKoPlayerLeaving`.

- [ ] **Step 3 : La machine KO**

`PlayerDies` : remplacer `c.GhostUntilMs = now + config.GhostSeconds * 1000L;` par

```csharp
        c.KoSinceMs = now;
        c.GhostUntilMs = now + config.AutoReleaseSeconds * 1000L;
        c.Waiting = false;
        c.Offer = null;
        c.DeathChoice = null;
```

`FlushAsync` : `RespawnSeconds = config.AutoReleaseSeconds`.

`Respawns` devient :

```csharp
    /// <summary>La machine KO (M1d §2.3) : relâcher sur demande, à l'auto-release, ou au plafond.</summary>
    private void Respawns(long now)
    {
        foreach (Combatant c in _combatants.Values)
        {
            if (c.KoSinceMs is not { } since) continue;

            bool release = c.DeathChoice == RpgDeathChoiceMessage.Release
                || !c.Waiting && c.GhostUntilMs is { } until && now >= until
                || now >= since + config.MaxKoSeconds * 1000L;

            if (c.DeathChoice == RpgDeathChoiceMessage.Accept && c.Offer is { } offer && now < offer.ExpiresAtMs)
            {
                StandUp(c, c.MaxHealth * offer.HealthPercent / 100, c.MaxMana * offer.HealthPercent / 100);
                continue;
            }

            if (!release) continue;

            if (runtime.RespawnCell() is { } cell) runtime.Teleport(c.ObjectId, cell.X, cell.Y);

            StandUp(c, RespawnHealth(c), c.Mana);
        }
    }

    private void StandUp(Combatant c, int health, int mana)
    {
        c.KoSinceMs = null;
        c.GhostUntilMs = null;
        c.Waiting = false;
        c.Offer = null;
        c.DeathChoice = null;
        c.Health = Math.Max(1, health);
        c.Mana = mana;
        LeaveCombat(c);
        runtime.SetPosture(c.ObjectId, null);
        _vitalsChanged.Add(c.ObjectId);
        _selfChanged.Add(c.ObjectId);
    }
```

`DeathChoice` (dans `CombatZone.Extensions.cs`) :

```csharp
    public void DeathChoice(PlayerId player, string choice)
    {
        if (!_players.TryGetValue(player, out int id) || _combatants[id] is not { IsGhost: true } c) return;

        switch (choice)
        {
            case RpgDeathChoiceMessage.Wait: c.Waiting = true; c.GhostUntilMs = null; break;
            case RpgDeathChoiceMessage.Decline: c.Offer = null; break;
            case RpgDeathChoiceMessage.Release or RpgDeathChoiceMessage.Accept: c.DeathChoice = choice; break;
        }
    }
```

`PlayerLeftAsync` : `int health = c.IsGhost || c.IsDead ? RespawnHealth(c) : c.Health;` existe déjà et relâche de fait ; ajouter `c.Offer = null;` avant.

`EntryVitals`, avant le `SelfVitals` final, pour le joueur entrant :

```csharp
        if (_players.TryGetValue(player, out int me) && _combatants[me] is { IsGhost: true } ko)
        {
            long now = runtime.NowMs;
            yield return new RpgDeathMessageComposer
            {
                RespawnSeconds = ko.Waiting || ko.GhostUntilMs is not { } until ? 0 : (int)Math.Max(0, (until - now) / 1000),
            };
            if (ko.Offer is { } offer && offer.ExpiresAtMs > now)
            {
                yield return new RpgResurrectOfferMessageComposer
                {
                    CasterObjectId = offer.CasterObjectId, HealthPercent = offer.HealthPercent, ExpiresMs = (int)(offer.ExpiresAtMs - now),
                };
            }
        }
```

Simulation, grain, handler : `IZoneSimulation.DeathChoice(PlayerId, string)` (no-op dans `NoZoneSimulation`, `combat.DeathChoice` dans `RpgZoneSimulation`) ; `IRoomCombat.DeathChoiceAsync` et `RoomGrain.Combat.cs` sur le modèle de `CancelCastAsync` ; `RpgDeathChoiceMessageHandler` sur le modèle de `RpgCastCancelMessageHandler`.

- [ ] **Step 4 : Vérifier le vert** — `dotnet test Vortex.Rpg.Combat.Tests && dotnet test Vortex.Rooms.Tests --filter "FullyQualifiedName~RoomCombat" && dotnet test Vortex.PacketHandlers.Tests`.

- [ ] **Step 5 : Commit** — `git commit -m "Combat : KO à la place du fantôme ; attendre, relâcher, auto-release, plafond" -- Vortex.Rpg.Combat Vortex.Rpg/Zones Vortex.Rpg.Monsters/Zones Vortex.Primitives/Rooms Vortex.Rooms/Grains Vortex.PacketHandlers Vortex.Rpg.Combat.Tests`

---

### Task 6 : Effet `resurrect`, ciblage `dead_ally`, l'offre

**Files :**
- Create : `Vortex.Rpg.Skills/Effects/ResurrectEffect.cs`
- Modify : `Vortex.Rpg.Skills/Effects/ISkillEffect.cs`, `SkillEffects.cs`, `SkillsModule.cs`, `Vortex.Rpg.Skills/Targeting/SkillTargets.cs`, `TargetCheck.cs`, `Vortex.Rpg.Skills/Casting/CastSystem.cs` (`SingleTarget`)
- Modify : `Vortex.Rpg/Content/Definitions/SkillTarget.cs`, `SkillEffectDefinition.cs`, `schemas/content/skill.schema.json` (régénéré)
- Modify : `Vortex.Rpg.Combat/Zones/CombatZone.Extensions.cs` (`Offer`), `CombatZone.Outbox.cs`
- Modify : `Vortex.Hosting.Tests/Rpg/ContentPackInRepoTests.cs`, `Vortex.Rpg.Skills.Tests/Support/SkillWorld.cs`, `Vortex.Rpg.Skills.Tests/Content/SkillKindTests.cs` (listes d'effets)
- Test : `Vortex.Rpg.Skills.Tests/EffectTests.cs`, `Vortex.Rpg.Combat.Tests/CombatZoneTests.cs`

**Interfaces :**
- Produces : `SkillTarget.DeadAlly = 7` (JSON `dead_ally`), `SkillEffectDefinition.HealthPercent : int`, `ISkillEffect.TargetsDead : bool` (défaut false), `CombatZone.Offer(Combatant target, int casterObjectId, int healthPercent)`.

- [ ] **Step 1 : Tests**

Dans `EffectTests` (avec la fabrique `Skill(...)` de `CastTests`, à laquelle on ajoute un paramètre `int healthPercent = 0` posé sur l'effet) :

```csharp
    private static readonly SkillDefinition Revive = CastTests.Skill(
        "skill.cleric.resurrect", SkillType.Cast, SkillTarget.DeadAlly, range: 8, castMs: 500, cls: "class.cleric",
        effect: "resurrect", healthPercent: 35
    );

    [Fact]
    public async Task Resurrect_OnAKoAlly_OffersAndDoesNotStandHimUp()
    {
        Combatant healer = await _world.PlayerAsync(player: 7, objectId: 1, classId: "class.cleric");
        Combatant fallen = await _world.PlayerAsync(player: 8, objectId: 2, x: 6);
        fallen.Health = 0;
        await _world.TickAsync();
        fallen.IsGhost.Should().BeTrue();

        _world.Cast(Revive.Id, 2).Should().BeNull();
        await _world.RunAsync(750);

        fallen.IsGhost.Should().BeTrue("une offre, pas un relevé");
        _world.SentTo<RpgResurrectOfferMessageComposer>(8).Single()
            .Should().BeEquivalentTo(new { CasterObjectId = 1, HealthPercent = 35, ExpiresMs = 30000 });

        _world.Combat.DeathChoice(new PlayerId(8), RpgDeathChoiceMessage.Accept);
        await _world.TickAsync();
        (fallen.IsGhost, fallen.Health).Should().Be((false, 35));
    }

    [Fact]
    public async Task AnOfferOnTheLiving_IsDropped()
    {
        await _world.PlayerAsync(player: 7, objectId: 1, classId: "class.cleric");
        await _world.PlayerAsync(player: 8, objectId: 2, x: 6);

        _world.Cast(Revive.Id, 2).Should().Be(RpgSkillResultMessageComposer.InvalidTarget);
    }

    [Fact]
    public async Task ASecondOffer_ReplacesTheFirst()
    {
        await _world.PlayerAsync(player: 7, objectId: 1, classId: "class.cleric");
        await _world.PlayerAsync(player: 9, objectId: 3, x: 4, classId: "class.cleric");
        Combatant fallen = await _world.PlayerAsync(player: 8, objectId: 2, x: 6);
        fallen.Health = 0;
        await _world.TickAsync();

        _world.Cast(Revive.Id, 2, player: 7);
        await _world.RunAsync(750);
        _world.Cast(Revive.Id, 2, player: 9);
        await _world.RunAsync(750);

        fallen.Offer!.CasterObjectId.Should().Be(3);
        _world.SentTo<RpgResurrectOfferMessageComposer>(8).Should().HaveCount(2);
    }
```

- [ ] **Step 2 : Vérifier l'échec** — `dotnet test Vortex.Rpg.Skills.Tests --filter "FullyQualifiedName~Resurrect_On|FullyQualifiedName~AnOfferOnTheLiving|FullyQualifiedName~ASecondOffer"`.

- [ ] **Step 3 : Contenu et ciblage**

`SkillTarget` : `DeadAlly = 7,`. `SkillEffectDefinition` : `/// <summary>Vie et mana rendues par une résurrection, en % du maximum.</summary> public int HealthPercent { get; init; }`.

`SkillTargets.Targets` : `case SkillTarget.DeadAlly: return Find(zone, targetObjectId) is { } ko && IsKoAlly(ctx.Caster, ko) ? [ko] : [];` et

```csharp
    public static bool IsKoAlly(Combatant caster, Combatant c) => c.IsPlayer == caster.IsPlayer && c.IsGhost;
```

`TargetCheck` : `SkillTarget.DeadAlly => target is not null && SkillTargets.IsKoAlly(caster, target),`. `CastSystem.SingleTarget` : `|| skill.Target == SkillTarget.DeadAlly`.

`ISkillEffect` : `/// <summary>Vrai pour un effet qui s'applique à un mort (une résurrection).</summary> public bool TargetsDead => false;`. `SkillEffects.Apply` : la garde devient `if (target.IsDead && !type.TargetsDead) return;` — placée **après** la résolution de `type` pour l'effet courant.

`ResurrectEffect.cs` :

```csharp
/// <summary>Propose de ranimer un allié KO (M1d §2.3) : le joueur accepte ou refuse, rien ne le relève ici.</summary>
public sealed class ResurrectEffect : ISkillEffect
{
    public string Kind => "resurrect";
    public bool TargetsDead => true;

    public IEnumerable<string> Problems(SkillEffectDefinition effect)
    {
        if (effect.HealthPercent is < 1 or > 100) yield return "needs a healthPercent between 1 and 100.";
    }

    public bool IsOffensive(SkillEffectDefinition effect, IRpgContent content) => false;

    public void Apply(SkillContext ctx, Combatant target, SkillEffectDefinition effect)
    {
        if (!target.IsPlayer || !target.IsGhost) return;

        ctx.Zone.Offer(target, ctx.Caster.ObjectId, effect.HealthPercent);
    }
}
```

`SkillsModule` : `AddEffect<ResurrectEffect>(services);` ; les trois listes de tests (`ContentPackInRepoTests`, `SkillWorld`, `SkillKindTests`) ajoutent `new ResurrectEffect()`. Schéma : `UPDATE_CONTENT_SCHEMAS=1 dotnet test Vortex.Hosting.Tests -o Vortex.Hosting.Tests/bin/alt --filter ContentSchemaTests` ; vérifier que `target.enum` contient `dead_ally` et `effects.items.properties.healthPercent`.

- [ ] **Step 4 : L'offre dans la zone**

`CombatZone.Extensions.cs` :

```csharp
    /// <summary>Une offre de résurrection : la plus récente remplace (M1d §2.3).</summary>
    public void Offer(Combatant target, int casterObjectId, int healthPercent)
    {
        long expires = runtime.NowMs + config.ResurrectOfferSeconds * 1000L;
        target.Offer = new ResurrectOffer(casterObjectId, healthPercent, expires);
        if (target.PlayerId is { } player)
        {
            SendTo(player, new RpgResurrectOfferMessageComposer
            {
                CasterObjectId = casterObjectId, HealthPercent = healthPercent, ExpiresMs = config.ResurrectOfferSeconds * 1000,
            });
        }
    }
```

- [ ] **Step 5 : Vérifier le vert** — `dotnet test Vortex.Rpg.Skills.Tests && dotnet test Vortex.Rpg.Combat.Tests && dotnet test Vortex.Hosting.Tests --filter "FullyQualifiedName~ContentSchemaTests"`.

- [ ] **Step 6 : Commit** — `git commit -m "Skills : l'effet resurrect et le ciblage dead_ally ; l'offre de résurrection" -- Vortex.Rpg.Skills Vortex.Rpg/Content Vortex.Rpg.Combat schemas/content/skill.schema.json Vortex.Rpg.Skills.Tests Vortex.Hosting.Tests/Rpg/ContentPackInRepoTests.cs`

---

### Task 7 : Menace sur le fil

**Files :**
- Modify : `Vortex.Rpg.Combat/Zones/CombatZone.Outbox.cs`, `CombatZone.cs` (`EntryVitals`)
- Test : `Vortex.Rpg.Combat.Tests/CombatZoneTests.cs`

- [ ] **Step 1 : Tests**

```csharp
    [Fact]
    public async Task Threat_IsSentOnSelection_ThenOnlyWhenItMoves()
    {
        await OnePlayerAsync();
        await EnterAsync(8, 2, (4, 5));
        int wolf = AddWolf(6, 5);
        _combat.Combatants[wolf].Threat.Add(1, 100);
        _combat.Combatants[wolf].Threat.Add(2, 50);

        _combat.SelectTarget(new PlayerId(8), wolf);
        await _combat.TickAsync(CancellationToken.None);
        _zone.Sent.Where(s => s.Player == new PlayerId(8)).Select(s => s.Composer).OfType<RpgThreatMessageComposer>()
            .Single().Should().BeEquivalentTo(new { MonsterObjectId = wolf, Percent = 50, HasAggro = false });

        _combat.Combatants[wolf].Threat.Add(2, 3);
        await _combat.TickAsync(CancellationToken.None);
        _zone.Sent.Select(s => s.Composer).OfType<RpgThreatMessageComposer>().Should().HaveCount(1, "53 % : sous le pas de 5");

        _combat.Combatants[wolf].Threat.Add(2, 60);
        await _combat.TickAsync(CancellationToken.None);
        _zone.Sent.Select(s => s.Composer).OfType<RpgThreatMessageComposer>().Last()
            .Should().BeEquivalentTo(new { Percent = 100, HasAggro = true });
    }

    [Fact]
    public async Task ThreatOfAnUnthreatenedMonster_IsZero()
    {
        await OnePlayerAsync();
        int wolf = AddWolf(6, 5);
        _combat.SelectTarget(new PlayerId(7), wolf);
        await _combat.TickAsync(CancellationToken.None);

        Sent<RpgThreatMessageComposer>().Single().Should().BeEquivalentTo(new { Percent = 0, HasAggro = false });
    }
```

- [ ] **Step 2 : Vérifier l'échec** — filtre `Threat_IsSent|ThreatOfAnUnthreatened`.

- [ ] **Step 3 : Le calcul au flush**

`CombatZone.Outbox.cs` : `private readonly Dictionary<PlayerId, (int Monster, int Percent, bool HasAggro)> _lastThreat = [];` et, dans `FlushAsync` après le diff des cibles :

```csharp
        foreach ((PlayerId player, int monsterId) in _selected)
        {
            if (!_players.TryGetValue(player, out int me) || !_combatants.TryGetValue(monsterId, out Combatant? m) || m.IsPlayer) continue;

            (int percent, bool hasAggro) = ThreatOf(m, me);
            bool send = !_lastThreat.TryGetValue(player, out var last)
                || last.Monster != monsterId || last.HasAggro != hasAggro
                || Math.Abs(last.Percent - percent) >= config.ThreatReportStep;

            if (!send) continue;

            _lastThreat[player] = (monsterId, percent, hasAggro);
            RpgThreatMessageComposer threat = new() { MonsterObjectId = monsterId, Percent = percent, HasAggro = hasAggro };
            sends.Add(() => runtime.SendToPlayerAsync(player, threat));
        }

        foreach (PlayerId p in _lastThreat.Keys.Where(p => !_selected.ContainsKey(p)).ToArray()) _lastThreat.Remove(p);
```

et dans `CombatZone.Extensions.cs` :

```csharp
    /// <summary>La menace du joueur sur ce monstre : part du premier, et s'il est le premier (M1d §2.1).</summary>
    public (int Percent, bool HasAggro) ThreatOf(Combatant monster, int playerObjectId)
    {
        int? top = monster.Threat.Top;
        long best = top is { } t ? monster.Threat.ThreatOf(t) : 0;
        long mine = monster.Threat.ThreatOf(playerObjectId);

        return best <= 0 ? (0, false) : ((int)Math.Min(100, mine * 100 / best), top == playerObjectId);
    }
```

`EntryVitals` : avant le bloc KO, si `_selected.TryGetValue(player, out int sel)` et que c'est un monstre vivant, `yield return` un `RpgThreatMessageComposer` calculé de même (et poser `_lastThreat[player]`).

- [ ] **Step 4 : Vérifier le vert** — `dotnet test Vortex.Rpg.Combat.Tests`.

- [ ] **Step 5 : Commit** — `git commit -m "Combat : la menace du joueur sur sa cible, par pas" -- Vortex.Rpg.Combat Vortex.Rpg.Combat.Tests`

---

### Task 8 : Cooldowns portés hors zone et dans la rafale

**Files :**
- Create : `Vortex.Primitives/Rpg/Characters/CarriedCooldown.cs`
- Modify : `Vortex.Primitives/Rpg/Characters/CharacterCombatProfile.cs` (`[Id(9)] Cooldowns`, `[Id(10)] GlobalCooldownRemainingMs`), `Vortex.Primitives/Rpg/Characters/IPlayerCharacterGrain.cs` (`SetVitalsAsync` gagne les deux paramètres), `Vortex.Rpg.Characters/Grains/PlayerCharacterGrain.Vitals.cs`
- Modify : `Vortex.Rpg.Combat/Systems/ICombatSystem.cs` (`Entry(CombatZone, Combatant player)`, `CarryingCooldowns`), `CombatZone.cs` (`PlayerLeftAsync`, `EntryVitals`), `Vortex.Rpg.Skills/Casting/CastSystem.cs` (`Added`, `Removing`, `Entry`), `Vortex.Rpg.Skills/Statuses/StatusSystem.cs` et `TelegraphNews`/tout `Entry` existant (signature)
- Test : `Vortex.Rpg.Skills.Tests/CastTests.cs`, `Vortex.Database.Tests/Rpg/PlayerCharacterGrainTests.cs`

- [ ] **Step 1 : Tests**

```csharp
    [Fact]
    public async Task Cooldowns_LeaveWithThePlayer_AndAreInTheEntryBurst()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = ToughWolf();
        _world.Cast(Bolt.Id, wolf.ObjectId);
        await _world.RunAsync(2000);

        await _world.Combat.PlayerLeftAsync(new PlayerId(7), CancellationToken.None);
        _world.Calls.Should().Contain(call => call.StartsWith("vitals 7:") && call.Contains($"cd {Bolt.Id}@"));

        Combatant back = await _world.PlayerAsync(carriedCooldowns: [new CarriedCooldown { SkillId = Bolt.Id, RemainingMs = 1500 }], globalCooldownRemainingMs: 0);
        _world.Combat.EntryVitals(new PlayerId(7)).OfType<RpgCooldownsMessageComposer>().Single()
            .Skills.Single().Should().BeEquivalentTo(new { SkillId = Bolt.Id, RemainingMs = 1500 });
        _world.Combat.EntryVitals(new PlayerId(7)).Last().Should().BeOfType<RpgSelfVitalsMessageComposer>();
    }
```

(`SkillWorld.PlayerAsync` gagne `carriedCooldowns` et `globalCooldownRemainingMs` posés sur le profil ; son faux grain écrit `cd <id>@<ms>` dans l'appel `vitals`.)

- [ ] **Step 2 : Vérifier l'échec** — `dotnet test Vortex.Rpg.Skills.Tests --filter "FullyQualifiedName~Cooldowns_LeaveWith"`.

- [ ] **Step 3 : DTO et profil**

```csharp
[GenerateSerializer, Immutable]
public sealed record CarriedCooldown
{
    [Id(0)] public required string SkillId { get; init; }
    [Id(1)] public required int RemainingMs { get; init; }
}
```

`CharacterCombatProfile` : `[Id(9)] public ImmutableArray<CarriedCooldown> Cooldowns { get; init; } = [];` et `[Id(10)] public int GlobalCooldownRemainingMs { get; init; }`. `IPlayerCharacterGrain.SetVitalsAsync(int health, int mana, ImmutableArray<CarriedStatus> statuses, ImmutableArray<CarriedCooldown> cooldowns, int globalCooldownRemainingMs)` ; le grain les garde en mémoire à côté de `_carried` (`_carriedCooldowns`, `_carriedGlobalMs`, même `_carriedAt`) et `GetCombatProfileAsync` les rend décomptés du temps écoulé, une fois, comme `TakeCarried()`.

- [ ] **Step 4 : Systèmes**

`ICombatSystem` : `public IEnumerable<IComposer> Entry(CombatZone zone, Combatant player) => [];` (tous les implémenteurs adaptent la signature) et `public IEnumerable<CarriedCooldown> CarryingCooldowns(CombatZone zone, Combatant c) => [];`. `CombatZone.PlayerLeftAsync` collecte `_systems.SelectMany(s => s.CarryingCooldowns(this, c))` et le GCD restant (`CastSystem` expose `GlobalRemainingMs(c, now)`), et les passe à `SetVitalsAsync`.

`CastSystem` :

```csharp
    public void Added(CombatZone zone, Combatant c, CharacterCombatProfile? profile)
    {
        if (profile is null) return;

        long now = zone.Runtime.NowMs;
        SkillCooldowns cooldowns = Cooldowns(c);
        cooldowns.GlobalUntilMs = now + profile.GlobalCooldownRemainingMs;
        foreach (CarriedCooldown carried in profile.Cooldowns) cooldowns.Until[carried.SkillId] = now + carried.RemainingMs;
    }

    public IEnumerable<CarriedCooldown> CarryingCooldowns(CombatZone zone, Combatant c)
    {
        long now = zone.Runtime.NowMs;

        return c.Get<SkillCooldowns>() is { } cd
            ? cd.Until.Where(kv => kv.Value > now).Select(kv => new CarriedCooldown { SkillId = kv.Key, RemainingMs = (int)(kv.Value - now) })
            : [];
    }

    public IEnumerable<IComposer> Entry(CombatZone zone, Combatant player)
    {
        yield return CooldownsOf(player, zone.Runtime.NowMs);
    }
```

(`CooldownsOf` est l'extraction du composer que `SendCooldowns` construit ; `SendCooldowns` l'appelle.) `EntryVitals` passe le combattant du joueur à `Entry`.

- [ ] **Step 5 : Vérifier le vert** — `dotnet test Vortex.Rpg.Skills.Tests && dotnet test Vortex.Rpg.Combat.Tests && dotnet test Vortex.Database.Tests --filter "FullyQualifiedName~PlayerCharacterGrain"`.

- [ ] **Step 6 : Commit** — `git commit -m "Skills : les cooldowns suivent le joueur hors zone et entrent dans la rafale" -- Vortex.Primitives/Rpg Vortex.Rpg.Characters Vortex.Rpg.Combat Vortex.Rpg.Skills Vortex.Rpg.Skills.Tests Vortex.Database.Tests/Rpg`

---

### Task 9 : Level-up : message, ordre, vie pleine

**Files :**
- Modify : `Vortex.Rpg.Characters/Grains/PlayerCharacterGrain.Progress.cs`, `PlayerCharacterGrain.Hud.cs`, `Vortex.Primitives/Rpg/Characters/CharacterCombatProfile.cs` (`[Id(11)] LevelledUp`), `Vortex.Rpg.Combat/Zones/CombatZone.cs` (`PlayerStatsChanged`)
- Test : `Vortex.Database.Tests/Rpg/PlayerCharacterGrainTests.cs`, `Vortex.Rpg.Combat.Tests/CombatZoneTests.cs`

- [ ] **Step 1 : Tests**

Dans `PlayerCharacterGrainTests` (avec les aides du fichier pour créer un personnage niveau 1 et lire ce qu'il pousse) :

```csharp
    [Fact]
    public async Task TwoLevelsAtOnce_MakeOneLevelUp_AfterProgressStatsAndSkills()
    {
        PlayerCharacterGrain grain = await LevelOneMageAsync();
        int toLevel3 = Progression.XpToNext[0] + Progression.XpToNext[1];

        await grain.GrantXpAsync("test", toLevel3, "test:1");

        IComposer[] pushed = Pushed();
        pushed.Select(c => c.GetType()).Should().ContainInOrder(
            typeof(RpgCharacterProgressMessageComposer), typeof(RpgCharacterStatsMessageComposer),
            typeof(RpgSkillsMessageComposer), typeof(RpgLevelUpMessageComposer));
        RpgLevelUpMessageComposer up = pushed.OfType<RpgLevelUpMessageComposer>().Single();
        (up.FromLevel, up.ToLevel).Should().Be((1, 3));
        up.StatsAfter.MaxHealth.Should().BeGreaterThan(up.StatsBefore.MaxHealth);
        up.SkillsLearned.Should().Equal(KnownSkillsAbove(1, 3));
    }
```

Dans `CombatZoneTests` :

```csharp
    [Fact]
    public async Task ALevelUp_FillsHealthAndMana()
    {
        await OnePlayerAsync();
        _combat.Combatants[1].Health = 10;
        _combat.Combatants[1].Mana = 5;

        _combat.PlayerStatsChanged(new PlayerId(7), _profiles[7] with { LevelledUp = true, Level = 2 });
        await _combat.TickAsync(CancellationToken.None);

        (_combat.Combatants[1].Health, _combat.Combatants[1].Mana).Should().Be((100, 50));
        Sent<RpgSelfVitalsMessageComposer>().Last().Health.Should().Be(100);
    }
```

- [ ] **Step 2 : Vérifier l'échec** — `dotnet test Vortex.Database.Tests --filter "FullyQualifiedName~TwoLevelsAtOnce" ; dotnet test Vortex.Rpg.Combat.Tests --filter "FullyQualifiedName~ALevelUp_Fills"`.

- [ ] **Step 3 : Le grain**

Dans `GrantXpAsync` (Progress.cs), avant `c.Xp += gaining.Amount;` : `CharacterStatsSnapshot? before = await GetStatsAsync();`. Après `PlaceSkillsLearnedSince(c, oldLevel);` retenir `string[] learned = [.. Known(c).Where(s => s.Level > oldLevel && s.Level <= c.Level).Select(s => s.Id)];`. `PushProgressAsync(bool levelledUp, progress)` devient `PushProgressAsync(int oldLevel, CharacterStatsSnapshot? before, string[] learned, progress)` et construit, quand le niveau a changé :

```csharp
                composers =
                [
                    progress,
                    new RpgCharacterStatsMessageComposer { Stats = stats },
                    SkillsComposer(c),
                    new RpgLevelUpMessageComposer
                    {
                        FromLevel = oldLevel, ToLevel = c.Level,
                        StatsBefore = before ?? stats, StatsAfter = stats, SkillsLearned = [.. learned],
                    },
                ];
                TellTheZone(stats, levelledUp: true);
```

`TellTheZone(stats, bool levelledUp = false)` passe `Profile(stats, c) with { LevelledUp = levelledUp }`. `CharacterCombatProfile` : `[Id(11)] public bool LevelledUp { get; init; }`.

- [ ] **Step 4 : La zone**

`PlayerStatsChanged` : remplacer les deux `Math.Min` par

```csharp
        // Un niveau gagné remplit tout (M1d §2.3) ; une autre raison garde la vie courante.
        c.Health = profile.LevelledUp ? c.MaxHealth : Math.Min(c.Health, c.MaxHealth);
        c.Mana = profile.LevelledUp ? c.MaxMana : Math.Min(c.Mana, c.MaxMana);
```

- [ ] **Step 5 : Vérifier le vert** — les deux projets de test ci-dessus.

- [ ] **Step 6 : Commit** — `git commit -m "Personnage : RpgLevelUp après progress, stats et skills ; la zone remplit vie et mana" -- Vortex.Rpg.Characters Vortex.Primitives/Rpg Vortex.Rpg.Combat Vortex.Database.Tests/Rpg Vortex.Rpg.Combat.Tests`

---

### Task 10 : Contenu — `skill.cleric.resurrect`, `icon`, l'aura d'exemple

**Files :**
- Create : `content/skill/cleric_resurrect.json`
- Modify : `Vortex.Rpg/Content/Definitions/SkillDefinition.cs`, `StatusDefinition.cs` (`Icon`), `schemas/content/{skill,status}.schema.json` (régénérés), `content/status/focused.json` (aura), chaque `content/skill/*.json` et `content/status/*.json` (icône), `Vortex.Hosting.Tests/Rpg/ContentPackInRepoTests.cs` (19 skills)
- Test : `Vortex.Hosting.Tests/Rpg/ContentPackInRepoTests.cs`

- [ ] **Step 1 : Le test pince le nouveau skill et l'icône**

```csharp
    [Fact]
    public void EverySkillAndStatus_NamesItsIcon()
    {
        Pack().OfType<SkillDefinition>().Should().OnlyContain(s => s.Icon.Length > 0, "le client ne mappe plus rien");
        Pack().OfType<StatusDefinition>().Should().OnlyContain(s => s.Icon.Length > 0);
        Pack().OfType<SkillDefinition>().Should().ContainSingle(s => s.Id == "skill.cleric.resurrect")
            .Which.Effects.Single().Kind.Should().Be("resurrect");
    }
```

et le compte `HaveCount(18)` passe à 19.

- [ ] **Step 2 : Vérifier l'échec** — `dotnet test Vortex.Hosting.Tests -o Vortex.Hosting.Tests/bin/alt --filter "FullyQualifiedName~ContentPackInRepoTests"`.

- [ ] **Step 3 : Définitions et contenu**

`SkillDefinition` et `StatusDefinition`, à côté de `Vfx` : `/// <summary>L'icône que le client affiche (clé d'image, M1d §6.1).</summary> public string Icon { get; init; } = string.Empty;`. Régénérer les schémas (commande de la tâche 6).

`content/skill/cleric_resurrect.json` :

```json
{
  "$schema": "../../schemas/content/skill.schema.json",
  "id": "skill.cleric.resurrect",
  "kind": "skill",
  "classes": ["class.cleric"],
  "level": 4,
  "cost": 30,
  "type": "cast",
  "castMs": 8000,
  "range": 8,
  "target": "dead_ally",
  "cooldownMs": 60000,
  "effects": [{ "kind": "resurrect", "healthPercent": 35 }],
  "animation": "cast",
  "icon": "rpg/skills/cleric_skill_icon_4",
  "vfx": { "release": { "sheet": "mage/magic-circle", "attach": "feet", "delayMs": 0 } }
}
```

`content/status/focused.json` gagne `"vfx": { "aura": { "sheet": "mage/power-aura", "attach": "feet", "delayMs": 0 } }`.

Les icônes des 18 autres skills et des 9 statuts : copier les valeurs de `../vortex-rpg-client/packages/ambreia-game/src/features/skills/skillArt.ts` (`SKILL_ICONS`, `STATUS_ICONS`, clé = id de contenu) dans le champ `icon` de chaque fichier ; un id absent de la table prend la valeur de repli du même fichier (`FALLBACK_SKILL`, `FALLBACK_STATUS`). Textes : `content/texts/fr.txt` et `en.txt` gagnent `skill.cleric.resurrect.name=Résurrection` / `Resurrection`.

- [ ] **Step 4 : Vérifier le vert** — même filtre, puis `node scripts/content/push.mjs` sur l'émulateur local (ou le driver) pour voir les définitions servies avec `icon`.

- [ ] **Step 5 : Commit** — `git commit -m "Contenu : skill.cleric.resurrect, l'icône sur skills et statuts, une aura d'exemple" -- content Vortex.Rpg/Content schemas/content Vortex.Hosting.Tests/Rpg/ContentPackInRepoTests.cs`

---

### Task 11 : Retrait de l'effet avatar 2624

**Files :**
- Modify : `Vortex.Revisions/Revision20260701/Headers.cs` (retirer `AvatarEffectMessageComposer`), le map qui l'enregistre, `Vortex.Rooms/Grains/RoomGrain.Entry.cs:224-238` (le replay), `Vortex.Primitives/Rooms/IRoomAvatar.cs` (`CurrentEffectId`, `SetEffect`) et ses implémentations
- Delete : `Vortex.Protocol/Messages/Outgoing/.../AvatarEffectMessageComposer.cs`, son sérialiseur
- Test : `Vortex.Revisions.Tests` (le test de pairage reste vert), `Vortex.Rooms.Tests`

- [ ] **Step 1 : Inventaire** — `grep -rn "AvatarEffect\|CurrentEffectId\|SetEffect" --include=*.cs .` ; chaque ligne est dans un fichier de la liste ci-dessus, sinon l'ajouter.
- [ ] **Step 2 : Retirer** la chaîne entière (header, record, sérialiseur, enregistrement, replay, propriété) ; `dotnet build Vortex.Cloud.sln > build.log` jusqu'à zéro erreur.
- [ ] **Step 3 : Vérifier** — `dotnet test Vortex.Revisions.Tests && dotnet test Vortex.Rooms.Tests` ; `grep -rn "AvatarEffect" --include=*.cs . | wc -l` = 0.
- [ ] **Step 4 : Commit** — `git commit -m "Salle : plus d'effet avatar Habbo (2624) ; les visuels de statut sont des auras de contenu" -- $(grep -rln "AvatarEffect\|CurrentEffectId" --include=*.cs . | sort -u) Vortex.Revisions/Revision20260701/Headers.cs` (plus les fichiers supprimés, déjà dans l'index par `git rm`)

---

### Task 12 : Porte complète et contrat client

- [ ] **Step 1 :** `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate` (Main et Hosting.Tests en `-o …/bin/alt` si besoin) → vert ; `node scripts/hooks/check-architecture-walls.mjs` et `check-code-layout.mjs` → 0.
- [ ] **Step 2 :** côté client, `pnpm gen:headers` puis `pnpm test` : `incoming-registered` et `outgoing-registered` listent les sept headers comme non servis — c'est l'entrée du plan client (sa tâche 1), pas une régression ici.
- [ ] **Step 3 :** `docs/CLIENT-SERVER-ARCHITECTURE.md` du client, §24 : retirer « Avatar effects — Deferred ».
- [ ] **Step 4 : Commit** des docs seuls.
