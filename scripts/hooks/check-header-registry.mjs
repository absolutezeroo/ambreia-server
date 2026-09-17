#!/usr/bin/env node
// Every header id we map must exist in the client's own message registry.
//
// This is the check that the old ceiling heuristic was approximating. An id above the client's
// maximum is dead, yes -- but so is an id in the middle of the range that the client simply never
// registered, and there are far more of those. Both register cleanly on our side, both can never
// fire, and neither shows up in the build, the tests or a grep. 14 of our 955 mapped headers are in
// that state right now: 14 handlers and composers that look implemented and are not reachable.
//
//   node scripts/hooks/check-header-registry.mjs
//   node scripts/hooks/check-header-registry.mjs --update   # accept the current list as baseline
//
// The client checkout lives outside this repository (same rule as the specs: a sibling holding a
// `sources` directory). Without it there is no registry to compare against, so the script falls
// back to the ceiling check -- weaker, but it needs nothing but this repository.
//
// Exit 2 = a mapped id the client's registry does not contain, and the baseline does not know about.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
// The override exists so the hook test can point at an empty baseline and see the block happen.
const baselineFile =
  process.env.VORTEX_HEADER_BASELINE ?? path.join(root, 'scripts', 'hooks', 'header-registry-baseline.json');
const revision = 'Revision20260701';
const headersFile = path.join(root, 'Vortex.Revisions', revision, 'Headers.cs');
const mapsDir = path.join(root, 'Vortex.Revisions', revision, 'Maps');
const update = process.argv.includes('--update');

// Highest id in the registry of the client build this revision targets. Only used when the client
// sources are absent; when they are present the registry itself is the answer.
const CEILING = 4101;
// ---- our side ------------------------------------------------------------------------------------
const headers = {};
let section = '';
for (const line of fs.readFileSync(headersFile, 'utf8').split(/\r?\n/)) {
  const enclosing = /class\s+(MessageEvent|MessageComposer)\b/.exec(line);
  if (enclosing) {
    section = enclosing[1];
    continue;
  }
  const constant = /const\s+int\s+([A-Za-z0-9_]+)\s*=\s*(\d+)\s*;/.exec(line);
  if (constant) headers[constant[1]] = { id: Number(constant[2]), section };
}

// A constant nobody maps is inert. Only what the revision actually wires up can be dead.
const mapped = new Set();
for (const file of fs.readdirSync(mapsDir)) {
  const text = fs.readFileSync(path.join(mapsDir, file), 'utf8');
  for (const use of text.matchAll(/Message(?:Event|Composer)\.([A-Za-z0-9_]+)/g)) mapped.add(use[1]);
}
const ours = [...mapped].filter((name) => headers[name]);

// Ids deliberately outside the official range: only a modified client speaks these. The AS3
// registry this script reads is a Habbo build's, so a Vortex-invented message is absent from it by
// construction -- being unreachable there is the definition of an extension, not a defect.
const EXTENSIONS = new Set([
  'RentableSpaceGetConfigMessageEvent',
  'RentableSpaceConfigMessageComposer',
  'RentableSpaceConfigureMessageEvent',
  // Raid protection's two reassigned composers. The feature is real and AIR-1.0.31 binds it, but at
  // 734 and 3620 -- both already spent in this revision on messages WIN63 really reads. So the
  // numbers were reassigned, in agreement with vortex-modern-client, which had to solve the same
  // collision and whose HabboMessages.ts carries the same pair. They sit inside the official range
  // rather than the 8000-8999 band because they were picked to be free in BOTH registries and in
  // that file, which is what makes the two sides line up; being absent from a Habbo registry is
  // therefore the expected state, not a dead mapping.
  'RaidProtectionCapabilityMessageComposer',
  'RaidProtectionSettingsResultMessageComposer',
]);
// 8000-8999 is the Vortex-specific band (Headers.cs, "Vortex-specific (no AS3 backing)"), reserved
// because it is empty in both registries. Naming each id here as well would mean editing this file
// on every Vortex packet -- and forgetting to would report a working message as dead. The three
// names above stay listed because they sit at 4600/4601, inside the official range.
const EXTENSION_BAND_FIRST = 8000;
const EXTENSION_BAND_LAST = 8999;
const isExtension = (name) =>
  EXTENSIONS.has(name) ||
  (headers[name].id >= EXTENSION_BAND_FIRST && headers[name].id <= EXTENSION_BAND_LAST);

// ---- the client's registry -----------------------------------------------------------------------
function findClientTrees() {
  const parent = path.dirname(root);
  const roots = [];
  for (const entry of fs.readdirSync(parent, { withFileTypes: true })) {
    if (!entry.isDirectory()) continue;
    const sources = path.join(parent, entry.name, 'sources');
    if (!fs.existsSync(sources)) continue;
    for (const build of fs.readdirSync(sources, { withFileTypes: true })) {
      if (!build.isDirectory()) continue;
      const src = path.join(sources, build.name, 'src');
      if (fs.existsSync(path.join(src, 'com', 'sulake', 'habbo', 'communication')))
        roots.push({ src, build: build.name });
    }
  }
  return roots;
}

function indexClasses(src) {
  const files = new Map();
  (function walk(dir) {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.name.endsWith('.as')) files.set(entry.name.slice(0, -3), full);
    }
  })(src);
  return files;
}

// The registry is one class holding two id tables. Its file name is obfuscated and changes with the
// build, so find it by content: the file in communication/ with the most `table[id] = Class` lines.
function readRegistry(src) {
  const dir = path.join(src, 'com', 'sulake', 'habbo', 'communication');
  let best = { count: 0, entries: [] };
  for (const name of fs.readdirSync(dir).filter((f) => f.endsWith('.as'))) {
    const entries = [
      ...fs.readFileSync(path.join(dir, name), 'utf8').matchAll(/(\w+)\[(\d+)\]\s*=\s*([A-Za-z_]\w*)\s*;/g),
    ].map((m) => ({ table: m[1], id: Number(m[2]), cls: m[3] }));
    if (entries.length > best.count) best = { count: entries.length, entries };
  }
  return best.entries;
}

// Which table is which direction, decided from the classes themselves rather than from a variable
// name the next client build will rename. A class the client COMPOSES is one it sends: that is an
// incoming message for us. One that `extends MessageEvent` is one it receives: our composer.
function directionOf(table, entries, classFiles) {
  let sent = 0;
  let received = 0;
  for (const entry of entries.filter((e) => e.table === table).slice(0, 24)) {
    const file = classFiles.get(entry.cls);
    if (!file) continue;
    const text = fs.readFileSync(file, 'utf8');
    if (/implements\s+IMessageComposer/.test(text)) sent++;
    else if (/extends\s+MessageEvent\b|implements\s+IMessageParser/.test(text)) received++;
  }
  if (sent === 0 && received === 0) return null; // no signal: refuse to guess a direction
  return sent > received ? 'MessageEvent' : 'MessageComposer';
}

// More than one client can sit beside the repository -- this checkout has the 2016 Flash client and
// two WIN63 builds. Only the one this revision targets can arbitrate its ids, so match on the
// revision's own date before falling back to whichever registry is largest.
const stamp = /(\d{6})/.exec(revision)?.[1] ?? '';
const trees = findClientTrees().sort(
  (a, b) => Number(b.build.includes(stamp)) - Number(a.build.includes(stamp))
);
// One tree's registry, resolved into the two directions, or null when it cannot be read with
// confidence.
function readKnown(src) {
  const entries = readRegistry(src);
  if (entries.length === 0) return null;
  const classFiles = indexClasses(src);
  const found = { MessageEvent: new Set(), MessageComposer: new Set() };
  let directions = 0;
  for (const table of new Set(entries.map((e) => e.table))) {
    const direction = directionOf(table, entries, classFiles);
    if (!direction) continue;
    directions++;
    for (const entry of entries.filter((e) => e.table === table)) found[direction].add(entry.id);
  }
  // Both directions or nothing: a half-read registry would report every composer as unreachable.
  if (directions >= 2 && found.MessageEvent.size > 0 && found.MessageComposer.size > 0) return found;
  return null;
}

// ---- the client's registry, second source: the TypeScript port ------------------------------------
//
// The AS3 dump lives under `sources/`, which vortex-modern-client GITIGNORES (line 14). So on every
// machine that did not unpack a client by hand -- CI included -- the reader above finds nothing and
// this check degrades to comparing against an id ceiling, which catches almost nothing.
//
// The TypeScript port carries the same two tables, in one COMMITTED file:
//
//   this._events.set(230, AuthenticationOKMessageEvent);        // the client RECEIVES it
//   this._composers.set(1953, WiredClickUserMessageComposer);   // the client SENDS it
//
// The direction inverts across the wire, and getting it backwards would report every id as
// unreachable, so it is pinned to two ids verified by hand against Headers.cs: 230 is
// AuthenticationOKMessageComposer on our side (we send what the client receives), and 1953 is
// WiredClickUserMessageEvent (we receive what the client sends).
function findClientTsRegistry() {
  const RELATIVE = path.join(
    'packages',
    'vortex-engine',
    'src',
    'habbo',
    'communication',
    'HabboMessages.ts'
  );
  const parent = path.dirname(root);
  const candidates = [];
  if (process.env.VORTEX_CLIENT_ROOT)
    candidates.push(path.join(process.env.VORTEX_CLIENT_ROOT, RELATIVE));
  candidates.push(path.join(parent, 'vortex-modern-client', RELATIVE));
  // The layout add_repo produces: <parent>/<owner>/<repo>.
  for (const entry of fs.existsSync(parent) ? fs.readdirSync(parent) : [])
    candidates.push(path.join(parent, entry, 'vortex-modern-client', RELATIVE));
  return candidates.find((c) => fs.existsSync(c)) ?? null;
}

function readKnownFromTypeScript(file) {
  const src = fs.readFileSync(file, 'utf8');
  const found = { MessageEvent: new Set(), MessageComposer: new Set() };
  for (const m of src.matchAll(/this\._(events|composers)\.set\(\s*(\d+)\s*,/g)) {
    // client receives -> our composer; client sends -> our incoming event.
    found[m[1] === 'events' ? 'MessageComposer' : 'MessageEvent'].add(Number(m[2]));
  }
  // Both directions or nothing, same rule as the AS3 reader: a half-read registry would report
  // every composer as unreachable.
  return found.MessageEvent.size > 0 && found.MessageComposer.size > 0 ? found : null;
}

// Same purpose as VORTEX_HEADER_BASELINE above: the ceiling fallback is the only path CI ever takes,
// and it cannot be reached on a machine that has a client sitting beside the repository -- which is
// exactly why it shipped broken and nobody saw it locally.
const forceNoClient = process.env.VORTEX_HEADER_NO_CLIENT === '1';

const tsRegistry = forceNoClient ? null : findClientTsRegistry();
const as3Known = !forceNoClient && trees.length > 0 ? readKnown(trees[0].src) : null;
const known = as3Known ?? (tsRegistry ? readKnownFromTypeScript(tsRegistry) : null);

// Named so a reader can tell which authority answered. "It exists somewhere" with no source would
// be indistinguishable from a guess -- the same reason the later-build list below names its build.
const registrySource = as3Known
  ? trees[0].build
  : tsRegistry
    ? `${path.relative(path.dirname(root), tsRegistry).split(path.sep).slice(-2).join('/')} (TypeScript port)`
    : 'none';

// The other Habbo builds sitting beside this repository.
//
// A feature can be newer than the build this revision targets: Variable FX is in the AIR client and
// in no WIN63 at all. An id read out of one of those registries is still an id a real Habbo build
// binds, which is the whole guarantee this check exists to give -- it is there to catch an id
// somebody invented, not an id that arrived in a later client.
//
// So those are reported and named rather than failed. Naming the build is the point: "it exists
// somewhere else" with no source would be indistinguishable from a guess, and the next reader could
// not tell which they were looking at.
const later = new Map();

for (const tree of trees.slice(1)) {
  const found = readKnown(tree.src);
  if (!found) continue;
  for (const section of ['MessageEvent', 'MessageComposer'])
    for (const id of found[section])
      if (!later.has(`${section}/${id}`)) later.set(`${section}/${id}`, tree.build);
}

// ---- the baseline ---------------------------------------------------------------------------------
//
// Read here rather than after the comparison, because the ceiling fallback below needs it too. It did
// not have it: the fallback exited 2 on any id above the ceiling without ever opening this file, so a
// mapping the baseline explicitly tolerates still failed the gate on every machine with no client
// beside the repository -- CI included, where this is the only path that ever runs. 9201 is the
// entry that did it, and the three CI jobs died on a header the repository had already accepted.
if (!fs.existsSync(baselineFile)) {
  console.error(`check-header-registry: no baseline at ${path.relative(root, baselineFile)}. Run with --update.`);
  process.exit(2);
}

const baseline = new Set(JSON.parse(fs.readFileSync(baselineFile, 'utf8')).unreachable);

// ---- fallback: the ceiling, when there is no client to ask ----------------------------------------
if (!known) {
  // Same entry spelling as the comparison below, so one baseline answers for both paths.
  const above = ours
    .filter((name) => headers[name].id > CEILING && !isExtension(name))
    .map((name) => `${headers[name].section}/${name}=${headers[name].id}`);
  const unbaselined = above.filter((entry) => !baseline.has(entry));

  console.error(
    'check-header-registry: no client sources beside this repository -- falling back to the id ceiling.'
  );
  if (unbaselined.length) {
    console.error(`\nMapped header ids above the ${revision} client ceiling (${CEILING}):`);
    for (const entry of unbaselined) console.error(`  - ${entry}`);
    console.error('\nAn id the client does not know registers fine and never fires.');
    process.exit(2);
  }
  console.error(
    `check-header-registry: ceiling OK (${ours.length} mapped headers, ${above.length} baselined above the ceiling).`
  );
  process.exit(0);
}

// ---- compare -------------------------------------------------------------------------------------
const missing = ours.filter(
  (name) => !isExtension(name) && !known[headers[name].section].has(headers[name].id)
);

const unreachable = [];
const fromLaterBuild = [];

for (const name of missing) {
  const header = headers[name];
  const build = later.get(`${header.section}/${header.id}`);
  const entry = `${header.section}/${name}=${header.id}`;
  if (build) fromLaterBuild.push(`${entry} (${build})`);
  else unreachable.push(entry);
}

unreachable.sort();
fromLaterBuild.sort();

if (fromLaterBuild.length) {
  console.error(
    `check-header-registry: ${fromLaterBuild.length} id(s) absent from ${registrySource} but bound by a later build:`
  );
  for (const entry of fromLaterBuild) console.error(`  - ${entry}`);
}

if (update) {
  // Keep the file's `notes`: they say why an entry is tolerated, and regenerating the list is not a
  // reason to lose that.
  const notes = fs.existsSync(baselineFile)
    ? (JSON.parse(fs.readFileSync(baselineFile, 'utf8')).notes ?? {})
    : {};
  fs.writeFileSync(baselineFile, `${JSON.stringify({ unreachable, notes }, null, 2)}\n`);
  console.error(`check-header-registry: baseline written (${unreachable.length} unreachable mappings).`);
  process.exit(0);
}

const added = unreachable.filter((entry) => !baseline.has(entry));
const fixed = [...baseline].filter((entry) => !unreachable.includes(entry));

for (const entry of fixed) console.error(`warning: ${entry} is reachable now -- run --update to lock it in`);

if (added.length) {
  console.error(`\nMapped to a header id the client's registry does not contain (${added.length}):`);
  for (const entry of added) console.error(`  - ${entry}`);
  console.error(
    '\nThe handler or composer registers cleanly and can never fire. Find the id the client actually\n' +
      "binds for this message in its registry, or drop the mapping. Don't baseline it to move on."
  );
  process.exit(2);
}

console.error(
  `check-header-registry: OK (${ours.length} mapped headers, ${unreachable.length} known unreachable, registry from ${registrySource}).`
);
