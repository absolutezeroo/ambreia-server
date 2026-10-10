# Zones — spec Z1 « Règles et déclencheurs »

> **Statut :** design approuvé par le propriétaire le 2026-10-10 (« façon je te fais confiance ») ·
> **Date :** 2026-10-10 · **Langue :** français
> **Dépôt :** `vortex-rpg-emulator`. Premier des six sous-projets qui font vivre les calques de
> l'éditeur de map (Z1 règles, Z2 PNJ, Z3 butin et coffres, Z4 boss d'arène et événements, Z5 PvP,
> Z6 ambiance). S'appuie sur la spec de l'éditeur (`2026-10-07-rpg-world-map-editor-design.md`
> §4.2–4.3) et sur M1a/M1c (zone de combat, apparitions).

---

## 1. But

Tout ce que l'éditeur permet d'écrire dans les déclencheurs et les points d'apparition, et que les
systèmes actuels savent faire, marche en jeu. Plus aucune règle « enregistrée mais sans effet » hors
de ce qui attend un système absent (quêtes, factions).

**Critère de réussite.** Dans une zone de preuve : un joueur de niveau 1 qui marche sur une aire
« niveau ≥ 2 » est refusé ; un monstre tué déclenche une embuscade (un groupe en sommeil apparaît en
une vague) ; le joueur gagne un niveau et un déclencheur « niveau 2 atteint » lui affiche un message ;
il reçoit un objet et un déclencheur « objet obtenu » se déclenche ; un point d'apparition éloigné ne
fait apparaître ses monstres qu'à l'approche du joueur. Le validateur de l'éditeur ne signale plus
ces règles comme sans effet.

## 2. Décisions — APPROUVÉ (conversation du 2026-10-10)

1. **Règles déclarées par leur module.** Les conditions et actions ne sont plus deux listes figées
   dans `Vortex.Rooms` : un module RPG déclare les siennes par DI. Les rooms gardent les leurs.
2. **Faits du joueur dans la zone.** La zone tient par joueur son niveau et ses objets comptés par
   définition ; les conditions restent synchrones (elles tournent à chaque pas).
3. **Trois déclencheurs** partent enfin : monstre tué, niveau atteint, objet obtenu.
4. **Groupe en sommeil :** un point d'apparition visé par une action « faire apparaître un groupe »
   ne s'éveille pas seul ; l'action fait apparaître une vague, sans réapparition.
5. **Conditions des points d'apparition :** pas d'apparition tant qu'elles ne tiennent pas ; les
   monstres hors combat disparaissent quand elles cessent de tenir.
6. **`min`/`max`, activation par proximité** des points d'apparition, appliqués.
7. **Niveau conseillé :** un avertissement au joueur sous le niveau conseillé d'une zone ou d'une
   aire ; rien s'il a le niveau.
8. **Aucun changement client.**

## 3. Périmètre

**Dedans :** conditions `PlayerLevel`, `HasItem` ; action `SpawnGroup` ; déclencheurs
`MonsterKilled`, `PlayerLevel`, `ItemOwned` ; `ZoneSpawn.Min`, `Group`, `Conditions`,
`AoiActivation` ; niveau conseillé (zone et aire) ; validateur.

**Dehors :** `QuestState`, `StartQuest`, `Faction` (attendent leur système ; restent signalés sans
effet) ; PvP (Z5) ; musique, météo, sons (Z6) ; PNJ (Z2).

## 4. Architecture

### 4.1 Projets touchés

| Projet | Change |
|---|---|
| `Vortex.Primitives` | `CharacterZoneFacts` (niveau, objets) ; `IZoneRuleDeclaration` ; `IPlayerCharacterGrain.GetZoneFactsAsync` ; `IRoomZone.UpdatePlayerFactsAsync` ; `IPlayerPresenceGrain.OnCharacterFactsChangedAsync` ; `IRpgContentService.GetItemIds` |
| `Vortex.Rpg` (`Zones/`) | contrats `IZoneConditionEvaluator`, `IZoneActionExecutor`, `ZoneRuleContext` ; `IZoneRuntime.ConditionsHold`, `MonsterKilled`, `PlayerCells` |
| `Vortex.Rooms` | registre des règles (les siennes + celles de DI) ; faits par joueur ; déclencheurs monstre tué / niveau / objet ; avertissement de niveau |
| `Vortex.Rpg.Characters` | évaluateurs `PlayerLevel`, `HasItem` ; relevé des faits ; envoi à chaque changement |
| `Vortex.Rpg.Monsters` | exécuteur `SpawnGroup` ; sommeil, vagues, `min`, conditions, proximité |
| `Vortex.Players` | relais des faits présence → room |
| `Vortex.World` | validateur : « sans effet » d'après les règles déclarées ; nouvelles vérifications |

### 4.2 Les règles déclarées

Contrats dans `Vortex.Rpg/Zones/Rules/` (les rooms référencent `Vortex.Rpg`, l'inverse jamais) :

```csharp
public interface IZoneConditionEvaluator : IZoneRuleDeclaration
{   bool Holds(ZoneCondition condition, ZoneRuleContext ctx); }

public interface IZoneActionExecutor : IZoneRuleDeclaration
{   Task RunAsync(ZoneAction action, ZoneRuleContext ctx, CancellationToken ct); }

public sealed record ZoneRuleContext(
    PlayerId PlayerId,               // PlayerId.Invalid pour l'horloge ou un point d'apparition
    CharacterZoneFacts? Facts,       // null : aucun joueur, ou faits pas encore lus
    DateTimeOffset LocalNow,
    IZoneRuntime Runtime,
    IZoneSimulation Simulation);
```

`IZoneRuleDeclaration` (dans `Vortex.Primitives/World/Rules/`) : `Type RuleType { get; }`. Il sert au
validateur (§9), qui ne référence pas `Vortex.Rpg`.

- **Les pièces des rooms** (heure, zone active, dialogue, activer/désactiver, téléporter, porte, son)
  gardent leur interface interne et leur `ZoneTriggerContext` (elles agissent sur la room). Le
  registre des rooms (`ZoneConditions`, `ZoneActions`) devient une instance tenue par `RoomGrain` :
  ses pièces d'abord, puis celles de DI (`IEnumerable<IZoneConditionEvaluator>` /
  `IEnumerable<IZoneActionExecutor>`). Une même règle déclarée deux fois : la dernière déclarée
  gagne, un avertissement au démarrage (convention des canaux de chat).
- Les rooms déclarent leurs propres types par DI comme `IZoneRuleDeclaration` (une déclaration par
  type), pour que le validateur les connaisse.
- Une condition sans pièce vaut **faux** ; une action sans pièce est sautée (journal Debug) :
  inchangé.

### 4.3 Les faits du joueur

```csharp
[GenerateSerializer, Immutable]
public sealed record CharacterZoneFacts
{   [Id(0)] int Level;  [Id(1)] ImmutableDictionary<string, int> Items; }   // définition → quantité
```

- **Compte :** sac et équipement (pas les colis : non réclamés, ils ne sont pas « possédés »).
- **Lecture à l'entrée :** quand un joueur entre dans une room de zone, la room lit
  `GetZoneFactsAsync()` en même temps que le profil de combat (pas d'aller-retour en plus sur le
  chemin critique) et le range dans `ZoneRuntimeState.Facts[player]`. Sans personnage : pas de faits,
  les conditions de niveau et d'objet valent faux.
- **Mise à jour :** `PlayerCharacterGrain` envoie ses faits après une montée de niveau, un objet
  reçu, retiré, détruit, équipé ou déséquipé. Même chemin que les stats : présence → room, jamais
  attendu (la room peut attendre ce personnage pendant son tick). La room remplace le relevé.
- **Départ :** le relevé du joueur est retiré.

### 4.4 Évaluateurs RPG (`Vortex.Rpg.Characters/Zones/`)

- `PlayerLevel {min, max}` : `min ≤ niveau ≤ max` ; `max` 0 = sans plafond.
- `HasItem {item, count}` : quantité ≥ `count` (au moins 1).

## 5. Déclencheurs

### 5.1 Monstre tué

- `RpgZoneSimulation`, à chaque mort d'un monstre issu d'un point d'apparition, appelle
  `IZoneRuntime.MonsterKilled(spawnId, monsterId, participants)`. Un renfort de boss compte aussi
  (son point est celui du boss).
- `RoomZoneRuntime` met l'événement en file ; après le tick de combat la room publie un
  `ZoneMonsterKilledEvent` par participant encore dans la room ; `ZoneTriggerModule` lance les
  déclencheurs `MonsterKilled` dont la cible est **l'id du point** ou **l'id du monstre**.
- Contexte : le participant (chacun a son temps de recharge et ses conditions).

### 5.2 Niveau atteint

À la mise à jour des faits, pour chaque niveau franchi (ancien + 1 … nouveau) : les déclencheurs
`PlayerLevel` de cible ce niveau (`"5"`). Pas à l'entrée dans la zone : il faut franchir le niveau
sur place.

### 5.3 Objet obtenu

À la mise à jour des faits, pour chaque objet dont la quantité augmente : les déclencheurs
`ItemOwned` de cible l'id de l'objet. Une première lecture (entrée) ne déclenche rien.

## 6. Points d'apparition

### 6.1 Sommeil et vagues

- **En sommeil :** un point dont l'`id` ou le `group` est nommé par une action `SpawnGroup` d'un
  déclencheur du document. Calculé à la création de la simulation.
- `SpawnGroup {spawn}` réveille tous les points d'`id` ou de `group` égal à `spawn` : chacun fait
  apparaître **une vague** de `min..max` monstres (tirage), **sans réapparition**. Tant qu'un monstre
  de sa vague vit, le point ignore un nouvel appel ; la vague morte, il peut repartir.
- Les conditions du point (§6.3) s'appliquent au moment du réveil.

### 6.2 Points ordinaires

Inchangés sauf : à l'éveil, chaque point fixe son effectif à `min..max` (tirage) et le maintient par
la réapparition (`RespawnSeconds`).

### 6.3 Conditions du point

Vérifiées une fois par seconde par point, sans joueur (`PlayerId.Invalid`, pas de faits : niveau et
objet valent faux, heure et zone active marchent). Fausses : pas de nouvelle apparition ; en
passant de vraies à fausses, les monstres du point **hors combat** disparaissent. Redevenues vraies,
le point se remplit à nouveau.

### 6.4 Activation par proximité

- `AoiActivation` vrai (la valeur par défaut des documents) : le point s'éveille quand un joueur est
  à `AoiRadius` cases ou moins d'une case de son aire ; il se rendort quand personne n'y est depuis
  `AoiSleepSeconds` : ses monstres hors combat disparaissent, ses réapparitions en attente sont
  effacées, il se remplira au réveil.
- Faux : comme aujourd'hui, éveillé dès le premier joueur dans la zone.
- Positions : `IZoneRuntime.PlayerCells()` (les joueurs vivants de la zone).

### 6.5 Configuration (`Vortex:Monsters`)

| Clé | Défaut | Rôle |
|---|---|---|
| `AoiRadius` | 12 | distance d'éveil (cases, Chebyshev) |
| `AoiSleepSeconds` | 30 | délai avant le sommeil |
| `SpawnConditionCheckMs` | 1000 | période de vérification des conditions |

## 7. Niveau conseillé

À l'arrivée dans la zone (`settings.recommendedLevel`) et à l'entrée d'une aire (son
`recommendedLevel`, déjà dans `ZoneCellRules`) : si le joueur a des faits et un niveau inférieur,
une bulle `zone.level` « Niveau conseillé : N — vous êtes niveau M. » Une fois par aire et par
visite de la zone (recharge par joueur `level:<aire>`). Rien sans faits.

## 8. Réseau

Aucun nouveau paquet. Les messages passent par `ZoneBubble` (2243) comme les autres messages de zone.

## 9. Validateur (`Vortex.World`)

- « Sans effet » (`InertUntilRpg`) : une condition ou une action dont **aucune règle n'est déclarée**
  (`IEnumerable<IZoneRuleDeclaration>`), au lieu de la liste écrite à la main ; pour les
  déclencheurs, seul `QuestState` reste sans effet.
- Nouvelles vérifications :
  - `PlayerLevel` : `min ≥ 1`, `max` 0 ou ≥ `min` (erreur) ;
  - `HasItem` : l'objet existe dans le contenu (erreur `UnknownItem`), `count ≥ 1` ; sans contenu
    chargé, avertissement comme pour les monstres ;
  - `SpawnGroup` : `spawn` nomme un point ou un groupe du document (erreur `BrokenReference`) ;
  - déclencheur `MonsterKilled` : cible = id d'un point ou monstre d'un point (erreur
    `WrongTarget`) ; `PlayerLevel` : cible entier ≥ 2 ; `ItemOwned` : objet du contenu.
- Information `DormantSpawn` sur un point en sommeil : l'auteur voit qu'il n'apparaîtra que sur
  déclencheur.

## 10. Erreurs et concurrence

- Les faits arrivent par un appel de grain non attendu par l'émetteur ; un relevé plus ancien qui
  arriverait après un plus récent n'est pas possible : un seul émetteur (le personnage), appels
  ordonnés vers une même room.
- Une action qui lève est journalisée et la suivante continue (inchangé).
- La file des morts est vidée après chaque tick, même si une publication échoue (journal).

## 11. Modularité

Le noyau des rooms ne nomme ni niveau, ni objet, ni groupe de monstres : ces règles sont des pièces
déclarées par `CharactersModule` et `MonstersModule`. Le futur module de quêtes déclarera
`StartQuest` et `QuestState` sans toucher aux rooms ni au validateur. `SpawnGroup` atteint la
simulation de son propre module (`RpgZoneSimulation`) par le contexte : savoir interne au module
monstres.

## 12. Tests

- Registre : une pièce déclarée par DI répond ; une règle déclarée deux fois, la dernière gagne.
- Faits : lus à l'entrée, remplacés à la mise à jour, retirés au départ ; conditions niveau et objet
  (bornes, `max` 0, colis non comptés).
- Déclencheurs : monstre tué (cible point et cible monstre, un par participant), niveau franchi
  (plusieurs niveaux d'un coup), objet obtenu (hausse seulement, rien à l'entrée).
- Points d'apparition (monde de test des monstres) : sommeil puis vague sans réapparition, vague
  ignorée tant qu'un monstre vit ; `min..max` ; conditions fausses puis vraies ; proximité : éveil,
  sommeil après délai, monstres en combat gardés.
- Niveau conseillé : bulle sous le niveau, rien au niveau, une seule par aire et visite.
- Validateur : chaque nouvelle vérification ; une règle non déclarée reste signalée.

## 13. Preuve et critères d'acceptation

- `VortexCloudQualityGate` vert.
- Scénario driver `rules` : zone de preuve publiée par l'API de l'éditeur (aire `niveau ≥ 2`, point
  ordinaire, point en sommeil réveillé par « monstre tué », déclencheurs niveau 2 et objet) ; le
  joueur est refusé, tue un monstre, voit la vague, gagne un niveau (route XP), reçoit un objet
  (route admin) ; chaque étape lue sur le fil (bulles 2243, `Users` 996 des monstres).
- La zone de preuve est validée sans finding « sans effet ».
