#!/usr/bin/env node
// The seam that cost the most and that nothing could see.
//
// A wired box's dialog collects N integers and sends exactly N. TryNormalizeIntParams, with no tail
// rule, refuses an update whose count is not exactly GetIntParamRules().Count -- not the parameter,
// the WHOLE update -- and the refusal is silent: the handler returns without sending anything, not
// even WiredSaveSuccessEvent, and what the official server answers to a rejected wired save is
// unknown. The box saves, the
// dialog closes, nothing is stored.
//
// It has shipped twice: 2ccb45b (two Variable FX displays declared 21 rules against a client that
// sends 22) and 7c2ad33. add-a-wired-box.md warns about it in bold, in a checklist, and both bugs
// landed the day after. Prose does not close this seam; the client does.
//
// The authority is the client's own configuration class per box. In vortex-modern-client that is
// packages/vortex-engine/src/habbo/roomevents/wired_setup/<family>/**/<Box>.ts, whose
// readIntParamsFromForm() returns the array that goes on the wire. That tree is COMMITTED -- unlike
// sources/, the AS3 dump, which is gitignored there. So this check works wherever the client is checked out,
// CI included.
//
//   node scripts/hooks/check-wired-param-counts.mjs            compare, exit 2 on a NEW disagreement
//   node scripts/hooks/check-wired-param-counts.mjs --all       print every box, agreeing or not
//   node scripts/hooks/check-wired-param-counts.mjs --skipped   print what could not be compared
//   node scripts/hooks/check-wired-param-counts.mjs --update    rewrite the baseline
//   VORTEX_CLIENT_ROOT=/path/to/vortex-modern-client node …     point it at the client explicitly
//
// Ratchet, not cleanup: the disagreements that exist today are listed in
// wired-param-counts-baseline.json so a NEW one blocks. They are OPEN BUGS, not accepted debt --
// each one is a box a player cannot configure -- and the baseline notes say so per entry. A count
// that changes on either side makes the entry new again, which is the point: baselining 21-vs-22
// must not also cover 22-vs-23.
//
// Without the client beside the repository it says so and exits 0, rather than reporting a clean
// run it did not perform.
import { readFileSync, writeFileSync, existsSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, relative, resolve, sep } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '../..');
const showAll = process.argv.includes('--all');
const showSkipped = process.argv.includes('--skipped');
const update = process.argv.includes('--update');
const baselineFile =
  process.env.VORTEX_WIRED_PARAM_BASELINE ?? join(here, 'wired-param-counts-baseline.json');

// ---- locate the client -----------------------------------------------------------------------

const WIRED_SETUP = 'packages/vortex-engine/src/habbo/roomevents/wired_setup';

function findClient() {
  const candidates = [];
  if (process.env.VORTEX_CLIENT_ROOT) candidates.push(process.env.VORTEX_CLIENT_ROOT);
  const parent = dirname(root);
  candidates.push(join(parent, 'vortex-modern-client'));
  // The session layout add_repo produces: <parent>/<owner>/<repo>.
  for (const owner of existsSync(parent) ? readdirSync(parent) : []) {
    const nested = join(parent, owner, 'vortex-modern-client');
    if (existsSync(nested)) candidates.push(nested);
  }
  return candidates.find((c) => existsSync(join(c, WIRED_SETUP))) ?? null;
}

const client = findClient();
if (!client) {
  console.error(
    'check-wired-param-counts: no vortex-modern-client checkout beside this repository ' +
      `(looked for <sibling>/${WIRED_SETUP}); skipping rather than reporting a pass.`
  );
  process.exit(0);
}

// ---- the six families, client side and ours ---------------------------------------------------

const FAMILIES = [
  { dir: 'actiontypes', enumName: 'WiredActionType', label: 'action' },
  { dir: 'triggerconfs', enumName: 'WiredTriggerType', label: 'trigger' },
  { dir: 'conditions', enumName: 'WiredConditionType', label: 'condition' },
  { dir: 'selectors', enumName: 'WiredSelectorType', label: 'selector' },
  { dir: 'addons', enumName: 'WiredAddonType', label: 'addon' },
  { dir: 'variables', enumName: 'WiredVariableBoxType', label: 'variable' },
];
const FAMILY_BY_DIR = Object.fromEntries(FAMILIES.map((f) => [f.dir, f.label]));

const stripComments = (src) => src.replace(/\/\/[^\n]*/g, '').replace(/\/\*[\s\S]*?\*\//g, '');

// Top-level elements of a bracketed list starting at `open`.
//
// Counting separators is the obvious way to do this and it is wrong: both C# collection expressions
// and TS array literals accept a trailing comma, so `[a, b, c,]` has three elements and three
// top-level commas. A check written to count parameters that is itself off by one reported thirty
// phantom disagreements the first time it ran. Count segments that hold something instead.
//
// `spread` reports a `...x` (TS) or `..x` (C#) element: the list continues somewhere else and the
// count on its own means nothing.
function bracketElements(text, open) {
  let depth = 0;
  let elements = 0;
  let segment = false;
  let spread = false;
  for (let i = open; i < text.length; i++) {
    const c = text[i];
    if ('([{'.includes(c)) {
      if (depth === 1) segment = true;
      depth++;
    } else if (')]}'.includes(c)) {
      if (--depth === 0) break;
    } else if (depth === 1) {
      if (c === ',') {
        if (segment) elements++;
        segment = false;
      } else if (!/\s/.test(c)) {
        if (!segment && c === '.' && text.startsWith('..', i)) spread = true;
        segment = true;
      }
    }
  }
  return { count: segment ? elements + 1 : elements, spread };
}

// ---- index the client tree ---------------------------------------------------------------------

function walkTs(dir, out = []) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const child = join(dir, entry.name);
    if (entry.isDirectory()) walkTs(child, out);
    else if (entry.name.endsWith('.ts')) out.push(child);
  }
  return out;
}

const clientClasses = new Map(); // class name -> { src, base, family, rel }
const codeConsts = new Map(); // "AddonCodes.MOVE_PHYSICS" -> 7

for (const file of walkTs(join(client, WIRED_SETUP))) {
  const rel = relative(join(client, WIRED_SETUP), file);
  // A file at the root of wired_setup/ belongs to no family, but DefaultElement lives there and is
  // the base of every box: skipping it broke chain resolution, so a box that inherits its
  // `readIntParamsFromForm` (which returns []) was reported as "variable" instead of as zero.
  const family = FAMILY_BY_DIR[rel.split(sep)[0]] ?? null;
  const src = stripComments(readFileSync(file, 'utf8'));
  const decl =
    /export\s+(?:abstract\s+)?class\s+([A-Za-z0-9_]+)(?:<[^>]*>)?(?:\s+extends\s+([A-Za-z0-9_]+))?/.exec(
      src
    );
  if (!decl) continue;
  const [, name, base] = decl;
  clientClasses.set(name, { src, base: base ?? null, family, rel: rel.split(sep).join('/') });
  for (const m of src.matchAll(/static\s+readonly\s+([A-Za-z0-9_]+)\s*:\s*number\s*=\s*(-?\d+)/g)) {
    codeConsts.set(`${name}.${m[1]}`, +m[2]);
  }
}

// The body of a TS method, from its signature to the matching brace. The client formats the opening
// brace Allman-style, on its own line, so a signature pattern that stops at the end of the line
// finds nothing at all.
function tsMethodBody(src, name) {
  const m = new RegExp(`\\b${name}\\s*\\(([^)]*)\\)\\s*:[^{;]*\\{`).exec(src);
  if (!m) return null;
  const open = src.indexOf('{', m.index + m[0].length - 1);
  let depth = 0;
  for (let i = open; i < src.length; i++) {
    if (src[i] === '{') depth++;
    else if (src[i] === '}' && --depth === 0)
      return { body: src.slice(open, i + 1), params: m[1] };
  }
  return null;
}

// Virtual dispatch: find the most derived definition of `method`, starting at `className`.
function resolveTsMethod(className, method) {
  for (let name = className; name; ) {
    const cls = clientClasses.get(name);
    if (!cls) return null;
    const found = tsMethodBody(cls.src, method);
    if (found) return { owner: name, ...found };
    name = cls.base;
  }
  return null;
}

// How many ints a body appends to `arr`, following `super.` and `this.<helper>(arr, …)` calls.
//
// Only unconditional, top-level appends are counted. A push nested in a block, a loop, a spread or a
// concat makes the count depend on what the operator selected, and this check says "unknown" rather
// than guessing -- a wrong count here would be indistinguishable from the bug it looks for.
//
// The one deliberate exception is a guard around the whole delegation (`if(state !== null)
// this.writeIntParams(params, state);` in VariableFxAddonBase): the guarded-out branch sends an
// empty array, which is the "nothing configured yet" state, not a second legitimate arity.
function countAppends(startClass, method, depthGuard = 0) {
  if (depthGuard > 8) return null;
  const found = resolveTsMethod(startClass, method);
  if (!found) return null;
  const arr = (found.params.split(',')[0] ?? '').trim().split(':')[0].trim();
  if (!arr) return null;
  return scanAppends(found, startClass, arr, depthGuard);
}

function scanAppends(found, startClass, arr, depthGuard) {
  const { body, owner } = found;
  if (/\bfor\s*\(|\bwhile\s*\(|\.forEach\s*\(|\.map\s*\(|\.concat\s*\(|\.\.\./.test(body)) {
    return null;
  }
  let depth = 0;
  let count = 0;
  let unreadable = false;
  let stmt = 0;
  for (let i = 0; i < body.length; i++) {
    const c = body[i];
    if (c === '{' || c === '}' || c === ';') {
      if (c === '{') depth++;
      else if (c === '}') depth--;
      stmt = i + 1;
      continue;
    }
    const isPush = body.startsWith(`${arr}.push(`, i);
    const isLong = body.startsWith(`pushIntAsLong(${arr}`, i);
    const superCall = body.startsWith(`super.`, i);
    const thisCall = /^this\.([A-Za-z0-9_]+)\s*\(\s*([A-Za-z0-9_]+)/.exec(body.slice(i));
    if (!isPush && !isLong && !superCall && !thisCall) continue;
    const guarded = /\bif\s*\(/.test(body.slice(stmt, i));
    if (isPush || isLong) {
      if (depth !== 1 || guarded) unreadable = true;
      else count += isPush ? 1 : 2;
      continue;
    }
    if (superCall) {
      const m = /^super\.([A-Za-z0-9_]+)\s*\(\s*([A-Za-z0-9_]+)/.exec(body.slice(i));
      if (!m || m[2] !== arr) continue;
      const parent = clientClasses.get(owner)?.base;
      const sub = parent ? countAppends(parent, m[1], depthGuard + 1) : null;
      if (sub === null) unreadable = true;
      else count += sub;
      continue;
    }
    if (thisCall[2] !== arr) continue;
    // Dispatch the helper from the MOST DERIVED class, not from the one that happens to call it:
    // VariableFxNumberDisplayAddon overrides writeIntParams, VariableFxAddonBase calls it.
    const sub = countAppends(startClass, thisCall[1], depthGuard + 1);
    if (sub === null) unreadable = true;
    else count += sub;
  }
  return unreadable ? null : count;
}

// How many ints this box's form sends, or null when the shape is not statically countable.
function clientParamCount(className) {
  const found = resolveTsMethod(className, 'readIntParamsFromForm');
  if (!found) return null;
  const { body } = found;
  const ret = /return\s*\[/.exec(body);
  if (ret) {
    if (/\.concat\s*\(/.test(body)) return null;
    const { count, spread } = bracketElements(body, body.indexOf('[', ret.index));
    return spread ? null : count;
  }
  // `const params: number[] = []; … return params;`
  const local = /return\s+([A-Za-z0-9_]+)\s*;/.exec(body);
  if (!local) return null;
  const arr = local[1];
  if (!new RegExp(`(?:const|let)\\s+${arr}\\s*:\\s*number\\[\\]\\s*=\\s*\\[\\s*\\]`).test(body)) {
    return null;
  }
  return scanAppends(found, className, arr, 0);
}

const clientBoxes = new Map(); // "family:code" -> { klass, count, rel }
for (const [name, cls] of clientClasses) {
  const codeBody = /get\s+code\s*\(\s*\)\s*:\s*number\s*\{([^}]*)\}/.exec(cls.src);
  if (!codeBody) continue;
  const ref = /return\s+([A-Za-z0-9_]+\.[A-Za-z0-9_]+)\s*;/.exec(codeBody[1]);
  if (!ref) continue;
  const code = codeConsts.get(ref[1]);
  if (code === undefined || cls.family === null) continue;
  clientBoxes.set(`${cls.family}:${code}`, {
    klass: name,
    rel: cls.rel,
    count: clientParamCount(name),
  });
}

// ---- read our side ----------------------------------------------------------------------------

const enumValues = new Map(); // "WiredActionType.GIVE_EFFECT" -> 4
for (const family of FAMILIES) {
  const file = join(root, 'Vortex.Primitives/Rooms/Enums/Wired', `${family.enumName}.cs`);
  if (!existsSync(file)) continue;
  const src = stripComments(readFileSync(file, 'utf8'));
  for (const m of src.matchAll(/([A-Z][A-Z0-9_]*)\s*=\s*(-?\d+)/g))
    enumValues.set(`${family.enumName}.${m[1]}`, +m[2]);
}

const WIRED_LOGIC_DIR = join(root, 'Vortex.Rooms/Object/Logic/Furniture/Floor/Wired');
function walkCs(dir, out = []) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const child = join(dir, entry.name);
    if (entry.isDirectory()) walkCs(child, out);
    else if (entry.name.endsWith('.cs')) out.push(child);
  }
  return out;
}

function memberBody(src, signature) {
  const m = signature.exec(src);
  if (!m) return null;
  const start = m.index + m[0].length;
  const arrow = /^\s*=>/.exec(src.slice(start));
  if (arrow) {
    let depth = 0;
    for (let i = start + arrow[0].length; i < src.length; i++) {
      const c = src[i];
      if ('([{'.includes(c)) depth++;
      else if (')]}'.includes(c)) depth--;
      else if (c === ';' && depth <= 0) return src.slice(start, i);
    }
    return src.slice(start);
  }
  const brace = src.indexOf('{', start);
  if (brace < 0) return null;
  let depth = 0;
  for (let i = brace; i < src.length; i++) {
    if (src[i] === '{') depth++;
    else if (src[i] === '}' && --depth === 0) return src.slice(brace, i + 1);
  }
  return null;
}

// Elements of the list a `GetIntParamRules` body returns, whether written as a collection
// expression or as `new List<IWiredParamRule> { … }`. A `..base.GetIntParamRules()` spread is not a
// rule; it says the base contributes its own.
function countListElements(body) {
  let text = body.trim();
  if (text.startsWith('{')) text = text.slice(1, -1);
  const open = text.search(/\[/) >= 0 ? text.search(/\[/) : text.search(/\{/);
  if (open < 0) return null;
  return bracketElements(text, open);
}

const ours = new Map(); // className -> record
for (const file of walkCs(WIRED_LOGIC_DIR)) {
  const src = stripComments(readFileSync(file, 'utf8'));
  const decl =
    /(?:^|\n)\s*(?:public |internal |protected )*(abstract |sealed )?class\s+([A-Za-z0-9_]+)(?:<[^>]*>)?\s*(?:\([^)]*\))?\s*:\s*([A-Za-z0-9_]+)/.exec(
      src
    );
  if (!decl) continue;
  const [, modifier, name, base] = decl;
  const rulesBody = memberBody(src, /List<IWiredParamRule>\s+GetIntParamRules\s*\(\s*\)/);
  let own = null;
  let usesBase = false;
  if (rulesBody !== null) {
    usesBase = /base\.GetIntParamRules\s*\(/.test(rulesBody);
    const added = (rulesBody.match(/rules\.Add\s*\(/g) || []).length;
    if (added > 0) {
      own = added;
    } else {
      const list = countListElements(rulesBody);
      own = list === null ? null : list.count;
      if (list?.spread) usesBase = true;
    }
  }
  const tailBody = memberBody(src, /IWiredParamRule\??\s+GetIntParamTailRule\s*\(\s*\)/);
  const codeM = /WiredCode\s*=>\s*\(int\)([A-Za-z]+\.[A-Z0-9_]+)/.exec(src);
  ours.set(name, {
    file: file.slice(root.length + 1),
    name,
    base,
    own,
    usesBase,
    hasTail: tailBody !== null && /new\s+Wired/.test(tailBody),
    codeRef: codeM ? codeM[1] : null,
    logicKey: (/\[RoomObjectLogic\("([^"]+)"\)\]/.exec(src) || [])[1] ?? null,
    isAbstract: (modifier ?? '').trim() === 'abstract',
  });
}
const chain = (name, acc = []) => {
  const c = ours.get(name);
  if (!c || acc.includes(c)) return acc;
  acc.push(c);
  return chain(c.base, acc);
};

const FAMILY_OF_ENUM = Object.fromEntries(FAMILIES.map((f) => [f.enumName, f.label]));

const rows = [];
for (const box of ours.values()) {
  if (box.isAbstract || !box.logicKey || !box.codeRef) continue;
  const [enumName] = box.codeRef.split('.');
  const family = FAMILY_OF_ENUM[enumName];
  const code = enumValues.get(box.codeRef);
  if (family === undefined || code === undefined) continue;
  const links = chain(box.name);
  let declared = 0;
  for (const link of links) {
    if (link.own === null) continue;
    declared += link.own;
    if (!link.usesBase) break;
  }
  const hasTail = links.some((l) => l.hasTail);
  rows.push({ box, family, code, declared, hasTail, clientBox: clientBoxes.get(`${family}:${code}`) });
}

// ---- compare -----------------------------------------------------------------------------------

const disagreements = [];
const unreadable = [];
const unmatched = [];
for (const row of rows) {
  if (!row.clientBox) unmatched.push(row);
  else if (row.clientBox.count === null) unreadable.push(row);
  // A tail rule accepts anything at or above the fixed count, which is what a client that sends a
  // variable number of ints needs.
  else if (row.hasTail ? row.clientBox.count < row.declared : row.clientBox.count !== row.declared)
    disagreements.push(row);
}

const byFamily = (a, b) => a.family.localeCompare(b.family) || a.code - b.code;
const describe = (r) =>
  `  ${r.box.logicKey.padEnd(32)} ${r.family}/${String(r.code).padStart(4)}  ` +
  `client=${r.clientBox ? (r.clientBox.count ?? 'variable') : '-'}  vortex=${r.declared}` +
  `${r.hasTail ? '+tail' : ''}`;

if (showAll) {
  console.error(`Boxes matched to a client configuration class (${rows.length - unmatched.length}):\n`);
  for (const r of rows.filter((x) => x.clientBox).sort(byFamily))
    console.error(`${describe(r)}   ${r.clientBox.klass}`);
  console.error('');
}

if (showSkipped) {
  console.error(`Not compared -- no client configuration class for this code (${unmatched.length}):\n`);
  for (const r of unmatched.sort(byFamily))
    console.error(`  ${r.box.logicKey.padEnd(32)} ${r.family}/${String(r.code).padStart(4)}  vortex=${r.declared}`);
  console.error(`\nNot compared -- the client builds this array from what the operator picked (${unreadable.length}):\n`);
  for (const r of unreadable.sort(byFamily))
    console.error(`  ${r.box.logicKey.padEnd(32)} ${r.family}/${String(r.code).padStart(4)}  ${r.clientBox.rel}`);
  console.error('');
}

// A count that moves on either side is a different disagreement, and must block again.
const fingerprint = (r) => `${r.family}/${r.box.logicKey}:client=${r.clientBox.count}:vortex=${r.declared}`;

if (update) {
  const notes = existsSync(baselineFile)
    ? (JSON.parse(readFileSync(baselineFile, 'utf8')).notes ?? {})
    : {};
  writeFileSync(
    baselineFile,
    `${JSON.stringify({ known: disagreements.map(fingerprint).sort(), notes }, null, 2)}\n`
  );
  console.error(`check-wired-param-counts: baseline written (${disagreements.length} known).`);
  process.exit(0);
}

const baseline = existsSync(baselineFile)
  ? new Set(JSON.parse(readFileSync(baselineFile, 'utf8')).known)
  : new Set();
const added = disagreements.filter((r) => !baseline.has(fingerprint(r)));
const fixed = [...baseline].filter((k) => !disagreements.some((r) => fingerprint(r) === k));

const print = (list, heading) => {
  console.error(`${heading}\n`);
  for (const r of list.sort(byFamily)) {
    console.error(describe(r));
    console.error(`        ${r.box.file}`);
    console.error(`        client: ${WIRED_SETUP}/${r.clientBox.rel}`);
  }
  console.error('');
};

if (showAll && disagreements.length) {
  print(disagreements, `All disagreements (${disagreements.length}), baselined or not:`);
}

if (fixed.length) {
  console.error(
    `check-wired-param-counts: ${fixed.length} baselined disagreement(s) are gone. ` +
      `Run --update to drop them: ${fixed.join(', ')}`
  );
}

if (added.length) {
  print(
    added,
    `check-wired-param-counts: ${added.length} box(es) whose rule count disagrees with the client.\n` +
      'Each one refuses its ENTIRE configuration update, silently, on every save.'
  );
  console.error(
    'Fix the rule count, or declare a tail rule when the client sends a variable number.\n' +
      'This is a bug in a box nobody can configure, so fix it rather than baselining it; --update is\n' +
      'for a disagreement you have decided to live with, and each one wants a line in the baseline\n' +
      'notes saying why.'
  );
  process.exit(2);
}

console.error(
  `check-wired-param-counts: OK (${rows.length - unmatched.length - unreadable.length} boxes compared ` +
    `against the client, ${unreadable.length} whose client form builds its array from the operator's ` +
    `selection, ${unmatched.length} with no client configuration class; ${disagreements.length} known ` +
    `disagreement(s) baselined; --skipped lists what was not compared).`
);
process.exit(0);
