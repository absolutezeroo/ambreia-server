// node deadmethods.mjs [dir...] : public/internal methods (declared in the given dirs) whose name
// appears in no production line other than a declaration. Interface + implementation pairs count
// as declarations, so an uncalled grain method shows up too. Overrides and handlers are skipped.
import fs from 'fs';
import path from 'path';

const root = process.cwd();
const dirs = process.argv.slice(2);
function walk(d, out = []) {
  for (const e of fs.readdirSync(d, { withFileTypes: true })) {
    if (['bin', 'obj', 'node_modules'].includes(e.name) || e.name.startsWith('.')) continue;
    const p = path.join(d, e.name);
    if (e.isDirectory()) walk(p, out);
    else if (e.name.endsWith('.cs')) out.push(path.relative(root, p).split(path.sep).join('/'));
  }
  return out;
}
const projects = fs.readdirSync(root).filter((d) => d.startsWith('Vortex.') && fs.existsSync(path.join(root, d, d + '.csproj')));
const prod = projects.filter((p) => !/\.Tests$|Tests\.Support$|TestPlugin$/.test(p)).flatMap((p) => walk(path.join(root, p)))
  .filter((f) => !f.includes('/Migrations/'));
const decl = /^\s*(?:public|internal)\s+(?!class|record|struct|enum|interface|override|const|static\s+class|sealed\s+class|abstract\s+class)[^=;(]*?\b([A-Z]\w*)\s*(?:<[^>]*>)?\s*\(/;
const declLine = /^\s*(?:public|internal|private|protected)?\s*(?:static\s+|async\s+|virtual\s+|abstract\s+|new\s+)*[\w<>\[\],?. ()]+\s+[A-Z]\w*\s*(?:<[^>]*>)?\s*\(/;
const uses = new Map();
const declared = [];
for (const f of prod) {
  const lines = fs.readFileSync(f, 'utf8').replace(/\/\*[\s\S]*?\*\//g, ' ').split(/\r?\n/).map((l) => l.replace(/\/\/.*$/, ''));
  lines.forEach((l, i) => {
    const m = l.match(decl);
    const isDecl = declLine.test(l) && !/[=]\s*[^>]/.test(l.split('(')[0]) && !/\breturn\b|\bawait\b|\bnew\b/.test(l.split('(')[0]);
    if (m && dirs.some((d) => f.startsWith(d))) declared.push({ f, line: i + 1, name: m[1] });
    if (isDecl) return;
    for (const t of l.match(/[A-Za-z_]\w*/g) || []) uses.set(t, (uses.get(t) || 0) + 1);
  });
}
const seen = new Set();
for (const d of declared) {
  if (uses.get(d.name)) continue;
  if (/^(HandleAsync|OnActivateAsync|OnDeactivateAsync|ConfigureServices|Dispose|DisposeAsync|ExecuteAsync|StartAsync|StopAsync|Main)$/.test(d.name)) continue;
  const k = d.f + ':' + d.name;
  if (seen.has(k)) continue;
  seen.add(k);
  console.log(`${d.f}:${d.line} ${d.name}`);
}
