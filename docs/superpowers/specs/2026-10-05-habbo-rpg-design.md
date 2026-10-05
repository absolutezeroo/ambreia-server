# Habbo RPG — design d'ensemble

- **Date :** 2026-10-05
- **Statut :** vision validée section par section ; chaque jalon (M0–M7) aura sa propre spec et son propre plan.
- **Repos :** `vortex-rpg-emulator` (fork de `vortex-cloud`, remote `upstream`) et `vortex-rpg-client`
  (fork de `vortex-modern-client`, à créer).

## 1. Intention

Transformer l'émulateur Vortex et son client en un **RPG en ligne multijoueur dans l'univers Habbo** :
on garde les avatars, les rooms, le furni et le style isométrique ; on retire tout ce qui ne sert pas
au RPG ; on construit le gameplay en code, sans wired.

**Objectif de la V1 : le socle technique.** Tous les systèmes fonctionnent (combat, classes, loot,
quêtes, métiers, artisanat, guildes, économie, PvP, donjons) avec un contenu minimal de démonstration.
Le contenu viendra ensuite.

**Exigence transverse : modularité.** Chaque domaine doit pouvoir être amélioré, remplacé ou retiré
sans toucher aux autres, et des plugins doivent pouvoir se greffer sur chaque étape du jeu.

### Décisions de game design

| Sujet | Décision |
|---|---|
| Combat | Ciblage + compétences : clic sur une cible, compétences avec cooldowns, déplacement sur la grille |
| Monde | Hub social (ville) + zones ouvertes partagées + donjons instanciés |
| Rooms des joueurs | Conservées comme **maison du joueur** (déco, trophées, ateliers) |
| Progression | **Classes fixes** avec arbre de compétences |
| Équipement | Stats portées par l'équipement ; **look Habbo libre** (transmog) ; arme visible en main |
| PvP | **Consenti** uniquement : duels sur demande, arènes |
| Économie | Or gagné en jeu + monnaie premium **uniquement cosmétique** + hôtel des ventes entre joueurs |
| Activités | Quêtes PNJ, métiers de récolte, artisanat, guildes |
| Thème | **Fantasy Habbo** : guerrier, mage, archer, forêts, donjons, dragons en style pixel Habbo |
| Contenu | Édité dans le **dashboard admin**, stocké en base, rechargé à chaud ; placement des monstres et PNJ en jeu |
| Mort | Respawn au point de la zone, 50 % PV, **10 % d'usure** de l'équipement, réparation en or |
| Pets | **Compagnons de combat** et **montures** |

## 2. Périmètre serveur (ménage)

| Module | Décision | Rôle dans le RPG |
|---|---|---|
| Rooms | Garder, alléger | Ville, zones, donjons, maisons |
| Rooms / Wired | **Supprimer** | Le gameplay est codé |
| Rooms / Games : Freeze, BattleBanzai, Football | **Supprimer** | — |
| Rooms / Games : Arena, Teams, Runtime, Scoring | Garder | Arènes et duels PvP |
| Rooms / Bots | Garder, étendre | Monstres et PNJ |
| Rooms / Pets : cœur (placement, mouvement, commandes, animations) | Garder | Base des compagnons |
| Rooms / Pets : Riding | Garder, adapter | Monture hors combat |
| Rooms / Pets : Breeding, Plants, Care | **Supprimer** | — |
| Rooms / Jukebox, MysteryBox, Crackable, Rating, Camera | **Supprimer** | — |
| Rooms / Trading, Doorbell, Moderation, RaidProtection | Garder | Échange, maisons, modération |
| Players | Garder, alléger (retirer Builders Club, `ChainLeaderboardService`, classification) | Compte et personnage |
| Inventory, Furniture | Garder | Sac, déco, loot |
| Progression | Transformer (retirer Polls) | Achievements ; base des quêtes |
| Social | Transformer | Amis ; groupes → guildes |
| Marketplace | Garder | Hôtel des ventes |
| Catalog, Shop | Garder, alléger (retirer LTD, vouchers, offres ciblées, Builders Club) | Boutique premium cosmétique |
| Fishing | Transformer | Fondu dans le métier Pêche |
| Navigator | Simplifier | Maisons, maisons d'amis, QG de guilde |
| Chain, Collectibles, Habbicons, RewardTracks | **Supprimer** | — |
| Crypto | Garder le chiffrement réseau, retirer la partie chain | — |
| Specs, Specs.Cli, `docs/habbo-specs`, `check-wire-conflicts`, `check-header-registry`, `check-variable-capabilities` | **Supprimer** | Ils imposent la conformité au client officiel, dont le RPG diverge volontairement |
| LoadGen, Benchmark | **Supprimer** | Récupérables depuis l'historique git |
| Networking, Pipeline, Runtime, Database, Authentication, Observability, Logging, Plugins, WebApi, Dashboard, Events, Signals, Messages, Protocol, Revisions, PacketHandlers, Primitives | Garder | Infrastructure ; le dashboard perd les pages des features supprimées |

**Base de données :** après le ménage, l'historique des migrations est remplacé par une migration
initiale unique. Aucune donnée de production n'est à conserver.

## 3. Architecture

### 3.1 Principe : le serveur fait autorité

Le client envoie des **intentions** (`TargetSelect`, `SkillCast`, `Gather`, `Craft`…). Le serveur
valide tout (portée, ligne de vue, cooldown, ressources, droits) et diffuse le résultat. Le client
n'est jamais cru sur parole.

### 3.2 Projets

| Projet | Contenu | Dépend de |
|---|---|---|
| `Vortex.Rpg` (noyau) | Pile de stats et `[StatSource]`, formules communes, contrats des events RPG, attributs de registre, définitions de contenu, `RpgContentGrain` | Primitives, Events, Signals, Database |
| `Vortex.Rpg.Characters` | Classes, `PlayerCharacterGrain`, niveaux et XP, équipement, durabilité | Rpg |
| `Vortex.Rpg.Combat` | Compétences, `[SkillEffect]`, menace, ligne de vue, mort, duels | Rpg |
| `Vortex.Rpg.Monsters` | `[MonsterBehavior]`, spawns, laisse, phases de boss, loot | Rpg, Rpg.Combat |
| `Vortex.Rpg.Companions` | Pets de combat (stats, compétences, suivi) et monture | Rpg, Rpg.Combat |
| `Vortex.Rpg.Quests` | `[QuestObjective]`, `PlayerQuestGrain`, PNJ donneurs, dialogues | Rpg, Signals |
| `Vortex.Rpg.Professions` | Récolte, `[ProfessionAction]`, recettes, artisanat (absorbe Fishing) | Rpg |
| `Vortex.Rpg.Economy` | Marchands PNJ, réparation, objets liés, extension de l'hôtel des ventes | Rpg, Marketplace |
| `Vortex.Rpg.Guilds` | Niveau et bonus de guilde, QG | Rpg, Social |

`Vortex.Rooms` référence `Rpg.Combat`, `Rpg.Monsters` et `Rpg.Companions` pour les exécuter dans son
tick. Ces trois projets ne référencent donc pas `Vortex.Rooms` : ils travaillent contre une
abstraction de la room de combat (grille, ligne de vue, combattants présents, diffusion) définie
dans le noyau et implémentée par `Vortex.Rooms`. À l'inverse, `Quests` et `Professions` peuvent
référencer `Vortex.Rooms` (par exemple pour `[RoomObjectLogic("gather_node")]`), car `Rooms` ne les
référence pas. Chaque projet a son projet de tests `<Projet>.Tests`.

**Règle de dépendance :** un domaine RPG ne référence **jamais** un autre domaine RPG, seulement le
noyau. Seules exceptions : `Monsters` et `Companions` → `Combat`. Les domaines communiquent
**uniquement par events et signals**. Critère de réussite : retirer n'importe quel domaine feuille
du `.sln` (Guilds, Economy, Professions, Quests, Companions) laisse le reste compiler et tourner. Un
**test d'architecture** échoue si une référence interdite apparaît.

Un projet correspond à une frontière retirable seule ; en dessous, on découpe en dossiers.

### 3.3 Propriété de l'état (grains)

| État | Propriétaire |
|---|---|
| Classe, niveau, XP, stats de base, équipement, points de compétence | `PlayerCharacterGrain` (un par joueur) |
| Or | `PlayerWalletGrain` (existant), nouvelle monnaie |
| PV, mana, cooldowns, buffs, menace de tous les combattants d'une room | `RoomGrain` via `RoomCombatSystem`, dans le tick de 50 ms (`RoomConfig.RoomTickMs`) |
| Monstres et PNJ (spawn, IA, respawn) | `RoomMonsterSystem`, au-dessus de `RoomBotSystem` |
| Compagnon (stats, niveau) | État du pet existant ; stats calculées par le noyau |
| Quêtes du joueur | `PlayerQuestGrain` |
| Métiers du joueur | `PlayerProfessionGrain` |
| Guilde | Groupe Social existant + extension Guilds |
| Définitions de contenu | `RpgContentGrain`, chargé depuis la base, rechargé à chaud |

**Entrée en room :** `RoomGrain` demande **une seule fois** les stats calculées à
`PlayerCharacterGrain`, puis gère le combat en mémoire. **Sortie :** une seule écriture de retour
(PV restants, XP gagnée, usure). Aucune écriture en base par coup porté.

**Donjons :** `RoomDirectoryGrain` instancie une copie temporaire d'une room modèle pour un groupe ;
l'instance est détruite quand elle est vide.

### 3.4 Protocole

Les packets RPG vont dans la révision embarquée (`Vortex.Revisions/Revision20260701`) sur une **plage
d'ids réservée à partir de 9000**, sans collision possible avec un packet Habbo. Côté client : parsers
et events dans `vortex-engine`. Les handlers restent de l'orchestration ; les envois passent par
`PlayerPresenceGrain.SendComposerAsync`.

## 4. Modularité et extensibilité

Le RPG réutilise les mécanismes existants : enregistrement par attribut (`[RoomGame]`,
`[RoomObjectLogic]`, `[RoomEventListener]`), events annulables (`ICancellableEventPublisher`,
`EventContext.Cancel`), behaviors (`IEventBehavior<T>`), vocabulaire de Signals et plugins
(`IVortexPlugin`, rechargement à chaud).

### Règle 1 — le contenu est de la donnée, le comportement est du code branchable

| Donnée (dashboard) | Code (registre par attribut, extensible par plugin) |
|---|---|
| Compétence : coût, cooldown, portée, cible, liste d'effets paramétrés | `[SkillEffect("damage" \| "dot" \| "heal" \| "buff" \| "debuff" \| "stun" \| "area")]` |
| Monstre : stats, comportement, table de loot, phases | `[MonsterBehavior("passive" \| "aggressive" \| ...)]` |
| Quête : objectifs typés | `[QuestObjective("kill" \| "collect" \| "talk" \| "reach_room" \| "craft" \| "gather")]` |
| Nœud de récolte, recette | `[ProfessionAction(...)]` |
| Source de stats | `[StatSource]` (classe, équipement, buffs, pet, guilde) |

Les clés disponibles alimentent automatiquement les listes déroulantes du dashboard. Une clé
référencée par le contenu mais absente des registres est refusée à la sauvegarde.

### Règle 2 — chaque étape publie un event

Les events « -ing » sont annulables et modifiables, les events « -ed » sont informatifs.

| Avant (modifiable / annulable) | Après (informatif) |
|---|---|
| `SkillCastingEvent` | `SkillCastEvent` |
| `DamageApplyingEvent` (montant modifiable) | `DamageDealtEvent` |
| `HealApplyingEvent` | `HealedEvent` |
| `CombatantDyingEvent` (annulable) | `CombatantKilledEvent` |
| `LootRollingEvent` (table modifiable) | `LootDroppedEvent` |
| `XpGainingEvent` (montant modifiable) | `LevelUpEvent` |
| `QuestCompletingEvent` | `QuestCompletedEvent` |
| `ItemCraftingEvent` | `ItemCraftedEvent` |

### Règle 3 — les stats sont une pile de modificateurs

Stats finales = somme des `[StatSource]` enregistrées. Ajouter une source (bonus de set, titre,
event saisonnier) ne modifie pas le calcul.

### Règle 4 — les quêtes écoutent les Signals

Les events « après » sont traduits en signals (`rpg.monster_killed`, `rpg.item_crafted`,
`rpg.item_gathered`…). Un objectif de quête attend N occurrences d'un signal avec des faits donnés :
tout signal émis par un plugin devient un objectif possible.

### Garde-fou performance

Les events de combat sont publiés dans le tick de la room, dans le grain, sans appel réseau. Leurs
handlers sont synchrones et rapides. Les I/O (base, webhook) se font sur les events « après », hors
du tick.

## 5. Combat

### Stats

| Stat | Effet |
|---|---|
| PV, Mana | Vie et ressource ; la mana se régénère après 5 s hors combat |
| Force, Intelligence, Agilité | Stat principale selon la classe : dégâts physiques, magiques, à distance |
| Armure | `dégâts × 100 / (100 + armure)` |
| Critique | Chance de 150 % de dégâts |
| Vitesse | Cadence d'attaque et vitesse de déplacement |

### Classes de départ

| Classe | Rôle | Portée |
|---|---|---|
| Guerrier | Tank, menace ×3 | 1 case |
| Archer | Dégâts mono-cible | 6 cases |
| Mage | Dégâts de zone, contrôle | 5 cases |
| Clerc | Soins, buffs | 5 cases |

Attaque de base automatique, 6 compétences au niveau 20, barre d'action 1–6. Les classes sont des
données : d'autres s'ajoutent depuis le dashboard.

### Résolution d'une action

1. Client : `TargetSelect(objectId)` puis `SkillCast(skillId)`.
2. Serveur, dans l'ordre : vivant → cible valide → portée (distance de Chebyshev en cases) → ligne
   de vue (aucun mur ni furni bloquant) → cooldown → global cooldown de 1 s → mana.
3. Échec : code d'erreur au client. Cible hors de portée : déplacement automatique à portée, puis
   lancement.
4. Succès : effet appliqué au tick suivant ; diffusion à la room de `CombatEvent` (dégâts, soin,
   critique, esquive), `HealthUpdate` et de l'animation.

### Monstres

- Passif ou agressif, avec un rayon d'aggro en cases.
- Table de menace : dégâts et soins génèrent de la menace ; le monstre attaque le premier de la table.
- Laisse : trop loin de son spawn, il rentre et se soigne entièrement.
- Boss : phases déclenchées par un pourcentage de PV, définies dans le dashboard.

### Mort et loot

- Joueur : fantôme 5 s, respawn au point de la zone avec 50 % des PV, 10 % d'usure. À 0 de
  durabilité, un objet ne donne plus de stats jusqu'à réparation.
- Monstre : XP partagée dans le groupe ; **loot personnel** tiré pour chaque joueur.

### PvP consenti

- Duel : demande, acceptation, combat jusqu'à 1 PV, sans pénalité.
- Arène : matchs par équipes sur Arena/Teams de `Vortex.Rooms/Games`.
- Hors duel et arène, aucun dégât possible entre joueurs.

### Compagnons

Combattant supplémentaire de la room : suit son maître, attaque sa cible, 2 compétences selon
l'espèce, gagne de l'XP avec son maître, « K.O. » jusqu'à la sortie de combat s'il meurt. Monture
uniquement hors combat.

## 6. Activités hors combat

### Quêtes

- PNJ donneur : bot avec le rôle `quest_giver`, fenêtre de dialogue (texte, choix, accepter/refuser).
- Quête = prérequis (niveau, quête précédente, classe) + objectifs + récompenses (XP, or, objets,
  furni, titre). Les quêtes peuvent s'enchaîner.
- Journal limité à 20 quêtes actives (valeur de configuration). Progression persistée par lot.

### Métiers (récolte)

- Nœuds de récolte : furni avec `[RoomObjectLogic("gather_node")]`. Clic, canalisation de 3 s,
  ressource reçue, nœud épuisé puis réapparition.
- Niveau de métier de 1 à 50, indépendant du niveau de combat.
- Départ : Minage, Herboristerie, Bûcheronnage, Pêche (reprend `Vortex.Fishing`).

### Artisanat

- Ateliers : furni (forge, établi, chaudron), en ville ou dans la maison du joueur.
- Recette = ingrédients + atelier + niveau de métier → objet, avec une chance de qualité supérieure.
- Départ : Forge (armes, armures), Alchimie (potions), Menuiserie (furni de maison).

### Guildes

Groupes Social existants (bannière, rangs, forum, chat), plus : niveau de guilde alimenté par un
pourcentage de l'XP des membres (`XpGainingEvent`), bonus par niveau (`[StatSource]`), QG partagé.

### Économie

- Or : monstres, quêtes, ventes aux PNJ.
- Puits d'or : réparations, frais d'hôtel des ventes de 5 %, achats aux PNJ, coût des recettes.
- Hôtel des ventes : `Vortex.Marketplace` étendu aux objets RPG. Échange direct : `RoomTradingSystem`.
- Premium (crédits, diamants) : cosmétique uniquement, sans stats ni XP.
- Loot de boss lié au joueur, non échangeable.

## 7. Client

Les fenêtres restent des layouts XML du système de fenêtres Habbo, édités dans **Glaze**, dans
`vortex-client/src/vortex-layouts/` (`vortex_rpg_*_xml.xml`).

**Organisation :** une feature par domaine dans `vortex-engine/src/habbo/rpg/` (`combat`, `character`,
`quests`, `professions`, `guild`), chacune avec ses messages, son état et ses fenêtres. Les features
communiquent par events client (`CombatTargetChanged`, `PlayerStatsUpdated`,
`QuestProgressUpdated`…), jamais par références directes.

**Dans la room :** barres de vie au-dessus des combattants (visualisation attachée à l'objet), nombres
flottants, cercle de cible et zones d'effet au sol, arme en main, animations d'attaque, de sort et
de mort.

**HUD :** portrait, PV, mana et niveau (et PV du compagnon) ; cible ; barre d'action 1–6 avec
cooldowns et barre d'XP ; groupe (5 portraits) ; suivi des quêtes ; barre de canalisation.

**Fenêtres :** Personnage (C), Sac (I), Compétences (K), Journal (J), Métiers (P), Guilde (G),
dialogue PNJ, marchand, réparation, hôtel des ventes.

**Retiré :** wired, Builders Club, Habbicons, reward track, derby, collectibles, NFT, Freeze, Banzai,
jukebox, mystery box, camera, rating, élevage.

**Accessibilité :** toutes les actions de combat au clavier et à la souris ; les barres affichent une
valeur numérique en plus de la couleur.

## 8. Dashboard

Chaque page respecte la règle « Add dashboard capability or admin page » d'`AGENTS.md` (4 fichiers,
vérifiés par `check-dashboard-capabilities`).

Pages : Classes, Compétences, Monstres, Objets, Tables de loot, Quêtes, Métiers et recettes, Zones,
Donjons. Les champs de comportement proposent les clés des registres. Chaque sauvegarde recharge
`RpgContentGrain` à chaud ; la validation refuse les références cassées.

## 9. Ménage (M0)

Suppression des feuilles vers le cœur. Chaque étape = un commit, avec `VortexCloudFastCheck` et tous
les tests verts avant l'étape suivante :

1. Outillage Habbo : Specs, Specs.Cli, `docs/habbo-specs`, `check-wire-conflicts`,
   `check-header-registry` ; mise à jour d'`AGENTS.md`, `CLAUDE.md`, `.claude/settings.json`.
2. Chain, Collectibles, Habbicons, RewardTracks, LoadGen, Benchmark.
3. Wired et `check-variable-capabilities`.
4. Freeze, BattleBanzai, Football, Jukebox, MysteryBox, Crackable, Rating, Camera.
5. Pets : Breeding, Plants, Care.
6. Players, Catalog, Navigator : Builders Club, LTD, vouchers, offres ciblées, Polls.
7. Pages correspondantes du dashboard.
8. Fenêtres et features correspondantes du client.
9. Migration initiale unique ; nettoyage de `scripts/sql/`.

Tant que le RPG n'a pas ses propres tests, les tests existants des modules conservés servent de
filet de sécurité.

## 10. Tests

- `Vortex.Rpg.*.Tests` : règles pures (dégâts, armure, critique, XP, usure, menace, loot).
- Combat de bout en bout dans `Vortex.Rooms.Tests`, sur le modèle de
  `WiredProjectileEndToEndTests` : cast, dégâts, menace, mort, loot, respawn.
- Extensibilité : un faux `[SkillEffect]` et un handler sur `DamageApplyingEvent` se branchent sans
  modifier le cœur.
- Architecture : aucun domaine RPG ne référence un autre domaine hors exceptions autorisées.
- Cohérence : toute clé référencée par le contenu de démonstration existe dans un registre.

## 11. Jalons

| # | Jalon | Livrable |
|---|---|---|
| M0 | Fork et ménage | Deux repos allégés, build et tests verts |
| M1 | Socle combat | Noyau, Characters, Combat, Monsters ; HUD de base ; mort et loot |
| M2 | Compagnons | Pets de combat, monture |
| M3 | Quêtes | PNJ, dialogues, objectifs par signals, journal |
| M4 | Métiers et artisanat | Récolte, recettes, ateliers |
| M5 | Économie et guildes | Marchands, réparation, hôtel des ventes RPG, guildes |
| M6 | PvP | Duels, arènes |
| M7 | Donjons et boss | Instances, phases de boss |

Chaque jalon reçoit sa propre spec détaillée et son propre plan d'implémentation.

## 12. Hors périmètre de la V1

PvP ouvert, élevage et capture de créatures, pets de maison, contenu au-delà de la démonstration,
monétisation au-delà de la boutique cosmétique existante.
