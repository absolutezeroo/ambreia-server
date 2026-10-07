#!/usr/bin/env node
// Regenerates the single InitialCreate migration after an entity change, keeping its seed block.
//
//   node .claude/skills/changing-the-schema/scripts/regenerate-initialcreate.mjs
//
// 1. reads the seed block (the comment + `migrationBuilder.Sql(SeedScripts.Read(...))` lines) from
//    the current *_InitialCreate.cs;
// 2. deletes the migration, its Designer and the model snapshot;
// 3. runs `pwsh scripts/ef.ps1 migrations add InitialCreate`;
// 4. puts the seed block back at the end of Up(), same order, adds `using Vortex.Database.Seeds;`;
// 5. runs csharpier on the migrations folder.
// Run from the repository root. Exits non-zero, with the reason, on any failure.
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const dir = 'Vortex.Database/Migrations';
const die = (m) => {
  console.error(`regenerate-initialcreate: ${m}`);
  process.exit(1);
};
if (!fs.existsSync('Vortex.Cloud.sln')) die('run me from the repository root.');

const current = fs.readdirSync(dir).filter((f) => /_InitialCreate\.cs$/.test(f));
if (current.length !== 1) die(`expected one *_InitialCreate.cs in ${dir}, found ${current.length}.`);

const read = (f) => fs.readFileSync(f, 'utf8').replace(/^﻿/, '').replace(/\r\n/g, '\n');
const old = read(path.join(dir, current[0]));
const lines = old.split('\n');
const first = lines.findIndex((l) => /SeedScripts\.Read\(/.test(l));
if (first < 0) die('no SeedScripts.Read line in the current migration; restore it from git first.');
let start = first;
while (start > 0 && /^\s*\/\//.test(lines[start - 1])) start--;
let end = first;
while (end + 1 < lines.length && /SeedScripts\.Read\(/.test(lines[end + 1])) end++;
const block = lines.slice(start, end + 1).join('\n');
console.log(`seed block: ${end - first + 1} seed(s) kept`);

for (const f of [current[0], current[0].replace('.cs', '.Designer.cs'), 'VortexDbContextModelSnapshot.cs']) {
  fs.rmSync(path.join(dir, f), { force: true });
}

const ef = spawnSync('pwsh', ['-NoProfile', '-File', 'scripts/ef.ps1', 'migrations', 'add', 'InitialCreate'], {
  encoding: 'utf8',
});
process.stdout.write(ef.stdout ?? '');
if (ef.status !== 0) die(`ef failed:\n${ef.stderr}\nRestore with: git checkout -- ${dir}`);

const fresh = fs.readdirSync(dir).filter((f) => /_InitialCreate\.cs$/.test(f));
if (fresh.length !== 1) die('ef did not produce exactly one InitialCreate.');
const target = path.join(dir, fresh[0]);
let text = read(target);
const marker = '        }\n\n        /// <inheritdoc />\n        protected override void Down(';
if (!text.includes(marker)) die('could not find the end of Up() in the generated migration.');
text = text.replace(marker, `\n${block}\n${marker}`);
if (!text.includes('using Vortex.Database.Seeds;')) {
  text = text.replace('using Microsoft.EntityFrameworkCore.Migrations;\n', 'using Microsoft.EntityFrameworkCore.Migrations;\nusing Vortex.Database.Seeds;\n');
}
fs.writeFileSync(target, '﻿' + text.replace(/\n/g, '\r\n'));

const fmt = spawnSync('dotnet', ['csharpier', 'format', dir], { encoding: 'utf8' });
if (fmt.status !== 0) die(`csharpier failed:\n${fmt.stdout}${fmt.stderr}`);
console.log(`regenerated ${target}; tables: ${(text.match(/CreateTable\(/g) || []).length}`);
