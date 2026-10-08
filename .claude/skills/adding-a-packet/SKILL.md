---
name: adding-a-packet
description: Adds or changes a client↔server packet in the Vortex emulator end to end — incoming message, parser, header id, map registration, handler, outgoing composer, serializer, tests and a live check. Use when the user asks for a new packet, a new client request or server response, a change to a message's fields, or when a handler "does nothing" at runtime.
---

# Adding a packet

A packet crosses five places, and forgetting one compiles fine and fails silently. Copy this
checklist into the conversation and tick it:

```
- [ ] 1. Message / composer record (Vortex.Protocol)
- [ ] 2. Header id (Headers.cs) with its evidence
- [ ] 3. Parser / serializer (Vortex.Revisions/Revision20260701)
- [ ] 4. Map registration (Maps/<Domain>Map.cs)
- [ ] 5. Handler (Vortex.PacketHandlers) — incoming only
- [ ] 6. Tests (wire test + handler test)
- [ ] 7. check-inert-declarations = 0 gaps, gate green, live check
```

## 1. The record

Incoming, `Vortex.Protocol/Messages/Incoming/<Domain>/<Name>Message.cs`:

```csharp
public record LookToMessage : IMessageEvent
{
    public required int X { get; init; }
    public required int Y { get; init; }
}
```

Outgoing, `Vortex.Protocol/Messages/Outgoing/<Domain>/<Name>MessageComposer.cs` (Orleans-serialized,
it crosses grains):

```csharp
[GenerateSerializer, Immutable]
public sealed record RoomForwardMessageComposer : IComposer
{
    [Id(0)]
    public required RoomId RoomId { get; init; }
}
```

## 2. The header id

`Vortex.Revisions/Revision20260701/Headers.cs`: `MessageEvent` (incoming) or `MessageComposer`
(outgoing). A Habbo packet takes the id the WIN63 client uses — write where you read it
(`// AS3-verified: …`). An RPG-only packet takes an id from the reserved band at 9000+.

## 3. Parser / serializer

```csharp
internal class LookToMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet) =>
        new LookToMessage { X = packet.PopInt(), Y = packet.PopInt() };
}

internal class RoomForwardMessageComposerSerializer(int header)
    : AbstractSerializer<RoomForwardMessageComposer>(header)
{
    protected override void Serialize(IServerPacket packet, RoomForwardMessageComposer message) =>
        packet.WriteInteger(message.RoomId);
}
```

Read and write fields in the exact order the client does; one field short kills the whole packet on
the client (`End of buffer`). Never write a constant in place of a real value; freeze a field only
with a comment saying why.

## 4. Registration

In `Maps/<Domain>Map.cs`:

```csharp
builder.MapParser(MessageEvent.LookToMessageEvent, new LookToMessageParser());
builder.MapSerializer(
    typeof(RoomForwardMessageComposer),
    new RoomForwardMessageComposerSerializer(MessageComposer.RoomForwardMessageComposer)
);
```

A new map file is added to the list in `Revision20260701.cs`.

## 5. The handler

`Vortex.PacketHandlers/<Domain>/<Name>MessageHandler.cs`, discovered by scan (no registration):
see `Room/Engine/MoveAvatarMessageHandler.cs`. Player and room come from `ctx` (`ctx.PlayerId`,
`ctx.AsActionContext()`), never from the packet. One call to a grain or service; the grain sends the
response through `PlayerPresenceGrain.SendComposerAsync` or the room stream.

## 6. Tests

- Wire test: serialize through the registered serializer and pop the fields back in client order —
  copy `Vortex.Revisions.Tests/Room/UserChangeWireTests.cs`.
- Handler test: construct the handler with `FakeProxy` fakes and record what it calls —
  copy `Vortex.PacketHandlers.Tests/SsoTicketMessageHandlerTests.cs`.
- `SerializerPairingTests` and `EmittedComposerRegistrationTests` already fail on a composer the
  server builds without a serializer.

## 7. Verify

```bash
node scripts/hooks/check-inert-declarations.mjs        # 0 gaps
dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate
node .claude/skills/run-vortex-emulator/driver.mjs smoke
```

For a new incoming packet, extend `cmdPlay` in `driver.mjs` (it has `frame(header, ...ints|strings)`
and `g.wait([headers])`) to send it and wait for the expected response, and show that output.
