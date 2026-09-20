namespace Vortex.Primitives.RewardTracks;

/// <summary>What a node of a step's condition tree does.</summary>
/// <remarks>
/// <see cref="Test"/> is zero so that a row written before the tree existed reads as one: every
/// filter stored until now is a test, and the flat list they formed is the children of an
/// <see cref="All"/>, which is precisely what "filters are ANDed" meant.
/// </remarks>
public enum PredicateKind
{
    /// <summary>One fact, one operator, one value. The only kind with no children.</summary>
    Test = 0,

    /// <summary>Every child holds. An empty one holds, which is what "no conditions" means.</summary>
    All = 1,

    /// <summary>At least one child holds. An empty one does not: nothing was satisfied.</summary>
    Any = 2,

    /// <summary>Its single child does not hold.</summary>
    Not = 3,
}
