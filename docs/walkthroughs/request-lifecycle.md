# Request Lifecycle: From Socket to Client

> **Coupe au noyau (2026-10-07).** Les exemples de ce guide citent des messages, systèmes ou
> logiques retirés depuis (chat, expressions, dés…). La mécanique décrite reste juste ; les
> exemples seront réécrits sur le noyau lors de la passe de guidage de l'IA.

This document traces **one real incoming packet** â€” a chat message â€” through every
layer of Vortex Cloud, using the actual code that ships in the repository today. If
you understand this one flow, you understand how the whole emulator is wired:
networking â†’ decode â†’ handler â†’ grain â†’ system â†’ Orleans stream â†’ presence â†’ session.

Read `docs/orleans.md` first for the mental model of grains and streams. This
document is the concrete counterpart to that one.

---

## The 10,000-foot view

```
client socket
   â”‚  raw bytes
   â–¼
ClientPacketDecoder            (Vortex.Networking/Package)
   â”‚  ChatMessage (typed incoming message)
   â–¼
ChatMessageHandler             (Vortex.PacketHandlers/Room/Chat)
   â”‚  delegates, never touches DB or sockets
   â–¼
RoomGrain.SendChatFromPlayerAsync   (Vortex.Rooms/Grains/RoomGrain.Avatar.cs)
   â”‚  thin partial entry point
   â–¼
RoomChatSystem.SendChatFromPlayerAsync   (Vortex.Rooms/Grains/Systems)
   â”‚  resolves player â†’ avatar, builds outgoing composer
   â–¼
RoomGrain.SendComposerToRoomAsync   (Vortex.Rooms/Grains/RoomGrain.cs)
   â”‚  publishes to an Orleans stream (RoomOutbound) â€” NOT a direct socket write
   â–¼
PlayerPresenceGrain  (subscribed to that room's stream)
   â”‚  one presence grain per player in the room receives RoomOutbound
   â–¼
SessionObserver.SendComposerAsync   â†’   client socket
```

The single most important architectural fact: **a room never writes to a socket.**
It publishes a composer to a stream. Each player's presence grain is subscribed to
the room's stream and is responsible for delivering to that player's own session(s).
This is what lets a room scale and relocate across silos without knowing anything
about connections.

---

## Step 1 â€” Decode: bytes become a typed message

The client sends a framed packet. `ClientPacketDecoder` and the revision layer turn
the wire bytes into a strongly-typed incoming message. For chat, that type is:

```csharp
// Vortex.Primitives/Messages/Incoming/Room/Chat/ChatMessage.cs
public sealed class ChatMessage
{
    public string Text { get; init; }
    public int StyleId { get; init; }
    public int TrackingId { get; init; }
}
```

You do not write decoding by hand per packet in `vortex-cloud`. Parser/serializer
trees for a given client revision live in the **plugin repo**
(`../turbo-sample-plugin/TurboSamplePlugin/Revision/**`) â€” see `CONTEXT.md`. The core
only ever sees the typed message.

## Step 2 â€” Dispatch: the handler

The pipeline routes the decoded message to the one handler registered for its type.
Here is the **real, complete** handler â€” note how little it does:

```csharp
// Vortex.PacketHandlers/Room/Chat/ChatMessageHandler.cs
public class ChatMessageHandler(IGrainFactory grainFactory) : IMessageHandler<ChatMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        ChatMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx is null || ctx.PlayerId <= 0 || ctx.RoomId <= 0)
            return;

        var roomChatGrain = _grainFactory.GetRoomGrain(ctx.RoomId);

        await roomChatGrain
            .SendChatFromPlayerAsync(
                ctx.PlayerId,
                message.Text,
                0,
                message.StyleId,
                [],
                message.TrackingId
            )
            .ConfigureAwait(false);
    }
}
```

What to take from this:

- **Guard first.** Every handler validates `ctx` before doing anything. `PlayerId`
  and `RoomId` come from the authenticated session context, not from the packet, so
  the client cannot spoof them.
- **Resolve a grain, delegate, return.** The handler holds no state, runs no query,
  and writes to no socket. It is pure orchestration. This is the hard boundary from
  `CONTEXT.md`: *packet handlers are orchestration-only.*
- **`MessageContext`** carries the ambient request data: `PlayerId`, `RoomId`, and a
  helper `AsActionContext()` used by handlers that drive room actions (movement,
  furniture) rather than chat.

## Step 3 â€” The grain entry point (thin partial)

`RoomGrain` is a `sealed partial class` split across many files by concern
(`RoomGrain.Avatar.cs`, `RoomGrain.Furni.cs`, `RoomGrain.Map.cs`, â€¦). The chat entry
point is a one-line forward into a *system*:

```csharp
// Vortex.Rooms/Grains/RoomGrain.Avatar.cs
public Task SendChatFromPlayerAsync(
    PlayerId playerId,
    string text,
    AvatarGestureType gesture,
    int styleId,
    List<(string, string, bool)> links,
    int trackingId
) => ChatSystem.SendChatFromPlayerAsync(playerId, text, gesture, styleId, links, trackingId);
```

The grain itself stays small. Real behavior lives in **modules** and **systems** that
each take the grain in their constructor and are instantiated once when the grain
activates:

```csharp
// Vortex.Rooms/Grains/RoomGrain.cs  (constructor, abridged)
PathingSystem = new(this);
EventModule   = new(this);
SecurityModule = new(this);
MapModule     = new(this);
ObjectModule  = new(this);
AvatarModule  = new(this);
// â€¦ ChatSystem, WiredSystem, RollerSystem, AvatarTickSystem, etc.
```

> **Module vs System (convention observed in the code):** *Modules* own a slice of
> grain state and the operations on it (avatars, furniture, map, security). *Systems*
> are behavioral engines that run logic over that state (pathing, chat, roller, wired,
> per-tick avatar updates). Both are plain classes holding a back-reference to the
> grain â€” they are **not** separate grains, so calling them is an in-process method
> call with no Orleans round-trip.

## Step 4 â€” The system does the work

```csharp
// Vortex.Rooms/Grains/Systems/RoomChatSystem.cs
public async Task SendChatFromPlayerAsync(
    PlayerId playerId,
    string text,
    AvatarGestureType gesture,
    int styleId,
    List<(string, string, bool)> links,
    int trackingId
)
{
    if (
        !_roomGrain._state.AvatarsByPlayerId.TryGetValue(playerId, out var objectId)
        || !_roomGrain._state.AvatarsByObjectId.TryGetValue(objectId, out var avatar)
    )
        return;

    await SendChatAsync(avatar.ObjectId, text, gesture, styleId, links, trackingId);
}

public async Task SendChatAsync(
    RoomObjectId objectId,
    string text,
    AvatarGestureType gesture,
    int styleId,
    List<(string, string, bool)> links,
    int trackingId
)
{
    await _roomGrain.SendComposerToRoomAsync(
        new ChatMessageComposer
        {
            ObjectId = objectId,
            Text = text,
            Gesture = gesture,
            StyleId = styleId,
            Links = links,
            TrackingId = trackingId,
        }
    );
}
```

Two things happen here:

1. **Player â†’ room object resolution.** The room tracks avatars by player id and by
   room-object id. Chat is addressed to the *room object* (the avatar in the room),
   not the player, because that is what other clients render. If the player has no
   avatar in this room, the message is silently dropped.
2. **An outgoing composer is built** (`ChatMessageComposer`). A *composer* is the
   outgoing counterpart to an incoming message â€” it knows how to serialize itself to
   the client. Composers live under
   `Vortex.Primitives/Messages/Outgoing/**`.

## Step 5 â€” Publish to the stream (the pivot)

```csharp
// Vortex.Rooms/Grains/RoomGrain.cs
public Task SendComposerToRoomAsync(IComposer composer) =>
    _roomOutbound.OnNextAsync(new RoomOutbound { RoomId = _state.RoomId, Composer = composer });
```

`_roomOutbound` is an `IAsyncStream<RoomOutbound>` â€” an Orleans stream, set up when
the grain activates. The room's job ends here. It has announced "this composer should
go to everyone in room X" and moved on. It does not know who is connected, how many
sessions they have, or which silo they live on.

## Step 6 â€” Presence grains receive and fan out

When a player enters a room, their `PlayerPresenceGrain` **subscribes** to that room's
outbound stream. This is the real subscription code:

```csharp
// Vortex.Players/Grains/PlayerPresenceGrain.Room.cs  (inside SetActiveRoomAsync)
var provider = this.GetStreamProvider(OrleansStreamProviders.ROOM_STREAM_PROVIDER);
var streamId = StreamId.Create(OrleansStreamNames.ROOM_STREAM, roomId.Value);
var stream   = provider.GetStream<RoomOutbound>(streamId);

_roomOutboundSub = await stream.SubscribeAsync(this);
```

Because the presence grain implements the stream observer, each `RoomOutbound` it
receives is handed to its own delivery method, which calls down to the session layer:

```csharp
// Vortex.Players/Grains/PlayerPresenceGrain.cs  (shape)
public Task SendComposerAsync(IComposer composer)
{
    // â€¦ resolves the player's live session(s) â€¦
    return _sessionObserver.SendComposerAsync(payload);
}
```

## Step 7 â€” The session writes to the socket

`SessionObserver` / `SessionGateway` (in `Vortex.Networking/Session`) hold the live
connection. `SendComposerAsync` serializes the composer for the player's client
revision and writes the framed bytes back to the socket. The chat bubble appears.

---

## Why this indirection is worth it

A naÃ¯ve emulator (e.g. classic Arcturus) holds a list of connected users on the room
object and loops over their sockets. That works on one process and falls apart the
moment you want horizontal scale, because the room must know about every connection.

Vortex's flow decouples the three concerns completely:

| Concern | Owner | Knows about |
|---|---|---|
| "What happened in the room" | `RoomGrain` + systems | room state only |
| "Who is present and where are their sessions" | `PlayerPresenceGrain` | one player's sessions |
| "Bytes on the wire" | `SessionObserver` | one socket |

A room can deactivate, migrate silos, or rehydrate from the database and none of the
presence or session machinery cares. That is the entire point of the grain + stream
design, and this chat path is the clearest example of it in the codebase.

---

## Quick reference â€” file map for this flow

| Step | File |
|---|---|
| Typed incoming message | `Vortex.Primitives/Messages/Incoming/Room/Chat/ChatMessage.cs` |
| Handler | `Vortex.PacketHandlers/Room/Chat/ChatMessageHandler.cs` |
| Grain entry | `Vortex.Rooms/Grains/RoomGrain.Avatar.cs` |
| Chat system | `Vortex.Rooms/Grains/Systems/RoomChatSystem.cs` |
| Stream publish | `Vortex.Rooms/Grains/RoomGrain.cs` (`SendComposerToRoomAsync`) |
| Outgoing composer | `Vortex.Primitives/Messages/Outgoing/Room/Chat/ChatMessageComposer.cs` |
| Stream subscribe + fan-out | `Vortex.Players/Grains/PlayerPresenceGrain.Room.cs`, `PlayerPresenceGrain.cs` |
| Socket write | `Vortex.Networking/Session/SessionObserver.cs` |

