# Éditeur de cartes — spec 1 : format de zone et serveur

> **Statut :** à relire par le propriétaire · **Date :** 2026-10-07 · **Langue :** français
> **Dépôt :** `vortex-rpg-emulator`. Spec fille « Monde et éditeur de cartes » de la spec unifiée
> `docs/superpowers/specs/2026-10-06-vortex-rpg-design.md` (§3.2, §3.2 bis, §3.2 quater, §3.2 quinquies,
> §3.2 sexies). Côté client : `vortex-rpg-client/docs/superpowers/specs/2026-10-07-client-architecture-design.md`
> (§8, World editor débloqué à J4).

---

## 1. Intention

Construire les zones du RPG avec de vrais meubles Habbo, dans un logiciel de bureau, et les rendre
jouables dès l'enregistrement. Le propriétaire a fourni une maquette et 35 panneaux (sol, murs,
entrées, mobis, sélection multiple, PNJ, monstres, spawns, zones, navigation, collisions, quêtes,
triggers, téléporteurs, transitions, objets interactifs, ressources, éclairage, VFX, audio, événements,
boss, loot, caméra, calques, inspecteur, mini-carte, recherche, validation, mode test, debug AOI,
debug serveur, historique, réglages, export). **Toute cette liste est la cible.**

**Ce que résout le découpage.** L'application a besoin de `@vortex/iso`, extrait du client au jalon
J4 de sa refonte : la construire avant serait la réécrire. Tout ce qui ne dépend pas du rendu avance
tout de suite.

| Spec | Contenu | Où | Quand |
|---|---|---|---|
| **1 (celle-ci)** | Document de zone (tous les calques), validation, stockage versionné, publication, verrou, chargement d'une zone par le serveur, rechargement à chaud, palette de meubles, ticket de test | ce dépôt | maintenant |
| 2 | Modèle de l'éditeur en TypeScript pur : document, commandes undo/redo, sélection, calques, prefabs, recherche, validation locale | `vortex-rpg-client/packages/vortex-map-editor-core` (aucun import du moteur) | maintenant, après la spec 1 |
| 3 | Application : coquille Tauri, viewport `@vortex/iso`, panneaux en fenêtres Habbo (`@vortex/ui`), mode test | `vortex-rpg-client/packages/vortex-map-editor` | après J4 |

**Critère de réussite de la spec 1.** Un document de zone envoyé par HTTP est validé, stocké, publié ;
un joueur qui entre dans la zone (driver `play`) voit le sol et le décor du document ; une deuxième
publication recharge la zone, occupée ou non ; un retour à la version précédente fait de même.

---

## 2. Décisions — APPROUVÉ (conversation du 2026-10-07)

1. **Logiciel de bureau**, pas une page web : coquille **Tauri** qui réutilise le vrai rendu du client
   (`@vortex/iso`, `@vortex/avatar`), avec une interface en **fenêtres Habbo** (`@vortex/ui`, comme
   Glaze). Avalonia écarté : il faudrait réécrire le rendu isométrique et l'éditeur ne montrerait
   plus exactement le jeu. (Détails : spec 3.)
2. **Une zone = un document JSON versionné en base**, seule source de vérité de la zone (§3).
3. **La carte place des références, elle ne définit pas le contenu** : un spawn pointe vers une
   définition de monstre, un PNJ vers un modèle, un coffre vers une table de loot. Les définitions
   vivent dans la base de contenu des systèmes RPG.
4. **Tous les calques existent dès la version 1 du schéma**, même ceux dont le système de jeu n'existe
   pas encore : une carte construite aujourd'hui reste valide demain.
5. **Conditions et actions structurées**, jamais du texte libre ni du code : un seul vocabulaire typé
   partagé par triggers, PNJ, spawns, zones et passages (§4.3).
6. **Pas de Wired ni de « scripts »** : le Wired a été coupé du serveur ; les triggers le remplacent.
7. **Monstres placés à l'unité = spawn de quantité 1 sans respawn**, pas un calque séparé.
8. **Debug AOI et debug serveur** lisent le serveur en direct : ce sont des vues du mode test (spec 3),
   pas des données de carte.

---

## 3. Une zone sur le serveur

### 3.1 Principe

Une zone reste une **room** : `RoomGrain`, chemin d'entrée (`RoomService`), pathfinding, tick, flux de
la room — rien de cela ne change. Ce qui change, c'est **d'où la room tire sa carte et ses meubles**.

| | Room classique (maisons, halls de guilde futurs) | Zone publiée |
|---|---|---|
| Carte | `rooms.model_id` → `room_models` | compilée depuis `floor` par `IRoomModelProvider.CompileCustomModel` (même décodage, mêmes trous) |
| Meubles | lignes `furniture` (propriétaire, `room_id`), empilement Habbo | **décor** construit en mémoire depuis `decor`, sans propriétaire ni ligne `furniture` |
| Persistance | la room réécrit ses meubles | rien : le décor ne change qu'en publiant |
| Interaction joueur | pose, déplacement, ramassage selon les droits | aucune sur le décor (spec unifiée §3.2 bis) ; les objets interactifs répondent par leur action RPG (plus tard) |

### 3.2 Nouveau projet `Vortex.World` (couche Platform)

Ajouté à `PLATFORM` dans `scripts/hooks/check-architecture-walls.mjs`. Il contient :

- `ZoneStore` : lecture et écriture des tables `zones` et `zone_versions` (seul écrivain de ces tables).
- `ZoneValidator` : toutes les règles du §5.
- `ZonePublisher` : brouillon, publication, retour arrière, verrou (§6).
- `ZoneRoomSource` : implémente, pour une room qui est une zone, la fourniture de carte et de décor.

Les **contrats** partagés vivent dans `Vortex.Primitives/World/` : les records du document (§4),
`IZoneRoomSource` (lu par `Vortex.Rooms`), les erreurs de validation. `Vortex.Rooms` ne référence pas
`Vortex.World` : au chargement, `RoomGrain` demande à `IZoneRoomSource` si sa room est une zone ; si
oui, il prend la carte et le décor qu'il fournit au lieu de `IRoomModelProvider`/`IRoomItemsProvider`.

### 3.3 Tables

`zones`

| Colonne | Sens |
|---|---|
| `id` | identifiant de zone (clé stable : `village_ambre`) |
| `room_id` | la room qui la fait vivre (créée avec la zone, propriétaire `__world__`) |
| `published_version` | version servie aux joueurs (nullable : jamais publiée) |
| `locked_by_account_id`, `locked_at` | verrou d'édition (§6.3) |

`zone_versions`

| Colonne | Sens |
|---|---|
| `zone_id`, `version` | clé ; `version` croît de 1 à chaque enregistrement |
| `schema_version` | version du schéma du document |
| `document` | JSON complet (`longtext`) |
| `author_account_id`, `created_at` | qui, quand |
| `status` | `Draft` ou `Published` (une version publiée l'a été au moins une fois) |
| `comment` | message libre de l'auteur (comme un commit) |

Ajout par régénération de `InitialCreate` (skill `changing-the-schema`).

### 3.4 Identifiants du décor

Chaque objet du décor reçoit à l'activation de la room un `RoomObjectId` dans l'ordre du document.
Les ids des lignes `furniture` d'une zone n'existent pas (une zone n'en a pas), il n'y a donc pas de
collision. L'id **stable** d'un objet, celui qu'utilisent la recherche, les triggers et l'historique,
est son `id` dans le document (§4.1), pas le `RoomObjectId`.

---

## 4. Le document de zone

### 4.1 Règles communes

- Racine : `{ "schemaVersion": 1, "settings": …, "floor": …, …calques }`.
- **Chaque élément a un `id`** unique dans le document, chaîne courte et lisible
  (`spawn_loups_nord`, `npc_intendant`). Les références internes (un trigger qui vise une zone, une
  case affectée à une zone) passent par cet id.
- Coordonnées en cases ; hauteur en décimal (centièmes), comme le reste du moteur.
- Un **meuble est désigné par son nom de classe Habbo** (`furniture_definitions.name`, ex.
  `g_hween_c21_sofa`), stable d'un import de données de référence à l'autre, contrairement à l'id
  numérique de la définition. *À vérifier pendant le plan : que `name` porte bien le nom de classe
  sur toutes les lignes importées.*

### 4.2 Calques

| Calque | Contenu | Validé dès la spec 1 |
|---|---|---|
| `settings` | id, nom affiché, région, type (`World`, `Dungeon`, `Lobby`, `Housing`, `Guild`, `Arena`), capacité, niveau conseillé, PvP autorisé, musique par défaut, météo, réglages AOI | types, bornes, id = `zones.id` |
| `floor` | largeur, longueur (≤ 64 × 64), cases : hauteur, marchable, trou, `zone` (id), note ; case et direction de la porte | taille ; porte sur une case existante ; zone référencée existe |
| `walls` | murs actifs, hauteur, épaisseur, visibilité (réglages globaux de la room) | bornes |
| `decor` | objets de sol : `id`, classe, x, y, z, rotation, état, surcharges de collision (§4.4), interaction, tag, verrouillé ; objets muraux : `id`, classe, mur gauche/droit, position murale, état | **la classe existe** et est du bon type (sol/mur) ; rotation permise pour ce meuble ; emprise dans la carte ; état < nombre d'états |
| `entries` | points d'entrée nommés (`arrival`, `respawn`, `dungeon_return`…), case, direction | case marchable ; au moins un point `arrival` |
| `passages` | téléporteurs et transitions : volume (cases ou polygone), destination (zone + entrée), fondu, conditions, coût, cooldown | géométrie ; destination existante (§5.2) |
| `zones` | polygones logiques : `kind` (`Safe`, `PvP`, `Quest`, `Boss`, `Town`, `Custom`), nom affiché, priorité, musique, ambiance, niveau conseillé, tags, conditions | polygone fermé sur la grille ; ids uniques |
| `npcs` | modèle, surcharges (nom, look), case, rotation, interactions (parler, boutique, quête, banque, téléportation), conditions d'apparition, animation, icône | case marchable ; modèle : **référence différée** (§5.3) |
| `spawns` | aire (polygone), définition de monstre, quantité min/max, respawn, errance, aggro, niveaux, activation AOI, groupe, conditions | géométrie ; aire contient une case marchable ; définition : référence différée |
| `resources` | nœud (meuble du décor ou position), modèle de ressource, métier et niveau requis, respawn | géométrie ; références différées |
| `chests` | objet du décor, table de loot, respawn, usage unique ou par joueur, conditions | l'objet du décor existe ; références différées |
| `interactives` | objet du décor + `InteractionDefinition` (levier, porte, autel…) | idem |
| `bosses` | aire d'arène, modèle d'encounter, point de spawn, points d'adds, portes fermées pendant le combat | géométrie ; références différées |
| `events` | aire, durée, monstres, objectifs, cooldown, annonce | géométrie ; références différées |
| `triggers` | type (`EnterZone`, `LeaveZone`, `Interact`, `MonsterKilled`, `Timer`, `QuestState`, `PlayerLevel`, `ItemOwned`), cible, conditions[], actions[], cooldown, actif, visible en debug | cible interne existante ; conditions et actions bien formées (§4.3) |
| `ambience` | éclairage global (teinte, luminosité), sources lumineuses, sources sonores (rayon, volume, boucle), VFX (position, échelle, rayon), cycle jour/nuit | bornes |
| `prefabs` | prefabs utilisés (id, version), pour la traçabilité | — |

### 4.3 Conditions et actions

Une condition ou une action est `{ "kind": "...", …paramètres }`. Le serveur connaît la liste des
`kind` et la forme de leurs paramètres ; un `kind` inconnu est une **erreur**. Vocabulaire de la
version 1 :

- **Conditions :** `PlayerLevel {min, max}`, `QuestState {quest, state}`, `TimeOfDay {from, to}`,
  `HasItem {item, count}`, `Faction {faction}`, `ZoneActive {zone}`.
- **Actions :** `StartQuest {quest}`, `ShowDialogue {dialogue}`, `ActivateZone {zone}`,
  `DeactivateZone {zone}`, `Teleport {zone, entry}`, `SpawnGroup {spawn}`, `PlaySound {sound}`,
  `OpenDoor {decor}`.

Les références (`quest`, `dialogue`, `item`…) suivent la règle des références différées (§5.3). Un
système RPG qui arrive ajoute ses `kind` au vocabulaire ; il ne change jamais le sens d'un `kind`
existant.

### 4.4 Collisions par objet

Chaque définition de meuble a des propriétés de collision par défaut, tirées de ses colonnes actuelles
(`can_walk`, `can_sit`, `can_lay`, `stack_height`, taille). L'objet du décor peut les surcharger :
`walkable`, `blocksSight`, `height` (surface marchable), `seat`. Ce sont elles, et non les règles
d'empilement Habbo, qui font la carte de navigation (spec unifiée §3.2 quinquies). **Dans cette spec**,
la zone publiée calcule ses hauteurs et sa marchabilité avec le moteur de cases actuel, nourri par ces
propriétés ; la carte de navigation dédiée (ligne de vue, portées) vient avec le runtime de zone (F1).

### 4.5 Schéma unique

Les records C# de `Vortex.Primitives/World/` sont la source. Un test exporte leur JSON Schema
(`System.Text.Json.Schema.JsonSchemaExporter`, inclus dans .NET, aucune dépendance ajoutée) vers
`schemas/zone.schema.json` et échoue si le fichier commité diffère (même mécanisme que
`Vortex.WebApi/openapi.json`, `UPDATE_ZONE_SCHEMA=1` pour le réécrire). L'éditeur génère ses types
TypeScript depuis ce fichier (spec 2). Le serveur et l'éditeur ne peuvent pas diverger sans qu'un
test le dise.

---

## 5. Validation

### 5.1 Erreurs et avertissements

Le validateur rend une liste de constats `{ severity, code, elementId, message }`. `elementId` permet à
l'éditeur de centrer la caméra sur l'élément fautif. Une **erreur** empêche l'enregistrement ; un
**avertissement** ne l'empêche pas.

### 5.2 Règles de la version 1

Erreurs :
- schéma : champ manquant, type faux, `kind` inconnu, borne dépassée, id dupliqué ;
- géométrie : carte > 64 × 64, élément hors carte, polygone non fermé, emprise de meuble hors carte ;
- référence interne cassée (trigger, case, coffre qui vise un id absent) ;
- meuble inconnu ou de mauvais type, rotation ou état impossible pour ce meuble ;
- aucun point d'entrée `arrival`, ou point d'entrée sur une case non marchable ;
- passage vers une zone qui n'existe pas.

Avertissements :
- **accessibilité** : un spawn, un PNJ, une ressource ou un passage qu'on ne peut pas atteindre à pied
  depuis l'entrée `arrival` (calcul avec le pathfinding du serveur sur la carte de la zone) ;
- passage vers une entrée qui n'existe pas encore dans la zone de destination ;
- **budget** : plus de N objets de décor ou d'entités (N réglable, valeur initiale mesurée sur le
  client pendant le plan) ;
- référence différée non résolue (§5.3).

### 5.3 Références différées

Tant que le système qui possède une référence n'existe pas (modèles de PNJ, monstres, loot, quêtes,
dialogues, objets, ressources, encounters), le serveur accepte n'importe quel id et rend un
**avertissement** `UnresolvedReference`. Chaque système, en arrivant, enregistre un résolveur pour
ses références ; elles deviennent alors des **erreurs** quand elles ne résolvent pas. Une carte
construite avant ce système n'est pas réécrite : elle est revalidée, et l'éditeur liste ce qu'il
faut corriger.

---

## 6. Publication, versions, verrou

### 6.1 Routes (web API, sous `/api/editor/`)

Authentifiées par la session web ; droits au §6.4.

| Route | Effet |
|---|---|
| `GET /api/editor/zones` | liste : id, nom, version publiée, dernière version, verrou |
| `POST /api/editor/zones` | crée une zone vide (et sa room), à partir d'un gabarit facultatif (§6.5) |
| `GET /api/editor/zones/{id}/versions/{n}` | un document (n ou `published` ou `latest`) |
| `GET /api/editor/zones/{id}/versions` | historique : version, auteur, date, statut, commentaire |
| `POST /api/editor/zones/{id}/validate` | valide un document sans l'écrire, rend les constats |
| `POST /api/editor/zones/{id}/versions` | enregistre un brouillon (validation d'abord) ; en développement, publie aussi (§6.2) |
| `POST /api/editor/zones/{id}/publish/{n}` | publie la version `n` (aussi le retour arrière) |
| `POST /api/editor/zones/{id}/lock`, `DELETE …/lock` | prend, rend ou force le verrou (§6.3) |
| `GET /api/editor/furniture` | palette : classe, nom, catégorie, type sol/mur, taille, rotations, états, collisions par défaut |
| `POST /api/editor/test-ticket` | ticket SSO de test qui mène le joueur dans une zone donnée (§6.6) |

### 6.2 Brouillon et publication

- Enregistrer valide puis écrit une nouvelle ligne `zone_versions` (statut `Draft`).
- Publier vérifie que la version passe encore la validation (les autres zones ont pu changer),
  déplace `zones.published_version` et marque la version `Published`, **dans une transaction**,
  puis prévient la room (§6.7).
- Réglage `Vortex:World:PublishOnSave` : `true` en développement (enregistrer = jouable tout de
  suite), `false` sur un serveur public (brouillon et publication séparés). Spec unifiée §3.2 sexies.
- Retour arrière = publier une version antérieure ; rien n'est jamais supprimé.

### 6.3 Verrou d'édition

Un constructeur prend le verrou en ouvrant une zone en écriture. Les autres peuvent l'ouvrir en
lecture. Enregistrer exige le verrou. Le verrou expire après une durée réglable sans enregistrement ni
renouvellement (valeur initiale : 30 minutes). Forcer le verrou est permis à qui a le droit de publier
et laisse une trace dans l'audit.

### 6.4 Droits

Deux capabilities, ajoutées à `Capabilities` et au rôle `owner` :
- `world.zone.edit` : lire, valider, enregistrer un brouillon, prendre le verrou ;
- `world.zone.publish` : publier, revenir en arrière, forcer un verrou.

### 6.5 Gabarits

Un gabarit est une zone ordinaire marquée `settings.template = true`. Créer une zone depuis un gabarit
copie son document publié. Aucun système à part.

### 6.6 Ticket de test

Le mode test de l'éditeur (spec 3) obtient un ticket SSO pour un avatar de l'auteur, portant une room
de destination (colonne nullable `security_tickets.target_room_id`, ajoutée avec les tables du §3.3).
Le handler SSO envoie alors `RoomForward` vers cette room au lieu de `world.start_room_id`. Le ticket
a la même durée de vie que les autres.

### 6.7 Rechargement à chaud

Après une publication, `ZonePublisher` appelle `RoomGrain.ReloadZoneAsync(version)` :
- **room inactive** : rien ; la prochaine activation charge la version publiée ;
- **room active, vide** : elle reconstruit sa carte et son décor ;
- **room active, occupée** : elle reconstruit, replace chaque avatar sur sa case si elle est encore
  marchable, sinon sur la case marchable la plus proche, sinon sur l'entrée `arrival`, puis envoie à
  chaque occupant un `RoomForward` vers la même room. Le client recharge la room, ce qui donne le
  fondu prévu par la spec unifiée, sans message nouveau.

---

## 7. Hors périmètre de cette spec

- L'application elle-même, ses panneaux, le rendu, les miniatures de meubles (le client les a déjà) :
  spec 3.
- Le modèle d'édition (undo/redo, sélection, prefabs, pinceau de dispersion) : spec 2.
- La carte de navigation dédiée (ligne de vue, portées), les collisions par couche (projectile…) :
  runtime de zone (F1).
- Le comportement en jeu des calques RPG (PNJ, spawns, triggers…) : chaque système RPG lit son calque
  en arrivant.
- La vue Monde (graphe des zones et passages) : ses données sont dans les documents (`passages`) ;
  sa vue est dans la spec 3. La route de liste (§6.1) suffit à la construire.

---

## 8. Tests

| Sujet | Preuve |
|---|---|
| Schéma | le JSON Schema exporté égale le fichier commité ; un document de chaque calque fait l'aller-retour JSON sans perte |
| Validation | un test par règle du §5.2 (document valide minimal + une seule faute) ; accessibilité sur une carte avec un mur qui isole un spawn |
| Stockage | SQLite (`SqliteTestDb`) : enregistrer, publier, retour arrière, transaction annulée si l'écriture échoue |
| Verrou | prise, refus pour un autre compte, expiration, forçage avec audit |
| Chargement | `RoomHarness` : une room-zone prend sa carte et son décor du document ; aucune ligne `furniture` lue |
| Rechargement | room occupée : avatar replacé sur la case la plus proche quand la sienne devient bloquante ; `RoomForward` envoyé |
| Routes | `WebApiTestFactory` : droits refusés sans capability, 400 avec constats sur un document invalide |
| En vrai | `driver.mjs play` dans une zone publiée par la route, puis republiée |

---

## 9. Questions ouvertes

1. Nom de classe des meubles : confirmer que `furniture_definitions.name` est bien le nom de classe
   Habbo pour toutes les lignes (sinon, ajouter la colonne au prochain import).
2. Valeur initiale du budget d'objets par zone : à mesurer sur le client avec une zone chargée.
3. La zone de développement (`scripts/sql/dev_zone.sql`) devient-elle la première zone publiée par la
   route, ou reste-t-elle une room classique ? Recommandation : la recréer en zone, pour que le
   chemin principal soit exercé chaque jour.
