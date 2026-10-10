---
name: adding-rpg-content
description: Adds, changes, renames or retires RPG content in the Vortex emulator — a class, specialization, item, monster, skill, status or progression as a JSON document in content/, its French texts, a content migration, a field added to or removed from a Definitions record, or a whole new kind of content — then validates the pack and pushes it to the running emulator. Use whenever the user wants a new item, monster, skill, status or class, tweaks numbers (damage, cooldown, health, XP), renames or removes a content id, touches Vortex.Rpg/Content, or when the emulator refuses to start with "contenu invalide".
---

# RPG content

Content is data, not code: one JSON document per definition in `content/<kind>/`, its texts in
`content/texts/fr.txt`, the whole pack pushed by `POST /api/content/import`. The server validates the
**whole pack**: one error refuses everything. Spec: `docs/superpowers/specs/2026-10-08-rpg-content-design.md`.

## Which case are you in?

| You want to… | Go to |
|---|---|
| Add or tune an item, monster, skill, status, class… | **A. A document** |
| Rename an id, or take one out of the game | **B. Ids that leave** |
| Add or remove a field of a record in `Vortex.Rpg/Content/Definitions` | **C. A record changes** |
| A kind that does not exist yet (`quest`, `loot`…) | **D. A new kind** |

## A. A document

1. **Copy a neighbour** of the same kind (`content/status/burning.json`, `content/monster/forest_wolf.json`).
   Keep its `"$schema": "../../schemas/content/<kind>.schema.json"` line: it gives autocompletion.
2. **The id** is `<prefix>.<segment>[.<segment>…]`: lower case, digits, `_`, 64 characters at most
   (`ContentIds`). The first segment is the kind, except `spec.` for specializations. The file name is
   free (`item/rusted_sword.json` holds `item.weapon.rusted_sword`), but an id **never changes** once
   pushed: renaming is case B.
3. **Strict JSON**: an unknown field, a `null`, a number in quotes are refused. The rules of each kind
   are in its `Check` (`Vortex.Rpg/Content/Kinds/<Kind>Kind.cs`): read it before choosing values.
   A reference to another content (a skill's status, an item's allowed classes) must exist in the pack
   with the right kind. A monster's `behavior` and a skill effect's `type` are keys declared by a
   module (`IMonsterBehavior`, `ISkillEffect`): an unknown key is an error.
4. **Texts**: add `<id>.name=` and `<id>.description=` to `content/texts/fr.txt`, in the block of its
   kind, plus any key the kind declares (`TextKeys`: a monster's phase yells). A missing text, or a text
   with no content, is a warning — and the repository pack must have **zero** findings.
5. **Validate offline**:
   `dotnet test Vortex.Hosting.Tests --filter "FullyQualifiedName~ContentPackInRepoTests"`.
   It prints every finding with its file and id. It pins counts (12 skills, 7 statuses…) and the list
   of shipped effects and behaviours: updating them is part of the change.
6. **Push it live**: `node .claude/skills/run-vortex-emulator/driver.mjs content` (the emulator must be
   running through the driver). It prints `added / changed / removed` and the findings; a second push
   answers 0 / 0. Then prove the change in game with the matching driver scenario (`combat`, `skills`).

## B. Ids that leave

A push is the whole pack: a document absent from `content/` is **removed**. Before removing, ask who
holds the id. Players hold class, specialization and item ids (`IContentReferences` in
`Vortex.Rpg.Characters/Content`).

| Case | Do |
|---|---|
| Nobody holds it | delete the file and its texts |
| Players hold it, it must go | delete it, and add `{ "from": "item.a", "action": "replace", "to": "item.b" }` to `content/migrations.json` (same kind, `item.b` in the pack) |
| Players keep it, nobody gets it any more | keep the document with `"retired": true` |
| A rename | the new id is a new document + a `replace` rule from the old one |

A removed id still held, with no rule, refuses the whole import: that is the guard working.

## C. A record changes

The registry re-reads **every stored document** strictly at start, and invalid stored content
refuses the start. The stored documents are the last pushed pack, not `content/`. Hence:

- **A field added** is optional with a default (`public int X { get; init; }`, `= []` for a list),
  never `required`: stored documents do not have it.
- **A field removed** stays declared, ignored, until a pack without it has been pushed. Remove it in a
  second change.
- Then regenerate the schemas:
  `UPDATE_CONTENT_SCHEMAS=1 dotnet test Vortex.Hosting.Tests --filter ContentSchemaTests` (PowerShell:
  `$env:UPDATE_CONTENT_SCHEMAS='1'` first, then clear it), and commit `schemas/content/*.schema.json`.
- A new rule in the kind's `Check` can make stored content invalid too: push the fixed pack before
  restarting on the new code.
- A grain that keeps a content id in its row (a character's class) never writes the id back from
  memory, and re-reads it when `IRpgContent.Version` changes: an import's `replace` rewrites the rows.

**Locked out anyway** (the emulator refuses to start): with the emulator stopped, the owner runs
`scripts/sql/rpg-content-reset.sql` (it retires every stored row; propose it, `/sql-fix` style), start,
then push again.

## D. A new kind

Ask first: a kind is a design decision of a spec, not a file.

1. Its record in `Vortex.Rpg/Content/Definitions/` (`sealed record XDefinition : ContentDefinition`,
   one type per file, enums in their own files).
2. Its `XKind : ContentKind<XDefinition>` in `Vortex.Rpg/Content/Kinds/`: `Kind`, `IdPrefix` if it
   differs, `Public` only if the client draws it (served at `/api/public/content/definitions/{kind}`),
   `Check`, `TextKeys` if it has texts beyond name and description.
3. Declared by the module that owns it: `services.AddSingleton<IContentKind, XKind>()` (`RpgModule`,
   or the system's own module). No scanning: the registry needs every kind before it loads.
4. If players will hold its ids: an `IContentReferences` declared by the system that stores them.
5. Tests: add it to `ContentSchemaTests` (`Kinds()` and `ByName`), regenerate the schema (C), add it to
   `ContentPackInRepoTests`, write a `<Kind>KindTests` for its rules in the owning system's test
   project (`Vortex.Rpg.Tests/Content/MonsterKindTests.cs`, `Vortex.Rpg.Skills.Tests/Content/`).
6. A first document in `content/<kind>/`, then A.5 and A.6.

## Done

`ContentPackInRepoTests` green with zero findings, a push answering `added`/`changed` as expected and
then 0 / 0, the live scenario showing the change, and the `VortexCloudQualityGate` when code changed.
Report the commands and their output.
