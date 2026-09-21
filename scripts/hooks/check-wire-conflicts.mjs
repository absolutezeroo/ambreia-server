#!/usr/bin/env node
// Ratchet on the wire disagreements the specs already compute and nothing reads.
//
// `Vortex.Specs.Cli -- conflicts --kind field_count` knows, for every packet, how many fields each
// source says it carries: the official client's own code, the reference emulators, and us. When our
// count differs from the CLIENT's count, either the client parses fields we never wrote or we write
// fields it never reads, and both desynchronize everything after that field. Nothing in the build,
// the tests or grep sees it; the symptom is a dialog that never opens.
//
// The list is a triage queue, not a bug list. Read the packet before you change it.
//
// Two things make an entry appear that is not a bug, and knowing them saves re-deriving them:
//
//   1. A loop in a client parser reads as its body's fields. ConflictDetector collapses runs marked
//      "inside a repeated block" so `count + array` compares against `count + loop body`, which is
//      what `incoming/AcceptFriend` was. Already handled.
//   2. A parser that delegates -- `new SomeEntry(wrapper)` inside the loop -- hides those reads.
//      As3ClientAnalyzer follows one level of that now, and WireLayoutExtractor follows any call
//      that hands our packet to another method (the test is the packet, not the method's name).
//      What NEITHER side follows is delegation through a *method*: `entry.parse(wrapper)` on an
//      object, or `Helper.parseObjectData(wrapper)` on a class. That undercounts the CLIENT, so an
//      entry where the client's number looks too small for the message is probably this. It is the
//      single commonest shape left -- the whole ObjectAdd/ItemAdd/Objects/Items family is it.
//   3. The client guards optional trailing reads with `bytesAvailable > 0`. Sending fewer fields than
//      it can read is then legal and intended, not a truncation: HanditemConfiguration reads four
//      booleans that way and we send one.
//
// History, so nobody re-derives it. Until 2026-08-25 the client scanner bound headers only to
// composers, so 17 of 805 outgoing packets had any evidence from the build this emulator targets --
// every outgoing comparison was against a client from 2016, and that is where the original 23
// baselined entries came from. Following the parser behind each MessageEvent wrapper took outgoing
// evidence to 514; the list went 23 -> 64 (disagreements with the RIGHT client, for the first time),
// then to 46 once loops and constructor delegation were understood on both sides.
//
// **All 45 that stand have now been read against the WIN63 AS3.** Every one is an artifact of the
// three shapes above, or a composer that writes nothing and is tracked as such elsewhere. The list is
// therefore a ratchet and not a queue: it exists so a NEW disagreement fails the gate on the commit
// that introduces it, and an entry appearing in it is news.
//
// Two real findings came out of the reading, both fixed:
//   - AcceptFriendResult was sent on 3407, which WIN63 hands to its self-donation handler. It is 3707.
//   - RentableSpaceStatus wrote a trailing CurrencyName that the client, Arcturus and Nitro all stop
//     before. Removed.
//
// 2026-08-26, entry 46 -- WiredEnvironment (2827), client 2 / vortex 3. Not a disagreement: the
// client reads a boolean, then guards `bytesAvailable > 0` around a count and a `readString()` in a
// while loop. Neither scanner follows a read inside a loop inside a guard, so the client is counted
// at bool+int and this emulator at bool+int+string for the same bytes. The real order is pinned by
// Vortex.Revisions.Tests/UserDefinedRoomEvents/WiredClickUserWireTests, read off _SafeCls_3496.
//
// 2026-09-21, ThumbnailStatus (outgoing) -- client 1 / vortex 2. Not a disagreement either, and
// the inverse spelling of shape 3: the client does not guard a *trailing* read, it wraps its whole
// body. `unknowns/_SafePkg_4018/_SafeCls_4248.parse()` is
//
//     if(param1.bytesAvailable) { _ok = readBoolean(); _renderLimitHit = readBoolean(); }
//
// with the two fields initialised to true and false before anything is read, which is how an empty
// body means "ok". The extractor sees the guard and counts one; the parser reads two booleans, and
// RoomThumbnailCameraWidgetHandler.onThumbnailStatus() consults both isOk() and
// isRenderLimitHit(). Writing both is correct.
//
// And one that is not a wire bug but is worth knowing: HanditemConfiguration's client reads four
// flags -- isHanditemControlBlocked, chooserDisabled, freeFurniMovementsEnabled, invisibleFurni --
// and this emulator has only the first. The other three are features nobody has built, not fields
// anybody forgot.
//
// This does not demand the existing disagreements be fixed. It fixes their LIST, so a new one
// fails the gate on the commit that introduces it instead of surfacing in a running client.
//
//   node scripts/hooks/check-wire-conflicts.mjs            # compare against the baseline
//   node scripts/hooks/check-wire-conflicts.mjs --update   # accept the current list as the baseline
//
// Exit 2 = a disagreement with the client that the baseline does not contain.
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const baselineFile = path.join(root, 'scripts', 'hooks', 'wire-conflicts-baseline.json');
const update = process.argv.includes('--update');

const cli = spawnSync(
  'dotnet',
  ['run', '--project', 'Vortex.Specs.Cli', '--', 'conflicts', '--kind', 'field_count', '--limit', '100000'],
  { cwd: root, encoding: 'utf8', timeout: 300_000 }
);

if (cli.status !== 0) {
  console.error(`check-wire-conflicts: could not run Vortex.Specs.Cli (exit ${cli.status}).\n${cli.stderr || cli.stdout}`);
  process.exit(2);
}

// Output shape:
//     cf_e941b1366b  incoming/AcceptFriend: field count
//         as3:WIN63-... (client_code): 1 fields: unknown
//         vortex (vortex_emulator): 2 fields: int32, array
const entries = [];
let current = null;
for (const line of cli.stdout.split(/\r?\n/)) {
  const header = /^ {4}(cf_[0-9a-f]+) {2}(.+?): field count$/.exec(line);
  if (header) {
    current = { id: header[1], subject: header[2], positions: [] };
    entries.push(current);
    continue;
  }
  const position = /^ {8}(\S+) \(([a-z_]+)\): (\d+) fields/.exec(line);
  if (position && current) {
    current.positions.push({ origin: position[1], authority: position[2], fields: Number(position[3]) });
  }
}

if (entries.length === 0) {
  console.error('check-wire-conflicts: parsed no conflicts -- the CLI output format changed, this check is blind.');
  process.exit(2);
}

// The client and reference checkouts live outside this repository on purpose (SpecWorkspace looks
// for a sibling holding a `sources` directory), so the specs scan degrades instead of failing when
// they are absent -- on CI, they always are. Without the client's own field counts there is nothing
// to compare against, and reporting "OK" for that would be worse than saying nothing.
if (!entries.some((e) => e.positions.some((p) => p.authority === 'client_code'))) {
  console.error(
    'check-wire-conflicts: skipped -- no client_code source in the scan. The official client sources\n' +
      'are not checked out beside this repository, so the specs have no field counts to compare ours to.'
  );
  process.exit(0);
}

// Only disagreements against the client itself. A disagreement with a reference emulator is
// evidence, not authority (AGENTS.md), and there are ~100 of those that mean nothing on their own.
const againstClient = entries
  .filter((e) => {
    const ours = e.positions.find((p) => p.origin === 'vortex');
    const client = e.positions.filter((p) => p.authority === 'client_code');
    return ours && client.length > 0 && client.some((c) => c.fields !== ours.fields);
  })
  .map((e) => e.subject)
  .sort();

// ---------------------------------------------------------------------------------------------
// Second ratchet: the call-site arity the conflict machinery cannot see.
//
// ConflictDetector excludes partial layouts on purpose (Vortex.Specs/Reasoning/ConflictDetector.cs:96)
// -- a reader that stopped early has claimed nothing, so comparing its count would be noise. But when
// the client scanner fails to resolve an outgoing composer's `push` calls it records exactly that
// shape: `field_count: 0, partial: true` for authority `client_code`. The comparison above then finds
// no client position to disagree with, and the packet is silently exempt from the only wire check
// there is.
//
// The scanner does still resolve the CALL SITE, and writes the arity into the evidence note:
//     "...RoomSettingsCtrl.as:1511 with 2 argument(s); the class writes 0 value(s) to the wire"
// Those two numbers sit in the same file and nothing compared them. That is how `UnbanUserFromRoom`
// (client 2 args, our parser 1) and `RemoveAllRights` (client 1, ours 0) both shipped reading the
// wrong room, with the right answer already written down in docs/habbo-specs.
//
// Arity is not a field count, so the rule is narrow -- measured over all 297 blind-bodied incoming
// notes, only these two shapes exist:
//
//   - ours >= args (227): normal and expected. A composer constructor pushes literals the caller
//     never passes -- SetChatPreferences takes 3 args and pushes a leading `false` (4 fields, and 4
//     is right); WiredSetPreferences takes 6 and pushes a hardcoded 0; SaveRoomSettings takes ONE
//     argument, a data object, and pushes 25 values. Never flagged.
//   - ours < args (70): we read fewer values than the client hands its composer. THIS LIST IS NOT
//     NOISE. It is baselined so a new entry fails the gate, but the entries in it are unread work,
//     not known-benign artifacts. Of the 11 read so far -- the ones where we already parse
//     something, so a short read loses real data -- SEVEN were real:
//         ConfirmPetBreeding   client 4, we read 1  (int, String, int, int)
//         BreedPets            client 3 ints, we read 2
//         GetMarketplaceOffers client 5, we read 4  (trailing Boolean dropped)
//         CallForHelpFrom{ForumMessage,ForumThread,IM,Photo}: every one drops the two trailing
//           strings the client fills for unlawful-category reports -- the reporter's name and
//           email (help_message_name / help_message_email, TopicsFlowHelpController.as:481).
//           The plain CallForHelp parser reads them; these four are the odd ones out.
//     Only three of the eleven were arity artifacts, and they are worth knowing as shapes:
//         MoveWallItem   ctor takes 3 params and pushes param1 and param3 -- param2 never ships.
//         ModAlert, GetMarketplaceItemStats: a trailing field pushed only inside an `if`.
//     The ~59 remaining entries all read 0 on our side. Some are a parser that delegates to a base
//     class (the wired Update* family: UpdateActionMessageParser is 4 lines over
//     UpdateWiredDataParser, so our analyzer counts nothing) -- the mirror of the client-side
//     blindness above. The rest have not been read. Do not treat a baselined entry as cleared.
const arityBaselineFile = path.join(root, 'scripts', 'hooks', 'wire-arity-baseline.json');
const specsDir = path.join(root, 'docs', 'habbo-specs', 'packets', 'incoming');

const callSiteNote = /with (\d+) argument\(s\); the class writes (\d+) value\(s\) to the wire/;
const vortexLayout = /- origin: vortex\n\s+authority: vortex_emulator\n\s+field_count: (\d+)/;
const clientOrigins = /- origin: "([^"]+)"\n\s+authority: client_code\b/g;

// A packet can carry a call-site note from more than one client build -- 48 of them do, because the
// 2016 PRODUCTION swf is scanned alongside the one this emulator targets, and BOTH are recorded with
// authority `client_code`. Reading the first note in the file compares our parser against whichever
// build happens to be listed first, which for `groups/GetThread` meant flagging a parser that is
// correct: the 2016 composer takes 4 arguments, the current one takes 2, and we read 2.
//
// Pick the newest build instead of hardcoding a revision here, so this keeps working when the target
// client moves: the origin carries the build timestamp (`as3:WIN63-202607011411-782849652`), so the
// greatest one wins. Falls back to the first note when a packet has no client_code layout at all.
const buildStamp = (origin) => {
  const digits = /(\d{8,14})/.exec(origin);
  return digits ? Number(digits[1].slice(0, 12).padEnd(12, '0')) : 0;
};

const callSiteArity = (text) => {
  const origins = [...text.matchAll(clientOrigins)].map((m) => m[1]);

  if (origins.length > 0) {
    const newest = origins.reduce((a, b) => (buildStamp(b) > buildStamp(a) ? b : a));

    for (const block of text.split(/\n {2}- id: /)) {
      if (block.includes(`origin: "${newest}"`)) {
        const scoped = callSiteNote.exec(block);
        if (scoped) return scoped;
      }
    }
  }

  return callSiteNote.exec(text);
};

const walk = (dir) =>
  fs.existsSync(dir)
    ? fs.readdirSync(dir, { withFileTypes: true }).flatMap((e) => {
        const full = path.join(dir, e.name);
        return e.isDirectory() ? walk(full) : e.name.endsWith('.yaml') ? [full] : [];
      })
    : [];

const specFiles = walk(specsDir);
let notesSeen = 0;
const underRead = [];

for (const file of specFiles) {
  const text = fs.readFileSync(file, 'utf8');
  const note = callSiteArity(text);
  if (!note) continue;

  const args = Number(note[1]);
  const writes = Number(note[2]);
  notesSeen += 1;

  // writes > 0 means the scanner read the composer body fine, and the normal field-count
  // comparison above already covers the packet.
  if (writes !== 0 || args === 0) continue;
  if (!/\bmapped_in_vortex: true\b/.test(text)) continue;

  const ours = vortexLayout.exec(text);
  if (!ours || Number(ours[1]) >= args) continue;

  underRead.push(`${path.relative(specsDir, file).replace(/\\/g, '/').replace(/\.yaml$/, '')} (client passes ${args}, we read ${ours[1]})`);
}

underRead.sort();

// Same reasoning as the client_code guard above: without the client checkout there are no call-site
// notes at all, and reporting OK for that would be worse than saying nothing.
const arityBlind = specFiles.length > 0 && notesSeen === 0;

if (update) {
  fs.writeFileSync(baselineFile, `${JSON.stringify({ subjects: againstClient }, null, 2)}\n`);
  console.error(`check-wire-conflicts: baseline written (${againstClient.length} disagreements with the client).`);
  if (!arityBlind) {
    fs.writeFileSync(arityBaselineFile, `${JSON.stringify({ subjects: underRead }, null, 2)}\n`);
    console.error(`check-wire-conflicts: arity baseline written (${underRead.length} under-reads).`);
  }
  process.exit(0);
}

if (!fs.existsSync(baselineFile)) {
  console.error(`check-wire-conflicts: no baseline at ${path.relative(root, baselineFile)}. Run with --update.`);
  process.exit(2);
}

let failed = false;

const baseline = new Set(JSON.parse(fs.readFileSync(baselineFile, 'utf8')).subjects);
const added = againstClient.filter((s) => !baseline.has(s));
const fixed = [...baseline].filter((s) => !againstClient.includes(s));

for (const s of fixed) console.error(`warning: ${s} no longer disagrees with the client -- run --update to lock it in`);

if (added.length) {
  console.error(`\nNew wire disagreement with the official client (${added.length}):`);
  for (const s of added) {
    const entry = entries.find((e) => e.subject === s);
    console.error(`  - ${s}`);
    for (const p of entry.positions) console.error(`      ${p.origin} (${p.authority}): ${p.fields} fields`);
  }
  console.error(
    '\nOur field count must match the client class that parses these bytes. Audit it against the AS3\n' +
      'source (.claude/agents/wire-truth-auditor.md), then fix the serializer -- or, if the client is\n' +
      'the one that is wrong, record why and run this script with --update.'
  );
  failed = true;
}

if (arityBlind) {
  console.error(
    'check-wire-conflicts: arity check skipped -- no call-site notes in the specs. The client sources\n' +
      'are not checked out beside this repository, so no composer arity was ever recorded.'
  );
} else if (!fs.existsSync(arityBaselineFile)) {
  console.error(`check-wire-conflicts: no arity baseline at ${path.relative(root, arityBaselineFile)}. Run with --update.`);
  failed = true;
} else {
  const arityBaseline = new Set(JSON.parse(fs.readFileSync(arityBaselineFile, 'utf8')).subjects);
  const arityAdded = underRead.filter((s) => !arityBaseline.has(s));
  const arityFixed = [...arityBaseline].filter((s) => !underRead.includes(s));

  for (const s of arityFixed) console.error(`warning: ${s} no longer under-reads -- run --update to lock it in`);

  if (arityAdded.length) {
    console.error(`\nOur parser reads fewer values than the client's composer is handed (${arityAdded.length}):`);
    for (const s of arityAdded) console.error(`  - ${s}`);
    console.error(
      '\nThe client scanner could not resolve these composer bodies, so the field-count check above is\n' +
        'blind to them and this arity note is the only evidence there is. Open the AS3 composer class\n' +
        'named in the spec evidence and count its pushes: either our parser is short a field, or the\n' +
        'constructor takes an argument it does not put on the wire -- record which, and --update.'
    );
    failed = true;
  }
}

if (failed) process.exit(2);

console.error(
  `check-wire-conflicts: OK (${againstClient.length} known disagreements with the client, ${entries.length} field-count conflicts total; ` +
    `${underRead.length} known call-site under-reads over ${notesSeen} composer notes).`
);
