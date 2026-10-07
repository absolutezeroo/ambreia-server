# Flow: room entry

A pointer page. The full 13-step trace, the exact composer burst, the three door gates and the leave
path live on **[Room lifecycle](../04-rooms/lifecycle.md)** — this page gives the shape and the
crossings into other domains.

## Trigger

`OpenFlatConnection` — room id, optional password.

## The shape

```
handler          OpenFlatConnectionMessageHandler   → IRoomService, nothing else
  │
gates            ban → re-entry short-circuit → pending set → ack
  │
activation       IRoomCore.EnsureRoomActiveAsync
  │                 OnActivateAsync: clocks, hydrate, register, bind stream, start 50ms tick
  │                 lazily: map arrays, floor items (tiles paused), wall items, pets
  │
authorization    IRoomSecurity.GetControllerLevelAsync → RoomSecurityPolicy ladder
  │
door gates       (only below Rights)  full / password / doorbell
  │
burst            ~15 composers in a fixed order
  │
membership       PlayerPresenceGrain.SetActiveRoomAsync
                    directory ← AddPlayerToRoomAsync
                    stream    ← subscribe as IAsyncObserver<RoomOutbound>
                    room      ← CreateAvatarFromPlayerAsync
```

## Why it crosses so many domains

| Crossing | Page |
|---|---|
| the ban check reads `room_bans` before anything else | [Ownership boundaries](../07-database/ownership-boundaries.md) |
| hydration is one `AsNoTracking` read plus rights and guild ranks | [Room lifecycle](../04-rooms/lifecycle.md) |
| the controller-level ladder and the two-part live-rights invariant | [Room architecture](../04-rooms/room-architecture.md) |
| a locked door parks the entry in `PendingDoorbellRingersMs` until an answer or a 20 s tick sweep | [Movement and tick](../04-rooms/movement-and-tick.md) |
| stream subscription is what makes room fan-out work at all | [Presence routing](../03-orleans/presence-routing.md) |
| the avatar's `flatctrl` status is stamped at creation from the resolved level | [Room architecture](../04-rooms/room-architecture.md) |
| `PlayerEnterEvent` is what feeds the wired `wf_trg_enter_room` trigger | Wireds (retirés au M0) |

## The one thing to remember

**A room never writes to a socket.** `CompleteRoomEntryAsync` sends the burst through the *entering
player's* presence grain, and every later room broadcast goes to an Orleans memory stream that each
occupant's presence grain is subscribed to.

That is what lets a room grain deactivate, be tested without a network stack, and — in principle —
relocate.

## Two entry paths, not identical

`RoomService.CompleteRoomEntryAsync` replays dances only.
`GetRoomEntryDataMessageHandler` replays dances **plus** avatar effects and hand items.

Whether that asymmetry is intentional is documented nowhere.
→ [Room lifecycle](../04-rooms/lifecycle.md)

## Leaving

Three triggers — explicit quit, disconnect, presence deactivation — all converge on
`PlayerPresenceGrain.ClearActiveRoomAsync`, which is **deliberately best-effort**: the room call is
wrapped in try/catch and the directory removal uses `CancellationToken.None`, because a ghost occupant
blocks re-entry and lingers in the navigator.

## Sources

- `Vortex.PacketHandlers/Room/Session/OpenFlatConnectionMessageHandler.cs`
- `Vortex.Rooms/RoomService.cs`, `RoomService.Doorbell.cs`
- `Vortex.Rooms/Grains/RoomGrain.cs`, `RoomGrain.Avatar.cs`
- `Vortex.Players/Grains/PlayerPresenceGrain.Room.cs`
- [Room lifecycle](../04-rooms/lifecycle.md) — the authoritative trace
