namespace Vortex.Primitives.Players;

/// <summary>
/// Which chat-bubble ids the server is allowed to speak about, and on which message.
/// </summary>
/// <remarks>
/// <para>
/// The client sorts its own style library by id before it asks anything, and the band decides which
/// answer it will look the style up in:
/// </para>
/// <list type="bullet">
/// <item>below <see cref="FirstUnlockableId"/> — the client gates the style itself, on club,
/// ambassador and staff rights and on <c>disabled.custom.chat.styles</c>. It never asks the server,
/// so a grant down here writes a row nothing reads.</item>
/// <item><see cref="FirstUnlockableId"/>..<see cref="FirstPurchasableId"/>-1 —
/// <c>hasNftChatStyle</c>, fed by <c>UserNftChatStyles</c>.</item>
/// <item><see cref="FirstPurchasableId"/> and up — <c>hasPurchasableChatStyle</c>, fed by
/// <c>UserPurchasableChatStyles</c> and kept current by <c>UserPurchasableChatStyleChanged</c>.</item>
/// </list>
/// <para>
/// Two lists, two lookups, and an id sent on the wrong one is simply never matched. That is why the
/// boundary is here rather than restated at each sender.
/// </para>
/// </remarks>
public static class ChatStyleBands
{
    /// <summary>The first id an unlock can reach; below this the client decides on its own.</summary>
    public const int FirstUnlockableId = 1000;

    /// <summary>The first id the client looks up in the purchasable list instead.</summary>
    public const int FirstPurchasableId = 10000;

    /// <summary>Whether the server can unlock this style at all.</summary>
    public static bool IsGrantable(int clientStyleId) => clientStyleId >= FirstUnlockableId;

    /// <summary>Whether this id belongs on <c>UserNftChatStyles</c>.</summary>
    public static bool IsUnlockable(int clientStyleId) =>
        clientStyleId >= FirstUnlockableId && clientStyleId < FirstPurchasableId;

    /// <summary>Whether this id belongs on the purchasable pair of messages.</summary>
    public static bool IsPurchasable(int clientStyleId) => clientStyleId >= FirstPurchasableId;
}
