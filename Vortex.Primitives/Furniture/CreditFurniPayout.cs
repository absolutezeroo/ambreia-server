using Orleans;
using Vortex.Primitives.Players.Enums.Wallet;

namespace Vortex.Primitives.Furniture;

/// <summary>
/// What cashing a coin in is worth, and in which purse.
/// </summary>
/// <remarks>
/// The currency travels with the amount because the room is the only thing that can tell them
/// apart: both kinds are an ordinary piece of furniture, and which one it is lives in the classname
/// the room already holds. A caller handed a bare number would have to look the definition up again
/// to know what to pay — and a caller that forgot would pay credits for an emerald, which is what
/// this type exists to make impossible.
/// </remarks>
[GenerateSerializer, Immutable]
public readonly record struct CreditFurniPayout
{
    /// <summary>Nothing was redeemed: absent, not the actor's own, or a name carrying no value.</summary>
    public static CreditFurniPayout None => new() { Amount = 0, Currency = CurrencyType.Credits };

    [Id(0)]
    public required int Amount { get; init; }

    [Id(1)]
    public required CurrencyType Currency { get; init; }
}
