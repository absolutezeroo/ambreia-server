// node unnamed.mjs <dir> : files under <dir> none of whose declared types is named (as a whole
// word, comments stripped) by any other production .cs file.
import fs from 'fs';
import path from 'path';

const root = process.cwd();
const [dir] = process.argv.slice(2);
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
const strip = (s) => s.replace(/\/\*[\s\S]*?\*\//g, ' ').replace(/\/\/.*$/gm, ' ');
const tok = new Map();
for (const f of prod) tok.set(f, new Set(strip(fs.readFileSync(f, 'utf8')).match(/[A-Za-z_]\w*/g) || []));
for (const f of prod.filter((f) => f.startsWith(dir + '/'))) {
  const src = strip(fs.readFileSync(f, 'utf8'));
  const types = [...src.matchAll(/\b(?:record\s+struct|record\s+class|class|record|struct|enum|interface)\s+([A-Za-z_]\w*)/g)].map((m) => m[1]);
  if (!types.length) continue;
  const named = types.some((t) => prod.some((g) => g !== f && (tok.get(g).has(t) || tok.get(g).has(t.replace(/Attribute$/, '')))));
  if (!named) console.log(f);
}
