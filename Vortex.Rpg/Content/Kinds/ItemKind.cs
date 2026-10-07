using System.Collections.Generic;
using Vortex.Primitives.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Validation;

namespace Vortex.Rpg.Content.Kinds;

/// <summary>Items (spec Objets §4).</summary>
public sealed class ItemKind : ContentKind<ItemDefinition>
{
    private const int MaxStackLimit = 999;

    public override string Kind => "item";

    protected override IEnumerable<ContentFinding> Check(ItemDefinition i, ContentCheck check)
    {
        bool equipment = i.Category == ItemCategory.Equipment;

        if (i.RequiredLevel < 1)
        {
            yield return Error("BadLevel", i.Id, $"Required level {i.RequiredLevel}: at least 1.");
        }

        if (i.MaxDurability < 0 || i.SellValue < 0)
        {
            yield return Error(
                "NegativeValue",
                i.Id,
                "Durability and sell value are never negative."
            );
        }

        if (equipment)
        {
            if (i.Slot is null)
            {
                yield return Error(
                    "SlotRequired",
                    i.Id,
                    "An equipment says which slot it goes in."
                );
            }

            if (i.MaxStack != 1)
            {
                yield return Error(
                    "EquipmentStacks",
                    i.Id,
                    "Equipment does not stack: maxStack is 1."
                );
            }

            if (i.Rarity >= ItemRarity.Rare && i.Binding == ItemBinding.None)
            {
                yield return Error(
                    "RareMustBind",
                    i.Id,
                    "Rare equipment and above binds: on_equip or on_pickup."
                );
            }
        }
        else
        {
            if (i.Slot is not null)
            {
                yield return Error(
                    "SlotForbidden",
                    i.Id,
                    $"Only equipment has a slot, not {i.Category}."
                );
            }

            if (i.Stats is not null || i.MaxDurability > 0)
            {
                yield return Error(
                    "FieldNotForCategory",
                    i.Id,
                    "Stats and durability belong to equipment."
                );
            }

            if (i.MaxStack is < 1 or > MaxStackLimit)
            {
                yield return Error(
                    "BadStack",
                    i.Id,
                    $"maxStack {i.MaxStack}: between 1 and {MaxStackLimit}."
                );
            }
        }

        if (i.Category == ItemCategory.Quest && i.Binding != ItemBinding.OnPickup)
        {
            yield return Error("QuestMustBind", i.Id, "A quest item binds on pickup.");
        }

        if (i.Category == ItemCategory.Furniture)
        {
            if (i.FurnitureDefinition is not { } furniture)
            {
                yield return Error(
                    "FurnitureRequired",
                    i.Id,
                    "A furniture item names the furniture definition it places."
                );
            }
            else if (!check.FurnitureExists(furniture))
            {
                yield return Error(
                    "UnknownFurniture",
                    i.Id,
                    $"Furniture definition {furniture} is not in the catalogue."
                );
            }
        }
        else if (i.FurnitureDefinition is not null)
        {
            yield return Error(
                "FieldNotForCategory",
                i.Id,
                "Only a furniture item names a furniture definition."
            );
        }

        foreach (string id in i.AllowedClasses)
        {
            if (!check.Is<ClassDefinition>(id))
            {
                yield return Error("BrokenReference", i.Id, $"Class '{id}' does not exist.");
            }
        }
    }
}
