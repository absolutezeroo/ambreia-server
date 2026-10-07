using System;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Primitives.Rooms.Object;
using Vortex.Protocol.Messages.Incoming.Fishing;

namespace Vortex.Revisions.Revision20260701.Parsers.Fishing;

// The fishing system's five client->server reads. One file because each is a single field or fewer,
// and a file per line would bury the one that is not: the Hook Havoc timeline below.
//
// Read order is the contract with vortex-modern-client's composers — see that repository's
// docs/vortex-original/fishing.md. Append-only on both sides.

/// <summary>The player clicked a fish shadow. Names the spot and nothing else.</summary>
internal class VortexStartFishingMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet) =>
        new VortexStartFishingMessage { SpotObjectId = new RoomObjectId(packet.PopInt()) };
}

/// <summary>The player walked away. Carries nothing — the server knows whose session it is.</summary>
internal class VortexStopFishingMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet) => new VortexStopFishingMessage();
}

/// <summary>Mount a recorded catch as a trophy. Names a record id the server itself issued.</summary>
internal class VortexFishingMountCatchMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet) =>
        new VortexFishingMountCatchMessage { RecordId = packet.PopInt() };
}

/// <summary>
/// The player's whole Hook Havoc attempt: a count, then that many ints, alternating tick and
/// direction.
/// </summary>
/// <remarks>
/// The count is clamped before anything is allocated. It arrives from the client, so an honest one
/// is bounded by the attempt's duration and a dishonest one is bounded by nothing — reading it as
/// given would let a single packet ask for a two-gigabyte array. Anything past the ceiling is left
/// unread, which desynchronises that packet and only that packet; the alternative, trusting it,
/// costs the whole silo.
/// </remarks>
internal class VortexHookHavocInputMessageParser : IParser
{
    /// <summary>
    /// 4,096 pairs. Hook Havoc runs for seconds and the guides say to tap rather than hold, so a
    /// real attempt is tens of entries; this is generous by two orders of magnitude and still small.
    /// </summary>
    private const int MaxTimelineLength = 8192;

    public IMessageEvent Parse(IClientPacket packet)
    {
        int count = Math.Clamp(packet.PopInt(), 0, MaxTimelineLength);
        int[] timeline = new int[count];

        for (int i = 0; i < count; i++)
        {
            timeline[i] = packet.PopInt();
        }

        return new VortexHookHavocInputMessage { Timeline = timeline };
    }
}
