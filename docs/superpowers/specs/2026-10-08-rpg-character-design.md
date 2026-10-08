# RPG — spec fille Personnage : classe, niveau, XP, stats, dernière zone, reconnexion

> **Statut :** à relire par le propriétaire · **Date :** 2026-10-08 · **Langue :** français
> **Dépôt :** `vortex-rpg-emulator`. Spec fille « Personnage » (§23 n° 1) de la spec unifiée
> `docs/superpowers/specs/2026-10-06-vortex-rpg-design.md` (§3.5, §5, §18, décision d'audit n° 1).
> Dépend de la spec Contenu (`2026-10-08-rpg-content-design.md`) ; la spec Objets
> (`2026-10-08-rpg-items-design.md`) branche l'équipement sur les stats définies ici.

---

## 1. But

Faire de chaque avatar un **personnage** : une classe, un niveau, de l'XP, des stats calculées, une
dernière zone où revenir, et une déconnexion qui ne le fait pas disparaître au premier coup de
réseau. Le combat (M1) lit ces stats ; il ne les calcule pas.

**Critère de réussite.** Un avatar créé avec la classe Guerrier a les stats de niveau 1 du
Guerrier ; de l'XP accordée par le serveur le fait monter de niveau et ses stats suivent ; il se
reconnecte dans la zone qu'il a quittée ; une coupure de moins de 60 s le laisse dans sa zone.

## 2. Décisions — APPROUVÉ (conversation du 2026-10-08)

1. **Personnage = avatar** (décision d'audit n° 1) : la table `players` reste l'identité sociale
   (nom, figure, devise) ; l'état RPG est à côté, dans `player_characters`.
2. **5 personnages par compte** (limite actuelle, `Vortex.WebApi/Configuration/WebApiConfig.cs:95`).
3. **Niveau max 20** en V1.
4. **Départ « Sans classe », puis une vraie classe, définitive** (révisé le 2026-10-08 par le
   propriétaire). Tout nouvel avatar commence dans la classe de départ `class.classless`
   (« Sans classe », `"starter": true` dans le contenu, une seule par paquet) : niveau, XP et stats
   tout de suite. Il la quitte **une fois** pour Guerrier, Archer, Mage ou Clerc, en gardant niveau
   et XP ; une vraie classe est définitive. Un avatar existant sans personnage reçoit « Sans
   classe » à sa prochaine sélection. Une seule spécialisation par classe en V1, attribuée
   d'office ; le changement de spécialisation est tranché par la spec Combat (décision ouverte
   n° 18).
5. **Délai de grâce à la déconnexion : 60 s** (décision ouverte n° 17).
6. **À la connexion : le personnage choisi, puis sa dernière zone** (décision d'audit n° 10).

## 3. Périmètre

**Dans F2.** Table et grain du personnage, classes et spécialisations en contenu, courbe d'XP, pile de
stats et ses deux premières sources (classe et niveau ; équipement par la spec Objets), gain d'XP et
montée de niveau, choix de la classe par le web API, dernière zone et retour à la connexion, délai
de grâce.

**Hors F2.** PV et mana courants, mort, états de combat (runtime de zone, M1) ; points de compétence
et barre d'action (spec Combat, M1) ; messages du jeu vers le client (réseau RPG, F3) ; écran de
choix de classe du site (chantier du site, qui appelle les routes de §8).

## 4. Contenu (sortes `class`, `specialization`)

`class.warrior` :

```json
{
  "id": "class.warrior", "kind": "class",
  "role": "tank", "range": 1, "threatMultiplier": 3,
  "primaryStat": "strength",
  "base":     { "strength": 12, "intelligence": 4, "agility": 6, "health": 60, "mana": 40, "armor": 4 },
  "perLevel": { "strength": 3,  "intelligence": 0, "agility": 1, "health": 18, "mana": 2, "armor": 1 },
  "specializations": ["spec.warrior.guardian"]
}
```

- `health` est le PV de la pile : au niveau 1, PV max = 60 + 12 × `healthPerStrength` (5) = 120.
- `role` ∈ `tank` | `damage` | `heal` ; `primaryStat` ∈ `strength` | `intelligence` | `agility`.
- `specialization` : `id`, `class` (référence), pour l'instant aucun autre champ (les modificateurs
  arrivent avec la spec Combat).
- Validation : `range` ≥ 1, valeurs `base` > 0, `perLevel` ≥ 0, au moins une spécialisation, chaque
  spécialisation référence cette classe.
- Les quatre classes de §5.3 de la spec unifiée sont livrées dans `content/` : Guerrier (FOR, 1
  case, menace × 3), Archer (AGI, 6), Mage (INT, 5), Clerc (INT, 5).

## 5. Contenu (sorte `progression`) et XP

`progression.default` porte la courbe d'XP et les coefficients des stats dérivées :

```json
{
  "id": "progression.default", "kind": "progression",
  "maxLevel": 20,
  "xpToNext": [100, 150, 220, …],
  "healthPerStrength": 5, "manaPerIntelligence": 5,
  "critBasePercent": 5, "critPerAgilityPercent": 0.2, "critCapPercent": 50,
  "dodgePerAgilityPercent": 0.1, "dodgeCapPercent": 30
}
```

- `xpToNext` a `maxLevel − 1` entrées, toutes > 0 et croissantes.
- **Gain d'XP** : `GainXpAsync(montant, source)` sur le grain, appelé seulement par le serveur.
  Publie `XpGaining` (montant modifiable, annulable), ajoute, passe autant de niveaux que l'XP le
  permet en publiant `LevelUp` pour chacun, puis `XpGained`. Au niveau max, l'XP ne s'accumule plus.
- Montant ≤ 0 ou au-delà d'un plafond par appel (`CharacterConfig.MaxXpPerGrant`) : refusé et
  journalisé.

## 6. Stats

### 6.1 Calcul (§4 règle 3 de la spec unifiée)

Stat finale = somme des contributions des sources `IStatSource` enregistrées. Une source est une
classe du code **déclarée par DI** dans le module qui la porte
(`services.AddSingleton<IStatSource, ClassLevelStatSource>()`, comme les sortes de contenu) ; F2 en
livre deux :

| Source | Contribue |
|---|---|
| `ClassLevelStatSource` | `base + perLevel × (niveau − 1)` de la classe |
| `EquipmentStatSource` (spec Objets) | stats des objets équipés dont la durabilité est > 0 |

Puis les dérivées, dans cet ordre, à partir des principales finales :

- **PV max** = PV de la pile + FOR × `healthPerStrength`
- **Mana max** = mana de la pile + INT × `manaPerIntelligence`
- **Armure** = armure de la pile (équipement)
- **Critique %** = min(`critBasePercent` + AGI × `critPerAgilityPercent`, `critCapPercent`)
- **Esquive %** = min(AGI × `dodgePerAgilityPercent`, `dodgeCapPercent`)
- **Vitesse** = 100 + vitesse de la pile (en %, bornée à [50, 200])
- **Puissance** = la stat principale de la classe

Aucune stat n'est négative (bornée à 0). Le calcul est pur et déterministe (`StatCalculator` dans
`Vortex.Rpg`), testé sans grain.

### 6.2 Instantané

`CharacterStatsSnapshot` (`[GenerateSerializer, Immutable]`, `Vortex.Primitives`) : les valeurs
finales **et le détail par source** (ce que l'interface et le debug affichent). Le grain le recalcule
quand le niveau, l'équipement ou le contenu changent, et le garde en mémoire ; la zone le demande
**une fois à l'entrée** (§3.5 de la spec unifiée).

## 7. Propriétaire et persistance

| État | Propriétaire | Où |
|---|---|---|
| Classe, spécialisation, niveau, XP, dernière zone | `PlayerCharacterGrain` (clé = id du joueur) | `player_characters` |
| Nom, figure, devise | `PlayerGrain` (inchangé) | `players` |
| Session, room active, délai de grâce | `PlayerPresenceGrain` (inchangé + §10) | mémoire |

- `player_characters` : `id`, `player_id` (unique, clé étrangère), `class_id` et
  `specialization_id` (ids de contenu), `level`, `xp`, `last_room_id` (nullable), horodatages.
  Une ligne n'existe que pour un avatar qui a une classe.
- **Écriture** par le grain seul, en différé : l'état sale est écrit au plus toutes les
  `CharacterConfig.FlushIntervalMs` (30 s), à la désactivation, et immédiatement à une montée de
  niveau (ne jamais perdre un niveau). Pas d'écriture par gain d'XP (§3.6 de la spec unifiée).
- Projet **`Vortex.Rpg.Characters`** (couche plateforme, `scripts/hooks/check-architecture-walls.mjs`) :
  grain, sources de stats, handlers d'events. Contrat du grain (`IPlayerCharacterGrain`) et
  instantanés dans `Vortex.Primitives`, accès `GetPlayerCharacterGrain` dans `GrainFactoryExtensions`.
- Le noyau **`Vortex.Rpg`** porte le contenu (spec Contenu), `IStatSource`, `StatCalculator`, les
  contrats des events RPG.

## 8. Commandes (web API)

| Route | Effet |
|---|---|
| `GET /api/public/content/classes` | Les classes : id, rôle, portée, stat principale (textes par clés) |
| `POST /api/user/avatars` | Champ `classId` facultatif (vérifié s'il est donné) ; sans lui, le personnage part « Sans classe » ; spécialisation par défaut |
| `POST /api/user/avatars/select` | Donne « Sans classe » à un avatar qui n'a pas encore de personnage |
| `POST /api/user/avatars/{uniqueId}/class` | Quitte « Sans classe » pour une vraie classe, une seule fois (409 ensuite) |
| `GET /api/user/avatars` | Chaque avatar porte `classId` et `level` (null s'il n'a pas de classe) |

Les routes vérifient que l'avatar appartient au compte de la session (comme `avatars/select`). Le
choix passe par le grain (`ChooseClassAsync`), jamais par une requête directe à la table.

## 9. Dernière zone et connexion

- **Enregistrement** : un handler de `PlayerEnteredRoomEvent` (dans `Vortex.Rpg.Characters`) donne la
  room au grain ; le grain la garde si c'est une zone **du monde** (pas une copie de donjon,
  `ZoneInstanceIds.IsInstance`), écrite au prochain vidage.
- **Connexion** (`SSOTicketMessageHandler`) : ordre du lieu d'arrivée —
  1. une entrée de test en attente (mode test de l'éditeur, inchangé) ;
  2. la dernière zone du personnage, si c'est encore une zone publiée ;
  3. la zone de départ (`world.start_room_id`).
  Le handler lit la dernière zone par `IPlayerCharacterGrain` (contrat dans `Vortex.Primitives` :
  le cœur ne référence pas le RPG).
- Un avatar sans classe se connecte comme aujourd'hui (le social marche) ; les systèmes RPG l'ignorent
  tant qu'il n'a pas de personnage.

## 10. Reconnexion : délai de grâce

- Quand la session d'un joueur **en zone** se ferme sans `Quit` (coupure), `PlayerPresenceGrain` ne
  fait pas sortir l'avatar tout de suite : il arme un minuteur de `PlayerConfig.DisconnectGraceSeconds`
  (60 s). L'avatar reste dans sa zone, immobile.
- **Reconnexion avant la fin** : la nouvelle session se rattache à la présence ; la connexion envoie
  `RoomForward` vers la même room ; l'entrée **reprend l'avatar existant** au lieu d'en créer un
  second (« jamais de double personnage », §18 de la spec unifiée), et renvoie l'instantané de la
  room.
- **Fin du délai** : sortie normale (comme aujourd'hui).
- Un `Quit` explicite sort immédiatement, sans grâce.

## 11. Events

`CharacterCreated`, `XpGaining` (annulable, montant modifiable), `XpGained`, `LevelUp` (ancien et
nouveau niveau), `CharacterStatsChanged`. Les events « -ed » sont publiés hors du chemin critique.

## 12. Concurrence et échecs

- Un seul propriétaire de l'état RPG : le grain (activation unique par joueur).
- Choisir une classe deux fois en même temps : le grain sérialise, la seconde reçoit « déjà choisie ».
- Classe ou spécialisation retirée du contenu : la migration `replace` (spec Contenu §8) réécrit
  `class_id` ; le grain relit à l'activation ; une référence inconnue au chargement est journalisée et
  le personnage reste lisible (stats de classe nulles) plutôt que de bloquer le joueur.
- Écriture échouée : l'état reste sale et repart au vidage suivant ; journalisé.

## 13. Observabilité

Log structuré à la création, au choix de classe, à chaque montée de niveau (joueur, classe, niveau).
Compteur des personnages actifs et des vidages (durée, échecs).

## 14. Anti-triche

Le client n'envoie ni stats, ni XP, ni niveau (§5.2 de la spec unifiée) ; l'XP n'entre que par
`GainXpAsync`, appelé par des systèmes serveur ; la classe est validée contre le contenu et une seule
fois ; les routes vérifient l'appartenance de l'avatar.

## 15. Tests

- `StatCalculator` : niveau 1 et niveau 20 de chaque classe, bornes (critique, esquive, vitesse),
  aucune valeur négative, détail par source.
- Grain : création, choix unique, gain d'XP sur plusieurs niveaux, niveau max, `XpGaining` annulé
  ou modifié, vidage différé, écriture immédiate à la montée de niveau.
- Contenu : classes et progression valides et invalides (spec Contenu §9).
- Connexion : dernière zone, zone retirée → départ, copie de donjon jamais retenue.
- Grâce : coupure puis reconnexion dans le délai → un seul avatar, même case ; délai écoulé → sortie.
- Routes : classe requise, choix une seule fois, appartenance.

## 16. Critères d'acceptation

- Créer un avatar Guerrier par le web API, se connecter (driver) : il a un personnage niveau 1 aux
  stats du Guerrier.
- Lui accorder de l'XP (commande de test) : il monte de niveau, la ligne est écrite.
- Quitter puis se reconnecter : il revient dans sa dernière zone.
- Couper la socket et revenir dans les 60 s : un seul avatar, au même endroit.
