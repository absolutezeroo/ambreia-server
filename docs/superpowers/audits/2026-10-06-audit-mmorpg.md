# Audit — ce qui sert le MMORPG (2026-10-06)

Synthèse de deux audits en lecture seule de `vortex-rpg-emulator` (branche `m0-menage`, après le M0) :
un audit des **133 tables** de `InitialCreate` et un audit des **systèmes** (projets, handlers,
logiques de meubles, joueurs, web, dashboard). Référence de conception :
`docs/superpowers/specs/2026-10-06-vortex-rpg-design.md`.

Les décisions du propriétaire prises après les audits **priment** sur leurs recommandations :
objet unique (un meuble est un type d'objet ; un seul sac), plusieurs personnages par compte,
décor de zone figé, maisons et halls de guilde décorables, catalogue scindé (répertoire pour
l'éditeur + catalogues de marchands PNJ), plus de navigator, MapEditor de bureau.

## 1. Constats majeurs

1. **Aucune logique de téléporteur.** Aucun `[RoomObjectLogic]` ne gère un téléporteur ;
   `FurnitureTeleportLinkEntity` n'est lu par aucun code. Les passages entre zones sont à écrire (F1).
2. **Les rooms n'ont pas de « type ».** `RoomEntity` / `RoomLiveState` ne distinguent pas zone,
   maison, hall de guilde, instance. Décor figé, fin du navigator et instances en dépendent (F1).
3. **81 handlers vides** (`await ValueTask.CompletedTask`) : 74 à retirer, 7 à garder comme puits
   (Pong, UniqueId, VersionCheck, FriendListUpdate, EventLog, LagWarningReport, LatencyPingReport).
4. **142 composers sur 447 jamais construits** en production (Game2/SnowWar, Competition, Crafting
   Habbo, Talent, Campaign, Youtube, vérification téléphone, FriendFurni…).
5. **Les couches §3.4 bis sont déjà violées par des références périmées.** `Vortex.PacketHandlers`
   (Cœur) référence Catalog, Players, Progression, Social pour 5 fichiers seulement
   (`Vortex.Catalog.Exceptions` ×3, `Players.Configuration`, `Social.Configuration`) ; Authentication,
   Catalog, Furniture, Inventory, Marketplace, Navigator référencent `Vortex.Players` sans l'utiliser.
   `check-architecture-walls.mjs` a 7 murs, aucun mur de couche.
6. **Le runtime de jeux n'héberge aucun jeu** en production (seul `TestOnlyGame` en test) ; gardé pour
   les arènes PvP après V1.
7. **Conflit de nom :** un `PlayerQuestGrain` existe déjà (`Vortex.Progression`, quêtes Habbo, 937 l.).
8. **Un compte possède déjà N avatars** (`/api/user/avatars`, `WebApiEndpoints.cs:652-760`) : base
   naturelle de « plusieurs personnages par compte ».
9. **Colonnes mortes dans `InitialCreate`** : `furniture.wired_chest_id`, `furniture.jukebox_id`,
   `community_goals.rewarded_level` (retirées par la passe de correction de la revue finale du M0),
   besoins des pets (`energy`, `nutrition`, `thirst`, `happiness`, `respect`), `players.respect_*`.
10. **FK à défaire avant certains retraits :** `rooms.category_id` → `navigator_flatcats` ;
    `catalog_offers.page_id` (obligatoire) ; cycle `groups.room_id` / `rooms.group_id` (futur QG) ;
    `furniture.rentable_space_furniture_id`.
11. **Docs en écart avec le code :** `AGENTS.md` cite `docs/architecture/` (supprimé), des fichiers
    `.js` devenus `.ts`, une recette « Add a room game » basée sur Freeze et `tools/catalog_converter` ;
    `CONTEXT.md` dit « six fichiers » de capability, `AGENTS.md`/`CLAUDE.md` en disent quatre.

## 2. Tables (133)

| Recommandation | Nombre | Tables |
|---|---|---|
| **GARDER** | 68 | comptes et sécurité (5) ; permissions, sanctions, signalements, filtre de mots (11) ; `players`, badges, effets, `figure_sellable_sets`, garde-robe, préférences ; monnaies, `commerce_operations`, `commerce_receipts`, `economy_ledger`, `item_events`, `shop_products`, `shop_orders` ; `room_models`, chatlogs, logs d'entrée, bans/mutes/droits/filtres de room (maisons), `furniture`, `furniture_definitions` ; `marketplace_settings` ; messagerie et amis (6), groupes et forums (10) ; moteur de succès (3) ; site web (articles, langues, notifications) ; audit et erreurs (3) ; `server_config` |
| **REFAIRE** | 21 | `player_clothing`, `furniture_purchasable_clothing` (M3) ; `rooms` (type de room, F1/M4) ; `catalog_pages`, `catalog_offers`, `catalog_products` (catalogue scindé, M3) ; `marketplace_offers` (hôtel des ventes, après V1) ; quêtes et journalières (5, M3) ; `pets`, `pet_levels` (compagnons, M5) ; `bots` (PNJ, M3) ; pêche (6, Pêche M5) |
| **RETIRER** | 20 | `player_chat_styles`, `player_chat_styles_owned` ; `player_navigator_preferences`, `navigator_flatcats`, `navigator_top_level_contexts`, `navigator_quick_links` ; `account_levels` ; `furniture_teleport_links` (morte) ; `hand_items` ; `songs` ; `catalog_frontpage_items` ; `achievement_resolutions`, `player_achievement_resolutions` ; `quizzes`, `quiz_questions`, `player_quizzes` ; `pet_commands`, `pet_command_names`, `pet_vocals`, `pet_palettes` |
| **À DÉCIDER** | 24 | Habbo Club et coffre (3) ; extras de room (3) ; extras de catalogue (cadeaux, offres et cadeaux HC) ; objectifs communautaires (3) ; derby de pêche (2) ; prize pools (4) ; fil social du site (6) |

## 3. Systèmes (code)

| Domaine | Garder | Modifier (jalon) | Retirer |
|---|---|---|---|
| Cœur | Networking, Pipeline, Crypto, Runtime, Events, Logging, Plugins, journal du commerce | Protocol, Revisions (ids ≥ 9000), Messages (limite par intention), Database, Observability, Primitives, Main, références de PacketHandlers (F0) | — |
| Handlers | handshake, tracking, modération, forums, guildes, blocage, avatar | liste d'amis → visites de maison (M4), préférences, Room/Engine (décor figé, F1), chat (styles, commandes de pets), pets (M5), réglages de room (M4), inventaire et échanges (M2), catalogue → marchands (M3), achats journaliers (M3), pêche (M5) | 74 handlers vides, protection anti-raid, bots possédés par le joueur, navigator (M4), CFH photo/selfie, guides/revue de chat/quiz, chansons, styles de chat, résolutions, espaces à louer, CreditFurniRedeem, objectifs communautaires |
| Rooms | persistance, annuaire, index d'objets, tick des avatars (modèle de regroupement), sonnette, décoration, éditeur de meubles en jeu (staff), cadre des jeux (dormant), listeners, modération | RoomGrain (runtime de zone), modèle d'interaction des meubles (F1), pathfinding (F1), chat, bots → PNJ (M3), pets → compagnons (M5), échanges (M2), modération et réglages limités aux maisons (M4), plan de sol (éditeur), création de room → instance de maison (M4) | protection anti-raid, soins des pets (commandes, paroles, objets en main) |
| Logiques de meubles (37 clés) | décor, avatar, `gate`, porte à sens unique, dimmer, couleur de fond, hauteur d'empilement, case de clic invisible, dés, roue, feux d'artifice, distributeurs, logiques de guilde, chronomètre et scores (dormants) | cabine d'habillage → station d'apparence (M3), vêtements achetables (M3) | jouets, trampoline et niche des pets (M5) ; à créer : passage/téléporteur (F1), coffre (M2/M3), nœud de récolte et atelier (M5) |
| Joueurs | présence, annuaire, vêtements, effets, monnaies (données), config serveur, figure et garde-robe | PlayerGrain ↔ personnage (F2), NUX → création de personnage (F2), wallet → or (M3), home room → « rentrer chez soi » (M4), niveaux et palettes de pets (M5) | styles de chat, espaces à louer, fournisseurs de commandes et paroles de pets |
| Inventaire, catalogue, boutique, marché | définitions de meubles (répertoire), formats de stuff data, assets | inventaire → objet unique (F2/M2), planificateur d'attribution → marchands (M3), catalogue scindé (M3), Shop → `WebApi.Shop` (F0), marché → hôtel des ventes (après V1), pêche → Pêche (M5) | inventaire de bots, `SongProvider`, navigator (M4) |
| Social, Signals, Progression | messagerie, groupes et forums, badges, permissions, signalements, filtre de mots | Signals (`rpg.*`), succès (M6), niveau de compte (M6), journalières (M3) | guides, revue de chat, quiz, résolutions, objectifs communautaires |
| Web API | connexion, compte, SSO, boutique, articles, profils publics, santé | avatars → personnages avec classe (F2), purse → or et premium (M3), notifications (sources) | `/api/newuser/room/select`, `/api/public/rooms` (M4) |
| Dashboard (45 pages) | 24 pages (exploitation, modération, définitions de meubles, articles, audit, config…) | 14 pages (performances par zone, enquête personnage, anomalies d'objets, rooms, bots → PNJ, pets → compagnons, économie, catalogue, achats, quêtes, succès, récompenses, pêche) | NavigatorConfig (M4), AchievementResolutions, Songs |

## 4. Questions tranchées par le propriétaire

Voir §5 de la réponse du 2026-10-06 et la spec unifiée (registre des décisions).

## 5. Ordre proposé

- **F0 (M0 bis)** : retirer d'abord (74 handlers vides et leurs parsers, messages et composers morts ;
  petits systèmes morts ci-dessus), puis défaire les références périmées et ajouter le mur de couches,
  couper `Vortex.Authentication` en `Vortex.WebApi.Authentication` + `Vortex.Permissions` (avec le filtre
  de mots), déplacer `Vortex.Shop` en `Vortex.WebApi.Shop`, corriger `AGENTS.md` / `CONTEXT.md`.
- **F1** : type de room, décor figé et routage du clic, passages/téléporteurs, entité de zone,
  index spatial, carte de navigation, horloge.
- **F2** : objet unique, compte et personnages (sur les avatars existants), pipeline de contenu.
- **F3 à F5, puis M1 à M6** : selon la spec unifiée ; les retraits coordonnés avec le client
  (navigator, catalogue joueur, commandes de pets…) suivent leur jalon.
