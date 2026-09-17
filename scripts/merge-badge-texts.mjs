#!/usr/bin/env node
// Fills in the badge captions the served localization file does not have.
//
// The client asks getAchievementName/getBadgeName for `badge_name_<code>`, falls back to
// `badge_name_<base>` (the "%roman%" one), and when neither key exists it draws the key itself.
// That is what `badge_name_ACH_AllTimeHotelPresence1` under a badge in the achievement window
// means, and it is not an emulator bug: the served gamedata/external_flash_texts.json carries
// 13 024 keys and not one of the 20 346 badge_* ones.
//
// Merges badge_* from a full external_flash_texts.txt into the served JSON and into every
// per-language copy beside it, and never overwrites a key the hotel already has — an operator's
// edited caption stays edited.
//
//   node scripts/merge-badge-texts.mjs <external_flash_texts.txt> <gamedata dir> [--apply]
//
// Dry run unless --apply. With --apply each file is backed up as <name>.json.bak first; the hash
// the client caches on comes from hashes.php (md5_file), so the new file is picked up by itself.

import { readFileSync, writeFileSync, copyFileSync, existsSync, readdirSync } from 'node:fs';
import { join } from 'node:path';

const [source, gamedata] = process.argv.slice(2).filter((a) => !a.startsWith('--'));
const apply = process.argv.includes('--apply');

if (!source || !gamedata) {
  console.error('usage: merge-badge-texts.mjs <external_flash_texts.txt> <gamedata dir> [--apply]');
  process.exit(2);
}

/** The full file is `key=value`, one per line, values free to contain `=`. */
const parsed = new Map();
for (const line of readFileSync(source, 'utf8').split(/\r?\n/)) {
  const at = line.indexOf('=');
  if (at > 0 && !line.startsWith('#')) parsed.set(line.slice(0, at), line.slice(at + 1));
}

const badges = [...parsed].filter(([key]) => key.startsWith('badge_'));
console.log(`${source}: ${parsed.size} keys, ${badges.length} badge_*`);

if (badges.length === 0) {
  console.error('No badge_* keys in the source. Wrong file?');
  process.exit(1);
}

// The default language sits at the root of gamedata/, the others one directory down. Both are
// served: external.texts.txt points at the root one, localization.<n>.url at the per-language ones.
const targets = [join(gamedata, 'external_flash_texts.json')];
for (const entry of readdirSync(gamedata, { withFileTypes: true })) {
  if (!entry.isDirectory()) continue;
  const candidate = join(gamedata, entry.name, 'external_flash_texts.json');
  if (existsSync(candidate)) targets.push(candidate);
}

for (const target of targets) {
  const texts = JSON.parse(readFileSync(target, 'utf8'));
  const added = badges.filter(([key]) => !(key in texts));

  console.log(
    `${target}: ${Object.keys(texts).length} keys, ${added.length} to add` + (apply ? '' : ' (dry run)')
  );

  if (!apply || added.length === 0) continue;

  copyFileSync(target, `${target}.bak`);
  for (const [key, value] of added) texts[key] = value;
  // Compact, as the served files already are: the client downloads this on every boot.
  writeFileSync(target, JSON.stringify(texts), 'utf8');
}
