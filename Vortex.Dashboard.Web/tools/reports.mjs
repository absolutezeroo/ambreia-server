#!/usr/bin/env node
// Read what the in-page reporter collected.
//
//   npm run reports          -- the open ones, grouped by the file they land in
//   npm run reports -- all   -- including the ones already marked done
//   npm run reports -- done 3 5   -- mark reports 3 and 5 as handled
//   npm run reports -- clear -- start a fresh list
//
// Grouped by source file rather than listed in arrival order, because a defect is fixed where
// it lives: five reports that all resolve to MembershipsPage are one visit to one file, and
// reading them together is what shows whether they are five defects or one.
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const FILE = resolve(dirname(fileURLToPath(import.meta.url)), '..', 'ui-reports.jsonl');
const [command = 'open', ...rest] = process.argv.slice(2);

if (!existsSync(FILE)) {
  console.log('Aucun signalement. Lance `npm run dev`, puis Ctrl+Shift+B dans la page.');
  process.exit(0);
}

const lines = readFileSync(FILE, 'utf8').trim();
const reports = lines ? lines.split('\n').map((l, i) => ({ ...JSON.parse(l), n: i + 1 })) : [];

if (command === 'clear') {
  writeFileSync(FILE, '');
  console.log(`${reports.length} signalement(s) effacé(s).`);
  process.exit(0);
}

if (command === 'done') {
  const marks = new Set(rest.map(Number));
  const out = reports.map((r) => {
    if (marks.has(r.n)) r.done = true;

    const { n, ...rest2 } = r;

    return JSON.stringify(rest2);
  });

  writeFileSync(FILE, out.join('\n') + '\n');
  console.log(`Marqué(s) traité(s): ${[...marks].join(', ')}`);
  process.exit(0);
}

const shown = command === 'all' ? reports : reports.filter((r) => !r.done);

if (!shown.length) {
  console.log(`Rien d'ouvert (${reports.length} au total).`);
  process.exit(0);
}

// A report belongs to the page it was filed on. The chain's last entry is usually that page,
// but not always: every route renders through AppShell, so on a page whose own markup ends
// above the selection the chain terminates in the shell -- and grouping by it would file every
// report on the dashboard under one component. A pages/ entry wins when the chain has one.
const groups = new Map();

for (const r of shown) {
  const chains = r.elements?.map((e) => e.sources).filter((c) => c?.length) ?? [];
  const flat = chains.flat();
  const own = flat.filter((f) => !f.includes('AppShell'));
  // The route is the honest fallback: it is always recorded, and a chain that reaches only the
  // shell -- which happens when the selection sits above the page's own markup -- would
  // otherwise file every report on the dashboard under one component.
  const page = flat.find((f) => f.includes('/pages/')) ?? own[own.length - 1] ?? null;
  const key = page ? page.split(':')[0] : `route ${r.route}`;

  if (!groups.has(key)) groups.set(key, []);

  groups.get(key).push(r);
}

for (const [file, group] of [...groups].sort((a, b) => b[1].length - a[1].length)) {
  console.log(`\n\x1b[1m${file}\x1b[0m  (${group.length})`);

  for (const r of group) {
    const lines2 = new Set(
      (r.elements ?? []).flatMap((e) => e.sources ?? []).filter((s) => s.startsWith(file)),
    );

    console.log(`  \x1b[33m#${r.n}\x1b[0m ${r.note || '(sans note)'}`);
    console.log(`      ${r.route}  ${r.theme}  ${r.viewport}${r.done ? '  [traité]' : ''}`);

    if (lines2.size) console.log(`      lignes: ${[...lines2].join(', ')}`);

    for (const el of (r.elements ?? []).slice(0, 4)) {
      const cls = el.classes?.length ? '.' + el.classes.join('.') : '';

      console.log(`      ${el.tag}${cls}  ${el.size}${el.text ? `  "${el.text}"` : ''}`);

      if (el.parent) {
        const p = el.parent;

        console.log(
          `        dans .${p.of}: ${p.display}` +
            (p.flexDirection ? ` ${p.flexDirection}` : '') +
            (p.gridTemplateColumns ? ` [${p.gridTemplateColumns}]` : ''),
        );
      }
    }
  }
}

console.log(`\n${shown.length} ouvert(s) sur ${reports.length}.`);
