# Captures

A capture is the only evidence in this tree that can answer "what does the real Habbo server
do". Client code shows what a packet looks like. Reference emulators show what somebody else
decided to do about it. Neither is Habbo.

Drop capture files in this directory as `*.json` and run `habbo-spec import-capture <file>`
or `habbo-spec bootstrap`.

## Format

```json
{
  "id": "room-move-furniture-001",
  "source": "official",
  "revision": "WIN63-202607011411-782849652",
  "recordedUtc": "2026-08-21T12:00:00Z",
  "note": "moving a rug one tile, owner of the room",
  "messages": [
    {
      "index": 0,
      "direction": "client_to_server",
      "name": "MoveObject",
      "header": 1482,
      "fields": { "object_id": "4021", "x": "7", "y": "3", "rotation": "2" }
    },
    {
      "index": 1,
      "direction": "server_to_client",
      "name": "ObjectUpdate",
      "recipient": "room_users"
    }
  ]
}
```

- `source` decides how much the capture is worth. `official` is the only value that settles a
  question; `third_party_server` is evidence about that server; `vortex` is useful for
  differential testing; anything else is treated as unbacked and said so in the specs.
- `name` or `header` is required on every message. A message carrying only a header id is
  resolved through the revision registry, which needs `revision` to name a build this
  workspace knows.
- `recipient` is optional and usually absent: one client cannot see what the rest of the room
  received. Capturing the same action from two accounts is what fills it in, and the recipient
  is part of the behaviour.
- `fields` are optional. Without them a capture still establishes which packets are sent and in
  what order, which is most of what is missing.
- `respondsTo` is the index of the request a server message answers. Pairing is otherwise
  positional — everything up to the next client message — so two requests sent before either
  answer arrives give both answers to the second one and leave the first reading as
  unanswered. Set it only where the capture really settles it (distinct payloads, a matching
  id); a wrong one invents official behaviour, and an index that is not a `client_to_server`
  message in the same capture is an error.