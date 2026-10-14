#!/usr/bin/env node
// Writes the content the database holds back into content/ (spec content authoring §6): every
// document at its path with its text as authored, the text files, migrations.json, and the version
// pulled in content/.pack-version (push.mjs sends it, so a push made from older files is refused).
//
//   node scripts/content/pull.mjs [--base http://127.0.0.1:8080] [--email you@x] [--password-env VAR] [--dir content]
//
// Every path the server sends is checked before anything is written: a document lives under its
// kind's folder, lowercase segments ending in .json; a text file is texts/<language>.txt. One path
// outside that and nothing is written. Files of content/ the export no longer has are removed.

import { readdirSync, existsSync, writeFileSync, mkdirSync, rmSync } from 'node:fs';
import { join, relative, resolve, sep, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { fail, option, signIn } from './session.mjs';

const REPO = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const DOCUMENT = /^[a-z0-9_-]+(\/[a-z0-9_-]+)+\.json$/;
const LANGUAGE = /^[a-z]{2}(-[a-z]{2})?$/;

/** What a pull writes and removes, paths relative to content/; throws on any unsafe path. */
export function planPull(exported, existing) {
  const writes = [];

  for (const file of exported.files) {
    if (!DOCUMENT.test(file.path)) throw new Error(`Path refused: ${JSON.stringify(file.path)}`);
    writes.push({ path: file.path, text: file.text });
  }

  for (const [language, body] of Object.entries(exported.texts ?? {})) {
    if (!LANGUAGE.test(language)) throw new Error(`Text language refused: ${JSON.stringify(language)}`);
    writes.push({ path: `texts/${language}.txt`, text: body });
  }

  if (exported.migrations !== null && exported.migrations !== undefined) {
    writes.push({ path: 'migrations.json', text: exported.migrations });
  }

  writes.push({ path: '.pack-version', text: `${exported.version}\n` });

  const kept = new Set(exported.files.map((f) => f.path));

  return { writes, removes: existing.filter((path) => !kept.has(path)) };
}

function* walk(dir) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) yield* walk(path);
    else yield path;
  }
}

/** The documents content/ has now, relative paths (migrations.json and the texts aside). */
function documentsIn(root) {
  if (!existsSync(root)) return [];
  return [...walk(root)]
    .map((f) => relative(root, f).split(sep).join('/'))
    .filter((p) => p.endsWith('.json') && p !== 'migrations.json');
}

async function main() {
  const root = resolve(REPO, option('dir', 'content'));
  const { base, cookie } = await signIn();
  const res = await fetch(`${base}/api/content/export`, { headers: { cookie } });
  if (res.status !== 200) fail(`Export refused: HTTP ${res.status}.`);

  let plan;
  try {
    plan = planPull(await res.json(), documentsIn(root));
  } catch (e) {
    fail(`${e.message}: nothing was written.`);
  }

  for (const write of plan.writes) {
    const target = resolve(root, write.path);
    // Belt and braces after planPull: nothing lands outside content/.
    if (!target.startsWith(root + sep)) fail(`Path refused: ${write.path}: nothing more is written.`);
    mkdirSync(dirname(target), { recursive: true });
    writeFileSync(target, write.text);
  }

  for (const path of plan.removes) rmSync(resolve(root, path));

  console.log(`Pulled: ${plan.writes.length - 1} file(s) written, ${plan.removes.length} removed.`);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  await main();
}
