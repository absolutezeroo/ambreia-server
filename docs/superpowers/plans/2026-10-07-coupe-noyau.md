# Coupe au noyau (F0 ter) — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** réduire `vortex-rpg-emulator` à son noyau — réseau, protocole, heightmap, pathfinding, moteur de room (futur moteur de zone), connexion classique — et retirer tout le reste, dashboard compris.

**Architecture :** on retire par blocs entiers, de l'extérieur vers le cœur : l'exploitation (dashboard), le web, l'économie, le social, la progression, puis l'intérieur des projets gardés (joueurs, rooms, permissions). Une tâche ajoute ensuite le seul comportement nouveau (zone de développement et envoi du joueur dedans à la connexion). Un balayage final retire les messages, contrats et tables devenus orphelins, puis la migration est régénérée et le tout validé en réel.

**Tech stack :** .NET 10, Orleans, EF Core + Pomelo MySQL, scripts node (`scripts/hooks/`), PowerShell (`scripts/ef.ps1`, `scripts/import-reference-data.ps1`).

**Spec :** `docs/superpowers/audits/2026-10-07-coupe-noyau.md` (liste validée le 2026-10-07, avec ses « Décisions ») et `docs/superpowers/specs/2026-10-06-vortex-rpg-design.md`. Le bilan du M0 bis (`docs/superpowers/plans/2026-10-06-m0-bis-fondation-f0.bilan.md`) décrit l'état de départ.

## Global Constraints

- Branche locale `noyau` depuis `main` @ `371cd7bf`, tag `pre-noyau` sur ce commit. Rien n'est poussé.
- Une tâche = un commit `noyau: …`, par `git commit -o <chemins> -F <fichier>` (hook `guard-commit.mjs`). Ne jamais inclure `docs/superpowers/specs/2026-10-06-vortex-mmorpg-master-design.md`. Message terminé par `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` et `Claude-Session: https://claude.ai/code/session_01JXCECoyL2rir7nZ7PFTkv3`.
- Suppressions : `git rm -r` ; projets : `dotnet sln Vortex.Cloud.sln remove`. Après une édition par script : `dotnet csharpier format <fichiers>`.
- **Mode rapide (choix du propriétaire, comme au M0 bis) :** compilation seule à la fin de chaque tâche (`dotnet build Vortex.Cloud.sln -p:SkipDashboardFrontendBuild=true`) ; tests, QualityGate et démarrage réel en Task 10 ; une revue finale de toute la branche.
- **Forme réseau :** un composer gardé garde sa forme ; un champ mort est figé à une constante et inventorié dans le bilan. Un paquet sans parser est ignoré avec un avertissement (`PackageHandler`), sans déconnexion.
- **Valeurs persistées** (enums, `[Id(n)]`) : retirer sans renuméroter.
- **Base :** aucune à conserver ; une seule `InitialCreate`, régénérée en Task 10.
- **Avant de supprimer un appel dans du code gardé, lire la méthode appelée.** Les chemins sont relevés à `371cd7bf` ; se fier aux noms de symboles.
- Ne jamais tuer un `Vortex.Main` qu'on n'a pas démarré (`guard-emulator.mjs`).

## Review Focus

1. **Le client actuel se connecte avec une rafale SSO réduite** → il atteint son écran et entre dans la zone, sans attente infinie d'un message qu'on n'envoie plus. Pin : Task 7, test de la rafale (liste exacte des composers) + démarrage réel Task 10.
2. **Le client envoie des paquets dont le handler est retiré** (catalogue, navigateur, chat…) → ignorés sans déconnexion. Pin : test existant de `PackageHandler` (« header sans parser »), gardé en Task 8.
3. **Une zone chargée depuis la base garde ses hauteurs** (meubles empilés, hauteur imposée, portail) → heightmap et pathfinding identiques. Pin : tests de carte et de pathfinding de `Vortex.Rooms.Tests` gardés en Task 6.
4. **Un service retiré reste injecté** (constructeur de grain, module, `[FromServices]` de la WebApi) → échec au démarrage ou 500, invisible au build. Pin : `check-webapi-forwarding.mjs`, tests d'enregistrement des modules de `Vortex.Hosting.Tests`, démarrage réel Task 10.
5. **Base neuve sans zone de développement** (import pas encore lancé) → la connexion réussit quand même, sans renvoi, avec un avertissement. Pin : Task 7, test dédié.

## Hors périmètre

Modèle de zone, carte de navigation, horloge (F1) ; éditeur de cartes (F4, avancé juste après F1) ; objet unique, personnages (F2) ; réseau RPG et chat (F3). Le client (`vortex-rpg-client`) n'est pas touché.

---

### Task 0 : Branche, tag, ligne de base

- [ ] `git tag pre-noyau 371cd7bf` ; `git checkout -b noyau`.
- [ ] Créer le ledger `.superpowers/sdd/2026-10-07-coupe-noyau/progress.md` ; baseline = QualityGate vert de `371cd7bf` (2 604 tests, bilan M0 bis).

### Task 1 : Exploitation — le dashboard part entièrement

**Files :** `git rm -r Vortex.Dashboard.API Vortex.Dashboard.Web Vortex.Dashboard.Tests` (+ `dotnet sln remove`) ; `Vortex.Players.Tests.csproj` (référence au dashboard et tests qui l'utilisent) ; `Vortex.Primitives/Permissions/Capabilities.cs` (`Capabilities.Dashboard` et tout ce qui ne sert qu'au dashboard) ; `Vortex.Permissions/DefaultRoles.cs` ; contrats `Vortex.Primitives/**/I*AdminService.cs` et leurs contrats `*AdminContracts.cs` qui n'avaient que le dashboard pour implémenteur ; `appsettings.Development.json` (`DevPluginPaths`) ; `Directory.Build.targets` (étapes `check-dashboard-capabilities`, `check-logic-groups`, `npm run lint`/`test`, `BuildDashboardFrontend`) ; `scripts/hooks/check-dashboard-capabilities.mjs`, `check-logic-groups.mjs`, `check-design-tokens.mjs`, `check-authorization-surface.mjs` (+ baselines) s'ils ne servent que le dashboard, `post-edit.mjs` et `__test/run.mjs` (cas correspondants) ; `scripts/hooks/check-architecture-walls.mjs` (mur 2 et couche Exploitation) ; `.claude/skills/dashboard-render`, `.claude/skills/new-dashboard-page`, `.claude/commands/web-guidelines.md` ; `.claude/settings.json` (hooks).

- [ ] Relever : `grep -rln "Dashboard" --include=*.cs --include=*.csproj --include=*.mjs --include=*.json --include=*.targets --include=*.md --exclude-dir=bin --exclude-dir=obj --exclude-dir=node_modules --exclude-dir=docs .`
- [ ] Supprimer, adapter, compiler. Contrôle : le grep ne liste plus que des mentions historiques ; `node scripts/hooks/__test/run.mjs` vert.
- [ ] Commit `noyau: le dashboard part`.

### Task 2 : Web — boutique et site réduits au minimum

**Files :** `git rm -r Vortex.WebApi.Shop Vortex.WebApi.Shop.Tests` ; `Vortex.WebApi` : ne garder que l'inscription, la connexion/déconnexion, la session, la liste et la création d'avatars, le ticket SSO et `/health`/infos publiques ; retirer articles, profils, rooms publiques, notifications (et leur diffusion SSE), signalements, préférences, e-mail, MFA, verrou, questions, mot de passe oublié, porte-monnaie, langues ; `Vortex.WebApi.Authentication` : garder `AccountAuthenticator` et `AccountPasswordService` (hachage à l'inscription, vérification) ; retirer MFA, e-mail, verrou, questions, lieux de confiance ; `WebApiWebHost.ForwardSingletons`, `WebApiTestFactory`, tests de `Vortex.WebApi.Tests` / `Vortex.WebApi.Authentication.Tests` des sujets retirés ; `Vortex.Main` (module Shop) ; `openapi.json` (régénéré en Task 10).

- [ ] Relever les routes : `grep -rn "Map\(Get\|Post\|Put\|Delete\)" Vortex.WebApi/Hosting`.
- [ ] Supprimer, adapter, compiler ; `node scripts/hooks/check-webapi-forwarding.mjs` vert.
- [ ] Commit `noyau: site réduit à l'inscription, la connexion et le ticket`.

### Task 3 : Économie — catalogue, inventaire, marché

**Files :** `git rm -r Vortex.Catalog Vortex.Inventory Vortex.Marketplace` (+ tests associés dans `Vortex.Database.Tests`) ; `Vortex.PacketHandlers/{Catalog,Inventory,Marketplace}` ; `Vortex.Main` (modules) ; dans `Vortex.Rooms` : la pose, le déplacement et le ramassage **par paquet** (`RoomService.Floor.cs`, `RoomService.Wall.cs`, handlers `PlaceObject`, `MoveObject`, `MoveWallItem`, `PickupObject`, `RemoveItem`), et les appels à `IInventoryGrain` dans `RoomActionModule*` : l'API de grain de pose/déplacement/ramassage est **gardée** (maisons, éditeur) ; le ramassage remet seulement `room_id` à `NULL` (le meuble reste à son propriétaire), sans notifier d'inventaire.

- [ ] Relever : `grep -rln "Inventory\|Catalog\|Marketplace\|Purchase" --include=*.cs Vortex.Rooms Vortex.Players Vortex.PacketHandlers Vortex.Primitives | grep -v /obj/`.
- [ ] Supprimer, adapter, compiler.
- [ ] Commit `noyau: l'économie part`.

### Task 4 : Social et navigation

**Files :** `git rm -r Vortex.Social Vortex.Navigator Vortex.Navigator.Tests` ; `Vortex.PacketHandlers/{FriendList,Users,GroupForums,Navigator,NewNavigator,RoomSettings}` et `Room/Action` ; `Vortex.Players/Grains/PlayerNavigatorGrain.cs` ; `Vortex.Main` (modules) ; tests `Social`, `Groups` de `Vortex.Rooms.Tests` / `Vortex.Players.Tests`.

- [ ] Relever : `grep -rln "Messenger\|Group\|Guild\|Navigator\|Friend" --include=*.cs Vortex.Rooms Vortex.Players Vortex.PacketHandlers | grep -v /obj/`.
- [ ] Supprimer, adapter, compiler.
- [ ] Commit `noyau: social et navigation partent`.

### Task 5 : Progression, signaux, pêche, et l'intérieur des joueurs

**Files :** `git rm -r Vortex.Progression Vortex.Signals Vortex.Signals.Tests Vortex.Fishing` ; `Vortex.PacketHandlers/{Quest,Fishing,Preferences,Avatar,Help,Moderator,RewardVault,Notifications,Tracking,Sound,Register,Nux,Advertisement}` ; `Vortex.Players` : garder `PlayerPresenceGrain` (session, envoi des composers, entrée/sortie de room), `PlayerDirectoryGrain`, `PlayerGrain` réduit à la fiche (nom, figure, genre, devise), `ServerConfigGrain`, `Authentication/` ; retirer `PlayerWalletGrain` et `Wallet` de la présence, `PlayerEffectGrain`, `PlayerClothingGrain`, `Membership/`, `RewardVault/`, `Effects/`, préférences de compte, garde-robe, modération du joueur, filtre de mots du joueur, modules d'inventaire et de portefeuille de la présence, providers devenus inutiles ; `Vortex.Main` (modules).

- [ ] Relever : `grep -rln "Wallet\|Currency\|Membership\|RewardVault\|Effect\|Clothing\|Wardrobe\|Badge\|Achievement\|Quest" --include=*.cs Vortex.Players Vortex.Rooms Vortex.PacketHandlers | grep -v /obj/`.
- [ ] Supprimer, adapter, compiler.
- [ ] Commit `noyau: progression, signaux et pêche partent ; le joueur réduit à sa fiche`.

### Task 6 : L'intérieur des rooms et des permissions

**Files :** `Vortex.Rooms` : retirer `Games/`, bots (`RoomGrain.Bots`, `RoomBotSystem*`), pets (`RoomGrain.Pets`, `RoomPetSystem*`, `RoomPetRuntime`, `PetAnimation`, `RoomService.Pets`), échanges (`RoomGrain.Trading`, `RoomTradingSystem`, `RoomTradeSession`, `RoomService.Trading`), chat (`RoomChatSystem`, `ChatGestures`), modération (`RoomGrain.Moderation`, `RoomModerationSystem`, `ModerationQueueGrain`, `CfhTicketService`, `ModeratorChatlogService`, `ModeratorRoomVisitService`, `RoomModerationStore`), sonnette (`RoomGrain.Doorbell`, `RoomService.Doorbell`), réglages (`RoomGrain.Settings`), plan de sol éditable (`RoomGrain.FloorPlan`), objets en main (`RoomHandItemModule`), éditeur de meubles en jeu (`RoomGrain.Furni.Edit` + handlers `Vortex*FurniEdit*`), meubles interactifs (`RoomGrain.Furni.Interactive`) ; logiques : garder `FurnitureFloorLogic`, `FurnitureWallLogic`, `FurnitureGateLogic`, `FurnitureCustomStackHeightLogic`, `FurnitureInvisibleClickTileLogic`, `FurnitureBackgroundColorLogic`, `FurnitureRoomDimmerLogic` et la logique d'avatar ; retirer les autres (`Dice`, `Fireworks`, `GameTimer`, `Games/`, `GroupForumTerminal`, `GuildCustomized`, `GuildGate`, `OneWayDoor`, `Present`, `VendingMachine`, `WheelOfFortune`, `ClothingChange`) ; `Vortex.PacketHandlers/Room/{Chat,Furniture,Pets}` et `Room/Avatar` sauf `LookTo`, `Room/Engine` sauf `MoveAvatar` et `GetFurnitureAliases` ; `Vortex.Permissions` : garder rôles et capacités du jeu, retirer sanctions, catalogue CFH, filtre de mots ; tests des sujets retirés dans `Vortex.Rooms.Tests` (`Bots`, `Chat`, `Games`, `Groups`, `Loot`, `Moderation`, `Pets`, `Social`, `Trading`, `Wallet`, et ceux de `Furniture` sur les logiques retirées). **Garder** les tests de carte, de hauteurs, d'empilement et de pathfinding.

- [ ] Test (rouge) dans `Vortex.Rooms.Tests/Furniture/RoomObjectLogicKeyTests.cs` : `ARemovedInteractiveLogic_LoadsAsDefaultLogic` (théorie sur `dice`, `furniture_habbowheel`, `fireworks`, `vending_machine`, `one_way_door`) → plus rouge une fois les classes retirées ; l'écrire d'abord, l'exécuter en Task 10.
- [ ] Supprimer, adapter, compiler.
- [ ] Commit `noyau: la room garde la carte, le pathfinding, les avatars et les meubles`.

### Task 7 : Connexion minimale et zone de développement

**Files :** `Vortex.PacketHandlers/Handshake/SSOTicketMessageHandler.cs` (rafale réduite) ; `Vortex.Database/Seeds/seed_dashboard_staff_actor.sql` → `seed_world_owner.sql` (joueur `__world__`, propriétaire des zones) ; `Vortex.Database/Seeds/dev_zone.sql` (une room « zone de développement » sur `model_a`, propriétaire `__world__`, rejouable ; appliqué par `scripts/import-reference-data.ps1` après l'import, les modèles venant de turbo) ; clé de config `world.start_room_id` dans `ConfigKeyCatalog` (défaut `0` = aucune) et dans `add_server_config.sql` (écrite par `dev_zone.sql` avec l'id de la zone créée) ; tests `Vortex.PacketHandlers.Tests`.

Rafale gardée, dans cet ordre : `AuthenticationOK`, `UserRights`, puis, si `world.start_room_id` > 0 et que la room existe, `RoomForward { RoomId }` ; sinon un avertissement journalisé et rien d'autre. `InfoRetrieve` → `UserObject` inchangé (champs morts déjà figés).

- [ ] Test (rouge) `SsoBurst_SendsOnlyTheCoreComposers` : la rafale contient exactement `AuthenticationOK`, `UserRights`, `RoomForward(42)` quand `world.start_room_id` = 42.
- [ ] Test (rouge) `SsoBurst_WithoutAStartZone_StillLogsIn` : `world.start_room_id` = 0 → `AuthenticationOK`, `UserRights`, pas de `RoomForward`.
- [ ] Implémenter ; compiler.
- [ ] Commit `noyau: connexion minimale, le joueur arrive dans la zone de développement`.

### Task 8 : Orphelins — protocole, contrats, tables

**Files :** `Vortex.Protocol`, `Vortex.Revisions` (messages, parsers, serializers, entrées de maps, constantes de `Headers.cs`), `Vortex.Primitives`, `Vortex.Database` (entités, DbSets, seeds, `scripts/sql/import_reference_data.sql`), `Vortex.Revisions.Tests`, `Vortex.Database.Tests`.

- [ ] Balayage par script, itéré jusqu'à point fixe : un type de `Vortex.Protocol` que plus aucun code hors `Vortex.Protocol`/`Vortex.Revisions` ne nomme part avec son parser/serializer, son entrée de map et sa constante de header ; un type de `Vortex.Primitives` que plus aucun autre projet ne nomme part ; une entité que seul `VortexDbContext` nomme part avec son DbSet et son seed. Garder ce qui est nommé par le cadre (`PackageHandler`, maps, `Headers` du flux gardé).
- [ ] Seeds : garder les définitions de meubles, logiques et données de meubles, modèles, config serveur, monde ; retirer ceux des tables parties ; mettre à jour `import_reference_data.sql` (plus de catalogue ni d'abonnement : n'importer que `room_models`, `furniture_definitions` et ce que les logiques gardées lisent).
- [ ] Compiler. Commit `noyau: protocole, contrats et tables orphelins retirés`.

### Task 9 : Règles et outillage

**Files :** `AGENTS.md`, `CONTEXT.md`, `CLAUDE.md`, `.github/copilot-instructions.md` (projets, couches sans Exploitation, recettes du dashboard retirées, mur 8) ; `docs/codebase/` supprimé entièrement (base `/document-vortex` d'avant le fork, régénérable) ; `docs/walkthroughs/` : ne garder que ce qui décrit le noyau ; `DATA-MODEL.md` ; `.claude/skills/run-vortex-emulator` (plus de dashboard : `smoke` = santé, socket, connexion, entrée en zone) ; `docker-compose.yml`, `appsettings*.json` (sections retirées).

- [ ] Corriger ; `node scripts/hooks/__test/run.mjs` vert ; `node scripts/hooks/check-architecture-walls.mjs` vert.
- [ ] Commit `noyau: règles et outillage à jour`.

### Task 10 : Migration, validation réelle, bilan

- [ ] Régénérer `InitialCreate` (`pwsh scripts/ef.ps1 migrations add InitialCreate`), recopier les appels de seeds gardés dans le même ordre, `dotnet csharpier format`.
- [ ] Base neuve `vortex_noyau_check` : `ef.ps1 database update`, puis `import-reference-data.ps1 -Target vortex_noyau_check` ; vérifier qu'une zone de développement existe et que `world.start_room_id` la désigne.
- [ ] QualityGate vert (régénérer `openapi.json` avec `UPDATE_OPENAPI=1`).
- [ ] Démarrage réel (user-secrets vers `vortex_noyau_check`) : santé, socket, inscription → avatar → SSO → `AuthenticationOK` → `RoomForward` → entrée en zone → `HeightMap`/`Objects` → `MoveAvatar` → `UserUpdate`. Arrêter l'émulateur ensuite.
- [ ] Bilan `docs/superpowers/plans/2026-10-07-coupe-noyau.bilan.md` : avant/après (projets, lignes, tests), écarts, champs réseau figés ajoutés, checklist du propriétaire avec le client.
- [ ] Commit `noyau: migration régénérée, validation réelle et bilan` ; revue finale de la branche (modèle le plus capable), passe de correction, puis fusion locale proposée.
