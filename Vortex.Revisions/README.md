# Revision Protocol Contract

This folder is the source of truth for plugin protocol revision mappings.

## What lives here
- Revision definitions:
  - `RevisionDefault/RevisionDefault.cs`
  - `Revision20260701/Revision20260701.cs`
- Incoming packet parsers:
  - `Revision<id>/Parsers/<Domain>/*MessageParser.cs`
- Outgoing message serializers:
  - `Revision<id>/Serializers/<Domain>/*MessageComposerSerializer.cs`

## Cross-repo boundary (important)
- `turbo-sample-plugin` owns revision parser/serializer code.
- `vortex-cloud` does not own `Revision<id>/Parsers` or `Revision<id>/Serializers`.
- `vortex-cloud` owns runtime host/domain handlers; plugin revision folders own packet read/write mappings.
- For handler/grain architecture rules, follow `AGENTS.md` and `.claude/rules/protocol.md`.
- For reusable AI task recipes/prompt contract, follow `../vortex-cloud/AGENTS.md`.

## When adding protocol support
1. Add parser(s) under `Parsers/<Domain>/`.
2. Add serializer(s) under `Serializers/<Domain>/` when needed.
3. Register header/parser/serializer mapping in the relevant `Revision<id>.cs`.
4. Keep naming consistent: `*MessageParser.cs` and `*MessageComposerSerializer.cs`.
5. Keep parsers/serializers deterministic and side-effect free.

## Revision20260701 packet checklist
For changes under `TurboSamplePlugin/Revision/Revision20260701`:
1. Update `Headers.cs`:
   - incoming ids in `MessageEvent`
   - outgoing ids in `MessageComposer`
2. Add parser class in `Parsers/<Domain>/`.
3. Add serializer class in `Serializers/<Domain>/` for outgoing packets.
4. Register mappings in `Revision20260701.cs`:
   - `Parsers` dictionary for incoming events
   - `Serializers` dictionary for outgoing composers
5. Add/adjust `using` directives in `Revision20260701.cs` for new parser/serializer namespaces.

## Production revisions

`Revision20260701` is the dev revision, `AMBREIA-DEV`. A production server never speaks it (its ids
are in this repository): it speaks a `ReleaseRevision`, the same parsers and serializers under ids
drawn at random for each deploy.

1. `node scripts/revision-new.mjs` draws every id of `Revision20260701/Headers.cs` again into
   `Releases/AMBREIA-<UTC yyyyMMddHHmm>.json`; `ClientHelloMessageEvent` and `DisconnectReasonComposer`
   stay on 4000. Commit the file.
2. The file is embedded in `Vortex.Revisions.dll`. Outside Development (or with
   `Vortex:Revisions:Release=true`) the server loads the newest one, and refuses to start if it does
   not cover every message the dev revision maps.
3. Build the client against the same file (`AMBREIA_RELEASE=<path> pnpm build` in vortex-rpg-client)
   and deploy both. A client on another revision is sent DisconnectReason 122 and offered a reload.

Spec: `vortex-rpg-client/docs/superpowers/specs/2026-10-08-random-header-revisions-design.md`.
