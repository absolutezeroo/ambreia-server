#!/usr/bin/env node
// Sends the whole content pack (spec Contenu §6) to a running emulator and prints what it said.
//
//   node scripts/content/push.mjs [--base http://127.0.0.1:8080] [--email you@x] [--password-env VAR] [--dir content]
//
// The password is read from the environment variable named by --password-env (default
// VORTEX_CONTENT_PASSWORD) and is never printed. The account needs content.publish (the owner role).
// Exit code 1 when the pack was refused or the server could not be reached.

import { readFileSync, readdirSync, existsSync } from 'node:fs';
import { join, relative, resolve, sep, dirname, basename } from 'node:path';
import { fileURLToPath } from 'node:url';

const REPO = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const COOKIE = 'habbo-web-session';

function option(name, fallback) {
  const i = process.argv.indexOf(`--${name}`);
  return i > 0 && process.argv[i + 1] ? process.argv[i + 1] : fallback;
}

function fail(message) {
  console.error(message);
  process.exit(1);
}

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

async function login(base, email, password) {
  const res = await fetch(`${base}/api/public/authentication/login`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ email, password }),
  });
  const cookie = res.headers.getSetCookie().find((c) => c.startsWith(`${COOKIE}=`));
  if (!cookie) fail(`Login refused for ${email}: HTTP ${res.status}.`);
  return cookie.split(';')[0];
}

const base = option('base', 'http://127.0.0.1:8080');
const email = option('email', process.env.VORTEX_CONTENT_EMAIL);
const passwordEnv = option('password-env', 'VORTEX_CONTENT_PASSWORD');
const password = process.env[passwordEnv];
if (!email) fail('No account: pass --email or set VORTEX_CONTENT_EMAIL.');
if (!password) fail(`No password: set the environment variable ${passwordEnv}.`);

const pack = readPack(resolve(REPO, option('dir', 'content')));
console.log(`Pack: ${pack.files.length} file(s), ${Object.keys(pack.texts).length} language(s).`);

let res;
try {
  const cookie = await login(base, email, password);
  res = await fetch(`${base}/api/content/import`, {
    method: 'POST',
    headers: { 'content-type': 'application/json', cookie },
    body: JSON.stringify(pack),
  });
} catch (e) {
  fail(`Cannot reach ${base}: ${e.message}`);
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
