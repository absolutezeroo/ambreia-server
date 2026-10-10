# Ambreia — matrice de modularité (MODULARITY-MATRIX)

Audit du 2026-10-10, lecture seule, `main` @ `c24e882`. Compagnon de `ENTERPRISE-AUDIT.md`.

## 1. Bounded contexts réels (§4)

Les frontières ci-dessous sont celles que le code dessine aujourd'hui, pas celles de la spec.

| Contexte | Projets | Possède (données) | Invariants | Contrats publics (ce que les autres voient) | Événements émis | Événements consommés | Dépend de (autorisé) | Dépend de (interdit, constaté) |
|---|---|---|---|---|---|---|---|---|
| **Réseau & protocole** | Networking, Pipeline, Messages, Protocol, Revisions, PacketHandlers, Crypto | sessions socket, file sortante par session | identité toujours issue du contexte de session, jamais du corps ; cadre ≤ 64 KiB | `IMessageHandler<T>`, `MessageContext`, composers typés, `ISessionGateway` | `PlayerConnected/Disconnected` | — | Primitives, Logging, Runtime | aucune (handlers n'ouvrent ni DB ni socket : mur 1) |
| **Comptes & sessions** | WebApi.Authentication, Players/Authentication, Players/Sessions | `player_accounts`, tickets, sessions web | un ticket par compte ; mot de passe BCrypt 12 ; grâce de reconnexion 60 s | `IAccountAuthenticator`, `IAuthenticationService`, `IWorldEntry` | `PlayerLoggedIn/LoginFailed` | — | Database, Crypto | — |
| **Joueurs & présence** | Players (grains) | `players` (carte), présence en mémoire, `server_config` | un socket par joueur ; file sortante ≤ 500 ; la présence envoie, les autres ne touchent pas le socket | `IPlayerGrain`, `IPlayerPresenceGrain` (16 méthodes), `IPlayerDirectoryGrain`, `IServerConfigGrain`, `IChatChannel/Rule/LinkKind` | `PlayerEntered/LeftRoom`, `Player*Changed`, `ChatSending/Sent` | — | Database, Events, Messages, Protocol | **relaie `CharacterCombatProfile` (type Rpg) vers la room** (`IPlayerPresenceGrain.Avatar.cs:15`) ; appelle `IPlayerCharacterGrain` à l'entrée monde |
| **Rooms (runtime de zone)** | Rooms, Furniture | `furniture`, hauteurs ; état vivant de zone | serveur pathfinde chaque pas ; une étape de tick qui lève n'arrête pas le tick ; une room sans joueur ne simule pas | 9 facettes de `IRoomGrain` (46 méthodes), `IZoneRuntime` (implémenté), `[RoomObjectLogic]`, `IRoomEventListener` | `PlayerEnteringRoom` (annulable, sans écouteur), `Item*`, events locaux de room | — | Database, Events, Furniture, Rpg (noyau, pour `IZoneSimulationFactory`) | `RoomTargetModule.cs:170` lit `PlayerCharacterGrain` (niveau, classe) : la room connaît les personnages |
| **Monde (éditeur de zones)** | World | `zones`, `zone_versions`, **`rooms` d'une zone** (SQL brut) | document validé (64×64, schéma 1, atteignabilité) avant stockage ; publication = nouvelle version | `IZoneEditorService`, `IZoneRoomSource`, `ZoneValidator`, `ZoneCompiler` | — | — | Database | le modèle du document (`Primitives/World/Layers`, `Rules`, 61 fichiers) vit dans le kernel |
| **Contenu RPG** | Rpg/Content | `rpg_content*` | ids stables ; import atomique ; contenu stocké relu strictement au démarrage | `IRpgContent`, `IContentKind`, `IContentReferences`, `IRpgContentService` | `ContentReloaded` (sans écouteur) | — | Database | — |
| **Personnages & objets** | Rpg.Characters (+ `Vortex.Rpg/Items` pur) | `player_characters`, `rpg_items`, `rpg_item_grants`, **`players` (insert)** | un objet, un emplacement ; quantité > 0 ≤ pile ; don idempotent par clé ; XP serveur seulement | `IPlayerCharacterGrain` (19 méthodes), `IAccountCharactersGrain`, `IStatSource`, `CharacterCombatProfile` | `CharacterCreated/ClassChanged/StatsChanged`, `LevelUp`, `XpGaining/Gained`, `Item*` | `PlayerEnteredRoom` (dernière zone) | Rpg, Database, Events, **Protocol** | crée la ligne `players` (concept Joueurs) ; construit des composers |
| **Combat** | Rpg.Combat | état de zone (PV, menace, cooldowns) en mémoire | ordre des hit steps stable ; RNG injecté ; un tick n'écrit pas en base | `IZoneSimulation`, `ICombatSystem`, `IHitStep`, `IStatModifierSource`, `Combatant` | `CombatEntered/Left`, `MonsterKilled`, `PlayerDied` (sans écouteur) | — | Rpg, **Protocol** | `ShieldStep` downcast `Combatant` ; `CombatZone.Outbox` construit 5 composers ; appelle `IPlayerCharacterGrain` |
| **Compétences & statuts** | Rpg.Skills | — (contenu + état de zone) | checks d'incantation serveur (§6.1) ; statuts portés par les vitals | `ISkillEffect`, `ICastingSystem`, `IStatusSystem` | — | — | Rpg, Rpg.Combat (**interdit par la spec §3.4, toléré par le script**), Protocol | enums fermés (statut, stat, cible) |
| **Monstres** | Rpg.Monsters | — (contenu + spawns en mémoire) | une apparition par point par tick ; comportement par clé | `IMonsterBehavior`, `RpgZoneSimulationFactory` | — | — | Rpg, Rpg.Combat, Protocol | — |
| **Permissions** | Permissions | rôles, capacités | fail-closed sur erreur DB ; cache 60 s | `IPermissionService`, `Capabilities`, `RoomSecurityPolicy` | — | — | Database, Observability | — |
| **Observabilité** | Observability, Logging | `audit_events`, `item_events`, `room_entry_logs`, `error_*` | hors chemin chaud ; canal borné 10 000 | `IVortexMetrics`, `IAuditSink`, `IItemForensics` | — | 14 events (audit, visites, forensics) | Database, Events | — |
| **Hôte & plugins** | Main, Plugins, Runtime | — | composition seulement | `IHostPluginModule`, `IVortexPlugin` | — | — | tout | `MaintenanceAnnouncer`, `ReferenceDataReloader` font un peu de métier dans Main (LOW) |
| **Web** | WebApi | sessions web (mémoire) | capacité par route ; corps bornés (4 / 16 MiB) | routes | — | — | Database, Events, Primitives | — |

Contextes **absents** et à prévoir sans les créer aujourd'hui : Économie (or, échanges, hôtel des
ventes), Quêtes, Métiers, Social (groupe, guilde), Donjons (au-delà des copies), Modération, Administration.

## 2. Propriété des concepts (§5) — un seul propriétaire logique ?

| Concept | Propriétaire(s) constaté(s) | Verdict |
|---|---|---|
| Account | WebApi.Authentication (ligne) + Players/Authentication (tickets) + AccountSessionGrain (socket en phase compte) | 3 vues d'un même concept, **cohérentes** : compte web / ticket / phase socket. Nommer un contexte « Comptes » qui les regroupe (TARGET §3) |
| Session (socket) | Networking (`SessionGateway`) + `PlayerPresenceGrain` (observer) | 2, par conception (transport / joueur) ✔ |
| Player (carte) | `PlayerGrain` (mise à jour) + `AccountCharactersGrain` (création) | **2 écrivains** ; à ramener à un (création par `IPlayerGrain`/fabrique) |
| Character | `PlayerCharacterGrain` | 1 ✔ |
| Inventory, Item, Equipment | `PlayerCharacterGrain` (+ règles pures `CharacterInventory`) | 1 ✔ ; le grain porte 9 partials et 19 méthodes : découper quand l'échange arrive |
| Room | `RoomGrain` (vivant) ; ligne `rooms` créée par `World` | 1 runtime, **0 écrivain déclaré** de la table |
| Zone | World (document) + Rooms (compilé, runtime) | 2 par conception (définition / instance) ✔, mais le modèle du document est dans Primitives |
| Dungeon (copie) | `ZoneInstanceGrain` + `RoomGrain` | ✔ |
| Combat, Skill, Status, Monster | `CombatZone` dans `RoomGrain` | 1 ✔ |
| Chat | `PlayerPresenceGrain` (`ChatPipeline`) | 1 ✔ ; les flux de groupe n'ont pas de place |
| Target | `RoomTargetModule` | 1 ✔ |
| Content | `RpgContentService` + registre | 1 ✔ |
| Config serveur | `ServerConfigGrain` + `dev_zone.sql` | 1 au runtime ✔ |
| Permissions | `Vortex.Permissions` | 1 ✔ ; 11 capacités sur 21 sans site de contrôle |
| Guild, Trade, Currency, Quest | — | n'existent pas ; placeholders de chat `group`/`guild` |

Aucun concept à trois propriétaires. Deux à corriger (`players`, `rooms`), un à surveiller
(`PlayerCharacterGrain` comme personnage **et** inventaire).

## 3. Kernel partagé (§6) — `Vortex.Primitives`, 374 fichiers

| Classe | Fichiers | Exemples | Devrait vivre dans |
|---|---|---|---|
| Vraie primitive globale | ≈ 20 | `PlayerId`, `RoomId`, `RoomObjectId`, `CorrelationId`, `ActionContext`, codes d'erreur, `SingletonGrainId` | Primitives ✔ |
| Interfaces de grains + accesseurs | 21 + `GrainFactoryExtensions` | `IRoomGrain` (9 facettes), `IPlayerPresenceGrain`, `IPlayerCharacterGrain` | `<Module>.Contracts` (Orleans.Sdk y suffit) |
| Snapshots `[GenerateSerializer]` | 48 | `PlayerSummarySnapshot`, `RoomPointerSnapshot`, `CharacterCombatProfile` | contrats du module |
| Modèle de domaine Rooms | 105 (`Rooms/Object/*` 27 interfaces, 23 events locaux, 17 enums, 13 snapshots) | `IRoomObject`, `IRoomAvatar`, logiques d'objets | `Vortex.Rooms.Contracts` (consommé par Rooms seulement) |
| Modèle du document de zone | 61 (`World/Layers` 27, `World/Rules` 16) | `ZoneDocument`, `ZoneCondition`, couches | `Vortex.World.Contracts` (consommé par World et Rooms) |
| Contrats d'extension | 8 (Chat) + Rpg 23 | `IChatChannel`, `IRpgContent`, définitions | contrats des modules |
| Infrastructure | Networking/Packets 22, Plugins 11, Hosting 8, Observability 18, Crypto 3, Console 2 | `ClientPacket`, `IVortexPlugin`, `IVortexMetrics` | acceptable dans un kernel d'infrastructure, séparable en `Vortex.Abstractions` |
| Events | 32 | domaines mêlés (chat, combat, rpg, joueurs) | avec le module producteur |

Verdict : le kernel **n'est pas un dépotoir** (0 référence vers le haut, 0 interface orpheline, 0 type
internal : 398 publics), mais il porte deux modèles de domaine entiers (Rooms, World = 44 %) : toute
modification d'une couche de zone recompile Networking, Crypto, Protocol. Mesure cible : Primitives
≤ 120 fichiers (primitives, infrastructure, events d'intégration), le reste dans `Module.Contracts`.
La découpe se fait **sans casser un mur** (Platform → contrats de Platform reste descendant).

## 4. Couplage (§93)

Graphe réel (26 projets de production, sans les arêtes vers Primitives ni depuis Main) : voir
`ENTERPRISE-AUDIT.md` §AS-IS. Pas de cycle. Pas de référence Players ↔ Rooms (ils se rencontrent par
Primitives et Orleans). `Rooms → Rpg` est la seule arête Platform → Platform prévue ;
`Rpg.Skills → Rpg.Combat` est celle que la spec interdit.

| Projet | Ca (référencé par) | Ce (référence) | Lecture |
|---|---|---|---|
| Primitives | 24 | 0 | hub attendu, trop gros (§3) |
| Database | 12 | 1 | **second hub** : les entités EF sont un contrat partagé par WebApi, Plugins, Observability, Rpg, World |
| Protocol | 10 | 1 | **le fil de fer fuit** : les 4 feuilles RPG construisent des composers ; un changement de wire rebuild 10 projets |
| Logging | 10 | 1 | pass-through (`LogAndForget`, `VortexException`) ; fusionnable dans Primitives |
| Events | 7 | 2 | `ICancellableEventPublisher` oblige Players/Rooms/Characters à référencer le runtime d'events |
| Rpg (noyau) | 6 | 2 | sain |
| Rpg.Combat | 3 | 4 | **hub RPG de fait** : Skills et Monsters en dépendent ; fichier le plus modifié de l'historique (`CombatZone.cs`, 10/50 commits) |
| Rooms | 1 | 8 | plus gros fan-out après Main |
| Main | 0 | 25 | composition root ✔ |

Couplage de changement (50 commits, 2026-10-09/10) : 24 % des commits touchent ≥ 4 projets ; 20 %
touchent Primitives **et** un projet RPG ; paires les plus fréquentes Combat↔Monsters (8), Combat↔Skills
(7), Primitives↔Combat (7), Primitives↔Rooms (7), Rooms↔Combat (6). Fichiers : `CombatZone.cs` ↔
`RoomZoneRuntime.cs` ↔ `RpgZoneSimulation.cs` bougent ensemble à chaque feature de combat. **La couture
`IZoneRuntime` n'est pas encore stable** : chaque mécanique y ajoute un membre.

## 5. Surface publique (§8)

| Projet | public / internal | Consommateurs réels | Devrait |
|---|---|---|---|
| Primitives | 398 / 0 | tous | public par nature ; réduire par découpe |
| Protocol | 118 / 0 | Revisions, handlers, grains | public (contrats de wire) |
| Rooms | 113 / 8 | Main, tests | **la plupart internal** (modules, systèmes, état) ; public = grains et `[RoomObjectLogic]` de base |
| Rpg, Rpg.Combat, Rpg.Skills, Rpg.Monsters | 75/3, 32/0, 28/0, 11/0 | Main, les uns les autres | internal par défaut, `InternalsVisibleTo` pour les tests ; public = contrats d'extension |
| PacketHandlers | 37 / 0 | scan (exige public) | contrainte du scanner : à lever (scanner les types internal) |
| Players | 8 / 22 | — | **modèle à suivre** |
| Revisions | 8 / 125 | — | modèle à suivre |
| Database | 48 / 0 | 12 projets | entités publiques par EF ; réduire les consommateurs (§4) |

Règle cible : `internal` par défaut dans tout projet Platform, public = contrats et pièces
d'extension. Le scanner de handlers accepte les types `internal` (aujourd'hui il les avertit et les ignore).

## 6. Revue des God objects (§91)

| Classe | Lignes / partials | Dépendances ctor | Méthodes d'interface | Domaines connus | Verdict |
|---|---|---|---|---|---|
| `RoomGrain` | 1 908 / 15 (+ modules 3 986, systèmes 704, zones 2 380) | 17 | 46 (9 facettes) | zones, combat (seam), personnages (`RoomTargetModule`), interactions, émotes | **coordinateur, pas God object** : la logique est dans les modules ; mais `RoomLiveState` mutable partagé par 20+ fichiers (même des exécuteurs de triggers) et des étapes de tick codées. MEDIUM |
| `PlayerPresenceGrain` | 1 204 / 6 | 8 services, 10 interfaces de grains | 16 | réseau, permissions, monde, rooms, personnages, chat, config | **hub par construction** : seul point où une feature par joueur peut se brancher, sans prise. MEDIUM |
| `PlayerCharacterGrain` | 1 437 / 9 | — | 19 | classe, XP, stats, dernière zone, HUD, objets, vitals, statuts portés, compétences, barre | personnage **et** inventaire : scinder quand l'échange arrive. MEDIUM |
| `CombatZone` | 414 + 4 partials + `Outbox` | — | — | combat, morts, XP, composers, grains de personnage | hub RPG ; les composers et l'appel de grain le rendent impur. MEDIUM |
| `CastSystem` | 750 (le plus gros fichier) | — | — | checks, approche, incantation, canalisation, télégraphes, annulations | découper par responsabilité (`Checks`, `Telegraph`, `Channel`) ; LOW |
| `RoomMapModule` | 1 484 / 7 | — | — | marche, hauteurs, cases | gros mais cohérent ; LOW |
| `EnvelopeHost` | 486 | — | — | pipeline générique | OK |

## 7. Scorecard de modularité par domaine (§95) — 0 à 10

| Domaine | Encapsulation | Extensibilité | Testabilité | Couplage (10 = faible) | Propriété | Note | Pourquoi |
|---|---|---|---|---|---|---|---|
| Réseau & protocole | 8 | 7 | 6 | 7 | 9 | **7,4** | identité par contexte, ids de header internes, doublons refusés ; mais handlers parallèles sans politique, gates qui échouent ouverts, aucun test de Networking/Messages/Events, limites manquantes (deadline, proxy) |
| Comptes & sessions | 7 | 5 | 7 | 8 | 8 | **7,0** | tickets solides, BCrypt ; `DateTime.UtcNow` sans horloge, ticket non lié à l'origine, pas de reset de mot de passe |
| Joueurs & présence | 6 | 4 | 7 | 6 | 7 | **6,0** | `internal` par défaut (modèle), chat ouvert ; présence = hub sans prise, relaie un type Rpg |
| Rooms | 6 | 5 | 8 | 5 | 8 | **6,4** | harnais et 369 tests ; registres de zone statiques, tick fermé, état partagé, 113 types publics, 8 références sortantes |
| Monde (éditeur) | 7 | 4 | 7 | 7 | 7 | **6,4** | validateur solide ; `switch` par kind, SQL brut sur `rooms`, modèle dans le kernel, pas de lecteur d'ancien schéma |
| Contenu RPG | 9 | 9 | 9 | 9 | 9 | **9,0** | kind générique, doublon refusé, import atomique, schémas générés, nouveau kind = 8 fichiers sans cœur ; `ContentReloaded` sans écouteur |
| Personnages & objets | 7 | 6 | 9 | 5 | 8 | **7,0** | `CharacterInventory` pur et 65 tests ; grain à 9 partials, référence Protocol, crée `players` |
| Combat | 6 | 7 | 9 | 4 | 8 | **6,8** | RNG/horloge injectés, hit chain et systèmes ouverts ; stats/camp fermés, composers dans le moteur, hub de changement |
| Compétences & statuts | 6 | 6 | 9 | 5 | 9 | **7,0** | 165 tests ; effets ouverts ; statuts et cibles fermés ; dépend de Combat contre la spec |
| Monstres | 7 | 6 | 8 | 6 | 8 | **7,0** | comportement par clé ; `Melee` scellé sans base ; contenu M1c sans runtime |
| Permissions | 8 | 6 | 5 | 8 | 8 | **7,0** | fail-closed, gates uniformes ; 11 capacités sans site, 4 tests |
| Observabilité | 8 | 6 | 7 | 8 | 9 | **7,6** | pipeline borné et testé ; audit limité au cycle joueur, tracing mort, pas de scope joueur/room |
| Hôte & plugins | 8 | 8 | 7 | 6 | 9 | **7,6** | `IHostPluginModule` + test d'enregistrement ; web API en plugin = complexité accidentelle, Dockerfile mort |
| Web API | 8 | 6 | 8 | 8 | 8 | **7,6** | gates, limites, OpenAPI épinglé ; health trompeur, logs effacés, limiteur par IP derrière proxy |

Moyenne : **7,1 / 10**. Le contenu RPG est le modèle ; les frontières Rooms/Players/Combat sont celles
à durcir en priorité (P1 de la roadmap).

## 8. Scorecard entreprise (§96)

| Axe | Note /10 | Justification courte |
|---|---|---|
| Architecture | 7 | couches réelles et vérifiées, composition root propre, seam de zone ; Primitives trop gros, Combat hub |
| Modularité | 7 | 7 points d'extension DI exemplaires ; 5 familles d'enums fermées, zone statique |
| Extensibilité | 6 | feature à clé additive ; feature de domaine = 5 fichiers d'infrastructure |
| Correction | 7 | 1 642 tests verts, invariants d'inventaire ; M1c sans runtime, fenêtre de 30 s sur les PV |
| Sécurité | 6 | autorité serveur partout, gates, BCrypt, limites ; amplification pré-auth, proxy, deadline, plugins full-trust |
| Fiabilité | 6 | tick isolé, write-behind, canal d'audit borné ; health couplé à la DB, pas d'overrun, pas de retry/timeout explicites |
| Performance | 5 | aucune mesure ; A* 12 288 nœuds synchrone, copie WS quadratique, chaîne de debug par paquet |
| Scalabilité | 4 | mono-silo déclaré et gardé ; flux mémoire, caches de process, sessions web en mémoire |
| Intégrité des données | 7 | un écrivain par table, dons idempotents, import atomique ; migrations manuelles, pas d'unicité d'emplacement |
| Tests | 7 | pyramide réelle (pur/grain/DB/protocole/archi/hosting) ; 0 property, 0 fuzz, 0 charge, e2e Windows-only |
| Observabilité | 6 | 25 instruments, audit, forensics ; pas de tracing, pas de scope, audit admin absent |
| Exploitabilité | 5 | une seule sonde `/health`, console utile ; pas de runbook, pas de liveness/readiness, backup non testé |
| Déployabilité | 4 | Dockerfile cassé, migrations au démarrage, pas de lock file |
| Maintenabilité | 8 | aucun fichier > 1 000 lignes, conventions de layout automatisées, csharpier, analyzers en erreurs |
| Expérience développeur | 7 | AGENTS.md + 9 skills + hooks ; README obsolète, outillage DB/live Windows-only, pas de golden path grain/endpoint/module |
| Documentation | 7 | 13 specs, 17 plans, `architecture.md` tenu ; pas d'ADR, dérives (couches, grains) |

## 9. Niveau de maturité (§97)

| | |
|---|---|
| **Niveau actuel** | **2 — Production capable (limite 1→2 sur le déploiement)** : le serveur tourne, est testé et gardé par des scripts, mais ne se déploie pas depuis le dépôt (Dockerfile) et ne s'exploite pas à l'aveugle |
| **Niveau cible (24 mois)** | **3 — Enterprise modular** : modules à frontières explicites, extension sans cœur, observabilité contractuelle, migrations forward-only, CI bloquante |
| **Écart** | P0 (déployabilité, amplification réseau, proxy) → P1 (contrats par module, kernel découpé, propriété `players`/`rooms`) → P2 (enums ouverts, zone DI, tick ouvert, flux de présence) → P3 (readiness, tracing, audit admin, runbooks) ; le niveau 4 (multi-silo) n'est **pas** un objectif tant que l'occupation d'une zone ne le justifie pas |
