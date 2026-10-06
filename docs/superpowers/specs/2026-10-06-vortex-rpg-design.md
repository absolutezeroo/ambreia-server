# Vortex RPG — spec de conception unifiée

> **Statut :** à valider par le propriétaire · **Date :** 2026-10-06 · **Langue :** français
> **Remplace :** `2026-10-05-habbo-rpg-design.md` (écrite dans le projet) et
> `2026-10-06-vortex-mmorpg-master-design.md` (écrite hors projet). Les deux restent dans le dépôt
> comme sources ; en cas de conflit, **ce document prévaut**. La spec maître reste une réserve de
> listes de candidats (stats dérivées, catégories, mécaniques optionnelles) que les specs filles
> peuvent consulter.
> **Repos :** `vortex-rpg-emulator` (serveur) et `vortex-rpg-client` (client).

---

## 0. Lire cette spec

### 0.1 Langage normatif

- **DOIT / NE DOIT PAS** : requis pour la correction.
- **DEVRAIT / NE DEVRAIT PAS** : recommandé, sauf raison documentée.
- **PEUT** : optionnel.

### 0.2 États de décision

- **APPROUVÉ** : décidé, ne change que par une révision de cette spec.
- **PROPOSÉ** : recommandé, modifiable par la spec fille du système.
- **OUVERT** : à trancher ; la ligne nomme la spec fille qui le tranche.

### 0.3 Règle d'ancrage

Toute affirmation sur l'existant cite le fichier qui la prouve (chemins relatifs au repo concerné).
Ce que le code ne sait pas encore faire est écrit comme un **chantier**, jamais comme un acquis.

### 0.4 Specs filles

Ce document fixe les frontières, les règles transverses et la V1. Chaque système reçoit une spec
fille (§23) avant son plan d'implémentation. Une spec fille est « complète » quand elle contient :
but, parcours joueur, périmètre et hors-périmètre, modèle de domaine, propriétaire de chaque état,
persistance, commandes serveur (messages entrants), events émis, états de l'interface, permissions,
validations, concurrence, cas d'échec, reconnexion, observabilité, anti-triche, tests, critères
d'acceptation, décisions OUVERTES tranchées. Un plan DOIT se conformer à sa spec fille au lieu de la
modifier en silence.

---

## 1. Intention

### 1.1 Concept — APPROUVÉ

Un MMORPG social isométrique construit sur l'émulateur et le client Habbo du fork. Ce n'est pas un
hôtel Habbo avec des commandes RPG : on garde le rendu isométrique, les avatars, la présence sociale,
la construction de maps avec de vrais meubles, et on y ajoute personnages persistants, combat,
monstres, classes, équipement, loot, quêtes, métiers, groupes, guildes, donjons et économie.

### 1.2 Piliers — APPROUVÉ

1. **Social d'abord.** Ville, marché, QG de guilde, zones de métier : le joueur y croise les autres.
2. **Monde cohérent.** Les zones s'enchaînent sans rupture visible (transition fondue, §3.2).
3. **Combat lisible en isométrique.** Ciblage, portées en cases ; aucune précision de type FPS.
4. **Serveur qui fait autorité.** Le client envoie des intentions, jamais des résultats.
5. **Simulation à la demande.** Une zone sans joueur ne consomme ni CPU ni réseau.
6. **Contenu en données.** Monstres, objets, quêtes, recettes et zones se créent au dashboard, sans
   code serveur spécifique.

### 1.3 Hors périmètre V1 — APPROUVÉ

Monde continu streamé, raids, PvP (duel et arène arrivent après la V1), dizaines de classes,
centaines de compétences, logement complexe, physique temps réel, matchmaking inter-serveurs,
scripts joueurs, élevage, wired, et tout ce que le ménage M0 retire (§2).

---

## 2. Existant et ménage M0 — APPROUVÉ

Le fork part de Vortex Cloud : .NET, Orleans, SuperSocket, EF Core + MySQL côté serveur ;
TypeScript + PixiJS v8 (`vortex-engine`, `vortex-client`, Glaze pour les layouts) côté client.

Le ménage M0 (`docs/superpowers/plans/2026-10-05-m0-menage-serveur.md`, puis le plan client
`vortex-rpg-client/docs/superpowers/plans/2026-10-05-m0-menage-client.md`) retire : outillage de
conformité Habbo, blockchain, collectibles/NFT, Habbicons, reward tracks, LoadGen, Benchmark, wired,
Freeze/BattleBanzai/Football, jukebox, mystery box, crackable, notes de room, caméra, élevage et
besoins des pets, Builders Club, classification, LTD, vouchers, offres ciblées, sondages, et réduit
le navigator. Il finit par une migration EF unique et une passe « protocole RPG » qui retire des
deux côtés les champs réseau figés (Task 12 du plan client).

Gardés et réutilisés par le RPG : rooms, meubles et logiques de meubles, bots, pets (cœur, selle et
monture), runtime de jeux (arènes, équipes, scores), échanges, marketplace, wallet, groupes Social,
amis, succès, Signals, events, dashboard, WebApi.

---

## 3. Architecture

### 3.1 Autorité serveur — APPROUVÉ

Le client envoie des intentions (`TargetSelect`, `SkillCast`, `Gather`, `Craft`…). Le serveur valide
portée, ligne de vue, cooldown, ressources et droits, applique, puis diffuse le résultat. Le client
ne décide jamais d'un dégât, d'un loot, d'un gain d'XP, d'une progression de quête, d'une recette
réussie, d'un prix ou d'un état d'inventaire. Les nombres affichés (dégâts flottants, barres) sont
de la présentation.

### 3.2 Modèle de monde : rooms-zones — APPROUVÉ

**Décision.** Le monde est un ensemble de **zones**, chacune étant une room bornée (≤ 64 × 64 cases),
reliées par des passages (portes, téléporteurs, bords de zone). Les maisons, QG de guilde, donjons et
arènes sont des **instances**. Le monde continu streamé par cellules est hors V1 (OUVERT, §22).

**Pourquoi (faits du code) :**

- Client : le sol et les murs d'une room sont construits une seule fois à l'entrée
  (`vortex-engine/src/habbo/room/RoomEngine.ts:3993`, second appel ignoré `:4006-4012`) ; chaque plan
  est dessiné sur son propre canvas sans limite de taille (`habbo/room/object/visualization/room/RoomPlane.ts:1305-1320`) ;
  une seule room vit à la fois (`habbo/session/RoomSessionManager.ts:575-589`) ; l'éditeur de plan
  borne à 64 cases par axe (`habbo/window/utils/floorplaneditor/FloorPlanCache.ts:41-44`).
- Serveur : une room est un ensemble de tableaux par case (`Vortex.Rooms/Grains/RoomLiveState.cs:34-39`),
  bornée à 64 × 64 par l'éditeur (`Vortex.Rooms/Grains/RoomGrain.FloorPlan.cs:28,121`) ; le
  pathfinding est un A* par requête dimensionné sur la room (`Vortex.Rooms/Grains/Systems/RoomPathingSystem.cs:105-381`) ;
  la diffusion va à tous les occupants sans filtre (`Vortex.Players/Grains/PlayerPresenceGrain.cs:80-94`).

Un monde continu demanderait de réécrire le moteur de plans du client (découpage par cellules, tri en
profondeur aux jonctions) et le modèle de room du serveur (visibilité, pathfinding hiérarchique,
protocole par cellule). Les rooms-zones tiennent avec le code existant.

**Transition entre zones — chantier client (M4).** Aujourd'hui, changer de room détruit tout puis
reconstruit (`RoomEngine.ts:2562-2593`), sans écran de transition. Le client DOIT afficher un fondu
entre la destruction de la room (`REE_DISPOSED`) et l'initialisation de la suivante, et DEVRAIT
précharger le contenu des meubles de la zone voisine (le cache de contenu survit déjà au changement
de room, `RoomEngine.ts:3956-3975`). Objectif : passage perçu < 1 s sur la machine de référence.

**Construction des maps.** Les zones se construisent avec de vrais meubles. Elles se construisent dans un **éditeur de cartes** : une **application de bureau**, pas un site web (APPROUVÉ ; technologie OUVERTE, §22) : une zone y devient un document de données (plan de sol et hauteurs ≤ 64 × 64, meubles, points d'apparition, nœuds de récolte, PNJ, passages, zones de sécurité), qui s'appuie sur le même moteur de placement. Le
placement, l'empilement, la rotation, les meubles muraux et la persistance des positions NE DOIVENT
PAS régresser ; les tests de `Vortex.Rooms.Tests/Furniture/` et `Mapping/` en sont le filet.

### 3.3 Runtime de zone (la couture) — APPROUVÉ

Combat, monstres et compagnons NE DOIVENT PAS dépendre de `Vortex.Rooms`. Ils travaillent contre une
abstraction définie dans le noyau `Vortex.Rpg`, le **runtime de zone** :

- la grille : marchable, hauteurs, distance en cases (Chebyshev), ligne de vue (murs et meubles
  bloquants) ;
- les combattants présents (joueurs, monstres, compagnons) et leurs positions ;
- la diffusion vers la zone ou vers un joueur ;
- l'horloge de la zone et l'ordonnancement par tick ;
- la publication d'events de zone.

**V1 :** `RoomGrain` implémente le runtime de zone. Le combat est une nouvelle étape de tick, isolée
comme les autres par `RunTickStepAsync` et mesurée par `RoomTickStepCompleted`
(`Vortex.Rooms/Grains/RoomGrain.cs:372-387,391,442`). Les monstres suivent le modèle des bots et des
pets (`RoomBotSystem`, `RoomPetSystem`) sans en hériter : ceux-ci n'ont ni PV, ni aggro, ni index
spatial.

**Plus tard :** un grain de zone hébergeant un monde ECS PEUT implémenter le même runtime de zone,
sans réécrire le gameplay. Il réutiliserait le timer aligné et l'isolation des étapes, la file de
flush, `RoomOutbound` et l'appartenance via `PlayerPresenceGrain.SetActiveRoomAsync`.

### 3.4 Projets et dépendances — APPROUVÉ

| Projet | Contenu | Dépend de |
|---|---|---|
| `Vortex.Rpg` (noyau) | Runtime de zone (abstraction), pile de stats et `[StatSource]`, formules, contrats des events RPG, attributs de registre, définitions de contenu, `RpgContentGrain` | Primitives, Events, Signals, Database |
| `Vortex.Rpg.Characters` | Classes, spécialisations, `PlayerCharacterGrain`, niveaux, XP, équipement, durabilité | Rpg |
| `Vortex.Rpg.Combat` | Compétences, `[SkillEffect]`, incantations, effets de statut, menace, ligne de vue, mort | Rpg |
| `Vortex.Rpg.Monsters` | `[MonsterBehavior]`, apparitions, laisse, boss, loot | Rpg, Rpg.Combat |
| `Vortex.Rpg.Companions` | Compagnon de combat, monture | Rpg, Rpg.Combat |
| `Vortex.Rpg.Quests` | `[QuestObjective]`, `PlayerQuestGrain`, PNJ, dialogues | Rpg, Signals |
| `Vortex.Rpg.Professions` | Récolte, `[ProfessionAction]`, recettes, artisanat (absorbe `Vortex.Fishing`) | Rpg |
| `Vortex.Rpg.Economy` | Marchands, réparation, rachat, extension de l'hôtel des ventes | Rpg, Marketplace |
| `Vortex.Rpg.Social` | Groupe, recherche de groupe, extension guildes | Rpg, Social |
| `Vortex.Rpg.Progression` | Succès RPG, réputation, titres, collections | Rpg, Signals |

`Vortex.Rooms` référence `Rpg.Combat`, `Rpg.Monsters` et `Rpg.Companions` pour les exécuter dans son
tick. `Quests` et `Professions` PEUVENT référencer `Vortex.Rooms` (ex. `[RoomObjectLogic("gather_node")]`).

**Règle :** un domaine RPG ne référence jamais un autre domaine RPG, seulement le noyau ; seules
exceptions : `Monsters` et `Companions` → `Combat`. Les domaines communiquent par events et signals.
Retirer un domaine feuille du `.sln` DOIT laisser le reste compiler et tourner. Un test
d'architecture DOIT échouer sur une référence interdite (base existante :
`scripts/hooks/check-architecture-walls.mjs`). Chaque projet a son `<Projet>.Tests`.

### 3.5 Propriété de l'état — APPROUVÉ

Une ressource modifiable a **un seul propriétaire** à un instant donné.

| État | Propriétaire |
|---|---|
| Classe, spécialisation, niveau, XP, stats de base, équipement, points de compétence, barre d'action | `PlayerCharacterGrain` (un par personnage) |
| Sac (objets RPG) | `PlayerCharacterGrain` (spec fille Objets) |
| Or | `PlayerWalletGrain` existant, nouvelle monnaie |
| PV, mana, cooldowns, incantations, effets de statut, menace de tous les combattants d'une zone | Runtime de zone (V1 : `RoomGrain`), en mémoire |
| Monstres, PNJ, nœuds de récolte, loot au sol | Runtime de zone |
| Quêtes du joueur | `PlayerQuestGrain` |
| Métiers du joueur | `PlayerProfessionGrain` |
| Groupe | `PartyGrain` (nouveau) |
| Guilde | Groupe Social existant + extension |
| Annonce de l'hôtel des ventes | Grain d'annonce existant de `Vortex.Marketplace` |
| Définitions de contenu | `RpgContentGrain`, chargé depuis la base, rechargé à chaud |

**Entrée en zone :** la zone demande une seule fois les stats calculées à `PlayerCharacterGrain`.
**Sortie :** une seule écriture de retour (PV restants, XP gagnée, usure). Aucune écriture en base par
coup porté.

### 3.6 Persistance — APPROUVÉ

- Persistant : personnage, niveau, XP, sac, équipement, monnaies, quêtes, succès, réputation,
  métiers, titres, guilde, annonces.
- Runtime seulement : cible, incantation, projectile, effet au sol, chemin d'IA, menace, cooldowns
  courts.
- Écriture par domaine et par lot quand c'est sûr ; frontières transactionnelles explicites ; pas
  d'écriture du personnage entier à chaque changement. La file de flush existante de `RoomGrain`
  (éléments sales toutes les 2 s, `Vortex.Rooms/Configuration/RoomConfig.cs:62-64`) est le modèle.

### 3.7 Réseau et protocole — APPROUVÉ

- Les messages RPG vont dans `Vortex.Revisions/Revision20260701` sur une plage d'ids **à partir de
  9000**. Côté client : parsers et events dans `vortex-engine`.
- Messages de domaine (`SkillCast`, `CombatEvents`, `StatusEffectApplied`, `QuestProgressUpdated`,
  `InventoryChanged`…) ; on ne détourne pas la sémantique d'un message Habbo.
- Snapshot complet à l'entrée en zone et à la reconnexion ; deltas ensuite.
- **Regroupement par tick :** les événements de combat d'une zone sont regroupés en **un message par
  tick**, comme `UserUpdateMessageComposer` le fait déjà pour les déplacements
  (`Vortex.Rooms/Grains/Systems/RoomAvatarTickSystem.cs`). Raison : aujourd'hui chaque message de
  room part une fois par occupant via un flux Orleans et un `PlayerPresenceGrain`
  (`RoomGrain.cs:276-281`, `PlayerPresenceGrain.cs:80-94`).
- Les handlers restent de l'orchestration ; les envois passent par
  `PlayerPresenceGrain.SendComposerAsync` ; aucune requête base dans un handler.

### 3.8 Zones vides et instances — APPROUVÉ

- Une zone sans joueur NE DOIT PAS simuler (ni IA, ni apparition, ni pathfinding). Une room sans
  occupant est désactivée après `RoomDeactivationDelayMs` (30 min aujourd’hui, `RoomConfig.cs:17`) ; les
  monstres repartent de leurs points d'apparition à la réactivation.
- **Instances — chantier serveur (M4).** Rien ne crée aujourd'hui de room temporaire : un
  `RoomGrain` est indexé par l'id de sa ligne en base (`Vortex.Rooms/RoomService.Create.cs:22-157`)
  et `RoomDirectoryGrain` n'est qu'un registre. La spec fille Donjons DOIT définir l'instanciation
  (copie d'une room modèle, clé hors base ou ligne temporaire, destruction quand l'instance est vide).

### 3.9 Invariants — APPROUVÉ

- **INV-001** Le client ne fait jamais autorité sur un résultat qui produit de la valeur.
- **INV-002** La simulation rapide (zone) est isolée de l'état métier durable (grains de domaine).
- **INV-003** Une zone sans joueur ne simule pas.
- **INV-004** Une ressource modifiable a un seul propriétaire à la fois.
- **INV-005** Les opérations économiques sont atomiques et idempotentes.
- **INV-006** Le contenu a des ids stables et vit en données.
- **INV-007** Chaque zone est une room bornée ; le monde est un graphe de zones et d'instances.
- **INV-008** Les donjons, QG de guilde et maisons sont des instances isolées.
- **INV-009** Aucun domaine RPG ne référence un autre domaine hors exceptions de §3.4.
- **INV-010** Chaque sous-système critique a des métriques de coût et de santé.

---

## 4. Modularité — APPROUVÉ

Le RPG réutilise les mécanismes existants : enregistrement par attribut (`[RoomGame]`,
`[RoomObjectLogic]`, `[RoomEventListener]`), events annulables (`Vortex.Events/Registry/ICancellableEventPublisher.cs`),
behaviors (`Vortex.Events/Registry/IEventBehavior.cs`), vocabulaire de Signals, plugins
(`Vortex.Primitives/Plugins/IVortexPlugin.cs`).

**Règle 1 — le contenu est de la donnée, le comportement est du code branchable.**

| Donnée (dashboard) | Code (registre par attribut, extensible par plugin) |
|---|---|
| Compétence : coût, cooldown, incantation, portée, cible, effets paramétrés | `[SkillEffect("damage" \| "dot" \| "heal" \| "hot" \| "buff" \| "debuff" \| "stun" \| "area")]` |
| Monstre : stats, comportement, table de loot, phases | `[MonsterBehavior("passive" \| "aggressive" \| …)]` |
| Quête : objectifs typés | `[QuestObjective("kill" \| "collect" \| "talk" \| "reach_zone" \| "craft" \| "gather")]` |
| Nœud de récolte, recette | `[ProfessionAction(…)]` |
| Source de stats | `[StatSource]` (classe, niveau, équipement, passifs, spécialisation, buffs) |

Les clés des registres alimentent les listes du dashboard ; une clé inconnue est refusée à la
sauvegarde ; le contenu invalide échoue au démarrage avec un message exploitable.

**Règle 2 — chaque étape publie un event.** Les events « -ing » sont annulables et modifiables, les
« -ed » informatifs : `SkillCasting`/`SkillCast`, `DamageApplying` (montant modifiable)/`DamageDealt`,
`HealApplying`/`Healed`, `CombatantDying` (annulable)/`CombatantKilled`, `LootRolling` (table
modifiable)/`LootDropped`, `XpGaining` (montant modifiable)/`LevelUp`, `QuestCompleting`/`QuestCompleted`,
`ItemCrafting`/`ItemCrafted`.

**Règle 3 — les stats sont une pile de modificateurs.** Stat finale = somme des `[StatSource]`
enregistrées ; ajouter une source ne modifie pas le calcul ; le calcul est déterministe ; le détail
par source est exposé à l'interface et au debug.

**Règle 4 — les quêtes et succès écoutent les Signals.** Les events « -ed » sont traduits en signals
(`rpg.monster_killed`, `rpg.item_crafted`, `rpg.item_gathered`…). Un objectif attend N occurrences
d'un signal avec des faits donnés : tout signal émis par un plugin devient un objectif possible.

**Garde-fou performance.** Les events de combat sont publiés dans le tick de la zone, sans appel
réseau ; leurs handlers sont synchrones et rapides ; les I/O (base, webhook) se font sur les events
« -ed », hors du tick.

---

## 5. Personnage, classes, stats

### 5.1 Personnage — APPROUVÉ (V1), OUVERT (nombre par compte, spec fille Personnage)

`PlayerCharacterGrain` porte classe, spécialisation, niveau, XP, stats de base, équipement, points de
compétence et barre d'action. Un personnage DOIT avoir un id stable distinct de son nom et un état
(actif, en zone, en combat, mort, déconnecté en grâce). Niveau max : OUVERT (spec fille Personnage).

### 5.2 Stats — APPROUVÉ (V1)

- Principales : **Force**, **Intelligence**, **Agilité** (stat principale selon la classe : dégâts
  physiques, magiques, à distance).
- Dérivées en V1 : PV, mana, armure (`dégâts × 100 / (100 + armure)`), critique (150 % des dégâts),
  esquive, vitesse (cadence d'attaque, vitesse de déplacement).
- La mana se régénère après 5 s hors combat.
- Les autres stats dérivées de la spec maître (résistance magique, blocage, pénétration…) sont des
  candidates pour plus tard.
- Le client NE DOIT PAS envoyer de stats ; les valeurs négatives ou invalides sont bornées.

### 5.3 Classes et spécialisations — APPROUVÉ (V1)

| Classe | Rôle | Portée | Ressource |
|---|---|---|---|
| Guerrier | Tank, menace × 3 | 1 case | Mana |
| Archer | Dégâts mono-cible | 6 cases | Mana |
| Mage | Dégâts de zone, contrôle | 5 cases | Mana |
| Clerc | Soins, buffs | 5 cases | Mana |

Une spécialisation par classe en V1 (l'onglet existe, §16). Les classes et spécialisations sont des
données ajoutables au dashboard. Coût et lieu du changement de spécialisation : OUVERT (spec fille
Combat).

---

## 6. Compétences

### 6.1 Barre et ciblage — APPROUVÉ

Attaque de base automatique ; barre d'action de **6** compétences, plus objets consommables liables ;
6 compétences actives au niveau 20 ; délai global de **1 s** ; ciblage au clic (`TargetSelect` puis
`SkillCast`). Un raccourci référence un id d'action et ne duplique jamais l'état d'un objet ; un objet
retiré invalide proprement son raccourci. Les raccourcis sont persistés par personnage.

### 6.2 Définition d'une compétence — APPROUVÉ

Id stable, classes et spécialisations autorisées, coût, cooldown, comportement vis-à-vis du délai
global, type (instantanée, incantée, canalisée), durée d'incantation ou de canalisation, portée,
mode de ciblage (soi, allié, ennemi, sol, zone autour du lanceur ou de la cible), forme et taille de
zone, liste d'effets `[SkillEffect]` paramétrés, ids d'animation, de VFX et de son, règles
d'interruption et de déplacement.

### 6.3 Résolution — APPROUVÉ

Ordre des contrôles serveur : vivant → cible valide → portée (distance de Chebyshev en cases) →
ligne de vue (aucun mur ni meuble bloquant) → cooldown → délai global → mana. Échec : code d'erreur
stable au client. Cible hors de portée : l'avatar s'approche à portée puis lance. Succès : effet
appliqué au tick suivant, diffusé dans le message groupé de la zone (§3.7).

### 6.4 Incantations, canalisations, interruptions — APPROUVÉ

Une incantation est annulée par : interruption, étourdissement, mort, changement de zone,
déconnexion au-delà de la grâce, perte de la cible. Une canalisation applique son effet par
pulsations et s'arrête sur les mêmes causes. Bouger annule une incantation sauf si la compétence
l'autorise.

### 6.5 Effets de statut — APPROUVÉ

Types : buff, debuff, dégâts sur la durée, soin sur la durée, étourdissement, ralentissement,
bouclier. Une définition donne : id stable, durée, rythme d'application, règle de cumul
(**remplacer**, **rafraîchir**, **cumuler jusqu'à N**, **ignorer**), effets, dissipable ou non.
Les effets disparaissent en quittant la zone, sauf ceux marqués persistants.

### 6.6 Passifs — PROPOSÉ

Les passifs modifient stats, coefficients, ressources ou cooldowns, de façon événementielle (pas de
sondage périodique quand un event suffit).

---

## 7. Combat, monstres, boss

### 7.1 État de combat — APPROUVÉ

Un combattant entre en combat sur action hostile émise ou subie ; il en sort après 5 s sans action
hostile et sans menace active. Ce délai est configurable.

### 7.2 Menace — APPROUVÉ

Dégâts et soins génèrent de la menace (× 3 pour le Guerrier) ; le monstre attaque le premier de sa
table ; la table se vide à la mort, à la sortie de zone ou à la remise à zéro du monstre. Provocation :
PROPOSÉ (spec fille Combat).

### 7.3 Monstres — APPROUVÉ

- Définition statique : id stable, stats, comportement `[MonsterBehavior]`, rayon d'aggro, laisse,
  table de loot, compétences.
- Entité de zone : position, PV, effets, menace, état d'IA (repos, patrouille, alerte, poursuite,
  combat, retour, mort).
- **Laisse :** trop loin de son point d'apparition, le monstre rentre, se soigne entièrement et vide
  sa table de menace.
- Archétypes V1 : 3 types communs, 1 élite, 1 boss (§21).
- **Pathfinding — chantier.** L'A* actuel est recalculé à chaque requête et alloue ses structures
  à chaque appel (`RoomPathingSystem.cs:105-381`). Les monstres NE DOIVENT PAS recalculer un chemin à
  chaque tick ; requêtes bornées, réutilisation quand la destination ne change pas, détection de
  blocage. Budget mesuré en M1.

### 7.4 Apparitions — APPROUVÉ

Point d'apparition = définition de monstre, position ou zone, nombre, délai de réapparition. Pas
d'apparition dans une zone sans joueur. Un monstre tué ne réapparaît pas en double après une
désactivation et une réactivation de la zone.

### 7.5 Boss — APPROUVÉ

Phases déclenchées par un pourcentage de PV, définies au dashboard ; télégraphes au sol (zone et
délai avant impact, visibles par tous) ; remise à zéro complète si tous les joueurs sont morts ou
sortis.

---

## 8. Mort, loot, objets

### 8.1 Mort du joueur — APPROUVÉ

Fantôme 5 s, réapparition au point de la zone avec 50 % des PV, 10 % d'usure de l'équipement. À 0 de
durabilité, un objet ne donne plus de stats jusqu'à réparation (or, chez un PNJ).

### 8.2 Mort d'un monstre — APPROUVÉ

XP partagée dans le groupe présent ; **loot personnel**, tiré une seule fois par joueur éligible, de
façon idempotente : un renvoi ou une course ne crée jamais un second tirage. Loot de boss **lié au
joueur**, non échangeable.

### 8.3 Objets — APPROUVÉ (modèle), OUVERT (détails, spec fille Objets)

Définition d'objet : id stable (`item.weapon.rusted_sword`), nom localisé, catégorie, rareté,
empilable ou non, niveau requis, classes autorisées, emplacement d'équipement, stats, durabilité,
valeur marchande, liaison. OUVERTS : liste des emplacements, échelle et noms de rareté, taille du sac
et son extension, liaison à l'équipement.

### 8.4 Sac et équipement — APPROUVÉ (règles)

Toute opération de sac (ajout, retrait, empilement, séparation, équipement, déséquipement) est
validée et exécutée par le serveur. Un loot sur sac plein va dans un état « à récupérer » plutôt que
d'être perdu (politique précise : spec fille Objets). Équiper vérifie classe, niveau, emplacement et
durabilité.

---

## 9. Compagnons et montures — APPROUVÉ

Le compagnon est le pet existant devenu combattant de la zone : il suit son maître, attaque sa cible,
a 2 compétences selon l'espèce, gagne de l'XP avec son maître, et reste « K.O. » jusqu'à la sortie de
combat s'il meurt. La monture n'est utilisable que hors combat ; la selle et la monte fonctionnent
depuis le ménage M0 (`CustomizePetWithFurni`, `GetPetInfoMessageHandler`).

---

## 10. Quêtes, dialogue, services PNJ

### 10.1 Quêtes — APPROUVÉ

Quête = prérequis (niveau, quête précédente, classe) + objectifs `[QuestObjective]` + récompenses
(XP, or, objets, meubles, titre). Les quêtes s'enchaînent. Journal limité à **20** quêtes actives
(configurable). Progression événementielle via Signals, persistée par lot. Quêtes journalières :
PROPOSÉ (heure de remise à zéro OUVERTE, spec fille Quêtes).

### 10.2 Dialogue et PNJ — APPROUVÉ

PNJ = bot avec un rôle (`quest_giver`, `vendor`, `repairer`…). Fenêtre de dialogue : texte, choix,
accepter ou refuser. Le serveur valide chaque choix (prérequis, distance au PNJ) ; le client ne fait
qu'afficher.

---

## 11. Métiers — APPROUVÉ (V1 : 1 récolte, 1 artisanat)

- Nœud de récolte : meuble `[RoomObjectLogic("gather_node")]`, clic, canalisation de 3 s, ressource
  reçue, nœud épuisé puis réapparition. Partage entre joueurs : premier qui termine la canalisation
  (spec fille Métiers).
- Niveau de métier de 1 à 50, indépendant du niveau de combat.
- Recette = ingrédients + atelier (meuble) + niveau de métier → objet, avec une chance de qualité
  supérieure. Consommation des ingrédients et production DOIVENT être atomiques.
- Cible complète (après V1) : Minage, Herboristerie, Bûcheronnage, Pêche (reprend `Vortex.Fishing`) ;
  Forge, Alchimie, Menuiserie (meubles de maison).

---

## 12. Économie

- **Or** — APPROUVÉ : gagné sur les monstres, les quêtes et les ventes aux PNJ ; dépensé en
  réparations, achats PNJ, recettes et frais d'hôtel des ventes (**5 %**).
- **Marchand** — APPROUVÉ (V1) : onglets Acheter / Vendre / Rachat ; catégories Équipement,
  Consommables, Matériaux, Divers ; rachat limité dans le temps et en nombre.
- **Échange direct** — APPROUVÉ : `RoomTradingSystem` existant, étendu aux objets RPG ; tout
  changement du contenu remet les deux validations à zéro ; l'objet lié n'est pas échangeable.
- **Hôtel des ventes** — après V1 : `Vortex.Marketplace` étendu aux objets RPG ; onglets Acheter /
  Vendre / Mes ventes / Historique ; durée et taxe exactes OUVERTES (spec fille Économie).
- **Premium** — APPROUVÉ : crédits et diamants cosmétiques seulement, sans stats ni XP.
- **Banque, courrier** — après V1 (spec fille Économie).

---

## 13. Social

- **Groupe** — APPROUVÉ (V1) : inviter, quitter, exclure, chef, statut des membres (PV, zone),
  partage d'XP. Taille max : OUVERT (spec fille Groupe).
- **Recherche de groupe** — APPROUVÉ (V1, basique) : onglets Recherche de groupe / Mes inscriptions ;
  annonces (activité, niveau, rôles recherchés) ; un joueur ne peut pas s'inscrire deux fois à la
  même activité.
- **Guildes** — après V1 : groupes Social existants (bannière, rangs, forum, chat) + niveau de guilde
  alimenté par une part de l'XP des membres (`XpGaining`), bonus par niveau (`[StatSource]`), QG en
  instance, journal d'audit des actions de rang.
- **Amis, blocage, chat** : l'existant ; un joueur bloqué ne peut ni chuchoter, ni inviter, ni
  échanger.

---

## 14. Progression — APPROUVÉ (V1 basique)

- **Succès RPG** : écoutent les Signals (règle 4) ; attribution idempotente.
- **Réputation** : par faction, gagnée par quêtes et monstres, paliers débloquant des objets chez des
  marchands.
- **Titres, collections, badges** : après V1 ; un seul système de propriété des titres (le titre de
  succès est une vue filtrée).

---

## 15. PvP — après V1

Consenti seulement : duel (demande, acceptation, combat jusqu'à 1 PV, sans pénalité) et arène par
équipes sur `Vortex.Rooms/Games` (Arena, Teams, Runtime gardés par le M0). Hors duel et arène, aucun
dégât entre joueurs. Champs de bataille et classement : OUVERT.

---

## 16. Client

### 16.1 Organisation — APPROUVÉ

Une feature par domaine dans `vortex-engine/src/habbo/rpg/` (`combat`, `character`, `skills`,
`quests`, `professions`, `group`, `shop`…), chacune avec ses messages, son état et ses fenêtres. Les
features communiquent par events client (`CombatTargetChanged`, `PlayerStatsUpdated`,
`QuestProgressUpdated`…), jamais par références directes. Les fenêtres sont des layouts XML Glaze
`vortex-client/src/vortex-layouts/vortex_rpg_*_xml.xml`.

### 16.2 Fenêtres et onglets — APPROUVÉ

| Fenêtre | Onglets |
|---|---|
| Personnage (C) | Équipement · Stats · Titres · Réputation |
| Compétences (K) | Actives · Passives · Spécialisation · Raccourcis |
| Quêtes (J) | Principales · Secondaires · Journalières · Terminées |
| Succès | Vue d'ensemble · Collections · Badges · Titres |
| Groupe | Groupe actuel · Recherche de groupe · Mes inscriptions |
| Boutique PNJ | Acheter · Vendre · Rachat (catégories : Équipement, Consommables, Matériaux, Divers) |
| Hôtel des ventes | Acheter · Vendre · Mes ventes · Historique |
| Sac (I) | — |
| Métiers (P) | Vue d'ensemble · Recettes |
| Guilde (G) | Vue d'ensemble · Membres · Activités · Gestion (après V1) |
| Dialogue PNJ, réparation, mort et réapparition | — |

Les onglets d'une fonctionnalité après V1 PEUVENT exister désactivés. Courrier, banque, carte,
résumé de donjon, PvP, notifications, suivi d'événement et tirage de loot : maquettes à valider dans
leur spec fille.

### 16.3 HUD et room — APPROUVÉ

HUD : portrait, PV, mana, niveau (et PV du compagnon) ; cible ; barre d'action 1–6 avec cooldowns ;
barre d'XP ; groupe (portraits) ; suivi de quêtes ; barre d'incantation. Dans la room : barres de vie
au-dessus des combattants, nombres flottants, cercle de cible, zones et télégraphes au sol, arme en
main, animations d'attaque, de sort et de mort. Les effets visuels restent sobres et lisibles en
isométrique.

**Point de vigilance — mesure en M1.** Les visualisations de meubles sont mises à jour à chaque image
sans culling, et le culling des avatars est désactivé (`habbo/room/renderer/RoomCullingMode.ts`).
Le coût avec une zone pleine de monstres DOIT être mesuré.

### 16.4 Accessibilité et réglages — APPROUVÉ

Toutes les actions de combat au clavier et à la souris ; chaque barre affiche une valeur chiffrée en
plus de la couleur ; volumes séparés (musique, effets, interface), intensité des VFX, nombres
flottants activables.

---

## 17. Dashboard et contenu — APPROUVÉ

- Pages : Classes, Spécialisations, Compétences, Effets de statut, Monstres, Objets, Tables de loot,
  Quêtes, PNJ, Métiers et recettes, Zones, Donjons. Chaque page suit la règle « Add dashboard
  capability or admin page » d'`AGENTS.md` (4 fichiers, `check-dashboard-capabilities.mjs`).
- Les champs de comportement proposent les clés des registres ; une référence cassée est refusée à
  la sauvegarde ; chaque sauvegarde recharge `RpgContentGrain` à chaud.
- **Ids stables** indépendants des noms (`monster.forest.bramble_wolf`, `quest.ambre.main.001`) ;
  convention exacte dans la spec fille Contenu.
- **Localisation** : tout texte de contenu passe par une clé ; aucun texte joueur codé en dur côté
  serveur.
- **Migration de contenu** : supprimer ou modifier un objet, une compétence, une quête ou une recette
  déjà détenue DOIT passer par une migration explicite (remplacement, remboursement ou conservation).

---

## 18. Intégrité, concurrence, reconnexion

- **Opérations atomiques et idempotentes** : échange, achat et vente PNJ, rachat, réparation,
  artisanat, loot, récompenses de quête et de succès, et plus tard achat et paiement de l'hôtel des
  ventes, courrier, banque de guilde. Un renvoi, un délai d'attente ou une requête dupliquée ne crée
  jamais d'objet ni de monnaie.
- **Concurrence** : chaque ressource a son propriétaire (§3.5) ; pas d'écritures concurrentes depuis
  deux grains.
- **Reconnexion** : délai de grâce (durée OUVERTE, spec fille Personnage) pendant lequel le
  personnage reste dans sa zone ; à la reconnexion, snapshot complet ; jamais de double personnage ni
  de double récompense.
- **Erreurs** : codes stables côté serveur, texte localisé côté client.
- **Limitation de débit** : par type d'intention (cast, déplacement, chat, échange), en plus des
  limites existantes de session.

---

## 19. Observabilité et budgets — APPROUVÉ

- Métriques : durée de tick par étape (existant : `RoomTickStepCompleted`), entités par zone,
  messages et octets par seconde et par zone, coût du pathfinding, latence de persistance,
  transactions économiques.
- Logs structurés corrélés par joueur, personnage, zone et action.
- Budgets chiffrés fixés en M1 après mesure (tick, messages par zone), puis vérifiés à chaque jalon.
- Le tick de 50 ms tourne en pratique vers 16 Hz sous Windows (`RoomConfig.cs:38-50`) : les budgets
  se mesurent sur la machine de référence, pas en théorie.

---

## 20. Tests — APPROUVÉ

- `Vortex.Rpg.*.Tests` : règles pures (dégâts, armure, critique, XP, usure, menace, cumul de statuts,
  loot).
- Combat de bout en bout dans `Vortex.Rooms.Tests` : incantation, dégâts, menace, mort, loot,
  réapparition. Le modèle cité par l'ancienne spec (`WiredProjectileEndToEndTests`) a disparu avec les
  wired : le harnais de room (`Vortex.Rooms.Tests/Support/RoomHarness.cs`) sert de base.
- Extensibilité : un faux `[SkillEffect]` et un handler sur `DamageApplying` se branchent sans
  modifier le cœur.
- Architecture : aucune référence interdite (§3.4).
- Cohérence : toute clé référencée par le contenu de démonstration existe dans un registre.
- Invariants : jamais d'objet ni de monnaie créés par un renvoi ; un loot attribué une seule fois ;
  une quête terminée une seule fois.
- Charge : un combat de 20 joueurs contre 15 monstres dans une zone, mesuré (tick, messages). Le
  générateur de charge supprimé par le M0 se récupère depuis l'historique git (`Vortex.LoadGen` à
  `af91e325`).

---

## 21. Jalons et tranche V1 — APPROUVÉ

### 21.1 Tranche V1

1 ville, 1 zone extérieure, 1 donjon instancié ; 4 classes avec 1 spécialisation chacune ; 3 types
de monstres, 1 élite, 1 boss ; équipement, sac, loot ; 10 à 15 quêtes ; 1 métier de récolte et 1
d'artisanat ; 1 marchand ; groupe et recherche de groupe basique ; succès et réputation basiques ;
compagnon de combat ; transition fondue entre zones.

### 21.2 Jalons

| Jalon | Contenu | Spec fille |
|---|---|---|
| M0 | Ménage serveur et client, puis passe protocole | plans existants |
| M1 | Éditeur de cartes (version minimale) ; noyau, runtime de zone, personnage, combat, monstres dans une zone ; regroupement réseau ; HUD de base ; budgets mesurés | Monde et éditeur de cartes, Personnage, Combat, Monstres |
| M2 | Objets, sac, équipement, loot | Objets |
| M3 | Quêtes, PNJ, dialogue, marchand | Quêtes, Économie (marchand) |
| M4 | Groupe, recherche de groupe, donjon instancié, transition fondue | Groupe, Donjons, Monde |
| M5 | Récolte, artisanat, compagnon de combat | Métiers, Compagnons |
| M6 | Succès et réputation basiques, polissage : tranche V1 complète | Progression |
| Après | Guildes, hôtel des ventes RPG, banque, courrier, titres et collections, PvP, événements et boss de monde, carte et voyage ; étude du monde continu | à créer |

### 21.3 Critères d'acceptation de la V1

- Un joueur entre dans la ville, passe dans la zone extérieure par un passage fondu.
- Une zone sans joueur ne simule pas ; elle se réveille correctement à l'arrivée d'un joueur.
- Plusieurs joueurs combattent le même monstre ; le combat est validé par le serveur.
- Compétences, cooldowns, incantations et effets de statut se résolvent correctement.
- Le loot est attribué une seule fois par joueur.
- Les changements de sac et d'équipement persistent.
- La progression de quête est événementielle et persiste.
- Un groupe entre dans une instance de donjon isolée.
- Une déconnexion suivie d'une reconnexion ne duplique ni joueur ni récompense.
- Échange et marchand ne peuvent pas dupliquer d'objet ni d'or.
- Les métriques montrent le coût par zone (tick, IA, pathfinding, réseau).
- Le contenu de démonstration se crée au dashboard sans code serveur spécifique.
- La construction des maps avec de vrais meubles fonctionne comme avant le M0.

---

## 22. Registre des décisions

### 22.1 APPROUVÉES

Monde en rooms-zones ≤ 64 × 64 avec transition fondue ; runtime de zone comme couture ; projets
`Vortex.Rpg.*` et règle de dépendance ; propriétaires d'état (§3.5) ; ids de messages ≥ 9000 et
regroupement par tick ; 4 classes (Guerrier, Archer, Mage, Clerc) avec 1 spécialisation chacune ;
stats principales Force, Intelligence, Agilité ; ressource mana ; barre de 6 ; attaque automatique ;
ciblage au clic ; incantations, canalisations, interruptions et effets de statut en V1 ; PvP
consenti seulement, après V1 ; mort (fantôme 5 s, 50 % PV, 10 % d'usure) ; loot personnel ; loot de
boss lié ; métiers cibles 4 récolte + 3 artisanat (V1 : 1 + 1) ; taxe d'hôtel des ventes 5 % ;
progression de guilde par part d'XP ; tick de 50 ms ; premium cosmétique ; onglets de §16.2 ; tranche
V1 de §21.1.

### 22.2 OUVERTES

| # | Décision | Spec fille |
|---|---|---|
| 1 | Liste des emplacements d'équipement | Objets |
| 2 | Échelle et noms de rareté | Objets |
| 3 | Liaison à l'équipement (en plus du loot de boss lié) | Objets |
| 4 | Taille du sac et extension | Objets |
| 5 | Politique de sac plein | Objets |
| 6 | Modèle de durée d'artisanat | Métiers |
| 7 | Heure de remise à zéro des journalières | Quêtes |
| 8 | Durée et taxe exacte de l'hôtel des ventes (après V1) | Économie |
| 9 | Recherche de groupe : annonces seules ou matchmaking | Groupe |
| 10 | Taille max du groupe | Groupe |
| 11 | Modèle de difficulté des donjons | Donjons |
| 12 | Instanciation (copie de room modèle, clé hors base ou ligne temporaire) | Donjons |
| 13 | Succès et collections : par personnage ou par compte | Progression |
| 14 | Règles de voyage rapide | Monde |
| 15 | Niveau max et progression après le niveau max | Personnage |
| 16 | Nombre de personnages par compte | Personnage |
| 17 | Durée de grâce à la reconnexion | Personnage |
| 18 | Coût et lieu du changement de spécialisation | Combat |
| 19 | Provocation (taunt) | Combat |
| 20 | Budgets chiffrés (tick, messages par zone) | Combat (mesure M1) |
| 21 | Vitesse de déplacement en combat (les avatars avancent d'une case par 500 ms, `RoomConfig.cs:52`) | Combat |
| 22 | Monde continu streamé | Étude après V1 |
| 23 | Technologie de l'éditeur de cartes (application de bureau, APPROUVÉ) : coquille native réutilisant le rendu isométrique de `vortex-engine` (Tauri ou Electron), ou application .NET native (Avalonia) partageant les modèles de contenu du serveur avec un rendu isométrique à écrire | Monde et éditeur de cartes |

---

## 23. Specs filles

Ordre de rédaction, chacune avant le plan de son jalon :

0. `…-rpg-world-map-editor-design.md` — zones comme documents de données, éditeur de cartes, passages et transition fondue (avant M1 : M1 a besoin d'une zone avec des monstres placés)
1. `…-rpg-character-design.md` — personnage, classes, stats, spécialisations, reconnexion (M1)
2. `…-rpg-combat-design.md` — runtime de zone, compétences, incantations, statuts, menace, budgets (M1)
3. `…-rpg-monsters-design.md` — définitions, IA, apparitions, laisse, boss, pathfinding (M1)
4. `…-rpg-items-design.md` — objets, sac, équipement, rareté, loot (M2)
5. `…-rpg-quests-design.md` — quêtes, PNJ, dialogue (M3)
6. `…-rpg-economy-design.md` — marchand, réparation, rachat, puis hôtel des ventes, banque, courrier (M3, après)
7. `…-rpg-group-dungeons-design.md` — groupe, recherche de groupe, instances, transition fondue (M4)
8. `…-rpg-professions-design.md` — récolte, artisanat (M5)
9. `…-rpg-companions-design.md` — compagnon de combat (M5)
10. `…-rpg-progression-design.md` — succès, réputation, puis titres et collections (M6)
11. `…-rpg-content-design.md` — ids stables, localisation, migrations de contenu, dashboard (transverse, avant M2)

Le préfixe `…` est la date de rédaction (`AAAA-MM-JJ`).
