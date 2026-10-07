// node fixusings.mjs : drops `using Vortex.*;` directives naming a namespace no .cs file declares.
import fs from 'fs';
import path from 'path';

const root = process.cwd();
function walk(d, out = []) {
  for (const e of fs.readdirSync(d, { withFileTypes: true })) {
    if (['bin', 'obj', 'node_modules'].includes(e.name) || e.name.startsWith('.')) continue;
    const p = path.join(d, e.name);
    if (e.isDirectory()) walk(p, out);
    else if (e.name.endsWith('.cs')) out.push(p);
  }
  return out;
}
const files = fs.readdirSync(root).filter((d) => d.startsWith('Vortex.') && fs.statSync(path.join(root, d)).isDirectory()).flatMap((d) => walk(path.join(root, d)));
const declared = new Set();
for (const f of files) {
  for (const m of fs.readFileSync(f, 'utf8').matchAll(/^\s*namespace\s+([\w.]+)/gm)) {
    const parts = m[1].split('.');
    for (let i = 1; i <= parts.length; i++) declared.add(parts.slice(0, i).join('.'));
  }
}
let n = 0;
for (const f of files) {
  const t = fs.readFileSync(f, 'utf8');
  const out = t.replace(/^using\s+(?:static\s+)?(?:\w+\s*=\s*)?(Vortex\.[\w.]+);\r?\n/gm, (line, ns) => {
    // `using static Vortex.X.Type` / alias to a type: keep when the parent namespace exists.
    const parent = ns.split('.').slice(0, -1).join('.');
    if (declared.has(ns) || (/static|=/.test(line) && declared.has(parent))) return line;
    n++;
    return '';
  });
  if (out !== t) fs.writeFileSync(f, out);
}
console.log(`removed ${n} using(s)`);
