using System;
using System.Globalization;

namespace Vortex.Primitives.Furniture;

/// <summary>
/// What an emerald furni is worth, read out of its classname — the collectibles counterpart of
/// <see cref="CreditFurniValue" />.
/// </summary>
/// <remarks>
/// <para>
/// The convention is not the same one and cannot be folded into it. A credit furni carries its
/// value after a <c>CF_</c> prefix; an emerald furni carries it in the digits that END the name, and
/// <c>FurnitureNftCreditLogic.initialize</c> in the client reads exactly that
/// (<c>type.match(/\d+$/)</c>), falling back to a table for the two names that carry no number.
/// </para>
/// <para>
/// So this is not decoration: the client shows the player "this item contains N emeralds" from its
/// own reading of the name, and a server that read it differently would promise one figure and pay
/// another.
/// </para>
/// </remarks>
public static class EmeraldFurniValue
{
    private const string Prefix = "nft_emerald";

    /// <summary>
    /// The two the client hard-codes, because their names end in no digits. Kept in the same order
    /// and with the same values as <c>FurnitureNftCreditLogic</c>; a name it does not list and that
    /// ends in no number is worth nothing to it either.
    /// </summary>
    private static readonly (string Name, int Value)[] NamedValues =
    [
        ("nft_emerald_emerhand", 15000),
        ("nft_emerald_eggmerald", 2000),
    ];

    /// <summary>
    /// Returns false for any name that is not an emerald furni or carries no positive value, so a
    /// mislabelled definition redeems for nothing rather than for zero.
    /// </summary>
    public static bool TryParse(string className, out int emeralds)
    {
        emeralds = 0;

        if (
            string.IsNullOrEmpty(className)
            || !className.StartsWith(Prefix, StringComparison.Ordinal)
        )
        {
            return false;
        }

        foreach ((string name, int value) in NamedValues)
        {
            if (string.Equals(className, name, StringComparison.Ordinal))
            {
                emeralds = value;

                return true;
            }
        }

        int digitStart = className.Length;

        while (digitStart > 0 && char.IsAsciiDigit(className[digitStart - 1]))
        {
            digitStart--;
        }

        return digitStart < className.Length
            && int.TryParse(
                className.AsSpan(digitStart),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out emeralds
            )
            && emeralds > 0;
    }
}
