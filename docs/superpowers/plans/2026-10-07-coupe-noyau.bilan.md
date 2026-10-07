# Bilan — coupe au noyau (F0 ter)

Plan : `docs/superpowers/plans/2026-10-07-coupe-noyau.md`. Liste validée : `docs/superpowers/audits/2026-10-07-coupe-noyau.md`.
Branche `noyau`, base `371cd7bf` (tag `pre-noyau`, fin du M0 bis), 100 % local, rien de poussé.

Décision de départ (propriétaire, 2026-10-07) : ne garder que le réseau, la connexion classique, la
gestion des rooms comme zones (heightmap, pathfinding, avatars, meubles) et une web API minimale ;
retirer le chat, l'économie, le social, le navigateur, la progression, la modération et le dashboard ;
faire F4 (l'éditeur de cartes) tôt.

## Avant / après

| | Avant (`371cd7bf`) | Après |
|---|---|---|
| Projets `.csproj` suivis | 49 | 35 |
| Fichiers `.cs` suivis | 3 712 | 852 |
| Lignes `.cs` (hors `Vortex.Database/Migrations`) | 248 185 | 60 533 |
| Attributs `[Fact]`/`[Theory]` | 1 788 | 520 |
| Tests exécutés par le QualityGate | 2 604 | **662, 0 échec** |
| Tables (InitialCreate) | 105 | 17 |
| Headers entrants / sortants (`Headers.cs`) | 305 / 280 | 14 / 47 |
| Fichiers parseurs / sérialiseurs | 292 / 297 | 14 / 54 (dont les sérialiseurs de données partagés) |

| Projet de tests | Tests exécutés |
|---|---|
| Vortex.Rooms.Tests | 286 |
| Vortex.Hosting.Tests | 151 |
| Vortex.WebApi.Tests | 47 |
| Vortex.Players.Tests | 43 |
| Vortex.Plugins.Tests | 37 |
| Vortex.Crypto.Tests | 34 |
| Vortex.WebApi.Authentication.Tests | 19 |
| Vortex.Revisions.Tests | 16 |
| Vortex.Pipeline.Tests | 13 |
| Vortex.Database.Tests | 11 |
| Vortex.Permissions.Tests | 3 |
| Vortex.PacketHandlers.Tests | 2 |

Projets retirés : Dashboard.API/Web/Tests, WebApi.Shop(.Tests), Catalog, Inventory, Marketplace, Social,
Navigator(.Tests), Progression, Signals(.Tests), Fishing.

## Commits

| Tâche | Commit | Contenu |
|---|---|---|
| 0 | `163804d1` | liste de la coupe et plan |
| 1 | `b927db73` | le dashboard part |
| 2 | `9c0bacc5` | site réduit à l'inscription, la connexion et le ticket |
| 3 | `5acf8c32` | l'économie part |
| 4 | `22b20ffa` | social et navigation partent |
| 5 | `40f67740` | progression, signaux et pêche ; le joueur réduit à sa fiche ; SSO du noyau |
| 6 | `72aecdde` | la room garde la carte, le pathfinding, les avatars et les meubles |
| 7 | `af52d38d` | joueur `__world__`, zone de développement, `world.start_room_id` |
| 8 | `e1132b48` | protocole, contrats et tables orphelins |
| 9 | `309f3035` | règles et outillage |
| 10 | (ce commit) | migration régénérée, colonnes mortes, tests alignés, validation réelle, bilan |

## Validation réelle (Tâche 10)

Base neuve `vortex_noyau_check` : `scripts/ef.ps1 database update`, puis
`scripts/import-reference-data.ps1 -Source turbo -Target vortex_noyau_check` (63 modèles, 55 282
définitions de meubles ; zone de développement id 1 sur `model_a`, propriétaire `__world__`,
`world.start_room_id = 1`). Émulateur lancé par le driver avec la connexion et le plugin WebApi publié
passés par variables d'environnement (les user-secrets n'ont pas été modifiés), puis arrêté.

```
GET :8080/health -> 200 {"status":"Healthy","database":"up","degradedServices":[]}
game socket OK: sent 2022 -> got 3309
Player New user … (id 2) holds an SSO ticket.
SSO -> 230, 3599, 3339
room 1 -> 611, 2349, 2792, 1956×4, 2260, 2885, 2986, 996, 2613, 2104, 3379, 2914, 2942, 934, 2502
MoveAvatar (4,5) -> UserUpdate. Zone OK.
Smoke OK.
```

Journal de l'émulateur : aucune erreur ; un seul avertissement, bénin (un item de stream reçu par la
présence après la fermeture de la session de test).

Rejouable : `node .claude/skills/run-vortex-emulator/driver.mjs smoke`.

## Écarts au plan

- **SSO avancé en T5** (prévu en T7) : T5 ne compilait pas sans. Pas de vérification d'existence de la
  room avant `RoomForward` : un handler ne lit pas la base, et une room absente échoue à l'entrée.
- **`dev_zone.sql` dans `scripts/sql/`**, pas dans `Vortex.Database/Seeds/` : un seed embarqué doit être
  appliqué par `InitialCreate` (`SeedCatalogTests`), et la zone a besoin des modèles importés.
- **`ConfigKeyCatalog` supprimé en T8** (il ne servait que le dashboard), avec la clé
  `world.start_room_id` ajoutée en T7 ; la clé reste lue par le SSO et écrite par `add_server_config.sql`
  et `dev_zone.sql`.
- **Le balayage de T8 a dépassé les types** : restes de systèmes retirés démontés à la main (journal
  commerce, grand livre, purge, capture d'audit, guildes dans les rooms, persistance des animaux, droits,
  création de room, actions d'avatar sans handler), et en T10 des colonnes mortes de `players`
  (`last_login_at`, `favourite_group_id`, `trading_locked_until`, `muted_until`, `nux_completed_at`,
  `profile_visible`) et de `player_accounts` (`totp_secret`, `safety_locked`), migration régénérée deux fois.
- **Skill `run-vortex-emulator` réécrit en T9** au lieu d'être seulement allégé : le ping de latence
  (544) n'existait plus ; `smoke` est devenu la preuve réelle de T10.

## Pannes à l'exécution trouvées en route (la compilation ne les voyait pas)

- `MoveAvatarMessageHandler` appelait le grain de pêche (supprimé en T5) à chaque pas.
- La WebApi exigeait `IWordFilter`, parti avec Permissions en T6 : 500 ou plantage au démarrage.
- Le test des hooks référençait `Vortex.Marketplace` (supprimé en T3) : la QualityGate serait tombée.

## Champs réseau figés pendant la coupe

| Message | Champ | Valeur figée |
|---|---|---|
| `AuthenticationOK` | actions de connexion suggérées | `[]` |
| `UserRights` | niveau d'abonnement | `None` |
| `UserObject` | compte sous verrou de sécurité | `false` |
| `Users` / avatar joueur | groupe favori (id, statut, nom) | `-1`, `-1`, `""` |
| `RoomSnapshot` (entrée en room) | catégorie, groupe | `-1`, `null` |

`PlayerSummarySnapshot` retire ses `[Id(7)]` à `[Id(10)]` sans renuméroter.

## Ce que la coupe a coûté (à savoir)

- **Plus aucun ban n'est appliqué** au login : les sanctions sont parties en T6. Le jour où elles
  reviennent, le contrôle se met dans `IAuthenticationService.GetPlayerIdFromTicketAsync`, pour que
  toute connexion y passe.
- **Une porte verrouillée refuse** (plus de sonnette) ; les animaux ne bloquent plus le pathfinding.
- **Logiques gardées mais plus réglables en jeu** : hauteur d'empilement, couleur de fond, variateur —
  aucun handler ne les modifie ; elles affichent l'état en base.
- **Couverture perdue** : une trentaine de tests de protocole passaient par des messages retirés, dont
  quelques tests du cadre lui-même (lectures tronquées, champs non lus). Le cadre est inchangé, mais ces
  cas ne sont plus exercés.
- **Restes cosmétiques** : le rôle par défaut `economy` et les capacités `economy.*` / `moderation.*`
  existent encore (seule la sécurité de `UserRights` lit les secondes) ; le message de démarrage des
  sauvegardes cite le dashboard.

## Checklist du propriétaire — côté client

1. **Couper les fonctions de l'hôtel** dans `vortex-rpg-client` : navigateur, catalogue, inventaire,
   messagerie, chat, club, quêtes, marketplace, modération, animaux, bots, échanges. Le serveur ignore
   leurs requêtes (avertissement au journal, pas de déconnexion), mais leurs fenêtres restent vides.
2. **Garder** la connexion (ticket SSO), le moteur de room, l'entrée en zone sur `RoomForward`, la marche
   et le regard. Le contrat réseau gardé est entièrement dans
   `Vortex.Revisions/Revision20260701/Headers.cs` (14 entrants, 47 sortants).
3. **Vérifier le parcours** inscription → avatar → ticket → client → zone de développement, dans le vrai
   client cette fois (le driver le fait en frames brutes).
4. **Réaligner `appsettings.Development.json`** (local, ignoré par git) : il référence encore le plugin
   `Vortex.Dashboard.API` dans `DevPluginPaths`.
5. **Ensuite** : F1, puis F4 (éditeur de cartes) tôt, comme décidé.

## Suite prévue

- Passe dédiée de **guidage de l'IA** (demande du propriétaire) : recherche approfondie des bonnes
  pratiques, puis CLAUDE.md, AGENTS.md, CONTEXT.md, skills et agents réécrits sur le code définitif, et
  les walkthroughs marqués « exemples à réécrire ».
