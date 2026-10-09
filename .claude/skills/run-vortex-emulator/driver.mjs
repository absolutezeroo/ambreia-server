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
const STATE = path.join(os.tmpdir(), 'vortex-run');
const PID_FILE = path.join(STATE, 'vortex-main.pid');
const LOG_FILE = path.join(STATE, 'vortex-main.log');

// appsettings.Development.json, when present, moves the game sockets off the 30000/30001 of
// appsettings.json. VORTEX_GAME_PORT names the port when it was moved another way (the unprefixed
// serverOptions__TcpServer__listeners__0__port variable).
const GAME_TCP =
  Number(process.env.VORTEX_GAME_PORT) ||
  (fs.existsSync(path.join(REPO, 'appsettings.Development.json')) ? 40000 : 30000);
const WEBAPI = 8080;
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
  for (const cfg of ['Debug', 'Release']) {
    const p = path.join(REPO, 'Vortex.Main', 'bin', cfg, 'net10.0', 'Vortex.Main.exe');
    if (fs.existsSync(p)) return p;
  }
  return null;
}

async function cmdStart() {
  const foreign = foreignPids();
  if (foreign.length) {
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
  const hello = await request(`http://127.0.0.1:${WEBAPI}/api/public/info/hello`);
  log(`GET :${WEBAPI}/api/public/info/hello -> ${hello.status} ${hello.body}`);
}

// A throwaway player, kept at a fixed address so re-runs reuse it; `cleanup` deletes it.
const PLAYER = { email: 'run-skill@vortex.local', password: 'RunSkill!2026' };
const WEB_COOKIE = 'habbo-web-session';

function sessionCookie(res) {
  const c = [].concat(res.headers['set-cookie'] || []).find((x) => x.startsWith(`${WEB_COOKIE}=`));
  return c ? c.split(';')[0] : null;
}

// Login, or registration when the account does not exist yet: the site's web session. Login first:
// registration allows 3 attempts per 5 minutes, and a few driver runs in a row used them all up.
let webSessionOnce = null;

// One login per driver run: the login allows 5 per minute, and a command logs in more than once.
function webSession() {
  return (webSessionOnce ??= openWebSession());
}

async function openWebSession() {
  const base = `http://127.0.0.1:${WEBAPI}`;
  const json = (o) => JSON.stringify(o);
  let res = await request(`${base}/api/public/authentication/login`, {
    method: 'POST',
    body: json({ email: PLAYER.email, password: PLAYER.password }),
  });
  if (!sessionCookie(res) && res.status !== 429) {
    res = await request(`${base}/api/public/registration/new`, {
      method: 'POST',
      body: json({ email: PLAYER.email, password: PLAYER.password, passwordRepeated: PLAYER.password }),
    });
  }
  const cookie = sessionCookie(res);
  if (!cookie) die(`No ${WEB_COOKIE} cookie: ${res.status} ${res.body}`);
  return { cookie };
}

// What the client does after its login: the account ticket, then the character screen on the
// socket -- the account's first character, or a new one « Sans classe » -- entered into the world.
// Answers the socket past AuthenticationOK, with the RoomForward when there is a start zone.
async function enterWorld() {
  const base = `http://127.0.0.1:${WEBAPI}`;
  const headers = { cookie: (await webSession()).cookie };
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

  const from = g.frames.length;
  g.send(frame(H.rpgEnterWorld, playerId));
  const forward = await g.wait([H.roomForward], from);
  if (!g.frames.slice(from).some((f) => f.header === H.authOk)) fail(`RpgEnterWorld(${playerId}): no AuthenticationOK.`);
  log(`Player ${playerId} entered the world.`);
  return { g, forward, from };
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
      // Room objects 1 to 3: a closed door on (2,2), a sign on (1,1), and a closed gate on (6,2)
      // that only the market's trigger opens.
      decor: [
        { id: 'door', definition: THRONE, class: 'throne', x: 2, y: 2, interaction: 'door' },
        { id: 'sign', definition: THRONE, class: 'throne', x: 1, y: 1, interaction: 'sign', tag: 'Bienvenue à Preuve A' },
        { id: 'gate', definition: THRONE, class: 'throne', x: 6, y: 2, interaction: 'door' },
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

  log(`proof_a: arrived on (${(await enter(rooms.proof_a)).join(',')}) (door 0,5)`);

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

// The item grant (spec Objets §10): the admin route gives the throwaway account's first character a
// sword, twice with the same key -- `granted` then `duplicate_grant`, and exactly one more sword row.
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
  log(`Player ${playerId}: ${before} -> ${after} swords; the replayed key granted nothing. Items OK.`);
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
  items     the admin item grant: a sword to the first character, replayed with its key -> one sword more
  grace [wait]  drop the socket in a room and come back: same room, one avatar, same tile (wait: then stay away past 60 s)
  cleanup   delete the throwaway account and its players`);
  process.exit(cmd ? 1 : 0);
}

await table[cmd](...args);
