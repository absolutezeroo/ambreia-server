# RPG — spec fille M1a « Premier combat » : monstres, attaque automatique, PV, mort, XP

> **Statut :** livrée le 2026-10-09 (plan `docs/superpowers/plans/2026-10-09-rpg-combat-m1a.md`,
> preuve `driver.mjs combat`) ; design approuvé par le propriétaire le 2026-10-09 ·
> **Date :** 2026-10-09 · **Langue :** français
> **Dépôt :** `vortex-rpg-emulator`. Jalon M1 de la spec unifiée
> `docs/superpowers/specs/2026-10-06-vortex-rpg-design.md` (§3.3 runtime de zone, §3.4 projets,
> §3.5 propriété, §5.2 stats, §6.3 résolution, §7 combat et monstres, §8.1–8.2 mort). M1 est
> découpé en trois tranches : **M1a premier combat** (cette spec), M1b compétences et effets de
> statut, M1c monstres avancés (comportements variés, élite, boss). Tient lieu, pour M1a, des specs
> filles « Combat » et « Monstres » de la spec unifiée §23 ; M1b et M1c les compléteront.

---

## 1. But

Un premier combat jouable de bout en bout : des monstres apparaissent dans une zone, un joueur en
clique un, s'approche, le frappe automatiquement jusqu'à sa mort et gagne de l'XP ; un monstre
agressif attaque à vue ; un joueur tué réapparaît. Les PV et la mana d'un personnage sont les mêmes
partout.

**Critère de réussite.** Sur une zone de preuve peuplée de loups : un clic sur un loup le tue en
quelques coups et donne de l'XP ; un loup agressif engage un joueur qui s'approche ; un joueur tué
réapparaît à 50 % de ses PV ; un joueur qui change de zone à 30 % de PV arrive à 30 % ; deux
joueurs qui frappent le même loup gagnent chacun l'XP entière.

## 2. Décisions — APPROUVÉ (conversation du 2026-10-09)

1. **Découpage de M1** en M1a (cette spec), M1b, M1c ; M1a d'abord, jouable seule.
2. **Visuel des monstres :** un **pet Habbo** (type d'avatar 2 : race et couleur) ; la définition
   du monstre dit lequel.
3. **Attaque :** **clic = cible + attaque**. Hors de portée, l'avatar s'approche (spec unifiée §6.3)
   puis frappe à sa cadence.
4. **XP :** chaque joueur encore dans la zone qui a touché le monstre reçoit **l'XP entière** ; le
   partage viendra avec le groupe (M4).
5. **Architecture :** l'approche de la spec unifiée §3.3–3.4 : un runtime de zone (abstraction du
   noyau `Vortex.Rpg`) que `RoomGrain` implémente ; deux **nouveaux projets** `Vortex.Rpg.Combat`
   et `Vortex.Rpg.Monsters` (création approuvée).
6. **PV et mana partout les mêmes :** le personnage les possède et les persiste ; la zone les porte
   pendant qu'on y est et les rend en une seule écriture à la sortie. Changer de zone ne remet rien
   à zéro.
7. **Modulaire et extensible** (règle du propriétaire) : comportements de monstre, étapes de coup
   et sorte de contenu sont des pièces déclarées par DI ; le cœur n'en nomme aucune (§13).

## 3. Périmètre

**Dans M1a.** Sorte de contenu `monster` ; apparitions des `ZoneSpawn` ; IA de base `melee`
(repos, poursuite, combat, retour, mort) ; attaque automatique des joueurs et des monstres ;
chaîne de coup (esquive, dégâts, critique, armure) ; état de combat ; régénération hors combat ;
menace ; mort et réapparition du joueur avec usure ; mort, XP et réapparition du monstre ; PV et
mana courants persistés ; paquets du HUD ; preuve `driver.mjs combat`.

**Hors M1a.** Compétences, délai global, incantations, effets de statut (M1b) ; comportements
`ranged`/`caster`, élite, boss, télégraphes (M1c) ; loot (M2) ; groupes et partage d'XP (M4) ;
PvP (après V1) ; vitesse de déplacement propre à chaque monstre (décision ouverte 21 : tout
avatar avance d'une case par 500 ms) ; `Group` et `Conditions` des apparitions (déclencheurs
`SpawnGroup`, plus tard).

## 4. Architecture

### 4.1 Projets

| Projet | Contenu | Dépend de |
|---|---|---|
| `Vortex.Rpg` (noyau) | le **runtime de zone** (interface), la sorte de contenu `monster` et sa définition | Primitives, Database |
| `Vortex.Rpg.Combat` (nouveau) | combattants, PV et mana en jeu, attaque automatique, chaîne de coup (`IHitStep`), état de combat, régénération, menace, mort, XP de fin de combat | Rpg, Primitives |
| `Vortex.Rpg.Monsters` (nouveau) | apparitions, `IMonsterBehavior` et `melee`, laisse, retour, réapparition | Rpg, Rpg.Combat, Primitives |

`Vortex.Rooms` référence `Vortex.Rpg.Combat` et `Vortex.Rpg.Monsters` pour les exécuter dans son
tick (spec unifiée §3.4). Les deux projets entrent dans `PLATFORM` de
`scripts/hooks/check-architecture-walls.mjs` ; chacun a son projet de tests. Aucun des deux ne
référence `Vortex.Rooms`.

### 4.2 Le runtime de zone

Une interface du noyau (`Vortex.Rpg`), implémentée par `RoomGrain` ; seulement ce que M1a utilise :

- **Grille :** distance de Chebyshev en cases ; case marchable ; ligne de vue (rayon sur la grille :
  ni mur, ni case fermée, ni meuble bloquant) ; règles de la case (`Safe`, `Hostile`).
- **Entités :** poser un monstre (un avatar pet) sur une case, le retirer ; lire la position d'un
  avatar ; faire marcher un avatar vers une case, arrêter sa marche ; téléporter (réapparition) ;
  poser ou retirer une posture (`lay` pour le fantôme).
- **Diffusion :** à la zone (le flux de la room) ou à un joueur (sa présence).
- **Horloge :** l'instant du tick en cours ; **aléatoire** injectable (tests déterministes).
- **Événements :** publication d'événements de zone et du bus.

Les chemins des monstres passent par l'A* « marcheur à règles propres » existant
(`RoomPathingSystem.FindPath(start, goal, canOccupy, canMove)`), avec un plafond de recherches par
tick et par zone (§10) ; un chemin est réutilisé tant que la destination ne change pas.

### 4.3 Le tick

Une étape `"combat"` de `RoomGrain`, à son propre rythme (`Vortex:Combat:TickMs`, 250 ms),
isolée par `RunTickStepAsync` et mesurée par `RoomTickStepCompleted`, placée après `"avatars"` et
avant les écritures différées. Elle fait, dans l'ordre : apparitions dues, IA des monstres, coups
(minuteur de frappe par combattant), sorties de combat, régénération, morts et réapparitions, puis
envoie les messages groupés du tick (§9). Les déplacements restent dans l'étape `"avatars"`
(une case par 500 ms). Une zone sans joueur ne simule pas (INV-003) : l'étape ne fait rien tant
qu'aucun joueur n'est dans la room.

### 4.4 Propriété de l'état

| État | Propriétaire |
|---|---|
| PV et mana courants d'un personnage hors zone ; leur persistance | `PlayerCharacterGrain` |
| PV et mana d'un joueur dans la zone, PV des monstres, menace, état de combat, minuteurs, IA | le runtime de zone (`RoomGrain`), en mémoire |
| Définitions de monstres | le contenu (`IRpgContent`) |

À l'entrée en zone, la room lit **une fois** stats et PV/mana du personnage ; à la sortie (changement
de zone, déconnexion au-delà de la grâce, désactivation de la room), elle les rend **une fois**.
Aucune écriture en base par coup. Un changement de stats du personnage pendant qu'il est en zone
(objet équipé, niveau) est poussé à la room par la présence du joueur, sans attente (aucun cycle
room ↔ personnage).

## 5. Monstres

### 5.1 La sorte de contenu `monster`

`content/monster/*.json`, schéma `schemas/content/monster.schema.json`, textes `<id>.name` dans
`content/texts/fr.txt`. Exemple :

```json
{
  "$schema": "../../schemas/content/monster.schema.json",
  "id": "monster.forest.wolf",
  "kind": "monster",
  "figure": "0 3 C0A060",
  "behavior": "melee",
  "aggressive": true,
  "base": { "health": 40, "armor": 2, "damageMin": 3, "damageMax": 5, "critPercent": 5, "dodgePercent": 3 },
  "perLevel": { "health": 12, "armor": 1, "damageMin": 1, "damageMax": 2 },
  "attackRange": 1,
  "attackIntervalMs": 2000,
  "leashRadius": 10,
  "xp": 20,
  "xpPerLevel": 6
}
```

- `figure` : la figure de pet Habbo, `"<type> <race> <couleur>"`.
- Stats au niveau L : `base + perLevel × (L − 1)` ; dégâts tirés entre min et max.
- `behavior` : une clé de comportement déclarée (§13) ; inconnue → contenu refusé à l'import.
- `aggressive` : attaque à vue dans le rayon d'aggro ; sinon il riposte seulement. Une case
  `Hostile` rend tout monstre agressif.
- `leashRadius` : distance maximale à son point de départ ; 0 → 2 × le rayon d'aggro.
- XP au niveau L : `xp + xpPerLevel × (L − 1)`.
- Le rayon d'aggro vient de l'apparition (`ZoneSpawn.AggroRadius`), la carte décidant.

Contrôles à l'import : valeurs positives, `damageMin ≤ damageMax`, `attackRange ≥ 1`,
`attackIntervalMs ≥ 500`, pourcentages entre 0 et 100, figure de trois éléments. Le validateur de
zone résout désormais `ZoneSpawn.Monster` contre ce contenu.

### 5.2 Apparitions

Les `ZoneSpawn` sont compilés avec la zone. À l'activation de la simulation (premier joueur
présent), chaque apparition fait naître `Max` monstres, chacun sur une case marchable, libre et non
`Safe` de son aire, à un niveau tiré entre `LevelMin` et `LevelMax`. Un monstre tué renaît après
`RespawnSeconds` sur une case de l'aire, au plus une naissance par apparition et par tick. Une
apparition dont le monstre est inconnu ou retiré du contenu ne fait rien (avertissement journalisé
une fois). Une room réactivée repart de ses apparitions : aucun monstre en double (spec unifiée §7.4).

### 5.3 IA de base (`melee`)

| État | Comportement |
|---|---|
| Repos | Erre dans `WanderRadius` autour de son point de départ (un pas de temps en temps). Agressif : passe en poursuite dès qu'un joueur vivant, hors case `Safe`, entre dans son rayon d'aggro et en ligne de vue. |
| Poursuite | Marche vers le premier de sa table de menace ; recalcule le chemin seulement si la cible a bougé (budget §10). |
| Combat | À portée et en ligne de vue : frappe à sa cadence. |
| Retour | Au-delà de la laisse, table vide, ou cible passée sur une case `Safe` sans autre cible : rentre à son point, **invulnérable** ; arrivé, remonte à 100 % de PV et vide sa table. |
| Mort | Retiré de la room ; attend sa réapparition. |

Un monstre n'entre jamais sur une case `Safe` et n'attaque pas un joueur qui s'y tient.

## 6. Combat

### 6.1 L'attaque du joueur

`RpgTargetSelect` sur un monstre le cible et **démarre l'attaque automatique**. Hors de portée
(portée de la classe : `ClassDefinition.Range`), l'avatar marche vers la cible et la suit ; à portée
et en ligne de vue, il frappe à sa cadence. L'attaque s'arrête : `RpgTargetSelect(0)` ou autre
cible, marche manuelle (`MoveAvatar`), mort de la cible ou du joueur, cible sur une case `Safe` ou
partie de la zone.

Cadence : `AttackIntervalMs × 100 / Vitesse%` (`Vortex:Combat:PlayerAttackIntervalMs`, 2000), au
moins `MinAttackIntervalMs` (1000).

### 6.2 La chaîne de coup

Un coup (joueur ou monstre) traverse des étapes déclarées, par priorité (§13) :

| Priorité | Étape | Effet |
|---|---|---|
| 100 | Esquive | la cible esquive selon son `DodgePercent` → résultat `miss`, 0 dégât, la chaîne s'arrête |
| 200 | Dégâts de base | joueur : `Power × tirage[0,85 ; 1,15]` ; monstre : tirage entre ses dégâts min et max |
| 300 | Critique | selon le `CritPercent` de l'attaquant : × 1,5 → résultat `crit` |
| 400 | Armure | `dégâts × 100 / (100 + armure de la cible)` (spec unifiée §5.2) |
| 900 | Minimum | au moins 1 si le coup touche |

Coefficients dans `Vortex:Combat` (§10). Un monstre en retour est invulnérable : le coup ne part pas.

### 6.3 État de combat, régénération, menace

- **État de combat :** entré en donnant ou en recevant un coup ; quitté 5 s
  (`CombatTimeoutMs`) après le dernier coup, si aucun monstre n'a le joueur dans sa table.
- **Régénération :** hors combat, PV et mana remontent de 2 % du maximum par seconde
  (`RegenPercentPerSecond`), appliqué par pas de 1 s ; en combat, rien.
- **Menace :** chaque point de dégâts infligé à un monstre ajoute `dégâts × ThreatMultiplier` de la
  classe (Guerrier × 3) à la table du monstre ; la table se vide à sa mort, à son retour, ou pour un
  joueur qui part, meurt ou devient fantôme. Le monstre vise le premier de sa table.

## 7. PV et mana du personnage

- `player_characters` gagne `current_health`, `current_mana` (null = plein) et `vitals_at`
  (instant de la dernière écriture) ; `InitialCreate` est régénéré et un script `scripts/sql/`
  migre la base du propriétaire.
- `PlayerCharacterGrain` les tient et les persiste avec son écriture différée existante. Hors zone,
  la régénération est calculée à la lecture d'après `vitals_at` (au même taux que §6.3).
- Ils sont bornés au maximum courant à chaque changement de stats ; un personnage neuf est plein.
- La room les lit à l'entrée (avec les stats) et les rend à la sortie (une écriture) ; entre les
  deux, ce sont ceux de la room qui font foi.

## 8. Mort

### 8.1 Mort du joueur (spec unifiée §8.1)

À 0 PV : attaque arrêtée, retiré des tables de menace, posture couchée (`lay`), **fantôme 5 s**
(`GhostSeconds`) pendant lesquelles il ne peut ni frapper ni être visé ; `RpgDeath` lui est envoyé.
Puis il réapparaît à l'entrée `respawn` de la zone (sinon l'arrivée par défaut) à **50 % de ses PV**
(`RespawnHealthPercent`), et ses objets équipés perdent **10 % de leur durabilité maximale**
(`DeathWearPercent`, au plus jusqu'à 0) par une méthode d'usure du personnage. Un joueur ne meurt
pas sur une case `Safe` (les monstres ne l'y attaquent pas).

### 8.2 Mort d'un monstre

Il est retiré de la room. Chaque joueur **encore dans la zone** qui l'a touché reçoit l'XP entière
(`GainXpAsync`, source `monster:<id>`), ajustée par l'écart de niveaux : `+10 %` par niveau du
monstre au-dessus du joueur (jusqu'à `+50 %`), `−10 %` par niveau en dessous ; à 8 niveaux en
dessous ou plus, 0. `MonsterKilledEvent` est publié (monstre, niveau, participants) ; pas de loot en
M1a (M2). Il renaît après `RespawnSeconds` (§5.2).

## 9. Réseau

Types sur le fil comme la spec F3a §6 ; ids dans les régions `Vortex-specific` de `Headers.cs`
(`// RPG (spec §3.7)`). Les messages vers la zone sont **groupés : un par tick de combat**, envoyés
seulement si le tick a produit quelque chose (spec unifiée §3.7).

### 9.1 Réutilisés

- **`Users`** : un monstre est un avatar de type **pet (2)** : `WebId = −ObjectId`, nom = l'id de
  contenu (le client traduit `<id>.name`), figure de pet, niveau de pet = niveau du monstre, sans
  propriétaire. Déplacements par `UserUpdate`, disparition par `UserRemove`.
- **`RpgTargetSelect` (9010)** : sur un monstre, cible + attaque ; sur un joueur, cible seulement.
  **`RpgTarget` (9009)** répond pour un monstre : sorte `monster`, nom = l'id, niveau, classe vide.
- La posture `lay` du fantôme passe par `UserUpdate`.

### 9.2 Nouveaux (serveur → client)

| Id | Message | À qui | Champs, dans l'ordre |
|---|---|---|---|
| 9012 | `RpgCombatEvents` | la zone | count × (attackerId (objet), targetId (objet), amount, outcome (`hit`, `crit`, `miss`), health (après le coup), maxHealth, killed (booléen)) |
| 9013 | `RpgVitals` | la zone | count × (objectId, health, maxHealth) — combattants changés sans coup (régénération, naissance, réapparition, retour) ; complet dans la rafale d'entrée en zone |
| 9014 | `RpgSelfVitals` | le joueur | health, maxHealth, mana, maxMana, inCombat (booléen) — à l'entrée et à chaque changement |
| 9015 | `RpgDeath` | le joueur | respawnSeconds |
| 9016 | `RpgCombatResult` | le joueur | code : `safe_zone`, `unreachable`, `dead` — un clic d'attaque refusé |

### 9.3 Côté client (pour la session client)

Barres de vie au-dessus des monstres et des joueurs touchés ; chiffres de dégâts flottants
(critique en grand, « raté ») ; cadre de cible avec barre de PV ; orbes PV et mana du HUD ; écran
de fantôme et compte à rebours ; indicateur « en combat » ; nom des monstres traduit
(`<id>.name`) ; clic sur un monstre = `RpgTargetSelect`.

## 10. Configuration (`Vortex:Combat`)

| Clé | Défaut | Rôle |
|---|---|---|
| `TickMs` | 250 | rythme de l'étape de combat |
| `PlayerAttackIntervalMs` | 2000 | cadence de base du joueur |
| `MinAttackIntervalMs` | 1000 | plancher de cadence |
| `PlayerDamageMinFactor` / `MaxFactor` | 0,85 / 1,15 | tirage des dégâts du joueur |
| `CritMultiplier` | 1,5 | critique |
| `CombatTimeoutMs` | 5000 | sortie de combat |
| `RegenPercentPerSecond` | 2 | régénération hors combat |
| `GhostSeconds` | 5 | fantôme |
| `RespawnHealthPercent` | 50 | PV à la réapparition |
| `DeathWearPercent` | 10 | usure à la mort |
| `MaxPathSearchesPerTick` | 8 | budget de chemins par zone et par tick |
| `XpLevelStepPercent` / `XpMaxBonusPercent` / `XpCutoffLevels` | 10 / 50 / 8 | ajustement d'XP |

## 11. Erreurs et concurrence

- Tout se passe dans le tour de `RoomGrain` (non réentrant) : pas de verrou, pas de course entre
  coups d'un même tick.
- La room appelle le personnage (lecture à l'entrée, écriture à la sortie, XP, usure) ; le
  personnage n'appelle jamais la room, il passe par la présence sans attendre : aucun cycle.
- Les appels de fin de combat (XP, usure) vers plusieurs personnages partent ensemble
  (`Task.WhenAll`) ; un échec est journalisé, jamais rejoué en boucle, et n'arrête pas le tick.
- Une étape de tick qui lève est isolée par `RunTickStepAsync` ; le tick suivant repart.
- Un clic d'attaque refusé répond `RpgCombatResult` ; un paquet invalide (objet inconnu) vide la
  cible comme en F3b.

## 12. Observabilité

`RoomTickStepCompleted("combat")` ; compteurs : coups par résultat, monstres tués, morts de
joueurs, recherches de chemin des monstres (et refus de budget). Journal : naissance et mort de
monstre en debug, mort de joueur en information ; aucune ligne par coup.

## 13. Modularité et extensibilité

| Pièce | Contrat | Livrée en M1a | Ajoutée plus tard par |
|---|---|---|---|
| Comportement de monstre | `IMonsterBehavior` (clé) | `melee` | M1c : `ranged`, `caster`, boss |
| Étape de coup | `IHitStep` (priorité) | esquive, dégâts de base, critique, armure, minimum | M1b : bouclier, buffs, vulnérabilités |
| Sorte de contenu | `IContentKind` | `monster` | — |

- Les pièces sont déclarées par DI par le module qui les possède (`CombatModule`,
  `MonstersModule`) ; le registre garde la dernière déclarée pour une clé ; l'ordre des étapes est
  stable et journalisé.
- Événements : `MonsterKilledEvent` (quêtes « tuer X » en M3, loot en M2), `PlayerDiedEvent`,
  `CombatEnteredEvent`, `CombatLeftEvent`.
- Le runtime de zone est la seule couture avec les rooms : un futur grain de zone peut
  l'implémenter sans toucher au combat ni aux monstres (spec unifiée §3.3).

## 14. Tests

- Chaîne de coup, étape par étape, tirage déterministe : esquive, dégâts, critique, armure, minimum.
- XP et écart de niveaux (dont le seuil des 8 niveaux).
- IA sur un faux runtime de zone : repos → poursuite → combat → retour sur laisse ; jamais en case
  `Safe` ; invulnérable en retour ; plein de PV à l'arrivée.
- Apparitions : nombre, aire, niveau, réapparition, aucun doublon après réactivation, monstre
  inconnu ignoré.
- PV et mana : lus à l'entrée, rendus à la sortie, portés d'une zone à l'autre, bornés au maximum,
  régénération hors zone d'après `vitals_at`.
- Mort du joueur (fantôme, réapparition à 50 %, usure) et du monstre (XP à chaque participant
  présent, `MonsterKilledEvent`).
- Fil : serializers des cinq messages, `Users` d'un pet.
- Extensibilité : un comportement et une étape de coup déclarés par le test se branchent.
- Contenu : schéma `monster`, contrôles d'import.

## 15. Critères d'acceptation

- `driver.mjs combat` sur une zone de preuve avec une apparition de loups :
  - les loups apparaissent (`Users`, type pet) ;
  - A clique un loup : `RpgTarget` (monster), l'avatar s'approche, `RpgCombatEvents` montre ses
    coups, le loup meurt, A gagne de l'XP (`RpgCharacterProgress`) ;
  - un loup agressif engage A qui s'approche ;
  - un clic depuis une case `Safe` → `safe_zone` ;
  - A s'approche d'un monstre de preuve beaucoup trop fort (`monster.proof.brute`, contenu de
    preuve poussé par le driver) et meurt → `RpgDeath`, puis réapparition à 50 % ;
  - A change de zone : `RpgSelfVitals` donne les mêmes PV.
- La porte de qualité est verte.
