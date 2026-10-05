#!/usr/bin/env node
// The box that is not like its brothers.
//
// Every check in this repository so far needs an authority to compare against: the client's form,
// the admin dropdown, the config catalog, the message registry. That is why check-wired-param-counts
// needs a client checkout.
//
// This one needs nothing outside the repository, because it uses the code as its own authority.
// When twenty-five conditions all override GetIntParamRules and one does not, the one is worth a
// look -- not because overriding is mandatory, but because a family of twenty-five that agrees on
// something is a rule somebody wrote down in twenty-five places without naming it.
//
// It found wf_slc_remote (no SelectAsync among eighteen selectors that have one) and
// wf_trg_user_performs_action (no CanTriggerAsync among twenty-four triggers) with no client at
// all -- two of the five boxes the param-count check needed the client to find.
//
// It is a SUGGESTION engine, not a verdict: a legitimate outlier exists, and this repository has
// several. wf_cnd_match_snapshot_new overrides no Evaluate because the furni-snapshot subsystem it
// needs does not exist yet, and the class says so. That is why every entry carries a note in the
// baseline rather than being silently accepted.
//
//   node scripts/hooks/check-sibling-divergence.mjs           exit 2 on a NEW outlier
//   node scripts/hooks/check-sibling-divergence.mjs --all     print every family
//   node scripts/hooks/check-sibling-divergence.mjs --update  accept the current outliers
import { readFileSync, writeFileSync, existsSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, resolve } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '../..');
const showAll = process.argv.includes('--all');
const update = process.argv.includes('--update');
const baselineFile =
  process.env.VORTEX_SIBLING_BASELINE ?? join(here, 'sibling-divergence-baseline.json');

// How much of a family must agree before a missing override counts as divergence. Four fifths: low
// enough to catch the one box in twenty-five, high enough that a family genuinely split down the
// middle says nothing.
const AGREEMENT = 0.8;
const MIN_FAMILY = 5;

const strip = (s) => s.replace(/\/\/[^\n]*/g, '').replace(/\/\*[\s\S]*?\*\//g, '');

function walk(dir, out = []) {
  if (!existsSync(dir)) return out;
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    if (['obj', 'bin'].includes(e.name)) continue;
    const p = join(dir, e.name);
    if (e.isDirectory()) walk(p, out);
    else if (e.name.endsWith('.cs')) out.push(p);
  }
  return out;
}

const WIRED = join(root, 'Vortex.Rooms/Object/Logic/Furniture/Floor/Wired');

const boxes = [];
for (const file of walk(WIRED)) {
  const src = strip(readFileSync(file, 'utf8'));
  const decl = /class\s+([A-Za-z0-9_]+)\s*\([^)]*\)\s*:\s*([A-Za-z0-9_]+)/s.exec(src);
  const key = (/\[RoomObjectLogic\("([^"]+)"\)\]/.exec(src) || [])[1];
  if (!decl || !key) continue;
  boxes.push({
    name: decl[1],
    base: decl[2],
    key,
    file: file.slice(root.length + 1),
    // Methods AND properties. Reading only methods was wrong: FurnitureWiredTriggerLogic's family
    // declares SupportedEventTypes as an overridden property with an initializer, so every trigger
    // that used that form read as "missing" it, and the `typeof` from the expression-bodied ones
    // read as a method they all shared. That produced one clean false positive
    // (wf_trg_transaction_complete) and polluted a real one.
    overrides: new Set([
      ...[...src.matchAll(/public\s+override\s[^\n(]*?\b([A-Za-z0-9_]+)\s*\(/g)].map((m) => m[1]),
      ...[...src.matchAll(/public\s+override\s+[^\n=;]*?\b([A-Za-z0-9_]+)\s*(?:\{\s*get|=>\s*[^(])/g)].map(
        (m) => m[1]
      ),
    ].filter((n) => n !== 'typeof')),
  });
}

const families = new Map();
for (const b of boxes) {
  if (!families.has(b.base)) families.set(b.base, []);
  families.get(b.base).push(b);
}

const outliers = [];
const report = [];
for (const [base, members] of [...families].sort()) {
  if (members.length < MIN_FAMILY) continue;
  const counts = new Map();
  for (const m of members) for (const o of m.overrides) counts.set(o, (counts.get(o) ?? 0) + 1);
  const agreed = [...counts].filter(([, c]) => c >= members.length * AGREEMENT).map(([o]) => o);
  report.push({ base, size: members.length, agreed });
  for (const m of members) {
    const missing = agreed.filter((o) => !m.overrides.has(o)).sort();
    if (missing.length) outliers.push({ key: m.key, base, size: members.length, missing, file: m.file });
  }
}

outliers.sort((a, b) => a.key.localeCompare(b.key));
const fingerprint = (o) => `${o.key}:${o.missing.join(',')}`;

if (showAll) {
  for (const r of report) {
    console.error(`  ${r.base} (${r.size} membres) s'accordent sur : ${r.agreed.sort().join(', ') || '—'}`);
  }
  console.error('');
}

if (update) {
  const notes = existsSync(baselineFile) ? (JSON.parse(readFileSync(baselineFile, 'utf8')).notes ?? {}) : {};
  writeFileSync(baselineFile, `${JSON.stringify({ known: outliers.map(fingerprint), notes }, null, 2)}\n`);
  console.error(`check-sibling-divergence: baseline written (${outliers.length} outlier(s)).`);
  process.exit(0);
}

const baseline = existsSync(baselineFile)
  ? new Set(JSON.parse(readFileSync(baselineFile, 'utf8')).known)
  : new Set();
const added = outliers.filter((o) => !baseline.has(fingerprint(o)));
const gone = [...baseline].filter((k) => !outliers.some((o) => fingerprint(o) === k));

if (gone.length) {
  console.error(
    `check-sibling-divergence: ${gone.length} baselined outlier(s) are gone. Run --update to drop them: ${gone.join(', ')}`
  );
}

if (added.length) {
  console.error(
    `check-sibling-divergence: ${added.length} box(es) differ from their family.\n` +
      'Not necessarily wrong -- but the family agreed, and this one did not.\n'
  );
  for (const o of added) {
    console.error(`  ${o.key.padEnd(32)} ne surcharge pas ${o.missing.join(', ')}`);
    console.error(`        ${o.file}`);
    console.error(`        (fratrie ${o.base}, ${o.size} membres)`);
  }
  console.error(
    '\nImplement it, or record WHY it differs in the baseline notes. A legitimate outlier with a\n' +
      'written reason is worth more than a silent one.'
  );
  process.exit(2);
}

console.error(
  `check-sibling-divergence: OK (${report.length} familles, ${boxes.length} boîtes, ` +
    `${outliers.length} écart(s) connu(s) baselinés).`
);
process.exit(0);
