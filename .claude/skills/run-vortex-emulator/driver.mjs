#!/usr/bin/env node
// Driver for the Vortex RPG emulator. Launches Vortex.Main and pokes its two live surfaces: the
// game socket (raw Habbo frames on :40000) and the web API (:8080, registration and SSO tickets).
//
// Zero dependencies on purpose -- node:net, node:http and node:child_process.
//
// Run state (pid file, console log) lives in
// %TEMP%/vortex-run/ -- printed by `status`, never inside the repo.

import { spawn, spawnSync, execFileSync } from 'node:child_process';
import fs from 'node:fs';
import http from 'node:http';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const SKILL_DIR = path.dirname(fileURLToPath(import.meta.url));
const REPO = path.resolve(SKILL_DIR, '../../..'); // .claude/skills/run-vortex-emulator -> repo root
// A second instance (VORTEX_MAIN_EXE) keeps its own pid and log, or `stop` would aim at the first.
const STATE = path.join(os.tmpdir(), process.env.VORTEX_MAIN_EXE ? 'vortex-run-alt' : 'vortex-run');
const PID_FILE = path.join(STATE, 'vortex-main.pid');
const LOG_FILE = path.join(STATE, 'vortex-main.log');

// appsettings.Development.json, when present, moves the game sockets off the 30000/30001 of
// appsettings.json. VORTEX_GAME_PORT names the port when it was moved another way (the unprefixed
// serverOptions__TcpServer__listeners__0__port variable).
const GAME_TCP =
  Number(process.env.VORTEX_GAME_PORT) ||
  (fs.existsSync(path.join(REPO, 'appsettings.Development.json')) ? 40000 : 30000);
// A second instance beside a running one (VORTEX_MAIN_EXE built elsewhere, its own ports) moves the
// web API too: two listeners cannot share 8080.
const WEBAPI = Number(process.env.VORTEX_WEBAPI_PORT) || 8080;
const log = (...a) => console.log(...a);
const die = (msg) => {
  console.error(msg);
  process.exit(1);
};

fs.mkdirSync(STATE, { recursive: true });

// ---------------------------------------------------------------- small helpers

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function portOpen(port, host = '127.0.0.1', timeout = 500) {
  return new Promise((resolve) => {
    const s = net.connect({ port, host });
    const done = (v) => {
      s.destroy();
      resolve(v);
    };
    s.setTimeout(timeout);
    s.once('connect', () => done(true));
    s.once('error', () => done(false));
    s.once('timeout', () => done(false));
  });
}

function request(url, { method = 'GET', body = null, headers = {} } = {}) {
  return new Promise((resolve, reject) => {
    const u = new URL(url);
    const req = http.request(
      {
        hostname: u.hostname,
        port: u.port,
        path: u.pathname + u.search,
        method,
        headers: body
          ? { 'content-type': 'application/json', 'content-length': Buffer.byteLength(body), ...headers }
          : headers,
      },
      (res) => {
        const chunks = [];
        res.on('data', (c) => chunks.push(c));
        res.on('end', () =>
          resolve({
            status: res.statusCode,
            headers: res.headers,
            body: Buffer.concat(chunks).toString('utf8'),
          }),
        );
      },
    );
    req.on('error', reject);
    if (body) req.write(body);
    req.end();
  });
}

// Laragon ships MySQL under a versioned directory; the version changes on every Laragon update, so
// glob it rather than hardcoding 8.4.3.
function mysqlExe() {
  if (process.env.VORTEX_MYSQL) return process.env.VORTEX_MYSQL;
  const root = 'C:\\laragon\\bin\\mysql';
  if (!fs.existsSync(root)) return null;
  for (const dir of fs.readdirSync(root)) {
    const exe = path.join(root, dir, 'bin', 'mysql.exe');
    if (fs.existsSync(exe)) return exe;
  }
  return null;
}

// The database the emulator itself uses: Vortex.Main's user-secrets connection string when it has
// one (the fork runs on its own DB, never on `turbo`), else the old turbo/admin default.
let dbConn;
function db() {
  if (dbConn) return dbConn;
  const out = spawnSync('dotnet', ['user-secrets', 'list', '--project', path.join(REPO, 'Vortex.Main')], {
    encoding: 'utf8',
  });
  // The emulator reads VORTEX__Vortex__Database__ConnectionString before user-secrets; so does this,
  // or a run pointed at a scratch database would query the owner's.
  const cs =
    process.env.VORTEX__Vortex__Database__ConnectionString ??
    (out.stdout || '').match(/Vortex:Database:ConnectionString = (.*)/)?.[1] ??
    '';
  const part = (k) => cs.match(new RegExp(`(?:^|;)\\s*${k}=([^;]*)`, 'i'))?.[1];
  dbConn = { name: part('Database') ?? 'turbo', password: part('Password') ?? 'admin' };
  return dbConn;
}

function sql(query) {
  const exe = mysqlExe();
  if (!exe) die('No mysql client found. Set VORTEX_MYSQL to its full path.');
  const { name, password } = db();
  const out = spawnSync(exe, ['-h127.0.0.1', '-uroot', name, '-N', '-B', '-e', query], {
    encoding: 'utf8',
    env: { ...process.env, MYSQL_PWD: password },
  });
  if (out.status !== 0) die(`mysql failed: ${out.stderr.trim()}`);
  return out.stdout.trim();
}

function runningPid() {
  // A pid file is only evidence; the process behind it may be long gone.
  if (!fs.existsSync(PID_FILE)) return null;
  const pid = Number(fs.readFileSync(PID_FILE, 'utf8').trim());
  try {
    process.kill(pid, 0);
    return pid;
  } catch {
    return null;
  }
}

function foreignPids() {
  // Vortex.Main instances this driver did not start -- typically the one the user is running.
  const mine = runningPid();
  const out = spawnSync(
    'powershell',
    ['-NoProfile', '-Command', "Get-Process -Name 'Vortex.Main' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id"],
    { encoding: 'utf8' },
  );
  return (out.stdout || '')
    .split(/\r?\n/)
    .map((l) => Number(l.trim()))
    .filter((n) => n > 0 && n !== mine);
}

// ---------------------------------------------------------------- mysql

async function cmdMysql() {
  if (await portOpen(3306)) return log('MySQL already listening on 3306.');
  const root = 'C:\\laragon\\bin\\mysql';
  const dir = fs.existsSync(root) ? fs.readdirSync(root)[0] : null;
  if (!dir) die('Laragon MySQL not found under C:\\laragon\\bin\\mysql.');
  const mysqld = path.join(root, dir, 'bin', 'mysqld.exe');
  const ini = path.join(root, dir, 'my.ini');
  spawn(mysqld, [`--defaults-file=${ini}`], { detached: true, stdio: 'ignore' }).unref();
  // Laragon's mysqld spawns a child and takes well over 10s to listen on a cold start.
  for (let i = 0; i < 120; i++) {
    await sleep(500);
    if (await portOpen(3306)) return log(`MySQL up on 3306 after ~${Math.ceil((i + 1) / 2)}s (${mysqld}).`);
  }
  die('MySQL did not come up within 60s.');
}

// ---------------------------------------------------------------- lifecycle

function exePath() {
  // Built elsewhere (`dotnet build Vortex.Main -o <dir>`) while a running instance holds bin/.
  if (process.env.VORTEX_MAIN_EXE) return fs.existsSync(process.env.VORTEX_MAIN_EXE) ? process.env.VORTEX_MAIN_EXE : null;
  for (const cfg of ['Debug', 'Release']) {
    const p = path.join(REPO, 'Vortex.Main', 'bin', cfg, 'net10.0', 'Vortex.Main.exe');
    if (fs.existsSync(p)) return p;
  }
  return null;
}

async function cmdStart() {
  const foreign = foreignPids();
  // VORTEX_MAIN_EXE means a separate instance on its own ports: the running one is left alone.
  if (foreign.length && !process.env.VORTEX_MAIN_EXE) {
    // Never stop it. It is the user's live-verification hotel, with activated RoomGrains and
    // connected sessions behind it.
    log(`Vortex.Main already running (pid ${foreign.join(', ')}) and NOT started by this driver.`);
    log('Reusing it. Do not stop it -- drive it with `ping`, `health`, `play`.');
    return;
  }
  if (runningPid()) return log(`Already started by this driver (pid ${runningPid()}).`);

  if (!(await portOpen(3306))) await cmdMysql();

  const exe = exePath();
  if (!exe) die('No Vortex.Main.exe. Build first: dotnet build Vortex.Main/Vortex.Main.csproj');

  const fd = fs.openSync(LOG_FILE, 'w');
  const child = spawn(exe, [], {
    cwd: path.dirname(exe),
    detached: true,
    stdio: ['ignore', fd, fd],
    env: { ...process.env, DOTNET_ENVIRONMENT: 'Development' },
  });
  child.unref();
  fs.writeFileSync(PID_FILE, String(child.pid));
  log(`Spawned Vortex.Main pid ${child.pid}; log -> ${LOG_FILE}`);

  // Startup: Orleans silo, furniture definitions and room models warm-up, plugin scan.
  for (let i = 0; i < 180; i++) {
    await sleep(1000);
    if (!runningPid()) {
      log(fs.readFileSync(LOG_FILE, 'utf8').split(/\r?\n/).slice(-30).join('\n'));
      die('Vortex.Main exited during startup (log tail above).');
    }
    if ((await portOpen(GAME_TCP)) && (await portOpen(WEBAPI))) {
      return log(`Ready after ~${i + 1}s: game :${GAME_TCP}, webapi :${WEBAPI}`);
    }
  }
  die('Timed out after 180s waiting for the listeners.');
}

async function cmdStatus() {
  const mine = runningPid();
  const foreign = foreignPids();
  log(`driver-started pid : ${mine ?? '(none)'}`);
  log(`other Vortex.Main  : ${foreign.length ? foreign.join(', ') + '  <- do NOT stop' : '(none)'}`);
  for (const [name, port] of [
    ['mysql   ', 3306],
    ['game tcp', GAME_TCP],
    ['webapi  ', WEBAPI],
  ]) {
    log(`  ${name} :${port}  ${(await portOpen(port)) ? 'open' : 'closed'}`);
  }
  log(`state dir : ${STATE}`);
}

function cmdStop() {
  const pid = runningPid();
  if (!pid) {
    const foreign = foreignPids();
    if (foreign.length)
      die(`Refusing: Vortex.Main (pid ${foreign.join(', ')}) was not started by this driver. Only the user stops that one.`);
    return log('Nothing to stop.');
  }
  execFileSync('taskkill', ['/PID', String(pid), '/T', '/F'], { stdio: 'ignore' });
  fs.rmSync(PID_FILE, { force: true });
  log(`Stopped pid ${pid}.`);
}

function cmdLogs(n = 40) {
  if (!fs.existsSync(LOG_FILE)) return log('(no log -- this driver has not started the emulator)');
  log(fs.readFileSync(LOG_FILE, 'utf8').split(/\r?\n/).slice(-Number(n)).join('\n'));
}


// ---------------------------------------------------------------- game socket

// Wire frame: [int32 BE length][int16 BE header][body]. `length` covers the header and the body.
// Plaintext until the Diffie handshake completes -- SetupEncryption is what installs the RC4
// engines, so a session that never completes it never encrypts anything.
function frame(header, ...fields) {
  const parts = [];
  for (const f of fields) {
    if (typeof f === 'string') {
      const s = Buffer.from(f, 'utf8');
      const len = Buffer.alloc(2);
      len.writeInt16BE(s.length, 0);
      parts.push(len, s);
    } else {
      const n = Buffer.alloc(4);
      n.writeInt32BE(f, 0);
      parts.push(n);
    }
  }
  const body = Buffer.concat([Buffer.from([(header >> 8) & 0xff, header & 0xff]), ...parts]);
  const out = Buffer.alloc(4 + body.length);
  out.writeInt32BE(body.length, 0);
  body.copy(out, 4);
  return out;
}

// Headers of Revision20260701 (Vortex.Revisions/Revision20260701/Headers.cs).
const H = {
  initDiffie: 2022, // -> InitDiffieHandshakeComposer
  initDiffieReply: 3309,
  sso: 882,
  authOk: 230,
  userRights: 3599,
  roomForward: 3339,
  openFlat: 3234,
  cantConnect: 2430,
  roomReady: 2349,
  entryTile: 2792,
  heightMap: 2260,
  floorHeightMap: 2885,
  users: 996,
  objects: 2104,
  userUpdate: 2613,
  moveAvatar: 2364,
  notificationDialog: 2243,
  useFurniture: 3353,
  // RPG (spec §3.7): incoming and outgoing are separate id spaces.
  rpgListRequest: 9000,
  rpgCreate: 9001,
  rpgChooseClass: 9002,
  rpgEnterWorld: 9003,
  rpgCharacterList: 9000,
  rpgClassList: 9001,
  rpgResult: 9002,
  rpgState: 9003,
  rpgProgress: 9004,
  rpgStats: 9005,
  // Items (spec Objets §17).
  rpgItemEquip: 9006,
  rpgItemUnequip: 9007,
  rpgInventory: 9006,
  rpgItemResult: 9007,
  // Interaction (spec F3b §7).
  rpgZoneInteractives: 9008,
  rpgTarget: 9009,
  rpgTargetSelect: 9010,
  // Chat (spec F3c §10).
  rpgChatSend: 9011,
  rpgChatMessage: 9010,
  rpgChatResult: 9011,
  // RP actions: in 9012, out 9017 (9012-9016 out are the M1a combat messages).
  rpgEmote: 9012,
  rpgEmoteShown: 9017,
  // Combat (spec M1a §9.2).
  rpgCombatEvents: 9012,
  rpgVitals: 9013,
  rpgSelfVitals: 9014,
  rpgDeath: 9015,
  rpgCombatResult: 9016,
  // Skills (spec M1b §10): in 9013-9015, out 9018-9024.
  rpgSkillCast: 9013,
  rpgActionBarSet: 9014,
  rpgCastCancel: 9015,
  rpgSkills: 9018,
  rpgSkillResult: 9019,
  rpgCastStart: 9020,
  rpgCastEnd: 9021,
  rpgCooldowns: 9022,
  rpgStatuses: 9023,
  rpgSkillEvents: 9024,
  // Monsters (spec M1c §9.2).
  rpgTelegraphs: 9025,
  rpgMonsterYell: 9026,
  userRemove: 3693,
  quit: 3061,
  objectDataUpdate: 2329,
  objectUpdate: 114,
};

// Reads a frame body in the client's order: int32 BE, int16-prefixed UTF-8, one-byte bool.
function reader(body) {
  let at = 0;
  return {
    int() {
      const v = body.readInt32BE(at);
      at += 4;
      return v;
    },
    str() {
      const len = body.readInt16BE(at);
      const v = body.subarray(at + 2, at + 2 + len).toString('utf8');
      at += 2 + len;
      return v;
    },
    bool() {
      return body[at++] !== 0;
    },
    left: () => body.length - at,
  };
}

// A socket that collects every frame the server sends, as { header, body }.
function gameSocket() {
  return new Promise((resolve, reject) => {
    const s = net.connect({ port: GAME_TCP, host: '127.0.0.1' });
    const frames = [];
    let buf = Buffer.alloc(0);
    s.on('error', reject);
    s.on('connect', () =>
      resolve({
        frames,
        send: (b) => s.write(b),
        close: () => s.destroy(),
        // Waits until a frame with one of `headers` arrives after index `from`, or times out.
        async wait(headers, from = 0, ms = 5000) {
          for (let t = 0; t < ms; t += 50) {
            const hit = frames.slice(from).find((f) => headers.includes(f.header));
            if (hit) return hit;
            await sleep(50);
          }
          return null;
        },
      }),
    );
    s.on('data', (chunk) => {
      buf = Buffer.concat([buf, chunk]);
      while (buf.length >= 4 && buf.length >= 4 + buf.readInt32BE(0)) {
        const len = buf.readInt32BE(0);
        frames.push({ header: buf.readInt16BE(4), body: buf.subarray(6, 4 + len) });
        buf = buf.subarray(4 + len);
      }
    });
  });
}

// The cheapest end-to-end proof the socket is alive: one frame through the decoder, the revision
// parser map, the handler, the serializer and the encoder -- no login needed. A green build proves
// none of that.
async function cmdPing() {
  const g = await gameSocket().catch((e) => die(`Socket error on :${GAME_TCP}: ${e.message}`));
  g.send(frame(H.initDiffie));
  const reply = await g.wait([H.initDiffieReply]);
  g.close();
  if (!reply) die(`No ${H.initDiffieReply} within 5s for ${H.initDiffie} on :${GAME_TCP}.`);
  log(`game socket OK: sent ${H.initDiffie} -> got ${reply.header}`);
}

// ---------------------------------------------------------------- http surfaces

async function cmdHealth() {
  const health = await request(`http://127.0.0.1:${WEBAPI}/health`);
  log(`GET :${WEBAPI}/health -> ${health.status} ${health.body}`);
  // Liveness above (always 200 while the process answers); readiness here (503 until the
  // listeners are open and the database answers, and again while the host drains).
  const ready = await request(`http://127.0.0.1:${WEBAPI}/ready`);
  log(`GET :${WEBAPI}/ready -> ${ready.status} ${ready.body}`);
  const hello = await request(`http://127.0.0.1:${WEBAPI}/api/public/info/hello`);
  log(`GET :${WEBAPI}/api/public/info/hello -> ${hello.status} ${hello.body}`);
}

// A throwaway player, kept at a fixed address so re-runs reuse it; `cleanup` deletes it.
const PLAYER = { email: 'run-skill@vortex.local', password: 'RunSkill!2026' };
// A second throwaway account, for what takes two players in a zone (`target`).
const PLAYER_B = { email: 'run-skill-b@vortex.local', password: 'RunSkill!2026' };
// A third one, made a mage for `skills`: the first keeps « Sans classe », which `items` relies on.
const PLAYER_C = { email: 'run-skill-c@vortex.local', password: 'RunSkill!2026' };
// A fourth, the mage `boss` raises to level 6: `skills` keeps its level-1 mage, whose Firebolt does
// not kill a wolf at once.
const PLAYER_D = { email: 'run-skill-d@vortex.local', password: 'RunSkill!2026' };
const WEB_COOKIE = 'habbo-web-session';

function sessionCookie(res) {
  const c = [].concat(res.headers['set-cookie'] || []).find((x) => x.startsWith(`${WEB_COOKIE}=`));
  return c ? c.split(';')[0] : null;
}

// Login, or registration when the account does not exist yet: the site's web session. Login first:
// registration allows 3 attempts per 5 minutes, and a few driver runs in a row used them all up.
const webSessions = new Map();

// One login per account and driver run: the login allows 5 per minute, and a command logs in more
// than once.
function webSession(account = PLAYER) {
  if (!webSessions.has(account.email)) webSessions.set(account.email, openWebSession(account));
  return webSessions.get(account.email);
}

async function openWebSession(account) {
  const base = `http://127.0.0.1:${WEBAPI}`;
  const json = (o) => JSON.stringify(o);
  let res = await request(`${base}/api/public/authentication/login`, {
    method: 'POST',
    body: json({ email: account.email, password: account.password }),
  });
  if (!sessionCookie(res) && res.status !== 429) {
    res = await request(`${base}/api/public/registration/new`, {
      method: 'POST',
      body: json({ email: account.email, password: account.password, passwordRepeated: account.password }),
    });
  }
  const cookie = sessionCookie(res);
  if (!cookie) die(`No ${WEB_COOKIE} cookie: ${res.status} ${res.body}`);
  return { cookie };
}

// What the client does after its login: the account ticket, then the character screen on the
// socket -- the account's first character, or a new one « Sans classe » -- entered into the world.
// With a class, the starter class is left for it first (a real class stays: a second run is told
// "already chosen"). Answers the socket past AuthenticationOK, with the RoomForward when there is a
// start zone.
async function enterWorld(account = PLAYER, classId = null) {
  const base = `http://127.0.0.1:${WEBAPI}`;
  const headers = { cookie: (await webSession(account)).cookie };
  const res = await request(`${base}/api/ssotoken`, { headers });
  if (res.status !== 200) die(`SSO ticket refused: ${res.status} ${res.body}`);

  const g = await gameSocket().catch((e) => die(`Socket error on :${GAME_TCP}: ${e.message}`));
  const fail = (m) => {
    g.close();
    die(m);
  };
  g.send(frame(H.sso, JSON.parse(res.body).ssoToken, 0));
  const list = await g.wait([H.rpgCharacterList]);
  if (!list) fail(`Account ticket -> ${g.frames.map((f) => f.header).join(', ')}: no character list.`);

  // RpgCharacterList: max, count, then per character its player id first.
  const l = reader(list.body);
  l.int();
  let playerId = l.int() > 0 ? l.int() : 0;
  if (!playerId) {
    const from = g.frames.length;
    const name = `Driver${Date.now() % 1_000_000}`;
    g.send(frame(H.rpgCreate, name, 'hd-180-1.ch-210-66.lg-270-82.sh-290-80', 'M'));
    const made = await g.wait([H.rpgResult], from);
    if (!made) fail('No RpgCharacterResult for the create.');
    const r = reader(made.body);
    const [, code, id] = [r.int(), r.str(), r.int()];
    if (code !== 'ok') fail(`Character creation refused: ${code}`);
    log(`Created character ${name} (id ${id}).`);
    playerId = id;
  }

  if (classId) {
    const asked = g.frames.length;
    g.send(frame(H.rpgChooseClass, playerId, classId));
    const chosen = await g.wait([H.rpgResult], asked);
    if (!chosen) fail(`No RpgCharacterResult for ${classId}.`);
    const r = reader(chosen.body);
    log(`Class ${classId} -> ${(r.int(), r.str())}`);
  }

  const from = g.frames.length;
  g.send(frame(H.rpgEnterWorld, playerId));
  const forward = await g.wait([H.roomForward], from);
  if (!g.frames.slice(from).some((f) => f.header === H.authOk)) fail(`RpgEnterWorld(${playerId}): no AuthenticationOK.`);
  log(`Player ${playerId} entered the world.`);
  return { g, forward, from, playerId };
}

// The core loop, end to end: account ticket -> character -> AuthenticationOK, RoomForward -> room
// entry -> heightmap and avatars -> one step -> the step echoed back.
async function cmdPlay() {
  const { g, forward, from: entered } = await enterWorld();
  const fail = (m) => {
    g.close();
    die(m);
  };

  log(`RpgEnterWorld -> ${g.frames.slice(entered).map((f) => f.header).join(', ')}`);
  if (!forward) fail('No RoomForward: is world.start_room_id set? (scripts/sql/dev_zone.sql)');
  const roomId = forward.body.readInt32BE(0);

  const before = g.frames.length;
  g.send(frame(H.openFlat, roomId, '', -1));
  const users = await g.wait([H.users, H.cantConnect], before, 10000);
  if (!users || users.header === H.cantConnect) fail(`Room ${roomId} refused or silent.`);
  const entry = g.frames.slice(before);
  const has = (h) => entry.some((f) => f.header === h);
  log(`room ${roomId} -> ${entry.map((f) => f.header).join(', ')}`);
  if (!has(H.heightMap) || !has(H.floorHeightMap)) fail('No heightmap in the room entry.');

  // One step off the door tile; the room's avatar tick answers with a UserUpdate.
  const tile = entry.find((f) => f.header === H.entryTile);
  const [x, y] = tile ? [tile.body.readInt32BE(0), tile.body.readInt32BE(4)] : [3, 5];
  const step = g.frames.length;
  g.send(frame(H.moveAvatar, x + 1, y));
  const moved = await g.wait([H.userUpdate], step, 5000);
  g.close();
  if (!moved) die(`No UserUpdate after MoveAvatar to (${x + 1},${y}).`);
  log(`MoveAvatar (${x + 1},${y}) -> UserUpdate. Zone OK.`);
}

// A zone published through the editor API, then played in: the decor arrives with the room, and a
// second publication while the player stands in it forwards them back in (spec 2026-10-07 §6.7).
const ZONE_KEY = 'proof_zone';
const THRONE = 230;

function zoneDocument(throneAt) {
  const tiles = Array.from({ length: 100 }, () => ({ height: 0, walkable: true, hole: false }));
  return JSON.stringify({
    schemaVersion: 1,
    settings: { id: ZONE_KEY, name: 'Zone de preuve' },
    floor: { width: 10, length: 10, tiles, door: { x: 0, y: 5 }, doorDirection: 2 },
    decor: [{ id: 'throne_1', definition: THRONE, class: 'throne', x: throneAt[0], y: throneAt[1] }],
  });
}

async function cmdZone() {
  const base = `http://127.0.0.1:${WEBAPI}`;
  const headers = { cookie: (await webSession()).cookie };

  // Building needs world.zone.edit/publish: the throwaway account gets the owner role on this
  // (scratch) database, once.
  sql(`INSERT INTO player_account_roles (account_id, role_id)
       SELECT a.id, r.id FROM player_accounts a, roles r
       WHERE a.email='${PLAYER.email}' AND r.\`key\`='owner'
         AND NOT EXISTS (SELECT 1 FROM player_account_roles x WHERE x.account_id=a.id AND x.role_id=r.id);`);

  let res = await request(`${base}/api/editor/zones`, {
    method: 'POST',
    headers,
    body: JSON.stringify({ key: ZONE_KEY, name: 'Zone de preuve' }),
  });
  if (res.status !== 200 && res.status !== 409) die(`Zone creation: ${res.status} ${res.body}`);
  res = await request(`${base}/api/editor/zones/${ZONE_KEY}/lock`, { method: 'POST', headers });
  if (res.status !== 200) die(`Lock: ${res.status} ${res.body}`);

  const publish = async (throneAt) => {
    let r = await request(`${base}/api/editor/zones/${ZONE_KEY}/versions?comment=driver`, {
      method: 'POST',
      headers,
      body: zoneDocument(throneAt),
    });
    if (r.status !== 200) die(`Save: ${r.status} ${r.body}`);
    const saved = JSON.parse(r.body);
    if (!saved.published) {
      r = await request(`${base}/api/editor/zones/${ZONE_KEY}/publish/${saved.version}`, {
        method: 'POST',
        headers,
      });
      if (r.status !== 200) die(`Publish: ${r.status} ${r.body}`);
    }
    log(`Published ${ZONE_KEY} v${saved.version} (throne at ${throneAt}).`);
  };

  await publish([3, 3]);
  const zones = JSON.parse((await request(`${base}/api/editor/zones`, { headers })).body);
  const roomId = zones.find((z) => z.key === ZONE_KEY).roomId;

  const { g } = await enterWorld();
  const fail = (m) => {
    g.close();
    die(m);
  };

  const enter = async () => {
    const before = g.frames.length;
    g.send(frame(H.openFlat, roomId, '', -1));
    const users = await g.wait([H.users, H.cantConnect], before, 10000);
    if (!users || users.header === H.cantConnect) fail(`Zone room ${roomId} refused or silent.`);
    const objects = await g.wait([H.objects], before, 5000);
    if (!objects) fail('No Objects in the zone entry: the decor did not load.');
    // The player's own avatar is announced after the entry burst; until then the room does not
    // count them as an occupant, and a republication would not forward them.
    const own = await g.wait([H.users], g.frames.indexOf(objects) + 1, 5000);
    if (!own) fail('The avatar was never announced in the zone.');
    log(`zone room ${roomId} -> Objects (${objects.body.length} bytes), avatar in`);
  };

  await enter();
  const before = g.frames.length;
  await publish([4, 4]);
  if (!(await g.wait([H.roomForward], before, 5000))) fail('No RoomForward after republishing.');
  log('Republished while inside -> RoomForward.');
  await enter();
  g.close();
  log('Zone OK.');
}

// Two zones published through the editor API and walked through: a named area greets the player
// (NotificationDialog), a passage forwards them to the other zone, and they arrive on the entry the
// passage names -- not the destination's default arrival, not its door.
async function cmdTravel() {
  const base = `http://127.0.0.1:${WEBAPI}`;
  const headers = { cookie: (await webSession()).cookie };
  sql(`INSERT INTO player_account_roles (account_id, role_id)
       SELECT a.id, r.id FROM player_accounts a, roles r
       WHERE a.email='${PLAYER.email}' AND r.\`key\`='owner'
         AND NOT EXISTS (SELECT 1 FROM player_account_roles x WHERE x.account_id=a.id AND x.role_id=r.id);`);

  const tiles = Array.from({ length: 100 }, () => ({ height: 0, walkable: true, hole: false }));
  const floor = { width: 10, length: 10, tiles, door: { x: 0, y: 5 }, doorDirection: 2 };
  const docs = {
    proof_b: {
      schemaVersion: 1,
      settings: { id: 'proof_b', name: 'Preuve B' },
      floor,
      entries: [
        { id: 'start', kind: 'arrival', cell: { x: 8, y: 8 } },
        { id: 'gate', kind: 'respawn', cell: { x: 2, y: 2 } },
      ],
      zones: [{ id: 'vault', kind: 'Restricted', name: 'Chambre forte', polygon: [{ x: 7, y: 0 }, { x: 9, y: 0 }, { x: 9, y: 9 }, { x: 7, y: 9 }] }],
    },
    proof_a: {
      schemaVersion: 1,
      settings: { id: 'proof_a', name: 'Preuve A' },
      floor,
      zones: [{ id: 'market', kind: 'Safe', name: 'Marché', polygon: [{ x: 3, y: 0 }, { x: 5, y: 0 }, { x: 5, y: 9 }, { x: 3, y: 9 }] }],
      passages: [{ id: 'road', area: [{ x: 7, y: 5 }], destinationZone: 'proof_b', destinationEntry: 'gate' }],
      // Room objects 1 to 4: a closed door on (2,2), a sign on (1,1), a closed gate on (6,2) that
      // only the market's trigger opens, and a throne on (8,1) with no interaction: frozen decor.
      decor: [
        { id: 'door', definition: THRONE, class: 'throne', x: 2, y: 2, interaction: 'door' },
        { id: 'sign', definition: THRONE, class: 'throne', x: 1, y: 1, interaction: 'sign', tag: 'Bienvenue à Preuve A' },
        { id: 'gate', definition: THRONE, class: 'throne', x: 6, y: 2, interaction: 'door' },
        { id: 'throne', definition: THRONE, class: 'throne', x: 8, y: 1 },
      ],
      triggers: [
        {
          id: 't_market',
          type: 'EnterZone',
          target: 'market',
          actions: [
            { kind: 'ShowDialogue', dialogue: 'Le marché s\'anime' },
            { kind: 'OpenDoor', decor: 'gate' },
          ],
        },
      ],
    },
  };

  const rooms = {};
  for (const [key, doc] of Object.entries(docs)) {
    let r = await request(`${base}/api/editor/zones`, { method: 'POST', headers, body: JSON.stringify({ key, name: doc.settings.name }) });
    if (r.status !== 200 && r.status !== 409) die(`Zone creation ${key}: ${r.status} ${r.body}`);
    r = await request(`${base}/api/editor/zones/${key}/lock`, { method: 'POST', headers });
    if (r.status !== 200) die(`Lock ${key}: ${r.status} ${r.body}`);
    r = await request(`${base}/api/editor/zones/${key}/versions?comment=driver`, { method: 'POST', headers, body: JSON.stringify(doc) });
    if (r.status !== 200) die(`Save ${key}: ${r.status} ${r.body}`);
    const saved = JSON.parse(r.body);
    if (!saved.published) {
      r = await request(`${base}/api/editor/zones/${key}/publish/${saved.version}`, { method: 'POST', headers });
      if (r.status !== 200) die(`Publish ${key}: ${r.status} ${r.body}`);
    }
    const zones = JSON.parse((await request(`${base}/api/editor/zones`, { headers })).body);
    rooms[key] = zones.find((z) => z.key === key).roomId;
  }
  log(`Published proof_a (room ${rooms.proof_a}) and proof_b (room ${rooms.proof_b}).`);

  const { g } = await enterWorld();
  const fail = (m) => {
    g.close();
    die(m);
  };

  // Users: count, then per avatar id, name, motto, figure, room index, x, y...
  const firstUserAt = (f) => {
    const b = f.body;
    let o = 4;
    const str = () => ((o += 2 + b.readInt16BE(o)), null);
    o += 4;
    str(), str(), str();
    o += 4;
    return [b.readInt32BE(o), b.readInt32BE(o + 4)];
  };
  const enter = async (roomId) => {
    const before = g.frames.length;
    g.send(frame(H.openFlat, roomId, '', -1));
    const burst = await g.wait([H.objects, H.cantConnect], before, 10000);
    if (!burst || burst.header === H.cantConnect) fail(`Room ${roomId} refused or silent.`);
    const own = await g.wait([H.users], g.frames.indexOf(burst) + 1, 5000);
    if (!own) fail(`No avatar announced in room ${roomId}.`);
    return firstUserAt(own);
  };
  const walk = async (x, y, headersToWait) => {
    const before = g.frames.length;
    g.send(frame(H.moveAvatar, x, y));
    return g.wait(headersToWait, before, 8000);
  };

  const entered = g.frames.length;
  log(`proof_a: arrived on (${(await enter(rooms.proof_a)).join(',')}) (door 0,5)`);

  // What can be clicked here (spec F3b §5): the door, the sign and the gate; the throne is frozen.
  const clickables = await g.wait([H.rpgZoneInteractives], entered, 5000);
  if (!clickables) fail('No RpgZoneInteractives with the entry.');
  const ci = reader(clickables.body);
  const listed = Array.from({ length: ci.int() }, () => `${ci.int()}:${ci.str()}`);
  log(`RpgZoneInteractives: ${listed.join(', ')}`);
  if (listed.join(',') !== '1:door,2:sign,3:door') fail('Expected 1:door, 2:sign, 3:door.');
  const frozenMark = g.frames.length;
  g.send(frame(H.useFurniture, 4, 0));
  await sleep(1500);
  const answered = g.frames
    .slice(frozenMark)
    .some((f) => [H.objectDataUpdate, H.objectUpdate, H.notificationDialog].includes(f.header));
  if (answered) fail('The frozen throne answered a click.');
  log('UseFurniture on the frozen throne -> no answer.');

  // A UserUpdate whose status moves the avatar onto (x, y).
  const movesOnto = (x, y, from) =>
    g.frames.slice(from).some((f) => f.header === H.userUpdate && f.body.includes(`mv ${x},${y},`));
  let mark = g.frames.length;
  g.send(frame(H.moveAvatar, 2, 2));
  await sleep(2500);
  if (movesOnto(2, 2, mark)) fail('Walked onto the closed door.');
  mark = g.frames.length;
  // Out of reach from the door: the player walks up to it first, then it opens.
  g.send(frame(H.useFurniture, 1, 0));
  await sleep(3500);
  g.send(frame(H.moveAvatar, 2, 2));
  await sleep(2500);
  if (!movesOnto(2, 2, mark)) fail('The door, used, did not let the avatar through.');
  log('Closed door refused the step; UseFurniture walked up to it and opened it; walked onto it.');
  mark = g.frames.length;
  g.send(frame(H.useFurniture, 2, 0));
  const sign = await g.wait([H.notificationDialog], mark, 5000);
  if (!sign || !sign.body.includes('Bienvenue')) fail('The sign said nothing.');
  log('UseFurniture on the sign -> NotificationDialog "Bienvenue à Preuve A".');
  if (!(await walk(4, 5, [H.notificationDialog]))) fail('No bubble on entering the Marché area.');
  log('Walked into Marché -> NotificationDialog.');
  await sleep(500);
  if (!g.frames.some((f) => f.header === H.notificationDialog && f.body.includes('anime')))
    fail("The market's EnterZone trigger said nothing.");
  mark = g.frames.length;
  g.send(frame(H.moveAvatar, 6, 2));
  await sleep(2500);
  if (!movesOnto(6, 2, mark)) fail("The trigger's OpenDoor did not open the gate.");
  log('EnterZone trigger -> ShowDialogue "Le marché s\'anime" and OpenDoor: walked through the gate.');
  const forward = await walk(7, 5, [H.roomForward]);
  if (!forward) fail('No RoomForward from the passage.');
  const to = forward.body.readInt32BE(0);
  if (to !== rooms.proof_b) fail(`Forwarded to ${to}, expected ${rooms.proof_b}.`);
  log(`Passage -> RoomForward ${to}.`);
  const at = await enter(to);
  if (at[0] !== 2 || at[1] !== 2) fail(`Arrived on (${at.join(',')}), expected the passage's entry (2,2).`);
  log(`proof_b: arrived on (2,2), the passage's entry.`);

  // The vault (columns 7 to 9) is Restricted with no condition: the walk stops at its edge.
  mark = g.frames.length;
  g.send(frame(H.moveAvatar, 8, 2));
  await sleep(3500);
  const intoVault = [7, 8, 9].some((x) => movesOnto(x, 2, mark));
  const told = g.frames.slice(mark).some((f) => f.header === H.notificationDialog && f.body.includes('restreint'));
  g.close();
  if (intoVault) die('Walked into the restricted vault.');
  if (!told) die('No "Accès restreint" bubble at the vault.');
  log('Walk into the restricted vault -> stopped at its edge, "Accès restreint". Travel OK.');
}

// A dungeon is played in the group's own copy: asking for the template's room forwards the player
// to a copy (room id >= 1 000 000 000), and they land on the passage's dungeon entry there.
async function cmdDungeon() {
  const base = `http://127.0.0.1:${WEBAPI}`;
  const headers = { cookie: (await webSession()).cookie };
  sql(`INSERT INTO player_account_roles (account_id, role_id)
       SELECT a.id, r.id FROM player_accounts a, roles r
       WHERE a.email='${PLAYER.email}' AND r.\`key\`='owner'
         AND NOT EXISTS (SELECT 1 FROM player_account_roles x WHERE x.account_id=a.id AND x.role_id=r.id);`);
  const tiles = Array.from({ length: 100 }, () => ({ height: 0, walkable: true, hole: false }));
  const doc = {
    schemaVersion: 1,
    settings: { id: 'proof_crypt', name: 'Crypte de preuve', type: 'Dungeon' },
    floor: { width: 10, length: 10, tiles, door: { x: 0, y: 5 }, doorDirection: 2 },
    entries: [{ id: 'crypt_gate', kind: 'dungeon_entry', cell: { x: 7, y: 7 } }],
  };
  let r = await request(`${base}/api/editor/zones`, { method: 'POST', headers, body: JSON.stringify({ key: 'proof_crypt', name: 'Crypte de preuve' }) });
  if (r.status !== 200 && r.status !== 409) die(`Zone creation: ${r.status} ${r.body}`);
  r = await request(`${base}/api/editor/zones/proof_crypt/lock`, { method: 'POST', headers });
  if (r.status !== 200) die(`Lock: ${r.status} ${r.body}`);
  r = await request(`${base}/api/editor/zones/proof_crypt/versions?comment=driver`, { method: 'POST', headers, body: JSON.stringify(doc) });
  if (r.status !== 200) die(`Save: ${r.status} ${r.body}`);
  const saved = JSON.parse(r.body);
  if (!saved.published) {
    r = await request(`${base}/api/editor/zones/proof_crypt/publish/${saved.version}`, { method: 'POST', headers });
    if (r.status !== 200) die(`Publish: ${r.status} ${r.body}`);
  }
  const zones = JSON.parse((await request(`${base}/api/editor/zones`, { headers })).body);
  const template = zones.find((z) => z.key === 'proof_crypt').roomId;
  log(`Published proof_crypt (Dungeon) on room ${template}.`);

  // The sign-in's own RoomForward, to the start zone, comes first; the copy's is the next one.
  const { g } = await enterWorld();
  const fail = (m) => {
    g.close();
    die(m);
  };

  let before = g.frames.length;
  g.send(frame(H.openFlat, template, '', -1));
  const forward = await g.wait([H.roomForward, H.objects], before, 10000);
  if (!forward || forward.header !== H.roomForward) fail('Entering the dungeon did not forward to a copy.');
  const copy = forward.body.readInt32BE(0);
  if (copy < 1000000000) fail(`Forwarded to ${copy}, not a copy.`);
  log(`OpenFlat ${template} (template) -> RoomForward ${copy} (the player's copy)`);

  before = g.frames.length;
  g.send(frame(H.openFlat, copy, '', -1));
  const burst = await g.wait([H.objects, H.cantConnect], before, 10000);
  if (!burst || burst.header === H.cantConnect) fail(`Copy ${copy} refused or silent.`);
  const own = await g.wait([H.users], g.frames.indexOf(burst) + 1, 5000);
  if (!own) fail('No avatar announced in the copy.');
  log(`Entered copy ${copy}: its floor and the avatar came.`);

  // A copy id is easy to guess: one the player was not given must refuse them.
  before = g.frames.length;
  g.send(frame(H.openFlat, copy + 1, '', -1));
  const refused = await g.wait([H.cantConnect, H.objects], before, 10000);
  g.close();
  if (!refused || refused.header !== H.cantConnect) die(`Copy ${copy + 1}, not the player's, let them in.`);
  log(`OpenFlat ${copy + 1} (someone else's copy) -> CantConnect. Dungeon OK.`);
}

// The map editor's test mode: the route mints a ticket whose sign-in starts in the zone, on the
// cell asked for, instead of the start zone. Run `travel` first: it publishes proof_b.
async function cmdTestMode() {
  const base = `http://127.0.0.1:${WEBAPI}`;
  const headers = { cookie: (await webSession()).cookie };
  const res = await request(`${base}/api/editor/zones/proof_b/test?x=6&y=6`, { method: 'POST', headers });
  if (res.status !== 200) die(`Test route: ${res.status} ${res.body}`);
  const { ssoToken, roomId } = JSON.parse(res.body);
  log(`POST /api/editor/zones/proof_b/test?x=6&y=6 -> ticket for room ${roomId}`);

  const g = await gameSocket().catch((e) => die(`Socket error on :${GAME_TCP}: ${e.message}`));
  g.send(frame(H.sso, ssoToken, 0));
  const forward = await g.wait([H.roomForward]);
  if (!forward) die('No RoomForward after the test ticket.');
  const to = forward.body.readInt32BE(0);
  if (to !== roomId) die(`Signed in towards room ${to}, not the zone under test (${roomId}).`);
  const before = g.frames.length;
  g.send(frame(H.openFlat, to, '', -1));
  const burst = await g.wait([H.objects, H.cantConnect], before, 10000);
  if (!burst || burst.header === H.cantConnect) die(`Room ${to} refused or silent.`);
  // Users can come before or after Objects; an empty room lists nobody first.
  let own = await g.wait([H.users], before, 5000);
  while (own && own.body.readInt32BE(0) === 0) own = await g.wait([H.users], g.frames.indexOf(own) + 1, 5000);
  g.close();
  if (!own) die('No avatar announced.');
  const b = own.body;
  let o = 8;
  for (let i = 0; i < 3; i++) o += 2 + b.readInt16BE(o);
  o += 4;
  const [x, y] = [b.readInt32BE(o), b.readInt32BE(o + 4)];
  if (x !== 6 || y !== 6) die(`Arrived on (${x},${y}), not the tested cell (6,6).`);
  log(`Sign-in -> RoomForward ${to}; arrived on (6,6), the tested cell. Test mode OK.`);
}

// Enters a published zone by its key, as the throwaway player, and lists the floor items the room
// sends: what the map editor saved is what a player sees.
async function cmdEnter(key) {
  if (!key) die('usage: driver.mjs enter <zone key>');
  const base = `http://127.0.0.1:${WEBAPI}`;
  const headers = { cookie: (await webSession()).cookie };
  const zones = JSON.parse((await request(`${base}/api/editor/zones`, { headers })).body);
  const zone = Array.isArray(zones) && zones.find((z) => z.key === key);
  if (!zone) die(`No zone ${key} (or the account cannot list zones).`);
  if (!zone.publishedVersion) die(`Zone ${key} was never published.`);

  const { g } = await enterWorld();
  const before = g.frames.length;
  g.send(frame(H.openFlat, zone.roomId, '', -1));
  const objects = await g.wait([H.objects], before, 10000);
  g.close();
  if (!objects) die(`No Objects from room ${zone.roomId}.`);

  // Objects: owner count, (id, name)*, then the floor item count and each item (id, sprite, x, y…).
  const b = objects.body;
  let o = 0;
  const int = () => ((o += 4), b.readInt32BE(o - 4));
  const str = () => {
    const n = b.readInt16BE(o);
    o += 2 + n;
    return b.subarray(o - n, o).toString('utf8');
  };
  for (let owners = int(); owners > 0; owners--) (int(), str());
  const count = int();
  const items = [];
  for (let i = 0; i < count; i++) {
    const [id, sprite, x, y] = [int(), int(), int(), int()];
    items.push(`#${id} sprite ${sprite} (${x},${y})`);
    break; // the rest of an item is variable-length; the first one proves the layout
  }
  log(`zone ${key} v${zone.publishedVersion} -> room ${zone.roomId}: ${count} floor item(s)${items.length ? `, first ${items[0]}` : ''}`);
}

function cmdCleanup() {
  const id = sql(`SELECT id FROM player_accounts WHERE email='${PLAYER.email}' LIMIT 1;`);
  if (!id) return log('Nothing to clean up.');
  const players = `SELECT id FROM players WHERE account_id=${id}`;
  sql(`DELETE FROM security_tickets WHERE player_id IN (${players});`);
  sql(`DELETE FROM room_entry_logs WHERE player_id IN (${players});`);
  sql(`DELETE FROM players WHERE account_id=${id};`);
  sql(`DELETE FROM player_account_roles WHERE account_id=${id};`);
  sql(`DELETE FROM player_accounts WHERE id=${id};`);
  log(`Deleted the throwaway account ${id} and its players.`);
}

// ---------------------------------------------------------------- smoke

async function cmdSmoke() {
  await cmdStart();
  await cmdHealth();
  await cmdPing();
  await cmdPlay();
  log('\nSmoke OK.');
}

// The grace period (spec Personnage §10): enter, step, drop the socket without Quit, sign in again
// at once -> forwarded back to the same room, ONE avatar of this player, on the tile it reached.
// With `wait`, drop again and stay away past the grace: the console log announces the leave.
async function cmdGrace(wait) {
  const enter = async () => {
    const { g, forward } = await enterWorld();
    if (!forward) die('No RoomForward at sign-in.');
    const roomId = forward.body.readInt32BE(0);
    const before = g.frames.length;
    g.send(frame(H.openFlat, roomId, '', -1));
    let users = await g.wait([H.users, H.cantConnect], before, 10000);
    if (!users || users.header === H.cantConnect) die(`Room ${roomId} refused or silent.`);
    // An empty room lists nobody first; the player's own avatar comes in a later Users.
    while (users.body.readInt32BE(0) === 0) {
      users = await g.wait([H.users], g.frames.indexOf(users) + 1, 5000);
      if (!users) die(`No avatar announced in room ${roomId}.`);
    }
    return { g, roomId, before, users };
  };
  // Users: count, then per avatar web id, name, motto, figure, object id, x, y, ...
  const firstAvatar = (b) => {
    let o = 4;
    const webId = b.readInt32BE(o);
    o += 4;
    for (let i = 0; i < 3; i++) o += 2 + b.readInt16BE(o);
    return { count: b.readInt32BE(0), webId, x: b.readInt32BE(o + 4), y: b.readInt32BE(o + 8) };
  };

  const first = await enter();
  // One step from where the avatar stands: a zone's arrival entry is not always by the door.
  const at = firstAvatar(first.users.body);
  const [x, y] = [at.x > 0 ? at.x - 1 : at.x + 1, at.y];
  const step = first.g.frames.length;
  first.g.send(frame(H.moveAvatar, x, y));
  if (!(await first.g.wait([H.userUpdate], step, 5000))) die('No UserUpdate after the step.');
  await new Promise((r) => setTimeout(r, 1500));
  first.g.close();
  log(`Entered room ${first.roomId} on (${at.x},${at.y}), stepped to (${x},${y}), dropped the socket.`);

  await new Promise((r) => setTimeout(r, 1500));
  const back = await enter();
  const seen = firstAvatar(back.users.body);
  log(`Back in room ${back.roomId}: ${seen.count} avatar(s), first ${seen.webId} on (${seen.x},${seen.y}).`);
  if (back.roomId !== first.roomId) die('Forwarded to another room: the grace room was not resumed.');
  if (seen.count !== 1) die('Not exactly one avatar in the room: a second avatar was made.');
  if (seen.x !== x || seen.y !== y) die(`The avatar is not on (${x},${y}).`);
  log('Grace OK: same room, one avatar, same tile.');

  if (wait !== 'wait') {
    back.g.close();
    return;
  }

  const logStart = fs.existsSync(LOG_FILE) ? fs.statSync(LOG_FILE).size : 0;
  back.g.close();
  log('Dropped again; staying away 70 s...');
  await new Promise((r) => setTimeout(r, 70_000));
  const tail = fs.existsSync(LOG_FILE) ? fs.readFileSync(LOG_FILE, 'utf8').slice(logStart) : '';
  const line = tail.split(/\r?\n/).find((l) => l.includes('did not come back within the grace period'));
  if (!line) die('No grace-end line in the console log (is this an emulator the driver started?).');
  log(line.trim());
}

// The RPG content pack through scripts/content/push.mjs, as the throwaway account (made owner on the
// target database, like `zone`). Its exit code is the import's: 1 when the pack is refused.
async function cmdContent(dir = 'content') {
  await webSession();
  sql(`INSERT INTO player_account_roles (account_id, role_id)
       SELECT a.id, r.id FROM player_accounts a, roles r
       WHERE a.email='${PLAYER.email}' AND r.\`key\`='owner'
         AND NOT EXISTS (SELECT 1 FROM player_account_roles x WHERE x.account_id=a.id AND x.role_id=r.id);`);

  const out = spawnSync(
    process.execPath,
    [path.join(REPO, 'scripts', 'content', 'push.mjs'), '--base', `http://127.0.0.1:${WEBAPI}`, '--email', PLAYER.email, '--dir', dir],
    { stdio: 'inherit', env: { ...process.env, VORTEX_CONTENT_PASSWORD: PLAYER.password } },
  );
  if (out.status !== 0) process.exit(out.status ?? 1);
}

// The character screen by the socket alone (spec F3a §4): an account ticket opens the account
// phase -> the lists; a character created « Sans classe », given a real class, entered into the
// world as the SSO would; its state on the HUD, its progress after an XP grant; and a screen packet
// sent in the world is dropped by the phase gate.
async function cmdLobby() {
  const base = `http://127.0.0.1:${WEBAPI}`;
  const headers = { cookie: (await webSession()).cookie };
  sql(`INSERT INTO player_account_roles (account_id, role_id)
       SELECT a.id, r.id FROM player_accounts a, roles r
       WHERE a.email='${PLAYER.email}' AND r.\`key\`='owner'
         AND NOT EXISTS (SELECT 1 FROM player_account_roles x WHERE x.account_id=a.id AND x.role_id=r.id);`);

  // The ticket names the account: the socket opens on the character screen.
  let res = await request(`${base}/api/ssotoken`, { headers });
  if (res.status !== 200) die(`SSO ticket refused: ${res.status} ${res.body}`);
  const ticket = JSON.parse(res.body).ssoToken;

  const g = await gameSocket().catch((e) => die(`Socket error on :${GAME_TCP}: ${e.message}`));
  const fail = (m) => {
    g.close();
    die(m);
  };
  const result = async (from, action) => {
    const f = await g.wait([H.rpgResult], from);
    if (!f) fail(`No RpgCharacterResult for action ${action}.`);
    const r = reader(f.body);
    const [a, code, playerId] = [r.int(), r.str(), r.int()];
    log(`  RpgCharacterResult(action ${a}, ${code}, player ${playerId})`);
    if (a !== action || code !== 'ok') fail(`Expected action ${action} ok.`);
    return playerId;
  };

  g.send(frame(H.sso, ticket, 0));
  const list = await g.wait([H.rpgCharacterList]);
  const classes = await g.wait([H.rpgClassList]);
  if (!list || !classes) fail(`Account ticket -> ${g.frames.map((f) => f.header).join(', ')}: no lists.`);
  if (g.frames.some((f) => f.header === H.authOk)) fail('AuthenticationOK before a character was chosen.');
  const l = reader(list.body);
  const [max, count] = [l.int(), l.int()];
  const c = reader(classes.body);
  const classIds = Array.from({ length: c.int() }, () => {
    const id = c.str();
    c.str(), c.int(), c.str(), c.bool();
    Array.from({ length: c.int() }, () => c.str());
    return id;
  });
  log(`Account phase -> RpgCharacterList (${count}/${max}), RpgClassList (${classIds.join(', ')})`);
  if (count >= max) fail('The throwaway account is full: run `cleanup` first.');

  let from = g.frames.length;
  const name = `Lobby${Date.now() % 1_000_000}`;
  g.send(frame(H.rpgCreate, name, 'hd-180-1.ch-210-66.lg-270-82.sh-290-80', 'M'));
  log(`RpgCharacterCreate(${name})`);
  const playerId = await result(from, 1);
  if (!(await g.wait([H.rpgCharacterList], from))) fail('No list after the create.');

  from = g.frames.length;
  g.send(frame(H.rpgChooseClass, playerId, 'class.warrior'));
  log(`RpgCharacterChooseClass(${playerId}, class.warrior)`);
  await result(from, 2);

  from = g.frames.length;
  g.send(frame(H.rpgEnterWorld, playerId));
  const forward = await g.wait([H.roomForward], from);
  const entered = g.frames.slice(from).map((f) => f.header);
  log(`RpgEnterWorld(${playerId}) -> ${entered.join(', ')}`);
  if (!entered.includes(H.authOk) || !forward) fail('No AuthenticationOK and RoomForward.');

  const roomId = forward.body.readInt32BE(0);
  from = g.frames.length;
  g.send(frame(H.openFlat, roomId, '', -1));
  const state = await g.wait([H.rpgState], from, 10000);
  if (!state) fail(`No RpgCharacterState on entering room ${roomId}.`);
  const s = reader(state.body);
  const st = { player: s.int(), cls: s.str(), spec: s.str(), level: s.int(), max: s.int(), xp: s.int(), next: s.int() };
  const stats = { str: s.int(), int: s.int(), agi: s.int(), maxHealth: s.int(), maxMana: s.int(), armor: s.int(), critBp: s.int() };
  log(`room ${roomId} -> RpgCharacterState ${JSON.stringify(st)} stats ${JSON.stringify(stats)}`);
  if (st.cls !== 'class.warrior' || st.level !== 1) fail('Expected a level 1 warrior.');

  from = g.frames.length;
  res = await request(`${base}/api/admin/characters/${playerId}/xp`, {
    method: 'POST',
    headers,
    body: JSON.stringify({ amount: 250 }),
  });
  if (res.status !== 200) fail(`XP grant failed: ${res.status} ${res.body}`);
  const progress = await g.wait([H.rpgProgress], from);
  if (!progress) fail('No RpgCharacterProgress after the XP grant.');
  const p = reader(progress.body);
  const pr = { level: p.int(), xp: p.int(), next: p.int() };
  const pushed = g.frames.slice(from).map((f) => f.header);
  log(`250 XP -> ${pushed.join(', ')}; RpgCharacterProgress ${JSON.stringify(pr)}`);
  if (pr.level !== 3) fail('Expected level 3.');

  from = g.frames.length;
  g.send(frame(H.rpgCreate, `${name}x`, '', 'M'));
  await sleep(2000);
  const answered = g.frames.slice(from).some((f) => f.header === H.rpgResult);
  g.close();
  if (answered) die('A character-screen packet sent in the world was answered.');
  log('RpgCharacterCreate in the world -> nothing (phase gate). Lobby OK.');
}

// RpgInventory: bag size, parcel limit, then per copy id, definition, quantity, durability, bound,
// container (0 bag, 1 equipment, 2 parcel), slot.
function inventoryOf(frame) {
  const r = reader(frame.body);
  const [bagSize, maxParcels, count] = [r.int(), r.int(), r.int()];
  const items = Array.from({ length: count }, () => ({
    id: r.int(), definition: r.str(), quantity: r.int(), durability: r.int(),
    bound: r.bool(), container: r.int(), slot: r.int(),
  }));
  return { bagSize, maxParcels, items };
}

// The items (spec Objets §10, §17). The admin route gives the throwaway account's first character a
// sword, twice with the same key -- `ok` then `duplicate_grant`, and exactly one more sword row.
// Then the socket: the bag arrives with the world entry; the sword is refused to a « Sans classe »
// (wrong_class); a copper ring, which any class wears, goes to ring slot 8 with the stats, and back.
async function cmdItems() {
  const base = `http://127.0.0.1:${WEBAPI}`;
  const headers = { cookie: (await webSession()).cookie };
  sql(`INSERT INTO player_account_roles (account_id, role_id)
       SELECT a.id, r.id FROM player_accounts a, roles r
       WHERE a.email='${PLAYER.email}' AND r.\`key\`='owner'
         AND NOT EXISTS (SELECT 1 FROM player_account_roles x WHERE x.account_id=a.id AND x.role_id=r.id);`);

  const playerId = Number(
    sql(`SELECT p.id FROM players p JOIN player_characters c ON c.player_id = p.id
         JOIN player_accounts a ON a.id = p.account_id
         WHERE a.email='${PLAYER.email}' AND p.deleted_at IS NULL ORDER BY p.id LIMIT 1;`).trim(),
  );
  if (!playerId) die('No character on the throwaway account: run `play` first.');

  const SWORD = 'item.weapon.rusted_sword';
  const swords = () =>
    Number(
      sql(`SELECT COUNT(*) FROM rpg_items
           WHERE player_id=${playerId} AND definition_id='${SWORD}' AND deleted_at IS NULL;`).trim(),
    );
  const before = swords();
  const grant = JSON.stringify({ grantKey: `driver-${Date.now()}`, definitionId: SWORD, quantity: 1 });
  for (const expected of ['ok', 'duplicate_grant']) {
    const res = await request(`${base}/api/admin/characters/${playerId}/items`, { method: 'POST', headers, body: grant });
    log(`POST /api/admin/characters/${playerId}/items -> ${res.status} ${res.body}`);
    if (res.status !== 200 || JSON.parse(res.body).result !== expected) die(`Expected ${expected}.`);
  }
  const after = swords();
  if (after !== before + 1) die(`${before} -> ${after} swords: expected exactly one more.`);
  log(`Player ${playerId}: ${before} -> ${after} swords; the replayed key granted nothing.`);

  const RING = 'item.ring.copper_ring';
  const ringGrant = JSON.stringify({ grantKey: `driver-ring-${Date.now()}`, definitionId: RING, quantity: 1 });
  const ringRes = await request(`${base}/api/admin/characters/${playerId}/items`, { method: 'POST', headers, body: ringGrant });
  if (ringRes.status !== 200) die(`Ring grant: ${ringRes.status} ${ringRes.body}`);

  const { g, playerId: playing } = await enterWorld();
  const fail = (m) => {
    g.close();
    die(m);
  };
  if (playing !== playerId) fail(`The socket plays ${playing}, the grants went to ${playerId}.`);
  const first = await g.wait([H.rpgInventory], 0, 10000);
  if (!first) fail('No RpgInventory with the world entry.');
  const bag = inventoryOf(first);
  const inBag = (inv, def) => inv.items.find((i) => i.definition === def && i.container === 0);
  log(`RpgInventory: ${bag.items.length} item(s), bag ${bag.bagSize}, parcels up to ${bag.maxParcels}`);
  const sword = inBag(bag, SWORD);
  const ring = inBag(bag, RING);
  if (!sword || !ring) fail('The sword and the ring are not both in the bag.');

  const result = async (from, action, code) => {
    const f = await g.wait([H.rpgItemResult], from, 5000);
    if (!f) fail(`No RpgItemResult for action ${action}.`);
    const r = reader(f.body);
    const [a, c] = [r.int(), r.str()];
    log(`  RpgItemResult(${a}, ${c})`);
    if (a !== action || c !== code) fail(`Expected (${action}, ${code}).`);
    return f;
  };
  const after1 = (f) => g.wait([H.rpgInventory], g.frames.indexOf(f) + 1, 5000);

  let from = g.frames.length;
  g.send(frame(H.rpgItemEquip, sword.slot));
  log(`RpgItemEquip(${sword.slot}) — the sword, warriors only`);
  await result(from, 3, 'wrong_class');

  from = g.frames.length;
  g.send(frame(H.rpgItemEquip, ring.slot));
  log(`RpgItemEquip(${ring.slot}) — the copper ring`);
  const equipped = await result(from, 3, 'ok');
  if (!g.frames.slice(from).some((f) => f.header === H.rpgStats)) fail('No RpgCharacterStats after equipping.');
  const worn = await after1(equipped);
  const wornRing = worn && inventoryOf(worn).items.find((i) => i.id === ring.id);
  if (!wornRing || wornRing.container !== 1) fail('The ring is not worn after the equip.');
  log(`  RpgCharacterStats, RpgInventory: ring ${ring.id} in equipment slot ${wornRing.slot}`);

  from = g.frames.length;
  g.send(frame(H.rpgItemUnequip, wornRing.slot));
  log(`RpgItemUnequip(${wornRing.slot})`);
  const unequipped = await after1(await result(from, 4, 'ok'));
  const back = unequipped && inventoryOf(unequipped).items.find((i) => i.id === ring.id);
  g.close();
  if (!back || back.container !== 0) die('The ring did not come back to the bag.');
  log(`  RpgInventory: ring ${ring.id} back in case ${back.slot}. Items OK.`);
}

// The target (spec F3b §6): two throwaway accounts in the same zone. A aims at B and is told B's
// name, level and class; B quits; A's target clears. Then an id that is no avatar clears it too.
async function cmdTarget() {
  const a = await enterWorld(PLAYER);
  let b = null;
  const closeAll = () => [a.g, b?.g].forEach((s) => s?.close());
  const fail = (m) => {
    closeAll();
    die(m);
  };
  if (!a.forward) fail('A: no RoomForward at sign-in.');
  const room = a.forward.body.readInt32BE(0);
  let mark = a.g.frames.length;
  a.g.send(frame(H.openFlat, room, '', -1));
  if (!(await a.g.wait([H.objects], mark, 10000))) fail(`A: room ${room} silent.`);

  b = await enterWorld(PLAYER_B);
  const aBefore = a.g.frames.length;
  mark = b.g.frames.length;
  b.g.send(frame(H.openFlat, room, '', -1));
  if (!(await b.g.wait([H.objects], mark, 10000))) fail(`B: room ${room} refused.`);

  // Users: count, then per avatar web id, name, motto, figure, room object id, ...
  const firstAvatar = (f) => {
    const r = reader(f.body);
    r.int();
    const webId = r.int();
    const name = r.str();
    r.str(), r.str();
    return { webId, name, objectId: r.int() };
  };
  let seenB = null;
  for (let from = aBefore; !seenB; ) {
    const users = await a.g.wait([H.users], from, 8000);
    if (!users) fail('A was never told B arrived.');
    const first = firstAvatar(users);
    if (first.webId === b.playerId) seenB = first;
    from = a.g.frames.indexOf(users) + 1;
  }
  log(`A sees B: player ${seenB.webId} "${seenB.name}" as room object ${seenB.objectId}`);

  const targetFrom = async (from) => {
    const f = await a.g.wait([H.rpgTarget], from, 5000);
    if (!f) fail('No RpgTarget.');
    const r = reader(f.body);
    return { objectId: r.int(), kind: r.str(), name: r.str(), level: r.int(), classId: r.str() };
  };

  mark = a.g.frames.length;
  a.g.send(frame(H.rpgTargetSelect, seenB.objectId));
  const aimed = await targetFrom(mark);
  log(`RpgTargetSelect(${seenB.objectId}) -> RpgTarget ${JSON.stringify(aimed)}`);
  if (aimed.objectId !== seenB.objectId || aimed.kind !== 'player' || aimed.name !== seenB.name)
    fail('The target is not B.');

  mark = a.g.frames.length;
  b.g.send(frame(H.quit));
  const cleared = await targetFrom(mark);
  log(`B quits -> A gets RpgTarget ${JSON.stringify(cleared)}`);
  if (cleared.objectId !== 0) fail('The target did not clear when B left.');

  mark = a.g.frames.length;
  a.g.send(frame(H.rpgTargetSelect, 999999));
  const none = await targetFrom(mark);
  closeAll();
  if (none.objectId !== 0) die('An id that is no avatar did not clear the target.');
  log('RpgTargetSelect(999999) -> RpgTarget(0). Target OK.');
}

// The avatars of a Users frame: the common block, then the player's or the pet's own fields.
function usersOf(f) {
  const r = reader(f.body);
  const count = r.int();
  const avatars = [];
  for (let i = 0; i < count; i++) {
    const a = { webId: r.int(), name: r.str() };
    r.str(); // motto
    a.figure = r.str();
    a.objectId = r.int();
    a.x = r.int();
    a.y = r.int();
    r.str(); // z
    r.int(); // rotation
    a.type = r.int();
    if (a.type === 1) {
      r.str(), r.int(), r.bool();
    } else if (a.type === 2) {
      r.int(), r.int(), r.str(), r.bool(), r.bool();
      a.level = r.int();
      r.str();
    }
    avatars.push(a);
  }
  return avatars;
}

// The first combat (spec M1a §15): wolves and a brute born in a proof zone when a player comes in;
// a click from a safe cell refused; a click outside it targets a wolf, the avatar walks to it and
// kills it for XP; a wolf hits back; the brute kills the player, who comes back at half health; a
// zone change keeps that health.
async function cmdCombat() {
  await cmdContent();
  const rooms = await publishCombatZones();

  const a = await enterWorld(PLAYER);
  const fail = (m) => {
    a.g.close();
    die(m);
  };
  const g = a.g;
  await combatSteps(a, g, rooms, fail);
}

// The two combat proof zones, published through the editor: proof_combat (a safe camp around the
// arrival (1, 5), two wolves at (6, 4)-(7, 5), a brute at (9, 0)) and proof_combat_b. Their room ids.
async function publishCombatZones() {
  const base = `http://127.0.0.1:${WEBAPI}`;
  const headers = { cookie: (await webSession()).cookie };
  const tiles = Array.from({ length: 100 }, () => ({ height: 0, walkable: true, hole: false }));
  const floor = { width: 10, length: 10, tiles, door: { x: 0, y: 5 }, doorDirection: 2 };
  const docs = {
    proof_combat: {
      schemaVersion: 1,
      settings: { id: 'proof_combat', name: 'Preuve combat' },
      floor,
      entries: [
        { id: 'start', kind: 'arrival', cell: { x: 1, y: 5 } },
        { id: 'grave', kind: 'respawn', cell: { x: 1, y: 1 } },
      ],
      zones: [{ id: 'camp', kind: 'Safe', name: 'Camp', polygon: [{ x: 0, y: 4 }, { x: 2, y: 4 }, { x: 2, y: 6 }, { x: 0, y: 6 }] }],
      spawns: [
        {
          id: 'wolves',
          area: [{ x: 6, y: 4 }, { x: 6, y: 6 }, { x: 7, y: 5 }],
          monster: 'monster.forest.wolf',
          min: 2,
          max: 2,
          respawnSeconds: 60,
          wanderRadius: 1,
          aggroRadius: 2,
        },
        { id: 'brute', area: [{ x: 9, y: 0 }], monster: 'monster.proof.brute', aggroRadius: 1, wanderRadius: 0, respawnSeconds: 60 },
      ],
    },
    proof_combat_b: {
      schemaVersion: 1,
      settings: { id: 'proof_combat_b', name: 'Preuve combat B' },
      floor,
      // Not (1, 5): the next sign-in lands here, and `smoke` walks the player from the door to (1, 5).
      entries: [{ id: 'start', kind: 'arrival', cell: { x: 3, y: 3 } }],
    },
  };

  const rooms = {};
  for (const [key, doc] of Object.entries(docs)) {
    let r = await request(`${base}/api/editor/zones`, { method: 'POST', headers, body: JSON.stringify({ key, name: doc.settings.name }) });
    if (r.status !== 200 && r.status !== 409) die(`Zone creation ${key}: ${r.status} ${r.body}`);
    r = await request(`${base}/api/editor/zones/${key}/lock`, { method: 'POST', headers });
    if (r.status !== 200) die(`Lock ${key}: ${r.status} ${r.body}`);
    r = await request(`${base}/api/editor/zones/${key}/versions?comment=driver`, { method: 'POST', headers, body: JSON.stringify(doc) });
    if (r.status !== 200) die(`Save ${key}: ${r.status} ${r.body}`);
    const saved = JSON.parse(r.body);
    if (!saved.published) {
      r = await request(`${base}/api/editor/zones/${key}/publish/${saved.version}`, { method: 'POST', headers });
      if (r.status !== 200) die(`Publish ${key}: ${r.status} ${r.body}`);
    }
    const zones = JSON.parse((await request(`${base}/api/editor/zones`, { headers })).body);
    rooms[key] = zones.find((z) => z.key === key).roomId;
  }
  log(`Published proof_combat (room ${rooms.proof_combat}) and proof_combat_b (room ${rooms.proof_combat_b}).`);

  return rooms;
}

async function combatSteps(a, g, rooms, fail) {
  // RpgSelfVitals: health, max, mana, max mana, in combat. RpgCombatEvents: count × hit.
  const selfOf = (f) => {
    const r = reader(f.body);
    return { health: r.int(), maxHealth: r.int(), mana: r.int(), maxMana: r.int(), inCombat: r.bool() };
  };
  const hitsAfter = (from) =>
    g.frames.slice(from).filter((f) => f.header === H.rpgCombatEvents).flatMap((f) => {
      const r = reader(f.body);
      return Array.from({ length: r.int() }, () => ({
        attacker: r.int(),
        target: r.int(),
        amount: r.int(),
        outcome: r.str(),
        health: r.int(),
        maxHealth: r.int(),
        killed: r.bool(),
      }));
    });
  const until = async (test, ms) => {
    for (let t = 0; t < ms; t += 100) {
      const hit = test();
      if (hit) return hit;
      await sleep(100);
    }
    return null;
  };
  const lastSelf = (from) => {
    const frames = g.frames.slice(from).filter((f) => f.header === H.rpgSelfVitals);
    return frames.length ? selfOf(frames[frames.length - 1]) : null;
  };
  const resultAfter = async (from) => {
    const f = await g.wait([H.rpgCombatResult], from, 5000);
    return f ? reader(f.body).str() : null;
  };

  // 1. Into the combat zone: monsters are born as pets.
  let mark = g.frames.length;
  g.send(frame(H.openFlat, rooms.proof_combat, '', -1));
  const avatars = await until(() => {
    const all = g.frames.slice(mark).filter((f) => f.header === H.users).flatMap(usersOf);
    return all.some((x) => x.name === 'monster.proof.brute') && all.filter((x) => x.name === 'monster.forest.wolf').length >= 2 ? all : null;
  }, 8000);
  if (!avatars) fail('1. No wolves and brute born as pets in proof_combat.');
  const me = avatars.find((x) => x.type === 1 && x.webId === a.playerId);
  const wolves = avatars.filter((x) => x.name === 'monster.forest.wolf');
  const brute = avatars.find((x) => x.name === 'monster.proof.brute');
  if (!me || wolves.some((w) => w.type !== 2 || w.webId !== -w.objectId)) fail(`1. Avatars: ${JSON.stringify(avatars)}`);
  const entered = await until(() => lastSelf(mark), 3000);
  if (!entered) fail('1. No RpgSelfVitals on entering.');
  log(`1. In proof_combat: me ${me.objectId}, wolves ${wolves.map((w) => `${w.objectId}@${w.x},${w.y} L${w.level}`).join(' ')}, brute ${brute.objectId}; vitals ${JSON.stringify(entered)}`);

  // 2. From the camp (safe), an attack is refused.
  mark = g.frames.length;
  g.send(frame(H.rpgTargetSelect, wolves[0].objectId));
  const safe = await resultAfter(mark);
  if (safe !== 'safe_zone') fail(`2. Click from the camp -> ${safe}`);
  log('2. Click on a wolf from the camp -> safe_zone');

  // 3. Out of the camp, a click targets the wolf, the avatar walks to it and kills it.
  g.send(frame(H.moveAvatar, 4, 5));
  await sleep(1500);
  mark = g.frames.length;
  g.send(frame(H.rpgTargetSelect, wolves[0].objectId));
  const target = await g.wait([H.rpgTarget], mark, 5000);
  if (!target) fail('3. No RpgTarget for the wolf.');
  const t = reader(target.body);
  const shown = { objectId: t.int(), kind: t.str(), name: t.str(), level: t.int(), classId: t.str() };
  if (shown.kind !== 'monster' || shown.name !== 'monster.forest.wolf') fail(`3. RpgTarget ${JSON.stringify(shown)}`);
  const kill = await until(() => hitsAfter(mark).find((h) => h.target === wolves[0].objectId && h.killed), 40000);
  if (!kill) fail(`3. The wolf did not die: ${JSON.stringify(hitsAfter(mark).slice(-5))}`);
  const progress = await g.wait([H.rpgProgress], mark, 5000);
  if (!progress) fail('3. No RpgCharacterProgress (XP) after the kill.');
  const mine = hitsAfter(mark).filter((h) => h.attacker === me.objectId);
  log(`3. RpgTarget ${JSON.stringify(shown)}; ${mine.length} hits of mine, the wolf died; XP granted`);

  // 4. A wolf hits the player (the second one is aggressive).
  const bitten = await until(() => hitsAfter(0).find((h) => h.target === me.objectId && wolves.some((w) => w.objectId === h.attacker)), 20000);
  if (!bitten) fail('4. No wolf ever hit the player.');
  log(`4. A wolf hit me: ${JSON.stringify(bitten)}`);

  // 5. The brute kills the player; he comes back at half health.
  mark = g.frames.length;
  g.send(frame(H.rpgTargetSelect, 0));
  g.send(frame(H.moveAvatar, 8, 0));
  const death = await g.wait([H.rpgDeath], mark, 25000);
  if (!death) fail('5. No RpgDeath next to the brute.');
  const seconds = reader(death.body).int();
  const back = await until(() => {
    const s = g.frames.slice(g.frames.indexOf(death) + 1).filter((f) => f.header === H.rpgSelfVitals).map(selfOf);
    return s.find((v) => v.health > 0);
  }, (seconds + 3) * 1000);
  if (!back) fail('5. No respawn after the death.');
  const half = Math.max(1, Math.floor((back.maxHealth * 50) / 100));
  if (Math.abs(back.health - half) > Math.ceil(back.maxHealth * 0.05)) fail(`5. Respawned at ${back.health}/${back.maxHealth}, expected about ${half}.`);
  log(`5. RpgDeath(${seconds}), back at ${back.health}/${back.maxHealth}`);

  // 6. Another zone: the same health.
  await sleep(500);
  const before = lastSelf(0);
  mark = g.frames.length;
  g.send(frame(H.openFlat, rooms.proof_combat_b, '', -1));
  const there = await until(() => lastSelf(mark), 8000);
  if (!there) fail('6. No RpgSelfVitals in proof_combat_b.');
  if (there.health < before.health || there.health > before.health + Math.ceil(before.maxHealth * 0.05))
    fail(`6. Health ${before.health} in proof_combat became ${there.health} in proof_combat_b.`);
  log(`6. proof_combat_b: ${there.health}/${there.maxHealth} (was ${before.health})`);

  g.close();
  log('Combat OK.');
}

// The skills (spec M1b §17), a mage on a third account in proof_combat: his skills and bar, a
// Firebolt cast then landing on a wolf, a second cast refused while the first runs, a cast cut by a
// walk, a cast from the camp refused.
async function cmdSkills() {
  await cmdContent();
  const rooms = await publishCombatZones();
  const a = await enterWorld(PLAYER_C, 'class.mage');
  const g = a.g;
  const fail = (m) => {
    g.close();
    die(m);
  };
  const FIREBOLT = 'skill.mage.firebolt';
  const after = (header, from) => g.frames.slice(from).filter((f) => f.header === header);
  const until = async (test, ms) => {
    for (let t = 0; t < ms; t += 100) {
      const hit = test();
      if (hit) return hit;
      await sleep(100);
    }
    return null;
  };
  // RpgCastStart / RpgCastEnd: count × entry. RpgSkillEvents: impacts, then outcomes.
  const starts = (from) =>
    after(H.rpgCastStart, from).flatMap((f) => {
      const r = reader(f.body);
      return Array.from({ length: r.int() }, () => ({
        caster: r.int(),
        skill: r.str(),
        durationMs: r.int(),
        channel: r.bool(),
        target: r.int(),
        x: r.int(),
        y: r.int(),
      }));
    });
  const ends = (from) =>
    after(H.rpgCastEnd, from).flatMap((f) => {
      const r = reader(f.body);
      return Array.from({ length: r.int() }, () => ({ caster: r.int(), skill: r.str(), reason: r.str() }));
    });
  const outcomes = (from) =>
    after(H.rpgSkillEvents, from).flatMap((f) => {
      const r = reader(f.body);
      for (let n = r.int(); n > 0; n--) r.int(), r.str(), r.int(), r.int(), r.int();
      return Array.from({ length: r.int() }, () => ({
        source: r.int(),
        sourceId: r.str(),
        target: r.int(),
        amount: r.int(),
        absorbed: r.int(),
        outcome: r.str(),
        health: r.int(),
        maxHealth: r.int(),
        killed: r.bool(),
      }));
    });
  const refusal = (from) => {
    const f = after(H.rpgSkillResult, from)[0];
    if (!f) return null;
    const r = reader(f.body);
    return { skill: r.str(), code: r.str() };
  };
  const cast = (target) => g.send(frame(H.rpgSkillCast, FIREBOLT, target, -1, -1));

  // 1. Into the zone: the skills and the bar.
  let mark = g.frames.length;
  g.send(frame(H.openFlat, rooms.proof_combat, '', -1));
  const avatars = await until(() => {
    const all = after(H.users, mark).flatMap(usersOf);
    return all.filter((x) => x.name === 'monster.forest.wolf').length >= 2 ? all : null;
  }, 8000);
  if (!avatars) fail('1. No wolves in proof_combat.');
  const me = avatars.find((x) => x.type === 1 && x.webId === a.playerId);
  const wolves = avatars.filter((x) => x.name === 'monster.forest.wolf');
  const skills = await until(() => after(H.rpgSkills, mark)[0], 5000);
  if (!me || !skills) fail('1. No avatar of mine or no RpgSkills on entering.');
  const s = reader(skills.body);
  const known = Array.from({ length: s.int() }, () => s.str());
  const bar = Array.from({ length: s.int() }, () => s.str());
  if (!known.includes(FIREBOLT) || bar[0] !== `skill:${FIREBOLT}`) fail(`1. RpgSkills ${JSON.stringify({ known, bar })}`);
  log(`1. RpgSkills: known ${known.join(', ')}; bar ${JSON.stringify(bar)}`);

  // 2. Out of the camp, Firebolt on a wolf: the cast, then the damage, then the recharges.
  g.send(frame(H.moveAvatar, 4, 5));
  await sleep(1500);
  mark = g.frames.length;
  cast(wolves[0].objectId);
  const start = await until(() => starts(mark).find((x) => x.caster === me.objectId), 3000);
  if (!start || start.durationMs !== 2000 || start.channel) fail(`2. RpgCastStart ${JSON.stringify(start)}; refused ${JSON.stringify(refusal(mark))}`);
  const landed = await until(() => outcomes(mark).find((o) => o.sourceId === FIREBOLT && o.target === wolves[0].objectId), 5000);
  if (!landed || landed.amount <= 0) fail(`2. No Firebolt damage: ${JSON.stringify(outcomes(mark))}`);
  if (!(await until(() => after(H.rpgCooldowns, mark)[0], 2000))) fail('2. No RpgCooldowns after the cast.');
  log(`2. RpgCastStart ${start.durationMs} ms -> ${JSON.stringify(landed)} -> RpgCooldowns`);

  // 3. Two casts at once: the second is refused while the first runs.
  await sleep(1200);
  const alive = () => wolves.filter((w) => !outcomes(0).some((o) => o.target === w.objectId && o.killed));
  mark = g.frames.length;
  cast(alive()[0].objectId);
  cast(alive()[0].objectId);
  const second = await until(() => refusal(mark), 3000);
  if (!second || !['casting', 'global_cooldown'].includes(second.code)) fail(`3. A second cast -> ${JSON.stringify(second)}`);
  log(`3. A second Firebolt at once -> ${second.code}`);
  if (!(await until(() => ends(mark).find((e) => e.caster === me.objectId), 4000))) fail('3. The first cast never ended.');

  // 4. A cast cut short by a walk.
  await sleep(1200);
  if (!alive().length) fail('4. No wolf left alive.');
  mark = g.frames.length;
  cast(alive()[0].objectId);
  if (!(await until(() => starts(mark).find((x) => x.caster === me.objectId), 3000))) fail(`4. No cast started: ${JSON.stringify(refusal(mark))}`);
  g.send(frame(H.moveAvatar, 4, 3));
  const cut = await until(() => ends(mark).find((e) => e.caster === me.objectId), 3000);
  if (!cut || cut.reason !== 'cancelled') fail(`4. A walk mid-cast -> ${JSON.stringify(cut)}`);
  log('4. A walk mid-cast -> RpgCastEnd(cancelled)');

  // 5. Back in the camp, a cast is refused.
  g.send(frame(H.moveAvatar, 1, 5));
  await sleep(2500);
  mark = g.frames.length;
  cast(alive()[0]?.objectId ?? wolves[0].objectId);
  const safe = await until(() => refusal(mark), 3000);
  if (!safe || safe.code !== 'safe_zone') fail(`5. A cast from the camp -> ${JSON.stringify(safe)}`);
  log('5. A cast from the camp -> safe_zone');

  g.close();
  log('Skills OK.');
}

// The boss (spec M1c §16): the mage of PLAYER_D raised to level 6, the arena proof_boss published,
// the guardian's cone shown on the ground and dodged, its second phase (yell, enraged, two wolves)
// reached by Firebolt, then the mage back in the camp: the guardian goes home, its wolves leave and
// its health comes back whole.
async function publishBossZone() {
  const base = `http://127.0.0.1:${WEBAPI}`;
  const headers = { cookie: (await webSession()).cookie };
  const size = 12;
  const tiles = Array.from({ length: size * size }, () => ({ height: 0, walkable: true, hole: false }));
  const key = 'proof_boss';
  const doc = {
    schemaVersion: 1,
    settings: { id: key, name: 'Preuve boss' },
    floor: { width: size, length: size, tiles, door: { x: 0, y: 6 }, doorDirection: 2 },
    entries: [
      { id: 'start', kind: 'arrival', cell: { x: 1, y: 6 } },
      { id: 'grave', kind: 'respawn', cell: { x: 1, y: 6 } },
    ],
    zones: [{ id: 'camp', kind: 'Safe', name: 'Camp', polygon: [{ x: 0, y: 5 }, { x: 2, y: 5 }, { x: 2, y: 7 }, { x: 0, y: 7 }] }],
    spawns: [
      { id: 'guardian', area: [{ x: 8, y: 6 }], monster: 'monster.grove.guardian', aggroRadius: 3, wanderRadius: 0, respawnSeconds: 30 },
      { id: 'archer', area: [{ x: 11, y: 0 }], monster: 'monster.goblin.archer', aggroRadius: 1, wanderRadius: 0, respawnSeconds: 30 },
      { id: 'shaman', area: [{ x: 11, y: 11 }], monster: 'monster.goblin.shaman', aggroRadius: 1, wanderRadius: 0, respawnSeconds: 30 },
    ],
  };
  let r = await request(`${base}/api/editor/zones`, { method: 'POST', headers, body: JSON.stringify({ key, name: doc.settings.name }) });
  if (r.status !== 200 && r.status !== 409) die(`Zone creation ${key}: ${r.status} ${r.body}`);
  r = await request(`${base}/api/editor/zones/${key}/lock`, { method: 'POST', headers });
  if (r.status !== 200) die(`Lock ${key}: ${r.status} ${r.body}`);
  r = await request(`${base}/api/editor/zones/${key}/versions?comment=driver`, { method: 'POST', headers, body: JSON.stringify(doc) });
  if (r.status !== 200) die(`Save ${key}: ${r.status} ${r.body}`);
  const saved = JSON.parse(r.body);
  if (!saved.published) {
    r = await request(`${base}/api/editor/zones/${key}/publish/${saved.version}`, { method: 'POST', headers });
    if (r.status !== 200) die(`Publish ${key}: ${r.status} ${r.body}`);
  }
  const zones = JSON.parse((await request(`${base}/api/editor/zones`, { headers })).body);
  const room = zones.find((z) => z.key === key).roomId;
  log(`Published ${key} (room ${room}).`);
  return room;
}

async function cmdBoss() {
  await cmdContent();
  const base = `http://127.0.0.1:${WEBAPI}`;
  // The editor and the admin XP route need the owner role: the first proof account holds it (signed
  // in first, so that it exists; the role is read at each request). The mage's account needs none.
  await webSession(PLAYER);
  sql(`INSERT INTO player_account_roles (account_id, role_id)
       SELECT a.id, r.id FROM player_accounts a, roles r
       WHERE a.email='${PLAYER.email}' AND r.\`key\`='owner'
         AND NOT EXISTS (SELECT 1 FROM player_account_roles x WHERE x.account_id=a.id AND x.role_id=r.id);`);
  const room = await publishBossZone();
  const a = await enterWorld(PLAYER_D, 'class.mage');
  const g = a.g;
  const fail = (m) => {
    g.close();
    die(m);
  };
  const GUARDIAN = 'monster.grove.guardian';
  const FIREBOLT = 'skill.mage.firebolt';
  const after = (header, from) => g.frames.slice(from).filter((f) => f.header === header);
  const until = async (test, ms) => {
    for (let t = 0; t < ms; t += 100) {
      const hit = test();
      if (hit) return hit;
      await sleep(100);
    }
    return null;
  };

  // 1. Level 6 for the mage, no more: Firebolt hits hard enough to reach phase 2 without killing the
  // guardian past it, run after run. XP to reach each level (content/progression): 100, 150, 220,
  // 300, 400.
  const toLevel = [0, 100, 250, 470, 770, 1170];
  const [level, xpNow] = sql(`SELECT level, xp FROM player_characters WHERE player_id=${a.playerId};`)
    .trim()
    .split(/\s+/)
    .map(Number);
  const missing = level >= 6 ? 0 : toLevel[5] - toLevel[level - 1] - xpNow;
  if (missing > 0) {
    const xp = await request(`${base}/api/admin/characters/${a.playerId}/xp`, {
      method: 'POST',
      headers: { cookie: (await webSession(PLAYER)).cookie },
      // The route wants a grant key of the caller's (a replayed request grants nothing more).
      body: JSON.stringify({ amount: missing, grantKey: `driver-boss-${a.playerId}-${Date.now()}` }),
    });
    if (xp.status !== 200) fail(`1. POST /api/admin/characters/${a.playerId}/xp -> ${xp.status} ${xp.body}`);
    log(`1. ${missing} XP granted: ${xp.body}`);
  } else {
    log(`1. The mage is level ${level} already.`);
  }

  // 2. Into the arena: the guardian, the archer and the shaman.
  let mark = g.frames.length;
  g.send(frame(H.openFlat, room, '', -1));
  const avatars = await until(() => {
    const all = after(H.users, mark).flatMap(usersOf);
    return all.some((x) => x.name === GUARDIAN) && all.some((x) => x.type === 1 && x.webId === a.playerId) ? all : null;
  }, 8000);
  if (!avatars) fail('2. No guardian or no avatar of mine in proof_boss.');
  const me = avatars.find((x) => x.type === 1 && x.webId === a.playerId);
  const guardian = avatars.find((x) => x.name === GUARDIAN);
  log(`2. In proof_boss: guardian ${guardian.objectId} at (${guardian.x}, ${guardian.y}), level ${guardian.level}.`);

  // 3. Close to it: it comes, casts its cone, the cells shown; step aside, the impact misses.
  // RpgTelegraphs: count × (caster, skill, shape, delay, count × (x, y)).
  const telegraphs = (from) =>
    after(H.rpgTelegraphs, from).flatMap((f) => {
      const r = reader(f.body);
      return Array.from({ length: r.int() }, () => {
        const t = { caster: r.int(), skill: r.str(), shape: r.str(), delayMs: r.int() };
        t.cells = Array.from({ length: r.int() }, () => [r.int(), r.int()]);
        return t;
      });
    });
  // RpgSkillEvents: impacts (caster, skill, target, x, y), then outcomes.
  const events = (from) =>
    after(H.rpgSkillEvents, from).map((f) => {
      const r = reader(f.body);
      const impacts = Array.from({ length: r.int() }, () => ({ caster: r.int(), skill: r.str(), target: r.int(), x: r.int(), y: r.int() }));
      const outcomes = Array.from({ length: r.int() }, () => ({
        source: r.int(),
        sourceId: r.str(),
        target: r.int(),
        amount: r.int(),
        absorbed: r.int(),
        outcome: r.str(),
        health: r.int(),
        maxHealth: r.int(),
        killed: r.bool(),
      }));
      return { impacts, outcomes };
    });
  mark = g.frames.length;
  g.send(frame(H.moveAvatar, 5, 6));
  const shown = await until(() => telegraphs(mark).find((t) => t.caster === guardian.objectId), 12000);
  if (!shown) fail('3. The guardian showed no telegraph.');
  log(`3. RpgTelegraphs: ${shown.skill} ${shown.shape}, ${shown.delayMs} ms, ${shown.cells.length} cells.`);
  const marked = new Set(shown.cells.map(([x, y]) => `${x},${y}`));
  // Two steps across the cone's axis leave it (a 90° cone of 3 holds no cell two off its side).
  const escapes = [[5, 8], [5, 4], [4, 8], [4, 4], [3, 6]].filter(([x, y]) => !marked.has(`${x},${y}`));
  g.send(frame(H.moveAvatar, ...escapes[0]));
  const landed = await until(
    () => events(mark).find((e) => e.impacts.some((i) => i.caster === guardian.objectId && i.skill === shown.skill)),
    shown.delayMs + 3000,
  );
  if (!landed) fail('3. The telegraph never landed.');
  const hitMe = landed.outcomes.some((o) => o.target === me.objectId);
  if (hitMe) fail(`3. Out of the cells and hit anyway: ${JSON.stringify(landed.outcomes)}`);
  log(`3. Stepped to (${escapes[0].join(', ')}); the ${shown.skill} landed without me.`);

  // 4. Firebolt until it falls under 66 %: its yell, its rage, two wolves more.
  // RpgVitals: count × (object, health, max).
  const guardianHealth = (from) => {
    let last = null;
    for (const f of after(H.rpgVitals, from)) {
      const r = reader(f.body);
      for (let n = r.int(); n > 0; n--) {
        const [id, health, max] = [r.int(), r.int(), r.int()];
        if (id === guardian.objectId) last = { health, max };
      }
    }
    return last;
  };
  // RpgStatuses: count × (object, count × (status, remaining, stacks)).
  const statusesOf = (from, objectId) => {
    let last = null;
    for (const f of after(H.rpgStatuses, from)) {
      const r = reader(f.body);
      for (let n = r.int(); n > 0; n--) {
        const id = r.int();
        const list = Array.from({ length: r.int() }, () => ({ status: r.str(), remainingMs: r.int(), stacks: r.int() }));
        if (id === objectId) last = list;
      }
    }
    return last;
  };
  const fightFrom = g.frames.length;
  g.send(frame(H.rpgTargetSelect, guardian.objectId));
  let yell = null;
  for (let t = 0; t < 40000 && !yell; t += 2500) {
    g.send(frame(H.rpgSkillCast, FIREBOLT, guardian.objectId, -1, -1));
    await sleep(2500);
    yell = after(H.rpgMonsterYell, fightFrom)
      .map((f) => reader(f.body))
      .map((r) => ({ objectId: r.int(), textKey: r.str() }))
      .find((y) => y.objectId === guardian.objectId);
  }
  const hp = guardianHealth(fightFrom);
  if (!yell) fail(`4. No yell from the guardian; its health ${JSON.stringify(hp)}.`);
  log(`4. RpgMonsterYell ${yell.textKey} at ${JSON.stringify(hp)}.`);
  const rage = await until(() => statusesOf(fightFrom, guardian.objectId)?.find((s) => s.status === 'status.enraged'), 3000);
  if (!rage || rage.remainingMs !== -1) fail(`4. The guardian is not enraged for good: ${JSON.stringify(statusesOf(fightFrom, guardian.objectId))}`);
  const wolves = await until(() => {
    const w = after(H.users, fightFrom).flatMap(usersOf).filter((x) => x.name === 'monster.forest.wolf');
    return w.length >= 2 ? w : null;
  }, 3000);
  if (!wolves) fail('4. No wolves summoned.');
  log(`4. status.enraged (no end) and ${wolves.length} wolves: ${wolves.map((w) => w.objectId).join(', ')}.`);

  // 5. Back to the camp: the guardian goes home, its wolves leave, its health comes back whole.
  mark = g.frames.length;
  g.send(frame(H.moveAvatar, 1, 6));
  const gone = await until(() => {
    const removed = after(H.userRemove, mark).map((f) => Number(reader(f.body).str()));
    return wolves.every((w) => removed.includes(w.objectId)) ? removed : null;
  }, 12000);
  if (!gone) fail('5. The wolves did not leave when the guardian reset.');
  const whole = await until(() => {
    const h = guardianHealth(mark);
    return h && h.health === h.max ? h : null;
  }, 15000);
  if (!whole) fail(`5. The guardian did not heal at home: ${JSON.stringify(guardianHealth(mark))}`);
  log(`5. Wolves gone; guardian back to ${whole.health}/${whole.max}.`);

  g.close();
  log('Boss OK.');
}

// The chat (spec F3c §15), two accounts: Général within a zone, Monde across, a whisper by name
// and its copy, the idle Guilde, an item link checked, the rate limit, unknown and offline names.
async function cmdChat() {
  const a = await enterWorld(PLAYER);
  let b = null;
  const closeAll = () => [a.g, b?.g].forEach((s) => s?.close());
  const fail = (m) => {
    closeAll();
    die(m);
  };
  if (!a.forward) fail('A: no RoomForward at sign-in.');
  const room = a.forward.body.readInt32BE(0);
  let mark = a.g.frames.length;
  a.g.send(frame(H.openFlat, room, '', -1));
  if (!(await a.g.wait([H.objects], mark, 10000))) fail(`A: room ${room} silent.`);

  b = await enterWorld(PLAYER_B);
  mark = b.g.frames.length;
  b.g.send(frame(H.openFlat, room, '', -1));
  if (!(await b.g.wait([H.objects], mark, 10000))) fail(`B: room ${room} refused.`);

  // RpgChatMessage: channel, sender id, sender name, text, sent at, target. RpgChatResult: channel, code.
  const lineOf = (f) => {
    const r = reader(f.body);
    return { channel: r.str(), senderId: r.int(), senderName: r.str(), text: r.str(), sentAt: r.int(), target: r.str() };
  };
  const linesAfter = (g, from) => g.frames.slice(from).filter((f) => f.header === H.rpgChatMessage).map(lineOf);
  const lineAfter = async (g, from, pred, ms = 5000) => {
    for (let t = 0; t < ms; t += 50) {
      const hit = linesAfter(g, from).find(pred);
      if (hit) return hit;
      await sleep(50);
    }
    return null;
  };
  const resultAfter = async (g, from) => {
    const f = await g.wait([H.rpgChatResult], from);
    if (!f) return null;
    const r = reader(f.body);
    return { channel: r.str(), code: r.str() };
  };
  const say = (who, channel, text, target = '') => who.g.send(frame(H.rpgChatSend, channel, target, text));

  // 1. Général in the same zone.
  let mb = b.g.frames.length;
  say(a, 'general', 'bonjour de A');
  const hello = await lineAfter(b.g, mb, (l) => l.text === 'bonjour de A');
  if (hello?.channel !== 'general') fail('1. B did not read A in Général.');
  const nameA = hello.senderName;
  log(`1. Général, same zone: B reads ${JSON.stringify(hello)}`);

  // 2. B speaks, A learns its name.
  let ma = a.g.frames.length;
  say(b, 'general', 'bonjour de B');
  const back = await lineAfter(a.g, ma, (l) => l.text === 'bonjour de B');
  if (!back) fail('2. A did not read B in Général.');
  const nameB = back.senderName;
  log(`2. A is "${nameA}", B is "${nameB}".`);

  // 2b. An RP action of A, seen by B in the same room (RpgEmote in 9012, out 9017).
  mb = b.g.frames.length;
  a.g.send(frame(H.rpgEmote, 'wave'));
  const wave = await b.g.wait([H.rpgEmoteShown], mb);
  if (!wave) fail('2b. B did not see A wave.');
  const w = reader(wave.body);
  const shown = { objectId: w.int(), key: w.str() };
  if (shown.key !== 'wave') fail(`2b. Emote -> ${JSON.stringify(shown)}`);
  log(`2b. A waves: B sees ${JSON.stringify(shown)}`);

  // 3. B leaves the room: no Général, Monde once.
  b.g.send(frame(H.quit));
  await sleep(1100);
  mb = b.g.frames.length;
  say(a, 'general', 'zone seulement');
  if (await lineAfter(b.g, mb, (l) => l.text === 'zone seulement', 2000)) fail('3. B, out of the room, read Général.');
  mb = b.g.frames.length;
  say(a, 'world', 'au monde');
  const world = await lineAfter(b.g, mb, (l) => l.text === 'au monde');
  if (world?.channel !== 'world') fail('3. B did not read A in Monde.');
  await sleep(500);
  const copies = linesAfter(b.g, mb).filter((l) => l.text === 'au monde').length;
  if (copies !== 1) fail(`3. B got ${copies} copies of the Monde line.`);
  log('3. B out of the room: Général not read, Monde read once.');

  // 4. Général outside any room.
  mb = b.g.frames.length;
  say(b, 'general', 'perdu');
  const lost = await resultAfter(b.g, mb);
  if (lost?.code !== 'not_in_zone') fail(`4. Général outside a room -> ${JSON.stringify(lost)}`);
  log(`4. B in Général outside a room -> ${JSON.stringify(lost)}`);

  // 5. A whisper by name, case aside: B reads it, A gets its copy.
  ma = a.g.frames.length;
  mb = b.g.frames.length;
  say(a, 'whisper', 'psst', nameB.toUpperCase());
  const heard = await lineAfter(b.g, mb, (l) => l.channel === 'whisper');
  const copy = await lineAfter(a.g, ma, (l) => l.channel === 'whisper');
  if (heard?.target !== nameA || copy?.target !== nameB || heard.text !== 'psst')
    fail(`5. Whisper: B ${JSON.stringify(heard)}, A ${JSON.stringify(copy)}`);
  log(`5. Whisper to "${nameB.toUpperCase()}": B reads target "${heard.target}", A's copy target "${copy.target}".`);

  // 6. Guilde is ready but idle.
  ma = a.g.frames.length;
  say(a, 'guild', 'salut la guilde');
  const guild = await resultAfter(a.g, ma);
  if (guild?.code !== 'not_in_guild') fail(`6. Guilde -> ${JSON.stringify(guild)}`);
  log(`6. Guilde -> ${JSON.stringify(guild)}`);

  // 7. An item link kept, an invented one removed.
  await sleep(1100);
  ma = a.g.frames.length;
  say(a, 'general', 'vend [item:item.material.amber_shard] et [item:item.fake.nothing]');
  const linked = await lineAfter(a.g, ma, (l) => l.channel === 'general' && l.text.startsWith('vend'));
  if (linked?.text !== 'vend [item:item.material.amber_shard] et') fail(`7. Links -> ${JSON.stringify(linked)}`);
  log(`7. Links -> "${linked.text}"`);

  // 8. Monde again within 10 s.
  ma = a.g.frames.length;
  say(a, 'world', 'encore');
  const limited = await resultAfter(a.g, ma);
  if (limited?.code !== 'rate_limited') fail(`8. Monde twice -> ${JSON.stringify(limited)}`);
  log(`8. Monde twice within 10 s -> ${JSON.stringify(limited)}`);

  // 9. An unknown name.
  await sleep(1100);
  ma = a.g.frames.length;
  say(a, 'whisper', 'allo', 'personne_zz');
  const nobody = await resultAfter(a.g, ma);
  if (nobody?.code !== 'player_not_found') fail(`9. Unknown name -> ${JSON.stringify(nobody)}`);
  log(`9. Whisper to "personne_zz" -> ${JSON.stringify(nobody)}`);

  // 9b. Commerce reaches B outside any room, like Monde.
  mb = b.g.frames.length;
  say(a, 'trade', 'vends épée rouillée');
  const trade = await lineAfter(b.g, mb, (l) => l.channel === 'trade');
  if (trade?.text !== 'vends épée rouillée') fail(`9b. Commerce -> ${JSON.stringify(trade)}`);
  log(`9b. Commerce: B reads ${JSON.stringify(trade)}`);

  // 10. B's socket gone: offline (its presence waits in grace, without a socket).
  b.g.close();
  await sleep(1100);
  ma = a.g.frames.length;
  say(a, 'whisper', 'tu es là ?', nameB);
  const offline = await resultAfter(a.g, ma);
  if (offline?.code !== 'player_offline') fail(`10. Offline name -> ${JSON.stringify(offline)}`);
  log(`10. Whisper to "${nameB}" after B left -> ${JSON.stringify(offline)}`);

  closeAll();
  log('Chat OK.');
}

// ---------------------------------------------------------------- dispatch

const [cmd, ...args] = process.argv.slice(2);
const table = {
  mysql: cmdMysql,
  start: cmdStart,
  status: cmdStatus,
  stop: cmdStop,
  logs: cmdLogs,
  ping: cmdPing,
  health: cmdHealth,
  play: cmdPlay,
  zone: cmdZone,
  enter: () => cmdEnter(args[0]),
  travel: cmdTravel,
  testmode: cmdTestMode,
  dungeon: cmdDungeon,
  content: () => cmdContent(args[0]),
  lobby: cmdLobby,
  items: cmdItems,
  target: cmdTarget,
  chat: cmdChat,
  combat: cmdCombat,
  skills: cmdSkills,
  boss: cmdBoss,
  grace: () => cmdGrace(args[0]),
  cleanup: cmdCleanup,
  smoke: cmdSmoke,
};

if (!cmd || !table[cmd]) {
  log(`usage: node driver.mjs <command>

  smoke     start + health + ping + play
  mysql     start Laragon MySQL if 3306 is closed
  start     launch Vortex.Main (reuses one already running; never stops it)
  status    pids and the ports
  stop      stop ONLY an emulator this driver started
  logs [n]  tail the console log of the emulator this driver started
  health    /health and /api/public/info/hello
  ping      raw frame on :${GAME_TCP} -- ${H.initDiffie} out, ${H.initDiffieReply} back
  play      throwaway account -> account ticket -> its character (made if none) -> start zone -> heightmap -> one step
  zone      publish a zone through /api/editor, enter it, republish while inside
  enter <k> enter the published zone <k> and count the floor items it sends
  travel    two proof zones: named area bubble, passage A -> B, arrival on the passage's entry
  testmode  the editor's test route: a ticket that signs in to proof_b on (6,6)
  dungeon   a Dungeon zone: its room forwards to the player's own copy, which is entered
  content [dir]  push the RPG content pack (default content/) through scripts/content/push.mjs
  lobby     the character screen by the socket: account ticket -> lists, create, class, enter world, HUD state, XP progress, phase gate
  target    two accounts in one zone: A targets B (name, level, class), B quits, A's target clears
  items     the admin item grant: a sword to the first character, replayed with its key -> one sword more
  grace [wait]  drop the socket in a room and come back: same room, one avatar, same tile (wait: then stay away past 60 s)
  boss      proof_boss: a cone telegraph dodged, phase 2 by Firebolt (yell, enraged, two wolves), back to camp -> reset
  cleanup   delete the throwaway account and its players`);
  process.exit(cmd ? 1 : 0);
}

await table[cmd](...args);
