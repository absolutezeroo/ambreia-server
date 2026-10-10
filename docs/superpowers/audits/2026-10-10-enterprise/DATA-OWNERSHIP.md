# Ambreia — propriété des données (DATA-OWNERSHIP)

Audit du 2026-10-10, en lecture seule, sur `main` @ `c24e882`. Compagnon de `ENTERPRISE-AUDIT.md`.
Règle d'entreprise évaluée : **une donnée mutable a un seul écrivain logique** (spec unifiée INV-004).

## 1. Matrice AS-IS (27 `DbSet`, `Vortex.Database/Context/VortexDbContext.cs:15-69`)

« Écrivains code » = tout fichier hors tests qui ajoute, supprime, `ExecuteUpdate`/`ExecuteDelete` sur la
table. « Écrivains SQL » = scripts et seeds. Les verdicts sont ceux de la passe faux positifs.

| Table | Propriétaire logique (DATA-MODEL / docs) | Écrivains code | Écrivains SQL | Lecteurs | Verdict |
|---|---|---|---|---|---|
| `player_accounts` | compte web | `Vortex.WebApi.Authentication/AccountAuthenticator.cs:115` (insert) | — | `PermissionSeederService`, `AuthenticationService` | OK, un écrivain |
| `account_tickets`, `security_tickets` | `AuthenticationService` | `Vortex.Players/Authentication/AuthenticationService.cs:77,100,158,172,210,248` (hard delete + insert) | `driver.mjs:965` (DELETE, outillage dev) | mêmes | OK ; suppression dure assumée (contredit DATA-MODEL §0.2 « jamais de hard delete » : le texte est faux, pas le code) |
| `players` | `PlayerGrain` | `PlayerGrain.cs:170-181` (`ExecuteUpdate` nom/motto/figure/genre) ; **`Vortex.Rpg.Characters/Grains/AccountCharactersGrain.cs:189` (insert)** | `Seeds/seed_world_owner.sql` | `PlayerDirectoryGrain`, `PermissionService`, `ZoneEditorService`, `RoomGrain`, `AuthenticationService` | **Deux grains, colonnes disjointes** : l'insert précède toute activation de `PlayerGrain`, l'index unique `players(name)` arbitre les courses. LOW, à documenter comme « création par le domaine Personnages, carte par `PlayerGrain` » |
| `player_characters` | `PlayerCharacterGrain` | `PlayerCharacterGrain.cs:221`, `.Class.cs:51,105` (`ExecuteUpdate` 7 colonnes, jamais les ids de classe) ; `Content/ClassReferences.cs:25`, `SpecializationReferences.cs:24` (réécriture des ids par l'import) | `scripts/sql/2026-10-09-character-vitals.sql`, `2026-10-10-character-action-bar.sql` (DDL) | grain | Conçu (`IContentReferences` : le grain relit l'id quand `IRpgContent.Version` bouge, ne le réécrit jamais). INFO |
| `rpg_items` | `PlayerCharacterGrain` → `CharacterItemStore` | `Grains/Persistence/CharacterItemStore.cs:85` (`IsModified` sélectif, jamais `definition_id`) ; `Content/ItemReferences.cs:26` | — | store | Conçu, idem. **Manque l'unicité `(player_id, container, slot)`** : `At()` est un `FirstOrDefault` et aucun index ne l'impose. MEDIUM |
| `rpg_item_grants` | `CharacterItemStore` | `CharacterItemStore.cs:109` (même `SaveChanges` que l'objet accordé : une transaction) | — | store | OK ; clé d'idempotence **globale** (`grant_key` unique sur toute la table, pas par joueur) : une clé partagée entre deux joueurs répond `duplicate_grant` au second. MEDIUM sémantique |
| `rpg_content`, `rpg_content_packs`, `rpg_content_migrations`, `rpg_content_texts` | `RpgContentService` | `Vortex.Rpg/Content/Import/RpgContentService.cs:244,260,285,344` (une transaction, `SemaphoreSlim`, rejouée par la stratégie de retry EF) | `scripts/sql/rpg-content-reset.sql` (soft delete) | `RpgContentRegistry` | OK |
| `rooms` | `RoomGrain` (DATA-MODEL) | aucun via EF ; **`Vortex.World/ZoneEditorService.cs:239-245` : `INSERT INTO rooms` en SQL brut**, relecture par marqueur `Description == marker` (:248-254) | `scripts/sql/dev_zone.sql` | `RoomGrain.cs:506`, `ZoneEditorService` | Le propriétaire déclaré n'écrit jamais la table ; l'éditeur crée la ligne hors conventions EF. LOW-MEDIUM (smell, pas de clobber : la ligne est neuve) |
| `room_rights` | `RoomGrain` | **aucun écrivain** (lu à `RoomGrain.cs:551`) | — | `RoomGrain` | Table morte en écriture. LOW (candidat `removing-dead-code`) |
| `room_models` | donnée de référence | aucun | `import_reference_data.sql` (TRUNCATE + INSERT) | `RoomModelProvider`, `ZoneEditorService` | OK (lecture seule au runtime) |
| `furniture_definitions` | donnée de référence | aucun | `import_reference_data.sql`, 6 seeds rejoués par `import-reference-data.ps1:57-64` | `FurnitureDefinitionProvider` | OK ; le `DELETE … wf_%` cascade dans `furniture` (documenté, dangereux sur une base vivante) |
| `furniture` | `RoomGrain` → `RoomPersistenceGrain` | `RoomPersistenceGrain.cs:176-201` (write-behind 2 s, 100 par flush, drain à la désactivation) ; `RoomFurnitureLocationStore.cs:49,73` (`ExecuteUpdate` conditionnel `room_id`/owner) | cascade FK de l'import de référence | `RoomItemsProvider.cs:54` | OK ; fenêtre de perte ≤ 2 s |
| `zones`, `zone_versions` | `Vortex.World` | `ZoneEditorService.cs:259-260,394-417`, `.Locking.cs:46,64`, `.Publishing.cs:82-98,174` | — | `ZoneRoomSource` | OK ; `RoomGrain` lit, ne possède pas |
| `server_config` | `ServerConfigGrain` | `ServerConfigGrain.cs:82-96` | `Seeds/add_server_config.sql`, **`scripts/sql/dev_zone.sql`** (`ON DUPLICATE KEY UPDATE world.start_room_id`) | cache du grain | Le grain cache (`_cache`, :63-66) ; `ReloadAsync` existe. Script documenté « base fraîche ». INFO |
| `roles`, `role_permissions`, `player_account_roles` | `Vortex.Permissions` | `PermissionSeederService.cs:51,56,148` | — | `PermissionService` (cache 60 s, fail-closed) | OK ; `PermissionSeederService` avale ses exceptions : l'hôtel peut démarrer sans rôle. LOW |
| `audit_events`, `item_events`, `room_entry_logs` | Observabilité | `AuditWriterService.cs:55-58`, `RoomVisitLog*Handler.cs`, `ForensicsRetentionService.cs:108-166` (`ExecuteDelete`) | — | rétention seulement | OK ; **aucun index dont la première colonne est `occurred_at`/`created_at`** : chaque balayage de rétention est un full scan. MEDIUM à l'échelle |
| `error_groups`, `error_occurrences` | Observabilité | `ErrorGroupingWriterService.cs:63,92,112,177` | — | mêmes | Pas de rétention, `remote_ip` en clair (l'audit hache). MEDIUM |

Résumé : 27 tables, **0 table possédée par un grain et écrite par le web API**, 0 interface de grain sans
implémentation (21 accesseurs de `GrainFactoryExtensions.cs` vérifiés), 2 tables à double écrivain de
conception (`players`, et les réécritures d'ids de contenu), 1 table dont le propriétaire déclaré n'écrit
jamais (`rooms`), 1 table morte (`room_rights`).

## 2. Ce qui n'est pas en base mais est de l'état

| État | Propriétaire | Durabilité | Fenêtre de perte |
|---|---|---|---|
| Présence (socket, file sortante 500, room active, abonnement de flux) | `PlayerPresenceGrain` | mémoire seulement (la doc dit « Orleans storage » : faux, seul `PubSubStore` est configuré, `HostApplicationBuilderExtensions.cs:127`) | perdue au redémarrage, par conception |
| Positions des meubles | `RoomPersistenceGrain` | write-behind 2 s | ≤ 2 s |
| XP, niveau, dernière zone, barre d'action, **PV/mana** (`SetVitalsAsync` ne marque que sale, `PlayerCharacterGrain.Vitals.cs:83-96`) | `PlayerCharacterGrain` | timer 30 s + désactivation ; niveau écrit immédiatement | **≤ 30 s, PV en zone compris** (un crash silo rend des PV d'avant le combat) |
| PV/mana/cooldowns/incantations/statuts/menace des combattants d'une zone | `RoomGrain` (runtime de zone) | mémoire ; écrits au personnage à la sortie et à la désactivation (`RoomGrain.cs:409-451`) | un crash de silo perd l'état de zone ; la désactivation normale est awaitée |
| Monstres, apparitions, cibles, portes de zone | `RoomGrain` | mémoire, reconstruit à l'activation | aucune par conception (INV-003) |
| Sessions web | `AccountSessionStore` en mémoire | 24 h en mémoire | perdues au redémarrage et au rechargement du plugin web |
| Caches de référence (meubles, modèles, contenu RPG) | singletons de process | rechargés par console/import | locaux au silo (bloqueur multi-silo) |

## 3. Transactions transverses (§23) et idempotence (§24)

| Opération | Domaines impliqués | Mécanisme actuel | Verdict |
|---|---|---|---|
| Don d'objet admin | Web API → `PlayerCharacterGrain` | clé de don unique + objet dans une transaction (`CharacterItemStore.cs:85-109`) ; rejeu → `duplicate_grant` | idempotent ✔ ; course inter-grains (même clé, deux joueurs) → `DbUpdateException` propagée en 500 (MEDIUM) |
| Don d'XP admin | Web API → grain | plafond `MaxXpPerGrant` 100 000, **pas de clé d'idempotence** : un retry HTTP double l'XP | à corriger avant tout usage automatisé (MEDIUM) |
| Mort d'un monstre → XP aux participants | `CombatZone` → N `PlayerCharacterGrain.GainXpAsync` (awaités dans le tick) | pas de clé ; une seule fois par mort en pratique | acceptable ; le futur loot aura besoin d'un **numéro de séquence de kill** dans `MonsterKilledEvent` (absent) |
| Import de contenu | `RpgContentService` + `IContentReferences` (réécrit `player_characters`, `rpg_items`) | une transaction MySQL, sérialisée par `SemaphoreSlim` | atomique ✔ ; rejoué deux fois : diff par hash → no-op ✔ |
| Publication de zone | `ZoneEditorService` (`zones`, `zone_versions`) puis `RoomGrain.ReloadZoneAsync` | DB puis appel de grain, pas de compensation si le reload échoue | la room garde l'ancienne version jusqu'à sa prochaine activation ; acceptable (éditeur) |
| Connexion / ticket | Web API → `AuthenticationService` → socket | un ticket par compte, remplacement à la ré-émission, lifetime glissante 300 s | OK ; `IsLocked` = identifiant permanent (MEDIUM exploitation) |
| Sortie de zone (PV restants) | `RoomGrain` → `PlayerCharacterGrain.SetVitalsAsync` | awaité avant que l'avatar parte ; marqué sale, écrit au timer | voir fenêtre 30 s |
| Échange, achat, loot, artisanat, courrier | n'existent pas encore | — | la spec (§18) exige atomique + idempotent : **prévoir un journal (`economy_ledger`) et une clé d'opération avant d'écrire la première de ces features** |

Aucune saga ni outbox n'est nécessaire aujourd'hui : chaque opération cross-domaine est soit
« une transaction MySQL puis une notification en mémoire », soit « un grain puis un autre » sans
exigence de tout-ou-rien. Le premier vrai besoin arrivera avec l'échange entre deux personnages (deux
grains, deux sacs) : un **grain d'échange** propriétaire de la transaction, avec clé d'opération, suffit ;
pas d'outbox tant qu'aucun consommateur externe n'existe.

## 4. Stratégie de schéma (§70-71)

**AS-IS.** Une seule migration `InitialCreate` (`Vortex.Database/Migrations/20261009214347_InitialCreate.cs`,
27 tables, 52 index, 8 seeds appliqués aux lignes 1765-1772), régénérée à chaque changement d'entité,
épinglée par `SeedCatalogTests` (« exactement une migration », « chaque seed embarqué est appliqué »).

**Le problème, déjà présent.** La base vivante du propriétaire est **déjà** sur une chaîne forward-only
écrite à la main : sept scripts `scripts/sql/2026-10-08-*.sql` … `2026-10-10-character-action-bar.sql`
qui font le DDL puis `UPDATE __EFMigrationsHistory SET MigrationId = <nouveau> WHERE MigrationId = <ancien>`.
Conséquences :

1. l'ordre n'est que dans les commentaires (« à lancer après … ») ;
2. une base qui a sauté un script a une ligne d'historique qu'EF croit à jour et un schéma qui ne l'est
   pas : `MigrateOnStartup` (`MigrationHelper.cs:23-66`) voit « 0 en attente » et démarre ;
3. le DDL manuel doit rester équivalent octet pour octet à ce qu'EF émettrait (collations copiées à la main) ;
4. aucun test n'exécute la migration (`SqliteTestDb.cs:16-24` dérive le schéma du modèle, pas de la migration).

**Cible, en deux phases.**

| Phase | Quand | Règle |
|---|---|---|
| Développement / pré-release | tant qu'aucune base hors du poste du propriétaire n'existe | `InitialCreate` régénérée **+ les scripts forward numérotés deviennent une liste ordonnée exécutée par un outil** (`scripts/sql/forward/NNN-*.sql` + table `vortex_forward_history`), testée sur un MySQL jetable en CI |
| Production | dès la première base qu'on ne peut pas recréer (serveur de test public) | geler `InitialCreate` en baseline, `migrations add <Nom>` empilées, retirer l'assertion « une seule migration », déplacer les scripts manuels en `legacy/`, test CI `migrations script --idempotent` appliqué sur MySQL réel, migration exécutée **hors** du démarrage (job), `expand → migrate → contract` pour les renommages de colonnes |

Le seuil de bascule est une décision à prendre maintenant (ADR-006 dans `ENTERPRISE-AUDIT.md`), car
chaque jour de régénération ajoute un script manuel à la chaîne.

**Évolution des documents JSON.** Versionnés : `zone_versions.document` (`schema_version` = 1) et
`rpg_content.document` (hash + pack + `rpg_content_migrations`). Non versionnés : `furniture.extra_data`,
`furniture_definitions.extra_data`, `server_config.config_value`, `player_characters.action_bar`. Il
manque un **lecteur d'ancienne version** pour les zones : un document `schema_version` ≠ 1 n'est pas
migré mais refusé, et un document stocké illisible fait ouvrir la room comme une room Habbo nue avec un
simple log (`ZoneRoomSource.cs:582-597`). Un champ ajouté à une définition de contenu doit être optionnel
(gotcha AGENTS.md) ; un champ rendu `required` par erreur **bloque le démarrage** et n'a que
`rpg-content-reset.sql` comme issue.

Autres points de schéma : `ApplyConventions` plafonne toute chaîne non annotée à `varchar(512)`
(`ModelBuilderExtensions.cs:105-113`), `furniture.extra_data` compris ; les enums sont stockés en `int`
(règle « jamais renuméroter » sans test) sauf les tables d'audit en chaîne ; `players(name)` unique sans
`deleted_at` dans la clé (un nom soft-deleted est bloqué à jamais).

## 5. Sauvegarde et reprise (§72-73)

Existant : `DatabaseBackupService` (dump planifié, mot de passe par `MYSQL_PWD`, `DatabaseBackupService.cs:138-139`),
`DatabaseBackupConfig` avec `[Range]` **inerte** (pas de `ValidateDataAnnotations`). Pas de test de
restauration, pas de RPO/RTO écrits.

Proposition proportionnée :

| Sinistre | Retour à un état sain | Ce qui manque |
|---|---|---|
| Perte de la base | restaurer le dernier dump ; RPO = intervalle de dump (proposer 1 h en test public, 15 min + binlog en production) ; RTO cible 30 min | un script `restore.ps1`/`.sh` **testé mensuellement** sur une base jetable, le binlog activé |
| Mauvais déploiement | redéployer l'image précédente ; les migrations forward-only interdisent le retour arrière de schéma : **ne jamais `contract`** dans la même release que `expand` | tag d'image par commit, journal des migrations appliquées |
| Contenu corrompu | `rpg-content-reset.sql` puis `driver.mjs content` ; ou republier le pack précédent (`rpg_content_packs` garde l'historique) | une commande console `content rollback <pack>` |
| Migration ratée | restaurer le dump pris **par le job de migration** juste avant | le job de migration (phase Production) |
| Corruption massive d'objets | `item_events` + `rpg_item_grants` permettent de rejouer les dons ; les déplacements ne sont pas journalisés | journaliser les destructions/retraits avec la cause (fait) et un outil de relecture par personnage |

## 6. Piste d'audit (§74) et sécurité admin (§75)

Le canal d'audit (`IAuditSink` → `audit_events`, borné à 10 000, lot de 200, dead-letter JSONL) est
solide mais **n'enregistre que le cycle de vie joueur** (10 actions : `auth.login.*`, `session.*`,
`room.entered/left`, `profile.*`, `security.name.changed`). Rien pour :

- le don d'XP et d'objets admin (`POST /api/admin/characters/{id}/xp|items` : l'acteur passé au grain est la
  chaîne `"admin"`, l'id du compte appelant n'est pas enregistré) ;
- l'import de contenu (la ligne `rpg_content_packs` garde `AccountId` : suffisant, mais hors du canal) ;
- la publication, le verrou forcé et la suppression de zone ;
- l'attribution du rôle owner, les commandes console `quit`/`reload`.

Les catégories `Staff`, `Moderation`, `Economy`, `Security`, `System` de `AuditCategory` existent et sont
inutilisées. Règle cible : **toute route sous capacité et toute commande console écrivent une
`AuditEvent` avec l'id du compte acteur**, et le don d'objet accepte un plafond par appel *et* par jour
(aujourd'hui : `MaxStack × (BagSize + MaxParcels)` par appel, aucun plafond de débit).

## 7. Cible (§101) — propriété par agrégat

| Agrégat / donnée | Module propriétaire | Écrivain unique | Lecteurs autorisés | Contrat de lecture |
|---|---|---|---|---|
| Compte web (`player_accounts`, sessions web) | Accounts (aujourd'hui `WebApi.Authentication`) | `AccountAuthenticator` | Permissions, Players (sign-in) | `IAccountDirectory` (lecture) |
| Tickets | Accounts/Sessions | `AuthenticationService` | handler SSO | `IAuthenticationService` |
| Carte du joueur (`players` : nom, figure, motto) | Players | `PlayerGrain` (**y compris la création**, à déplacer depuis `AccountCharactersGrain` via `IPlayerGrain.CreateAsync` ou un `IPlayerFactory`) | Rooms (snapshot), Characters | `PlayerSummarySnapshot` |
| Personnage (classe, XP, niveau, stats, barre, vitals persistés) | Characters | `PlayerCharacterGrain` | Rooms (profil de combat), Presence (HUD) | `CharacterCombatProfile`, `RpgCharacterState` |
| Sac, équipement, colis (`rpg_items`, `rpg_item_grants`) | Items (aujourd'hui partie de Characters) | `PlayerCharacterGrain` → à terme un `PlayerInventoryGrain` **si** l'échange ou l'hôtel des ventes l'exigent | Characters (stats d'équipement), Economy | `IInventoryCommands` (don, retrait, destruction par clé) |
| Contenu RPG (`rpg_content*`) | Content | `RpgContentService` + règles `IContentReferences` déclarées par les modules | tous via `IRpgContent` | `IRpgContent.Get<T>/All<T>/Version` |
| Zones (`zones`, `zone_versions`) et **la ligne `rooms` d'une zone** | World | `ZoneEditorService` (via EF, plus de SQL brut) | Rooms (activation) | `IZoneRoomSource` |
| Meubles posés (`furniture`), hauteurs | Rooms | `RoomPersistenceGrain` | — | snapshots de room |
| État de zone vivant (combattants, monstres, cibles, cooldowns) | Rooms (runtime) + Combat (simulation) | `RoomGrain` | personne (lecture par messages) | `IZoneRuntime` |
| Config serveur | Platform | `ServerConfigGrain` | tous | `IServerConfigGrain` |
| Rôles et capacités | Permissions | `PermissionSeederService` + futur `IRoleAdmin` | gates web, garde d'acteur des grains | `IPermissionService.HasCapabilityAsync` |
| Audit, forensics, erreurs | Observability | writers asynchrones | admin (lecture) | `IAuditSink` |
| Futur : or, échanges, hôtel des ventes | Economy | un grain par transaction + `economy_ledger` | Items (crédit/débit par contrat) | `IEconomyLedger` (append-only, clé d'opération) |
| Futur : quêtes, métiers | Quests, Professions | `PlayerQuestGrain`, `PlayerProfessionGrain` | — | events `-ed` |

Règle qui en découle, à automatiser : **une entité EF n'est référencée que depuis son module propriétaire
et depuis `Vortex.Database`**. Aujourd'hui `check-architecture-walls.mjs` interdit la base aux handlers et
aux modules de room, pas le partage d'une entité entre deux modules (voir `EXTENSION-POINTS.md` §6 pour
les fitness functions proposées).
