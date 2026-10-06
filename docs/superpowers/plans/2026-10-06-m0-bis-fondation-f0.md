# M0 bis — Fondation F0 : un projet propre — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** retirer de `vortex-rpg-emulator` tout ce que l'audit et le propriétaire ont classé « retirer », renommer pour le RPG ce qui est gardé, puis réorganiser la solution en couches (Cœur / Plateforme / RPG / Web / Exploitation) avec un mur de couches vérifié à chaque build.

**Architecture :** d'abord les retraits (moins de code à déplacer ensuite), du plus mort au plus couplé ; puis les renommages ; puis les références et le mur de couches ; puis les découpes de projets (Authentication, Shop) ; enfin la doc et la validation réelle. Chaque tâche retire une chose **entière** (code, protocole, handlers, base, dashboard, tests, seeds) et laisse un build et des tests verts.

**Tech stack :** .NET 9, Orleans, EF Core 9 + Pomelo MySQL, Svelte (dashboard), scripts node (`scripts/hooks/`), PowerShell (`scripts/ef.ps1`, `scripts/import-reference-data.ps1`).

**Spec :** `docs/superpowers/specs/2026-10-06-vortex-rpg-design.md` (§3.4 bis couches, §22.1 bis décisions de l'audit) et `docs/superpowers/audits/2026-10-06-audit-mmorpg.md` (inventaire). Lire les deux avant de commencer. Le bilan du M0 (`docs/superpowers/plans/2026-10-05-m0-menage-serveur.bilan.md`) décrit l'état de départ.

## Global Constraints

- Une tâche = un commit (ou une suite de commits nommés `m0bis: …`). Avant chaque commit : `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck` vert. FastCheck lance `dotnet test Vortex.Cloud.sln`.
- Commits : `guard-commit.mjs` bloque un `git commit` qui ne nomme rien et `git add -A`/`.`. Utiliser `git commit -o <chemins> -F <fichier-message>`. Ne **jamais** inclure `docs/superpowers/specs/2026-10-06-vortex-mmorpg-master-design.md` (fichier du propriétaire, indexé, hors plan). Le message se termine par :
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` puis `Claude-Session: https://claude.ai/code/session_01JXCECoyL2rir7nZ7PFTkv3`.
- Suppressions : `git rm` / `git rm -r` (jamais `rm -rf`). Déplacements : `git mv` (garder l'historique). Projets : `dotnet sln Vortex.Cloud.sln remove|add <csproj>`, ne pas éditer le `.sln` à la main.
- `csharpier` reformate un `.cs` après chaque `Edit` (hook `post-edit.mjs`) : relire avant l'`Edit` suivant. Après une édition par script : `dotnet csharpier format <fichiers>`.
- **Forme réseau** : un composer **gardé** garde sa forme ; un champ devenu mort reçoit une valeur fixe (`false`/`0`/`""`) et est noté dans l'inventaire des champs figés du bilan (le retrait se fera avec le client, Task 12 du plan client). Supprimer un handler **et** son parser est sans risque pour le client actuel : un paquet sans parser est ignoré avec un avertissement, sans compter d'erreur (`Vortex.Networking/Package/PackageHandler.cs:65-76`).
- **Valeurs persistées** (enums, `[Id(n)]` Orleans) : retirer **sans renuméroter** ; ne jamais réutiliser un `[Id]`.
- **Base de données** : il n'existe encore aucune base à conserver (la spec §2 et le M0 : bases jetables `vortex_m0_check*`). Pendant tout le M0 bis, la migration reste **une seule `InitialCreate`, régénérée** quand le modèle change (procédure en Task 14), en gardant les appels de seeds dans le même ordre. Pas de migrations incrémentales.
- Dashboard : retirer une capability = la retirer des **4 fichiers** (`Vortex.Primitives/Permissions/Capabilities.cs` constante + `Dashboard.All`, `Vortex.Dashboard.Web/src/lib/dashboardPermissions.ts`, `routes.ts`, `locales/{en,fr}.js`), puis `Vortex.Authentication/Permissions/DefaultRoles.cs` (ou son successeur après la Task 11), `apiTypes.d.ts`, `Vortex.Dashboard.Tests/Hosting/authorization-matrix.txt`, `ApiTypeScriptContractTests.cs`, `DashboardResponseSchemaTests.cs`. `check-dashboard-capabilities.mjs` vérifie la parité.
- Avant de supprimer un appel dans du code gardé, lire le corps de la méthode appelée.
- **Les chemins et numéros de ligne de ce plan sont indicatifs** (relevés à `9a1d2d3a`). Se fier aux noms de symboles ; relire chaque fichier avant de le modifier. Chaque tâche a un **grep de contrôle** : il doit être vide (ou ne lister que ce que la tâche garde explicitement) avant le commit.
- Le M0 a laissé l'émulateur arrêté. Ne pas tuer un `Vortex.Main` qu'on n'a pas démarré (hook `guard-emulator.mjs`).

## Hors périmètre (jalons ultérieurs, ne pas toucher)

Navigator (M4, coordonné avec le client) ; scission du catalogue et interface catalogue joueur (M3) ; retrait des crédits et duckets (M3 : les prix du catalogue actuel en dépendent) ; or et marchands (M3) ; compagnons de combat (M5) ; MapEditor (F4) ; type de room, entité de zone, carte de navigation (F1) ; objet unique (F2).

## Review Focus

1. **Le client actuel envoie un paquet dont on a retiré le handler** (login, ouverture d'une fenêtre) → le serveur l'ignore sans déconnecter. Pin : Task 1, test de `PackageHandler` « header sans parser → pas d'échec compté ».
2. **La rafale de connexion (`SSOTicketMessageHandler`) envoie un composer d'une fonctionnalité retirée** (respects, objectifs communautaires, vue d'hôtel…) → elle garde la forme des composers gardés et n'envoie plus ceux retirés. Pin : `Vortex.Hosting.Tests/Architecture/RoomEntryPayloadTests` et un test de la rafale SSO adapté à chaque tâche qui touche `SSOTicketMessageHandler`.
3. **Un service ou un grain retiré reste injecté quelque part** (constructeur de grain, plugin dashboard, `[FromServices]` de la WebApi) → échec au démarrage ou 500 à chaque requête, invisible au build. Pin : Task 14, démarrage réel par le driver + `check-webapi-forwarding.mjs` à chaque tâche.
4. **Un meuble dont la logique est retirée** (roller, jouet, trampoline, niche) → se charge avec la logique par défaut, posable et déplaçable en maison. Pin : `RoomObjectLogicKeyTests` étendu en Task 4 et Task 8.
5. **Un renommage casse une identité Orleans ou une clé de configuration** (types de grains renommés, clés `ConfigKeyCatalog`) → le silo démarre et retrouve ses grains ; les clés de config renommées sont migrées dans les seeds. Pin : Task 9, test de `ConfigKeyCatalog` et démarrage réel Task 14.

---

### Task 0 : Branche et ligne de base

**Files :** aucun.

- [ ] **Step 1 : branche**

```bash
git switch -c m0-bis
```

- [ ] **Step 2 : ligne de base**

Run : `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck`
Expected : succès (dernier passage connu : 15 projets, 3 002 tests). Noter le nombre de tests par projet pour le bilan. Si rouge, s'arrêter et le signaler.

---

### Task 1 : Handlers vides et messages jamais envoyés

Retire les **74 handlers vides** (corps `await ValueTask.CompletedTask`) avec leur parser, leur message entrant et leur constante de header, puis les **composers jamais construits** par le code de production.

**Files :**
- Supprimer (handlers vides, avec parser + message + header) : `Vortex.PacketHandlers/{Game2,Competition,Crafting,Talent,Campaign,Youtube}/**`, les vides de `Gifts/` (vérification téléphone, 5), `Nux/` (cadeaux, 2), `Landingview/` (2), `Advertisement/InterstitialShown`, `Hotlooks`, `FriendFurni`, `RentOrBuyout` (3), `OpenPetPackage`, `SetAreaHideData`, `ChangeQueue`, `AmbassadorAlert`, `ChangeEmail`/`GetEmailStatus`, `GiveStarGem`, `CreditVault` (2), `BuyMarketplaceTokens`, `RoomNetworkOpenConnection`, `GetSeasonalCalendarDailyOffer` — liste exacte établie au Step 1.
- **Garder** (puits de télémétrie et de poignée de main, même vides) : `Pong`, `UniqueId`, `VersionCheck`, `FriendListUpdate`, `EventLog`, `LagWarningReport`, `LatencyPingReport`.
- Supprimer : les composers jamais construits (`Vortex.Protocol/Messages/Outgoing/**`) et leurs serializers (`Vortex.Revisions/Revision20260701/Serializers/**`), leurs mappings dans `Maps/*.cs`, leurs constantes de `Headers.cs`, leurs tests de forme réseau.
- Test : `Vortex.Revisions.Tests/…/UnknownHeaderTests.cs` (nouveau, ou dans le fichier de tests de `PackageHandler` existant).

**Interfaces :** aucune.

- [ ] **Step 1 : inventaire (constater)**

```bash
# handlers dont le corps n'est qu'un ValueTask.CompletedTask
grep -rlE "await ValueTask\.CompletedTask;|return ValueTask\.CompletedTask;" Vortex.PacketHandlers --include=*Handler.cs | xargs grep -L "Send\|Grain\|Async(" | sort > /tmp/empty-handlers.txt
wc -l /tmp/empty-handlers.txt
```
Relire chaque fichier listé : un handler n'est « vide » que si son corps ne fait rien d'autre. Retirer de la liste les 7 puits à garder. Expected : environ 74 fichiers. Écrire la liste finale dans le rapport.

Pour les composers : pour chaque type de `Vortex.Protocol/Messages/Outgoing/**`, chercher `new <Composer>` hors tests ; ceux sans occurrence (hors leur serializer et leurs tests) sont morts (environ 142). Écrire la liste dans le rapport. **Ne pas** retirer `EmeraldBalanceMessageComposer`, `SilverBalanceMessageComposer` (envoyés à la connexion) ni un composer construit par un test d'intégration seulement s'il est aussi construit en production.

- [ ] **Step 2 : test « header sans parser » (rouge si absent)**

Dans les tests de `PackageHandler` (chercher `PackageHandler` dans `*.Tests`), ajouter un test : un paquet dont le header n'a pas de parser n'incrémente pas `RecordParseFailure` et ne ferme pas la session. Suivre la structure du test existant le plus proche.

Run : `dotnet test <projet de test de PackageHandler> --filter "FullyQualifiedName~UnknownHeader"` → Expected : PASS (le comportement existe déjà, `PackageHandler.cs:65-76`) ; s'il échoue, le signaler (BLOCKED) au lieu de modifier `PackageHandler`.

- [ ] **Step 3 : supprimer handlers, parsers, messages, headers**

Pour chaque handler de la liste : `git rm` le handler, son parser (`Vortex.Revisions/Revision20260701/Parsers/**`), son message (`Vortex.Protocol/Messages/Incoming/**`), retirer son mapping dans `Maps/*.cs` et sa constante dans `Headers.cs`. Un dossier vidé est supprimé avec `git rm -r`.

- [ ] **Step 4 : supprimer les composers morts**

Pour chaque composer de la liste : `git rm` le composer, son serializer, son mapping, sa constante, et les tests de forme réseau qui ne testent que lui.

- [ ] **Step 5 : vérifier**

Run : `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck` → succès.
Contrôle : relancer le grep du Step 1 → ne reste que les 7 puits.

- [ ] **Step 6 : commit** — `m0bis: retire les handlers vides et les messages jamais envoyés`.

---

### Task 2 : Petits systèmes morts côté serveur

Systèmes sans rôle dans le MMORPG et sans dépendance forte : chansons, résolutions de succès et niveaux de compte, quiz, guides et revue de chat, CFH photo/selfie, reste `RewardTracks`, liens de téléporteurs morts, hub NFT.

**Files :**
- **Chansons** : `Vortex.Furniture/Providers/SongProvider.cs` (+ enregistrement `FurnitureModule.cs`), `Vortex.PacketHandlers/Sound/{GetOfficialSongId,GetSongInfo,GetSoundMachinePlayList,GetUserSongDisks}*` (+ parsers, messages, composers `OfficialSongId`, `PlayList`, `TraxSongInfo`, `UserSongDisksInventory`, serializers), `SongEntity` (+ DbSet), dashboard `SongsPage.svelte`, `SongAdminService`, `SongReads`, `DashboardEndpoints.Songs`, capabilities `SongsRead`/`OpsSongsManage` (4 fichiers), `DashboardPlugin.cs` (enregistrement du provider). **Garder** `GetSoundSettings`.
- **Résolutions de succès et niveaux de compte** : `PlayerAchievementResolutionGrain`, `AchievementResolutionRules`, `AccountLevelProvider`, `AccountLevelLadder` (Progression), entités `achievement_resolutions`, `player_achievement_resolutions`, `account_levels`, handlers `Game/Lobby` `Get/ResetResolutionAchievement` (+ composers `AchievementResolutions*`), dashboard `AchievementResolutionsPage`, seeds `achievement_resolutions.sql`, `account_levels.sql`. Le profil étendu (`GetExtendedProfile`) envoie `accountLevel` : **figer à 0** (champ réseau gardé). `players.achievement_score` reste.
- **Quiz, guides, revue de chat** : `Vortex.Social` `PlayerQuizGrain`, `GuideDirectoryGrain`, `ChatReviewDispatch`, `GuideDisconnectHandlers`, `QuizTranslator`, `GuideAdvertisementRead` ; handlers `Help/` `GuideSession*`, `ChatReview*`, `GetQuizQuestions`, `PostQuizAnswers` (+ parsers, messages, composers) ; `QuizSubmittedEvent` (`Primitives/Events/PlayerEvents.cs`) et son handler d'audit (`Observability/Events/SocialAuditHandlers.cs`) ; annuaire quiz du dashboard (`SignalDirectoryReads.cs`, `DashboardEndpoints.Directory.cs`) ; entités `quizzes`, `quiz_questions`, `player_quizzes` ; seed `quizzes.sql`. **Garder** le CFH (signalements) de `Help/`.
- **CFH photo/selfie** : `Help/CallForHelpFromPhoto*`, `CallForHelpFromSelfie*` (+ parsers, messages).
- **Reste M0** : `Vortex.Primitives/RewardTracks/*` (vérifier qu'aucun traducteur gardé ne lit `RewardTrackFacts`/`RewardTrackActions` ; sinon déplacer les constantes encore lues dans `Vortex.Primitives/Signals/` avant de supprimer) ; `FurnitureTeleportLinkEntity` (+ DbSet) ; handlers `Nft/GetNftCredits`, `Nft/GetSilver` (+ parsers, messages ; **garder** les composers `EmeraldBalance`/`SilverBalance` envoyés à la connexion).
- Tests : supprimer ceux qui ne testent que ces systèmes ; adapter `SeedCatalogTests` (liste des tables retirées), `VocabularyGovernanceTests`, `ConsumerTableTests`, `TranslatorCaseTests`.

- [ ] **Step 1 : constater**

```bash
grep -rIlE "SongProvider|SongEntity|AchievementResolution|AccountLevel|PlayerQuiz|QuizEntity|GuideDirectory|ChatReview|CallForHelpFrom(Photo|Selfie)|RewardTrack|FurnitureTeleportLink|GetNftCredits|GetSilverMessage" --include=*.cs --include=*.ts --include=*.svelte --include=*.js --include=*.sql --exclude-dir=bin --exclude-dir=obj --exclude-dir=node_modules --exclude-dir=Migrations . | sort
```
Expected : non vide. Garder la liste dans le rapport.

- [ ] **Step 2 : supprimer et adapter** (une sous-section à la fois, build entre chaque : chansons → résolutions/niveaux → quiz/guides/revue → CFH photo → reste M0).

- [ ] **Step 3 : vérifier** — relancer le grep du Step 1 → ne reste que `players.achievement_score` et la valeur figée `accountLevel` (commentée). FastCheck vert.

- [ ] **Step 4 : commit** — `m0bis: retire chansons, résolutions, niveaux de compte, quiz, guides, revue de chat et restes M0`.

---

### Task 3 : Styles de chat

Décision du propriétaire : les bulles de chat déblocables n'ont pas de rôle.

**Files :**
- Supprimer : `Vortex.Players/Grains/PlayerGrain.ChatStyles.cs`, `ChatStyleFulfillmentStrategy` (inventaire/catalogue), `ChatStyleBands`, entités `player_chat_styles`, `player_chat_styles_owned` (+ DbSets), handlers `Preferences/SetChatStylePreference`, `Users/GetUserNftChatStyles` (+ parsers, messages), composers `UserNftChatStyles`, `UserPurchasableChatStyles`, `UserPurchasableChatStyleChanged` (+ serializers, mappings, headers), la partie styles de chat de `PlayerRewardsPage` / `PlayerRewardReads.cs` / `ContentAdminService.Hotel.cs` (Grant/Revoke) / `DashboardEndpoints.Content.cs` / `ChatStyleGrantRequest`, `ProductType.ChatStyle` (sans renuméroter).
- Modifier : `PlayerEntity.room_chat_style_id` (retirer la colonne), `IRoomAvatar`/`RoomAvatar.LastChatStyleId` et `RoomService` (retirer), composers de chat sortants (`Chat`, `Shout`, `Whisper`) : le champ de style **reste sur le réseau** avec une valeur fixe `0` ; messages entrants de chat : le champ de style est lu et ignoré (forme gardée).
- Tests : supprimer `ChatStyleUnlockTests`, `ChatStyleWireTests` ; adapter `RoomEntryPayloadTests` (plus de composer de styles à l'entrée si l'un était envoyé), `PlayerRewardReadsTests` (ajouté au M0 : garder le test de l'ordre des récompenses restantes ou le supprimer s'il ne testait que les styles — le dire dans le rapport).

- [ ] **Step 1 : constater** — `grep -rIl "ChatStyle" --include=*.cs --include=*.ts --include=*.svelte --exclude-dir=bin --exclude-dir=obj --exclude-dir=Migrations --exclude-dir=node_modules .`
- [ ] **Step 2 : supprimer, figer les champs réseau, adapter les tests.**
- [ ] **Step 3 : vérifier** — grep → seulement les champs figés (commentés « figé, retiré avec le client »). FastCheck vert.
- [ ] **Step 4 : commit** — `m0bis: retire les styles de chat`.

---

### Task 4 : Rollers

Décision 6 : les rollers sont retirés.

**Files :**
- Supprimer : `Vortex.Rooms/Grains/Systems/RoomRollerSystem.cs` (et fichiers partiels), la logique `[RoomObjectLogic("roller")]`, l'étape de tick `"rollers"` et `RollerTickMs`/`NextRollerBoundaryMs` (`RoomGrain.cs`, `RoomConfig.cs`, `RoomLiveState.cs`), les composers et serializers propres aux rollers (ex. `SlideObjectBundle` si plus rien ne l'envoie — vérifier avant), les tests rollers.
- Modifier : `scripts/hooks/check-logic-groups.mjs` (retirer `roller` du groupe) puis `node scripts/hooks/check-logic-groups.mjs --write` ; `RoomObjectLogicKeyTests` : ajouter un cas « un meuble à logique `roller` se charge en logique par défaut ».

- [ ] **Step 1 : test (rouge)** — dans `Vortex.Rooms.Tests/Furniture/RoomObjectLogicKeyTests.cs`, ajouter `ARollerDefinition_LoadsAsDefaultLogic` : une définition `roller` produit `DefaultFloorLogic`. Run : `dotnet test Vortex.Rooms.Tests --filter "FullyQualifiedName~RoomObjectLogicKeyTests"` → FAIL (la logique roller existe encore).
- [ ] **Step 2 : supprimer** le système, la logique, l'étape de tick, les réglages.
- [ ] **Step 3 : vérifier** — le test passe ; `grep -rIl "Roller" --include=*.cs --exclude-dir=bin --exclude-dir=obj --exclude-dir=Migrations .` → vide (hors le nouveau test) ; FastCheck vert.
- [ ] **Step 4 : commit** — `m0bis: retire les rollers`.

---

### Task 5 : Respects

Décision 9 : respects quotidiens retirés (joueurs **et** pets) ; statut relationnel gardé.

**Files :**
- Supprimer : `TryGiveRespect` et le respect de pet (`PlayerGrain`, `RoomPetSystem.Care` partie respect), `RespectBudget`, `RespectConfig`, handlers `Users/RespectUser`, `ReplenishRespect`, `Room/Pets/RespectPet` (+ parsers, messages), composers `PetRespectNotification`, `RespectNotification` (s'il existe) et leurs serializers, traducteurs `PetRespected`, `Respect*` (Signals) et leurs règles de succès, clés `respect.*` de `ConfigKeyCatalog`.
- Modifier : `players.respect_*` (retirer les colonnes) ; composers gardés qui portent des compteurs de respect (infos utilisateur, profil, statut de pet) : **figer à 0**.
- **Garder** : `SetRelationshipStatus`, `GetRelationshipStatusInfo`.
- Tests : supprimer ceux du respect ; adapter `VocabularyGovernanceTests`, `ConsumerTableTests`, les tests de forme réseau des composers figés (octets inchangés).

- [ ] **Step 1 : constater** — `grep -rIlE "Respect" --include=*.cs --exclude-dir=bin --exclude-dir=obj --exclude-dir=Migrations .`
- [ ] **Step 2 : supprimer, figer, adapter.**
- [ ] **Step 3 : vérifier** — grep → seulement les champs figés commentés ; FastCheck vert.
- [ ] **Step 4 : commit** — `m0bis: retire les respects`.

---

### Task 6 : Extras retirés (décision 11)

Objectifs communautaires, derby de pêche, espaces à louer, protection anti-raid, bots possédés par les joueurs.

**Files :**
- **Objectifs communautaires** : `CommunityGoalGrain`, `CommunityGoalLadder`, objectif d'utilisateurs simultanés, entités `community_goals`, `community_goal_levels`, `player_community_goal_contributions`, handlers `Quest/GetCommunityGoal{Progress,HallOfFame}`, `GetConcurrentUsers{GoalProgress,Reward}` (+ parsers, messages, composers), contrats dashboard de quête associés (`CommunityGoalLevelRow`…). Si la rafale SSO envoie un composer d'objectif, le retirer de la rafale.
- **Derby de pêche** : `fishing_derbies`, `fishing_derby_entries`, la logique de derby de `Vortex.Fishing` et ses handlers/composers. **Garder** le reste de la pêche (future Pêche, M5).
- **Espaces à louer** : `RentableSpaceGrain`, `room_rentable_space_terms`, `room_rentable_spaces`, `furniture.rentable_space_furniture_id` (FK + colonne), le confinement « espace loué » de `RoomActionModule`, `RentableSpaceAuditHandlers`, `RentableSpaceEvents`, handlers `RentableSpace{Rent,CancelRent,Status}`, `ConfigureRentableSpace`, `GetRentableSpaceConfig` (+ parsers, messages, composers), onglet locations d'`EconomyExtrasPage`.
- **Protection anti-raid** : `RoomRaidProtectionSystem`, `RoomRaidProtectionState`, `Vortex.Primitives/Rooms/RaidProtection/*`, `RoomRaidDetectedEvent`, `room_raid_protection`, handlers `Room/RaidProtection/{Get,Save}RaidProtectionSettings` (+ parsers, messages, composers), le chemin d'entrée qui l'appelle dans `RoomGrain`, l'étape de tick `"raid-protection"`.
- **Bots possédés par les joueurs** : inventaire de bots (`InventoryGrain.Bots`, `GetBotInventory`, composer `BotInventory`), `PlaceBot`, `RemoveBotFromFlat`, `CommandBot`, `GetBotCommandConfigurationData` (+ parsers, messages, composers `BotCommandConfiguration`, `BotSkillListUpdate`), `BotFulfillmentStrategy` (achat de bots au catalogue). **Garder** `RoomBotSystem` (base des PNJ, Chatter, Motion, Commands gardés au M0), la table `bots` et la page dashboard Bots (lecture), et le chargement des bots d'une room.
- Capabilities : retirer celles qui ne servent plus qu'à ces systèmes (relever avec `grep` dans `Capabilities.cs`), 4 fichiers.
- Tests : supprimer ceux de ces systèmes ; adapter `RoomHarness`, `Vortex.Hosting.Tests/Architecture/*` (manifestes), `single-silo-inventory.yaml`.

- [ ] **Step 1 : constater** — `grep -rIlE "CommunityGoal|ConcurrentUsers|FishingDerby|RentableSpace|RaidProtection|BotInventory|PlaceBot|CommandBot|BotCommandConfiguration|BotFulfillment" --include=*.cs --include=*.ts --include=*.svelte --exclude-dir=bin --exclude-dir=obj --exclude-dir=Migrations --exclude-dir=node_modules .`
- [ ] **Step 2 : supprimer** — un système à la fois, build entre chaque, dans l'ordre : anti-raid → espaces à louer → objectifs communautaires → derby → bots possédés.
- [ ] **Step 3 : vérifier** — grep → vide ; `node scripts/hooks/check-webapi-forwarding.mjs` vert ; FastCheck vert.
- [ ] **Step 4 : commit** — `m0bis: retire objectifs communautaires, derby, espaces à louer, anti-raid et bots possédés`.

---

### Task 7 : Vue d'hôtel et fil social du site

Décision 10 : articles gardés ; fil social du site et vue d'hôtel retirés. À la connexion, le client actuel ouvre encore la vue d'hôtel : seuls les **contenus serveur** de la vue d'hôtel partent (le passage « choix du personnage → dernière zone » est F2).

**Files :**
- **Fil social** : `Vortex.WebApi` endpoints du fil (`public`, `user`, `vote`, `reaction`, flux SSE du fil), `WebApiFeedService`, entités `web_feed_posts`, `web_feed_poll_choices`, `web_feed_poll_votes`, `web_feed_reactions`, `web_feed_tags`, `web_feed_post_tags` (+ DbSets), dashboard `FeedAdminService`, `OpsFeedModerate` (4 fichiers), tests du fil (`Vortex.WebApi.Tests`), `openapi.json` (régénérer avec `UPDATE_OPENAPI=1` comme au M0).
- **Vue d'hôtel** : page dashboard `HotelViewPage` et son service/capability, handlers de vue d'hôtel restants (promos, `Landingview` non vides s'il en reste après la Task 1), composers de la vue d'hôtel envoyés par la rafale SSO (les retirer de la rafale).
- **Garder** : articles (`web_articles*`, `ArticlesPage`), notifications web, langues.

- [ ] **Step 1 : constater** — `grep -rIlE "WebFeed|Feed(Admin|Service|Post|Poll)|HotelView|Landingview|PromoArticle" --include=*.cs --include=*.ts --include=*.svelte --exclude-dir=bin --exclude-dir=obj --exclude-dir=Migrations --exclude-dir=node_modules .`
- [ ] **Step 2 : supprimer, régénérer `openapi.json`, adapter les tests.**
- [ ] **Step 3 : vérifier** — grep → vide ; `cd Vortex.Dashboard.Web && npm run lint && npm run test && npm run build` ; FastCheck vert.
- [ ] **Step 4 : commit** — `m0bis: retire la vue d'hôtel et le fil social du site`.

---

### Task 8 : Simulation de vie des pets

Décision du propriétaire et spec §9 : le compagnon suit et attaque ; plus de commandes Habbo, de paroles, d'objets en main, de jouets ni de niche. **La selle et la monture restent** (`CustomizePetWithFurni`, `GetPetInfo`, `RoomPetSystem.Riding`, `pets.saddle_definition_id`).

**Files :**
- Supprimer : `PetCommandProvider`, `PetVocalProvider` (+ contrats `Vortex.Primitives/Pets/Providers/*`, `PetCommandEntry`, enregistrements `PlayerModule.cs`), `RoomPetSystem.Care.cs` (commandes : `IssueCommandAsync`, `GrantPetCommandXpAsync`, paroles `BroadcastPetVocalAsync`), `RoomPetSystem.HandItem.cs`, la reconnaissance des commandes dans le chat (`RoomChatSystem` `TryIssuePetCommandFromChatAsync`), `IRoomPets.IssueCommandAsync`, `RoomGrain.Pets.cs` (entrées correspondantes), champs fournisseurs de `RoomGrain.cs`, logiques `pet_toy`, `pet_trampoline`, `nest`, `pet_nest`, la sieste de niche (`PetConfig.NestNapMs`, `StartNestNap`), `PetCommandObeyedEvent` et son fait Signals, handlers `GetPetCommands`, la liste de commandes de `PetSelected`, `PassCarryItemToPet` (+ parsers, messages, composers `PetCommands`), entités `pet_commands`, `pet_command_names`, `pet_vocals`, `hand_items` (+ DbSets), seeds `pet_commands.sql`, `pet_command_names.sql`, `pet_vocals.sql`, `hand_items.sql`, dashboard : annuaire des commandes de pets, éditeur d'objets en main (`ContentAdminService.Hotel.cs`, `BotReads.cs`, `InventoryReads.cs`).
- Modifier : `pets` — retirer `energy`, `nutrition`, `thirst`, `happiness`, `respect` ; composers d'infos et de statut de pet : ces champs **restent sur le réseau**, figés (maximum pour énergie/nutrition/soif, valeurs actuelles du M0) ; `RoomPetRuntime` et le mouvement des pets : le pet suit son maître et erre, sans besoin ni commande. `pet_levels` reste (XP de compagnon en M5).
- `check-logic-groups.mjs` : retirer les clés de pets supprimées, puis `--write`.
- Tests : supprimer `PetCommandSeedTests`, `PetCommandsWireTests`, `PetHandItemTests`, `PetNestNapTests`, `PetNestSeekingTests`, `PetToyAndTuningTests` (ou leurs parties) ; adapter `RoomHarness`, `PetCareTests`, `PetActionFlagsTests` ; **garder et faire passer** `PetSaddleTests`, `GetPetInfoMessageHandlerTests`, les tests de forme réseau des infos et du statut de pet (octets inchangés) ; `RoomObjectLogicKeyTests` : un meuble `pet_toy`/`nest` se charge en logique par défaut.

- [ ] **Step 1 : test (rouge)** — `RoomObjectLogicKeyTests` : `APetToyOrNestDefinition_LoadsAsDefaultLogic` → FAIL.
- [ ] **Step 2 : supprimer et adapter** (fournisseurs → système de soins → chat → logiques → base → dashboard).
- [ ] **Step 3 : vérifier** — `grep -rIlE "PetCommand|PetVocal|HandItem|NestNap|pet_toy|pet_trampoline|IssueCommandAsync" --include=*.cs --include=*.sql --include=*.ts --include=*.svelte --exclude-dir=bin --exclude-dir=obj --exclude-dir=Migrations --exclude-dir=node_modules .` → vide ; `PetSaddleTests` vert ; FastCheck vert.
- [ ] **Step 4 : commit** — `m0bis: pets sans commandes, paroles, objets en main, jouets ni niche ; selle et monture gardées`.

---

### Task 9 : Renommages RPG

Décisions 3, 5 et 12. Renommer **le code et la base**, jamais les messages réseau (les composers Club gardent leur nom et leur forme jusqu'à l'étape protocole).

| Ancien | Nouveau | Portée |
|---|---|---|
| Habbo Club (`Vortex.Players/Club/`, `Club*`, `SubscriptionType.HabboClub`, `player_subscriptions`, `player_kickback`, `catalog_club_offers`, `catalog_club_gifts`, `ClubMetrics*`, `ClubAuditHandlers`, `ClubNotificationHandlers`, page `SubscriptionsPage`) | **Abonnement** — code `Membership` (`Vortex.Players/Membership/`, `PlayerMembership*`, `MembershipType.Premium`, tables `player_memberships`, `player_membership_kickbacks`, `membership_offers`, `membership_gifts`) | Serveur, dashboard, base, config |
| Coffre de gains (`PlayerVaultGrain`, `player_vault_income_rewards`, `Vault*`) | **Coffre de récompenses** — code `RewardVault` (`Vortex.Players/RewardVault/`, `PlayerRewardVaultGrain`, table `player_reward_vault_entries`) | Serveur, base |
| Prize pools (`PrizePoolManagerGrain`, `PlayerPrizeGrain`, `Vortex.Primitives/Prizes`, `prize_pools`, `prize_pool_entries`, `prize_pool_bindings`, `player_prize_claims`, page `PrizePoolsPage`, capabilities `PrizePools*`) | **Tables de loot** — code `LootTable` (`LootTableManagerGrain`, `PlayerLootGrain`, `Vortex.Primitives/Loot`, tables `loot_tables`, `loot_table_entries`, `loot_table_bindings`, `player_loot_claims`, page `LootTablesPage`, capabilities `LootTables*`) | Serveur, dashboard, base |
| Palettes de pets (`PetPaletteProvider`, `pet_palettes`, `GetSellablePetPalettes`) | **Skins de pets** — code `PetSkin` (`PetSkinProvider`, table `pet_skins`) ; la notion de race disparaît du modèle (chaque pet est unique) | Serveur, base. Le handler `GetSellablePetPalettes` et son composer gardent leur nom réseau |

Règles : `git mv` des fichiers, renommage des types et namespaces, des DbSets et des noms de tables (`[Table("…")]`/`ToTable`), des clés de `ConfigKeyCatalog` (mettre à jour les seeds `server_config` qui les écrivent), des capabilities (4 fichiers + `DefaultRoles` + tests), des locales. Les valeurs d'enums persistées gardent leur **nombre**. Les identités Orleans changent avec les noms de classes : sans base à conserver, c'est accepté.

**Files :** tous ceux nommés dans le tableau ; relever le reste par grep.

- [ ] **Step 1 : test (rouge)** — `Vortex.Hosting.Tests` : un test qui vérifie que `ConfigKeyCatalog` ne contient plus de clé `club.*` ni `prize_pools.*` et contient leurs remplaçantes `membership.*` et `loot_tables.*` (relever les clés réelles d'abord). Run → FAIL.
- [ ] **Step 2 : renommer** — un renommage à la fois (Abonnement → Coffre → Tables de loot → Skins), build et tests entre chaque.
- [ ] **Step 3 : vérifier** — `grep -rIlE "HabboClub|\bClub[A-Z]|PrizePool|PlayerPrize|PetPalette|PlayerVault" --include=*.cs --include=*.ts --include=*.svelte --include=*.sql --exclude-dir=bin --exclude-dir=obj --exclude-dir=Migrations --exclude-dir=node_modules .` → seulement les noms de **messages réseau** gardés (composers et handlers `Club*`, `GetSellablePetPalettes*`) ; le test du Step 1 passe ; FastCheck vert.
- [ ] **Step 4 : commit** — `m0bis: renomme pour le RPG (abonnement, coffre de récompenses, tables de loot, skins de pets)`.

---

### Task 10 : Références périmées et mur de couches

**Files :**
- Modifier : `Vortex.PacketHandlers/Vortex.PacketHandlers.csproj` (retirer les références à `Vortex.Catalog`, `Vortex.Players`, `Vortex.Progression`, `Vortex.Social`) ; déplacer dans `Vortex.Primitives` les 3 types qu'il utilise (`CatalogPurchaseException` et voisins de `Vortex.Catalog.Exceptions`, la config de `Vortex.Players.Configuration` et celle de `Vortex.Social.Configuration` réellement lues) — relever avec `grep -rn "using Vortex.\(Catalog\|Players\|Social\|Progression\)" Vortex.PacketHandlers`.
- Modifier : `.csproj` de `Vortex.Authentication`, `Vortex.Catalog`, `Vortex.Furniture`, `Vortex.Inventory`, `Vortex.Marketplace`, `Vortex.Navigator` : retirer la référence inutilisée à `Vortex.Players` (vérifier qu'aucun fichier ne l'utilise).
- Modifier : `scripts/hooks/check-architecture-walls.mjs` : ajouter un **mur de couches** — Cœur (`Networking`, `Pipeline`, `Protocol`, `Messages`, `Revisions`, `PacketHandlers`, `Runtime`, `Crypto`, `Database`, `Events`, `Logging`, `Observability`, `Plugins`, `Primitives`) ne référence aucun projet Plateforme (`Rooms`, `Players`, `Inventory`, `Furniture`, `Social`, `Marketplace`, `Signals`, `Progression`, `Fishing`, `Catalog`, `Permissions`, `Navigator`), Web (`WebApi*`) ni Exploitation (`Dashboard.*`) ; Plateforme ne référence ni Web ni Exploitation. `Main` (hôte) est exempté. Mettre à jour `architecture-walls-baseline.json` si nécessaire (il doit rester vide pour le nouveau mur).
- Test : `scripts/hooks/__test/run.mjs` : un cas « une référence Cœur → Plateforme est bloquée » (sonde temporaire, comme les cas existants) et un cas « l'arbre actuel passe ».

- [ ] **Step 1 : test (rouge)** — ajouter le cas sonde dans `run.mjs`. Run : `node scripts/hooks/__test/run.mjs` → FAIL (le mur n'existe pas).
- [ ] **Step 2 : déplacer les 3 types, retirer les références.**
- [ ] **Step 3 : écrire le mur.**
- [ ] **Step 4 : vérifier** — `node scripts/hooks/__test/run.mjs` → PASS ; `node scripts/hooks/check-architecture-walls.mjs` → vert ; FastCheck vert.
- [ ] **Step 5 : commit** — `m0bis: références périmées retirées, mur de couches`.

---

### Task 11 : Scission de Vortex.Authentication

Spec §3.4 bis : la partie compte web devient `Vortex.WebApi.Authentication` ; permissions, rôles, sanctions, catalogue de signalements et filtre de mots deviennent `Vortex.Permissions` ; la validation du ticket de connexion au jeu passe côté Plateforme.

**Files :**
- Créer : `Vortex.WebApi.Authentication/Vortex.WebApi.Authentication.csproj` ← `AccountAuthenticator`, `AccountEmailService`, `AccountMfaService`, `AccountPasswordService`, `AccountSafetyLockService`, `AccountSafetyQuestionsService`, `AccountTrustedLocationService`, `Configuration/AuthenticationConfig*` (`git mv`).
- Créer : `Vortex.Permissions/Vortex.Permissions.csproj` ← `Permissions/*` (`PermissionService`, `DefaultRoles`, `DefaultSanctionPresets`, `PermissionSeederService`, `SanctionPresetSeederService`, `SanctionPresetService`), `Moderation/*` (`CfhCatalogSeederService`, `DefaultCfhCatalog`), et `Vortex.Observability/WordFilter/*` (`git mv`).
- Déplacer : `AuthenticationService` (validation du ticket SSO de jeu) et `PluginSessionRevoker` vers le projet Plateforme qui convient (`Vortex.Players`), interface dans `Vortex.Primitives` si `PacketHandlers` l'appelle.
- Modifier : `AuthenticationModule.cs` → deux modules (`WebApiAuthenticationModule`, `PermissionsModule`) ; `Vortex.Main/Program.cs` et `Vortex.Main.csproj` ; `WebApiWebHost.ForwardSingletons` et `WebApiTestFactory` (services forwardés) ; namespaces ; `Vortex.Authentication.Tests` → `Vortex.Permissions.Tests` et `Vortex.WebApi.Authentication.Tests` selon les sujets ; mur de couches (Permissions = Plateforme, WebApi.Authentication = Web).
- `dotnet sln Vortex.Cloud.sln remove Vortex.Authentication/Vortex.Authentication.csproj` puis `add` des nouveaux projets.

- [ ] **Step 1 : constater** — lister les consommateurs : `grep -rln "Vortex.Authentication" --include=*.cs --include=*.csproj --exclude-dir=bin --exclude-dir=obj .`
- [ ] **Step 2 : créer les deux projets, `git mv`, renommer les namespaces, enregistrer les modules.**
- [ ] **Step 3 : vérifier** — `node scripts/hooks/check-webapi-forwarding.mjs` vert ; `node scripts/hooks/check-architecture-walls.mjs` vert ; `grep -rn "Vortex.Authentication" --include=*.cs --include=*.csproj --exclude-dir=bin --exclude-dir=obj .` → vide ; FastCheck vert (même nombre de tests qu'avant la tâche, répartis).
- [ ] **Step 4 : commit** — `m0bis: Authentication scindé en WebApi.Authentication et Permissions`.

---

### Task 12 : Vortex.Shop → Vortex.WebApi.Shop

**Files :**
- `git mv Vortex.Shop Vortex.WebApi.Shop`, renommer le `.csproj`, le namespace (`Vortex.Shop` → `Vortex.WebApi.Shop`), `ShopModule` ; `Vortex.Shop.Tests` → `Vortex.WebApi.Shop.Tests` ; `Vortex.Main/Program.cs`, `Vortex.Main.csproj` ; `WebApiWebHost.ForwardSingletons`, `WebApiTestFactory` ; mur de couches (Web) ; `dotnet sln remove/add`.

- [ ] **Step 1 : déplacer et renommer.**
- [ ] **Step 2 : vérifier** — `grep -rn "Vortex\.Shop\b" --include=*.cs --include=*.csproj --exclude-dir=bin --exclude-dir=obj .` → vide ; `check-webapi-forwarding.mjs` et `check-architecture-walls.mjs` verts ; FastCheck vert (19 tests de Shop toujours là).
- [ ] **Step 3 : commit** — `m0bis: Shop devient WebApi.Shop`.

---

### Task 13 : Documents de règles

**Files :** `AGENTS.md`, `CONTEXT.md`, `CLAUDE.md`, `.github/copilot-instructions.md`, `docs/codebase/**` (sections décrivant wired, collectibles, navigator complet, chansons, rollers, respects…).

- `AGENTS.md` : retirer les liens vers `docs/architecture/` (supprimé), corriger `dashboardPermissions.js`/`routes.js` en `.ts`, remplacer la recette « Add a room game » (exemple Freeze, `tools/catalog_converter`) par une recette sans exemple supprimé ou la retirer, remplacer `PurchaseGrain`/`LimitedItemGrain` par `CatalogPurchaseGrain`, décrire les **couches** (§3.4 bis) et le mur.
- `CONTEXT.md` : « six fichiers » de capability → quatre ; projets à jour (Permissions, WebApi.Authentication, WebApi.Shop).
- `CLAUDE.md` : tableau des automatisations à jour (mur de couches).
- `docs/codebase/**` : pour chaque fichier qui décrit un système retiré au M0 ou au M0 bis, supprimer le fichier s'il n'a plus d'objet, sinon retirer les sections ; mettre à jour les index (`.claude/gen-indexes.py` s'il les génère).

- [ ] **Step 1 : constater** — `grep -rIlE "architecture/dashboard|dashboardPermissions\.js|routes\.js|catalog_converter|Freeze|LimitedItemGrain|six files|wired|Wired" AGENTS.md CONTEXT.md CLAUDE.md .github docs/codebase`
- [ ] **Step 2 : corriger.**
- [ ] **Step 3 : vérifier** — grep → seulement des mentions historiques assumées (ex. « retiré au M0 ») ; `node scripts/hooks/__test/run.mjs` vert.
- [ ] **Step 4 : commit** — `m0bis: documents de règles à jour (couches, retraits)`.

---

### Task 14 : Migration régénérée, validation réelle, bilan

**Files :**
- Régénérer : `Vortex.Database/Migrations/*_InitialCreate*.cs` et le snapshot.
- Modifier : `Vortex.Database/Seeds/*` (retirer les seeds des tables supprimées, renommer les tables renommées), `scripts/import-reference-data.ps1` et `scripts/sql/import_reference_data.sql` si une table importée a été renommée ou supprimée, `SeedCatalogTests`.
- Créer : `docs/superpowers/plans/2026-10-06-m0-bis-fondation-f0.bilan.md`.

- [ ] **Step 1 : régénérer la migration**

```bash
git rm Vortex.Database/Migrations/*_InitialCreate.cs Vortex.Database/Migrations/*_InitialCreate.Designer.cs Vortex.Database/Migrations/VortexDbContextModelSnapshot.cs
pwsh scripts/ef.ps1 migrations add InitialCreate --context VortexDbContext
```
Recopier dans `Up` les appels `migrationBuilder.Sql(SeedScripts.Read("…"))` de l'ancienne migration (`git show HEAD:<ancien fichier>`), **même ordre**, sans les seeds supprimés. `MigrationHelper.cs` reste.

- [ ] **Step 2 : migrer et importer une base neuve**

Base jetable `vortex_m0bis_check` sur le MySQL Laragon (127.0.0.1:3306, root ; mot de passe lu dans `dotnet user-secrets list --project Vortex.Main`, jamais affiché ni écrit). `pwsh scripts/ef.ps1 database update` avec `Vortex__Database__ConnectionString` (non préfixée) vers cette base, puis `pwsh scripts/import-reference-data.ps1` (source `turbo` en lecture seule). Expected : migration sans erreur ; comptes d'import identiques au M0 (63 modèles de room, 55 282 définitions de meubles, 2 168 pages, 79 799 offres, 104 764 produits).

- [ ] **Step 3 : gate complet**

Run : `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate` → Expected : succès (relancer une fois si le seul échec est le test instable connu `WordFilterReadsTests.TheDraftIsTried_BesideTheSavedList`).

- [ ] **Step 4 : démarrage réel**

Pointer les user-secrets vers `vortex_m0bis_check` (`dotnet user-secrets set "Vortex:Database:ConnectionString" … --project Vortex.Main`, sans afficher le mot de passe), puis `node .claude/skills/run-vortex-emulator/driver.mjs smoke`. Expected : hôte démarré, aucun plugin en échec, connexion, création de room, pose/déplacement/empilement/ramassage d'un meuble, un meuble à logique retirée (roller, jouet de pet) chargé en logique par défaut, toutes les pages du dashboard sans 500. Arrêter ensuite l'émulateur avec `driver.mjs stop`.

- [ ] **Step 5 : bilan**

Dans `2026-10-06-m0-bis-fondation-f0.bilan.md` : projets, lignes et tests avant/après ; décisions appliquées ; écarts ; **inventaire des champs réseau figés ajoutés au M0 bis** (message, champ, valeur, fichier), à joindre à celui du M0 pour l'étape protocole du client ; checklist du propriétaire avec le client (connexion, room, construction de map, pet sellé et monture, échange de meubles).

- [ ] **Step 6 : commit** — `m0bis: migration régénérée, validation réelle et bilan`.
