#!/usr/bin/env node
// LOGIC_GROUPS is the dropdown an operator picks a furniture behaviour from, and it is the one list
// in the dashboard with no enum behind it: `furniture_definitions.logic` is a free string matched at
// runtime against every [RoomObjectLogic("...")]-attributed class found by assembly scanning. A key
// that is not in this list cannot be picked; a key in the list that no class claims resolves to
// nothing and silently falls back to default_floor.
//
// It was hand-maintained, and drifted to 109 of 236 keys -- every game built since and the whole furniture_* family were unpickable.
//
//   node scripts/hooks/check-logic-groups.mjs           compare, exit 2 on drift
//   node scripts/hooks/check-logic-groups.mjs --write   regenerate the block
import { readFileSync, writeFileSync, readdirSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, resolve } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '../..');
// .ts since the front end became TypeScript. Read as text, not imported: --write regenerates the
// LOGIC_GROUPS block in place, which needs the source rather than the evaluated value.
const enumsPath = join(root, 'Vortex.Dashboard.Web/src/lib/furnitureEnums.ts');

// Groups are derived from the key prefix, which is how the server names them in the first place --
// no second list to keep in step. Order is the order an operator thinks in: the plain furniture
// behaviours first, the games after.
const GROUPS = [
  ['Basic', (k) => !/^(furniture_|pet_|monsterplant_|game_)/.test(k)],
  ['Furniture', (k) => k.startsWith('furniture_')],
  ['Pets', (k) => k.startsWith('pet_') || k.startsWith('monsterplant_')],
  ['Games: other', (k) => k.startsWith('game_')],
];

// Hand-written descriptions worth keeping: the plain behaviours are not self-describing. Anything absent here shows its raw key, which is what every other
// catalogue tool calls it by anyway.
const LABELS = {
  default_floor: 'Default (floor item)',
  default_wall: 'Default (wall item)',
  default_avatar: 'Default (avatar)',
  gate: 'Gate (blocks/unblocks a tile)',
  roller: 'Roller (conveyor belt)',
  dice: 'Dice',
  fireworks: 'Fireworks',
  room_invisible_click_tile: 'Invisible click tile',
  wheel_of_fortune: 'Wheel of fortune',
  monsterplant_seed: 'Monsterplant seed',
  pet_drink: 'Pet drink bowl',
  pet_nest: 'Pet nest',
  pet_food: 'Pet food bowl',
};

function walk(dir, out = []) {
  for (const entry of readdirSync(dir)) {
    if (entry === 'bin' || entry === 'obj' || entry === 'node_modules' || entry === '.git') continue;

    const full = join(dir, entry);
    if (statSync(full).isDirectory()) walk(full, out);
    else if (entry.endsWith('.cs')) out.push(full);
  }
  return out;
}

const keys = new Set();

for (const file of walk(root)) {
  // Comment lines are dropped first. Prose that quotes the attribute — `[RoomObjectLogic("wf_...")]`
  // in a doc comment explaining the convention — reads like a registration to a plain regex, and the
  // placeholder inside it then has to be carried in the operator's dropdown forever.
  const source = readFileSync(file, 'utf8').replace(/^\s*(\/\/|\*|\/\*).*$/gm, '');

  for (const m of source.matchAll(/RoomObjectLogic\("([^"]+)"\)/g)) {
    keys.add(m[1]);
  }
}

const sorted = [...keys].sort();
const assigned = new Set();
const blocks = [];

for (const [label, test] of GROUPS) {
  const members = sorted.filter((k) => !assigned.has(k) && test(k));
  if (!members.length) continue;

  members.forEach((k) => assigned.add(k));
  const options = members
    .map((k) => `      { value: '${k}', label: '${LABELS[k] ? `${k} - ${LABELS[k]}` : k}' },`)
    .join('\n');
  blocks.push(`  {\n    label: '${label}',\n    options: [\n${options}\n    ],\n  },`);
}

const generated = `export const LOGIC_GROUPS = [\n${blocks.join('\n')}\n];`;

const source = readFileSync(enumsPath, 'utf8');
const eol = source.includes('\r\n') ? '\r\n' : '\n';
const flat = source.replace(/\r\n/g, '\n');
const start = flat.indexOf('export const LOGIC_GROUPS');
const end = flat.indexOf('\n];', start);

if (start === -1 || end === -1) {
  console.error('check-logic-groups: LOGIC_GROUPS introuvable dans furnitureEnums.js');
  process.exit(2);
}

const current = flat.slice(start, end + 3);

if (current === generated) {
  console.log(`check-logic-groups: OK (${sorted.length} cles, ${blocks.length} groupes).`);
  process.exit(0);
}

if (process.argv.includes('--write')) {
  const next = flat.slice(0, start) + generated + flat.slice(end + 3);
  writeFileSync(enumsPath, eol === '\r\n' ? next.replace(/\n/g, '\r\n') : next);
  console.log(`check-logic-groups: regenere (${sorted.length} cles, ${blocks.length} groupes).`);
  process.exit(0);
}

const currentKeys = new Set([...current.matchAll(/'([a-z0-9_]+)'/g)].map((m) => m[1]));
const missing = sorted.filter((k) => !currentKeys.has(k));
const ghosts = [...currentKeys].filter((k) => !keys.has(k) && k.includes('_'));

if (missing.length) {
  console.error(`check-logic-groups: ${missing.length} cle(s) du code absente(s) du menu, dont : ${missing.slice(0, 8).join(', ')}`);
}
if (ghosts.length) {
  console.error(`check-logic-groups: ${ghosts.length} cle(s) du menu que plus aucune classe ne declare : ${ghosts.slice(0, 8).join(', ')}`);
}

console.error('\nLancez `node scripts/hooks/check-logic-groups.mjs --write` pour regenerer.');
process.exit(2);
