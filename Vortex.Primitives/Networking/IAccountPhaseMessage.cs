namespace Vortex.Primitives.Networking;

/// <summary>
/// A character-screen message (spec F3a §5.5): accepted only while the socket is in the account
/// phase — signed in to an account, no character in the world yet.
/// </summary>
public interface IAccountPhaseMessage : IMessageEvent;
