#!/usr/bin/env node
// The boundaries CONTEXT.md states in prose, made executable.
//
// Seven rules that no compiler, test or grep enforces today, all of them currently respected --
// which is what makes encoding them cheap: only a state to hold. A rule that lives in a document is
// a rule that erodes on the first busy afternoon; the stale project references this repository
// carried for months are the proof, and wall 7 was already breached in 205 places when it was
// written, silently, for as long as the pet system has existed.
//
//   node scripts/hooks/check-architecture-walls.mjs
//   node scripts/hooks/check-architecture-walls.mjs --update   # re-baseline leaks and the
//                                                              # protocol-free project list
//
// Exit 2 = a new violation. Exit 0 = the walls still stand.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
// The override exists so the hook test can point the check at an empty baseline and see it block.
const baselineFile =
  process.env.VORTEX_WALLS_BASELINE ??
  path.join(root, 'scripts', 'hooks', 'architecture-walls-baseline.json');
const update = process.argv.includes('--update');

/** Source files of a project, minus build output. */
function sources(project) {
  const dir = path.join(root, project);
  if (!fs.existsSync(dir)) return [];
  const out = [];
  const walk = (d) => {
    for (const e of fs.readdirSync(d, { withFileTypes: true })) {
      if (e.name === 'obj' || e.name === 'bin') continue;
      const p = path.join(d, e.name);
      if (e.isDirectory()) walk(p);
      else if (e.name.endsWith('.cs')) out.push(p);
    }
  };
  walk(dir);
  return out;
}

const rel = (p) => path.relative(root, p).split(path.sep).join('/');

// A mention inside a comment is prose about the rule, not a breach of it -- the emulator guard hook
// learned this the hard way when a commit message describing it blocked that very commit.
const isComment = (line) => /^\s*(\/\/|\/\*|\*)/.test(line);

/** Lines of `project` matching `pattern`, ignoring comments. */
function hits(project, pattern, skip = () => false) {
  const found = [];
  for (const file of sources(project)) {
    if (skip(rel(file))) continue;
    const lines = fs.readFileSync(file, 'utf8').split(/\r?\n/);
    lines.forEach((line, i) => {
      if (!isComment(line) && pattern.test(line)) {
        found.push(`${rel(file)}:${i + 1}  ${line.trim().slice(0, 100)}`);
      }
    });
  }
  return found;
}

const failures = [];

// ---------------------------------------------------------------------------------------------
// Wall 1 -- packet handlers stay orchestration-only.
//
// CONTEXT.md: handlers do not query database contexts or repositories. They route to a grain and
// nothing else. Currently zero violations across 536 handler files; this holds that at zero.
// ---------------------------------------------------------------------------------------------
const handlerDb = hits(
  'Vortex.PacketHandlers',
  /\b(IDbContextFactory|VortexDbContext|SaveChangesAsync)\b/
);
if (handlerDb.length > 0) {
  failures.push([
    'A packet handler reaches for the database. Handlers orchestrate; the grain owns the state.',
    handlerDb,
  ]);
}

// ---------------------------------------------------------------------------------------------
// Wall 2 -- only the authoring services persist, and they own what they persist.
//
// This used to forbid SaveChangesAsync anywhere in Vortex.Dashboard.API, and that rule was simply
// wrong: it reported 146 violations, every one of them an *AdminService doing exactly its job.
// docs/architecture/dashboard-architecture.md §11-13 states the real rule -- what matters is who
// owns the mutable state, not whether the call happens to be in this assembly. Catalogue rows,
// furniture definitions and poll content are owned by the authoring services; a grain's live state
// is not, and that distinction is not something a regex can see.
//
// So this wall guards the part that IS mechanical, and that the architecture actually holds today:
// persistence lives in Admin/ and nowhere else. A Reads class that starts writing breaks §26.3
// ("reads do not modify state"); an Operations class that persists directly has gone around the
// authoring service that owns the data. Both are real regressions, and both are invisible to the
// compiler.
//
// The ownership rule above stays prose on purpose: an executable rule that does not match the
// architecture pushes the code towards the wrong shape, which is worse than no rule at all (§25).
// ---------------------------------------------------------------------------------------------
const dashboardWrites = hits(
  'Vortex.Dashboard.API',
  /\bSaveChangesAsync\b/,
  (file) => file.startsWith('Vortex.Dashboard.API/Admin/')
);
if (dashboardWrites.length > 0) {
  failures.push([
    'The dashboard persists outside Admin/. Reads never write, and an operation goes through the ' +
      'authoring service that owns the data.',
    dashboardWrites,
  ]);
}

// ---------------------------------------------------------------------------------------------
// Wall 5 -- a password is verified in exactly one place.
//
// Vortex.WebApi used to carry its own copy of the credential check: same player_accounts row, same
// BCrypt call, same constant-time dummy hash. Two copies read as harmless duplication right up until
// the day one of them gained a second factor and the other did not -- and the one that did not was
// the login that issues SSO tickets into the game. Verification now lives only in
// Vortex.WebApi.Authentication, behind IAccountAuthenticator, so a factor added there reaches every caller.
//
// Hashing is not covered: registration legitimately creates a hash where the account is created.
// It is the *checking* that must not fork.
// ---------------------------------------------------------------------------------------------
const verifiers = fs
  .readdirSync(root, { withFileTypes: true })
  .filter((e) => e.isDirectory() && e.name.startsWith('Vortex.') && !e.name.endsWith('.Tests'))
  .filter((e) => e.name !== 'Vortex.WebApi.Authentication')
  .flatMap((e) => hits(e.name, /BCrypt[\w.]*\.Verify\s*\(/));
if (verifiers.length > 0) {
  failures.push([
    'A password is verified outside Vortex.WebApi.Authentication. Go through IAccountAuthenticator, or the '
      + 'second factor will guard one login and not the other.',
    verifiers,
  ]);
}

// ---------------------------------------------------------------------------------------------
// Wall 6 -- a RoomGrain partial stays a facade.
//
// The grain is a core plus ~28 partials, and the architecture note's word for what those partials
// are is "facades": they take a call, hand it to a Module or a System, and hold no behaviour of
// their own. That is the property; these are the two halves of it that a script can hold.
//
// The exact half: a concrete furniture behaviour is a class carrying [RoomObjectLogic("...")] and
// it lives under Object/Logic. One declared inside the grain would be a behaviour nobody can test
// without a room, which is the whole thing the wired extraction was for.
//
// The proxy half: size. A facade that reaches nine hundred lines has stopped being one whatever it
// contains, and no rule that reads code can say that as cheaply. The ceiling is not a target --
// RoomGrain.Settings.cs is at 847 and is the file to watch. Crossing it means the partial has a
// System inside it waiting to be lifted out, not that the number needs raising.
// ---------------------------------------------------------------------------------------------
const FACADE_CEILING = 900;

const grainLogics = hits('Vortex.Rooms/Grains', /\[RoomObjectLogic\s*\(/);
if (grainLogics.length > 0) {
  failures.push([
    'A concrete furniture logic is declared inside a RoomGrain partial. It belongs under '
      + 'Object/Logic, where it can be built without a room.',
    grainLogics,
  ]);
}

const oversized = sources('Vortex.Rooms/Grains')
  .filter((f) => path.basename(f).startsWith('RoomGrain'))
  .map((f) => [f, fs.readFileSync(f, 'utf8').split(/\r?\n/).length])
  .filter(([, lines]) => lines > FACADE_CEILING)
  .map(([f, lines]) => `${path.relative(root, f)} (${lines} lines)`);
if (oversized.length > 0) {
  failures.push([
    `A RoomGrain partial is past ${FACADE_CEILING} lines. It is not a facade any more -- lift the `
      + 'behaviour into a Module or a System rather than raising the ceiling.',
    oversized,
  ]);
}

// ---------------------------------------------------------------------------------------------
// Wall 3 -- protocol does not leak into contracts.
//
// Vortex.Primitives is referenced by every project in the solution; Vortex.Protocol holds the 1,092
// message records and is the highest-churn layer there is. While the two were one project, editing a
// composer rebuilt everything, and a grain interface typed in a wire record made the client's byte
// layout the domain model. Ten files were in that state and the baseline ratcheted them to zero, so
// this is now a hard zero rather than a ratchet: the hub does not reference the protocol at all.
//
// The pattern matches Vortex.Protocol, not the old Vortex.Primitives.Messages. That rename is
// exactly how this check could have died without anyone noticing -- it would have matched nothing
// and reported the walls holding, which is why the hook test drives it with a real violation.
// ---------------------------------------------------------------------------------------------
const leak = hits('Vortex.Primitives', /^\s*using\s+Vortex\.Protocol\b/).map((h) =>
  h.split(':')[0]
);
const leakFiles = [...new Set(leak)].sort();

// ---------------------------------------------------------------------------------------------
// Wall 4 -- the projects that hold no protocol keep holding none.
//
// Splitting Vortex.Protocol out of the hub is worth a measured 74s -> 17s on a composer edit,
// because the projects below stop rebuilding entirely: they contain no wire types at all. That win
// is one ProjectReference away from being handed back, and nothing about giving it back is visible
// -- the build simply gets slower again. So the list is held rather than trusted.
//
// A project earns its place here by not needing the protocol, not by deserving to. When one
// genuinely starts speaking the wire, take it off the list on purpose and know what it costs.
// ---------------------------------------------------------------------------------------------
const projectsWithProtocol = fs
  .readdirSync(root, { withFileTypes: true })
  .filter((e) => e.isDirectory() && e.name.startsWith('Vortex.'))
  .filter((e) => fs.existsSync(path.join(root, e.name, `${e.name}.csproj`)))
  .filter((e) =>
    fs
      .readFileSync(path.join(root, e.name, `${e.name}.csproj`), 'utf8')
      .includes('Vortex.Protocol.csproj')
  )
  .map((e) => e.name);

const protocolFree = fs.existsSync(baselineFile)
  ? (JSON.parse(fs.readFileSync(baselineFile, 'utf8')).protocolFree ?? [])
  : [];
const regressed = protocolFree.filter((p) => projectsWithProtocol.includes(p));
if (regressed.length > 0) {
  failures.push([
    'A protocol-free project now references Vortex.Protocol. Every composer edit rebuilds it again.',
    regressed,
  ]);
}

// ---------------------------------------------------------------------------------------------
// Wall 7 -- grain code stays on its own scheduler.
//
// AGENTS.md says grain state needs no locks because Orleans runs one turn at a time per activation.
// `ConfigureAwait(false)` is an explicit opt-out of the current TaskScheduler, so in grain code the
// continuation resumes on the thread pool and everything after that await runs outside the turn --
// concurrently with the next one. RoomPetSystem had 184 of them, several with a `_state` mutation
// on the very next line.
//
// Nothing else sees this. CA2007 is off in .editorconfig, and ORLEANS0014 only looks at classes
// that implement IGrain -- the Modules and Systems here are plain helpers holding a RoomGrain, so
// the analyser reports zero while the escape is real. Outside grain code ConfigureAwait(false) is
// correct and there are ~3,200 of them; this wall covers only what runs on an activation.
// ---------------------------------------------------------------------------------------------
const grainDirs = fs
  .readdirSync(root, { withFileTypes: true })
  .filter((e) => e.isDirectory() && e.name.startsWith('Vortex.') && !e.name.endsWith('.Tests'))
  .map((e) => `${e.name}/Grains`)
  .filter((d) => fs.existsSync(path.join(root, d)));

const schedulerEscapes = grainDirs.flatMap((d) =>
  hits(d, /\.ConfigureAwait\s*\(\s*false\s*\)/)
);
if (schedulerEscapes.length > 0) {
  failures.push([
    'Grain code opts out of its own scheduler. Drop the ConfigureAwait(false): the continuation '
      + 'would resume off the activation and void the one-turn-at-a-time guarantee the state relies on.',
    schedulerEscapes,
  ]);
}

// ---------------------------------------------------------------------------------------------
// Wall 8 -- the layers point one way.
//
// The RPG spec (§3.4 bis) cuts the solution into Core (network, protocol, persistence, plumbing),
// Platform (the game's domains), Web and Operations. Core references no Platform, Web or Operations
// project; Platform references neither Web nor Operations. Vortex.Main is the host and wires them
// all, so it is exempt, and so are test projects. A project outside the four lists is not judged
// until it is given a layer.
// ---------------------------------------------------------------------------------------------
const CORE = [
  'Networking', 'Pipeline', 'Protocol', 'Messages', 'Revisions', 'PacketHandlers', 'Runtime',
  'Crypto', 'Database', 'Events', 'Logging', 'Observability', 'Plugins', 'Primitives',
];
const PLATFORM = [
  'Rooms', 'Players', 'Inventory', 'Furniture', 'Social', 'Marketplace', 'Signals', 'Progression',
  'Fishing', 'Catalog', 'Permissions', 'Navigator',
];
const layerOf = (project) => {
  const name = project.replace(/^Vortex\./, '');
  if (CORE.includes(name)) return 'Core';
  if (PLATFORM.includes(name)) return 'Platform';
  if (name.startsWith('WebApi')) return 'Web';
  if (name.startsWith('Dashboard.')) return 'Operations';
  return null;
};
const FORBIDDEN = { Core: ['Platform', 'Web', 'Operations'], Platform: ['Web', 'Operations'] };

const layerBreaches = fs
  .readdirSync(root, { withFileTypes: true })
  .filter((e) => e.isDirectory() && e.name.startsWith('Vortex.') && !e.name.endsWith('.Tests'))
  .filter((e) => fs.existsSync(path.join(root, e.name, `${e.name}.csproj`)))
  .flatMap((e) => {
    const from = layerOf(e.name);
    if (!from || !FORBIDDEN[from]) return [];
    const csproj = fs.readFileSync(path.join(root, e.name, `${e.name}.csproj`), 'utf8');
    return [...csproj.matchAll(/<ProjectReference\s+Include="[^"]*?(Vortex\.[\w.]+)\.csproj"/g)]
      .map((m) => m[1])
      .filter((to) => FORBIDDEN[from].includes(layerOf(to)))
      .map((to) => `${e.name} (${from}) -> ${to} (${layerOf(to)})`);
  });
if (layerBreaches.length > 0) {
  failures.push([
    'A project references a layer above its own. Core knows no game domain, and no domain knows '
      + 'the web or the dashboard -- move the shared type down into Vortex.Primitives instead.',
    layerBreaches,
  ]);
}

if (update) {
  const free = fs
    .readdirSync(root, { withFileTypes: true })
    .filter((e) => e.isDirectory() && e.name.startsWith('Vortex.'))
    .filter((e) => fs.existsSync(path.join(root, e.name, `${e.name}.csproj`)))
    .map((e) => e.name)
    .filter((n) => !projectsWithProtocol.includes(n))
    .sort();
  fs.writeFileSync(
    baselineFile,
    JSON.stringify({ protocolLeak: leakFiles, protocolFree: free }, null, 2) + '\n'
  );
  console.log(
    `Baseline updated: ${leakFiles.length} leaking file(s), ${free.length} protocol-free project(s).`
  );
  process.exit(0);
}

const baseline = fs.existsSync(baselineFile)
  ? JSON.parse(fs.readFileSync(baselineFile, 'utf8')).protocolLeak
  : [];
const known = new Set(baseline);
const newLeak = leakFiles.filter((f) => !known.has(f));
if (newLeak.length > 0) {
  failures.push([
    'A contracts file imports the wire protocol. Pass a snapshot, not a message record.',
    newLeak,
  ]);
}

if (failures.length === 0) {
  const healed = baseline.filter((f) => !leakFiles.includes(f));
  const note = healed.length > 0 ? ` (${healed.length} baselined leak(s) now gone -- --update)` : '';
  console.log(`Architecture walls hold: 8 checked, ${leakFiles.length} baselined leak(s)${note}.`);
  process.exit(0);
}

for (const [message, lines] of failures) {
  console.error(`\n${message}`);
  for (const line of lines) console.error(`  ${line}`);
}
console.error('');
process.exit(2);
