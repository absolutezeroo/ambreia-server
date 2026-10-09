# RPG — spec fille M1b « Compétences et effets de statut »

> **Statut :** livrée le 2026-10-10 (plan `docs/superpowers/plans/2026-10-09-rpg-skills-m1b.md`,
> preuve `driver.mjs skills`) ; design approuvé par le propriétaire le 2026-10-09, section par section ·
> **Date :** 2026-10-09 · **Langue :** français
> **Dépôt :** `vortex-rpg-emulator`. Deuxième tranche du jalon M1 de la spec unifiée
> `docs/superpowers/specs/2026-10-06-vortex-rpg-design.md` (§5.3 classes, §6 compétences, §7.1–7.2
> combat et menace). S'appuie sur M1a (`2026-10-09-rpg-combat-m1a-design.md`) : zone de combat,
> chaîne de coup, PV et mana du personnage. M1c (monstres avancés) suivra.

---

## 1. But

Un joueur d'une vraie classe remplit sa barre de 6 compétences et les lance : dégâts, soins,
incantations, canalisations, zones, sol, et des effets de statut qui durent. Les compétences
s'apprennent seules en montant de niveau.

**Critère de réussite.** Un mage de niveau 1 lance Trait de feu sur un loup : l'incantation de 2 s
s'affiche, le loup prend les dégâts, le délai global puis le cooldown s'affichent ; relancer pendant
le délai global est refusé ; marcher pendant l'incantation l'annule. Les tests prouvent chaque
compétence de départ, chaque type de statut et chaque règle de cumul.

## 2. Décisions — APPROUVÉ (conversation du 2026-10-09)

1. **Périmètre :** M1b complet en une tranche : compétences instantanées, incantées, canalisées,
   délai global, cooldowns, mana, barre de 6 persistée, les 7 types d'effets de statut avec cumul,
   3 compétences par classe en contenu.
2. **Acquisition :** **par niveau.** Chaque compétence déclare le niveau où elle est apprise ; rien à
   dépenser, rien à persister hormis la barre. « Sans classe » n'a aucune compétence.
3. **Ciblage :** les 6 modes, **sol compris** : soi, allié, ennemi, sol, autour du lanceur, autour
   de la cible.
4. **Contenu :** 3 compétences par classe (niveaux 1, 3, 6), choisies pour couvrir chaque mécanique
   (§16) ; les suivantes s'ajoutent en JSON.
5. **Architecture :** un projet **`Vortex.Rpg.Skills`** (couche Platform) branché sur la zone de
   combat par des points d'extension ; types d'effet déclarés par DI sous leur clé ; `skill` et
   `status` sont des sortes de contenu.
6. **Coups et incantations :** recevoir des coups **n'interrompt pas** une incantation (pas de
   recul) ; seuls l'interruption, l'étourdissement, la mort, la sortie de zone, la perte de la cible
   et le déplacement l'annulent.
7. **Statuts persistants :** rendus au personnage avec les PV à la sortie de zone, gardés **en
   mémoire** du grain du personnage (perdus à la déconnexion), jamais en base.

## 3. Périmètre

**Dans M1b.** Sortes de contenu `skill` et `status` ; compétences connues par classe et niveau ;
barre de 6 persistée ; lancer avec contrôles, approche, instantanée, incantée, canalisée ;
annulations ; délai global, cooldowns, mana ; ciblage 6 modes avec rayon et cibles max ; effets
`damage`, `heal`, `apply_status`, `interrupt`, `dispel`, `threat` ; statuts buff, debuff, dégâts et
soins sur la durée, étourdissement, ralentissement, bouclier ; cumul remplacer, rafraîchir,
cumuler, ignorer ; menace des soins ; marche ralentie par avatar dans la room ; paquets ;
12 compétences et leurs statuts ; preuve `driver.mjs skills`.

**Hors M1b.** Monstres qui lancent des sorts, élites, boss, télégraphes (M1c) ; passifs (spec
unifiée §6.6, PROPOSÉ) ; provocation (décision ouverte 19) ; consommables dans la barre (le format
d'action est prêt, §9) ; statuts persistants en base ; points de compétence ; spécialisations (le
champ existe, aucune compétence ne l'utilise).

## 4. Architecture

### 4.1 Projets

| Projet | Couche | Contient |
|---|---|---|
| `Vortex.Rpg` (cœur) | Core RPG | sortes de contenu `skill` et `status` (`Content/Kinds`, `Definitions`) ; méthodes de lancer de `IZoneSimulation` |
| `Vortex.Rpg.Combat` | Platform | points d'extension de la zone (§4.2) ; `Combatant` gagne des stats de base et effectives |
| **`Vortex.Rpg.Skills`** (nouveau) | Platform | lancers, incantations, statuts, types d'effet, étapes de coup des statuts, `SkillsModule` ; référence `Vortex.Rpg.Combat` |
| `Vortex.Rpg.Monsters` | Platform | l'IA respecte l'étourdissement (rien de plus) |
| `Vortex.Rpg.Characters` | Platform | compétences connues, barre, statuts persistants en mémoire |
| `Vortex.Rooms` | Platform | marche ralentie par avatar, avatar immobilisé ; handlers des nouveaux paquets |

`Vortex.Rooms` ne référence toujours que le cœur `Vortex.Rpg`. `Vortex.Rpg.Skills` est ajouté à
`PLATFORM` dans `check-architecture-walls.mjs` et câblé par `Vortex.Main`.

### 4.2 Points d'extension de la zone de combat

`CombatZone` ne nomme ni compétence ni statut. Elle expose :

- **`ICombatSystem`** (ordre, déclaré par DI) : appelé à chaque tick par la zone, avec la zone et
  l'instant ; prévenu quand un combattant entre, meurt ou part. Les compétences en déclarent deux :
  `StatusSystem` et `CastSystem`.
- **Parties de combattant** : un `Combatant` porte des parties typées qu'un module attache
  (`Get<T>`/`Set<T>`) — les statuts et l'incantation en cours y vivent ; elles disparaissent avec
  le combattant.
- **Stats effectives** : `Combatant` garde ses stats de base (profil du personnage, bloc du monstre)
  et des stats effectives, recalculées quand une source de modificateurs le demande
  (`IStatModifierSource`, déclaré par DI).
- **Opérations partagées** : infliger des dégâts (participants, menace, dernier coup, entrée en
  combat, mort au tick), soigner, ajouter de la menace, mettre un message en file pour la zone ou
  pour un joueur. Les dégâts d'une compétence et l'attaque de base passent par le même chemin.
- **Étapes de coup** (M1a) : les compétences ajoutent les modificateurs de dégâts en % et le
  bouclier ; une compétence peut marquer un coup comme sien (source, pas d'esquive si elle le dit).

### 4.3 Le tick (250 ms)

Statuts (expirations, pulsations) → incantations et canalisations (fins, pulsations) et lancers
instantanés en attente → coups automatiques → morts → sorties de combat → régénération →
réapparitions → envois. Une compétence instantanée validée au tour d'un paquet s'applique au tick
suivant (spec unifiée §6.3).

### 4.4 Propriété de l'état

| État | Propriétaire | Persistance |
|---|---|---|
| Barre d'action | `PlayerCharacterGrain` | colonne JSON `action_bar` de `player_characters` |
| Compétences connues | déduites de la classe et du niveau | aucune |
| Incantation, cooldowns, délai global, statuts en zone | la zone (partie du combattant) | aucune |
| Statuts persistants hors zone | `PlayerCharacterGrain`, en mémoire | aucune |

Les cooldowns restent dans la zone : changer de zone les remet à zéro. Coût accepté en V1 ; la zone
les rendra au personnage comme les PV si l'équilibrage le demande.

## 5. Contenu

### 5.1 La sorte `skill` (`content/skill/*.json`)

| Champ | Type | Défaut | Sens |
|---|---|---|---|
| `id` | chaîne | — | stable, `skill.<classe>.<nom>` |
| `classes` | liste d'ids de classe | — | au moins une |
| `specializations` | liste | vide | vide : toutes |
| `level` | entier ≥ 1 | — | appris en atteignant ce niveau |
| `cost` | entier ≥ 0 | 0 | mana |
| `cooldownMs` | entier ≥ 0 | 0 | |
| `globalCooldown` | booléen | vrai | déclenche le délai global et y est soumise |
| `type` | `instant`, `cast`, `channel` | — | |
| `castMs` | entier > 0 | — | requis pour `cast` |
| `channelMs`, `pulseMs` | entiers > 0 | — | requis pour `channel` ; `pulseMs` ≤ `channelMs` |
| `range` | entier ≥ 0 | — | cases, distance de Chebyshev ; 0 pour `self` et `around_caster` |
| `target` | `self`, `ally`, `enemy`, `ground`, `around_caster`, `around_target` | — | §6.6 |
| `radius` | entier ≥ 0 | 0 | requis > 0 pour `ground`, `around_caster`, `around_target` |
| `maxTargets` | entier ≥ 1 | 10 | zones seulement |
| `moveAllowed` | booléen | faux | une incantation survit au déplacement |
| `unavoidable` | booléen | faux | pas de jet d'esquive |
| `effects` | liste d'effets (§5.2) | — | au moins un |
| `animation`, `vfx`, `sound` | chaînes | vides | clés traduites par le client |

### 5.2 Les effets

Un effet est un enregistrement plat `{ kind, base, coefficient, status, amount, threatPercent }`,
tous optionnels sauf `kind`. Le `kind` désigne un **type d'effet** (`ISkillEffect`) déclaré par DI
sous sa clé ; chaque type vérifie à l'import les champs qu'il exige.

| `kind` | Exige | Fait |
|---|---|---|
| `damage` | `base` ou `coefficient` | dégâts `base + coefficient × Puissance` par la chaîne de coup |
| `heal` | `base` ou `coefficient` | soin `base + coefficient × Puissance`, critique possible, borné au max |
| `apply_status` | `status` | applique le statut (§7) |
| `interrupt` | — | annule l'incantation ou la canalisation de la cible |
| `dispel` | `amount` | retire jusqu'à `amount` statuts dissipables (débuffs sur un allié, buffs sur un ennemi), les plus récents d'abord |
| `threat` | `threatPercent` | menace supplémentaire : % des dégâts de la même compétence sur cette cible |

La Puissance est celle du lanceur (stat principale de la classe ; pour un monstre, la moyenne de ses
dégâts). Les effets s'appliquent dans l'ordre de la liste à chaque cible ; une cible tuée par un
effet ne reçoit pas les suivants.

### 5.3 La sorte `status` (`content/status/*.json`)

| Champ | Type | Défaut | Sens |
|---|---|---|---|
| `id` | chaîne | — | `status.<nom>` |
| `type` | `buff`, `debuff`, `dot`, `hot`, `stun`, `slow`, `shield` | — | |
| `durationMs` | entier > 0 | — | |
| `periodMs` | entier > 0 | — | requis pour `dot` et `hot` |
| `stacking` | `replace`, `refresh`, `stack`, `ignore` | `refresh` | §7.1 |
| `maxStacks` | entier ≥ 2 | — | requis pour `stack` |
| `dispellable` | booléen | vrai | |
| `persistent` | booléen | faux | survit à la sortie de zone (§7.4) |
| `modifiers` | liste `{ stat, percent, flat }` | vide | `buff`, `debuff` ; `stat` : `strength`, `intelligence`, `agility`, `armor`, `critPercent`, `dodgePercent`, `speedPercent`, `damageDealtPercent`, `damageTakenPercent` |
| `base`, `coefficient` | nombres | 0 | montant par pulsation (`dot`, `hot`) ou absorption (`shield`), calculé une fois à l'application avec la Puissance du lanceur |
| `slowPercent` | entier 1–90 | — | requis pour `slow` |

### 5.4 Validation

L'import refuse le pack, avec un message par erreur : `kind` d'effet inconnu, champ exigé
manquant, statut inconnu dans `apply_status`, classe inconnue, champs requis par le type absents.
Les sortes ajoutent leur schéma (`UPDATE_CONTENT_SCHEMAS=1`). Un champ ajouté plus tard sera
optionnel avec un défaut (règle du registre).

## 6. Le lancer

### 6.1 Contrôles (spec unifiée §6.3)

Dans cet ordre, au tour du paquet ; le premier échec répond `RpgSkillResult(skill, code)` :

1. lanceur combattant de la zone, vivant (`dead`), pas fantôme (`dead`), pas étourdi (`stunned`),
   sans incantation ni canalisation en cours (`casting`) ;
2. compétence existante (`unknown_skill`) et connue de ce personnage (`not_learned`) ;
3. cible valide pour le mode (`invalid_target`, §6.6) ;
4. case sûre : une compétence qui inflige des dégâts ou un débuff ne part pas d'une case sûre et ne
   vise ni un combattant ni une case sûre (`safe_zone`) ; soins et buffs partout ;
5. portée et ligne de vue : sinon approche (§6.2) ;
6. cooldown (`cooldown`) ; délai global si la compétence y est soumise (`global_cooldown`) ;
   mana (`no_mana`).

### 6.2 Approche

Hors de portée ou hors de vue, l'avatar marche vers la cible ou la case (même runtime qu'en M1a,
`WalkNear`) ; le lancer attend. À chaque tick, en portée et en vue, il reprend aux contrôles 6
puis se résout. Il est abandonné, sans message, si le joueur marche à la main, lance autre chose ou
annule ; avec `unreachable` si aucun chemin n'existe ou si la cible meurt ou part. L'attaque
automatique en cours continue pendant l'approche d'un lancer instantané.

### 6.3 Résolution

| Type | Au lancer | Pendant | À la fin |
|---|---|---|---|
| `instant` | délai global, mana, cooldown ; effets au tick suivant | — | — |
| `cast` | délai global ; `RpgCastStart` | attaque auto en pause | portée et vue revérifiées (sinon `RpgCastEnd(failed)` + `RpgSkillResult`), mana revérifiée et prise, cooldown, effets, `RpgCastEnd(done)` |
| `channel` | délai global, mana, cooldown ; `RpgCastStart` | une pulsation d'effets tous les `pulseMs` (la première après `pulseMs`) ; attaque auto en pause | `RpgCastEnd(done)` |

Les zones (`ground`, `around_*`) choisissent leurs cibles à chaque application (fin d'incantation,
pulsation) : combattants vivants du bon camp dans le rayon, les plus proches du centre d'abord,
`maxTargets` au plus, ligne de vue depuis le centre.

### 6.4 Annulations

Une incantation ou une canalisation s'arrête, avec `RpgCastEnd(raison)` diffusé, sur :

| Cause | Raison |
|---|---|
| le joueur marche (sauf `moveAllowed`) ou envoie `RpgCastCancel` | `cancelled` |
| effet `interrupt`, étourdissement | `interrupted` |
| mort, sortie de zone | `cancelled` |
| cible morte, partie ou devenue invalide (cible unique) | `cancelled` |

Annulée, une incantation ne coûte rien et ne déclenche pas son cooldown ; le délai global déjà parti
reste. Une canalisation garde ce qu'elle a déjà appliqué. Recevoir des coups n'annule rien
(décision 6).

### 6.5 Délai global, cooldowns, mana

Délai global : `Vortex:Skills:GlobalCooldownMs` (1 000), partagé par toutes les compétences qui y
sont soumises, parti au lancer. Cooldown par compétence, parti à la résolution (fin d'incantation,
lancer instantané, début de canalisation). La mana n'est jamais négative ; elle se régénère comme
en M1a. Après chaque lancer résolu, le joueur reçoit `RpgCooldowns`.

### 6.6 Ciblage

| `target` | Paquet | Valide si | Centre de la zone |
|---|---|---|---|
| `self` | rien | toujours | — |
| `ally` | objet ciblé (0 : soi) | un joueur vivant de la zone, soi compris | — |
| `enemy` | objet ciblé | un monstre vivant, pas invulnérable | — |
| `ground` | case (x, y) | case dans la carte et marchable | la case |
| `around_caster` | rien | toujours | le lanceur |
| `around_target` | objet ciblé | comme `enemy` | la cible |

Les zones touchent les ennemis (monstres) pour une compétence offensive et les alliés (joueurs) pour
un soin ou un buff ; le camp est celui de son premier effet. Sans PvP, un joueur n'est jamais
l'ennemi d'un autre. La portée se mesure du lanceur à la cible ou à la case.

## 7. Effets de statut

### 7.1 Application et cumul

Appliquer un statut déjà présent sur la cible, venant du même lanceur ou d'un autre :

| `stacking` | Effet |
|---|---|
| `replace` | l'ancien est retiré, le nouveau posé (montants recalculés) |
| `refresh` | la durée repart, montants et cumuls inchangés |
| `stack` | un cumul de plus (au plus `maxStacks`), la durée repart ; montant × cumuls |
| `ignore` | rien ; l'ancien reste |

Au-delà de `Vortex:Skills:MaxStatusesPerCombatant` (32), le plus ancien est retiré. Un statut
appliqué à un monstre en fait entrer le lanceur en combat ; un débuff donne de la menace comme
1 point de dégâts.

### 7.2 Types

| Type | Effet |
|---|---|
| `buff`, `debuff` | modificateurs (§7.3) tant qu'il dure |
| `dot` | dégâts tous les `periodMs` (chaîne de coup sans esquive ni critique), menace et XP au lanceur |
| `hot` | soin tous les `periodMs`, menace des soins (§8) |
| `stun` | ni coup automatique, ni lancer, ni marche ; annule l'incantation (`interrupted`) ; un monstre étourdi ne pense pas (ni poursuite, ni retour) |
| `slow` | cadence d'attaque et vitesse de marche réduites de `slowPercent` (le plus fort s'applique s'il y en a plusieurs) |
| `shield` | absorbe les dégâts jusqu'à épuisement de son montant, puis disparaît |

### 7.3 Stats effectives

Stats effectives = base + Σ plats, puis × (1 + Σ % / 100), bornées (pas de valeur négative ;
critique et esquive bornés comme en M1a ; vitesse bornée à [50, 200]). Recalculées à chaque pose ou
retrait de statut et à chaque changement de stats du personnage (M1a §4.4). `damageDealtPercent` et
`damageTakenPercent` agissent dans la chaîne de coup (étape après le critique, avant l'armure) ; le
bouclier absorbe après le minimum.

### 7.4 Fin des statuts

Expiration ; dissipation ; mort (tous les statuts tombent) ; sortie de zone : les statuts non
persistants tombent, les persistants (`persistent`) sont rendus au personnage avec les PV et la mana
(même écriture, M1a §4.4) avec leur durée restante, gardés en mémoire du grain du personnage, puis
réappliqués à l'entrée de la zone suivante s'il leur reste du temps. Une déconnexion au-delà de la
grâce les perd. Aucun statut de départ n'est persistant (§16).

## 8. Menace

Dégâts d'une compétence ou d'un statut : comme l'attaque de base (montant × multiplicateur de la
classe), plus l'effet `threat`. Soin : `Vortex:Skills:HealThreatPercent` (50 %) du montant
réellement rendu, ajouté à chaque monstre dont la table de menace contient le joueur soigné. Buff,
bouclier : aucune menace.

## 9. Barre d'action et compétences connues

- **Compétences connues** : les compétences dont `classes` contient la classe du personnage (et
  `specializations` sa spécialisation, si renseigné) et dont `level` ≤ son niveau. Calculées par le
  personnage et par la zone depuis le contenu ; rien en base.
- **Barre** : `Vortex:Skills:ActionBarSize` (6) cases, chacune `skill:<id>` ou vide ; colonne
  JSON `action_bar` de `player_characters` (`InitialCreate` régénéré + script re-jouable
  `scripts/sql/`). Le format `item:<id>` est réservé aux consommables (plus tard).
- **Remplissage** : une compétence nouvellement apprise (gain de niveau) va dans la première case
  vide ; choisir une vraie classe vide la barre puis la remplit avec ses compétences connues, dans
  l'ordre de leur niveau.
- **`RpgActionBarSet`** : case hors bornes, compétence inconnue ou non connue → refus
  (`RpgSkillResult(code)`), barre inchangée ; une compétence déjà placée ailleurs y est déplacée.
- **`RpgSkills`** (compétences connues + barre) est poussé avec l'état du personnage, au gain de
  niveau, au choix de classe et après un `RpgActionBarSet`.

## 10. Réseau

Tous les paquets RPG restent dans la plage 9000+. Les chaînes sont des ids de contenu ou des codes.

### 10.1 Client → serveur

| Id | Paquet | Champs (ordre du fil) |
|---|---|---|
| 9013 | `RpgSkillCast` | `string skillId`, `int targetObjectId` (0 : aucun), `int x`, `int y` (−1 : aucun) |
| 9014 | `RpgActionBarSet` | `int slot`, `string actionId` (vide : vider) |
| 9015 | `RpgCastCancel` | — |

Handlers : la room (`IRoomCombat` côté serveur, pas de `DbContext`) transmet à la simulation ;
`RpgActionBarSet` va au personnage. Limite par intention du pipeline sur 9013.

### 10.2 Serveur → client

| Id | Paquet | Vers | Champs (ordre du fil) |
|---|---|---|---|
| 9018 | `RpgSkills` | joueur | `int n`, n × `string skillId` ; `int m`, m × `string actionId` (la barre, cases vides incluses) |
| 9019 | `RpgSkillResult` | joueur | `string skillId`, `string code` |
| 9020 | `RpgCastStart` | zone | `int casterObjectId`, `string skillId`, `int durationMs`, `bool channel`, `int targetObjectId`, `int x`, `int y` |
| 9021 | `RpgCastEnd` | zone | `int casterObjectId`, `string skillId`, `string reason` (`done`, `cancelled`, `interrupted`, `failed`) |
| 9022 | `RpgCooldowns` | joueur | `int globalRemainingMs`, `int n`, n × (`string skillId`, `int remainingMs`) |
| 9023 | `RpgStatuses` | zone | `int n`, n × (`int objectId`, `int k`, k × (`string statusId`, `int remainingMs`, `int stacks`)) — liste complète de chaque combattant changé |
| 9024 | `RpgSkillEvents` | zone | `int a`, a × impact (`int casterObjectId`, `string skillId`, `int targetObjectId`, `int x`, `int y`) ; `int b`, b × résultat (`int sourceObjectId`, `string sourceId`, `int targetObjectId`, `int amount`, `int absorbed`, `string outcome` (`hit`, `crit`, `dodge`, `heal`), `int health`, `int maxHealth`, `bool killed`) |

9020 à 9024 sont regroupés : au plus un message par sorte et par tick pour la zone (`RpgCastStart`
et `RpgCastEnd` aussi, en listes si plusieurs — leur forme ci-dessus est celle d'un élément,
précédée d'un compte). `RpgCombatEvents` (9012) garde son format : l'attaque de base y reste. La
rafale d'entrée ajoute `RpgStatuses` des combattants qui en portent, et `RpgSkills` part avec l'état
du personnage.

### 10.3 Côté client (pour la session client)

Barre de 6 sous le HUD (glisser une compétence connue sur une case → `RpgActionBarSet`) ; touches
1–6 → `RpgSkillCast` avec la cible courante ; curseur de case pour une compétence `ground` ;
barre d'incantation au-dessus de l'avatar (`RpgCastStart`/`RpgCastEnd`) ; icônes de statut sur le
cadre de cible et le cadre du joueur (`RpgStatuses`) ; recharge sur les cases (`RpgCooldowns`) ;
chiffres flottants et VFX (`RpgSkillEvents`) ; textes des codes de refus.

## 11. Configuration (`Vortex:Skills`)

| Clé | Défaut |
|---|---|
| `GlobalCooldownMs` | 1000 |
| `HealThreatPercent` | 50 |
| `MaxStatusesPerCombatant` | 32 |
| `ActionBarSize` | 6 |

Le tick reste celui de la room (`RoomConfig.CombatTickMs`).

## 12. Erreurs et concurrence

- Tout se passe dans le tour de la room ; aucun appel d'un grain n'est attendu pendant un lancer,
  sauf les écritures déjà prévues par M1a (PV à la sortie, XP à la mort).
- Le choix de classe et la barre sont écrits par le grain du personnage ; la zone apprend une
  nouvelle classe ou un nouveau niveau par le profil relayé (M1a §4.4, profil complet).
- Un paquet d'un joueur sans combattant (sans personnage) répond `dead` comme l'attaque, et rien
  d'autre.
- Un type d'effet ou un statut absent du registre au moment d'appliquer (pack remplacé) : effet
  ignoré, une alerte par id.

## 13. Observabilité

Compteurs : lancers par compétence et par issue (résolu, refusé avec code, annulé), statuts posés
par id, durée du tick de combat (déjà mesurée). Aucun journal par coup.

## 14. Modularité et extensibilité

| Pièce | Déclarée par | Exemple livré |
|---|---|---|
| Type d'effet | `ISkillEffect` (clé), DI de son module | `damage`, `heal`, `apply_status`, `interrupt`, `dispel`, `threat` |
| Système de zone | `ICombatSystem` (ordre) | statuts, incantations |
| Source de modificateurs de stats | `IStatModifierSource` | statuts |
| Étape de coup | `IHitStep` (priorité) | dégâts infligés et subis en %, bouclier |
| Sorte de contenu | `IContentKind` | `skill`, `status` |

Ajouter un effet (« repousser »), un type de statut ou une étape ne change ni `CombatZone` ni le cœur.

## 15. Tests

- **Lancer** : l'ordre exact des contrôles, un test par code de refus ; approche puis lancer ;
  approche abandonnée par une marche à la main.
- **Résolution** : instantanée au tick suivant ; incantation menée à terme, échouée hors de portée à
  la fin ; canalisation à pulsations ; attaque automatique en pause puis reprise.
- **Annulations** : chaque cause de §6.4, avec ce qui est pris ou non.
- **Délai global et cooldowns** : partagé, hors délai global (`globalCooldown: false`), cooldown à
  la résolution.
- **Ciblage** : chaque mode ; rayon, cibles max, plus proches d'abord ; ligne de vue depuis le
  centre ; case sûre.
- **Statuts** : chaque type, chaque règle de cumul, expiration, mort, sortie de zone, persistant
  rendu puis réappliqué ; bouclier qui absorbe puis disparaît ; étourdissement respecté par l'IA des
  monstres ; ralentissement sur la cadence ; stats effectives et bornes.
- **Menace** : soin réparti, effet `threat`, débuff.
- **Contenu** : chaque refus de §5.4 ; les 12 compétences et leurs statuts du dépôt passent l'import.
- **Barre** : persistée, remplie au gain de niveau et au choix de classe, refus d'une compétence
  d'une autre classe, déplacement d'une case à l'autre.
- **Room** : un avatar ralenti saute des pas ; un avatar étourdi ne marche pas.
- **Réseau** : un test de format par paquet 9013–9015, 9018–9024.

## 16. Contenu de départ

P = Puissance du lanceur. Chiffres de départ, ajustables en JSON.

| Classe | Niv. | Id | Type, cible | Effets | Coût / recharge |
|---|---|---|---|---|---|
| Guerrier | 1 | `skill.warrior.heroic_strike` | instant, ennemi, 1 case | dégâts 4 + 1,0 P ; menace +50 % | 5 / 4 s |
| | 3 | `skill.warrior.challenging_shout` | instant, autour de soi, rayon 2 | `status.weakened` (débuff −20 % dégâts infligés, 8 s, remplacer) | 10 / 15 s |
| | 6 | `skill.warrior.shield_bash` | instant, ennemi, 1 case | dégâts 0,5 P ; `status.stunned` (2 s, ignorer) ; interruption | 8 / 12 s |
| Archer | 1 | `skill.archer.aimed_shot` | incantée 1,5 s, ennemi, 6 cases | dégâts 6 + 1,6 P | 8 / — |
| | 3 | `skill.archer.focus` | instant, soi, hors délai global | `status.focused` (buff +20 critique en plat, 10 s, rafraîchir) | 6 / 30 s |
| | 6 | `skill.archer.volley` | canalisée 3 s, pulsation 1 s, sol, rayon 1, 6 cases, 5 cibles | dégâts 0,5 P par pulsation | 15 / 15 s |
| Mage | 1 | `skill.mage.firebolt` | incantée 2 s, ennemi, 5 cases | dégâts 8 + 1,8 P | 10 / — |
| | 3 | `skill.mage.flame_burst` | incantée 1,5 s, autour de la cible, rayon 1, 5 cases | dégâts 0,8 P ; `status.burning` (dot 0,2 P / 2 s, 6 s, cumul ≤ 3) | 18 / 10 s |
| | 6 | `skill.mage.frost_nova` | instant, autour de soi, rayon 2 | dégâts 0,4 P ; `status.frozen` (ralentissement 60 %, 5 s, remplacer) | 15 / 20 s |
| Clerc | 1 | `skill.cleric.heal` | incantée 1,5 s, allié, 5 cases | soin 10 + 1,5 P | 12 / — |
| | 3 | `skill.cleric.renew` | instant, allié, 5 cases | `status.renewed` (hot 0,4 P / 3 s, 12 s, rafraîchir) | 10 / — |
| | 6 | `skill.cleric.aegis` | instant, allié, 5 cases | `status.aegis` (bouclier 20 + 2 P, 10 s, ignorer) | 15 / 20 s |

Chaque type de statut et chaque règle de cumul sert au moins une fois ; `dispel` n'est utilisé par
aucune compétence de départ (testé seul). Les textes (noms, descriptions) vont dans
`content/texts/fr.txt`.

## 17. Preuve et critères d'acceptation

**`driver.mjs skills`** sur un troisième compte jetable (`run-skill-c@vortex.local`), pour ne pas
changer la classe du compte de `items` : choisir Mage si besoin, entrer dans `proof_combat`, puis
1. `RpgSkills` reçu (Trait de feu connu, dans la barre) ;
2. Trait de feu sur un loup : `RpgCastStart` (2 000 ms), puis dégâts dans `RpgSkillEvents`, puis
   `RpgCooldowns` ;
3. un second lancer pendant le délai global : `RpgSkillResult(global_cooldown)` ;
4. un Trait de feu interrompu par une marche : `RpgCastEnd(cancelled)` ;
5. un lancer depuis le camp : `RpgSkillResult(safe_zone)`.

Les compétences de niveau 3 et 6 sont prouvées par les tests (pas de moyen propre de monter un
personnage de niveau pendant que l'émulateur tourne).

**Acceptation** : porte de qualité verte ; `smoke`, `combat` et `skills` passent ; aucun paquet
9013–9024 sans parser ni serializer (`check-inert-declarations`).
