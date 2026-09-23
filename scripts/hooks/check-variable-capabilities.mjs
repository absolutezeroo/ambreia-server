#!/usr/bin/env node
// A wired variable's flags are a promise made to the client, and the client believes it: every
// variable picker in the wired editor filters on one flag and greys out whatever fails it. So a
// variable that declares a capability it cannot honour is offered to the builder, selected, saved --
// and then does nothing, in complete silence. The build passes, the tests pass, a grep finds the
// class. It only looks alive.
//
// Two things this does NOT catch, stated here so a green run is not read as more than it is:
//
//   - a box whose flags are copied from another variable at runtime. The echo box republishes the
//     source's flags unmasked, so nothing about its promises is written down anywhere to compare.
//     Its own silence was a real bug and this check would have slept through it.
//   - a flag that is true and a store that is missing. The room variable that could never be
//     written, the context variable that threw on every access, the shared availability with nowhere
//     to write: every one of those declared exactly the right flags. The lie was further down.
//
// So this is the cross-check for one shape only -- a capability named and not implemented behind it:
//
//   CanInterceptChanges  the "variable changed" trigger listens for a change event. Only a
//                        FurnitureWiredVariableLogic raises one, and only an IWiredDerivedVariable or
//                        an IWiredPolledVariable (a value that moves with time, asked every wired
//                        tick) has the room raise one on its behalf. Anything else is a trigger that
//                        can be configured and can never fire.
//   CanWriteValue        the "change variable value" effect filters on this. The internal bases
//                        refuse every write unless the leaf overrides a hook, so a leaf that
//                        declares it without one is a row the builder can pick and watch do nothing.
//   CanCreateAndDelete   the "give" and "remove" effects filter on this, and the internal bases
//                        refuse both outright.
//
//   node scripts/hooks/check-variable-capabilities.mjs   exit 2 on a broken promise
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, relative, resolve } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '../..');
const scanRoot = join(root, 'Vortex.Rooms');

// A type is a wired variable if its ancestry reaches one of these. Named rather than inferred so a
// new unrelated class mentioning the flags enum -- the shared-variable index does -- is not audited
// as a variable it is not.
const VARIABLE_ROOTS = new Set([
  'IWiredVariable',
  'FurnitureWiredVariableLogic',
  'WiredInternalVariable',
  'IWiredInternalVariable',
]);

// The base whose own write and create implementations land in a store. A leaf under it needs no
// override of its own.
const HONOURS_EVERYTHING = 'FurnitureWiredVariableLogic';

// Bases whose implementations of these members refuse or delegate. An override found on one of them
// is not evidence that a leaf below it can do anything.
const REFUSING_BASES = new Set(['WiredInternalVariable', 'UserVariable', 'FurnitureVariable']);

const WRITE_HOOKS = ['SetValueAsync', 'SetValueForAvatarAsync'];
const CREATE_HOOKS = ['GiveValueAsync'];

// Known and accepted, with the reason. An entry here is news when it appears, not a queue -- the
// same ratchet the header and wire-conflict checks use.
const BASELINE = new Set([]);

function walk(dir, out = []) {
  for (const entry of readdirSync(dir)) {
    if (entry === 'obj' || entry === 'bin') continue;

    const full = join(dir, entry);

    if (statSync(full).isDirectory()) walk(full, out);
    else if (entry.endsWith('.cs')) out.push(full);
  }

  return out;
}

/**
 * The bare names in a base list: commas split at the top level only, and each entry keeps its
 * identifier alone.
 * </summary>
 * A base written with a primary constructor reads `UserPlayerVariable(roomGrain)`, and carrying the
 * arguments into the name is how this check first came back green while auditing three variables out
 * of forty: every chain stopped at the first base that passed an argument along.
 */
function baseNames(list) {
  const names = [];
  let depth = 0;
  let current = '';

  for (const c of list) {
    if (c === '(' || c === '<' || c === '[') depth++;
    else if (c === ')' || c === '>' || c === ']') depth--;

    if (c === ',' && depth === 0) {
      names.push(current);
      current = '';
    } else {
      current += c;
    }
  }

  names.push(current);

  return names
    .map((n) => n.replace(/[(<[][\s\S]*$/, '').trim())
    .filter(Boolean);
}

/** The declaration head of every class in a file: its name, and what it is written after the colon. */
function declarations(source) {
  const found = [];
  const re = /\bclass\s+(\w+)/g;
  let match;

  while ((match = re.exec(source)) !== null) {
    // Forward to the body, past a generic list and a primary constructor, tracking nesting so a
    // comma or colon inside either is not mistaken for the base list.
    let depth = 0;
    let colon = -1;
    let i = re.lastIndex;

    for (; i < source.length; i++) {
      const c = source[i];

      if (c === '<' || c === '(' || c === '[') depth++;
      else if (c === '>' || c === ')' || c === ']') depth--;
      else if (depth === 0 && c === ':' && colon === -1) colon = i;
      else if (depth === 0 && c === '{') break;
      else if (depth === 0 && c === ';') break; // a forward declaration, not a body
    }

    found.push({
      name: match[1],
      bases: colon === -1 ? [] : baseNames(source.slice(colon + 1, i)),
      bodyStart: i,
    });
  }

  return found;
}

const types = new Map();

// Every class, not only the ones naming a flag: the chain from a leaf to IWiredVariable runs through
// bases that declare none, and skipping them left the ancestry unresolvable and the audit blind --
// it reported three variables out of forty and called itself green.
for (const file of walk(scanRoot)) {
  const source = readFileSync(file, 'utf8');

  for (const decl of declarations(source)) {
    types.set(decl.name, {
      ...decl,
      file: relative(root, file).replace(/\\/g, '/'),
      source,
    });
  }
}

/** Every ancestor name reachable from a type, itself included. */
function ancestry(name, seen = new Set()) {
  if (seen.has(name)) return seen;

  seen.add(name);

  for (const base of types.get(name)?.bases ?? []) ancestry(base, seen);

  return seen;
}

/** Source with comments removed, so a flag named in a remark is not read as a flag declared. */
function stripComments(text) {
  return text.replace(/\/\*[\s\S]*?\*\//g, ' ').replace(/\/\/[^\n]*/g, ' ');
}

/**
 * Whether this type, or a repo ancestor that is not one of the refusing bases, actually implements a
 * hook. Not "overrides": a variable that implements IWiredVariable directly carries no override
 * keyword, and the level-up readings are exactly that shape.
 */
function implementsHook(name, hooks) {
  for (const ancestor of ancestry(name)) {
    if (ancestor !== name && REFUSING_BASES.has(ancestor)) continue;

    const type = types.get(ancestor);

    if (!type) continue;

    const body = stripComments(type.source.slice(type.bodyStart));

    for (const hook of hooks) {
      // A member head from its accessibility to the parameter list, ending in a body or an
      // expression body. An abstract declaration promises nothing and is skipped.
      const re = new RegExp(
        `\\b(?:public|protected|internal|private)\\b([^;{}()]*)\\b${hook}\\s*\\([^)]*\\)\\s*(?:=>|\\{)`,
        'g',
      );

      for (const match of body.matchAll(re)) {
        if (!/\babstract\b/.test(match[1])) return true;
      }
    }
  }

  return false;
}

const problems = [];

for (const [name, type] of types) {
  const line = type.bases.join(' ');
  const family = ancestry(name);

  if (![...family].some((a) => VARIABLE_ROOTS.has(a))) continue;

  // Only the flags this very type names. A leaf inherits its base's promise along with its base's
  // implementation, so auditing inherited ones would report the base twice.
  const body = stripComments(type.source.slice(type.bodyStart));
  const declared = new Set([...body.matchAll(/WiredVariableFlags\.(\w+)/g)].map((m) => m[1]));

  const derived = ['IWiredDerivedVariable', 'IWiredPolledVariable'].some(
    (announcer) => line.includes(announcer) || body.includes(announcer)
  );
  const honoursEverything = family.has(HONOURS_EVERYTHING);

  const checks = [
    [
      'CanInterceptChanges',
      honoursEverything || derived,
      'nothing raises a change event under its own id, so the "variable changed" trigger can never fire',
    ],
    [
      'CanWriteValue',
      honoursEverything || implementsHook(name, WRITE_HOOKS),
      `no ${WRITE_HOOKS.join(' / ')} behind it, so the change-value effect silently does nothing`,
    ],
    [
      'CanCreateAndDelete',
      honoursEverything || implementsHook(name, CREATE_HOOKS),
      `no ${CREATE_HOOKS.join(' / ')} behind it, so the give and remove effects silently do nothing`,
    ],
  ];

  for (const [flag, honoured, why] of checks) {
    if (!declared.has(flag) || honoured || BASELINE.has(`${name}:${flag}`)) continue;

    problems.push({ name, flag, why, file: type.file });
  }
}

const audited = [...types.keys()].filter((n) => [...ancestry(n)].some((a) => VARIABLE_ROOTS.has(a)));

if (problems.length === 0) {
  console.log(
    `check-variable-capabilities: OK (${audited.length} variables, ${BASELINE.size} baselined).`,
  );
  process.exit(0);
}

console.error(
  `check-variable-capabilities: ${problems.length} variable(s) promise a capability nothing honours.\n`,
);

for (const p of problems) {
  console.error(`  ${p.name} declares ${p.flag}`);
  console.error(`    ${p.why}`);
  console.error(`    ${p.file}`);
}

console.error(
  '\nEither implement it, or drop the flag. Dropping is often the right answer: @type declared\n' +
    'CanWriteValue with no write behind it, and nothing turns a player into a bot, so the flag was\n' +
    'the thing that was wrong.',
);

process.exit(2);
