# G-Earth capture recorder

Records a G-Earth session straight into `docs/habbo-specs/evidence/captures/`, in the schema
`Vortex.Specs` already imports. It exists because that schema was being filled in by hand: the
three captures in the tree carry no `fields`, no `timestampMs` and no `payloadHex`, because
nobody is going to type those.

```bash
python -m pip install g-python
python vortex_capture.py --selftest    # asserts the output shape, no G-Earth needed
```

Then add `vortex_capture.py` to G-Earth's extension list. **Double-click** the extension to
start recording, **double-click again** to write the file. Losing the connection writes it too.

## Before recording

Write `capture.config.json` next to the script. It is re-read at the start of every recording,
so it can be edited between two without restarting anything.

```json
{
  "id": "room-move-furniture-001",
  "source": "official",
  "revision": "AIR-1.0.31",
  "note": "moving a rug one tile, owner of the room"
}
```

`source` is the only field that decides what the capture is worth — `official`, `vortex`,
`third_party_server`. Leave it out and the importer treats the file as unbacked and says so.
An `id` that already exists is never overwritten; the next recording gets `-002`.

## What it does with names

Header ids are per-revision. The workspace can name 48 of AIR-1.0.31's 1181 packets
(`docs/habbo-specs/revisions/as3_AIR-1.0.31.yaml`), so an id alone is not enough to say what a
message was. The recorder writes the name G-Earth resolved, and writes no name at all when
G-Earth has none — an unresolved id is left unresolved rather than matched against a registry
for a different build.

`payloadHex` is the body only: the 6-byte prefix is the length and the header id, both already
their own fields. Nothing reads `payloadHex` yet.

## Why it is this dumb

The analysis already exists on the other side, and that is where it stays:

```bash
dotnet run --project Vortex.Specs.Cli -- import-capture <file>
dotnet run --project Vortex.Specs.Cli -- diff <official.json> <vortex.json>
```

`diff` compares the same trigger recorded against Habbo and against this emulator. Record the
same action twice, once with `"source": "official"` and once with `"source": "vortex"`, and it
prints what our server does differently.
