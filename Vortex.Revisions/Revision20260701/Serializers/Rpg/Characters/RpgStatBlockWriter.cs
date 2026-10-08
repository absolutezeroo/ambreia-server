using System;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Rpg.Characters;

namespace Vortex.Revisions.Revision20260701.Serializers.Rpg.Characters;

/// <summary>
/// The stat block shared by 9003 and 9005 (spec F3a §6.2): ten ints, crit and dodge in basis points
/// (percent × 100) so the wire never carries a float.
/// </summary>
internal static class RpgStatBlockWriter
{
    public static void Write(IServerPacket packet, CharacterStatsSnapshot stats) =>
        packet
            .WriteInteger(stats.Strength)
            .WriteInteger(stats.Intelligence)
            .WriteInteger(stats.Agility)
            .WriteInteger(stats.MaxHealth)
            .WriteInteger(stats.MaxMana)
            .WriteInteger(stats.Armor)
            .WriteInteger(BasisPoints(stats.CritPercent))
            .WriteInteger(BasisPoints(stats.DodgePercent))
            .WriteInteger(stats.SpeedPercent)
            .WriteInteger(stats.Power);

    private static int BasisPoints(double percent) =>
        (int)Math.Round(percent * 100, MidpointRounding.AwayFromZero);
}
