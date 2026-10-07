#!/usr/bin/env node
// One bug class, two seams.
//
// Every one of these is a place where a declaration on one side has to match an implementation on
// the other, where the compiler sees neither side, and where a mismatch produces SILENCE rather
// than an error: the box saves nothing, the packet never arrives, the handler never runs, the knob
// does nothing. They are the "we forgot something obvious again" bugs, and they have all shipped:
//
//   seam 3  inbound chain       a handler whose message has no parser mapped to a header id
//                               -> the client sends, PackageHandler logs "Incoming Unknown", done
//   seam 4  outbound composers  a composer built and sent with no serializer mapped
//                               -> PackageEncoder returns 0, SuperSocket writes nothing
//
//   node scripts/hooks/check-inert-declarations.mjs           compare, exit 2 on a NEW gap
//   node scripts/hooks/check-inert-declarations.mjs --update  accept the current gaps as baseline
//   node scripts/hooks/check-inert-declarations.mjs --all     print every gap, baselined or not
//
// Ratchet, not cleanup: the gaps that exist today are listed in inert-declarations-baseline.json
// with a reason each. A new one fails.
import { readFileSync, writeFileSync, existsSync, readdirSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, resolve, sep } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '../..');
const baselineFile =
  process.env.VORTEX_INERT_BASELINE ?? join(here, 'inert-declarations-baseline.json');
const update = process.argv.includes('--update');
const showAll = process.argv.includes('--all');

// ---- shared helpers ------------------------------------------------------------------------

function walk(rel, out = []) {
  const abs = join(root, rel);
  if (!existsSync(abs)) return out;
  for (const entry of readdirSync(abs, { withFileTypes: true })) {
    if (entry.name === 'obj' || entry.name === 'bin' || entry.name === 'node_modules') continue;
    const child = join(rel, entry.name);
    if (entry.isDirectory()) walk(child, out);
    else if (entry.name.endsWith('.cs')) out.push(child);
  }
  return out;
}

const read = (rel) => readFileSync(join(root, rel), 'utf8');
// Comments explain the bugs these checks look for, so they have to go before matching or a
// paragraph about a removed flag reads as the flag still being declared.
const stripComments = (src) => src.replace(/\/\/[^\n]*/g, '').replace(/\/\*[\s\S]*?\*\//g, '');

const projects = readdirSync(root).filter(
  (d) => d.startsWith('Vortex.') && statSync(join(root, d)).isDirectory()
);
const productionFiles = projects
  .filter((p) => !p.endsWith('Tests') && p !== 'Vortex.Tests.Support')
  .flatMap((p) => walk(p));

const gaps = [];
const gap = (seam, id, detail, where) => gaps.push({ seam, id, detail, where });

// ---- seam 3: a handler whose message never reaches the dispatcher ----------------------------
// contract -> parser -> header id -> handler. Miss the middle and the handler is unreachable code
// that compiles, tests and ships.

const incomingContracts = new Set();
for (const rel of walk('Vortex.Protocol')) {
  if (!rel.includes(`Messages${sep}Incoming`)) continue;
  for (const m of read(rel).matchAll(
    /(?:^|\n)\s*(?:public |internal )(?:sealed |abstract )?(?:class|record)\s+([A-Za-z0-9_]+)/g
  ))
    incomingContracts.add(m[1]);
}
const mappedParsers = new Set();
for (const rel of walk('Vortex.Revisions'))
  for (const m of read(rel).matchAll(/MapParser\(\s*MessageEvent\.[A-Za-z0-9_]+\s*,\s*new\s+([A-Za-z0-9_]+)/g))
    mappedParsers.add(m[1]);
const parsedTypes = new Set();
for (const rel of walk('Vortex.Revisions')) {
  if (!rel.includes('Parsers')) continue;
  const src = read(rel);
  const classNames = [
    ...src.matchAll(/(?:^|\n)\s*(?:public |internal )?(?:sealed )?class\s+([A-Za-z0-9_]+)/g),
  ].map((m) => m[1]);
  if (!classNames.some((c) => mappedParsers.has(c))) continue;
  for (const m of [
    ...src.matchAll(/(?:return|=>)\s*new\s+([A-Za-z0-9_]+)/g),
    ...src.matchAll(/typeof\(\s*([A-Za-z0-9_]+Message)\s*\)/g),
  ])
    if (incomingContracts.has(m[1])) parsedTypes.add(m[1]);
}
const handlers = new Map();
for (const rel of walk('Vortex.PacketHandlers'))
  for (const m of read(rel).matchAll(/IMessageHandler<\s*([A-Za-z0-9_]+)\s*>/g))
    if (!handlers.has(m[1])) handlers.set(m[1], rel);
for (const [message, rel] of handlers)
  if (!parsedTypes.has(message))
    gap('inbound-chain', message, 'a handler exists but no mapped parser produces this message: it can never run', rel);
for (const message of parsedTypes)
  if (!handlers.has(message))
    gap('inbound-chain', message, 'parsed off the wire and handled by nobody', 'Vortex.Revisions');

// ---- seam 4: a composer sent with no serializer ----------------------------------------------
// PackageEncoder.Encode returns 0 and SuperSocket writes nothing. One warning, one counter, and a
// client that waits forever for a reply that was never bytes.

const outgoingContracts = new Set();
for (const rel of walk('Vortex.Protocol')) {
  if (!rel.includes(`Messages${sep}Outgoing`)) continue;
  // Only the types that are actually composers: Messages/Outgoing also holds the payload
  // sub-records nested inside them, which have no serializer of their own and need none.
  for (const m of read(rel).matchAll(
    /(?:^|\n)\s*(?:public |internal )(?:sealed |abstract )?(?:class|record)\s+([A-Za-z0-9_]+)[^{;]*:\s*[^{;]*\bIComposer\b/g
  ))
    outgoingContracts.add(m[1]);
}
const mappedSerializers = new Set();
for (const rel of walk('Vortex.Revisions'))
  for (const m of read(rel).matchAll(/MapSerializer\(\s*typeof\(\s*([A-Za-z0-9_]+)\s*\)/g))
    mappedSerializers.add(m[1]);
const builtComposers = new Map();
for (const rel of productionFiles) {
  if (rel.startsWith('Vortex.Revisions') || rel.startsWith('Vortex.Protocol')) continue;
  for (const m of stripComments(read(rel)).matchAll(/new\s+([A-Za-z0-9_]+)\s*[({]/g))
    if (outgoingContracts.has(m[1]) && !builtComposers.has(m[1])) builtComposers.set(m[1], rel);
}
for (const [composer, rel] of builtComposers)
  if (!mappedSerializers.has(composer))
    gap('outbound-composers', composer, 'built and sent with no serializer mapped: the packet is dropped at the encoder', rel);

// ---- report ----------------------------------------------------------------------------------

gaps.sort((a, b) => a.seam.localeCompare(b.seam) || a.id.localeCompare(b.id));
const fingerprint = (g) => `${g.seam}/${g.id}`;

if (update) {
  const notes = existsSync(baselineFile) ? (JSON.parse(readFileSync(baselineFile, 'utf8')).notes ?? {}) : {};
  writeFileSync(
    baselineFile,
    `${JSON.stringify({ known: gaps.map(fingerprint), notes }, null, 2)}\n`
  );
  console.error(`check-inert-declarations: baseline written (${gaps.length} known gap(s)).`);
  process.exit(0);
}

const baseline = existsSync(baselineFile)
  ? new Set(JSON.parse(readFileSync(baselineFile, 'utf8')).known)
  : new Set();
const added = gaps.filter((g) => !baseline.has(fingerprint(g)));
const fixed = [...baseline].filter((k) => !gaps.some((g) => fingerprint(g) === k));

const print = (list, title) => {
  if (!list.length) return;
  console.error(`\n${title}`);
  let seam = null;
  for (const g of list) {
    if (g.seam !== seam) {
      seam = g.seam;
      console.error(`\n  [${seam}]`);
    }
    console.error(`    ${g.id}`);
    console.error(`        ${g.detail}`);
    console.error(`        ${g.where}`);
  }
};

if (showAll) print(gaps, `All gaps (${gaps.length}), baselined or not:`);

if (fixed.length)
  console.error(
    `check-inert-declarations: ${fixed.length} baselined gap(s) are gone. Run --update to drop them: ${fixed.join(', ')}`
  );

if (!added.length) {
  console.error(
    `check-inert-declarations: OK (` +
      `${handlers.size} handlers, ${builtComposers.size} composers; ` +
      `${gaps.length} known gap(s) baselined).`
  );
  process.exit(0);
}

print(added, `check-inert-declarations: ${added.length} NEW silent gap(s).`);
console.error(
  '\nEach of these compiles, passes its tests and does nothing at runtime. Close the seam rather\n' +
    "than baselining it; --update is for gaps you have decided to live with, and each one wants a\n" +
    'line in the baseline notes saying why.'
);
process.exit(2);
