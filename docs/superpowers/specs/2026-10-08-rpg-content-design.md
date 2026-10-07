# RPG — spec fille Contenu : définitions, registre, import, traductions, migrations

> **Statut :** à relire par le propriétaire · **Date :** 2026-10-08 · **Langue :** français
> **Dépôt :** `vortex-rpg-emulator`. Spec fille « Contenu » (§23 n° 11) de la spec unifiée
> `docs/superpowers/specs/2026-10-06-vortex-rpg-design.md` (§3 bis « Pipeline de contenu », §4, §17,
> INV-006). Première des trois specs F2 : Contenu → Personnage
> (`2026-10-08-rpg-character-design.md`) → Objets (`2026-10-08-rpg-items-design.md`).

---

## 1. But

Que tout le contenu du RPG — classes, objets, courbe d'XP aujourd'hui ; compétences, monstres,
tables de loot, quêtes, PNJ, recettes ensuite — soit de la **donnée** : des définitions à id stable,
validées avant d'entrer en jeu, rechargeables sans redémarrer, traduisibles, et dont la suppression
ne casse jamais ce que les joueurs possèdent. Le code (`[SkillEffect]`, `[StatSource]`…) donne le
comportement ; le contenu le paramètre (§4 règle 1).

**Critère de réussite.** Un fichier de contenu modifié dans le dépôt est importé ; une erreur (champ
manquant, référence cassée, id mal formé, texte sans traduction) refuse **tout** l'import avec un
message qui nomme le fichier et le champ ; un import valide est en jeu dans la seconde, sans
redémarrage ; un emulateur dont la base contient du contenu invalide refuse de démarrer et dit
pourquoi.

## 2. Décisions — APPROUVÉ (conversation du 2026-10-08)

1. **Le contenu est un ensemble de documents JSON**, comme les zones : un document par définition,
   typé par un record C# et décrit par un schéma JSON (§4). Pas une table par sorte : un champ de
   plus ne demande pas de migration de base.
2. **Le dashboard n'existe plus (coupe du 2026-10-07)** : le contenu s'écrit dans des **fichiers du
   dépôt** (`content/`), importés et validés par une route du web API. Un éditeur graphique viendra
   plus tard dans l'application de l'éditeur de cartes, sur la même route. Cela remplace les pages
   dashboard de §17.
3. **Le serveur n'envoie que des clés de texte** ; les traductions vivent dans les fichiers de textes
   du client (§7).

### 2.1 Amendement à approuver avec cette spec

La spec unifiée nomme le registre `RpgContentGrain` (§3.4, §3.5). Il devient un **registre en
mémoire du silo** (`IRpgContent`, un instantané immuable remplacé d'un coup), comme le catalogue de
meubles (`Vortex.Furniture/Providers/FurnitureDefinitionProvider.cs`). Raison : le combat lira une
définition à chaque coup, dans le tick de la zone ; un appel de grain par lecture coûterait un
message Orleans là où une lecture de dictionnaire suffit. Le rechargement reste à chaud (§6).

## 3. Périmètre

**Dans F2.** Le format (id, sorte, version, document), le registre, l'import validé, les sortes
`class`, `specialization`, `progression`, `item` (champs définis par les specs Personnage et
Objets), les clés de texte, les migrations de contenu.

**Hors F2.** Les sortes des jalons suivants (compétence, effet de statut, monstre, table de loot,
quête, PNJ, recette) : elles s'ajoutent par le même mécanisme, chacune dans sa spec. L'éditeur
graphique de contenu. Le transport des textes vers le client (chantier client).

## 4. Modèle

### 4.1 Id stable

`<sorte>.<segment>[.<segment>…]` : minuscules, chiffres et `_` par segment, séparés par des points,
64 caractères au plus (`VortexEntity.ContentIdLength`). Le premier segment **est** la sorte :
`class.warrior`, `spec.warrior.guardian`, `item.weapon.rusted_sword`, `progression.default`.
Un id ne change jamais ; renommer, c'est créer un id et migrer (§8).

### 4.2 Document

Chaque définition est un record C# de `Vortex.Rpg/Content/Definitions/` (un type par fichier),
sérialisé en JSON avec les mêmes options strictes que les zones (`ZoneJson` : nombres stricts, champ
inconnu refusé, `null` refusé) :

```json
{ "id": "item.weapon.rusted_sword", "kind": "item", "category": "equipment", "rarity": "common", … }
```

Le schéma JSON de chaque sorte est **généré depuis les records** et vérifié par un test, comme
`schemas/zone.schema.json` (`Vortex.Hosting.Tests/World/ZoneSchemaTests.cs`) : `schemas/content/<sorte>.schema.json`.
Les fichiers du dépôt l'utilisent (`$schema`) pour l'autocomplétion dans l'éditeur de texte.

### 4.3 Sortes de F2

| Sorte | Record | Défini par |
|---|---|---|
| `class` | `ClassDefinition` | spec Personnage §4 |
| `specialization` | `SpecializationDefinition` | spec Personnage §4 |
| `progression` | `ProgressionDefinition` (XP par niveau) | spec Personnage §5 |
| `item` | `ItemDefinition` | spec Objets §4 |

Une sorte est une classe (`IContentKind`) qui nomme son record et son validateur, déclarée dans le
conteneur par le module qui la possède (`services.AddSingleton<IContentKind, ItemKind>()`), comme les
fournisseurs de données de référence ; un plugin en ajoute par son propre module. Pas de balayage
d'assemblies : le registre doit connaître toutes les sortes avant de charger le contenu au
démarrage, et le balayage n'a pas d'ordre garanti par rapport à ce chargement. Une sorte inconnue
dans un fichier est refusée.

## 5. Propriétaire et persistance

| État | Propriétaire | Où |
|---|---|---|
| Documents de contenu publiés | service d'import (`Vortex.Rpg`) — seul à écrire | table `rpg_content` |
| Règles de migration appliquées | service d'import | table `rpg_content_migrations` |
| Instantané en mémoire | registre `IRpgContent` | silo |

- `rpg_content` : `id`, `content_id` (unique), `kind`, `document` (texte JSON), `hash` (SHA-256 du
  document normalisé), `pack_version` (version de l'import qui l'a écrit), horodatages
  `VortexEntity`, suppression douce.
- `rpg_content_migrations` : `id`, `from_content_id`, `action` (`replace` | `retire`),
  `to_content_id`, `pack_version`, `applied_at`.
- Une seule migration `InitialCreate` régénérée (`.claude/rules/database.md`). Aucun seed : le contenu
  de démonstration est dans `content/` et s'importe comme le reste.

## 6. Import (la seule commande)

`POST /api/content/import` (capability `content.publish`, rôle `owner`) reçoit **le paquet entier**
(tous les fichiers de `content/`, plus `content/migrations.json`). Le script
`scripts/content/push.mjs` le lit, l'envoie et affiche les constats.

1. **Lecture** : chaque fichier est désérialisé selon sa sorte ; une erreur nomme le fichier, le champ
   et la raison.
2. **Validation** du paquet entier (§9). La moindre **erreur** refuse tout ; les **avertissements**
   (texte sans traduction, sorte inerte) passent.
3. **Différence** avec la base : ajouts, modifications (hash différent), retraits.
4. **Migrations** (§8) : un retrait sans règle, d'un id détenu par des joueurs, est une erreur.
5. **Écriture** en une transaction : documents, règles appliquées, `pack_version` + 1.
6. **Rechargement** : le registre relit la base et remplace son instantané en une écriture atomique.

Réponse : `{ packVersion, added, changed, removed, findings[] }`. Un import identique au contenu en
place est un succès sans écriture. Deux imports concurrents : le second attend le verrou de la
transaction ; il revalide contre l'état écrit par le premier.

**Au démarrage**, le registre charge `rpg_content` et valide l'ensemble ; une erreur arrête l'hôte
avec le constat (« contenu invalide : item.weapon.x — classes autorisées : class.paladin n'existe
pas »). Une base vide démarre avec un registre vide.

## 7. Textes et traductions

- Un document ne contient aucun texte joueur. Ses textes ont des clés **dérivées** de l'id :
  `<id>.name`, `<id>.description` (et celles qu'une sorte déclare).
- Les traductions sont dans `content/texts/<langue>.txt` (format des fichiers de textes du client :
  `clé=valeur`), `fr` obligatoire, `en` facultatif.
- Valider : une clé dérivée absente de `fr.txt` est un **avertissement** ; une clé de `fr.txt` qui ne
  correspond à aucun contenu aussi.
- `GET /api/public/content/texts/{langue}` sert le fichier importé, pour que le client le charge à
  côté de ses textes (branchement client : chantier).

## 8. Migrations de contenu

Les joueurs détiennent des références au contenu (classe d'un personnage, définition d'un objet).
Avant d'écrire, l'import cherche, pour chaque id retiré, s'il est encore référencé (requêtes fournies
par chaque sorte : `IContentReferences`).

| Cas | Règle dans `content/migrations.json` | Effet |
|---|---|---|
| Id retiré, non référencé | aucune | retiré |
| Id retiré, référencé | `{ "from": "item.a", "action": "replace", "to": "item.b" }` | chaque référence pointe sur `item.b` (même sorte, `item.b` existe), dans la transaction |
| Id à garder pour ceux qui l'ont | le document reste, avec `"retired": true` | plus obtenable (loot, marchand), toujours chargé |
| Id retiré, référencé, sans règle | — | **erreur**, rien n'est écrit |

Une règle déjà appliquée (même `from`, même `to`) n'est pas rejouée. Le remboursement en or attend
la monnaie (spec Économie).

## 9. Validation

Par niveau, chacun dans sa classe (pas de validateur unique) :

1. **Forme** : id (§4.1), sorte connue, préfixe de l'id = sorte, champs requis, valeurs dans leurs
   bornes (niveau 1–max, rareté connue…).
2. **Sorte** : règles propres (`IContentValidator<TDefinition>`), écrites par la spec de la sorte.
3. **Références** : toute référence vers un autre contenu existe et a la bonne sorte (une classe
   autorisée d'un objet est une `class`).
4. **Paquet** : ids en double, règles de migration cohérentes.
5. **Textes** : §7.

Le rapport utilise le format des constats de zone (`ZoneFinding` : sévérité, code, id, message).

## 10. Interfaces

- `IRpgContent` (`Vortex.Rpg`) : `T? Get<T>(string id)`, `IReadOnlyList<T> All<T>()`, `long Version`.
  Lecture sans verrou ni allocation (instantané immuable). Pas de grain.
- Events : `ContentReloaded(packVersion, added, changed, removed)` publié après chaque
  rechargement ; un système qui garde une copie (le combat plus tard) se rafraîchit dessus.
- Permissions : nouvelle capability `content.publish`, accordée au rôle `owner`.

## 11. Concurrence, échecs, reconnexion

- Le registre est remplacé d'un coup : un lecteur voit l'ancien ou le nouveau, jamais un mélange.
- Un import échoué n'écrit rien et laisse le registre intact.
- Un personnage en jeu pendant une migration `replace` : ses grains relisent leurs objets à la
  prochaine activation ; en attendant, une définition retirée reste lisible jusqu'au rechargement
  suivant (§8 « retired »). (Le détail par sorte est dans les specs Personnage et Objets.)
- Rien n'est lié à la session : la reconnexion ne touche pas le contenu.

## 12. Observabilité

Au chargement : version, nombre de définitions par sorte, durée, en log structuré. Chaque import :
log de qui, quoi (ajouts, modifications, retraits, migrations) et durée.

## 13. Anti-triche

Le contenu ne vient jamais du client du jeu. La route d'import exige la capability ; un paquet est
validé intégralement côté serveur, quoi que dise le script.

## 14. Tests

- Forme de l'id, préfixe, sortes inconnues, champs inconnus ou `null` refusés.
- Paquet invalide → rien écrit, registre intact ; paquet valide → registre remplacé.
- Références cassées, doublons, textes manquants (avertissement).
- Migration : retrait d'un id référencé sans règle refusé ; `replace` réécrit les références ;
  règle rejouée sans effet.
- Démarrage sur un contenu invalide : l'hôte refuse avec le constat.
- Schémas générés à jour (test comme `ZoneSchemaTests`).
- Import identique → aucune écriture.

## 15. Critères d'acceptation

- `content/` du dépôt contient les 4 classes (Guerrier, Archer, Mage, Clerc), leur spécialisation, la
  courbe d'XP et une dizaine d'objets de démonstration, en français.
- `node scripts/content/push.mjs` les importe ; la réponse liste les ajouts.
- Une faute introduite exprès dans un fichier fait refuser l'import, le message nomme le fichier.
- Après import, le registre les sert sans redémarrage.
