namespace Vortex.PacketHandlers.Configuration;

/// <summary>
/// The least time between two character-screen packets of one intent, per socket (spec F3a §5.5).
/// Each intent counts apart: a create does not use up the list's allowance.
/// </summary>
public sealed class RpgIntentLimitConfig
{
    public const string SECTION_NAME = "Vortex:RpgIntents";

    public int ListIntervalMs { get; init; } = 1000;

    public int CreateIntervalMs { get; init; } = 2000;

    public int ChooseClassIntervalMs { get; init; } = 1000;

    public int EnterWorldIntervalMs { get; init; } = 1000;
}
