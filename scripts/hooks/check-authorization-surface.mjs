#!/usr/bin/env node
// Every place that decides "may this actor do this?", and which idiom decides it.
//
// This check does NOT judge whether an authorization is correct. It cannot, and neither can a
// reviewer, which is the point: the same question is answered nineteen different ways in this
// repository, and five of them are invisible in a diff.
//
//   if (ctx.PlayerId <= 0) return;                        the handler guard
//   if (ctx.PlayerId <= 0 || ctx.RoomId <= 0) return;     the service guard
//   await SecurityModule.CanManipulateFurniAsync(ctx)     the explicit module call
//   await FindManipulableItemAsync(ctx, itemId)           a gate hidden inside a LOOKUP
//   EnsurePetOwner(ctx, pet);                             a private static that throws
//   if (item.OwnerId != ctx.PlayerId) return null;        an inline ownership comparison
//   permissions.Has(Capabilities.Room.FurniEdit)          in the HANDLER, grain delegating
//   .RequireAuthorization(cap) / .AddEndpointFilter(...)  / ctx.AccountId(s) / SelectedPlayerAsync(...)
//
// Two of those -- the gated lookup and the throw-helper -- read exactly like an ungated method.
// `_state.ItemsById.TryGetValue(id, out item)` and `FindManipulableItemAsync(ctx, id)` differ by one
// identifier, and only one of them asks the security module. So a method that forgets the gate
// produces a diff indistinguishable from one that does not, and no tool can compute coverage.
//
// What IS checkable is the surface itself. This records every actor-taking room-grain method and
// every HTTP endpoint together with the gate idiom found next to it, and blocks when that inventory
// moves: a NEW entry, or an existing entry whose gate disappeared. It does not ask you to be right.
// It asks you to be explicit, once, in the diff where it is cheap to notice.
//
//   node scripts/hooks/check-authorization-surface.mjs            exit 2 on a change
//   node scripts/hooks/check-authorization-surface.mjs --all      print the whole surface
//   node scripts/hooks/check-authorization-surface.mjs --update   accept the current surface
import { readFileSync, writeFileSync, existsSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, relative, resolve, sep } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '../..');
const showAll = process.argv.includes('--all');
const update = process.argv.includes('--update');
const baselineFile =
  process.env.VORTEX_AUTHZ_BASELINE ?? join(here, 'authorization-surface-baseline.json');

const strip = (s) => s.replace(/\/\/[^\n]*/g, '').replace(/\/\*[\s\S]*?\*\//g, '');

function walk(dir, out = []) {
  if (!existsSync(dir)) return out;
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    if (['obj', 'bin', '.git', 'node_modules'].includes(e.name)) continue;
    const p = join(dir, e.name);
    if (e.isDirectory()) walk(p, out);
    else if (e.name.endsWith('.cs')) out.push(p);
  }
  return out;
}

function blockAt(src, open) {
  let d = 0;
  for (let i = open; i < src.length; i++) {
    if (src[i] === '{') d++;
    else if (src[i] === '}' && --d === 0) return src.slice(open, i + 1);
  }
  return src.slice(open);
}

// ---- the gate idioms, named ---------------------------------------------------------------------
//
// Order matters only for reporting: the first match names the entry. A method using two is still
// one entry; what the baseline pins is that SOME named gate is there.
const GATES = [
  ['security-module', /SecurityModule\s*\.\s*([A-Za-z0-9_]+)/],
  ['controller-level', /GetControllerLevelAsync|RoomControllerType\s*\./],
  ['room-owner', /Is(?:Get)?RoomOwnerAsync|GetIsRoomOwnerAsync/],
  ['capability', /HasCapabilityAsync|permissions\s*\.\s*Has\s*\(|Capabilities\s*\./],
  ['moderation', /CanModerateAsync|HasStaffModerationCapabilityAsync|ModerationPolicy/],
  ['gated-lookup', /Find(?:Manipulable|RentedSpaceForOwned)[A-Za-z]*Async\s*\(|LoadIf[A-Za-z]*Async\s*\(|LoadForModerationAsync\s*\(/],
  ['gated-mutator', /MutateAs(?:Admin)?Async\s*\(/],
  ['group-rank', /IsAdminAsync\s*\(|GroupMemberRank|OwnerPlayerEntityId\s*(?:!=|==)/],
  ['permission-matrix', /Allows\s*\([A-Za-z.]*(?:Mod|Read|Post)Permission|CanRead\b|PostPermission/],
  ['ensure-helper', /Ensure[A-Za-z]*(?:Owner|Rights|Permission|Allowed|Authori)[A-Za-z]*\s*\(/],
  ['can-helper', /\bCan[A-Za-z]+Async\s*\(/],
  ['owner-compare', /Owner(?:Player(?:Entity)?)?Id\s*(?:!=|==)|(?:!=|==)\s*ctx\.PlayerId/],
  ['actor-guard', /(?:ctx\.)?PlayerId\s*(?:<=\s*0|<\s*1|==\s*0)|PlayerId\.Invalid/],
  ['explicit-rights', /HasExplicitRights|PlayerIdsWithRights/],
];

function gateOf(body) {
  const found = [];
  for (const [name, re] of GATES) if (re.test(body)) found.push(name);
  return found.length ? found.join('+') : null;
}

// ---- surface 1: grain methods that take an actor ------------------------------------------------
//
// Not only room grains. The rule the repository states -- "callable by anything in the cluster that
// can name it" -- has nothing to do with rooms, and the groups system proved it: fifteen methods on
// IGroupGrain and eight on IGroupForumGrain take an actor and decide who may kick, promote, rename
// and moderate. They are all gated today, through idioms found nowhere else in the tree.
const ifaceRoots = [
  join(root, 'Vortex.Primitives/Rooms/Grains'),
  join(root, 'Vortex.Primitives/Groups/Grains'),
  join(root, 'Vortex.Primitives/Players/Grains'),
];
const ACTOR =
  /\b(?:PlayerId\s+(?:actor|playerId|requesterId|viewer|viewerId|player)|ActionContext\s+(?:ctx|actorCtx))\b/;

const declared = [];
for (const ifaceDir of ifaceRoots) {
  const label = relative(join(root, 'Vortex.Primitives'), ifaceDir).split(sep)[0];
  for (const f of walk(ifaceDir)) {
    const src = strip(readFileSync(f, 'utf8'));
    for (const m of src.matchAll(/(?:Task|ValueTask)(?:<[^>]*>)?\s+([A-Za-z0-9_]+)\s*\(([^;{]*)\)/g)) {
      if (ACTOR.test(m[2]))
        declared.push({ iface: `${label}/${relative(ifaceDir, f).split(sep).join('/')}`, method: m[1] });
    }
  }
}

// Implementations, by method name, across Vortex.Rooms. A name can be implemented more than once
// (grain facade + module + system); the gate may live in any of them, so all are considered.
const impls = new Map();
for (const f of [
  ...walk(join(root, 'Vortex.Rooms')),
  ...walk(join(root, 'Vortex.Players')),
]) {
  const src = strip(readFileSync(f, 'utf8'));
  for (const m of src.matchAll(/(?:public|internal|private)\s[^\n;{}]*?\b([A-Za-z0-9_]+)\s*\(([^)]*)\)\s*(?:=>|\{)/g)) {
    const at = m.index + m[0].length - 1;
    const body = src[at] === '{' ? blockAt(src, at) : src.slice(at, src.indexOf(';', at) + 1 || undefined);
    if (!impls.has(m[1])) impls.set(m[1], []);
    impls.get(m[1]).push(body);
  }
}

// One hop: a facade that forwards to System.Method carries that method's gate.
function gateFor(method, depth = 0) {
  const bodies = impls.get(method) ?? [];
  if (!bodies.length) return depth === 0 ? 'IMPL-NOT-FOUND' : null;
  for (const b of bodies) {
    const g = gateOf(b);
    if (g) return g;
  }
  if (depth > 1) return null;
  for (const b of bodies) {
    for (const fwd of b.matchAll(/(?:[A-Za-z0-9_]+\s*\.\s*)?([A-Za-z0-9_]+)\s*\(\s*(?:ctx|actor|requesterId|playerId)\b/g)) {
      if (fwd[1] === method) continue;
      const g = gateFor(fwd[1], depth + 1);
      if (g && g !== 'IMPL-NOT-FOUND') return g;
    }
  }
  return null;
}

const entries = [];
for (const d of declared) {
  entries.push({ kind: 'grain', id: `${d.iface}::${d.method}`, gate: gateFor(d.method) ?? 'NONE' });
}

// ---- surface 2: HTTP endpoints ------------------------------------------------------------------

const HTTP_GATES = [
  ['require-authorization', /\.RequireAuthorization\s*\(/],
  ['endpoint-filter', /\.AddEndpointFilter\s*\(/],
  ['authorize-attribute', /\[Authorize/],
  ['account-id', /\.AccountId\s*\(/],
  ['selected-player', /SelectedPlayerAsync\s*\(/],
  ['anonymous', /\.AllowAnonymous\s*\(/],
  // A session, then a capability from IPermissionService (Hosting/CapabilityGate.cs): declared on
  // the route, or asked inline where the capability depends on the request.
  ['capability', /\.RequireCapability\s*\(|\bDenyAsync\s*\(/],
];

for (const f of walk(root)) {
  if (f.includes('.Tests') || f.includes(`${sep}Tests${sep}`)) continue;
  const raw = readFileSync(f, 'utf8');
  if (!/\.Map(?:Get|Post|Put|Delete|Patch)\(/.test(raw)) continue;
  const rel = relative(root, f).split(sep).join('/');
  const calls = [...raw.matchAll(/\.Map(Get|Post|Put|Delete|Patch)\(\s*"([^"]*)"/g)];
  // A group filter covers every endpoint declared on that group in the same file.
  const groupGate = /MapGroup\s*\([^)]*\)[\s\S]{0,400}?(\.AddEndpointFilter\s*\(|\.RequireAuthorization\s*\()/.test(raw)
    ? (/\.AddEndpointFilter\s*\(/.test(raw) ? 'endpoint-filter' : 'require-authorization')
    : null;
  for (let i = 0; i < calls.length; i++) {
    const seg = raw.slice(calls[i].index, i + 1 < calls.length ? calls[i + 1].index : Math.min(raw.length, calls[i].index + 6000));
    const found = HTTP_GATES.filter(([, re]) => re.test(seg)).map(([n]) => n);
    const gate = found.length ? found.join('+') : groupGate ?? 'NONE';
    entries.push({ kind: 'http', id: `${calls[i][1].toUpperCase()} ${calls[i][2]}  (${rel})`, gate });
  }
}

// ---- compare against the baseline ----------------------------------------------------------------

entries.sort((a, b) => a.kind.localeCompare(b.kind) || a.id.localeCompare(b.id));
const surface = Object.fromEntries(entries.map((e) => [`${e.kind}:${e.id}`, e.gate]));

if (update) {
  const notes = existsSync(baselineFile) ? (JSON.parse(readFileSync(baselineFile, 'utf8')).notes ?? {}) : {};
  writeFileSync(baselineFile, `${JSON.stringify({ surface, notes }, null, 2)}\n`);
  console.error(`check-authorization-surface: baseline written (${entries.length} entries).`);
  process.exit(0);
}

if (showAll) {
  for (const e of entries) console.error(`  ${e.gate.padEnd(34)} ${e.id}`);
  console.error('');
}

if (!existsSync(baselineFile)) {
  console.error('check-authorization-surface: no baseline; run --update to record the current surface.');
  process.exit(0);
}

const base = JSON.parse(readFileSync(baselineFile, 'utf8')).surface ?? {};
const added = Object.keys(surface).filter((k) => !(k in base));
const removed = Object.keys(base).filter((k) => !(k in surface));
const changed = Object.keys(surface).filter((k) => k in base && base[k] !== surface[k]);

if (added.length || changed.length) {
  console.error('check-authorization-surface: the authorization surface moved.\n');
  for (const k of added) {
    console.error(`  NEW      ${k}`);
    console.error(`           gate: ${surface[k]}${surface[k] === 'NONE' ? '   <-- nothing found; say which gate applies, or add one' : ''}`);
  }
  for (const k of changed) {
    console.error(`  CHANGED  ${k}`);
    console.error(`           ${base[k]}  ->  ${surface[k]}`);
  }
  if (removed.length) console.error(`\n  (${removed.length} entr(ies) gone; --update drops them.)`);
  console.error(
    '\nThis check does not claim any of these is wrong. It claims the decision is worth one\n' +
      'deliberate look, here, rather than in a bug report. Confirm the gate, then --update.'
  );
  process.exit(2);
}

if (removed.length) {
  console.error(`check-authorization-surface: ${removed.length} baselined entr(ies) are gone. Run --update to drop them.`);
}

const byGate = {};
for (const e of entries) byGate[e.gate === 'NONE' ? 'NONE' : e.gate.split('+')[0]] = (byGate[e.gate === 'NONE' ? 'NONE' : e.gate.split('+')[0]] ?? 0) + 1;
console.error(
  `check-authorization-surface: OK (${entries.length} entries: ` +
    `${entries.filter((e) => e.kind === 'grain').length} actor-taking grain methods, ` +
    `${entries.filter((e) => e.kind === 'http').length} HTTP endpoints; ` +
    `${Object.keys(byGate).length} distinct gate idioms).`
);
process.exit(0);
