# RPG — spec fille Objets : objet unique, sac, équipement, colis, liaison

> **Statut :** à relire par le propriétaire · **Date :** 2026-10-08 · **Langue :** français
> **Dépôt :** `vortex-rpg-emulator`. Spec fille « Objets » (§23 n° 4) de la spec unifiée
> `docs/superpowers/specs/2026-10-06-vortex-rpg-design.md` (§3 bis « Objet unique », §3.5, §8.3, §8.4,
> §18, INV-005). Dépend des specs Contenu (`2026-10-08-rpg-content-design.md`) et Personnage
> (`2026-10-08-rpg-character-design.md`). Le loot, les marchands et l'échange arrivent avec leurs
> jalons (M2, M3) sur ce socle.

---

## 1. But

Un seul système d'objets pour tout le jeu : armes, armures, consommables, matériaux, meubles,
montures, cosmétiques, objets de quête. Un objet est une **définition** de contenu et des
**exemplaires** que possèdent les personnages, rangés dans un sac de 30 cases et 10 emplacements
d'équipement. Le serveur valide et exécute toute opération ; rien ne se crée ni ne se perd par un
renvoi ou une course (INV-005).

**Critère de réussite.** Un personnage reçoit une épée (attribution serveur, rejouée deux fois : un
seul exemplaire), l'équipe — ses stats montent —, la range, la sépare d'une pile, reçoit un objet
avec un sac plein (il part en colis) et le récupère ; tout est là après un redémarrage.

## 2. Décisions — APPROUVÉ (conversation du 2026-10-08)

1. **10 emplacements** : tête, torse, jambes, pieds, mains, arme, main secondaire, cou, anneau 1,
   anneau 2 (décision ouverte n° 1).
2. **5 raretés** : Commun, Peu commun, Rare, Épique, Légendaire (n° 2).
3. **Liaison** : loot de boss lié au ramassage ; équipement Rare et plus lié quand on l'équipe ; le
   reste libre (n° 3).
4. **Sac de 30 cases** ; l'extension (or, premium) viendra avec l'économie (n° 4).
5. **Sac plein** : le surplus part en **colis**, gardés jusqu'à ce qu'on les récupère, 50 au plus
   (n° 5).
6. **L'équipement ne change pas l'apparence** en V1 ; seuls les cosmétiques le feront (avec leur
   spec).

## 3. Périmètre

**Dans F2.** La sorte de contenu `item`, les exemplaires, le sac, l'équipement, les colis, la
liaison, la durabilité (le champ et sa règle), l'attribution idempotente, la source de stats de
l'équipement, une route d'attribution pour l'administration et les tests.

**Hors F2.** Le loot (tables, tirage, M2), les marchands, la réparation et le rachat (Économie, M3),
l'échange entre joueurs, l'utilisation des consommables (Combat, M1), l'usure à la mort (M1), la pose
des meubles en maison (chantier Maisons), les effets des montures et des cosmétiques, les messages
du jeu (réseau RPG, F3).

## 4. Contenu (sorte `item`)

```json
{
  "id": "item.weapon.rusted_sword", "kind": "item",
  "category": "equipment", "rarity": "common",
  "slot": "weapon", "requiredLevel": 1, "allowedClasses": ["class.warrior"],
  "stats": { "strength": 3 },
  "maxDurability": 50, "maxStack": 1, "sellValue": 4, "binding": "none"
}
```

| Champ | Valeurs | Règle |
|---|---|---|
| `category` | `equipment`, `consumable`, `material`, `furniture`, `mount`, `cosmetic`, `quest` | requis |
| `rarity` | `common`, `uncommon`, `rare`, `epic`, `legendary` | requis |
| `slot` | `head`, `chest`, `legs`, `feet`, `hands`, `weapon`, `offhand`, `neck`, `ring` | requis pour `equipment`, interdit sinon ; `ring` va dans anneau 1 ou 2 |
| `requiredLevel` | 1 – niveau max | défaut 1 |
| `allowedClasses` | ids de `class` | vide = toutes |
| `stats` | `strength`, `intelligence`, `agility`, `health`, `mana`, `armor`, `speed` → entier | `equipment` seulement |
| `maxDurability` | ≥ 0 | 0 = inusable ; `equipment` seulement |
| `maxStack` | 1 – 999 | 1 pour `equipment` |
| `sellValue` | ≥ 0 (or) | défaut 0 |
| `binding` | `none`, `on_pickup`, `on_equip` | `equipment` de rareté ≥ `rare` : `on_equip` ou `on_pickup` (erreur sinon) ; `quest` : `on_pickup` |
| `furnitureDefinition` | id de `furniture_definitions` | requis pour `furniture` (existe dans le catalogue) |
| `retired` | booléen | plus obtenable (spec Contenu §8) |

Textes : `<id>.name`, `<id>.description` (spec Contenu §7). Livré dans `content/` : une dizaine
d'objets de démonstration couvrant chaque emplacement des quatre classes, un matériau empilable, un
objet de quête, un meuble.

## 5. Modèle des exemplaires

- `rpg_items` : `id` (bigint), `player_id` (le personnage), `definition_id` (id de contenu),
  `quantity`, `durability` (null si la définition est inusable), `bound` (booléen), `container`
  (`bag` = 0, `equipment` = 1, `parcel` = 2 ; jamais renuméroté), `slot` (case du sac 0–29, numéro
  d'emplacement, ou ordre du colis), horodatages, suppression douce.
- `rpg_item_grants` : `id`, `grant_key` (unique), `player_id`, `created_at` — une clé par
  attribution déjà faite (§7).
- Un exemplaire est **une ligne** : une pile de 20 matériaux est une ligne de quantité 20.
- L'audit réutilise `item_events` (existant) : attribution, retrait, destruction, liaison, avec
  l'id de corrélation de l'opération.

## 6. Propriétaire et persistance

- Le sac, l'équipement et les colis d'un personnage appartiennent à son **`PlayerCharacterGrain`**
  (§3.5 de la spec unifiée), dans des modules séparés — `CharacterBag`, `CharacterEquipment`,
  `CharacterParcels` — pour ne pas en faire une classe géante. Le grain charge ses exemplaires à
  l'activation.
- Les règles pures (peut-on équiper, empiler, séparer) sont dans `Vortex.Rpg/Items/` (sans grain),
  testées seules.
- **Écriture** : les déplacements, séparations, équipements sont marqués sales et écrits par lot au
  plus toutes les `ItemConfig.FlushIntervalMs` (2 s, comme les meubles des rooms) et à la
  désactivation. **Une attribution** est écrite tout de suite, dans une transaction qui contient sa
  clé (§7) : ce qui crée de la valeur n'attend pas un vidage.

## 7. Opérations

Toutes sur le grain, validées par lui, refusées avec un **code stable** (`bag_full`,
`slot_occupied`, `wrong_class`, `level_too_low`, `not_equipment`, `bound`, `not_found`,
`not_stackable`, `invalid_quantity`, `parcels_full`, `duplicate_grant`…) sans rien modifier.

| Opération | Règle |
|---|---|
| `GrantAsync(clé, [définition, quantité])` | Serveur seulement. Clé déjà vue → `duplicate_grant`, rien de fait (le premier résultat est rendu). Complète les piles existantes, puis les cases vides ; le surplus va en colis ; si les colis sont pleins, **rien** n'est attribué (`parcels_full`) et l'appelant garde l'objet (le loot reste au sol, M2). Liaison `on_pickup` → lié. |
| `MoveAsync(de, vers)` | Case vide : déplace. Même définition empilable : fusionne jusqu'à `maxStack`, le reste reste. Sinon : échange. |
| `SplitAsync(case, quantité, vers)` | Vers une case vide ; 0 < quantité < pile. |
| `EquipAsync(case)` | `equipment`, niveau, classe autorisée, emplacement (`ring` : anneau 1, sinon 2, sinon échange avec l'anneau 1). L'objet porté dans l'emplacement revient à la case quittée. `on_equip` → lié. Un objet à durabilité 0 s'équipe mais ne donne pas de stats (§8.1 de la spec unifiée). |
| `UnequipAsync(emplacement)` | Vers la première case vide ; sac plein → `bag_full`. |
| `DestroyAsync(case, quantité)` | Retire du sac, audité. |
| `ClaimParcelAsync(n)` | Le colis n va dans le sac (piles puis case vide) ; sac plein → `bag_full`. |
| `RemoveAsync(définition, quantité)` | Serveur seulement (quêtes, artisanat plus tard) : retire des piles, tout ou rien. |

Chaque opération réussie publie son event (§9) et recalcule les stats si l'équipement a changé.

## 8. Stats de l'équipement

`EquipmentStatSource` (`[StatSource]`, spec Personnage §6) additionne les `stats` des objets équipés
dont la durabilité est > 0 (ou inusables). Changer d'équipement recalcule
`CharacterStatsSnapshot` et publie `CharacterStatsChanged`.

## 9. Events

`ItemGranted` (clé, objets, colis), `ItemMoved`, `ItemSplit`, `ItemEquipped`, `ItemUnequipped`,
`ItemDestroyed`, `ItemRemoved`, `ParcelClaimed`, `ItemBound`. Ils serviront à la synchronisation du
client (F3) et aux objectifs de quête (« collect », M3).

## 10. Commandes externes

- `POST /api/admin/characters/{uniqueId}/items` (capability existante `economy.grant_item`) : attribue
  une définition et une quantité, avec la clé d'attribution **fournie par l'appelant** (`grantKey`,
  requise) — une requête rejouée porte la même clé et n'attribue rien de plus. Pour l'administration
  et les preuves du driver.
- Les opérations du joueur (déplacer, équiper…) arrivent par paquets au jalon F3 ; F2 les expose sur
  le grain, prouvées par les tests.

## 11. Concurrence et échecs

- Un seul propriétaire (le grain) : deux opérations simultanées sont sérialisées.
- Attribution rejouée (renvoi, délai) : la clé la rend sans effet ; la clé et les objets sont écrits
  dans la même transaction, donc jamais l'un sans l'autre.
- Vidage échoué : les exemplaires restent sales et repartent au vidage suivant ; journalisé.
- Définition retirée ou remplacée (spec Contenu §8) : `replace` réécrit `definition_id` ; un
  exemplaire dont la définition est inconnue reste dans le sac, sans stats, et est journalisé.

## 12. Reconnexion

Le sac est l'état du grain, pas de la session : une reconnexion ne le touche pas. Le client recevra
l'instantané complet du sac à l'entrée (F3).

## 13. Observabilité

`item_events` pour chaque création, retrait, destruction et liaison ; logs structurés des refus
(code, joueur, opération) ; compteur des vidages (durée, échecs).

## 14. Anti-triche

Le client ne désigne que des cases et des emplacements ; le serveur décide de tout le reste. Aucune
opération du joueur ne crée d'objet ; seule `GrantAsync`, côté serveur et avec une clé, le fait.
Les quantités sont bornées (> 0, ≤ pile).

## 15. Tests

- Règles pures : empilement, séparation, équipement (classe, niveau, anneaux, emplacement), liaison.
- Grain : chaque opération et chaque code de refus ; attribution avec sac plein → colis ; colis pleins
  → rien ; clé rejouée → un seul exemplaire ; récupération d'un colis.
- Persistance : vidage par lot, attribution écrite tout de suite avec sa clé (SQLite, comme
  `RoomPersistenceLossWindowTests`) ; rechargement après désactivation identique.
- Stats : équiper puis déséquiper, durabilité 0.
- Contenu : définitions valides et invalides (slot, liaison des raretés, meuble inconnu).

## 16. Critères d'acceptation

- `content/` contient les objets de démonstration ; ils s'importent.
- La route d'administration attribue une épée à un personnage du driver ; rejouée avec la même clé,
  pas de second exemplaire.
- Équipée (test d'intégration du grain), les stats du personnage montent ; tout est relu à
  l'identique après désactivation du grain.
