namespace Vortex.Primitives.Catalog.Enums;

/// <summary>Which of the gift dialog's three choices a <c>catalog_gift_wrappings</c> row offers.</summary>
public enum GiftWrappingKind
{
    /// <summary>A parcel furniture (<c>present_gen*</c>), named by its class name.</summary>
    Parcel = 0,

    /// <summary>A box art index, drawn from the client's own sprites.</summary>
    Box = 1,

    /// <summary>A ribbon art index, likewise client art.</summary>
    Ribbon = 2,
}
