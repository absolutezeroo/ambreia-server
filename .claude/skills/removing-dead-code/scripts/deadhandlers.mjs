// node deadhandlers.mjs <dir> [--apply] : in each .cs file of <dir>, drops every top-level class
// implementing IEventHandler<E> whose E is named by no production file outside <dir> and outside
// Vortex.Primitives/Events (where events are declared). A file left with no type is deleted.
import fs from 'fs';
import path from 'path';

const [dir] = process.argv.slice(2);
const apply = process.argv.includes('--apply');
const root = process.cwd();
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
const prod = projects.filter((p) => !/\.Tests$|Tests\.Support$|TestPlugin$/.test(p)).flatMap((p) => walk(path.join(root, p)));
const tokens = new Set();
for (const f of prod) {
  if (f.startsWith(dir + '/') || f.startsWith('Vortex.Primitives/Events/')) continue;
  const src = fs.readFileSync(f, 'utf8').replace(/\/\*[\s\S]*?\*\//g, ' ').replace(/\/\/.*$/gm, ' ');
  for (const t of src.match(/[A-Za-z_]\w*/g) || []) tokens.add(t);
}
for (const f of walk(path.join(root, dir))) {
  const text = fs.readFileSync(f, 'utf8');
  const crlf = text.includes('\r\n');
  const lines = text.replace(/\r\n/g, '\n').split('\n');
  // top-level chunks: [start, end] where start is the first ///, [ or "public" line of a type
  const chunks = [];
  for (let i = 0; i < lines.length; i++) {
    if (!/^(public|internal) .*\b(class|record)\b/.test(lines[i])) continue;
    let s = i;
    while (s > 0 && /^(\/\/\/|\[)/.test(lines[s - 1])) s--;
    let e = lines.findIndex((l, k) => k > i && l === '}');
    chunks.push({ s, e, head: lines.slice(i, i + 4).join(' ') });
    i = e;
  }
  const drop = chunks.filter((c) => {
    const m = c.head.match(/IEventHandler<(\w+)>/);
    return m && !tokens.has(m[1]);
  });
  if (!drop.length) continue;
  console.log(`${f}: drop ${drop.length}/${chunks.length} ${drop.map((c) => c.head.match(/IEventHandler<(\w+)>/)[1]).join(',')}`);
  if (!apply) continue;
  if (drop.length === chunks.length) { fs.unlinkSync(f); continue; }
  for (const c of [...drop].reverse()) {
    let e = c.e;
    if (lines[e + 1] === '') e++;
    lines.splice(c.s, e - c.s + 1);
  }
  const out = lines.join('\n');
  fs.writeFileSync(f, crlf ? out.replace(/\n/g, '\r\n') : out);
}
