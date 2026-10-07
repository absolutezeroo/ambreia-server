namespace Vortex.World;

public sealed class WorldConfig
{
    public const string SECTION_NAME = "Vortex:World";

    /// <summary>Development: every save is published (playable at once). Public server: false.</summary>
    public bool PublishOnSave { get; init; }

    public int LockMinutes { get; init; } = 30;

    /// <summary>Decor objects above which validation warns (client render cost).</summary>
    public int DecorBudget { get; init; } = 2000;
}
