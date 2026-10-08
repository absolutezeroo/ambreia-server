# RPG F3a — Réseau RPG, écran des personnages : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** une socket en deux phases — compte (liste, création, choix de classe, entrée en jeu par paquets ≥ 9000) puis personnage (état du personnage poussé au HUD) — sans site.

**Architecture:** un ticket de compte ouvre la socket en phase compte ; `AccountSessionGrain` (clé = compte) envoie à la socket pendant cette phase, `AccountCharactersGrain` (clé = compte) fait le métier et passe par `PlayerCharacterGrain`. `RpgEnterWorld` rattache la socket au joueur par le même chemin que le SSO actuel (`WorldEntry`, extrait du handler SSO). En jeu, `PlayerCharacterGrain` pousse l'état par la présence du joueur.

**Tech Stack:** C# .NET 10, Orleans, EF Core 9 (SQLite en test), SuperSocket, xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-10-08-rpg-network-characters-design.md` (contrat des paquets : §6).

## Global Constraints

- Ids RPG ≥ 9000 dans les régions `Vortex-specific` de `Headers.cs`, commentaire `// RPG (spec §3.7)` ; `MessageEvent` et `MessageComposer` sont deux espaces (9000–9003 entrants, 9000–9005 sortants).
- Ordre et types des champs **exactement** comme spec §6 (le client lit dans cet ordre) ; chaîne = int16 + UTF-8, booléen = un octet, liste = int32 puis éléments, `critBp`/`dodgeBp` = pourcentage × 100 arrondi.
- Codes de `RpgCharacterResult` : `ok`, `character.name_invalid`, `character.name_taken`, `character.limit_reached`, `character.figure_invalid`, `character.not_owned`, `character.unknown_class`, `character.class_already_chosen`, `rate_limited` ; actions 1 créer, 2 classe, 3 entrer.
- 5 personnages par compte ; le personnage naît « Sans classe » (classe `starter` du contenu).
- Handlers : un appel de grain ou de service, l'id du compte et du joueur pris dans `MessageContext`, jamais dans le paquet ; aucun `DbContext` ; les envois passent par un grain (`AccountSessionGrain`, `PlayerPresenceGrain`).
- Le ticket de joueur et le chemin SSO actuel (driver, mode test de l'éditeur) restent inchangés.
- Un type par fichier ; modifications par Edit/Write ; commits en français, chemins explicites, sur `main`.
- Les ids 9000+ cassent les tests du client (`incoming-registered`, `outgoing-registered`) tant que la session client ne les sert pas : le prévenir au commit de la Tâche 2 (message au propriétaire), ne rien modifier côté client.

## Ce que le code impose à la spec (corrigé en Tâche 1)

1. Aucun contrôle d'IP sur les tickets aujourd'hui : « mêmes règles » = TTL, expiration glissante et `AuthenticationConfig.TicketSingleUse` (faux par défaut, donc un ticket est réutilisable 300 s au plus). Le ticket de compte suit **les mêmes règles** que le ticket de joueur, ni plus ni moins.
2. La limite de 5 côté paquets vit dans `CharacterConfig.MaxCharactersPerAccount` (le web API est un plugin, sa configuration n'est pas visible ailleurs) ; `WebApiConfig.MaxAvatarsPerAccount` reste pour les routes HTTP.
3. « Figure validée » = longueur ≤ `FigureString.MaxLength` (255), comme les routes HTTP aujourd'hui.

## Review Focus

1. **Un paquet d'écran des personnages envoyé en jeu, ou un paquet de jeu (`MoveAvatar`) en phase compte** → sans effet, sans exception (Tâche 5, `AnAccountPhasePacketInTheWorld_IsDropped`, Tâche 3, `AGamePacketInTheAccountPhase_HasNoPlayer`).
2. **Deux connexions du même compte en phase compte** → la première est fermée, la seconde reçoit la liste (Tâche 3, `ASecondAccountSession_ReplacesTheFirst`).
3. **`RpgEnterWorld` sur le personnage d'un autre compte** → `character.not_owned`, la session reste en phase compte (Tâche 5, `EnteringAnotherAccountsCharacter_IsRefused`).
4. **Deux créations simultanées à 4 personnages** → une seule passe, l'autre `character.limit_reached` (Tâche 4, `TwoCreatesAtTheLimit_LetOnlyOneThrough`).
5. **Socket fermée en phase compte puis nouvelle connexion** → la session de compte est libérée, la nouvelle reçoit la liste (Tâche 3, `ClosingTheAccountSocket_ReleasesTheAccount`).

---

### Task 1: Le ticket de compte

**Files:**
- Create: `Vortex.Database/Entities/Security/AccountTicketEntity.cs`, `Vortex.Primitives/Authentication/TicketOwner.cs`, `scripts/sql/2026-10-08-account-tickets.sql`
- Modify: `Vortex.Database/Context/VortexDbContext.cs` (`DbSet<AccountTicketEntity> AccountTickets`), `Vortex.Database/Entities/Players/PlayerAccountEntity.cs` (navigation inverse), `Vortex.Primitives/Authentication/IAuthenticationService.cs`, `Vortex.Players/Authentication/AuthenticationService.cs`, `Vortex.PacketHandlers/Handshake/SSOTicketMessageHandler.cs` (appel renommé seulement), `Vortex.WebApi/Services/IWebApiAuthService.cs`, `WebApiAuthService.cs`, `Vortex.WebApi/Hosting/WebApiEndpoints.cs` (`/api/ssotoken`), `Vortex.WebApi.Tests/Fakes.cs`, la spec (§5.1, §5.3, §7 : les trois corrections ci-dessus)
- Regenerate: `InitialCreate`
- Test: `Vortex.Players.Tests/Authentication/AuthenticationServiceTests.cs`, `Vortex.WebApi.Tests/WebApiEndpointsTests.cs`, `Vortex.PacketHandlers.Tests/SsoBurstTests.cs`

**Interfaces:**
- Produces:
  - `[Table("account_tickets")] [Index(AccountId, unique)] [Index(Ticket, unique)] AccountTicketEntity : VortexEntity` — `[Column("account_id")] required int AccountId`, `[Column("ticket")] required string Ticket`, `[Column("ip_address")] required string IpAddress`, `[Column("expires_at")] DateTime? ExpiresAt`, `[ForeignKey] PlayerAccountEntity? Account`.
  - `sealed record TicketOwner(int PlayerId, int AccountId)` avec `static TicketOwner None => new(0, 0)` ; exactement un des deux > 0, ou aucun.
  - `IAuthenticationService.ResolveTicketAsync(string ticket, string? remoteIp = null, CancellationToken ct = default) : Task<TicketOwner>` remplace `GetPlayerIdFromTicketAsync` (ticket de joueur d'abord, puis de compte ; mêmes TTL, expiration glissante, usage unique, `PlayerLoggedInEvent` pour un joueur, `PlayerLoginFailedEvent` si inconnu).
  - `IWebApiAuthService.GetAccountSsoTokenAsync(int accountId, string ip, CancellationToken ct) : Task<(bool Success, string? Ticket, string? Error)>`.
  - `/api/ssotoken` : avatar sélectionné ou `uniqueId` → ticket de joueur (inchangé) ; sinon → ticket de compte (au lieu du premier avatar ou du 404 actuels).

- [ ] **Step 1: Tests** :
  - `AuthenticationServiceTests.AnAccountTicket_ResolvesToTheAccount`, `…AnExpiredAccountTicket_ResolvesToNoOne`, `…APlayerTicket_StillResolvesToThePlayer` (renommer les appels existants vers `ResolveTicketAsync(…).PlayerId`).
  - `WebApiEndpointsTests.SsoToken_WithoutASelectedAvatar_IsAnAccountTicket` : le faux `IWebApiAuthService` rend `"account-ticket"` pour `GetAccountSsoTokenAsync` → réponse `ssoToken = "account-ticket"`.
  - `SsoBurstTests` : le faux d'authentification passe à `ResolveTicketAsync` (rien d'autre ne change dans cette tâche).
- [ ] **Step 2:** échec (compilation).
- [ ] **Step 3:** entité, `DbSet`, migration régénérée (`changing-the-schema`), base neuve, script additif (`CREATE TABLE IF NOT EXISTS` tiré de `SHOW CREATE TABLE`, puis `UPDATE __EFMigrationsHistory … WHERE MigrationId = '<id actuel>'`) appliqué deux fois à `ambreia-database` ; services et route ; corrections de la spec.
- [ ] **Step 4:** `dotnet test Vortex.Players.Tests`, `Vortex.WebApi.Tests`, `Vortex.PacketHandlers.Tests`, `Vortex.Database.Tests` · Expected: PASS.
- [ ] **Step 5: Commit** — `connexion : ticket de compte`.

---

### Task 2: Les paquets RPG

**Files:**
- Create (`Vortex.Primitives/Networking/IAccountPhaseMessage.cs`) : `public interface IAccountPhaseMessage : IMessageEvent { }`
- Create (`Vortex.Protocol/Messages/Incoming/Rpg/Characters/`) : `RpgCharacterListRequestMessage.cs`, `RpgCharacterCreateMessage.cs`, `RpgCharacterChooseClassMessage.cs`, `RpgEnterWorldMessage.cs` (tous `: IAccountPhaseMessage`)
- Create (`Vortex.Protocol/Messages/Outgoing/Rpg/Characters/`) : `RpgCharacterListMessageComposer.cs`, `RpgCharacterListEntry.cs`, `RpgClassListMessageComposer.cs`, `RpgClassEntry.cs`, `RpgCharacterResultMessageComposer.cs`, `RpgCharacterStateMessageComposer.cs`, `RpgCharacterProgressMessageComposer.cs`, `RpgCharacterStatsMessageComposer.cs`
- Create (`Vortex.Revisions/Revision20260701/`) : `Parsers/Rpg/Characters/*Parser.cs` (4), `Serializers/Rpg/Characters/*Serializer.cs` (6) + `Serializers/Rpg/Characters/RpgStatBlockWriter.cs` (le bloc de stats commun), `Maps/RpgCharactersMap.cs`
- Modify: `Headers.cs` (régions `Vortex-specific`), `Revision20260701.cs` (`new RpgCharactersMap()`)
- Test: `Vortex.Revisions.Tests/Rpg/RpgCharactersWireTests.cs`

**Interfaces:**
- Produces (records ; composers `[GenerateSerializer, Immutable]`, champs `[Id(n)]` dans l'ordre de spec §6) :
  - `RpgCharacterCreateMessage { required string Name; required string Figure; required string Gender }` ; `RpgCharacterChooseClassMessage { required int PlayerId; required string ClassId }` ; `RpgEnterWorldMessage { required int PlayerId }` ; `RpgCharacterListRequestMessage`.
  - `RpgCharacterListEntry { PlayerId, Name, Figure, Gender, ClassId, SpecializationId, Level, Starter }` ; `RpgCharacterListMessageComposer { int MaxCharacters; ImmutableArray<RpgCharacterListEntry> Characters }`.
  - `RpgClassEntry { Id, Role, Range, PrimaryStat, Starter, ImmutableArray<string> Specializations }` ; `RpgClassListMessageComposer { ImmutableArray<RpgClassEntry> Classes }`.
  - `RpgCharacterResultMessageComposer { int Action; string Code; int PlayerId }` + constantes `ActionCreate = 1`, `ActionChooseClass = 2`, `ActionEnterWorld = 3`.
  - `RpgCharacterStateMessageComposer { int PlayerId; string ClassId; string SpecializationId; int Level; int MaxLevel; int Xp; int XpToNext; CharacterStatsSnapshot Stats }` ; `RpgCharacterProgressMessageComposer { int Level; int Xp; int XpToNext }` ; `RpgCharacterStatsMessageComposer { CharacterStatsSnapshot Stats }`.
  - `RpgStatBlockWriter.Write(IServerPacket packet, CharacterStatsSnapshot s)` : strength, intelligence, agility, maxHealth, maxMana, armor, `(int)Math.Round(s.CritPercent * 100)`, `(int)Math.Round(s.DodgePercent * 100)`, speedPercent, power.
  - `Headers.cs` : `MessageEvent.RpgCharacterListRequestMessageEvent = 9000`, `RpgCharacterCreateMessageEvent = 9001`, `RpgCharacterChooseClassMessageEvent = 9002`, `RpgEnterWorldMessageEvent = 9003` ; `MessageComposer.RpgCharacterListMessageComposer = 9000`, `RpgClassListMessageComposer = 9001`, `RpgCharacterResultMessageComposer = 9002`, `RpgCharacterStateMessageComposer = 9003`, `RpgCharacterProgressMessageComposer = 9004`, `RpgCharacterStatsMessageComposer = 9005`.

- [ ] **Step 1: Tests** (forme de `UserChangeWireTests` pour les sortants — sérialiser par la révision, relire avec `ClientPacket`, `Remaining == 0` ; forme de `UseFurnitureWireTests` pour les entrants — un aide `Body(params object[])` écrit int32 BE et chaînes int16 + UTF-8) :
  - `TheCharacterList_IsReadInTheClientsOrder` (deux personnages, dont un `starter`), `TheClassList_CarriesItsSpecializations`, `AResult_CarriesActionCodeAndPlayer`, `TheState_EndsWithTheStatBlock` (crit 6,2 % → 620), `Progress_AndStats_AreFullyConsumed` ;
  - `ACreate_ReadsNameFigureGender`, `AClassChoice_ReadsPlayerAndClass`, `EnterWorld_ReadsThePlayer`, `TheListRequest_HasNoField`.
- [ ] **Step 2:** échec (compilation).
- [ ] **Step 3:** records, parsers, serializers, map, ids.
- [ ] **Step 4:** `dotnet test Vortex.Revisions.Tests` · Expected: PASS (dont `SerializerPairingTests`, `EmittedComposerRegistrationTests`) ; `node scripts/hooks/check-inert-declarations.mjs` · Expected: les 4 parsers sans handler signalés — normal, la Tâche 5 les sert ; ligne du registre, pas de mise à jour de la base de référence.
- [ ] **Step 5: Commit** — `rpg : paquets de l'écran des personnages et de l'état (9000+)` ; prévenir le propriétaire que la session client doit maintenant servir ces ids (tests `incoming/outgoing-registered`).

---

### Task 3: La phase compte de la session

**Files:**
- Create: `Vortex.Primitives/Players/Grains/IAccountSessionGrain.cs`, `Vortex.Players/Grains/AccountSessionGrain.cs`
- Modify: `Vortex.Primitives/Orleans/GrainFactoryExtensions.cs` (`GetAccountSessionGrain(int accountId)`), `Vortex.Primitives/Networking/ISessionGateway.cs` (`GetAccountId`, `AddSessionToAccountAsync`), `Vortex.Networking/Session/SessionGateway.cs`, `Vortex.Messages/Registry/MessageContext.cs` (`int AccountId`), `Vortex.Messages/Registry/MessageRegistry.cs` (le passe), `Vortex.PacketHandlers.Tests/SsoBurstTests.cs` (construction du contexte)
- Test: `Vortex.Players.Tests/Accounts/AccountSessionGrainTests.cs`, `Vortex.Hosting.Tests/Sessions/SessionGatewayAccountTests.cs`

**Interfaces:**
- Produces:
  - `IAccountSessionGrain : IGrainWithIntegerKey` : `Task<long> BeginSessionAsync()`, `Task RegisterSessionObserverAsync(ISessionContextObserver observer, long generation)`, `Task UnregisterSessionObserverAsync()`, `Task SendComposerAsync(params IComposer[] composers)` (envoi direct et dans l'ordre à l'observateur ; rien sans observateur ; pas d'attribut d'entrelacement : aucun appel ne revient vers lui).
  - `ISessionGateway.GetAccountId(SessionKey key) : int` (0 hors phase compte) ; `ISessionGateway.AddSessionToAccountAsync(SessionKey key, int accountId, CancellationToken ct = default) : Task` — sous `_mappingGate` : une autre session du même compte est fermée (`DisconnectReason ConcurrentLogin`, comme pour un joueur), la correspondance session ↔ compte est posée, `BeginSessionAsync` puis `RegisterSessionObserverAsync`.
  - `AddSessionToPlayerAsync` retire d'abord la correspondance de compte de la même session (et désinscrit la session de compte) : c'est le passage compte → joueur.
  - `RemoveSessionAsync` retire aussi la correspondance de compte et désinscrit la session de compte.
  - `MessageContext(ISessionContext session, PlayerId playerId, RoomId roomId, int accountId = 0)` avec `public int AccountId`.

- [ ] **Step 1: Tests** :
  - `AccountSessionGrainTests` (forme de `PresenceSessionLifetimeTests`) : `RegisteringASession_SendsInOrder`, `AnOlderGeneration_IsIgnored`, `WithoutASession_NothingIsSent`.
  - `SessionGatewayAccountTests` (passerelle construite avec un faux `IGrainFactory` qui rend des faux `IAccountSessionGrain`/`IPlayerPresenceGrain` enregistreurs et de faux `ISessionContext`) : `AnAccountSession_IsMappedAndRegistered`, `ASecondAccountSession_ReplacesTheFirst` (Review Focus 2), `EnteringTheWorld_LeavesTheAccountPhase` (`GetAccountId == 0`, `GetPlayerId == joueur`), `ClosingTheAccountSocket_ReleasesTheAccount` (Review Focus 5), `AGamePacketInTheAccountPhase_HasNoPlayer` (Review Focus 1 : `GetPlayerId == -1` en phase compte, donc les handlers de jeu ne font rien).
- [ ] **Step 2:** échec. **Step 3:** implémenter (`_sessionToAccount`, `_accountToSession` comme les dictionnaires joueur). **Step 4:** `dotnet test Vortex.Players.Tests`, `Vortex.Hosting.Tests`, `Vortex.PacketHandlers.Tests` · Expected: PASS ; `grain-rules-reviewer` sur `Vortex.Players/Grains` · Expected: rien de bloquant.
- [ ] **Step 5: Commit** — `connexion : phase compte de la session`.

---

### Task 4: Les personnages d'un compte

**Files:**
- Create: `Vortex.Primitives/Rpg/Characters/Grains/IAccountCharactersGrain.cs`, `Vortex.Rpg.Characters/Grains/AccountCharactersGrain.cs`, `Vortex.Primitives/Players/NameShape.cs` (déplacé, public)
- Modify: `Vortex.WebApi/Http/WebApiRequests.cs` (retire `NameShape`, utilise celui de Primitives), `Vortex.WebApi/Services/WebApiPlayerService.cs` (idem), `Vortex.Rpg.Characters/Configuration/CharacterConfig.cs` (`MaxCharactersPerAccount = 5`, `DefaultFigure = "hd-180-1.ch-210-66.lg-270-82.sh-290-80"`), `GrainFactoryExtensions.cs` (`GetAccountCharactersGrain(int accountId)`)
- Test: `Vortex.Database.Tests/Rpg/AccountCharactersGrainTests.cs` (SQLite)

**Interfaces:**
- Consumes: `IAccountSessionGrain.SendComposerAsync`, les composers de la Tâche 2, `IPlayerCharacterGrain` (`GetSummaryAsync`, `ChooseClassAsync`), `IRpgContent` (`All<ClassDefinition>()`).
- Produces: `IAccountCharactersGrain : IGrainWithIntegerKey` :
  - `Task OpenAsync()` — envoie `RpgCharacterList` puis `RpgClassList` (classes non retirées, ordre des ids) ;
  - `Task ListAsync()` — envoie `RpgCharacterList` ;
  - `Task CreateAsync(string name, string figure, string gender)` — `NameShape.IsWellFormed` sinon `name_invalid` ; figure vide → `DefaultFigure`, > 255 → `figure_invalid` ; compte à `MaxCharactersPerAccount` → `limit_reached` ; nom pris → `name_taken` ; sinon ligne `players` (comme `WebApiPlayerService.CreateAvatarAsync`), puis `ChooseClassAsync(<classe starter>)` ; envoie `RpgCharacterResult(1, code, playerId)` puis, sur `ok`, `RpgCharacterList` ;
  - `Task ChooseClassAsync(int playerId, string classId)` — appartenance sinon `not_owned` ; `Chosen` → `ok`, `AlreadyChosen` → `class_already_chosen`, `UnknownClass` → `unknown_class` ; envoie `RpgCharacterResult(2, …)` puis la liste sur `ok` ;
  - `Task<bool> OwnsAsync(int playerId)` — personnage non supprimé de ce compte ; sinon envoie `RpgCharacterResult(3, "character.not_owned", playerId)` et rend `false`.
  - Une liste : chaque avatar du compte (non supprimé, ordre des ids) avec `GetSummaryAsync` en parallèle ; `starter` = classe du personnage marquée `Starter` dans le contenu ; un avatar sans personnage (ancien) reçoit la classe de départ à `OpenAsync`.

- [ ] **Step 1: Tests** (faux `IAccountSessionGrain` enregistreur, faux `IPlayerCharacterGrain` en mémoire comme `FakeCharacters`, contenu par `CharacterContent.Registry`) : `Opening_SendsTheListThenTheClasses`, `ACreate_IsBornClassless`, `ABadName_IsRefused`, `ATakenName_IsRefused`, `TheFifthCharacter_IsTheLast`, `TwoCreatesAtTheLimit_LetOnlyOneThrough` (Review Focus 4 : `Task.WhenAll` de deux `CreateAsync` à 4 personnages → 5 lignes, un `limit_reached`), `ChoosingAClassForSomeoneElse_IsNotOwned`, `ChoosingAClass_SendsOkThenTheList`, `OwnsAsync_RefusesAnotherAccountsCharacter`.
- [ ] **Step 2:** échec. **Step 3:** implémenter (un grain par compte sérialise ses appels). **Step 4:** `dotnet test Vortex.Database.Tests`, `Vortex.WebApi.Tests` · Expected: PASS.
- [ ] **Step 5: Commit** — `rpg : personnages d'un compte, création et classe par paquets`.

---

### Task 5: Handlers, barrière de phase, débit, entrée en jeu

**Files:**
- Create: `Vortex.PacketHandlers/Handshake/WorldEntry.cs` (extrait de `SSOTicketMessageHandler` : tout ce qui suit le rattachement — permissions, lieu d'arrivée, `AuthenticationOK`, `UserRights`, `RoomForward`), `Vortex.PacketHandlers/Rpg/Characters/{RpgCharacterListRequest,RpgCharacterCreate,RpgCharacterChooseClass,RpgEnterWorld}MessageHandler.cs`, `Vortex.PacketHandlers/Rpg/AccountPhaseGateBehavior.cs`, `Vortex.PacketHandlers/Rpg/RpgIntentLimitBehavior.cs`, `Vortex.PacketHandlers/Rpg/RpgIntentLimiter.cs`, `Vortex.PacketHandlers/Configuration/RpgIntentLimitConfig.cs`
- Modify: `SSOTicketMessageHandler.cs` (ticket de compte → `AddSessionToAccountAsync` puis `GetAccountCharactersGrain(id).OpenAsync()` ; ticket de joueur → `AddSessionToPlayerAsync` puis `WorldEntry.EnterAsync`), `Vortex.PacketHandlers/PacketHandlersModule.cs` (`WorldEntry`, `RpgIntentLimiter` en singleton, `Configure<RpgIntentLimitConfig>`), `Vortex.Primitives/Observability/IVortexMetrics.cs` + `VortexMetrics.cs` (`RpgPacketRefused(string reason)` : `phase` | `rate_limited`)
- Test: `Vortex.PacketHandlers.Tests/Rpg/AccountPhaseTests.cs`, `SsoBurstTests.cs`

**Interfaces:**
- Produces:
  - `WorldEntry.EnterAsync(SessionKey key, int playerId, CancellationToken ct) : Task` — `AddSessionToPlayerAsync` puis la suite actuelle du SSO, inchangée ; puis `GetPlayerCharacterGrain(id).PushStateAsync()` (Tâche 6 ; dans cette tâche, l'appel est ajouté en Tâche 6).
  - `AccountPhaseGateBehavior : IMessageBehavior<IAccountPhaseMessage>`, `[Order(int.MinValue + 1)]` : passe si `ctx.AccountId > 0 && ctx.PlayerId <= 0`, sinon `RpgPacketRefused("phase")` et rien.
  - `RpgIntentLimitBehavior : IMessageBehavior<IAccountPhaseMessage>`, `[Order(int.MinValue + 2)]` : `RpgIntentLimiter.TryConsume(sessionKey, messageType)` ; refus → `RpgCharacterResult(action, "rate_limited", 0)` par `GetAccountSessionGrain` (rien pour la liste) et `RpgPacketRefused("rate_limited")`.
  - `RpgIntentLimitConfig` (`Vortex:RpgIntents`) : `CreateIntervalMs = 2000`, `ChooseClassIntervalMs = 1000`, `EnterWorldIntervalMs = 1000`, `ListIntervalMs = 1000`.
  - Handlers : liste → `ListAsync` ; créer → `CreateAsync` ; classe → `ChooseClassAsync` ; entrer → `if (await OwnsAsync(id)) await worldEntry.EnterAsync(ctx.SessionKey, id, ct)`.

- [ ] **Step 1: Tests** :
  - `SsoBurstTests.AnAccountTicket_OpensTheAccountPhase` (faux gateway enregistreur : `AddSessionToAccountAsync` appelé, `OpenAsync` appelé, aucun `AuthenticationOK`) ; les tests existants (ticket de joueur) passent inchangés.
  - `AccountPhaseTests` : `EnteringTheWorld_SendsTheSameBurstAsTheSso`, `EnteringAnotherAccountsCharacter_IsRefused` (Review Focus 3 : `OwnsAsync` faux → aucun rattachement), `AnAccountPhasePacketInTheWorld_IsDropped` (Review Focus 1 : behavior avec `PlayerId > 0` → `next` jamais appelé), `ASecondCreateWithinTwoSeconds_IsRateLimited`, `TheLimiter_KeepsIntentsApart` (une création n'empêche pas une liste).
- [ ] **Step 2:** échec. **Step 3:** implémenter. **Step 4:** `dotnet test Vortex.PacketHandlers.Tests`, `Vortex.Hosting.Tests` ; `node scripts/hooks/check-inert-declarations.mjs` · Expected: 0 écart.
- [ ] **Step 5: Commit** — `rpg : écran des personnages par paquets, entrée en jeu`.

---

### Task 6: L'état du personnage poussé au client

**Files:**
- Modify: `Vortex.Primitives/Rpg/Characters/Grains/IPlayerCharacterGrain.cs` (`PushStateAsync`, `EnteredRoomAsync` remplace `SetLastRoomAsync`), `Vortex.Rpg.Characters/Grains/PlayerCharacterGrain.cs` (+ `IGrainFactory` au constructeur), `Vortex.Rpg.Characters/Events/LastZoneHandler.cs`, `Vortex.PacketHandlers/Handshake/WorldEntry.cs`, les constructions du grain dans `Vortex.Database.Tests/Rpg/*.cs`
- Test: `Vortex.Database.Tests/Rpg/CharacterStatePushTests.cs`

**Interfaces:**
- Produces:
  - `Task PushStateAsync()` — sans personnage : rien ; sinon `RpgCharacterState` (stats par `GetStatsAsync`, `MaxLevel` et `XpToNext` de `progression.default`, 0 au max) par `GetPlayerPresenceGrain(id).SendComposerAsync`.
  - `Task EnteredRoomAsync(RoomId roomId)` — la dernière zone comme avant, puis `PushStateAsync()`.
  - Dans `GainXpAsync` : après `XpGainedEvent`, `RpgCharacterProgress` ; sur une montée de niveau, `RpgCharacterStats` avant.
  - `WorldEntry.EnterAsync` appelle `PushStateAsync()` après le `RoomForward` (la reprise après la grâce n'émet pas d'entrée en zone).

- [ ] **Step 1: Tests** (faux `IGrainFactory` qui rend un faux `IPlayerPresenceGrain` enregistreur) : `EnteringARoom_PushesTheState` (Guerrier niveau 1 : MaxHealth 120, critBp attendu au fil 620), `AGain_PushesProgress`, `ALevelUp_PushesStatsThenProgress`, `WithoutACharacter_NothingIsPushed`, `AtTheMaxLevel_XpToNextIsZero`.
- [ ] **Step 2:** échec. **Step 3:** implémenter ; mettre à jour les quatre fichiers de tests qui construisent le grain (`CreateWithIntegerKey<PlayerCharacterGrain>`). **Step 4:** `dotnet test Vortex.Database.Tests`, `Vortex.PacketHandlers.Tests` · Expected: PASS.
- [ ] **Step 5: Commit** — `rpg : état du personnage poussé au client`.

---

### Task 7: Preuve de bout en bout et documentation

**Files:**
- Modify: `.claude/skills/run-vortex-emulator/driver.mjs` (commande `lobby` ; `ssoTicket` garde le ticket de joueur), `SKILL.md`, `docs/architecture.md` (flux 1 « sign-in » réécrit : ticket de compte, phase compte, `EnterWorld`), `AGENTS.md` si une ligne « Where code goes » change

- [ ] **Step 1: Gate** — `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate` (redirigé) · Expected: 0 erreur, tests verts.
- [ ] **Step 2: `driver.mjs lobby`** : nouvelle session web sans sélection, `/api/ssotoken` → ticket de compte ; socket : `SSOTicket` → attendre `RpgCharacterList` (9000) et `RpgClassList` (9001) ; `RpgCharacterCreate` (nom aléatoire, figure du driver, `M`) → `RpgCharacterResult` `ok` + liste ; `RpgCharacterChooseClass(id, class.warrior)` → `ok` ; `RpgEnterWorld(id)` → `AuthenticationOK`, `RoomForward` ; entrée en room → `RpgCharacterState` (niveau 1, maxHealth 120) ; `POST /api/admin/characters/{id}/xp` 250 → `RpgCharacterProgress` (niveau 3) reçu sur la socket ; puis un `RpgCharacterCreate` envoyé en jeu → aucune réponse. Lancer sur `ambreia-database`, ports 40010/40011 (la session client occupe 40000) · Expected: chaque étape affichée et vérifiée.
- [ ] **Step 3: Docs et commit** — `docs : réseau RPG et écran des personnages`.
