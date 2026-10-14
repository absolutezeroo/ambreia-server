#!/usr/bin/env node
// Sends the whole content pack (spec Contenu §6) to a running emulator and prints what it said.
//
//   node scripts/content/push.mjs [--base http://127.0.0.1:8080] [--email you@x] [--password-env VAR] [--dir content] [--force]
//
// The version content/.pack-version records (written by pull.mjs) goes with the pack: when the
// database moved since (the Game Editor wrote to it), the push is refused rather than erasing that
// work. --force pushes anyway, and the server logs it.
//
// The password is read from the environment variable named by --password-env (default
// VORTEX_CONTENT_PASSWORD) and is never printed. The account needs content.publish (the owner role).
// Exit code 1 when the pack was refused or the server could not be reached.

import { readFileSync, readdirSync, existsSync } from 'node:fs';
import { join, relative, resolve, sep, dirname, basename } from 'node:path';
import { fileURLToPath } from 'node:url';
import { fail, flag, option, signIn } from './session.mjs';

const REPO = join(dirname(fileURLToPath(import.meta.url)), '..', '..');

function* walk(dir) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) yield* walk(path);
    else yield path;
  }
}

// Read as the server's ContentPackInRepoTests reads it: documents by path relative to the pack,
// the migrations file apart, one text file per language.
function readPack(root) {
  if (!existsSync(root)) fail(`No content directory at ${root}.`);
  const migrations = join(root, 'migrations.json');
  const files = [...walk(root)]
    .filter((f) => f.endsWith('.json') && f !== migrations)
    .sort()
    .map((f) => ({ path: relative(root, f).split(sep).join('/'), text: readFileSync(f, 'utf8') }));
  const textsDir = join(root, 'texts');
  const texts = Object.fromEntries(
    (existsSync(textsDir) ? readdirSync(textsDir) : [])
      .filter((f) => f.endsWith('.txt'))
      .map((f) => [basename(f, '.txt'), readFileSync(join(textsDir, f), 'utf8')]),
  );
  return { files, migrations: existsSync(migrations) ? readFileSync(migrations, 'utf8') : null, texts };
}

const root = resolve(REPO, option('dir', 'content'));
const pack = readPack(root);
console.log(`Pack: ${pack.files.length} file(s), ${Object.keys(pack.texts).length} language(s).`);

const versionFile = join(root, '.pack-version');
const query = flag('force')
  ? '?force=true'
  : existsSync(versionFile)
    ? `?baseVersion=${Number.parseInt(readFileSync(versionFile, 'utf8'), 10)}`
    : '';

let res;
try {
  const { base, cookie } = await signIn();
  res = await fetch(`${base}/api/content/import${query}`, {
    method: 'POST',
    headers: { 'content-type': 'application/json', cookie },
    body: JSON.stringify(pack),
  });
} catch (e) {
  fail(`Cannot reach ${base}: ${e.message}`);
}

if (res.status === 409) {
  fail('Refused: the content moved since your last pull (the Game Editor wrote to it). Run pull.mjs first, or --force to erase that work.');
}

const text = await res.text();
let result;
try {
  result = JSON.parse(text);
} catch {
  result = { imported: false, findings: [] };
  console.error(`HTTP ${res.status}: ${text}`);
}

for (const f of result.findings ?? []) {
  console.log(`[${f.severity}] ${f.code} ${f.file ?? '-'} ${f.contentId ?? '-'} : ${f.message}`);
}

// exitCode, not exit(): exiting while fetch still closes its socket aborts Node on Windows
// (libuv UV_HANDLE_CLOSING assertion), and the exit code is lost.
if (result.imported) {
  console.log(
    `Imported: version ${result.packVersion}, ${result.added} added, ${result.changed} changed, ${result.removed} removed.`,
  );
} else {
  console.error(`Refused (HTTP ${res.status}): nothing was written.`);
  process.exitCode = 1;
}
