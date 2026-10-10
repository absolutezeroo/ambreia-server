# RPG — spec fille M1c « Monstres avancés »

> **Statut :** design approuvé par le propriétaire le 2026-10-10, section par section ·
> **Date :** 2026-10-10 · **Langue :** français
> **Dépôt :** `vortex-rpg-emulator`. Troisième et dernière tranche du jalon M1 de la spec unifiée
> `docs/superpowers/specs/2026-10-06-vortex-rpg-design.md` (§7.3 monstres, §7.5 boss). S'appuie sur
> M1a (`2026-10-09-rpg-combat-m1a-design.md` : zone de combat, monstres `melee`, apparitions, laisse)
> et M1b (`2026-10-09-rpg-skills-m1b-design.md` : compétences, incantation, statuts).

---

## 1. But

Les monstres se battent comme des personnages : certains tirent de loin, d'autres lancent des
sorts, un élite est plus coriace et a ses propres compétences, et un boss change de comportement
selon ses PV, annonce ses attaques par des zones au sol et se remet à zéro quand le groupe tombe ou
s'en va.

**Critère de réussite.** Dans l'arène de preuve, un joueur voit le boss incanter un cône dessiné au
sol ; sortir des cases avant l'impact évite les dégâts. À 66 % de PV, le boss crie sa ligne de
phase, devient enragé et invoque deux loups. Quand le joueur quitte l'arène, le boss rentre, ses
PV remontent, les loups disparaissent. Les tests prouvent chaque forme, chaque comportement et chaque
règle de phase.

## 2. Décisions — APPROUVÉ (conversation du 2026-10-10)

1. **Périmètre :** M1c complet en une tranche : comportements `ranged` et `caster`, monstres qui
   lancent des compétences, rang élite, boss à phases, télégraphes au sol, remise à zéro du boss.
2. **Sorts des monstres :** ce sont des **compétences M1b** (`content/skill`). Un monstre liste des
   règles d'usage (priorité, conditions) ; une compétence sans classe est réservée aux monstres.
3. **Télégraphes :** **cercle, ligne, cône**. Le télégraphe est un champ de la compétence ; la zone
   est figée au début de l'incantation, l'impact touche qui y est encore.
4. **Élite :** un **rang** dans la définition du monstre (`normal`, `elite`, `boss`), qui applique
   des multiplicateurs configurables ; un élite est un monstre de contenu à part entière.
5. **Phases de boss :** une phase peut changer les **compétences**, poser un **statut** (stats),
   **invoquer des renforts** et faire **crier une ligne** localisée.
6. **Architecture (approche A) :** **une seule machine d'incantation** pour joueurs et monstres
   (`CastSystem` ouvert à tout lanceur) ; les comportements partagent une base ; le boss est une
   **couche de phases**, pas un comportement.

**Écarts relevés en écrivant la spec** (à confirmer à la relecture) :
- Les immunités de rang portent sur les types `stun` et `slow` : le type `root` évoqué en
  conversation n'existe pas (la marche bloquée vient de l'étourdissement, M1b §7.2).
- Une compétence de monstre peut cibler `self` (le hurlement de l'élite est un buff sur lui-même) ;
  la section 5 de la conversation l'interdisait par erreur.

## 3. Périmètre

**Dans M1c.** Champs `rank`, `skills`, `phases` du monstre ; `telegraph` de la compétence ;
compétences sans classe ; comportements `ranged` et `caster` sur une base commune avec `melee` ;
sélecteur de compétence ; incantation de monstre par `CastSystem` ; télégraphes cercle, ligne,
cône ; multiplicateurs et immunités de rang ; phases ; renforts ; remise à zéro complète ; paquets
9025 et 9026, `rank` dans 9009, résultat `immune` ; contenu de démonstration ; preuve
`driver.mjs boss`.

**Hors M1c.** Loot (M2, spec Objets) et loot lié du boss ; recul d'un tireur (kiting) ; patrouilles
sur chemin ; provocation ; boss de monde ; arène fermée pendant le combat ; groupes et partage
d'XP (M4) ; compétence télégraphiée de joueur en contenu (la règle la permet, aucune n'est livrée).

## 4. Architecture

### 4.1 Projets touchés

| Projet | Ce qu'il gagne |
|---|---|
| `Vortex.Rpg` | champs de contenu (`MonsterDefinition`, `SkillDefinition`, records de règle, de phase, de télégraphe), validation dans `MonsterKind` et `SkillKind` |
| `Vortex.Rpg.Combat` | `CastRequest` désigne son lanceur par id d'objet ; `TelegraphShape` (calcul des cases) ; tir de base à distance avec ligne de vue |
| `Vortex.Rpg.Skills` | `CastSystem` pour tout lanceur ; télégraphe (cases figées, impact par cases) ; immunités dans `StatusApplier` ; statut sans fin (phase) |
| `Vortex.Rpg.Monsters` | `MonsterBehavior` (base), `MeleeBehavior`, `RangedBehavior`, `CasterBehavior` ; `MonsterSkillSelector` ; `BossPhases` ; renforts dans `MonsterSpawner` ; rang à l'apparition ; `MonstersConfig` |
| `Vortex.Protocol`, `Vortex.Revisions` | `RpgTelegraphsMessageComposer` (9025), `RpgMonsterYellMessageComposer` (9026), `rank` dans `RpgTargetMessageComposer` |
| `Vortex.Rooms` | le descripteur de cible d'un monstre porte son rang |

Aucune table, aucune colonne : rien n'est persisté par M1c.

### 4.2 Une machine d'incantation

`CastRequest` désigne son lanceur par **id d'objet**. Le handler joueur (9013) continue d'envoyer
son paquet ; la room traduit le joueur en id d'objet comme aujourd'hui. Les contrôles de M1b §6.1
restent dans leur ordre ; ceux qui ne concernent qu'un joueur sont sautés pour un monstre :

| Contrôle (M1b §6.1) | Joueur | Monstre |
|---|---|---|
| compétence connue, dans la barre | oui | non : la compétence vient de ses règles |
| vivant, pas étourdi, pas déjà en incantation | oui | oui |
| délai global, cooldown | oui | oui (cooldowns par monstre, même délai global) |
| mana | oui | non : un monstre n'en a pas |
| cible valide, vivante, hostile ou alliée selon le mode | oui | oui (hostile = joueur, allié = monstre) |
| portée et ligne de vue | oui, avec approche | oui, **sans approche** : le comportement place le monstre |
| case sûre | oui | oui : un monstre ne vise jamais un joueur sur une case sûre |

Un échec de monstre ne renvoie rien à un socket : le sélecteur essaiera autre chose au tick
suivant. La métrique `SkillCast(skillId, outcome)` compte les deux.

Annulations d'un monstre : mort, étourdissement, départ en retour (laisse ou table vide), remise à
zéro de boss, désactivation de zone. Comme un joueur (M1b décision 6), recevoir des coups
n'interrompt pas ; l'effet `interrupt` d'un joueur interrompt un monstre.

### 4.3 Comportements

`MeleeBehavior` (M1a) devient la base abstraite **`MonsterBehavior`**, qui garde le socle : laisse,
retour invulnérable, cases sûres, acquisition, poursuite du premier de la table de menace,
errance. Elle laisse deux décisions à ses dérivés :

- **`DesiredRange(brain, target)`** : à quelle distance se tenir de la cible.
- **`Act(brain, target)`** : quoi faire à portée — par défaut, essayer le sélecteur (§6), sinon
  laisser le tir de base de la zone de combat (M1a).

| Clé | Distance voulue | À portée |
|---|---|---|
| `melee` | `attackRange` | sélecteur, sinon tir de base |
| `ranged` | `attackRange` (ligne de vue exigée) | immobile `RangedAimMs` (250 ms) avant le premier tir après un déplacement ; puis sélecteur, sinon tir de base |
| `caster` | portée de la compétence que le sélecteur choisirait, sinon `attackRange` | sélecteur d'abord ; tir de base faible sinon |

Un monstre qui incante ne bouge pas (sauf `moveAllowed`). Chaque comportement est déclaré par DI
sous sa clé ; le dernier déclaré pour une clé gagne (M1a).

Le tir de base d'un monstre dont `attackRange` > 1 exige la ligne de vue ; le projectile est
cosmétique : l'impact arrive avec le coup, le client dessine le `vfx`.

## 5. Contenu

### 5.1 Le monstre (`content/monster/*.json`)

Champs ajoutés, tous **facultatifs avec défaut** (le contenu stocké reste valide, AGENTS.md) :

| Champ | Défaut | Sens |
|---|---|---|
| `rank` | `normal` | `normal`, `elite` ou `boss` (§7.1) |
| `skills` | `[]` | règles d'usage (§6.1), hors phases |
| `phases` | `[]` | phases déclenchées par les PV (§7.2) |

`behavior` accepte en plus `ranged` et `caster`.

```json
{
  "id": "monster.grove.guardian",
  "kind": "monster",
  "figure": "15 0 FFFFFF",
  "behavior": "melee",
  "rank": "boss",
  "skills": [{ "skill": "skill.monster.sweep", "priority": 10 }],
  "phases": [
    {
      "atHealthPercent": 66,
      "skills": [
        { "skill": "skill.monster.root_lash", "priority": 20 },
        { "skill": "skill.monster.sweep", "priority": 10 }
      ],
      "status": "status.enraged",
      "summon": { "monster": "monster.forest.wolf", "count": 2 },
      "yell": "monster.grove.guardian.phase2"
    }
  ]
}
```

### 5.2 Règle d'usage

| Champ | Défaut | Sens |
|---|---|---|
| `skill` | requis | id d'une compétence |
| `priority` | `0` | plus grand d'abord ; à égalité, l'ordre du fichier |
| `minRange` | `0` | distance minimale à la cible (un tireur qui garde un coup pour le loin) |
| `belowHealthPercent` | `100` | ne sert que sous ce % de PV : ceux du lanceur, ou de l'allié choisi pour une compétence `ally` |
| `chance` | `100` | % de chance de l'essayer quand elle est disponible (1–100) |

### 5.3 La compétence (`content/skill/*.json`)

- **`classes` peut être vide** : compétence réservée aux monstres ; elle n'est ni apprise ni
  plaçable dans une barre.
- **`telegraph`** (facultatif) : `{ "shape": "circle" | "line" | "cone", "size": 3, "width": 1,
  "angle": 90 }`. Le délai avant impact est `castMs` (une compétence télégraphiée est incantée).

### 5.4 Validation (à l'import ; le pack est refusé)

- `behavior` qu'aucun module ne déclare ; `rank` inconnu.
- Règle : compétence inconnue ; compétence dont un effet n'a pas de règle ; `chance` hors 1–100 ;
  `belowHealthPercent` hors 1–100 ; `minRange` négatif ou au-delà de la portée de la compétence.
- Phase : `atHealthPercent` hors 1–99 ou en double ; statut inconnu ; renfort inconnu, de rang
  `boss`, `count` hors 1–8, `level` hors 1–`MaxLevel` ; clé de cri absente des textes français
  (avertissement, comme les autres textes).
- Télégraphe : `size` hors 1–10 ; `width` pair ou hors 1–5 ; `angle` hors 30–180 ; sur une
  compétence sans `castMs` ou canalisée.
- Une compétence avec `classes` vide reste soumise aux règles de M1b §5.4 hors celles des classes.

Avertissement (pas de refus) : un `elite` ou un `boss` sans aucune compétence.

## 6. Le sélecteur de compétence

### 6.1 Choix

À chaque tick de combat, un monstre en état `Fight`, pas en incantation, pas étourdi, appelle
**`MonsterSkillSelector`**. La liste vient de la phase courante, sinon de `skills`. Les règles sont
essayées par priorité décroissante ; la première qui passe est lancée :

1. compétence hors cooldown et délai global écoulé ;
2. cible choisie selon le mode (§6.2) et dans la portée, `minRange` respecté, ligne de vue ;
3. condition de PV (`belowHealthPercent`) remplie ;
4. tirage `chance`.

Aucune ne passe : le tir de base de la zone de combat suit son cours (M1a).

### 6.2 Cible par mode

| Mode (M1b §6.6) | Cible d'un monstre |
|---|---|
| `enemy` | le premier de sa table de menace |
| `ally` | l'allié monstre le plus blessé en portée (en % de PV), lui compris |
| `self`, `around_caster` | lui-même |
| `ground`, `around_target` | la case ou le premier de sa table de menace |

## 7. Élite, boss, phases

### 7.1 Rang

À l'apparition, les multiplicateurs du rang (`Vortex:Monsters:Ranks`, §10) s'appliquent aux stats
calculées pour le niveau : PV, dégâts min et max, XP. Un rang absent de la configuration vaut ×1.

Chaque rang porte une liste d'**immunités** par type de statut (défaut : `boss` → `stun`, `slow`).
Un statut d'un type immunisé n'est pas posé ; le résultat est `immune` (paquets 9012/9024), le
client l'affiche en texte flottant. L'effet `interrupt` n'est pas un statut : un boss reste
interruptible.

### 7.2 Phases

Après chaque coup subi, le pourcentage de PV est comparé aux seuils des phases **non encore
jouées**, de la plus haute à la plus basse ; chaque seuil franchi joue sa phase, dans l'ordre (deux
seuils franchis d'un coup jouent les deux). Entrer dans une phase :

1. remplace la liste de compétences (celle de la phase ; absente : la liste précédente reste) ;
2. retire le statut de la phase précédente et pose le sien, **sans fin** tant que la phase dure ;
3. fait apparaître les renforts (§7.3) ;
4. crie la ligne de phase (paquet 9026).

`LiveMonster` retient l'index de la dernière phase jouée ; la métrique `BossPhase(monsterId, phase)`
la compte.

### 7.3 Renforts

- Apparaissent sur des cases libres, non sûres, à 1 ou 2 cases du boss (moins s'il n'y a pas de
  place ; aucun si aucune case : avertissement au journal).
- Niveau : `level` de la phase, sinon celui du boss. Ils reçoivent la table de menace du boss (même
  ordre) et attaquent aussitôt.
- Rattachés au boss : hors compte du point d'apparition, ils ne réapparaissent jamais.
- Tués : XP normale. À la **remise à zéro** du boss : ils disparaissent sans XP. À la **mort** du
  boss : ils restent, avec leur propre laisse mesurée depuis leur case d'apparition.
- Plafond `MaxSummonsPerMonster` (16) renforts vivants par monstre ; au-delà, l'invocation est
  ignorée, avertissement au journal.

### 7.4 Remise à zéro

Déclenchée par le chemin de M1a : laisse franchie, ou table de menace vide (tous morts, sortis, ou
sur une case sûre). En plus du retour invulnérable et des PV pleins, pour tout monstre : cooldowns
vidés, incantation et télégraphe annulés. Pour un monstre à phases : index de phase remis à zéro,
statut de phase retiré, liste de compétences d'origine, renforts disparus. Métrique
`BossReset(monsterId)`.

Une zone vidée de ses joueurs fait déjà disparaître ses monstres (M1a) ; le boss revient entier à la
réactivation, après son délai s'il avait été tué.

## 8. Télégraphes

### 8.1 Les cases

Calculées **une fois, au début de l'incantation**, par `TelegraphShape` (pur, testé) :

| Forme | Origine | Cases |
|---|---|---|
| `circle` | la case de la cible ou du point visé ; le lanceur pour `self` / `around_caster` | `dx² + dy² ≤ size²` |
| `line` | le lanceur, vers la cible ou le point (8 directions) | `size` cases devant lui, élargies de `(width − 1) / 2` de chaque côté |
| `cone` | le lanceur, vers la cible ou le point (angle continu) | distance euclidienne 1–`size`, écart d'angle ≤ `angle / 2` |

Cible sur la case du lanceur : la direction est son orientation. Les cases hors carte ou non
marchables sont retirées. Le lanceur ne se tourne plus jusqu'à l'impact.

### 8.2 L'impact

À la fin de l'incantation, la compétence touche les combattants **hostiles au lanceur** (alliés
pour une compétence de soin) qui se tiennent **encore** sur une des cases, jusqu'à `maxTargets`,
les plus proches de l'origine d'abord. La cible initiale ne compte plus : on évite en sortant. Les
effets sont ceux de la compétence (M1b §5.2) ; l'impact est annoncé par 9024 comme les autres.

Une incantation annulée (mort, étourdissement, retour, remise à zéro, zone désactivée) n'a pas
d'impact ; son `RpgCastEnd` efface le télégraphe côté client.

## 9. Réseau

### 9.1 Inchangé

Une incantation de monstre utilise `RpgCastStart` (9020), `RpgCastEnd` (9021), `RpgSkillEvents`
(9024), `RpgStatuses` (9023) : tous désignent le lanceur par id d'objet.

### 9.2 Serveur → client (nouveaux)

| Id | Composer | Contenu | Quand |
|---|---|---|---|
| 9025 | `RpgTelegraphsMessageComposer` | liste de `{ casterObjectId, skillId, shape, delayMs, cells: [(x, y)] }` | au début d'une incantation télégraphiée, regroupé par tick, à toute la room ; à l'entrée dans la room, les télégraphes en cours avec leur délai restant |
| 9026 | `RpgMonsterYellMessageComposer` | `objectId`, `textKey` | entrée de phase, à toute la room |

Le serveur envoie les **cases**, pas seulement la forme : il fait autorité et le client n'a pas de
géométrie à refaire.

### 9.3 Enrichi

- **9009 `RpgTarget`** : `rank` (`normal`, `elite`, `boss`) ajouté **en dernier champ** ; un
  client pas encore à jour lit les champs qu'il connaît et ignore la fin. Joueur : `normal`.
- `outcome` (9012, 9024) gagne la valeur `immune`.

### 9.4 Côté client (pour la session client)

Cases du télégraphe en surbrillance (rouge pour un lanceur hostile) jusqu'au `RpgCastEnd` du
lanceur, avec le délai en remplissage ; bulle et ligne de journal pour 9026 (texte par
`/api/public/content/texts/fr`) ; cadre de cible doré (`elite`) ou de boss ; texte flottant
`Immunisé`. Les compétences de monstre arrivent par `/api/public/content/definitions/skill`
(`classes` vide, `telegraph`).

## 10. Configuration (`Vortex:Monsters`)

| Clé | Défaut | Sens |
|---|---|---|
| `Ranks:elite` | PV ×3, dégâts ×1,5, XP ×3, immunités `[]` | multiplicateurs et immunités |
| `Ranks:boss` | PV ×10, dégâts ×2, XP ×10, immunités `["stun", "slow"]` | idem |
| `MaxSummonsPerMonster` | 16 | renforts vivants par monstre |
| `RangedAimMs` | 250 | immobilité d'un tireur avant de tirer après un déplacement |

Lue au démarrage ; une valeur invalide (multiplicateur ≤ 0, type de statut inconnu) refuse le
démarrage avec un message qui nomme la clé.

## 11. Erreurs et concurrence

- Tout se passe dans le tick de la zone de combat, sur le grain de la room : aucun verrou.
- Compétence de monstre qui échoue : rien n'est envoyé, le monstre continue.
- Lanceur mort ou parti : incantation et télégraphe annulés, renforts traités selon §7.3.
- Contenu rechargé en plein combat : un monstre garde la définition de son apparition jusqu'à sa
  mort ou la désactivation de la zone (comme M1a) ; une compétence retirée est sautée par le
  sélecteur.

## 12. Observabilité

`SkillCast(skillId, outcome)` couvre les monstres ; nouvelles métriques `BossPhase(monsterId,
phase)` et `BossReset(monsterId)` (ids de contenu, ensemble borné). Journal : avertissement pour un
renfort refusé (plafond, pas de case), information pour une phase et une remise à zéro de boss.

## 13. Modularité

Un comportement est une classe déclarée par DI sous sa clé (`ranged`, `caster` s'ajoutent comme
`melee`) ; une forme de télégraphe est une valeur de `TelegraphShape` ; le rang est de la
configuration ; phases, règles et compétences sont du contenu. Le cœur de la zone de combat ne
nomme aucun comportement ni aucune compétence.

## 14. Tests

Chaque comportement a son test, écrit d'abord en rouge :

- **Sélecteur** : priorité, ordre à égalité, cooldown, délai global, portée et `minRange`, ligne de
  vue, `belowHealthPercent` (lanceur, allié), `chance`, cible par mode, rien de dispo → tir de base.
- **Incantation de monstre** : sans mana ; sans approche ; étourdissement qui interrompt ; coup reçu
  qui n'interrompt pas ; annulée au retour et à la mort ; métrique.
- **Télégraphe** : cases du cercle, de la ligne (8 directions, largeurs 1, 3, 5), du cône (8
  directions, 90°, 180°) ; cases hors carte retirées ; sortir avant l'impact évite ; entrer avant
  l'impact touche ; `maxTargets` par proximité ; annulation sans impact ; état d'entrée.
- **Rang** : multiplicateurs ; rang absent → ×1 ; immunité `stun` et `slow` → `immune` ;
  `interrupt` passe.
- **Phases** : seuil franchi ; deux seuils d'un coup ; statut sans fin remplacé ; liste de
  compétences remplacée ; cri ; métrique.
- **Renforts** : placement, menace héritée, plafond, pas de case, disparition à la remise à zéro
  sans XP, survie à la mort du boss.
- **Remise à zéro** complète.
- **`ranged`** : immobile `RangedAimMs` avant le tir ; ligne de vue exigée. **`caster`** : se place
  à la portée de sa compétence.
- **Validation** : chaque règle de §5.4.
- **Paquets** : forme de 9025, 9026, `rank` en fin de 9009.
- **Configuration** : valeur invalide refusée.

## 15. Contenu de démonstration

| Id | Rang | Comportement | Ce qu'il montre |
|---|---|---|---|
| `monster.goblin.archer` | normal | `ranged` (portée 5) | tir à distance, ligne de vue |
| `monster.goblin.shaman` | normal | `caster` | `skill.monster.mend` (soin `ally` sous 50 %), `skill.monster.frost_bolt` (`status.frozen`) |
| `monster.forest.alpha_wolf` | elite | `melee` | `skill.monster.howl` (`self`, `status.howling` : dégâts +20 %, 8 s) |
| `monster.grove.guardian` | boss | `melee` | 3 phases : `skill.monster.sweep` (cône 3, 90°, 1,5 s) ; à 66 % `status.enraged` (dégâts +30 %), 2 loups, `skill.monster.root_lash` (ligne 5, largeur 1, 1,5 s), cri ; à 33 % `skill.monster.quake` (cercle 3 sur la cible, 2 s), cri |

Statuts nouveaux : `status.howling`, `status.enraged`. Noms et cris dans `content/texts/fr.txt`.

## 16. Preuve et critères d'acceptation

`driver.mjs boss`, sur l'instance alt :

1. crédite d'XP le mage de preuve par `/api/admin/characters/{id}/xp` (niveau 6 : ses trois
   compétences, dont la zone au sol et les statuts que le client n'a pas encore vus en jeu) ;
2. publie l'arène `proof_boss` (boss, et à l'entrée un archer et un chaman) par l'éditeur, y entre ;
3. attend un `RpgTelegraphs` du boss puis l'impact (`RpgSkillEvents`) ;
4. frappe jusqu'à 66 % : `RpgMonsterYell`, statut `status.enraged` du boss (`RpgStatuses`), deux
   loups de plus dans la room ;
5. sort de l'arène : le boss revient à PV pleins (`RpgVitals`), les loups disparaissent.

Critères : porte complète verte ; `driver.mjs boss`, `skills`, `combat` et `smoke` verts en direct ;
contrat réseau transmis aux sessions client.
