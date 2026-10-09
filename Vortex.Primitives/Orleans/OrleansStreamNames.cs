namespace Vortex.Primitives.Orleans;

public static class OrleansStreamNames
{
    public const string ROOM_STREAM = "RoomStream";

    /// <summary>The World chat channel (spec F3c §4): every presence in the world listens, key 0.</summary>
    public const string WORLD_CHAT_STREAM = "world-chat";
}
