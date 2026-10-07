# Walkthrough: Add a Feature End-to-End

> **Coupe au noyau (2026-10-07).** Les exemples de ce guide citent des messages, systèmes ou
> logiques retirés depuis (chat, expressions, dés…). La mécanique décrite reste juste ; les
> exemples seront réécrits sur le noyau lors de la passe de guidage de l'IA.

This is the project's "hello world." It shows how to add a brand-new client-driven
feature across **every layer it must touch**, in the order you should touch them, using
the real shapes that exist in the repository. Once you can do this, every other packet
feature is a variation on it.

The example feature: **a "wave" action.** A player triggers it, and everyone in the
room sees that player's avatar perform a wave gesture. It is deliberately trivial so
that the *wiring* is the lesson, not the logic.

> Throughout, follow the hard boundaries in `CONTEXT.md` and the contract in
> `AGENTS.md`. The placement rules below are not invented here — they restate where
> `CONTEXT.md` says each kind of code belongs.

---

## Mental checklist

A client→room→broadcast feature touches these layers, in this order:

1. **Incoming message** — the typed packet the client sends (`Vortex.Primitives`)
2. **Handler** — orchestration only (`Vortex.PacketHandlers`)
3. **Grain entry point** — thin partial method (`Vortex.Rooms/Grains`)
4. **System/Module** — the actual behavior (`Vortex.Rooms/Grains/Systems` or `Modules`)
5. **Outgoing composer** — what the other clients receive (`Vortex.Primitives`)
6. **Test** — failure-path first (see `docs/patterns/vertical-slice.md`)
7. **Revision wiring** — parser/serializer, in the **plugin repo**, not here

If your feature only reads/returns data to the caller (e.g. "get my wallet"), you skip
the composer/broadcast step. If it mutates persisted state, the grain is also where the
DB write happens — never the handler.

---

## Step 1 — Define the incoming message

Create the typed message the client sends. Keep it a plain data carrier; no logic.

```csharp
// Vortex.Primitives/Messages/Incoming/Room/Engine/WaveMessage.cs
namespace Vortex.Protocol.Messages.Incoming.Room.Engine;

public sealed class WaveMessage
{
    // Wave carries no payload; the actor is taken from session context, not the packet.
}
```

> **Security note, repeated because it matters:** never read the acting player id from
> the packet. The handler reads it from `MessageContext`, which is bound to the
> authenticated session. A packet field claiming "I am player 5" is ignored by design.

## Step 2 — Write the handler (orchestration only)

```csharp
// Vortex.PacketHandlers/Room/Engine/WaveMessageHandler.cs
using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.Protocol.Messages.Incoming.Room.Engine;

namespace Vortex.PacketHandlers.Room.Engine;

public sealed class WaveMessageHandler(IGrainFactory grainFactory)
    : IMessageHandler<WaveMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        WaveMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx is null || ctx.PlayerId <= 0 || ctx.RoomId <= 0)
            return;

        var room = _grainFactory.GetRoomGrain(ctx.RoomId);

        await room.WaveFromPlayerAsync(ctx.PlayerId).ConfigureAwait(false);
    }
}
```

This mirrors `ChatMessageHandler` exactly: guard `ctx`, resolve the room grain,
delegate, return. No DB. No socket. No business logic. If you find yourself wanting to
add an `if` about *whether the wave is allowed*, that belongs in the grain's security
module (Step 4), not here.

## Step 3 — Add the grain entry point

`RoomGrain` is a `sealed partial class`. Add the entry point in the avatar partial,
next to `SendChatFromPlayerAsync`, and keep it a one-liner that forwards to a system:

```csharp
// Vortex.Rooms/Grains/RoomGrain.Avatar.cs  (add alongside the existing chat method)
public Task WaveFromPlayerAsync(PlayerId playerId) =>
    AvatarModule.WaveFromPlayerAsync(playerId);
```

You also need this method to exist on the grain interface so the handler can call it:

```csharp
// Vortex.Primitives/Rooms/Grains/IRoomGrain.cs  (add to the interface)
Task WaveFromPlayerAsync(PlayerId playerId);
```

> **Why an interface entry?** `IGrainFactory.GetRoomGrain(id)` returns the grain
> *interface*, not the class. Every method a handler calls must be declared on
> `IRoomGrain`. This is also your natural seam: the interface is the room's public
> API surface.

## Step 4 — Implement the behavior in the module

Modules own state slices and the operations on them. Waving is an avatar concern, so it
goes in `RoomAvatarModule`. This is where resolution and (if any) permission checks
live:

```csharp
// Vortex.Rooms/Grains/Modules/RoomAvatarModule.cs  (new method)
public async Task WaveFromPlayerAsync(PlayerId playerId)
{
    if (
        !_roomGrain._state.AvatarsByPlayerId.TryGetValue(playerId, out var objectId)
        || !_roomGrain._state.AvatarsByObjectId.TryGetValue(objectId, out var avatar)
    )
        return;

    // If this action needed rights, you would consult SecurityModule here, e.g.:
    //   var ctx = ...; if (!await _roomGrain.SecurityModule.CanUseFurniAsync(...)) return;
    // A wave is free for anyone present, so we proceed.

    await _roomGrain.SendComposerToRoomAsync(
        new WaveComposer { ObjectId = avatar.ObjectId }
    );
}
```

Note the pattern is identical to `RoomChatSystem`: resolve player → room object, then
publish a composer to the room. You are reusing the exact broadcast mechanism the chat
flow uses (`SendComposerToRoomAsync` → Orleans stream → presence grains → sessions),
which means you get fan-out and multi-silo correctness for free. You never enumerate
sessions yourself.

## Step 5 — Define the outgoing composer

```csharp
// Vortex.Primitives/Messages/Outgoing/Room/Engine/WaveComposer.cs
using Vortex.Primitives.Rooms.Object;

namespace Vortex.Protocol.Messages.Outgoing.Room.Engine;

public sealed class WaveComposer : IComposer
{
    public required RoomObjectId ObjectId { get; init; }

    // The serializer for the target client revision lives in the plugin repo
    // (see Step 7). This type only declares the data to send.
}
```

## Step 6 — Test the behavior, failure path first

Follow `docs/patterns/UnitTestPattern.cs` — assert the *guards* before the happy path.
The most valuable test here is "waving when the player has no avatar in the room is a
no-op," because that is the branch most likely to regress when someone refactors avatar
storage. See `docs/patterns/vertical-slice.md` for the full handler+module+test set on
this exact feature.

## Step 7 — Wire the revision (in the plugin repo, NOT here)

The decoder needs to know which incoming header maps to `WaveMessage`, and the
serializer needs to turn `WaveComposer` into bytes for your target client. Per
`CONTEXT.md`, those parser/serializer trees are owned by:

```
../turbo-sample-plugin/TurboSamplePlugin/Revision/**
```

Do **not** create `Revision<id>/Parsers` or `Revision<id>/Serializers` directories
inside `vortex-cloud`. Add the header→message and composer→bytes mappings in the plugin
repo for the revision you target.

---

## The placement rules, in one table

This is the whole walkthrough compressed. When adding any feature, ask "what kind of
code is this?" and put it where the answer says:

| Kind of code | Goes in | Example from this walkthrough |
|---|---|---|
| Typed incoming packet | `Vortex.Primitives/Messages/Incoming/<Domain>/` | `WaveMessage` |
| Request orchestration | `Vortex.PacketHandlers/<Domain>/<Name>MessageHandler.cs` | `WaveMessageHandler` |
| Room public API method | `Vortex.Primitives/Rooms/Grains/IRoomGrain.cs` | `WaveFromPlayerAsync` (interface) |
| Grain entry point | `Vortex.Rooms/Grains/RoomGrain.<Slice>.cs` | one-line forward |
| State + operation | `Vortex.Rooms/Grains/Modules/` | `RoomAvatarModule.WaveFromPlayerAsync` |
| Behavioral engine | `Vortex.Rooms/Grains/Systems/` | (chat uses `RoomChatSystem`) |
| Permission check | `RoomSecurityModule` | consulted from the module |
| Outgoing packet | `Vortex.Primitives/Messages/Outgoing/<Domain>/` | `WaveComposer` |
| Parser / serializer | **plugin repo** `Revision/**` | header + bytes mapping |
| Test | test project, failure-path first | no-avatar no-op |

## Anti-patterns this walkthrough exists to prevent

- ❌ Querying the database from `WaveMessageHandler`. → DB access belongs in the grain.
- ❌ Looping over sessions in the module to "send to everyone." → Publish one composer
  to the room stream; presence grains fan out.
- ❌ Trusting a player id from the packet body. → Read it from `MessageContext`.
- ❌ Putting permission logic in the handler. → It goes in `SecurityModule`.
- ❌ Adding a `Revision/Serializers` folder in `vortex-cloud`. → That lives in the
  plugin repo.

---

## What changes when the feature is *not* a broadcast

| Feature shape | What's different |
|---|---|
| **Read-only** ("get my wallet") | No composer, no stream. Grain method returns a snapshot to the handler, which sends a direct composer to the caller via their presence grain. |
| **Mutates persisted state** ("rename room") | The grain method writes through `IDbContextFactory` (as `RoomGrain` does in `HydrateRoomStateAsync`), updates in-memory `_state`, then broadcasts the change. The handler still does none of this. |
| **Targets another player** ("send friend request") | Resolve the *target's* presence grain via `IGrainFactory` and call `SendComposerAsync` on it, rather than broadcasting to a room. |
| **Player-scoped, not room-scoped** | Use `PlayerGrain` / `PlayerPresenceGrain` instead of `RoomGrain`; the handler resolves the player grain from `ctx.PlayerId`. |
