namespace Vortex.Players.Configuration;

public sealed class PlayerDirectoryConfig
{
    public const string SECTION_NAME = "Vortex:PlayerDirectory";

    /// <summary>Player names the directory keeps in memory; past it the cache starts over and the
    /// names are read again from the database.</summary>
    public int MaxCachedNames { get; init; } = 10_000;
}
