# RPG F2 — Objets : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** donner à chaque personnage des objets réels — un sac de 30 cases, 10 emplacements d'équipement, des colis — attribués côté serveur une seule fois par clé, équipés avec effet sur les stats, et relus à l'identique après un redémarrage.

**Architecture:** les règles sont une classe pure, `CharacterInventory` (`Vortex.Rpg/Items`), testée seule. Elle est découpée en `partial` par sujet : sac, attribution, équipement, colis. `PlayerCharacterGrain` en tient une instance (`PlayerCharacterGrain.Items.cs`) et la persiste par `CharacterItemStore` (`Grains/Persistence`) dans `rpg_items`, avec la clé d'attribution dans `rpg_item_grants`. Le grain passe les stats de l'équipement porté à `StatSourceContext` ; `EquipmentStatSource` les additionne. Les attributions, destructions, retraits et liaisons publient un event, qu'un handler d'`Vortex.Observability` écrit dans `item_events`. Une route d'administration attribue un objet ; le driver la prouve.

**Tech Stack:** C# .NET 10, Orleans (silo unique), EF Core 9 + Pomelo MySQL (SQLite en test), xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-10-08-rpg-items-design.md`, qui dépend de `2026-10-08-rpg-content-design.md` (livrée : la sorte `item`, `IContentReferences`) et de `2026-10-08-rpg-character-design.md` (livrée : `PlayerCharacterGrain`, `IStatSource`).

## Global Constraints

- **Contenance :**
  - Sac de 30 cases (`ItemConfig.BagSize = 30`).
  - 10 emplacements : tête, torse, jambes, pieds, mains, arme, main secondaire, cou, anneau 1, anneau 2.
  - Colis : 50 au plus (`ItemConfig.MaxParcels = 50`).
- **Raretés :** `common`, `uncommon`, `rare`, `epic`, `legendary` (déjà dans `ItemRarity`).
- **Liaison :**
  - Le loot de boss est lié au ramassage (`on_pickup`).
  - L'équipement Rare et plus est lié à l'équipement (`on_equip`), règle déjà vérifiée par `ItemKind`.
  - Le reste est libre.
- **Écriture :**
  - Une attribution est écrite tout de suite, dans la même transaction que sa clé.
  - Les déplacements, séparations et équipements sont écrits par lot, au plus toutes les 2 s (`ItemConfig.FlushIntervalMs = 2000`), et à la désactivation.
- **Ids de contenu :** `rpg_items.definition_id` n'est jamais réécrit depuis la mémoire du grain (AGENTS.md, Gotchas). Le vidage n'écrit que `quantity`, `durability`, `bound`, `container`, `slot`, `deleted_at`.
- **Persistance :** `container` est stocké en entier, `bag = 0`, `equipment = 1`, `parcel = 2`, jamais renuméroté.
- **Apparence :** l'équipement ne la change pas en V1.
- **Rôle du client :** il ne désigne que des cases et des emplacements, et aucune opération du joueur ne crée d'objet. Les quantités sont bornées (> 0, ≤ pile).
- **Conventions du dépôt :**
  - un type par fichier ;
  - le namespace suit le dossier ;
  - le code se modifie par Edit/Write ;
  - commits en français, avec des chemins explicites, sur `main`.
- **Couches :** le cœur ne référence pas le RPG. Les contrats vont dans `Vortex.Primitives`, et les enums persistés dans `Vortex.Primitives.<Domaine>.Enums` (DATA-MODEL.md).

## Décisions de ce plan (écarts assumés à la spec, à valider par le propriétaire)

1. **Les « modules » `CharacterBag` / `CharacterEquipment` / `CharacterParcels` (spec §6)** deviennent les `partial` d'une seule classe pure, `CharacterInventory` (`.Bag.cs`, `.Grant.cs`, `.Equipment.cs`, `.Parcels.cs`). Le grain n'a qu'un fichier, `PlayerCharacterGrain.Items.cs`.
   - **Pourquoi :** une attribution touche à la fois le sac et les colis, et un équipement touche le sac et l'équipement. Les séparer obligerait à faire circuler l'état entre trois objets.
2. **`rpg_items.id` est un `int`** (la base `VortexEntity`), pas un `bigint` comme dans la spec §5. Toutes les tables du dépôt sont en `int`.
3. **Quatre events sur neuf** : `ItemGranted`, `ItemDestroyed`, `ItemRemoved`, `ItemBound`. Ce sont ceux que l'audit consomme dès maintenant.
   - `ItemMoved`, `ItemSplit`, `ItemEquipped`, `ItemUnequipped` et `ParcelClaimed` n'ont pas encore de lecteur. Ils arrivent avec la synchronisation du client (F3b), qui en aura un ; un event sans abonné est le code mort que le lot 1 a retiré.
   - Équiper ou déséquiper publie déjà `CharacterStatsChangedEvent`.
4. **L'audit réutilise `item_events`** avec quatre types ajoutés à la fin d'`ItemEventType` (`RpgGranted`, `RpgDestroyed`, `RpgRemoved`, `RpgBound`).
   - **Pourquoi :** les ids de `rpg_items` et ceux des meubles sont deux espaces distincts. Un type `Rpg…` dit à quelle table l'`item_id` renvoie.
5. **Clé rejouée :** la réponse est `duplicate_grant`, sans rien refaire, et la route répond 200.
   - **Pourquoi :** la spec dit que « le premier résultat est rendu », mais il faudrait stocker chaque résultat. `rpg_item_grants` ne garde que la clé, et l'appelant qui rejoue sait déjà ce qu'il a demandé.
6. **Codes de refus :** `bound` et `not_stackable` (cités dans la spec §7) ne sont pas créés, aucune règle de F2 ne les produit.
   - `bound` interdira l'échange et la vente (M3).
   - Séparer un objet non empilable est une quantité invalide (`invalid_quantity`).
   - `unknown_item` est ajouté : l'attribution d'une définition inconnue ou retirée.
7. **Destruction et retrait sont écrits tout de suite** (comme l'attribution), pour que l'audit porte l'id réel de l'exemplaire. Le sac est vidé juste avant l'opération.
8. **La route d'administration prend `{playerId:int}`** comme la route d'XP existante, et non `{uniqueId}` comme dans la spec §10. Elle utilise la capability existante `Capabilities.Economy.GrantItem` (`economy.item.grant`).

## Review Focus

1. **Attribution dont l'écriture échoue** (base indisponible au commit) : rien n'est attribué. Le sac en mémoire reste celui d'avant, et la clé n'est pas consommée, donc un nouvel essai réussit. Test : Tâche 4, `AGrantWhoseWriteFails_LeavesTheBagAndTheKeyFree`.
2. **Quantité hors bornes** (0, négative, `int.MaxValue`) à l'attribution, la séparation, la destruction ou le retrait : `invalid_quantity`, rien ne change, aucun dépassement d'entier. Test : Tâche 1, `AQuantityOutOfBounds_IsRefused`.
3. **Case ou emplacement hors bornes** (−1, 30, `int.MaxValue`) envoyé par un client : `not_found`, jamais d'exception. Tests : Tâche 1, `ACaseOutOfTheBag_IsNotFound` ; Tâche 2, `AnEquipmentSlotOutOfRange_IsNotFound`.
4. **Import qui remplace une définition pendant que le grain est actif** (règle `replace`) : le grain relit l'id, et le vidage suivant ne remet pas l'ancien. Test : Tâche 4, `AReplacedDefinition_IsReReadAndNeverWrittenBack`.
5. **Séparation puis désactivation avant le vidage** : la nouvelle pile, qui n'a pas encore d'id, est écrite à la désactivation et relue à l'identique. Test : Tâche 4, `ASplitNotYetFlushed_IsWrittenAtDeactivation`.

---

### Task 1: L'inventaire pur — le sac, l'attribution, les colis

**Files:**
- Create: `Vortex.Primitives/Rpg/Items/Enums/ItemContainer.cs`
- Create: `Vortex.Primitives/Rpg/Items/ItemOutcome.cs`, `ItemGrantLine.cs`, `ItemGrantResult.cs`, `CharacterItemSnapshot.cs`
- Create: `Vortex.Rpg/Items/ItemInstance.cs`, `InventoryLimits.cs`, `ItemQuantity.cs`, `GrantPlan.cs`, `EquipmentSlots.cs`
- Create: `Vortex.Rpg/Items/CharacterInventory.cs`, `CharacterInventory.Bag.cs`, `CharacterInventory.Grant.cs`, `CharacterInventory.Parcels.cs`
- Test: `Vortex.Rpg.Tests/Items/TestItems.cs`, `CharacterInventoryBagTests.cs`, `CharacterInventoryGrantTests.cs`, `CharacterInventoryParcelTests.cs`

**Interfaces:**
- Produces:
  - `enum ItemContainer { Bag = 0, Equipment = 1, Parcel = 2 }` (`Vortex.Primitives.Rpg.Items.Enums`, persisté).
  - `enum ItemOutcome { Ok = 0, NoCharacter = 1, NotFound = 2, BagFull = 3, SlotOccupied = 4, WrongClass = 5, LevelTooLow = 6, NotEquipment = 7, InvalidQuantity = 8, ParcelsFull = 9, DuplicateGrant = 10, UnknownItem = 11 }` (`Vortex.Primitives.Rpg.Items`). Le web API l'envoie en snake_case.
  - `[GenerateSerializer, Immutable] sealed record ItemGrantLine { [Id(0)] required string DefinitionId; [Id(1)] required int Quantity; }`.
  - `[GenerateSerializer, Immutable] sealed record ItemGrantResult { [Id(0)] required ItemOutcome Outcome; [Id(1)] int ToBag; [Id(2)] int ToParcels; }`.
  - `[GenerateSerializer, Immutable] sealed record CharacterItemSnapshot { [Id(0)] int ItemId; [Id(1)] required string DefinitionId; [Id(2)] int Quantity; [Id(3)] int? Durability; [Id(4)] bool Bound; [Id(5)] ItemContainer Container; [Id(6)] int Slot; }`.
  - `sealed class ItemInstance { int Id; required string DefinitionId; int Quantity; int? Durability; bool Bound; ItemContainer Container; int Slot; bool Dirty; bool Deleted; ItemInstance Copy(); }` (tout en `{ get; set; }`).
  - `sealed record InventoryLimits(int BagSize, int MaxParcels)`.
  - `sealed record ItemQuantity(ItemInstance Item, int Quantity)`.
  - `sealed record GrantPlan(ItemOutcome Outcome, int ToBag, int ToParcels, IReadOnlyList<ItemQuantity> Placed) { static GrantPlan Refused(ItemOutcome outcome); }`.
  - `static class EquipmentSlots { const int Ring1 = 8; const int Ring2 = 9; const int Count = 10; static bool IsValid(int slot); }`.
  - `sealed partial class CharacterInventory`, avec :
    - `static CharacterInventory Load(InventoryLimits, Func<string, ItemDefinition?>, IEnumerable<ItemInstance>)` ;
    - `IEnumerable<ItemInstance> Live` et `IReadOnlyList<ItemInstance> Dirty` ;
    - `CharacterInventory Clone()` et `void MarkWritten()` ;
    - `ItemInstance? At(ItemContainer, int)` et `CharacterItemSnapshot[] Snapshot()` ;
    - `ItemOutcome Move(int from, int to)`, `ItemOutcome Split(int from, int quantity, int to)`, `ItemOutcome Destroy(int slot, int quantity)` ;
    - `(ItemOutcome Outcome, IReadOnlyList<ItemQuantity> Taken) Remove(string definitionId, int quantity)` ;
    - `GrantPlan Grant(IReadOnlyList<ItemGrantLine> lines)` et `ItemOutcome ClaimParcel(int parcel)`.

- [ ] **Step 1: Les contrats de `Vortex.Primitives`.** Écrire les cinq fichiers ci-dessus. Le style est celui de `Vortex.Primitives/Rpg/Characters/CharacterSummary.cs` (`[GenerateSerializer, Immutable]`, `[Id(n)]` sur chaque propriété) et de `ChooseClassOutcome.cs` pour l'enum de réponse. Le résumé d'`ItemContainer` rappelle « stocké dans `rpg_items.container` : jamais renuméroté ».

- [ ] **Step 2: Les jeux de test.** `Vortex.Rpg.Tests/Items/TestItems.cs`. Les définitions suivent `content/item/*.json` :

```csharp
using System;
using System.Linq;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Items;

namespace Vortex.Rpg.Tests.Items;

/// <summary>Item definitions shaped like content/item, and inventories over them.</summary>
internal static class TestItems
{
    public static readonly ItemDefinition Sword = new()
    {
        Id = "item.weapon.rusted_sword", Kind = "item",
        Category = ItemCategory.Equipment, Rarity = ItemRarity.Common,
        Slot = EquipmentSlot.Weapon, AllowedClasses = ["class.warrior"],
        Stats = new StatBlock { Strength = 3 }, MaxDurability = 50,
    };

    public static readonly ItemDefinition Ring = new()
    {
        Id = "item.ring.copper_ring", Kind = "item",
        Category = ItemCategory.Equipment, Rarity = ItemRarity.Common,
        Slot = EquipmentSlot.Ring, Stats = new StatBlock { Agility = 1 }, MaxDurability = 40,
    };

    /// <summary>Rare: binds when equipped, and only from level 5.</summary>
    public static readonly ItemDefinition Blade = new()
    {
        Id = "item.weapon.amber_blade", Kind = "item",
        Category = ItemCategory.Equipment, Rarity = ItemRarity.Rare,
        Slot = EquipmentSlot.Weapon, RequiredLevel = 5,
        Stats = new StatBlock { Strength = 9 }, Binding = ItemBinding.OnEquip,
    };

    public static readonly ItemDefinition Shard = new()
    {
        Id = "item.material.amber_shard", Kind = "item",
        Category = ItemCategory.Material, Rarity = ItemRarity.Common, MaxStack = 50,
    };

    public static readonly ItemDefinition Letter = new()
    {
        Id = "item.quest.sealed_letter", Kind = "item",
        Category = ItemCategory.Quest, Rarity = ItemRarity.Common, Binding = ItemBinding.OnPickup,
    };

    public static readonly ItemDefinition Retired = Shard with { Id = "item.material.old_shard", Retired = true };

    private static readonly ItemDefinition[] All = [Sword, Ring, Blade, Shard, Letter, Retired];

    public static CharacterInventory Inventory(int bagSize = 30, int maxParcels = 50, params ItemInstance[] items) =>
        CharacterInventory.Load(new InventoryLimits(bagSize, maxParcels), Find, items);

    public static ItemInstance InBag(ItemDefinition definition, int slot, int quantity = 1) => new()
    {
        Id = 100 + slot, DefinitionId = definition.Id, Quantity = quantity,
        Durability = definition.MaxDurability > 0 ? definition.MaxDurability : null,
        Container = Vortex.Primitives.Rpg.Items.Enums.ItemContainer.Bag, Slot = slot,
    };

    private static ItemDefinition? Find(string id) => All.FirstOrDefault(d => d.Id == id);
}
```

- [ ] **Step 3: Écrire les tests qui échouent.**

`CharacterInventoryBagTests` :
- `AMove_ToAnEmptyCase_MovesAndMarksDirty`
- `AMove_OntoAnotherItem_Swaps`
- `AMove_OntoTheSameStack_MergesUpToTheMaxAndLeavesTheRest` : 30 éclats en case 0, 40 en case 1, `Move(0, 1)` ; la case 1 passe à 50 et la case 0 garde 20.
- `AMove_OfAWholeStackIntoAnother_DeletesTheSource`
- `ACaseOutOfTheBag_IsNotFound` : en théorie sur `(-1, 0)`, `(0, 30)`, `(0, int.MaxValue)`, et un `Move` depuis une case vide. Chaque refus laisse `Dirty` vide.
- `ASplit_MakesANewStackWithoutAnId`
- `ASplit_OntoAnOccupiedCase_IsSlotOccupied`
- `AQuantityOutOfBounds_IsRefused` : `Split` avec 0, la pile entière, −1 ; `Destroy` avec 0, plus que la pile, `int.MaxValue` ; `Remove` avec 0. Tout répond `InvalidQuantity` et `Dirty` reste vide.
- `ADestroy_OfPartOfAStack_KeepsTheRest`
- `ADestroy_OfAWholeStack_DeletesIt`
- `ARemove_TakesFromStacksInOrder_AllOrNothing` : 10 + 10 éclats. `Remove(15)` laisse la case 0 supprimée et la case 1 à 5, et `Taken` liste (case 0, 10) et (case 1, 5). `Remove(16)` répond `NotFound` sans rien changer.
- `MarkWritten_DropsDeletedCopiesAndClearsDirty`
- `AClone_IsIndependent` : un `Move` sur le clone ne change pas l'original.

`CharacterInventoryGrantTests` :
- `AGrant_FillsStacksThenEmptyCases` : 45 éclats en case 0, attribution de 10. La case 0 passe à 50, une nouvelle pile de 5 apparaît en case 1, et `ToBag` vaut 10.
- `AFullBag_SendsTheRestToParcels` : sac de 1 case, occupé par l'épée. L'attribution de 20 éclats crée un colis n° 0 de 20 ; `ToParcels` vaut 20, `ToBag` 0.
- `FullParcels_GrantNothing` : sac de 1 case occupé, 1 colis au plus, déjà pris. L'attribution d'éclats répond `ParcelsFull`, et `Live` comme `Dirty` restent tels quels.
- `AGrantOfSeveralLines_IsWholeOrNothing` : épée + définition inconnue répond `UnknownItem`, et l'épée n'est pas placée.
- `AnUnknownOrRetiredItem_IsRefused`
- `AQuantityOutOfBounds_IsRefused` : 0, −1 et `int.MaxValue` répondent `InvalidQuantity`. Une liste vide aussi.
- `APickupBindingItem_IsBoundAtGrant` : la lettre, `Bound` vaut true.
- `ABoundAndAFreeCopy_DoNotStack`
- `AnEquipment_StartsAtFullDurability` : l'épée a 50, l'éclat `null`.

`CharacterInventoryParcelTests` :
- `AClaim_GoesIntoStacksThenAnEmptyCase`
- `AClaim_WithAFullBag_IsBagFullAndChangesNothing`
- `AnUnknownParcel_IsNotFound`

Forme d'un test (les autres suivent la même) :

```csharp
[Fact]
public void FullParcels_GrantNothing()
{
    ItemInstance parcel = new()
    {
        Id = 200, DefinitionId = TestItems.Shard.Id, Quantity = 50,
        Container = ItemContainer.Parcel, Slot = 0,
    };
    CharacterInventory inventory = TestItems.Inventory(
        bagSize: 1, maxParcels: 1, TestItems.InBag(TestItems.Sword, 0), parcel);

    GrantPlan plan = inventory.Grant([new ItemGrantLine { DefinitionId = TestItems.Shard.Id, Quantity = 1 }]);

    plan.Outcome.Should().Be(ItemOutcome.ParcelsFull);
    inventory.Live.Should().HaveCount(2);
    inventory.Dirty.Should().BeEmpty();
}
```

- [ ] **Step 4: Les lancer — échec attendu.** `dotnet test Vortex.Rpg.Tests --filter "FullyQualifiedName~Items"`. Attendu : la compilation échoue, `CharacterInventory` n'existe pas.

- [ ] **Step 5: Implémenter.** Résumé de chaque fichier : la classe pure, sans grain ni base. **Chaque opération vérifie tout avant de modifier quoi que ce soit.**

`CharacterInventory.cs` (le socle) :

```csharp
public sealed partial class CharacterInventory
{
    private readonly InventoryLimits _limits;
    private readonly Func<string, ItemDefinition?> _definitions;
    private readonly List<ItemInstance> _items;

    private CharacterInventory(InventoryLimits limits, Func<string, ItemDefinition?> definitions, List<ItemInstance> items)
    {
        _limits = limits;
        _definitions = definitions;
        _items = items;
    }

    public static CharacterInventory Load(InventoryLimits limits, Func<string, ItemDefinition?> definitions, IEnumerable<ItemInstance> items) =>
        new(limits, definitions, [.. items]);

    /// <summary>The copies held; deleted ones wait in the list for the next write.</summary>
    public IEnumerable<ItemInstance> Live => _items.Where(i => !i.Deleted);

    /// <summary>What changed since the last write, deleted copies included.</summary>
    public IReadOnlyList<ItemInstance> Dirty => [.. _items.Where(i => i.Dirty)];

    /// <summary>A deep copy: what must be written before it counts (a grant) is applied to one,
    /// which replaces the original once written.</summary>
    public CharacterInventory Clone() => new(_limits, _definitions, [.. _items.Select(i => i.Copy())]);

    /// <summary>The dirty copies were written: deleted ones leave, the rest are clean.</summary>
    public void MarkWritten()
    {
        _items.RemoveAll(i => i.Deleted);

        foreach (ItemInstance item in _items)
        {
            item.Dirty = false;
        }
    }

    public ItemInstance? At(ItemContainer container, int slot) =>
        Live.FirstOrDefault(i => i.Container == container && i.Slot == slot);

    public CharacterItemSnapshot[] Snapshot() =>
        [.. Live.OrderBy(i => i.Container).ThenBy(i => i.Slot).Select(i => new CharacterItemSnapshot
        {
            ItemId = i.Id, DefinitionId = i.DefinitionId, Quantity = i.Quantity,
            Durability = i.Durability, Bound = i.Bound, Container = i.Container, Slot = i.Slot,
        })];

    private ItemDefinition? Definition(ItemInstance item) => _definitions(item.DefinitionId);

    private bool InBag(int slot) => slot >= 0 && slot < _limits.BagSize;

    private int? FirstEmptyBagSlot()
    {
        for (int slot = 0; slot < _limits.BagSize; slot++)
        {
            if (At(ItemContainer.Bag, slot) is null)
            {
                return slot;
            }
        }

        return null;
    }

    /// <summary>Two copies of one stackable item merge only when both are bound or both free.</summary>
    private static bool Stacks(ItemInstance a, ItemInstance b) =>
        a.DefinitionId == b.DefinitionId && a.Bound == b.Bound;

    private static void Touch(ItemInstance item) => item.Dirty = true;

    private static void Delete(ItemInstance item)
    {
        item.Deleted = true;
        item.Dirty = true;
    }
}
```

`CharacterInventory.Bag.cs` — `Move`, `Split`, `Destroy`, `Remove` :

```csharp
public ItemOutcome Move(int from, int to)
{
    if (!InBag(from) || !InBag(to) || At(ItemContainer.Bag, from) is not { } source)
    {
        return ItemOutcome.NotFound;
    }

    if (from == to)
    {
        return ItemOutcome.Ok;
    }

    ItemInstance? target = At(ItemContainer.Bag, to);

    if (target is null)
    {
        source.Slot = to;
        Touch(source);

        return ItemOutcome.Ok;
    }

    if (Definition(source) is { MaxStack: > 1 } definition && Stacks(source, target))
    {
        // Merged up to the stack size; what does not fit stays where it was.
        int moved = Math.Min(definition.MaxStack - target.Quantity, source.Quantity);

        if (moved > 0)
        {
            target.Quantity += moved;
            Touch(target);
            source.Quantity -= moved;

            if (source.Quantity == 0) { Delete(source); } else { Touch(source); }
        }

        return ItemOutcome.Ok;
    }

    source.Slot = to;
    target.Slot = from;
    Touch(source);
    Touch(target);

    return ItemOutcome.Ok;
}

public ItemOutcome Split(int from, int quantity, int to)
{
    if (!InBag(from) || !InBag(to) || At(ItemContainer.Bag, from) is not { } source)
    {
        return ItemOutcome.NotFound;
    }

    if (quantity <= 0 || quantity >= source.Quantity)
    {
        return ItemOutcome.InvalidQuantity;
    }

    if (At(ItemContainer.Bag, to) is not null)
    {
        return ItemOutcome.SlotOccupied;
    }

    ItemInstance part = source.Copy();
    part.Id = 0;
    part.Quantity = quantity;
    part.Slot = to;
    Touch(part);
    _items.Add(part);

    source.Quantity -= quantity;
    Touch(source);

    return ItemOutcome.Ok;
}

public ItemOutcome Destroy(int slot, int quantity)
{
    if (!InBag(slot) || At(ItemContainer.Bag, slot) is not { } item)
    {
        return ItemOutcome.NotFound;
    }

    if (quantity <= 0 || quantity > item.Quantity)
    {
        return ItemOutcome.InvalidQuantity;
    }

    if (quantity == item.Quantity) { Delete(item); } else { item.Quantity -= quantity; Touch(item); }

    return ItemOutcome.Ok;
}

/// <summary>Server side (quests, crafting): from the bag's stacks in case order, all or nothing.</summary>
public (ItemOutcome Outcome, IReadOnlyList<ItemQuantity> Taken) Remove(string definitionId, int quantity)
{
    if (quantity <= 0)
    {
        return (ItemOutcome.InvalidQuantity, []);
    }

    List<ItemInstance> stacks =
        [.. Live.Where(i => i.Container == ItemContainer.Bag && i.DefinitionId == definitionId).OrderBy(i => i.Slot)];

    if (stacks.Sum(i => (long)i.Quantity) < quantity)
    {
        return (ItemOutcome.NotFound, []);
    }

    List<ItemQuantity> taken = [];
    int left = quantity;

    foreach (ItemInstance stack in stacks)
    {
        int take = Math.Min(left, stack.Quantity);
        taken.Add(new ItemQuantity(stack, take));
        stack.Quantity -= take;
        left -= take;

        if (stack.Quantity == 0) { Delete(stack); } else { Touch(stack); }

        if (left == 0)
        {
            break;
        }
    }

    return (ItemOutcome.Ok, taken);
}
```

`CharacterInventory.Grant.cs` — vérifié ligne par ligne, puis placé sur un clone, adopté seulement si tout a trouvé sa place :

```csharp
public GrantPlan Grant(IReadOnlyList<ItemGrantLine> lines)
{
    if (lines.Count == 0)
    {
        return GrantPlan.Refused(ItemOutcome.InvalidQuantity);
    }

    foreach (ItemGrantLine line in lines)
    {
        if (_definitions(line.DefinitionId) is not { Retired: false } definition)
        {
            return GrantPlan.Refused(ItemOutcome.UnknownItem);
        }

        // Above what bag and parcels could ever hold, it cannot fit; refusing here also keeps the
        // arithmetic below far from int overflow.
        if (line.Quantity < 1 || line.Quantity > (long)definition.MaxStack * (_limits.BagSize + _limits.MaxParcels))
        {
            return GrantPlan.Refused(ItemOutcome.InvalidQuantity);
        }
    }

    CharacterInventory work = Clone();
    GrantPlan plan = work.Place(lines);

    if (plan.Outcome == ItemOutcome.Ok)
    {
        _items.Clear();
        _items.AddRange(work._items);
    }

    return plan;
}

/// <summary>Stacks first, then empty cases, then parcels (spec Objets §7).</summary>
private GrantPlan Place(IReadOnlyList<ItemGrantLine> lines)
{
    List<ItemQuantity> placed = [];
    int toBag = 0;
    int toParcels = 0;

    foreach (ItemGrantLine line in lines)
    {
        ItemDefinition definition = _definitions(line.DefinitionId)!;
        bool bound = definition.Binding == ItemBinding.OnPickup;
        int left = line.Quantity;

        if (definition.MaxStack > 1)
        {
            foreach (ItemInstance stack in Live
                .Where(i => i.Container == ItemContainer.Bag && i.DefinitionId == definition.Id && i.Bound == bound && i.Quantity < definition.MaxStack)
                .OrderBy(i => i.Slot)
                .ToList())
            {
                int add = Math.Min(left, definition.MaxStack - stack.Quantity);
                stack.Quantity += add;
                Touch(stack);
                placed.Add(new ItemQuantity(stack, add));
                toBag += add;
                left -= add;

                if (left == 0)
                {
                    break;
                }
            }
        }

        while (left > 0 && FirstEmptyBagSlot() is { } slot)
        {
            int add = Math.Min(left, definition.MaxStack);
            placed.Add(new ItemQuantity(Create(definition, add, bound, ItemContainer.Bag, slot), add));
            toBag += add;
            left -= add;
        }

        while (left > 0)
        {
            if (ParcelCount() >= _limits.MaxParcels)
            {
                return GrantPlan.Refused(ItemOutcome.ParcelsFull);
            }

            int add = Math.Min(left, definition.MaxStack);
            placed.Add(new ItemQuantity(Create(definition, add, bound, ItemContainer.Parcel, NextParcelSlot()), add));
            toParcels += add;
            left -= add;
        }
    }

    return new GrantPlan(ItemOutcome.Ok, toBag, toParcels, placed);
}

private ItemInstance Create(ItemDefinition definition, int quantity, bool bound, ItemContainer container, int slot)
{
    ItemInstance item = new()
    {
        DefinitionId = definition.Id,
        Quantity = quantity,
        Durability = definition.MaxDurability > 0 ? definition.MaxDurability : null,
        Bound = bound,
        Container = container,
        Slot = slot,
        Dirty = true,
    };
    _items.Add(item);

    return item;
}
```

`CharacterInventory.Parcels.cs` — `ParcelCount()`, `NextParcelSlot()` (le plus grand numéro + 1, 0 sans colis) et `ClaimParcel`. Le colis n'entre que s'il tient en entier : la place restante dans les piles d'abord, sinon une case vide pour le reste.

```csharp
public ItemOutcome ClaimParcel(int parcel)
{
    if (At(ItemContainer.Parcel, parcel) is not { } item)
    {
        return ItemOutcome.NotFound;
    }

    int maxStack = Definition(item)?.MaxStack ?? 1;
    List<ItemInstance> stacks = maxStack > 1
        ? [.. Live.Where(i => i.Container == ItemContainer.Bag && Stacks(i, item) && i.Quantity < maxStack).OrderBy(i => i.Slot)]
        : [];
    int room = stacks.Sum(s => maxStack - s.Quantity);
    int? empty = FirstEmptyBagSlot();

    if (room < item.Quantity && empty is null)
    {
        return ItemOutcome.BagFull;
    }

    int left = item.Quantity;

    foreach (ItemInstance stack in stacks)
    {
        int add = Math.Min(left, maxStack - stack.Quantity);
        stack.Quantity += add;
        Touch(stack);
        left -= add;

        if (left == 0)
        {
            break;
        }
    }

    if (left == 0)
    {
        Delete(item);
    }
    else
    {
        item.Quantity = left;
        item.Container = ItemContainer.Bag;
        item.Slot = empty!.Value;
        Touch(item);
    }

    return ItemOutcome.Ok;
}
```

`ItemInstance.Copy()` vaut `(ItemInstance)MemberwiseClone()`. `GrantPlan.Refused(o)` vaut `new(o, 0, 0, [])`. Pour `EquipmentSlots.IsValid(slot)`, la règle est `slot is >= 0 and < Count`.

- [ ] **Step 6: Les lancer — succès attendu.** `dotnet test Vortex.Rpg.Tests` : tout est vert.

- [ ] **Step 7: Vérifications de forme.** Lancer `node scripts/hooks/check-code-layout.mjs` (OK) et `dotnet csharpier check .` (le hook a déjà formaté).

- [ ] **Step 8: Commit.**

```bash
git add Vortex.Primitives/Rpg/Items Vortex.Rpg/Items Vortex.Rpg.Tests/Items
git commit -o Vortex.Primitives/Rpg/Items Vortex.Rpg/Items Vortex.Rpg.Tests/Items -m "objets : l'inventaire pur — sac, attribution, colis"
```

---

### Task 2: L'équipement pur et ses stats

**Files:**
- Create: `Vortex.Rpg/Items/CharacterInventory.Equipment.cs`
- Modify: `Vortex.Rpg/Stats/StatSourceContext.cs`
- Create: `Vortex.Rpg.Characters/Stats/EquipmentStatSource.cs`
- Modify: `Vortex.Rpg.Characters/CharactersModule.cs` (une ligne `IStatSource`)
- Test: `Vortex.Rpg.Tests/Items/CharacterInventoryEquipmentTests.cs`, `Vortex.Database.Tests/Rpg/EquipmentStatSourceTests.cs`
  - Ce second fichier va dans `Vortex.Database.Tests` parce que `Vortex.Rpg.Characters` n'a pas de projet de test à lui et que ses tests vivent déjà là.

**Interfaces:**
- Consumes: `CharacterInventory`, `EquipmentSlots`, `TestItems` (Tâche 1).
- Produces:
  - `ItemOutcome Equip(int bagSlot, string classId, int level)` ;
  - `ItemOutcome Unequip(int equipmentSlot)` ;
  - `IReadOnlyList<StatBlock> EquippedStats()` ;
  - sur `StatSourceContext`, la propriété `IReadOnlyList<StatBlock> Equipment { get; init; } = [];` ;
  - `EquipmentStatSource : IStatSource` avec `Name => "equipment"`.

- [ ] **Step 1: Écrire les tests qui échouent** (`CharacterInventoryEquipmentTests`).
  - `AWeapon_GoesToTheWeaponSlot_AndTheWornOneComesBackToTheCase`
  - `Rings_FillRingOneThenRingTwo_ThenSwapRingOne`
  - `AnotherClassesItem_IsWrongClass` : l'épée équipée par `class.mage`.
  - `AnItemAboveTheLevel_IsLevelTooLow` : la lame au niveau 4.
  - `AMaterial_IsNotEquipment`
  - `EquipOnBindingItem_IsBoundWhenEquipped` : la lame au niveau 5 a `Bound` à true.
  - `AnUnequip_GoesToTheFirstEmptyCase`
  - `AnUnequip_WithAFullBag_IsBagFullAndChangesNothing`
  - `AnEquipmentSlotOutOfRange_IsNotFound` : `Unequip(-1)`, `Unequip(10)`, `Unequip(int.MaxValue)` et `Equip(30, …)`.
  - `EquippedStats_LeaveOutBrokenAndUnknownItems` :
    - une épée à durabilité 0 ne compte pas ;
    - un exemplaire de définition inconnue ne compte pas ;
    - une bague à 40 compte, avec `Agility = 1`.

  Dans `EquipmentStatSourceTests` :
  - `TheEquipment_IsSummed` : `{Strength 3}` et `{Agility 1, Strength 2}` donnent `{Strength 5, Agility 1}` ;
  - `NoEquipment_IsZero`.

- [ ] **Step 2: Les lancer — échec attendu** (compilation).

- [ ] **Step 3: Implémenter.**

```csharp
// CharacterInventory.Equipment.cs
public ItemOutcome Equip(int bagSlot, string classId, int level)
{
    if (!InBag(bagSlot) || At(ItemContainer.Bag, bagSlot) is not { } item)
    {
        return ItemOutcome.NotFound;
    }

    if (Definition(item) is not { Category: ItemCategory.Equipment, Slot: { } slot } definition)
    {
        return ItemOutcome.NotEquipment;
    }

    if (level < definition.RequiredLevel)
    {
        return ItemOutcome.LevelTooLow;
    }

    if (definition.AllowedClasses.Count > 0 && !definition.AllowedClasses.Contains(classId))
    {
        return ItemOutcome.WrongClass;
    }

    int target = slot == EquipmentSlot.Ring ? RingSlot() : (int)slot;
    ItemInstance? worn = At(ItemContainer.Equipment, target);

    item.Container = ItemContainer.Equipment;
    item.Slot = target;
    item.Bound |= definition.Binding == ItemBinding.OnEquip;
    Touch(item);

    if (worn is not null)
    {
        worn.Container = ItemContainer.Bag;
        worn.Slot = bagSlot;
        Touch(worn);
    }

    return ItemOutcome.Ok;
}

public ItemOutcome Unequip(int equipmentSlot)
{
    if (!EquipmentSlots.IsValid(equipmentSlot) || At(ItemContainer.Equipment, equipmentSlot) is not { } item)
    {
        return ItemOutcome.NotFound;
    }

    if (FirstEmptyBagSlot() is not { } slot)
    {
        return ItemOutcome.BagFull;
    }

    item.Container = ItemContainer.Bag;
    item.Slot = slot;
    Touch(item);

    return ItemOutcome.Ok;
}

/// <summary>What the worn items add (spec Objets §8): nothing from a copy at durability 0, nor
/// from a definition the content no longer has.</summary>
public IReadOnlyList<StatBlock> EquippedStats() =>
    [.. Live.Where(i => i.Container == ItemContainer.Equipment)
        .Select(i => (Item: i, Definition: Definition(i)))
        .Where(x => x.Definition?.Stats is not null && (x.Definition.MaxDurability == 0 || x.Item.Durability > 0))
        .Select(x => x.Definition!.Stats!)];

/// <summary>Ring 1 if free, else ring 2 if free, else ring 1, whose ring is swapped out.</summary>
private int RingSlot() =>
    At(ItemContainer.Equipment, EquipmentSlots.Ring1) is null ? EquipmentSlots.Ring1
    : At(ItemContainer.Equipment, EquipmentSlots.Ring2) is null ? EquipmentSlots.Ring2
    : EquipmentSlots.Ring1;
```

Dans `StatSourceContext`, remplacer la phrase du résumé « (a field added with the Objets plan) » par la propriété :

```csharp
public sealed record StatSourceContext(PlayerId PlayerId, ClassDefinition? Class, int Level)
{
    /// <summary>The stats of the items worn and not broken, handed in by the character grain.</summary>
    public IReadOnlyList<StatBlock> Equipment { get; init; } = [];
}
```

`EquipmentStatSource` additionne chaque champ de `context.Equipment` dans un nouveau `StatBlock` (même forme que `ClassLevelStatSource`). Il est déclaré dans `CharactersModule` après la source de classe, avec `services.AddSingleton<IStatSource, EquipmentStatSource>();`.

- [ ] **Step 4: Les lancer — succès attendu.** Lancer `dotnet test Vortex.Rpg.Tests` et `dotnet test Vortex.Database.Tests --filter "FullyQualifiedName~EquipmentStatSource"`.

- [ ] **Step 5: Commit** (`objets : l'équipement pur et la source de stats de l'équipement`).

- [ ] **Step 6: Relecture des tâches 1–2** par un agent relecteur général (pas de `Grains/` touché), sur ces deux commits. Corriger les Critical et Important, chacun avec un test qui échoue d'abord, et noter le résultat dans le registre `.superpowers/sdd/2026-10-09-rpg-objets/progress.md`.

---

### Task 3: Les tables `rpg_items` et `rpg_item_grants`

Suivre le skill `changing-the-schema`.

**Files:**
- Create: `Vortex.Database/Entities/Rpg/RpgItemEntity.cs`, `RpgItemGrantEntity.cs`
- Modify: `Vortex.Database/Context/VortexDbContext.cs` (`RpgItems`, `RpgItemGrants`)
- Regenerate: `Vortex.Database/Migrations/*_InitialCreate*.cs`, `VortexDbContextModelSnapshot.cs`
- Create: `scripts/sql/2026-10-09-rpg-items.sql`
- Create: `Vortex.Rpg.Characters/Content/ItemReferences.cs` ; Modify: `CharactersModule.cs`
- Test: `Vortex.Database.Tests/Rpg/ItemReferencesTests.cs`

**Interfaces:**
- Produces:
  - `RpgItemEntity : VortexEntity`, avec :
    - `required int PlayerEntityId` (`player_id`, FK `players`, cascade) ;
    - `required string DefinitionId` (`definition_id`, `[MaxLength(ContentIdLength)]`) ;
    - `int Quantity` (`quantity`) et `int? Durability` (`durability`) ;
    - `bool Bound` (`bound`) ;
    - `ItemContainer Container` (`container`, `[DefaultValue(ItemContainer.Bag)]`, `[DatabaseGenerated(DatabaseGeneratedOption.None)]`) ;
    - `int Slot` (`slot`) ;
    - index sur `PlayerEntityId` et sur `DefinitionId`.
  - `RpgItemGrantEntity : VortexEntity`, avec :
    - `public const int GrantKeyLength = 128;` ;
    - `required string GrantKey` (`grant_key`, unique, `[MaxLength(GrantKeyLength)]`) ;
    - `required int PlayerEntityId` (`player_id`, FK `players`, cascade).
  - `ItemReferences : IContentReferences`, avec :
    - `Kind => "item"` ;
    - `IsReferencedAsync`, qui cherche une ligne `rpg_items` non supprimée de cet id ;
    - `ReplaceAsync`, un `ExecuteUpdate` de `definition_id`.

- [ ] **Step 1: Test qui échoue** (`ItemReferencesTests`, SQLite comme `CharacterContentReferencesTests`) :
  - `AHeldItem_IsReferenced` ;
  - `ADeletedCopy_IsNot` ;
  - `AReplace_RewritesTheDefinitionOfEveryCopy`.

- [ ] **Step 2: Les entités et `ItemReferences`**, puis le test passe : `dotnet test Vortex.Database.Tests --filter "FullyQualifiedName~ItemReferences"`.

- [ ] **Step 3: Régénérer.** `node .claude/skills/changing-the-schema/scripts/regenerate-initialcreate.mjs`. Lire le `Up()` généré : exactement deux tables nouvelles, leurs index et leurs FK, et le bloc de seeds inchangé.

- [ ] **Step 4: Base neuve.** `pwsh .claude/skills/changing-the-schema/scripts/fresh-db.ps1 -Name vortex_schema_check` : migration et import OK.

- [ ] **Step 5: Script pour une base existante.** `scripts/sql/2026-10-09-rpg-items.sql`, sur le modèle de `2026-10-08-player-characters.sql`, additif et rejouable :
  - les deux `CREATE TABLE IF NOT EXISTS`, copiés de `SHOW CREATE TABLE` sur `vortex_schema_check` ;
  - puis `UPDATE __EFMigrationsHistory SET MigrationId = '<nouvel id>_InitialCreate' WHERE MigrationId = '20261008163542_InitialCreate';`.

  L'appliquer à `ambreia-database` (base des preuves du driver) avec `mysql` et `MYSQL_PWD`, sans jamais afficher le mot de passe. Vérifier `SHOW TABLES LIKE 'rpg_item%'`.

- [ ] **Step 6: Tests et commit.** Lancer `dotnet test Vortex.Database.Tests` (vert, `SeedCatalogTests` compris), puis commit `base : tables rpg_items et rpg_item_grants` avec les entités, le contexte, les migrations, le script, `ItemReferences`, le module et le test.

---

### Task 4: Les objets dans le grain du personnage

**Files:**
- Create: `Vortex.Rpg.Characters/Configuration/ItemConfig.cs`
- Create: `Vortex.Rpg.Characters/Grains/Persistence/CharacterItemStore.cs`
- Create: `Vortex.Rpg.Characters/Grains/PlayerCharacterGrain.Items.cs`
- Modify:
  - `PlayerCharacterGrain.cs` : le constructeur reçoit `IOptions<ItemConfig> itemConfig` **en dernier**, `OnActivateAsync` arme le timer des objets, `OnDeactivateAsync` vide les objets, et `ComputeStatsAsync` passe `Equipment`.
- Modify: `Vortex.Rpg.Characters/CharactersModule.cs` (`services.Configure<ItemConfig>(…section "Vortex:Items")`)
- Modify: `Vortex.Primitives/Rpg/Characters/Grains/IPlayerCharacterGrain.cs`
- Modify: `Vortex.Primitives/Observability/IVortexMetrics.cs`, `Vortex.Observability/Metrics/VortexMetrics.cs` (`ItemFlushCompleted`)
- Modify, car chacun construit `PlayerCharacterGrain` par `CreateWithIntegerKey`, qui place ses arguments par position : `Vortex.Database.Tests/Rpg/{PlayerCharacterGrainTests,PlayerCharacterXpTests,CharacterStatePushTests,LastZoneTests,CharacterContentReferencesTests}.cs`. Ajouter `Options.Create(new ItemConfig())` en dernier argument.
- Test: `Vortex.Database.Tests/Rpg/ItemContent.cs`, `CharacterItemsTests.cs`
- Modify: `Vortex.WebApi.Tests/FakeCharacters.cs` (rien : le faux répond `null` aux méthodes inconnues, mais `IPlayerCharacterGrain` gagne des méthodes, à vérifier à la compilation)

**Interfaces:**
- Consumes: Tâches 1–3.
- Produces, sur `IPlayerCharacterGrain` (tous répondent `NoCharacter`, ou `[]`, pour un avatar sans classe) :
  - `Task<ItemGrantResult> GrantItemsAsync(string grantKey, ItemGrantLine[] items)` — côté serveur seulement ;
  - `Task<ItemOutcome> RemoveItemsAsync(string definitionId, int quantity)` — côté serveur seulement ;
  - `Task<ItemOutcome> MoveItemAsync(int from, int to)`, `SplitItemAsync(int from, int quantity, int to)` ;
  - `Task<ItemOutcome> EquipItemAsync(int bagSlot)`, `UnequipItemAsync(int equipmentSlot)` ;
  - `Task<ItemOutcome> DestroyItemAsync(int bagSlot, int quantity)`, `ClaimParcelAsync(int parcel)` ;
  - `Task<CharacterItemSnapshot[]> GetItemsAsync()`.
- Produces, sur `CharacterItemStore(IDbContextFactory<VortexDbContext>)` :
  - `Task<List<ItemInstance>> LoadAsync(PlayerId, CancellationToken)` ;
  - `Task<bool> GrantKeySeenAsync(string, CancellationToken)` ;
  - `Task WriteAsync(PlayerId, IReadOnlyList<ItemInstance> dirty, string? grantKey, CancellationToken)` ;
  - `Task<Dictionary<int, string>> ReadDefinitionIdsAsync(PlayerId, CancellationToken)`.
- Produces: `void IVortexMetrics.ItemFlushCompleted(double elapsedMilliseconds, bool failed)` (histogramme `Vortex.item.flush.duration`, étiquette `outcome`).

- [ ] **Step 1: Le contenu de test.** `ItemContent.cs` (`internal static class`) reprend l'épée et l'éclat de `TestItems`, avec les mêmes valeurs. On ne partage pas entre projets de test.

- [ ] **Step 2: Écrire les tests qui échouent** (`CharacterItemsTests`, SQLite, activation comme `PlayerCharacterGrainTests.ActivateAsync`).
  - **Configuration :** le personnage est un guerrier (`ChooseClassAsync("class.warrior")`), et la config est `new ItemConfig { BagSize = 2, MaxParcels = 1 }` pour remplir vite.
  - **Désactivation :** elle se fait par `await grain.OnDeactivateAsync(new DeactivationReason(DeactivationReasonCode.ShuttingDown, "test"), CancellationToken.None)`. On réactive ensuite un nouveau grain sur la même base.

  Les tests :
  - `WithoutACharacter_EveryOperationIsNoCharacter`
  - `AGrant_IsWrittenAtOnceWithItsKey` : juste après l'appel, la base a une ligne `rpg_items` (quantité, durabilité 50, `container` 0, case 0) et une ligne `rpg_item_grants`.
  - `AReplayedKey_GrantsNothing` : la seconde réponse est `DuplicateGrant`, il reste une seule ligne.
  - `AFullBag_SendsToAParcel_ThenTheParcelIsClaimed` :
    - deux épées remplissent le sac, et un éclat part en colis ;
    - on détruit une épée, puis `ClaimParcelAsync(0)` répond `Ok` ;
    - l'éclat est en case 0.
  - `FullParcels_GrantNothingAndConsumeNoKey` : la réponse est `ParcelsFull`, et ni la clé ni l'objet n'existent en base.
  - `AGrantWhoseWriteFails_LeavesTheBagAndTheKeyFree` :
    - options avec `.AddInterceptors(faults)`, et `faults.FailNext = 1` (`CommitFaults`) ;
    - le grant lève une exception, `GetItemsAsync()` est vide et aucune clé n'existe ;
    - le même grant rejoué répond `Ok`.
  - `Equipping_RaisesTheStats_AndAllSurvivesReactivation` :
    - attribution de l'épée, `EquipItemAsync(0)`, puis `GetStatsAsync().Strength` vaut 15 (12 + 3) ;
    - les sources sont `class` et `equipment` ;
    - après désactivation et réactivation, `GetItemsAsync()` est égal et `Strength` vaut toujours 15.
  - `AMove_IsWrittenByTheFlush_NotAtOnce` :
    - deux éclats, puis `MoveItemAsync(0, 1)` ;
    - la ligne en base est encore à la case d'origine ;
    - après désactivation, elle est à la nouvelle case.
  - `ASplitNotYetFlushed_IsWrittenAtDeactivation` : 20 éclats, `SplitItemAsync(0, 5, 1)`, puis désactivation. Il y a deux lignes en base (15 et 5), relues à l'identique.
  - `AReplacedDefinition_IsReReadAndNeverWrittenBack` :
    - éclat attribué ;
    - la base passe `definition_id` à `item.material.new_shard`, et la version du contenu passe de 1 à 2 ;
    - un `MoveItemAsync`, puis désactivation : la base garde `new_shard`, et `GetItemsAsync()` le montrait déjà.
  - `ACopyOfAnUnknownDefinition_StaysWithoutStats` : une ligne d'arme inconnue en équipement est chargée ; `GetItemsAsync` la liste, aucune stat d'équipement, un avertissement journalisé.
  - `ADestroy_IsWrittenAtOnce`

- [ ] **Step 3: Les lancer — échec attendu.**

- [ ] **Step 4: `ItemConfig`.**

```csharp
namespace Vortex.Rpg.Characters.Configuration;

/// <summary>The bag, the parcels and how often moved items are written (spec Objets §2, §6).</summary>
public sealed class ItemConfig
{
    public const string SECTION_NAME = "Vortex:Items";

    /// <summary>How often moved, split and equipped items are written; a grant, a destroy and a
    /// removal are written at once.</summary>
    public int FlushIntervalMs { get; init; } = 2_000;

    public int BagSize { get; init; } = 30;

    /// <summary>Parcels kept for a character whose bag was full; past this a grant is refused.</summary>
    public int MaxParcels { get; init; } = 50;
}
```

- [ ] **Step 5: `CharacterItemStore`.** Le seul code qui lit et écrit `rpg_items` et `rpg_item_grants`. `WriteAsync` fait un seul `SaveChangesAsync`, donc une seule transaction :
  - une copie neuve (`Id == 0`) est ajoutée, sauf si elle est déjà supprimée ;
  - une copie existante est attachée, et seules ses colonnes de placement sont marquées modifiées ;
  - la clé est ajoutée, si elle est donnée ;
  - après la sauvegarde, les ids générés sont reportés sur les copies.

```csharp
public async Task WriteAsync(PlayerId player, IReadOnlyList<ItemInstance> dirty, string? grantKey, CancellationToken ct)
{
    await using VortexDbContext db = await dbFactory.CreateDbContextAsync(ct);
    List<(ItemInstance Item, RpgItemEntity Row)> added = [];

    foreach (ItemInstance item in dirty)
    {
        if (item.Id == 0)
        {
            if (!item.Deleted)
            {
                RpgItemEntity row = ToRow(player, item);
                db.RpgItems.Add(row);
                added.Add((item, row));
            }

            continue;
        }

        RpgItemEntity existing = ToRow(player, item);
        existing.Id = item.Id;
        existing.DeletedAt = item.Deleted ? DateTime.UtcNow : null;
        EntityEntry<RpgItemEntity> entry = db.RpgItems.Attach(existing);
        // definition_id is never written from memory: an import's replace rule owns it (AGENTS.md).
        entry.Property(r => r.Quantity).IsModified = true;
        entry.Property(r => r.Durability).IsModified = true;
        entry.Property(r => r.Bound).IsModified = true;
        entry.Property(r => r.Container).IsModified = true;
        entry.Property(r => r.Slot).IsModified = true;
        entry.Property(r => r.DeletedAt).IsModified = true;
    }

    if (grantKey is not null)
    {
        db.RpgItemGrants.Add(new RpgItemGrantEntity { GrantKey = grantKey, PlayerEntityId = player.Value });
    }

    await db.SaveChangesAsync(ct);

    foreach ((ItemInstance item, RpgItemEntity row) in added)
    {
        item.Id = row.Id;
    }
}
```

`ToRow(player, item)` construit l'entité à partir des champs de la copie :
- `PlayerEntityId = player.Value` ;
- `DefinitionId`, `Quantity`, `Durability`, `Bound`, `Container` et `Slot` repris de la copie.

`LoadAsync` lit les lignes non supprimées du joueur, et `ReadDefinitionIdsAsync` lit les paires `(id, definition_id)` de ces mêmes lignes.

- [ ] **Step 6: `PlayerCharacterGrain.Items.cs`.**
  - **Champs :** `CharacterInventory? _inventory`, `long _itemIdsVersion`, `IDisposable? _itemFlushTimer`, `CharacterItemStore _itemStore` (créé dans le constructeur principal à partir de `dbFactory`) et `ItemConfig _itemConfig`.
  - **`InventoryAsync()`** :
    - `null` sans personnage ;
    - sinon, charge l'inventaire au premier besoin (`CharacterInventory.Load(new InventoryLimits(BagSize, MaxParcels), id => content.Get<ItemDefinition>(id), await _itemStore.LoadAsync(...))`) ;
    - si `content.Version` a changé depuis `_itemIdsVersion`, relit les ids de définition (`ReadDefinitionIdsAsync`) dans les copies qui ont un id, et remet `_stats = null`.
  - **Copie de définition inconnue :** au chargement, chacune est journalisée une fois (`LogWarning`, joueur, id d'exemplaire, définition).
  - **`GrantItemsAsync`** :
    - une clé vide ou plus longue que `RpgItemGrantEntity.GrantKeyLength` lève une `ArgumentException` (erreur de programmation : la route la valide) ;
    - une clé déjà vue répond `DuplicateGrant` ;
    - sinon : `next = inventory.Clone()`, puis `plan = next.Grant(items)` ; un refus est journalisé et rendu ;
    - en cas de succès : `await _itemStore.WriteAsync(_playerId, next.Dirty, grantKey, CancellationToken.None)`, puis `next.MarkWritten()`, puis `_inventory = next` ;
    - enfin, `Publish(new ItemGrantedEvent(...))` pour chaque `plan.Placed`, et le résultat est rendu.
    - Si l'écriture lève, `_inventory` n'a pas changé et l'exception remonte.
  - **`Move` / `Split` / `ClaimParcel`** : l'opération pure, puis rien de plus. Le timer écrit.
  - **`Equip` / `Unequip`** : avec `Ok`, mettre `_stats = null`, publier `CharacterStatsChangedEvent` et lancer `await PushStatsAsync()`.
    - Si l'objet vient d'être lié (lu avant et après), vider tout de suite et publier `ItemBoundEvent`.
    - `Equip` reçoit `_character.ClassId` et `_character.Level`.
  - **`DestroyItemAsync` / `RemoveItemsAsync`** :
    - d'abord `await FlushItemsAsync(CancellationToken.None)`, pour que la copie ait son id ;
    - puis l'opération ; avec `Ok`, vider de nouveau et publier `ItemDestroyedEvent` (ou un `ItemRemovedEvent` par `Taken`).
  - **Refus :** chacun est journalisé en `LogInformation("Item {Operation} refused for player {PlayerId}: {Outcome}.")`.
  - **`FlushItemsAsync(ct)`** suit `FlushAsync` de `PlayerCharacterGrain.cs` :
    - rien sans inventaire ni copie sale ;
    - sinon écriture, `MarkWritten()` et `metrics.ItemFlushCompleted(ms, false)` ;
    - une erreur est journalisée, comptée en échec, et laisse l'état sale.
  - **`GetItemsAsync()`** répond `inventory?.Snapshot() ?? []`.

- [ ] **Step 7: Le reste du grain.**
  - **`OnActivateAsync`** arme `_itemFlushTimer`, comme `_flushTimer`, avec `_itemConfig.FlushIntervalMs` et `FlushItemsAsync`.
  - **`OnDeactivateAsync`** dispose le timer et appelle `await FlushItemsAsync(ct)` après `FlushAsync(ct)`.
  - **`GetStatsAsync`** appelle `await InventoryAsync()` avant le calcul.
  - **`ComputeStatsAsync`** construit le contexte ainsi :

    ```csharp
    new StatSourceContext(_playerId, cls, character.Level)
    {
        Equipment = _inventory?.EquippedStats() ?? [],
    };
    ```

- [ ] **Step 8: Les métriques.** Dans `IVortexMetrics.ItemFlushCompleted`, le résumé suit celui de `CharacterFlushCompleted`. Dans `VortexMetrics`, l'histogramme est `Vortex.item.flush.duration`, en ms, avec la description « Writes of a character's moved items, by outcome (ok, failed: they stay dirty for the next flush). ».

- [ ] **Step 9: Les events** (contrats seulement, les handlers sont à la Tâche 5), dans `Vortex.Primitives/Events/Rpg/` :
  - `ItemGrantedEvent(PlayerId PlayerId, string GrantKey, int ItemId, string DefinitionId, int Quantity, bool Bound) : IEvent` ;
  - `ItemDestroyedEvent(PlayerId PlayerId, int ItemId, string DefinitionId, int Quantity)` ;
  - `ItemRemovedEvent(PlayerId PlayerId, int ItemId, string DefinitionId, int Quantity)` ;
  - `ItemBoundEvent(PlayerId PlayerId, int ItemId, string DefinitionId)`.

- [ ] **Step 10: Mettre à jour les cinq tests qui construisent le grain** (dernier argument `Options.Create(new ItemConfig())`), puis lancer `dotnet test Vortex.Database.Tests` : tout vert, `CharacterItemsTests` compris. Lancer aussi `dotnet test Vortex.WebApi.Tests` et `Vortex.Players.Tests` : verts.

- [ ] **Step 11: Commit** (`objets : sac, équipement et colis dans le grain du personnage`).

- [ ] **Step 12: Relecture des tâches 3–4** par l'agent `grain-rules-reviewer` (le diff touche `Grains/`). Les points à vérifier :
  - aucun `await` en boucle sur des grains ;
  - aucune écriture par événement, hormis l'attribution, la destruction et le retrait, qui sont voulus et justifiés ;
  - pas de constante de capacité codée en dur ;
  - un refus journalisé.

  Puis corriger et noter dans le registre.

---

### Task 5: L'audit des objets

**Files:**
- Modify: `Vortex.Primitives/Observability/ItemEventType.cs` : ajouter **à la fin** `RpgGranted`, `RpgDestroyed`, `RpgRemoved`, `RpgBound`, avec un commentaire disant que `item_id` y est un id de `rpg_items`.
- Create: `Vortex.Observability/Events/RpgItemGrantedForensicsHandler.cs`, `RpgItemDestroyedForensicsHandler.cs`, `RpgItemRemovedForensicsHandler.cs`, `RpgItemBoundForensicsHandler.cs`
- Test: `Vortex.Database.Tests/Observability/RpgItemForensicsTests.cs`

**Interfaces:**
- Consumes: les quatre events (Tâche 4) et `IItemForensics.Record(in ItemForensicEvent)`.
- Produces: une ligne `item_events` par event, avec :
  - `ItemId` = l'id de l'exemplaire ;
  - `ToOwnerId` = le joueur (`FromOwnerId` pour la destruction et le retrait) ;
  - `ActorPlayerId` = le joueur pour la destruction et la liaison, `null` pour l'attribution et le retrait (gestes du serveur) ;
  - `Data` = le JSON `{"definition":…,"quantity":…}`, plus `"grantKey"` et `"bound"` pour l'attribution.

- [ ] **Step 1: Test qui échoue.** Chaque handler, appelé directement avec un faux `IItemForensics` qui enregistre ce qu'il reçoit (`FakeProxy.Create<IItemForensics>`), produit l'`ItemForensicEvent` attendu. Il y a un test par handler, et `ItemGranted` vérifie le `Data` JSON (`System.Text.Json.JsonSerializer.Serialize` d'un objet anonyme).

- [ ] **Step 2: Implémenter les quatre handlers** sur le modèle de `ItemPlacedForensicsHandler`. Ils sont découverts par balayage : rien à enregistrer.

- [ ] **Step 3: Tests verts.** Lancer `dotnet test Vortex.Database.Tests --filter "FullyQualifiedName~RpgItemForensics"`. Lancer aussi `node .claude/skills/removing-dead-code/scripts/deadhandlers.mjs Vortex.Observability/Events` : aucun handler orphelin, puisque les quatre events sont publiés par le grain.

- [ ] **Step 4: Commit** (`objets : l'audit des attributions, destructions, retraits et liaisons`).

---

### Task 6: La route d'administration, le driver, la preuve

**Files:**
- Create: `Vortex.WebApi/Http/Requests/GrantItemsRequest.cs`, `Vortex.WebApi/Http/Responses/GrantItemsResponse.cs`
- Modify: `Vortex.WebApi/Hosting/WebApiEndpoints.Characters.cs`
- Modify: `Vortex.WebApi/openapi.json` (`UPDATE_OPENAPI=1`), `scripts/hooks/authorization-surface-baseline.json` (`--update`)
- Modify: `Vortex.WebApi.Tests/FakeCharacters.cs`, `Vortex.WebApi.Tests/CharacterEndpointsTests.cs`
- Modify: `.claude/skills/run-vortex-emulator/driver.mjs` (commande `items`), `.claude/skills/run-vortex-emulator/SKILL.md`
- Modify: `docs/architecture.md` (« Flow 8 — items »)
- Modify: `docs/superpowers/specs/2026-10-08-rpg-items-design.md` : statut et écarts n° 1 à 8 de ce plan, en une ligne chacun sous §2.

**Interfaces:**
- Consumes: `IPlayerCharacterGrain.GrantItemsAsync` (Tâche 4), `Capabilities.Economy.GrantItem`.
- Produces: `POST /api/admin/characters/{playerId:int}/items`, avec :
  - en entrée, le corps `{ "grantKey": string, "definitionId": string, "quantity": int }` ;
  - en réponse, `GrantItemsResponse(string Result, int ToBag, int ToParcels)`, où `Result` est le snake_case de l'issue.

  | Issue | Statut |
  |---|---|
  | `Ok`, `DuplicateGrant` | 200 |
  | `NoCharacter` | 404 `character_not_found` |
  | `UnknownItem`, `InvalidQuantity`, corps invalide | 400 |
  | `ParcelsFull` | 409 |

- [ ] **Step 1: Tests qui échouent** (`CharacterEndpointsTests`).
  - Le faux `FakeCharacters` gagne :
    - `List<(long Player, string Key, string Definition, int Quantity)> ItemGrants` ;
    - un `HashSet<string>` des clés vues ;
    - la réponse `DuplicateGrant` sur une clé déjà vue, et `NoCharacter` sans entrée dans `ByPlayer`.
  - Les tests :
    - `GrantingItems_WithoutTheCapability_IsForbidden`
    - `GrantingItems_ReachesTheCharacterWithTheCallersKey`
    - `AReplayedGrant_AnswersDuplicateAndGrantsOnce`
    - `AGrantWithoutAKey_IsABadRequest` : une clé vide, ou de 129 caractères ;
    - `AGrantToAnAvatarWithoutACharacter_IsNotFound`.

- [ ] **Step 2: Implémenter la route.**
  - **Règle de validité :** `GrantItemsRequest.IsValid` exige une clé non vide d'au plus 128 caractères, un `definitionId` non vide et `quantity ≥ 1`.
  - **Mapping des issues :** un `switch` explicite, qui lève sur une issue non prévue, comme le mapping des inscriptions de `WebApiAuthService`.
  - **Capability :** `.RequireCapability(Capabilities.Economy.GrantItem)`.
  - **Contrat :** les `.Produces` comme pour la route d'XP.
- [ ] **Step 3: Contrats.**
  - `UPDATE_OPENAPI=1 dotnet test Vortex.WebApi.Tests --filter "FullyQualifiedName~OpenApiDocumentTests"` ;
  - `node scripts/hooks/check-authorization-surface.mjs --update` : la nouvelle route y entre avec son garde `RequireCapability` ;
  - `dotnet test Vortex.WebApi.Tests` est vert.

- [ ] **Step 4: Le driver.** Ajouter la commande `items` :
  - **Préparation :** session web et rôle owner (le même bloc SQL que `lobby`). L'id du premier personnage du compte vient de `SELECT p.id FROM players p JOIN player_characters c ON c.player_id = p.id JOIN player_accounts a ON a.id = p.account_id WHERE a.email = '…' AND p.deleted_at IS NULL ORDER BY p.id LIMIT 1` (s'il n'y en a pas : « run `play` first »).
  - **Attribution :** `POST …/items` avec `{ grantKey: "driver-" + Date.now(), definitionId: "item.weapon.rusted_sword", quantity: 1 }`, deux fois avec la même clé. Attendu : `granted`, puis `duplicate_grant`.
  - **Contrôle :** compter les lignes `rpg_items` de cette épée pour ce joueur avant et après ; exactement +1, sinon `die`.
  - **Usage :** une ligne d'usage, et une ligne dans le tableau de `SKILL.md`.

- [ ] **Step 5: Documentation.**
  - **`docs/architecture.md`, « Flow 8 — items » :** cinq à huit lignes sur le propriétaire (le grain), la classe pure, l'écriture (attribution immédiate avec sa clé, le reste par lot), la source de stats, l'audit et la route.
  - **Spec Objets :** statut « livrée par le plan 2026-10-09-rpg-objets », et les écarts.

- [ ] **Step 6: Porte et preuve.**
  - Lancer `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate`, ou, si la mémoire manque, le script de tous les tests plus les checks, en le disant.
  - Publier le plugin web : `dotnet publish Vortex.WebApi -c Debug -o "$LOCALAPPDATA/Ambreia/webapi-plugin"`.
  - Driver sur `ambreia-database` : `smoke`, `content` (les objets s'importent), `items`, puis `lobby` en non-régression. Rapporter les sorties.

- [ ] **Step 7: Commit** (`objets : route d'attribution, preuve du driver, documentation`).

- [ ] **Step 8: Relecture des tâches 5–6** par un agent relecteur général, puis corrections et registre. Mettre aussi à jour la mémoire `rpg-progress.md` : Objets livré, la suite est F3b.
