#!/usr/bin/env node
// What the dashboard can actually do with each thing the hotel stores.
//
// Every DbSet on VortexDbContext is placed in one of three buckets:
//
//   manage  -- named under Vortex.Dashboard.API/Admin or /Operations: something mutates it
//   read    -- named only under Vortex.Dashboard.API/Api: an operator can look, not act
//   absent  -- named nowhere in the dashboard at all
//
// This is a grep, not a call graph, and it is wrong in both directions on purpose-built edges:
//
//   * a false NEGATIVE happens when a surface reaches the data through a service outside the
//     dashboard. ErrorGroups/ErrorOccurrences are the known one -- the Incidents page shows them
//     through IncidentDetectionService, which lives in Vortex.Observability. They are listed in
//     KNOWN_INDIRECT below so the count does not lie about them.
//   * a false POSITIVE happens when a name is merely mentioned. "manage" is therefore a ceiling,
//     not a guarantee that every verb exists for that entity.
//
// Read it as a map of where the blind spots are, and confirm any single row by opening the code.
//
//   node scripts/dashboard-admin-coverage.mjs
//   node scripts/dashboard-admin-coverage.mjs --list   # every entity, not just the totals
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const dashboard = path.join(root, 'Vortex.Dashboard.API');
const list = process.argv.includes('--list');

/** Surfaced by a service that does not live in the dashboard, so the grep cannot see it. */
const KNOWN_INDIRECT = new Set(['ErrorGroups', 'ErrorOccurrences']);

/**
 * Per-player interface state. Nobody administers a player's navigator window placement, and
 * counting these as gaps would pad the number with rows nobody wants.
 */
const UI_STATE = new Set([
  'GroupForumReadMarkers',
  'MessengerCategories',
  'PlayerAccountPreferences',
  'PlayerModToolPreferences',
  'PlayerNavigatorPreferences',
  'PlayerWiredPreferences',
]);

function sources(dir, out = []) {
  if (!fs.existsSync(dir)) return out;

  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === 'obj' || entry.name === 'bin') continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) sources(full, out);
    else if (entry.name.endsWith('.cs')) out.push(full);
  }

  return out;
}

const read = (files) => files.map((f) => fs.readFileSync(f, 'utf8')).join('\n');

const context = read(
  sources(path.join(root, 'Vortex.Database', 'Context')).filter((f) =>
    path.basename(f).startsWith('VortexDbContext')
  )
);

const entities = [
  ...new Set([...context.matchAll(/public DbSet<\w+>\s+(\w+)/g)].map((m) => m[1])),
].sort();

const reads = read(sources(path.join(dashboard, 'Api')));
const writes = read([
  ...sources(path.join(dashboard, 'Admin')),
  ...sources(path.join(dashboard, 'Operations')),
]);

const mentions = (haystack, name) => new RegExp(`(?<![\\w])${name}(?![\\w])`).test(haystack);

const buckets = { manage: [], read: [], absent: [] };

for (const name of entities) {
  if (mentions(writes, name)) buckets.manage.push(name);
  else if (mentions(reads, name) || KNOWN_INDIRECT.has(name)) buckets.read.push(name);
  else buckets.absent.push(name);
}

const gaps = buckets.absent.filter((n) => !UI_STATE.has(n));

console.log(`dashboard-admin-coverage: ${entities.length} entities`);
console.log(`  manage  ${String(buckets.manage.length).padStart(3)}  read and act`);
console.log(`  read    ${String(buckets.read.length).padStart(3)}  visible, no action`);
console.log(
  `  absent  ${String(buckets.absent.length).padStart(3)}  not surfaced ` +
    `(${gaps.length} of them worth a decision, ${buckets.absent.length - gaps.length} per-player UI state)`
);

if (list) {
  for (const [key, label] of [
    ['manage', 'MANAGED'],
    ['read', 'READ-ONLY'],
    ['absent', 'ABSENT'],
  ]) {
    console.log(`\n${label} (${buckets[key].length})`);
    for (const name of buckets[key]) {
      const note = UI_STATE.has(name) ? '  [ui state]' : KNOWN_INDIRECT.has(name) ? '  [indirect]' : '';
      console.log(`  ${name}${note}`);
    }
  }
}
