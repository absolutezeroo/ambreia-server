using System.Text;

namespace Vortex.Primitives.Packets;

public interface IClientPacket : IVortexPacket
{
    public int Remaining { get; }
    public bool End { get; }
    public int Position { get; }

    /// <summary>The payload as hex with the read position marked, for when a parser and the client
    /// stop agreeing. Bounded: it ends up in a log.</summary>
    public string ToHexDump(int maxBytes = 128);

    public byte PopByte();
    public byte[] PopBytes(int count);
    public bool PopBoolean();
    public short PopShort();
    public ushort PopUShort();
    public int PopInt();

    /// <summary>
    /// A repeat count the client declared, clamped to what the bytes left could actually hold.
    /// </summary>
    /// <remarks>
    /// For any count that sizes an allocation. <c>new List&lt;int&gt;(PopInt())</c> hands the
    /// client the capacity: one packet declaring <c>int.MaxValue</c> asks for 8 GB before the first
    /// element is read, and a negative one throws. A count the payload cannot back is a lie either
    /// way; past the clamp, the element reads still fail loudly on a short packet.
    /// </remarks>
    /// <param name="minBytesPerElement">The fewest bytes one element can take on the wire: 4 for an
    /// int, 2 for a string (its length prefix).</param>
    public int PopCount(int minBytesPerElement);
    public float PopFloat();
    public long PopLong();
    public string PopString(Encoding? encoding = null);
}
