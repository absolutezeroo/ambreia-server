# Ambreia — points d'extension (EXTENSION-POINTS)

Audit du 2026-10-10, lecture seule, `main` @ `c24e882`. Compagnon de `ENTERPRISE-AUDIT.md`.
Invariant évalué (règle de modularité n° 1) : **ajouter une variante ne modifie pas le noyau.**

## 1. Catalogue AS-IS

| Extension | Contrat | Clé / ordre | Registre | Déclaré par | Découverte | Doublon | Tests du registre |
|---|---|---|---|---|---|---|---|
| Effet de compétence | `ISkillEffect : ISkillEffectRule` (`Vortex.Rpg.Skills/Effects/ISkillEffect.cs`) : `Kind`, `Problems`, `Apply`, `IsOffensive` | `Kind` (ordinal, sensible à la casse) | `SkillEffects` (`Dictionary`, `SkillEffects.cs:19-31`) côté zone ; `SkillKind._rules = GroupBy(Kind).Last()` côté import | `SkillsModule.AddEffect<T>` (`SkillsModule.cs:46-52`) | DI | **dernier gagne, silencieux** (les deux registres) | oui (effet inconnu, offensif) ; pas de test « dernier gagne » |
| Comportement de monstre | `IMonsterBehavior` : `Key`, `Think(MonsterBrain)` | `Key` | `RpgZoneSimulationFactory._behaviors` + `DeclaredBehaviors` (import) | `services.AddSingleton<IMonsterBehavior,T>` dans `MonstersModule` | DI | dernier gagne ; clés loguées au démarrage ; clé inconnue → warning, pas d'apparition | oui (déclaré par un autre module, non déclaré ne spawn pas) |
| Étape de coup | `IHitStep` : `Priority`, `bool Apply(HitContext)` | `Priority` (100 esquive … 950 bouclier) | `HitResolver` `OrderBy(Priority).ThenBy(FullName)` | `CombatModule`, `SkillsModule` | DI | tous s'exécutent ; **égalité déterministe par nom de type** | oui ; pas de test d'égalité |
| Système de combat | `ICombatSystem` : `Order`, `Tick`, `Added`, `Removing`, `PlayerWalked`, `BeforeFlush`, `Entry` | `Order` (statuts 100, incantations 200) | `CombatZone._systems` trié `(Order, FullName)` | `SkillsModule` | DI | tous ; `ICastingSystem`/`IStatusSystem` = `OfType<T>().LastOrDefault()` **par ordre trié, pas par ordre DI** | oui (5 cas avec pièces déclarées par le test) |
| Source de modificateurs | `IStatModifierSource.Contribute(Combatant, StatModifiers)` | aucune | tableau, ordre DI | `SkillsModule` | DI | tous, sommés (commutatif) | oui |
| Source de stats | `IStatSource.ContributeAsync` | `Name` (affichage) | `PlayerCharacterGrain._sources`, **awaités séquentiellement** (`:184-189`) | `CharactersModule.cs:26-27` | DI | tous sommés ; même `Name` deux fois accepté | calcul pur testé ; aucune source déclarée par un test |
| Kind de contenu | `IContentKind` : `Kind`, `IdPrefix`, `Public`, `DefinitionType`, `Parse`, `TextKeys`, `Check` | `Kind` | `ContentPackValidator._kinds = ToDictionary` | `RpgModule.cs:24-31` | DI | **lève `ArgumentException` à la construction** : le seul registre qui refuse un doublon | oui (kind inconnu, id dupliqué) ; pas de test « kind dupliqué » |
| Références de contenu | `IContentReferences` : `Kind`, `IsReferencedAsync`, `ReplaceAsync` | `Kind` (plusieurs par kind) | tableau filtré par kind, dans la transaction d'import | `CharactersModule.cs:30-32` | DI | tous | oui |
| Canal / règle / lien de chat | `IChatChannel` (`IsDefault`), `IChatRule` (`Priority`), `IChatLinkKind` (`Prefix`) | clé / priorité / préfixe | `ChatPipeline` : canaux `IsDefault` triés d'abord puis dernier gagne, règles `OrderBy(Priority).ThenBy(FullName)`, liens max 5 | chaque module, dans son dossier `Chat/` | DI | dernier gagne avec un log Info ; un canal livré cède toujours | oui (`ANewChannel_IsRoutedWithoutTouchingTheCore`, `ADeclaredChannel_ReplacesTheShippedPlaceholder_WhateverTheOrder`) |
| Fabrique de simulation de zone | `IZoneSimulationFactory.Create(IZoneRuntime, ZoneDocument)` | unique | résolu par `RoomGrain` (`RoomGrain.cs:472`), `NoZoneSimulation` par défaut | `MonstersModule.cs:22` | DI | dernier `AddSingleton` gagne | oui |
| Logique d'objet de room | `IRoomObjectLogic` + `[RoomObjectLogic("clé")]` | `(clé, famille)` sensible à la casse | `RoomObjectLogicProvider` (`ConcurrentDictionary`) | — | **scan d'assembly** (`RoomObjectLogicFeatureProcessor.cs:238-280`), non-public → warning | même type deux fois → inerte ; type différent → `LogError` + `InvalidLogic` | oui (collision, famille, unicité, clé) |
| Écouteur d'événement de room | `IRoomEventListener` + `[RoomEventListener]` | ordre d'enregistrement | liste, awaits séquentiels, try/catch par écouteur | — | scan | dédoublonnage par référence seulement | oui |
| Handler de paquet | `IMessageHandler<T>` | type `T` | `EnvelopeHost` (handlers d'un même `T` **en parallèle**) | — | scan, **types publics seulement** | **aucune politique : deux handlers d'un même `T` tournent tous les deux** (`EnvelopeHost.cs:36`) | pas de test de doublon |
| Behavior de pipeline | `IMessageBehavior` + `[Order]` | `Order` (0 par défaut) | `ImmutableArray.Sort` **instable** sur égalité (`EnvelopeHost.cs:246-254`) | — | scan | — | partiel |
| Handler d'événement | `IEventHandler<T>` | type | `EventRegistry`, handlers en parallèle, erreur isolée | — | scan | tous | oui (isolation) |
| Parser / serializer | `IParser`, `AbstractSerializer` | header id / type de composer | `RevisionMapBuilder` | `IRevisionMap` de `Revision20260701` | liste manuelle de 12 maps (`Revision20260701.cs:445-459`) | **refusé au démarrage** | oui (pairing, round-trips) |
| Révision | `IRevision` | id | `RevisionManager` | `ServiceCollectionExtensions.cs:28-35` (une seule, par `if/else`) | DI | **écrase silencieusement** (`RevisionManager.cs:27`) | 12 cas release |
| Plugin | `IVortexPlugin` | manifeste | `PluginManager` (ALC collectable) | dossier `plugins/` | fichiers | — | 37 tests |
| Condition / action de trigger de zone | `IZoneConditionEvaluator`, `IZoneActionExecutor` | type du record | **`Dictionary` statique écrit à la main** (`ZoneConditions.cs:14-19`, `ZoneActions.cs:17-26`) | le cœur `Vortex.Rooms` | aucune | lève à l'init de type | oui |
| Interaction de zone | `IZoneInteraction { Kind; UseAsync }` | `Kind` | **statique** (`ZoneInteractionModule.cs:24-35`) + `NeedsReach` nommant quatre kinds | le cœur | aucune | lève à l'init | oui |
| Étape de tick de room | — | — | **codée en dur** : `avatars, zone-clock, combat, flush-tiles, flush-items` (`RoomGrain.cs:336-347`) | — | — | — | aucun test d'une étape qui lève |
| Flux auxquels une présence s'abonne | — | — | **deux sites codés en dur** (`PlayerPresenceGrain.Room.cs:164`, `.Chat.cs:133-137`) | — | — | — | — |

Lecture : les sept points RPG et le chat respectent la règle (déclaration DI par le module, cœur sans
nom de variante). Les points de **zone** (conditions, actions, interactions) et du **tick** sont des listes
fermées dans le cœur ; ils contredisent la règle que les projets RPG suivent et sont le modèle à
aligner sur `RoomObjectLogicProvider` (scan ou DI + politique de collision + diagnostic).

## 2. Qualité des contrats (§11)

| Propriété | Effets | Comportements | Hit steps | Systèmes | Kinds | Chat | Logics | Handlers | Zone (cond./act./inter.) |
|---|---|---|---|---|---|---|---|---|---|
| Contrat documenté (skill `adding-an-rpg-extension`) | ✔ | ✔ | ✔ | ✔ | ✔ (`adding-rpg-content`) | ✔ | ✔ (`adding-a-room-object-logic`) | ✔ (`adding-a-packet`) | ✘ |
| Cycle de vie (singleton, par zone, par paquet) | singleton partagé zone+import | singleton | singleton | singleton copié par zone | singleton | singleton | instance par objet | **activé par paquet** | statique |
| Exigence de thread-safety écrite | ✘ (implicite : tour du grain) | ✘ | ✘ | ✘ | ✘ | ✘ | ✘ | ✘ (handlers parallèles !) | ✘ |
| Propriété de l'état | zone (`parts` sur combattants) | `MonsterBrain` | `HitContext` | zone | registre | présence | objet | contexte | `RoomLiveState` partagé |
| Sémantique d'erreur | exception = tick de zone isolé | idem | idem | idem | import refusé | refus `RpgChatResult` | logué | avalée + reportée | logué, inconnu = no-op / false |
| Ordre | DI | DI | priorité + nom | ordre + nom | n/a | priorité + nom | famille | parallèle | ordre du document |
| Doublon | dernier gagne silencieux | idem | tous | tous | **refusé** | dernier gagne + Info | **refusé** | tous | refusé (type) |
| Validation au démarrage | ✘ | log des clés | log | ✘ | ✔ | ✘ | ✔ | pairing | ✘ |
| Diagnostic runtime | `WarnMissing` une fois | warning | — | — | — | — | — | `OnNoHandlerRegistered` | `LogDebug` |
| Tests du point lui-même | partiel | ✔ | partiel | ✔ | partiel | ✔ | ✔ | ✘ | ✔ |

Manques transverses : **aucun registre à clé n'expose sa liste** (pas de commande console `extensions`
ni de métrique `vortex_extensions{kind}`), la thread-safety n'est écrite nulle part, et la politique
« dernier gagne » n'est ni testée ni loguée au niveau Warning pour les effets et les comportements.

## 3. Déterminisme (§12) et registres (§13)

| Risque | Constat | Verdict |
|---|---|---|
| Deux `IHitStep` de même priorité | départagés par `FullName` ordinal | déterministe, **non testé** |
| Deux `ICombatSystem` de même ordre | idem | déterministe |
| `ICastingSystem` résolu par `OfType<T>().LastOrDefault()` | dépend de l'ordre trié `(Order, FullName)` et non de l'ordre de déclaration : un module qui remplace le système d'incantation doit choisir un `Order` **et** un nom qui trie après | surprenant, à documenter ou à remplacer par une résolution explicite |
| Behaviors de pipeline à `[Order]` égal | tri instable, ordre 0 par défaut | pas d'effet aujourd'hui (ordres distincts) ; latent |
| Handlers scannés | ordre du scan, exécution parallèle | une seconde implémentation d'un handler de commande s'exécute en plus de la première sans avertissement |
| `IStatSource` | sommés : commutatif | OK |
| RNG de combat | `IZoneRuntime.Random` injecté partout ; production = `Random.Shared` jamais seedé ni logué ; `FixedRandom` ne surcharge pas `Next()`/`Next(max)` | rejouable en test ✔, **non reproductible en production** (pas de graine par zone) |
| Ordre des swings | itération d'un `Dictionary` après suppressions | dépend de l'implémentation ; à remplacer par une liste ordonnée par id |
| Horloge | `IZoneRuntime.NowMs` (Stopwatch) ; `TimeProvider` dans 3 classes seulement | cooldowns et statuts testables ; tickets, grâce, heartbeat non |

## 4. Violations ouvert/fermé dans les moteurs (§9)

| Variante | Fichiers à modifier pour la N+1 | Gravité |
|---|---|---|
| **Type de statut** (`StatusType`, 7) | `StatusSystem.Contribute` (`:103-124`), `Pulses` (`:230`), `StatusApplier.IsBuff` (`:150`), `StatusSet.Absorb`, `StatusKind`, `StatModifiers` (+ drapeau), `CombatZone.Recompute` (stun → rooted, slow → `SetMoveSpeed`, `CombatZone.Extensions.cs:60-70`), schéma, client | **HIGH** : la spec M1b §14 dit « un type de statut est additif », le code dit le contraire |
| **Stat** (`StatBlock`, `CombatStat`, `ModifierStat`) | `StatBlock`, `CharacterStatsSnapshot` (`[Id]`), `CharacterStatContribution`, `StatCalculator`, `ModifierStat→CombatStat` (`StatusSystem.cs:331-342`, lève sur inconnu), `Combatant`, `ICombatantStats`, `CombatZone.Recompute` (`:48-58`), formules, schémas, serializer `RpgCharacterStats`, client (~12 fichiers) | **HIGH** |
| **Camp** (`IsPlayer` = camp) | `CombatZone`, `SkillTargets.IsAlly/IsEnemy`, `Deaths` | HIGH pour le futur (compagnons, PNJ alliés, PvP consenti impossibles), neutre en M1 |
| École de dégâts / résistance | `HitContext`/`HitOptions` (pas de type), `DamageEffect`, définitions, + tout « nouvelle stat » | HIGH (traverse le cœur) |
| Mode de ciblage (`SkillTarget`, 6) | `SkillTargets.Resolve`, `TargetCheck`, `CastSystem.Centre/SingleTarget/Mark`, `SkillKind` (4-5 sites) | MEDIUM, interne au module |
| Type d'incantation | `CastSystem.Begin` | MEDIUM |
| Forme de télégraphe (cercle, ligne, cône) | `TelegraphCells` (`_ =>` cône par défaut : une forme inconnue devient silencieusement un cône), `CastSystem.Mark` (cas spécial cercle), `SkillKind` | MEDIUM |
| Emplacement d'équipement | `EquipmentSlot`, `EquipmentSlots.Count = 10`, cas spécial anneau (`CharacterInventory.Equipment.cs:43`) | MEDIUM |
| Intention de l'écran personnage | `RpgIntentLimitBehavior.cs:50-56`, `RpgIntentLimiter.cs:57-64` (deux `switch` sur les types concrets) + config | LOW |
| Snapshot de stuff data | `StuffDataSnapshotSerializer.cs:15-75` (8 sous-types, pas de `default`) | LOW |
| Condition / action / interaction de zone | voir §1 : record `Primitives/World/Rules`, schéma, `ZoneRuntimeRules.cs:108,165,193,249,386`, évaluateur + liste manuelle | MEDIUM |

Propres (aucune variante nommée) : `ContentPackValidator`, `MonsterSpawner`, `RpgZoneSimulationFactory`,
`HitResolver`, `CombatZone` (hors stats/camp), `ChatPipeline`, `PackageHandler`, `MessageRegistry`.

## 5. Points d'extension manquants mais attendus (§10)

| Besoin | Preuve de l'attente | Aujourd'hui |
|---|---|---|
| Loot | `MonsterKilledEvent` publié (`CombatZone.Deaths.cs:84-91`), spec §8.2, skill `adding-rpg-content` | zéro consommateur, pas de `ILootTable`, pas de numéro de séquence de kill dans l'event |
| Quêtes | `ItemCategory.Quest`, `RemoveItemsAsync` « quêtes, artisanat » | rien ne consomme `MonsterKilledEvent`/`XpGainedEvent` |
| Comportement d'objet (consommable, monture, cosmétique) | `ItemCategory` a 7 membres | pas de `IItemUse` ; `CharacterInventory` ne connaît qu'équipement et quête |
| Effets d'équipement / procs | `ItemDefinition.Stats : StatBlock?` seulement | `HitContext` n'expose pas l'équipement de l'attaquant |
| Règles de mort | `PlayerDies` / `MonsterDiesAsync` codés (`CombatZone.Deaths.cs:40-125`) | fantôme 5 s, 50 %, usure 10 % en config ; pas de `IDeathRule` |
| Politique d'apparition | `MonsterSpawner` file fixe, +1 000 ms de retry codé | pas de `ISpawnPolicy` |
| Sélecteur de compétence, rang, phases de monstre | contenu M1c accepté (`rank`, `skills`, `phases`, `summon`, `yell`) | **aucun runtime** : `RpgZoneSimulationFactory.Create` ne construit jamais de `RankScale` (`CombatZone.AddMonster` reçoit `RankScale.None`), `MeleeBehavior` ne lance rien ; un « boss » joue comme un loup |
| Formules XP / menace | `CombatMath.XpFor` statique, menace inline (`CombatZone.Extensions.cs:115-119`) | fermé |
| Étape de tick de room | spec §3 bis « fréquence par système » | liste codée (5 étapes) |
| Flux de groupe / guilde | spec §13, chat `group`/`guild` placeholders | la présence ne sait s'abonner qu'à sa room et à `world-chat` |
| Rechargement de contenu | `docs/architecture.md` Flow 6 « chaque système écoute `ContentReloadedEvent` » | **aucun `IEventHandler<ContentReloadedEvent>`** ; `SpawnPoint` garde la définition jusqu'à la désactivation de la room |

## 6. Test « feature future » (§46) — combien de fichiers existants ?

| Feature | Ajoutés | Modifiés (existants) | Cœur ? | Base ? | Protocole ? | Tests |
|---|---|---|---|---|---|---|
| Nouveau monstre | 1 JSON | `fr.txt` | non | non | non | pack |
| Nouveau type d'effet (`push`) | 1 classe + 1 test | `SkillsModule.cs` (1 ligne), 2 listes de tests ; + record et schéma si nouveau champ | non | non | non si aucun affichage | `EffectTests` |
| Comportement `ranged` | 1 classe + 1 test | `MonstersModule.cs` (1 ligne), 1 liste de test | non | non | non | **copie ~150 lignes** : `MeleeBehavior` est `sealed` sans base commune |
| Nouvelle quête | projet `Vortex.Rpg.Quests` (kind, grain, entité, handlers d'events, paquets) | `RpgModule`, `GrainFactoryExtensions`, `InitialCreate`, `Headers.cs` + maps, `PLATFORM` du script de murs, `.sln`, `Main` | non (événementiel) | **oui** | **oui** | nouveaux |
| Nouveau métier / recette | idem quêtes (projet + content kind + grain) | idem | non | oui | oui | nouveaux |
| Boss à phases | système de phases, constructeur de `RankScale`, config, cri/invocation | `RpgZoneSimulationFactory.Create`, `MonsterSpawner` (invocations hors `SpawnPoint`, `Live` en private set), `RpgZoneSimulation.TickAsync`, `RpgTargetMessageComposer.Rank` | **non** (seams `AddMonster(RankScale)`, `ApplyStatus`, `ResetCasts` existent) | non | 9026 existe, inerte | M1c 7-9 |
| Nouveau canal de chat | 1 classe | 1 ligne de module | non | non | non | ✔ ; **mais** un canal à flux propre (groupe) exige d'éditer `PlayerPresenceGrain` |
| Nouvelle mécanique de donjon (condition de zone) | record + évaluateur | `Primitives/World/Rules`, schéma, `ZoneRuntimeRules.cs`, `ZoneConditions.cs` | **oui** | non | non | `ZoneTriggerTests` |
| Nouvelle monnaie | n'existe pas : projet Economy + entité + grain + paquets | `GrainFactoryExtensions`, `InitialCreate`, headers | — | oui | oui | nouveaux |
| Nouveau type d'équipement (emplacement `dos`) | — | `EquipmentSlot`, `EquipmentSlots`, client, `ItemKind` ; cas spécial si pairé | non (règle pure) | non (int) | non | `EquipmentTests` |
| Nouvelle stat (`hâte`) | — | ~12 fichiers dont `CombatZone.Recompute` | **oui** | non | **oui** | nombreux |
| Nouveau type de statut (`root`) | — | `StatusType`, `StatusSystem` ×3, `StatusApplier`, `StatModifiers`, `CombatZone.Recompute`, schéma, client | **oui** | non | non | `StatusTests` |

Conclusion : les variantes **à clé** (effets, comportements, étapes, systèmes, kinds, canaux, logics)
sont additives ; les variantes **à enum** (stat, statut, école de dégâts, cible, camp) et les
variantes de **zone** traversent le cœur. Une feature de domaine (quête, métier, monnaie) coûte
aujourd'hui cinq fichiers d'infrastructure partagée (`GrainFactoryExtensions`, `InitialCreate`,
`Headers.cs`, script de murs, `Main`) : c'est le « coût d'un module » à réduire (TARGET §4).

## 7. Modèle cible (§102)

| Capacité | Contrat cible | Propriétaire | Enregistrement | Politique de doublon |
|---|---|---|---|---|
| Effet de compétence | `ISkillEffect` (inchangé) | Skills | `AddEffect<T>` | **refus au démarrage** sauf `[Replaces("damage")]` explicite ; liste loguée |
| Comportement de monstre | `IMonsterBehavior` + base `MonsterBehavior` (errance, laisse, aggro partagées) | Monsters | DI | refus sauf remplacement explicite |
| Étape de coup | `IHitStep` + `HitContext.School`, `HitContext.AttackerEquipment` (lecture) | Combat | DI | tous ; test d'égalité de priorité |
| Système de combat | `ICombatSystem` ; `ICastingSystem`/`IStatusSystem` résolus **explicitement** (`AddCastingSystem<T>`) | Combat | DI | un seul par rôle, refus du second |
| Stat | `StatId` **ouvert** (record `StatDefinition` de contenu ou registre `IStatKind`) ; `StatBlock` = dictionnaire compact ; `Recompute` générique | Rpg (noyau) | `AddStat<T>` ou contenu `content/stat` | refus |
| Type de statut | `IStatusKind` : `Contribute(StatModifiers)`, `Pulse`, `IsBuff`, `OnApplied/Removed` ; `StatModifiers` à drapeaux nommés | Skills | `AddStatusKind<T>` | refus |
| École de dégâts | `DamageSchool` (contenu) + stat de résistance par école | Combat | contenu | — |
| Ciblage | `ITargetMode` : `Resolve`, `Check`, `Centre` | Skills | `AddTargetMode<T>` | refus |
| Camp | `Faction` sur `Combatant` + `IFactionPolicy.AreHostile(a, b)` (défaut : joueurs vs monstres) | Combat | DI, une politique | un seul |
| Loot | `ILootSource` (table de contenu `content/loot`), `ILootRoll`, `ILootRecipientPolicy` ; consommateur de `MonsterKilledEvent` enrichi d'un `KillSequence` | Loot (dossier de Monsters d'abord, projet ensuite) | `AddLootRoll<T>` | refus |
| Quête | `IQuestObjective(kind)`, `IQuestCondition`, `IQuestReward` ; objectifs nourris par les events `-ed` | Quests | DI + contenu `content/quest` | refus |
| Règle de mort / apparition | `IDeathRule`, `ISpawnPolicy` **seulement quand une 2ᵉ implémentation existe** (anti-surconception) | Combat / Monsters | DI | — |
| Comportement d'objet | `IItemUse(kind)` : consommable, monture, cosmétique | Items | `AddItemUse<T>` | refus |
| Étape de tick de room | `IRoomTickStep { Key; Order; EveryMs; RunAsync }` ; les cinq étapes actuelles deviennent des implémentations | Rooms | DI | refus par clé |
| Condition / action / interaction de zone | `IZoneConditionEvaluator<T>` etc. **déclarés par DI** (le `Dictionary` statique devient un registre construit depuis `IEnumerable<>`), schéma polymorphe généré depuis les records | Rooms (puis modules RPG pour leurs kinds) | DI | refus |
| Flux de présence | `IPresenceStreamSource { StreamId For(player) }` ; un module (groupe, guilde) déclare ses flux, la présence s'abonne à tous | Players | DI | tous |
| Handler de paquet | inchangé + **refus de deux handlers pour une commande** (les events gardent le fan-out) | Core | scan | refus |
| Behavior de pipeline | `[Order]` obligatoire, tri stable, **échec fermé** pour `MessageRegistry` | Core | scan | — |
| Kind de contenu | inchangé | Rpg | DI | refus (déjà) |
| Révision | `AddRevision<T>` multiple, défaut par config | Revisions | DI | refus |

Chaque point cible expose la même triade : **contrat + `Add…<T>` + commande console `extensions` qui
liste clés, ordre et module d'origine** (et une métrique `vortex_extensions_registered{kind}`).

## 8. Test de modularité (§48) — « un module externe apporte une extension sans toucher la production »

| Point | Faisable aujourd'hui | Preuve ou blocage |
|---|---|---|
| Effet, comportement, hit step, système, source de stats, kind, référence de contenu, canal/règle/lien de chat, logic de room, handler, event handler | **oui** | `CombatExtensionTests`, `StatusFromModuleTests`, `DeclaredBehaviorsTests`, `ANewChannel_IsRoutedWithoutTheCore`, `RoomObjectLogicCollisionTests`, `Vortex.Plugins.TestPlugin` |
| Type de statut, stat, école, cible, camp | **non** | enums fermés (§4) |
| Condition/action/interaction de zone, étape de tick, flux de présence | **non** | listes statiques ou codées (§1) |
| Un projet `Vortex.Rpg.<X>` entier | **partiel** | compile et se branche par `IHostPluginModule`, mais exige `PLATFORM` du script de murs, `.sln`, `Main`, `GrainFactoryExtensions`, `InitialCreate` |

Fitness function proposée : un projet `Vortex.Rpg.FitnessProbe` (tests seulement) déclarant un
effet, un comportement, un hit step, un système, une source de stats, un kind, un canal, **un type de
statut, une stat et une condition de zone**, et un test par point qui prouve l'enregistrement et
l'exécution — rouge tant que §4 n'est pas traité, puis gardien permanent.
