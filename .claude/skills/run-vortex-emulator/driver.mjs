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
};

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

// Registration, or login when the address exists: the site's web session.
async function webSession() {
  const base = `http://127.0.0.1:${WEBAPI}`;
  const json = (o) => JSON.stringify(o);
  let res = await request(`${base}/api/public/registration/new`, {
    method: 'POST',
    body: json({ email: PLAYER.email, password: PLAYER.password, passwordRepeated: PLAYER.password }),
  });
  if (res.status === 409) {
    res = await request(`${base}/api/public/authentication/login`, {
      method: 'POST',
      body: json({ email: PLAYER.email, password: PLAYER.password }),
    });
  }
  const cookie = sessionCookie(res);
  if (!cookie) die(`No ${WEB_COOKIE} cookie: ${res.status} ${res.body}`);
  return { cookie };
}

// The web session, an avatar, then a short-lived SSO ticket -- exactly what the site does before
// it opens the client.
async function ssoTicket() {
  const base = `http://127.0.0.1:${WEBAPI}`;
  const json = (o) => JSON.stringify(o);
  const headers = { cookie: (await webSession()).cookie };
  let res;

  let avatars = JSON.parse((await request(`${base}/api/user/avatars`, { headers })).body);
  if (!avatars.length) {
    res = await request(`${base}/api/user/avatars`, {
      method: 'POST',
      headers,
      body: json({ name: '', figure: 'hd-180-1.ch-210-66.lg-270-82.sh-290-80', gender: 'M' }),
    });
    if (res.status !== 200) die(`Avatar creation failed: ${res.status} ${res.body}`);
    avatars = JSON.parse(res.body);
  }
  res = await request(`${base}/api/user/avatars/select`, {
    method: 'POST',
    headers,
    body: json({ uniqueId: avatars[0].uniqueId }),
  });
  if (res.status !== 200) die(`Avatar selection failed: ${res.status} ${res.body}`);
  res = await request(`${base}/api/ssotoken`, { headers });
  if (res.status !== 200) die(`SSO ticket refused: ${res.status} ${res.body}`);
  log(`Player ${avatars[0].name} (id ${avatars[0].uniqueId}) holds an SSO ticket.`);
  return JSON.parse(res.body).ssoToken;
}

// The core loop, end to end: SSO -> AuthenticationOK, UserRights, RoomForward -> room entry ->
// heightmap and avatars -> one step -> the step echoed back.
async function cmdPlay() {
  const ticket = await ssoTicket();
  const g = await gameSocket().catch((e) => die(`Socket error on :${GAME_TCP}: ${e.message}`));
  const fail = (m) => {
    g.close();
    die(m);
  };

  g.send(frame(H.sso, ticket, 0));
  const forward = await g.wait([H.roomForward]);
  const seen = g.frames.map((f) => f.header);
  log(`SSO -> ${seen.join(', ')}`);
  if (!seen.includes(H.authOk)) fail('No AuthenticationOK: the ticket was refused.');
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

  const ticket = await ssoTicket();
  const g = await gameSocket().catch((e) => die(`Socket error on :${GAME_TCP}: ${e.message}`));
  const fail = (m) => {
    g.close();
    die(m);
  };
  g.send(frame(H.sso, ticket, 0));
  if (!(await g.wait([H.authOk]))) fail('No AuthenticationOK.');

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
    },
    proof_a: {
      schemaVersion: 1,
      settings: { id: 'proof_a', name: 'Preuve A' },
      floor,
      zones: [{ id: 'market', kind: 'Safe', name: 'Marché', polygon: [{ x: 3, y: 0 }, { x: 5, y: 0 }, { x: 5, y: 9 }, { x: 3, y: 9 }] }],
      passages: [{ id: 'road', area: [{ x: 7, y: 5 }], destinationZone: 'proof_b', destinationEntry: 'gate' }],
      // Room objects 1 and 2: a closed door on (2,2) and a sign on (1,1).
      decor: [
        { id: 'door', definition: THRONE, class: 'throne', x: 2, y: 2, interaction: 'door' },
        { id: 'sign', definition: THRONE, class: 'throne', x: 1, y: 1, interaction: 'sign', tag: 'Bienvenue à Preuve A' },
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

  const ticket = await ssoTicket();
  const g = await gameSocket().catch((e) => die(`Socket error on :${GAME_TCP}: ${e.message}`));
  const fail = (m) => {
    g.close();
    die(m);
  };
  g.send(frame(H.sso, ticket, 0));
  if (!(await g.wait([H.authOk]))) fail('No AuthenticationOK.');

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
  g.send(frame(H.useFurniture, 1, 0));
  await sleep(500);
  g.send(frame(H.moveAvatar, 2, 2));
  await sleep(2500);
  if (!movesOnto(2, 2, mark)) fail('The door, used, did not let the avatar through.');
  log('Closed door refused the step; UseFurniture opened it; walked onto it.');
  mark = g.frames.length;
  g.send(frame(H.useFurniture, 2, 0));
  const sign = await g.wait([H.notificationDialog], mark, 5000);
  if (!sign || !sign.body.includes('Bienvenue')) fail('The sign said nothing.');
  log('UseFurniture on the sign -> NotificationDialog "Bienvenue à Preuve A".');
  if (!(await walk(4, 5, [H.notificationDialog]))) fail('No bubble on entering the Marché area.');
  log('Walked into Marché -> NotificationDialog.');
  const forward = await walk(7, 5, [H.roomForward]);
  if (!forward) fail('No RoomForward from the passage.');
  const to = forward.body.readInt32BE(0);
  if (to !== rooms.proof_b) fail(`Forwarded to ${to}, expected ${rooms.proof_b}.`);
  log(`Passage -> RoomForward ${to}.`);
  const at = await enter(to);
  g.close();
  if (at[0] !== 2 || at[1] !== 2) die(`Arrived on (${at.join(',')}), expected the passage's entry (2,2).`);
  log(`proof_b: arrived on (2,2), the passage's entry. Travel OK.`);
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

  const ticket = await ssoTicket();
  const g = await gameSocket().catch((e) => die(`Socket error on :${GAME_TCP}: ${e.message}`));
  g.send(frame(H.sso, ticket, 0));
  if (!(await g.wait([H.authOk]))) die('No AuthenticationOK.');
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
  play      throwaway player -> SSO -> start zone -> heightmap -> one step
  zone      publish a zone through /api/editor, enter it, republish while inside
  enter <k> enter the published zone <k> and count the floor items it sends
  travel    two proof zones: named area bubble, passage A -> B, arrival on the passage's entry
  cleanup   delete the throwaway account and its players`);
  process.exit(cmd ? 1 : 0);
}

await table[cmd](...args);
