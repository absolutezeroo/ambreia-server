# Glossary

## The main glossary lives elsewhere

**[`docs/glossary.md`](../../glossary.md)** is the repository's glossary and is current. It defines the
two vocabularies this codebase sits between — Habbo domain terms (controller level, rights, wired,
furni) and Vortex/Orleans terms (grain, presence, snapshot, composer, system vs module) — and points
at the file that embodies each.

Read that first. This page adds only what it does not cover, plus one correction.

## Correction

> `docs/glossary.md` refers to incoming message types under **`Vortex.Primitives/Messages/`**. That
> directory **does not exist**. Message records and composers live in
> `Vortex.Protocol/Messages/{Incoming,Outgoing}/<Domain>/` after commit `7919b871`.
> → [Packet pipeline](../02-network-protocol/packet-pipeline.md)

## Terms this documentation adds

**Facet**
One of 14 grain interfaces (`IRoomCore`, `IRoomAvatars`, `IRoomFurni`, …) all implemented by the same
`RoomGrain` class, so every one resolves to the same activation for a room id. Requesting a narrow
facet is free and documents intent.
→ [Grain map](../03-orleans/grain-map.md)

**Map** (revision map)
One `IRevisionMap` class per protocol domain, registering that domain's parsers and serializers into
the revision tables. 43 of them replaced a single 3612-line dictionary file.
→ [Revisions](../02-network-protocol/revisions.md)

**Mapped vs written**
A parser or serializer class **exists** (written) versus is **registered by a map** (mapped). 102
serializers are written and unmapped, which means they compile, ship, and can never run.

**Pivot**
The irreversible moment in a value-moving flow where the goods change hands. Before it, failure is
compensated by refunding; after it, failure is retried, never reversed.
→ [Economy overview](../06-economy/overview.md)

**Journal / receipt**
`commerce_operations` plus `commerce_receipts`. The receipt's `(operation_id, step_key)` uniqueness is
the replay guard — `TryRecordStepAsync` lets the insert fail and reads that failure as "already ran".

**View-only**
A method that mutates an in-memory collection and notifies the client, without writing the database.
`IInventoryGrain.Add/RemoveFurnitureAsync` are the canonical case, and the most misread contract in
the repository. → [Inventory](../06-economy/inventory.md)

**Loss window**
The interval between a memory-first mutation and its batched flush — 2 s for furniture, 60 s for pet
stats, zero for permanent wired variables and for commerce.
→ [State ownership](../03-orleans/persistence.md)

**Live state vs DB-owned**
Which of the two is authoritative *while a grain is activated*. A table is not the owner of the value
in it.

**Pile** (wired)
The set of wired boxes co-located on one tile. Resolved **live at fire time**, never cached — which is
what makes the same-pile rule enforceable by construction.
→ [Wireds](../04-rooms/wireds.md)

**Wall**
One of six text-scan rules in `check-architecture-walls.mjs`, ratcheted against a baseline, that fail
the build on an architectural violation the compiler cannot see.
→ [Architecture boundaries](architecture-boundaries.md)

**Capability**
A `dashboard.*` string that gates one authorization policy. 52 of them, duplicated across four files,
with three different guards covering different halves.
→ [Capabilities](../08-dashboard/capabilities.md)

**Class (a) / (b) / (c) / (d)**
The four shapes an admin write can take, distinguished by whether the live hotel actually changed.
→ [Dashboard operations](../08-dashboard/operations.md)

**LoadStage**
An `IReferenceDataProvider`'s ordering rank at startup. Stage 0 loads before stage 1; providers within
a stage run under one `Task.WhenAll`. Furniture definitions are stage 0 because the catalog reads them
at stage 1. → [Startup](../01-runtime/startup.md)

## Sources

- `docs/glossary.md` — the primary glossary
- the pages linked above for each added term
