using System.Collections.Generic;
using Vortex.Primitives.Bots;
using Vortex.Primitives.Pets;

namespace Vortex.Inventory.Fulfillment;

/// <summary>
/// Everything a catalog offer promises, worked out before anything durable happens.
/// </summary>
/// <remarks>
/// <para>
/// It is data, not delegates. A plan that carried a <c>DbContext</c> or a callback would be a
/// workflow engine wearing a smaller name, and it would be exactly as hard to test as the method it
/// replaced. What is here is what the grant writes: rows, badge codes, pet and bot requests, effect
/// grants.
/// </para>
/// <para>
/// Producing it is deterministic and pure. That matters because it runs <em>before</em> the debit:
/// any error a plan can raise — an unknown definition, a malformed product — is one the purchase
/// never has to compensate for, because it happens while there is still nothing to compensate.
/// </para>
/// </remarks>
public sealed record FulfillmentPlan
{
    /// <summary>One entry per copy, carrying the extra data baked in at grant time.</summary>
    public required IReadOnlyList<PlannedFurniture> Furniture { get; init; }

    public required IReadOnlyList<string> BadgeCodes { get; init; }

    public required IReadOnlyList<PetCreateRequest> Pets { get; init; }

    public required IReadOnlyList<BotCreateRequest> Bots { get; init; }

    public required IReadOnlyList<PlannedEffect> Effects { get; init; }

    public static readonly FulfillmentPlan Empty = new()
    {
        Furniture = [],
        BadgeCodes = [],
        Pets = [],
        Bots = [],
        Effects = [],
    };

    /// <summary>Whether the plan promises nothing at all — a bundle of product types nothing here
    /// knows how to grant, which is worth telling apart from a bundle that succeeded.</summary>
    public bool IsEmpty =>
        Furniture.Count == 0
        && BadgeCodes.Count == 0
        && Pets.Count == 0
        && Bots.Count == 0
        && Effects.Count == 0;
}

/// <summary>One furniture row to write: which definition, and the stuff data baked into it.</summary>
/// <remarks>
/// The extra data is decided at plan time because the client renders a guild furni's badge and
/// recolours straight from it and never asks the server again — stamping it later would mean
/// stamping it never.
/// </remarks>
public readonly record struct PlannedFurniture(int DefinitionId, string? ExtraData);

/// <summary>One avatar effect to grant. Duration zero is permanent.</summary>
public readonly record struct PlannedEffect(int EffectId, int SubType, int DurationSeconds);
