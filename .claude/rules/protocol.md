---
paths:
  - "Vortex.Protocol/**"
  - "Vortex.Revisions/**"
  - "Vortex.PacketHandlers/**"
---

# Protocol and packet handlers

The full procedure for a new packet is the `adding-a-packet` skill. The constraints:

- An incoming message is a record with `required` fields — no default-fallback contract. A parser
  reads exactly what the client writes; an optional trailing field is read only when
  `packet.Remaining` allows it (`MoveAvatarMessageParser`).
- A serializer writes the real payload. Never replace a field with a constant to make a composer
  compile. A field the server no longer has data for is **frozen** with a comment saying why
  (`UserObjectMessageSerializer`, safety lock).
- Wire strings are an int16 length + UTF-8. Header ids live in `Headers.cs`; note the evidence for a
  new id (`// AS3-verified: …`) — a guessed id is a packet nobody receives.
- A handler gets the player and the room from `MessageContext` (the authenticated session), never
  from the packet body. It calls one grain or service and returns; no `DbContext`, no socket.
- RPG packets take ids from 9000 up (spec §3.7), in the `Vortex-specific` regions of `Headers.cs`.
  `MessageEvent` (incoming) and `MessageComposer` (outgoing) are separate id spaces: take the next
  free id in each. The evidence comment is `// RPG (spec §3.7)`, and `vortex-rpg-client` must use the
  same id. Never repurpose a Habbo message's meaning; zone-wide combat events go out as one message
  per tick, like `UserUpdate`.
- After any change here: `node scripts/hooks/check-inert-declarations.mjs` (0 gaps), then
  `driver.mjs ping` / `play` against a running emulator.
