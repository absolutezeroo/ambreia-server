using System.Collections.Immutable;

namespace Vortex.Primitives.Orleans.Snapshots.Room;

/// <summary>Tag1/Tag2 (RoomEntity) -&gt; Tags mapping for the live room snapshot: blank or whitespace
/// tags are dropped, the rest trimmed.</summary>
public static class RoomTagMapper
{
    public static ImmutableArray<string> ToTags(string? tag1, string? tag2)
    {
        ImmutableArray<string>.Builder builder = ImmutableArray.CreateBuilder<string>(2);

        if (!string.IsNullOrWhiteSpace(tag1))
        {
            builder.Add(tag1.Trim());
        }

        if (!string.IsNullOrWhiteSpace(tag2))
        {
            builder.Add(tag2.Trim());
        }

        return builder.ToImmutable();
    }
}
