# Protocol revisions

## Purpose

How a header id becomes a parser and a composer becomes bytes, why the answer is per-revision, and
what the embedded default actually contains.

> **Header ids are per-revision.** Nothing on this page is a statement about "the Habbo protocol".
> An id is a fact about one client build and one revision table.

## The abstraction

Four interfaces, all in `Vortex.Primitives/Networking/Revisions/`:

```csharp
interface IRevision {
    string Revision { get; }                                  // the client build string
    IReadOnlyDictionary<int,  IParser>     Parsers    { get; } // keyed by header id
    IReadOnlyDictionary<Type, ISerializer> Serializers { get; } // keyed by CLR composer type
}
```

**The asymmetry is deliberate.** Incoming is keyed by a numeric header because that is all the wire
gives you. Outgoing is keyed by the composer's CLR type, and the header id lives *inside* the
serializer (`ISerializer.Header`), passed in at registration. That is what keeps ids revision-scoped
instead of hardcoded in 554 serializer files.

Two consequences worth internalising:

1. **Outgoing lookup is by exact type — no inheritance.** `ShoutMessageComposer : ChatMessageComposer`
   needs its own registration, and `Maps/RoomMap.cs` registers all three of `ChatMessageComposer`,
   `ShoutMessageComposer` and `WhisperMessageComposer` separately. Inheritance dispatch exists on the
   *inbound* side only.
2. **Both dictionaries are `IReadOnlyDictionary`** — frozen after construction.

`IRevisionManager` holds the registry (`Revisions`, `DefaultRevisionId`, `GetRevision`,
`RegisterRevision`, `SetDefault`).

## Maps

`Vortex.Revisions/RevisionBase.cs` takes `IEnumerable<IRevisionMap>`, runs each through a
`RevisionMapBuilder`, then calls virtual `ConfigureParsers`/`ConfigureSerializers` hooks so a
*descendant* revision can override individual entries without copying the table.

`Vortex.Revisions/Revision20260701/Revision20260701.cs` is **59 lines**: a list of 43 `IRevisionMap`
instances and the build string `"WIN63-202607011411-782849652"`.

It used to be **3612 lines** of two giant dictionary initialisers separated by `#region` comments.
Commit `bd155d2f` split it, and its message records the mechanical check: the original held 500
parser and 367 serializer entries, and the 43 maps register the same counts. Three wins: one domain
file per edit instead of a merge-conflict magnet; loud duplicate detection; and a second revision
that can inherit rather than fork.

> Maps split by **client feature**, not strictly by protocol domain — the `Help` incoming messages
> and the `Callforhelp` outgoing composers live in different map files.

### Duplicates are a startup crash

`Vortex.Revisions/RevisionMapBuilder.cs` — `MapParser` and `MapSerializer` use `TryAdd` and throw
`InvalidOperationException` on a duplicate header id or composer type. A silent overwrite is
impossible.

## Headers.cs

`Vortex.Revisions/Revision20260701/Headers.cs` — 1288 lines, two `internal static class` blocks:

| Class | Direction | Constants |
|---|---|---|
| `MessageEvent` | client → server | 540 |
| `MessageComposer` | server → client | 534 |

Most constants carry a trailing comment citing the AS3 class that fixes the id.

> **Those comments are documentation, not authority.** "AS3-verified" comments have been wrong
> before and caused live wire bugs. Re-derive from the client sources at
> `vortex-modern-client/sources/WIN63-202607011411-782849652/src/**` before trusting one.

## Written vs mapped — the gap that matters

| | Classes | Mapped | Unmapped |
|---|---|---|---|
| Parsers | 540 | 534 | 6 |
| Serializers | 556 | 452 | **104** |

> Counting basis: **classes**, not files. Two files declare more than one
> (`Quest/DailyTasksSerializers.cs` holds three), so a file count reads 540 / 554 and the generated
> index — which counts files declaring the base type — differs by that margin. Say which you mean.

A serializer that no map registers is **inert**. `PackageEncoder.Encode` looks the composer type up,
misses, logs a warning, records `PacketDropped("serializer_not_found")` and writes **zero bytes**.
The client is told nothing; the feature simply does not respond.

The unmapped serializers are not strays — they are whole families:

- every `Game2*` (32 files, 0 mapped — verified)
- all `Talent*` composers (`TalentMap` registers only the *parsers*)
- the jukebox / playlist set (`PlayList*`, `Jukebox*`, `TraxSongInfo`, `NowPlaying`, `OfficialSongId`)
- `MessengerInit`, `InitDiffieHandshake`, `HotLooks`, `UserNameChanged`
- ~12 named `class_<n>MessageComposerSerializer` (unresolved AS3 class numbers)

Unmapped parsers: `ChargeFirework`, `GetTargetedOffer`, `GiveStarGemToUser`,
`SetRelationshipStatus`, `class_165`, `class_200`.

Whether each is dead weight or a half-finished feature cannot be answered from the wire layer alone.

## Which revision a session speaks

```
SessionContext.RevisionId = "Default"          ← field initialiser
        ↓ on connect
ctx.SetRevisionId(revisionManager.DefaultRevisionId)
        ↓ on ClientHello
ctx.SetRevisionId(message.Production)          ← the client's own build string wins
```

`Vortex.PacketHandlers/Handshake/ClientHelloMessageHandler.cs` closes the session if `Production` is
null, otherwise pins it. Its comment states the failure mode deliberately: **a build nobody
registered leaves the session on an id no revision answers to** — inbound throws in `PackageHandler`,
outbound is dropped by `PackageEncoder`, the socket stays open and stops speaking.

`DefaultRevisionId` comes from `Vortex.Revisions/RevisionRegistrationService.cs`, which registers
every DI-resolved `IRevision` and calls `SetDefault` **only if** `Vortex:Revisions:DefaultRevisionId`
is configured.

> That key is **not** in `appsettings.json` or `appsettings.Development.json` — neither file has a
> `Vortex:Revisions` section. So `RevisionManager.RegisterRevision`'s "first registration wins"
> fallback decides. With one registered revision that is fine; with a second it becomes
> registration-order-dependent.

Registration is by contract, so the host never names a concrete revision:

```csharp
// Vortex.Revisions/Extensions/ServiceCollectionExtensions.cs
services.AddSingleton<IRevision, RevisionType>();   // RevisionType = Revision20260701
```

## Writing a parser and a serializer

Three real shapes, all `internal`:

**Body-less parser** — an expression-bodied `Parse` returning the record.

**Flat parser** — one object initialiser, `Pop*` in wire order, with enum and value-object casts
applied at the boundary (`new RoomObjectId(packet.PopInt())`, `(Rotation)packet.PopInt()`). No
validation beyond typing.

**Bounded-collection parser** — takes a limit in its constructor and throws `InvalidDataException`
when the count exceeds it:

```csharp
// SaveRoomSettingsMessageParser
if (tagCount < 0 || tagCount > maxTags) throw new InvalidDataException(…);
```

Limits come from `Vortex.Revisions/Configuration/ProtocolLimitsConfig.cs` (`MaxFriendRemovalIds`
1000, `MaxRoomTags` 100, `MaxTradeItems` 1500, `MaxQuizAnswers` 100), injected into 5 domain maps.
Its XML doc draws the line: **wire-safety ceilings, not business policy.**

**Serializer** — `internal class X(int header) : AbstractSerializer<TComposer>(header)` with one
`Serialize` override, count-then-records, fluent `.Write*` chain. The header comes from the map, never
from the serializer.

**Registration** — in the domain's map file:

```csharp
builder.MapParser(MessageEvent.XMessageEvent, new XMessageParser());
builder.MapSerializer(typeof(XMessageComposer), new XMessageComposerSerializer(MessageComposer.XMessageComposer));
```

Checklist: [`docs/walkthroughs/`](../../walkthroughs/) and `AGENTS.md` "Packet addition checklist".

## Invariant tests

| Test | Proves |
|---|---|
| `Vortex.Revisions.Tests/EmittedComposerRegistrationTests.cs` | every composer constructed in an emitting project is registered — the guard against the silent `serializer_not_found` drop |
| `Vortex.Revisions.Tests/SerializerPairingTests.cs` | a serializer is registered against the composer it actually serializes, by walking `AbstractSerializer<T>` base chains. Its doc records two live `InvalidCastException`s it caught (`CampaignCalendarDoorOpened`, `UnseenItemsEvent`), both hidden because the serializer bodies were empty |

> **Latent hole in `EmittedComposerRegistrationTests`.** Its `EmittingProjects` list is both too wide
> and too narrow since the protocol split. `Vortex.Marketplace`, `Vortex.Navigator` and
> `Vortex.WebApi` no longer reference `Vortex.Protocol` at all (dead entries), while `Vortex.Social`
> (11 composers) and `Vortex.Progression` (18) **do** and are **not** listed. Checked: all 29 of
> those are currently mapped, so the gap is latent, not a live bug.

## Embedded default vs plugin revisions

`Revision20260701` is the default **embedded in core** so the emulator runs standalone. Editing its
`Parsers/` and `Serializers/` trees in this repository is correct and expected.

Any *additional* revision belongs in the plugin repo. How that actually works — and what could not be
verified — is [Protocol revision plugins](../09-extensibility/protocol-revision-plugins.md).

> **Both `Vortex.Revisions/README.md` and `Vortex.Revisions/Revision20260701/README.md` are stale.**
> They describe the pre-Maps single-dictionary checklist, name a `RevisionDefault/RevisionDefault.cs`
> that does not exist, and state that "vortex-cloud does not own `Revision<id>/Parsers` or
> `Serializers`" — which `AGENTS.md` and `CONTEXT.md` explicitly contradict for the embedded default.

## Known unknowns

- **Unknown:** whether each of the 102 unmapped serializers and 5 unmapped parsers is dead weight or
  an unfinished feature.
  - Inspected: the map registrations and the class names; the families are coherent (`Game2*`,
    `Talent*`, jukebox), which suggests deliberate scoping rather than accident.
  - Why unresolved: it needs a product decision per family, not a code reading.
  - What would resolve it: comparing each family against the client's own message registry to see
    whether the client can even send or receive it.

## Sources

- `Vortex.Primitives/Networking/Revisions/{IRevision,IRevisionManager,IRevisionMap,IRevisionMapBuilder}.cs`
- `Vortex.Revisions/RevisionBase.cs`, `RevisionMapBuilder.cs`, `RevisionRegistrationService.cs`
- `Vortex.Revisions/Revision20260701/Revision20260701.cs`, `Headers.cs`, `Maps/*.cs`
- `Vortex.Revisions/Configuration/ProtocolLimitsConfig.cs`, `RevisionConfig.cs`
- `Vortex.Revisions/Extensions/ServiceCollectionExtensions.cs`
- `Vortex.Networking/Revisions/RevisionManager.cs`, `Package/PackageEncoder.cs`
- `Vortex.PacketHandlers/Handshake/ClientHelloMessageHandler.cs`
- `Vortex.Revisions.Tests/EmittedComposerRegistrationTests.cs`, `SerializerPairingTests.cs`
- [Revision index](../generated/revision-index.md)
