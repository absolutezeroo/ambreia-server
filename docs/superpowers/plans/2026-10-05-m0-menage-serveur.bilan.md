# Bilan — M0 ménage serveur

Plan : `docs/superpowers/plans/2026-10-05-m0-menage-serveur.md`. Branche `m0-menage`, partie de `main`
`af91e325`. Commits : `adb5a06c` (pré-étape) … `6ca376b1` (Tâche 11) ; Tâche 12 : `1b2c0ab4` (import des
données de référence), `2a495357` (gate, 500 du dashboard, nettoyages différés, driver), puis ce bilan.

## Avant / après

| | Avant (`af91e325`) | Après (Tâche 12) |
|---|---|---|
| Projets dans `Vortex.Cloud.sln` | 58 | 47 |
| Fichiers `.cs` suivis | 7 142 | 4 710 |
| Lignes `.cs` (hors `Vortex.Database/Migrations`) | 469 704 | 289 813 |
| Lignes de migrations EF | 1 639 148 (139 migrations) | 29 418 (`InitialCreate` seule) |
| Lignes front (`.svelte`/`.ts`/`.js` suivis) | 59 995 | 44 029 |
| Lignes de seeds/scripts `.sql` | 28 729 | 22 604 (dont le nouvel import) |
| Fichiers suivis | 11 741 | 5 107 |

Projets retirés (11) : `Vortex.Benchmark`, `Vortex.Chain`, `Vortex.Chain.Tests`, `Vortex.Collectibles`,
`Vortex.Habbicons`, `Vortex.LoadGen`, `Vortex.RewardTracks`, `Vortex.Rewards.Tests`, `Vortex.Specs`,
`Vortex.Specs.Cli`, `Vortex.Specs.Tests`.

Tests (`dotnet test Vortex.Cloud.sln`, tous verts) :

| Projet de tests | Tâche 0 | Tâche 12 |
|---|---|---|
| Vortex.Rooms.Tests | 1 858 | 893 |
| Vortex.Signals.Tests | 480 | 363 |
| Vortex.Players.Tests | 447 | 316 |
| Vortex.Revisions.Tests | 381 | 259 |
| Vortex.WebApi.Tests | 299 | 230 |
| Vortex.Dashboard.Tests | 291 | 233 |
| Vortex.Database.Tests | 266 | 161 |
| Vortex.Hosting.Tests | rouge au baseline (9 échecs dus aux docs supprimées), non compté | 215 |
| Vortex.Crypto.Tests | 152 | 64 |
| Vortex.Authentication.Tests | 68 | 68 |
| Vortex.PacketHandlers.Tests | 62 | 60 |
| Vortex.Navigator.Tests | 60 | 61 |
| Vortex.Plugins.Tests | 40 | 40 |
| Vortex.Shop.Tests | 19 | 19 |
| Vortex.Pipeline.Tests | 13 | 13 |
| Vortex.Specs.Tests / Rewards.Tests / Chain.Tests | 220 / 147 / 22 | supprimés |
| **Total** | **4 825** (+ Hosting) | **2 995** |

## Décisions par défaut appliquées (plan, section « Décisions prises par défaut »)

Toutes appliquées telles qu'écrites :
1. Coffre de gains (vault) déplacé dans `Vortex.Players` (Tâche 2).
2. Objectifs communautaires gardés, paiement en VortexCoin retiré (Tâche 3).
3. Échanges : furni seulement, NFT retirés (Tâche 4).
4. Caméra retirée avec les photos web (Tâche 7).
5. Pets : énergie/faim/soif constantes, plus de nourriture ; niche et jouets gardés ; `CustomizePetWithFurni`
   gardé pour la selle seule (Tâche 8, selle = `FurnitureCategory.PetSaddle` 16).
6. Crackable, boîtes à récompense et cadeau de bienvenue retirés ; `CrackableStuffData` garde son format (Tâche 7).
7. Ballon de foot : `furniture_pushable` part avec le football, les balles prennent la logique par défaut (Tâche 6).
8. Word quiz retiré avec les sondages (Tâche 9).
9. Navigator réduit à Mes rooms, Rooms des amis, Mes groupes / QG de guilde, recherche et création (Tâche 10).
10. Habbo Club et rentable spaces gardés.

## Arbitrages du contrôleur (ledger, lignes « Ruling »)

- Une autre session Claude vivait sur l'arbre : chaque commit nomme ses chemins, `git status` avant commit.
- R1 : la Tâche 11 migre dans une base jetable (`vortex_m0_check`) au lieu de `DROP DATABASE vortex`.
- R2 : FastCheck **et** les projets de tests touchés à chaque tâche.
- R3 : la casse des tests sur `docs/architecture-v4` est traitée en Tâche 1 (WorkflowStateTests supprimés ;
  `interleaving-manifest.yaml` et `single-silo-inventory.yaml` déplacés en données de test).
- R4 : le contrôleur fait les suppressions en masse (`git rm -r`) après refus de `rm -rf` par le classifieur ;
  les implémenteurs font `git rm` fichier par fichier.
- R5 : la Tâche 5 déplace elle-même `IWiredCounter`/`WiredCountdownClock`/`WiredBotChatType` avant de supprimer
  leurs dossiers.
- Tâche 5 : l'API de commandes de bot sans appelant (`SayAsync`, Teleport/WalkTo/SetFollowTarget/SetFigure)
  est **gardée** : primitives de PNJ/monstres pour M1.
- Tâche 7 : `MysteryBoxColors` inliné dans `PrizeVariantsTests` (aucun code de prod ne lit les couleurs).
- Tâche 7 : prize pools gardés sans consommateur en jeu (tables de loot possibles en M1).
- Tâche 7 : anciens posts Photo/Album du fil (Kind 1/3) rendus vides — sans objet sur base neuve.
- R6 : l'étape 6 de la Tâche 11 (migrer une base vide) faite par le contrôleur sur `vortex_m0_check`.
- Tâche 11 : la ré-application des 9 seeds dépendants des furnis après import est portée par le script
  d'import de la Tâche 12 (Step 0).

## Écarts rencontrés

- Pré-étape `adb5a06c` : 4 135 suppressions préparées à la main par le propriétaire, commitées avant la Tâche 1.
- Tâche 4 : `check-authorization-surface` / `check-inert-declarations` rouges avant M0 ; vérifié en Tâche 12
  (voir « Contrôles hors gate »).
- Tâche 5 : `check-sibling-divergence` (+ baseline) et `ConfiguredBudgetTests` supprimés (ne vérifiaient
  plus que des wired) ; 88 constantes d'en-tête devenues orphelines retirées.
- Tâche 6 : `TestOnlyGame` modelé sur `RoomGameModule` (aucun jeu supprimé n'était plus petit).
- Tâche 8 : correction en cours de route — `PetInfo` doit renseigner `HasFreeSaddle`/`IsRiding`, sinon le client
  ne propose jamais « monter ». Nouvelle constante `PetConfig.NestNapMs = 60 s`.
- Tâche 10 : les tests d'ouverture de cadeau, supprimés avec `CatalogRoomAdAndPresentTests`, sont revenus (`65f01779`).
- Tâche 11 : `highscore_families.sql` ne reprend pas le renommage `furniture_high_score` (aucune logique serveur
  ne porte ce nom) ; une base neuve n'a ni modèles de room, ni définitions, ni catalogue → décision du
  propriétaire : script d'import réutilisable (Tâche 12, Step 0).
- Tâche 12 : une base neuve n'a pas non plus les monnaies Crédits / Duckets / Diamants (aucune migration ne les a
  jamais semées ; elles venaient de l'ancien convertisseur). L'import les ajoute depuis la source. **À trancher** :
  une base sans import n'a pas de crédits.
- Tâche 12 : le driver du skill `run-vortex-emulator` écrivait en dur dans `turbo` (compte opérateur de `login`) et
  attendait le port de jeu 40000 ; il lit maintenant la base des user-secrets et choisit 40000/30000 selon la
  présence d'`appsettings.Development.json`.

## Import des données de référence (Tâche 12, Step 0)

`scripts/import-reference-data.ps1 -Source turbo -Target vortex_m0_check` (défauts). Copie, colonnes communes
seulement (calculées depuis `information_schema`), avec les ids de la source : `room_models`,
`furniture_definitions`, `furniture_purchasable_clothing`, `catalog_pages`, `catalog_offers`, `catalog_products`,
`catalog_club_offers`, `catalog_club_gifts`, `catalog_frontpage_items` (vidées puis rechargées) ; `currency_types`
fusionnée par type (Crédits, Duckets, Diamants ajoutés). Puis rejoue, dans l'ordre d'`InitialCreate` :
catalog_pet_products, guild_furni_logic, furni_logic_bindings, furni_stuff_data_types, pet_commands,
highscore_families, purchasable_clothing, furni_logic_self_named_rebind, categorize_room_decorations. Aucune donnée
de joueur copiée. Ré-exécutable (vérifié : deux passages, mêmes comptes, ~14 s).

Résultat sur `vortex_m0_check` : room_models 63, furniture_definitions 55 282, furniture_purchasable_clothing 3 936,
catalog_pages 2 168, catalog_offers 79 799, catalog_products 104 764, catalog_club_offers 4, catalog_club_gifts 5,
catalog_frontpage_items 7, currency_types 5, pet_commands 352.

629 définitions portent une clé de logique d'une feature retirée (178 clés, retombent sur la logique par défaut) :
furniture_crackable 353, wired `wf_*` 157 (144 clés), pet_food 28, furniture_pushable 19, furniture_effectbox 19,
freeze_* 11, pet_drink 10, battlebanzai_* 8, furniture_welcome_gift 8, football_goal_* 4, furniture_furnichest 3,
furniture_ecotron_box 3, monsterplant_seed 2, furniture_coinschest 2, furniture_mysterytrophy 1, furniture_mysterybox 1.

## Validation finale (Tâche 12)

**QualityGate** (`dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate`) : vert, 0 erreur.
Le premier passage échouait sur `dotnet format analyzers` (VSTHRD200, `ItemPlacedAchievementHandlerTests.Place`
sans suffixe `Async`) ; corrigé, avec deux `using` dupliqués (CS0105, `GroupDirectoryGrain`, `IGroupDirectoryGrain`).
Les trois étaient là avant M0 (fichiers identiques à `af91e325`). Avertissements restants, tous antérieurs à M0 :
CS8619 (`ChatStyleUnlockTests.cs:395`), NU1903 (`SQLitePCLRaw.lib.e_sqlite3` 2.1.10 dans 4 projets de tests),
NU1510 (`Microsoft.Extensions.Hosting` dans `Vortex.Main`). Flake connu depuis la Tâche 4 et antérieur à M0 :
`WordFilterReadsTests.TheDraftIsTried_BesideTheSavedList` a échoué 2 passages sur 5 sous la charge du gate (vert seul,
3/3) — l'essai à blanc du filtre a un délai de regex de 50 ms en temps réel (`WordFilterReads.DraftMatchTimeout`,
calqué sur le filtre vivant) et retombe sur « inchangé » quand la machine est chargée. Le dernier passage du gate
est vert.

**Contrôles hors gate** (non exécutés par FastCheck ni par la QualityGate) : `check-authorization-surface` et
`check-inert-declarations` sont rouges, mais **chaque entrée signalée l'était déjà à `af91e325`** (mêmes listes ;
M0 en a seulement fait disparaître). Authorization surface : 18 entrées (Feed ×7, Notifications ×3, forgotPassword ×2,
`ApplyDecorationAsync`, `SetAdjacentCustomStackHeightAsync`, 4 `IRoomSettings.*Rights*` passées de
`+explicit-rights` à sans). Inert declarations : 7 clés lues mais absentes du `ConfigKeyCatalog`
(`pets.command_obey_{base_chance,chance_per_level,max_chance}_percent`, `pets.respect_happiness_reward`,
`rooms.max_rooms_per_player_vip`, `word_filter.refused_message`, `word_filter.replacement`) et une entrée de baseline
périmée (`inbound-chain/SetRelationshipStatusMessage`). Pas de `--update` : à trancher par le propriétaire.

**Démarrage réel** sur `vortex_m0_check` (skill `run-vortex-emulator`) : l'hôte démarre, les deux plugins (dashboard,
web API) se chargent, `/health` → `Healthy`, base `up`, silo Orleans actif, providers chargés (63 modèles,
55 282 définitions, catalogue 2 169 pages / 79 711 offres). Ce qu'il a fallu, faute d'`appsettings.Development.json`
dans ce checkout : une paire RSA jetable, les ports de jeu 40000/40001 (WebStorm tient 30000/30001) et les plugins
**publiés** (`dotnet publish`), le `bin` d'un plugin n'ayant pas Swashbuckle.

**Parcours automatisé** (socket de jeu en clair + API) :
- inscription web → avatar → ticket SSO → `AuthenticationOK` ;
- création de room (`CreateFlat`, model_a) → entrée (`RoomReady`) → marche (`UserUpdate`) → chat (`Chat`, journalisé) ;
- achat de 3 furnis (crédits 200 → 197) : paravent 2×1 `furniture_multistate`, `es_tile` (`freeze_tile`, clé
  retirée), `gumus_sandik` (`furniture_crackable`, clé retirée) ;
- pose, déplacement + rotation, empilement (le coffre crackable posé à z = 1.0 sur le 2×1), ramassage (retour en
  inventaire) ; positions relues en base. Les deux clés retirées retombent sur `default_floor`
  (`WRN RoomObjectLogicProvider … falling back to default_floor`), sans erreur ;
- achievements progressés (4 lignes) ; après redémarrage, la room 1 se réhydrate depuis la base.
- dashboard : les 97 routes GET de `authorization-matrix.txt` interrogées (ids remplis) → 91 × 200, 5 × 404
  (objets inexistants), 1 × 400 (`/chatlogs` exige un filtre), **0 × 500**. Deux 500 trouvés au premier passage :
  `/player-rewards/{id}` (requête EF non traduisible, antérieure à M0) **corrigé** ;
  `/directory/rooms/{id}/occupants` sur une room inexistante (activation du grain qui lève, antérieur à M0) laissé.
- Antérieur à M0, non corrigé : republier le dashboard dans un hôte lancé casse le rechargement à chaud
  (`ObjectDisposedException: DefaultMeterFactory`), il faut redémarrer.

## Inventaire des champs réseau figés à une constante (pour l'étape de nettoyage protocole)

| Message | Champ | Valeur | Fichier |
|---|---|---|---|
| `AccountPreferencesEventMessageComposer` | `WiredMenuButton`, `WiredInspectButton`, `PlayTestMode`, `WiredWhisperDisabled`, `ShowAllNotifications` | `false` | `Vortex.PacketHandlers/Preferences/AccountPreferences.cs` |
| `AccountPreferencesEventMessageComposer` | `VariableSyntaxMode` / `UiStyle` | `1` / `""` | idem |
| `RoomRatingMessageComposer` | `CanRate` | `false` | `Vortex.Rooms/RoomService.cs` (entrée en room) |
| `PerkAllowancesMessageComposer` | `Perks[CAMERA].IsAllowed`, `Perks[NAVIGATOR_ROOM_THUMBNAIL_CAMERA].IsAllowed` | `false` (forcé) | `Vortex.PacketHandlers/Handshake/SSOTicketMessageHandler.cs` (`BuildPerkAllowances`) |
| `PetInfoMessageComposer` | `energy` | plafond du niveau (`MaxEnergy`) | `Vortex.PacketHandlers/Room/Pets/GetPetInfoMessageHandler.cs` |
| `PetInfoMessageComposer` | `canBreed`, `canHarvest`, `canRevive`, `hasBreedingPermission` | `false` | idem |
| `PetInfoMessageComposer` | `rarityLevel` | `0` | idem |
| `PetInfoMessageComposer` | `maxWellBeingSeconds`, `remainingWellBeingSeconds`, `remainingGrowingSeconds` | `-1` | idem |
| `PetStatusUpdateMessageComposer` | `canBreed`, `canHarvest`, `canRevive`, `hasBreedingPermission` | `false` | `Vortex.Rooms/Grains/Systems/RoomPetSystem.Riding.cs` (`SendPetStatusAsync`) |
| Bloc avatar de pet (`Users`/`UserUpdate`) | `rarityLevel` | `-1` | `Vortex.Revisions/Revision20260701/Serializers/Room/Engine/Data/RoomAvatarSerializer.cs` |
| Bloc avatar de pet | `canBreed`, `canHarvest`, `canRevive`, `hasBreedingPermission` | `false` | idem |
| `PetData` (inventaire de pets, `PetAddedToInventory`…) | `rarityLevel` | `-1` | `Vortex.Revisions/Revision20260701/Serializers/Inventory/Pets/Data/PetDataSerializer.cs` |
| `PetRespectNotification` | `rarityLevel` | `-1` | `Vortex.Revisions/Revision20260701/Serializers/Users/PetRespectNotificationEventMessageComposerSerializer.cs` |
| `ModeratorUserInfoEventMessageComposer` (2589) | `userClassification` | `""` | `Vortex.Revisions/Revision20260701/Serializers/Moderation/ModeratorUserInfoEventMessageComposerSerializer.cs` (`[Id(16)]` retiré) |
| `FavouritesMessageComposer` (login) | `Limit` / `FavoriteRoomIds` | `0` / `[]` | `Vortex.PacketHandlers/Handshake/SSOTicketMessageHandler.cs` |
| `NavigatorCollapsedCategoriesMessage` (init) | liste | vide | `Vortex.PacketHandlers/NewNavigator/NewNavigatorInitMessageHandler.cs` |
| `NavigatorSavedSearchesMessage` (init) | liste | vide | idem |
| `GetGuestRoomResult` | staff pick | `false` | `Vortex.Revisions/Revision20260701/Serializers/Navigator/GetGuestRoomResultMessageComposerSerializer.cs` |
| Données de room invitée (`RoomSettingsSerializer`) | bit `RoomAd` (4) / `DisplayRoomAd` (32) | jamais posés (trio nom/description/minutes de pub jamais écrit) | `Vortex.Revisions/Revision20260701/Serializers/Navigator/Data/RoomSettingsSerializer.cs` |
| `NavigatorSearchResultBlock` | `ViewMode` | `Rows` (0) | `Vortex.Navigator/NavigatorService.cs`, `Vortex.PacketHandlers/Navigator/NavigatorSearchHandlerHelper.cs` |
| `NavigatorLiftedRoomsMessage` | liste | vide (déjà avant M0) | — |
| `RoomSettingsDataEventMessageComposer` | `hiddenByBc` | constante (déjà avant M0) | `Vortex.Revisions/Revision20260701/Serializers/RoomSettings/RoomSettingsDataEventMessageComposerSerializer.cs` |

Rareté d'un pet : pas de constante neutre unique (`-1` bloc avatar/PetData/respect, `0` PetInfo).

Messages retirés entièrement (pas figés) : jukebox (AddJukeboxDisk 1637, GetJukeboxPlayList 3707, GetNowPlaying 1281,
RemoveJukeboxDisk 2003), OpenMysteryTrophy 2242, MysteryBoxWaitingCanceled 1063, `MysteryBoxKeys` (plus envoyé au
login), RateFlat 407, les six événements caméra ; Builders Club (2378, 1893, 2458 ; entrants 3849, 2740, 1739) ; LTD
(3389, 1084 ; entrants 287, 3682) ; vouchers (1771, 133 ; entrant 2779) ; offres ciblées (2155, 2013 ; entrants 848,
2497, 2874, 3046) ; classification (543 ; entrants 157, 628) ; sondages (1297, 969, 579 ; entrants 3386, 1088, 743) ;
word quiz (2157, 1659, 2108) ; tout `UserDefinedRoomEvents` (wired) ; les messages navigator/room-ad listés dans le
rapport de la Tâche 10 (officiel, populaire, événements, favoris, recherches sauvegardées, vues). Un client stock qui
les envoie est journalisé « Incoming Unknown ». Les deux composers wired de l'entrée en room ne sont plus envoyés.

Contrats HTTP (pas le protocole de jeu) : `PlayerPurse` perd `buildersClubDays`/`buildersFurniLimit` (encore lus par
`vortex-rpg-client` `Purse.svelte` et `api.generated.ts`) ; le fil web perd photos/albums et leurs routes.

## Mineurs différés qui demandent une décision du propriétaire

- Achievements sans émetteur : `GamePlayed`, `MusicPlayer`, `MusicCollector` (et achievements 33/34), `HorseRent` —
  à supprimer si le RPG n'a ni musique ni ces jeux.
- Prize pools gardés sans consommateur en jeu (`GrantConsumedPrizeAsync` testé seulement, liaisons du dashboard
  inertes) : tables de loot en M1, ou suppression.
- API de commandes de bot sans appelant (`RoomBotSystem.SayAsync`, `Commands.cs` Teleport/WalkTo/SetFollowTarget/SetFigure,
  branche motion) : gardée pour les PNJ ; sa doc parle encore d'« a wired stack ».
- Monnaies de base absentes d'une base neuve sans import (voir Écarts).
- Éditeur d'objets en main du dashboard : édite encore `nutrition`/`thirst` (n'ont plus que le sens « un pet accepte cet objet »).
- `furnitureEnums.ts` a perdu les catégories 19-22 et pet_food/pet_drink/monsterplant_seed alors que les données en portent.
- `IGameCounter` : 5 membres inutilisés ; `GamePointsScoredEvent` sans traducteur (les arènes PvP en auront besoin).
- Spec RPG §10 cite `WiredProjectileEndToEndTests` comme modèle e2e de combat : supprimé en Tâche 5.
- `docs/codebase/**` décrit encore NFT, l'ancien historique de migrations et `SeedDashboardStaffActor`.
- Dashboard : l'entrée de menu « Pets » s'intitule encore « Population et élevage ».
- Le checkout du fork n'a pas d'`appsettings.Development.json` ; le web API et le dashboard ne démarrent depuis
  leur `bin` que publiés (Swashbuckle) — antérieur à M0, à outiller si le fork doit tourner sans ce fichier.

## Checklist propriétaire (client `vortex-modern-client`, émulateur laissé lancé)

L'émulateur laissé lancé tourne avec une **paire RSA jetable** : si le client vérifie la clé du serveur, il faut
le relancer avec la vraie (celle de `vortex-emulator`), par user-secrets
(`dotnet user-secrets set Vortex:Crypto:PublicKey … --project Vortex.Main`, idem `PrivateKey`) ou en recopiant son
`appsettings.Development.json` à la racine (ignoré par git ; les user-secrets gardent `vortex_m0_check`). Puis :
`node .claude/skills/run-vortex-emulator/driver.mjs stop` et `start`, avec les variables de ports et de plugins de
la section Gotchas du skill. Ports : jeu TCP 40000, WebSocket 40001, web API 8080, dashboard 9000.

Construction de map (exigence du propriétaire) :
- [ ] furni 1×1 : poser, déplacer, tourner ;
- [ ] furni multi-cases (2×1, 2×2) : poser, déplacer, tourner (l'empreinte suit la rotation) ;
- [ ] empiler sur un furni empilable ; hauteur d'empilement personnalisée (stack helper) ; refus sur un non empilable ;
- [ ] objet mural : poser, déplacer, ramasser ;
- [ ] logiques gardées : porte (`gate`), téléporteur (paire liée, aller-retour), dimmer, cabine d'essayage ;
- [ ] clés retirées : une boîte wired, une dalle freeze, une balle de foot, un jukebox, un coffre crackable, une
      mystery box — chacun se charge, se pose, se déplace comme un furni par défaut (pas d'erreur côté serveur) ;
- [ ] éditeur de plan d'étage : modifier, sauvegarder, la room se recharge ;
- [ ] quitter la room puis y revenir : toutes les positions, rotations et hauteurs sont conservées.

Reste du parcours de fumée du plan :
- [ ] connexion SSO depuis le client (handshake chiffré) ;
- [ ] pet : placement, commande, caresse, selle puis monture (menu « monter » proposé) ;
- [ ] échange de furni entre deux joueurs ;
- [ ] navigator : Mes rooms, Rooms des amis, Mes groupes ;
- [ ] dashboard : ouvrir chaque entrée du menu à l'écran (les routes API sont déjà vertes).
