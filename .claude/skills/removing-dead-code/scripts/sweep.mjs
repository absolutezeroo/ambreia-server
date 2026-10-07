// node .claude/skills/removing-dead-code/scripts/sweep.mjs [--apply]   (from the repository root)
// Report mode lists the dead files; --apply deletes them, drops their map statements, Headers.cs
// constants and DbSets. Re-run until it deletes 0, then build: what breaks is a dangling use.
// Liveness sweep over Protocol / Revisions / Primitives / Database entities.
// Roots: every production .cs file outside the candidate areas. A candidate file is live when a
// type (or, for static classes, a public static method) it declares is named by a live file.
// Revisions parsers/serializers are live iff the message/composer they handle is live; maps and
// Headers.cs never propagate. Comments and plain string literals are stripped before tokenising.
import fs from 'fs';
import path from 'path';

const root = process.cwd();
const apply = process.argv.includes('--apply');

function walk(dir, out = []) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (e.name === 'bin' || e.name === 'obj' || e.name === 'node_modules' || e.name.startsWith('.')) continue;
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, out);
    else if (e.name.endsWith('.cs')) out.push(path.relative(root, p).split(path.sep).join('/'));
  }
  return out;
}

const projects = fs.readdirSync(root).filter((d) => d.startsWith('Vortex.') && fs.existsSync(path.join(root, d, d + '.csproj')));
const isTest = (p) => /\.Tests$|Tests\.Support$|TestPlugin$/.test(p);
const files = projects.flatMap((p) => walk(path.join(root, p)));

const REV = 'Vortex.Revisions/Revision20260701/';
function area(f) {
  if (f.startsWith('Vortex.Protocol/')) return 'protocol';
  if (f.startsWith(REV + 'Maps/') || f === REV + 'Headers.cs') return 'frame';
  if (f.startsWith(REV + 'Parsers/') || f.startsWith(REV + 'Serializers/')) return 'codec';
  if (f.startsWith('Vortex.Primitives/')) return 'primitives';
  if (f.startsWith('Vortex.Database/Entities/')) return 'entity';
  if (f.startsWith('Vortex.Database/Context/') || f.startsWith('Vortex.Database/Migrations/')) return 'frame';
  if (isTest(f.split('/')[0])) return 'test';
  return 'root';
}

function strip(src) {
  return src
    .replace(/\/\*[\s\S]*?\*\//g, ' ')
    .replace(/\/\/.*$/gm, ' ')
    .replace(/@"(?:[^"]|"")*"/g, '""')
    .replace(/(?<!\$)"(?:[^"\\\n]|\\.)*"/g, '""')
    .replace(/^\s*(?:global\s+)?using\s+[\w.=\s]+;\s*$/gm, ' ')
    .replace(/^\s*namespace\s+[\w.]+\s*;?\s*$/gm, ' ');
}

const info = new Map();
const declares = new Map(); // symbol -> files
for (const f of files) {
  const raw = fs.readFileSync(f, 'utf8');
  const src = strip(raw);
  const types = [...src.matchAll(/\b(?:record\s+struct|record\s+class|class|record|struct|enum|interface)\s+([A-Za-z_]\w*)/g)].map((m) => m[1]);
  const statics = /\bstatic\s+(?:partial\s+)?class\b/.test(src)
    ? [...src.matchAll(/\bpublic\s+static\s+(?:async\s+)?[\w<>\[\],?. ]+?\s+([A-Za-z_]\w*)\s*(?:<[^>]*>)?\s*\(\s*this\b/g)].map((m) => m[1])
    : [];
  const tokens = new Set(src.match(/[A-Za-z_]\w*/g) || []);
  const syms = [...new Set([...types, ...statics])];
  info.set(f, { area: area(f), syms, tokens, types });
  for (const s of syms) {
    if (!declares.has(s)) declares.set(s, new Set());
    declares.get(s).add(f);
  }
}

// DbSet property names stand for their entity: `dbCtx.Rooms` names RoomEntity.
const ctxFile = 'Vortex.Database/Context/VortexDbContext.cs';
const dbsets = [...fs.readFileSync(ctxFile, 'utf8').matchAll(/DbSet<(\w+)>\s+(\w+)/g)];
for (const [, entity, prop] of dbsets) {
  const ds = declares.get(entity);
  if (!ds) continue;
  if (!declares.has(prop)) declares.set(prop, new Set());
  for (const d of ds) declares.get(prop).add(d);
}
const dbsetNames = new Set(dbsets.map((m) => m[2]));
const entityTypes = new Set([...info].filter(([, i]) => i.area === 'entity').flatMap(([, i]) => i.types));

// codec file -> the protocol type it handles (file name minus Parser/Serializer suffix)
function handled(f) {
  const b = path.basename(f, '.cs');
  return b.replace(/Serializer$/, '').replace(/Parser$/, '');
}

const live = new Set();
const queue = [];
const mark = (f) => { if (!live.has(f)) { live.add(f); queue.push(f); } };
for (const [f, i] of info) if (i.area === 'root') mark(f);

function propagate() {
  while (queue.length) {
    const f = queue.pop();
    const fromEntity = info.get(f).area === 'entity';
    for (const t of info.get(f).tokens) {
      // Navigations do not keep an entity alive: only code outside the entities can.
      if (fromEntity && (entityTypes.has(t) || dbsetNames.has(t))) continue;
      for (const name of [t, t + 'Attribute']) {
        const ds = declares.get(name);
        if (!ds) continue;
        for (const d of ds) {
          const a = info.get(d).area;
          if (a === 'frame' || a === 'test' || a === 'root') continue;
          // codecs come alive through what they handle, or as a helper a live codec names
          if (a === 'codec' && info.get(f).area !== 'codec') continue;
          mark(d);
        }
      }
    }
    // codecs whose handled type is now live
    for (const [g, i] of info) {
      if (i.area !== 'codec' || live.has(g)) continue;
      const ds = declares.get(handled(g));
      if (ds && [...ds].some((d) => live.has(d))) mark(g);
    }
  }
}
propagate();

const dead = [...info].filter(([f, i]) => ['protocol', 'codec', 'primitives', 'entity'].includes(i.area) && !live.has(f)).map(([f]) => f);
const byArea = {};
for (const f of dead) (byArea[info.get(f).area] ??= []).push(f);
for (const [a, fs_] of Object.entries(byArea)) console.log(`${a}: ${fs_.length} dead`);
if (!apply) for (const f of dead) console.log(`  ${f}`);
function rewrite(file, fn) {
  const text = fs.readFileSync(file, 'utf8');
  const crlf = text.includes('\r\n');
  const out = fn(text.replace(/\r\n/g, '\n'));
  fs.writeFileSync(file, crlf ? out.replace(/\n/g, '\r\n') : out);
}
if (apply) {
  const deadCodecs = new Set(dead.filter((f) => info.get(f).area === 'codec').flatMap((f) => info.get(f).types));
  const deadEntities = new Set(dead.filter((f) => info.get(f).area === 'entity').flatMap((f) => info.get(f).types));
  for (const f of dead) fs.unlinkSync(f);
  // Maps: drop each `builder.MapXxx(...);` statement naming a dead codec.
  for (const m of fs.readdirSync(REV + 'Maps')) {
    rewrite(REV + 'Maps/' + m, (t) =>
      t.replace(/\n[ \t]*builder\.Map\w+\([\s\S]*?\);[ \t]*(?=\n)/g, (stmt) =>
        [...deadCodecs].some((c) => new RegExp('\\b' + c + '\\b').test(stmt)) ? '' : stmt));
  }
  // Headers: drop constants no map names any more.
  const mapText = fs.readdirSync(REV + 'Maps').map((m) => fs.readFileSync(REV + 'Maps/' + m, 'utf8')).join('\n');
  rewrite(REV + 'Headers.cs', (t) =>
    t.split('\n').filter((l) => {
      const m = l.match(/public const int (\w+)\s*=/);
      return !m || new RegExp('\\.' + m[1] + '\\b').test(mapText);
    }).join('\n'));
  // DbContext: drop DbSets of dead entities.
  rewrite(ctxFile, (t) =>
    t.replace(/=\n\s+null!;/g, '= null!;').split('\n').filter((l) => {
      const m = l.match(/DbSet<(\w+)>/);
      return !m || !deadEntities.has(m[1]);
    }).join('\n'));
}
console.log(apply ? `deleted ${dead.length}` : `report only (${dead.length}); --apply to delete`);
