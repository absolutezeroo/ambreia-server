#!/usr/bin/env node
// Driver for the Vortex Cloud emulator. Launches Vortex.Main and pokes its three live surfaces:
// the game socket (raw Habbo frames on :40000), the web API (:8080) and the operator dashboard
// (:9000, SPA + API behind a session cookie).
//
// Zero dependencies on purpose -- node:net, node:http, node:child_process and the global
// WebSocket that Node 22+ ships (used to talk CDP to headless Edge for screenshots).
//
// Run state (pid file, console log, dashboard cookie, screenshots) lives in
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
const COOKIE_FILE = path.join(STATE, 'dash-cookie.txt');

// appsettings.Development.json, when present, moves the game sockets off the 30000/30001 of
// appsettings.json. VORTEX_GAME_PORT names the port when it was moved another way (the unprefixed
// serverOptions__TcpServer__listeners__0__port variable).
const GAME_TCP =
  Number(process.env.VORTEX_GAME_PORT) ||
  (fs.existsSync(path.join(REPO, 'appsettings.Development.json')) ? 40000 : 30000);
const WEBAPI = 8080;
// :9000 serves the copy of the SPA embedded in the assembly at its last build. While iterating on
// the front end the live page is Vite's on :9001, and a cookie ignores the port, so the session
// `login` writes works against either: VORTEX_DASH_PORT=9001 shoots the dev server.
const DASH = Number(process.env.VORTEX_DASH_PORT) || 9000;

// A throwaway owner used for the dashboard session. Kept at a fixed address so re-runs reuse the
// row instead of littering player_accounts; `cleanup` deletes it.
const OPERATOR = { email: 'run-skill@vortex.local', password: 'RunSkill!2026' };
const OWNER_ROLE_ID = 5;

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
  const cs = (out.stdout || '').match(/Vortex:Database:ConnectionString = (.*)/)?.[1] ?? '';
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
  for (let i = 0; i < 20; i++) {
    await sleep(500);
    if (await portOpen(3306)) return log(`MySQL up on 3306 (${mysqld}).`);
  }
  die('MySQL did not come up within 10s.');
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
    log('Reusing it. Do not stop it -- drive it with `ping`, `health`, `api`.');
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

  // Startup is slow: Orleans silo, ~160 tables of catalogue/furniture warm-up, plugin scan.
  for (let i = 0; i < 180; i++) {
    await sleep(1000);
    if (!runningPid()) {
      log(fs.readFileSync(LOG_FILE, 'utf8').split(/\r?\n/).slice(-30).join('\n'));
      die('Vortex.Main exited during startup (log tail above).');
    }
    if ((await portOpen(GAME_TCP)) && (await portOpen(DASH))) {
      return log(`Ready after ~${i + 1}s: game :${GAME_TCP}, webapi :${WEBAPI}, dashboard :${DASH}`);
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
    ['dash    ', DASH],
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
// Plaintext until the Diffie handshake runs -- SetupEncryption is what installs the RC4 engines,
// so an unauthenticated probe never needs to encrypt anything.
function frame(header, ints = []) {
  const body = Buffer.alloc(2 + 4 * ints.length);
  body.writeInt16BE(header, 0);
  ints.forEach((v, i) => body.writeInt32BE(v, 2 + i * 4));
  const out = Buffer.alloc(4 + body.length);
  out.writeInt32BE(body.length, 0);
  body.copy(out, 4);
  return out;
}

const LATENCY_PING_REQUEST = 544; // -> LatencyPingRequestMessageHandler
const LATENCY_PING_RESPONSE = 188; // <- LatencyPingResponseMessageSerializer

// The cheapest end-to-end proof the emulator is alive: it walks the socket, the pipeline filter,
// the packet decoder, the revision parser map, the handler, the composer, the serializer and the
// encoder -- and needs no login. A green build proves none of that.
function cmdPing(requestId = 1234) {
  return new Promise((resolve) => {
    const id = Number(requestId);
    const s = net.connect({ port: GAME_TCP, host: '127.0.0.1' });
    let buf = Buffer.alloc(0);
    const fail = (m) => {
      s.destroy();
      die(m);
    };
    s.setTimeout(5000, () => fail(`No reply within 5s on :${GAME_TCP}.`));
    s.on('error', (e) => fail(`Socket error on :${GAME_TCP}: ${e.message}`));
    s.on('connect', () => s.write(frame(LATENCY_PING_REQUEST, [id])));
    s.on('data', (chunk) => {
      buf = Buffer.concat([buf, chunk]);
      if (buf.length < 4) return;
      const len = buf.readInt32BE(0);
      if (buf.length < 4 + len) return;
      const header = buf.readInt16BE(4);
      const echoed = len >= 6 ? buf.readInt32BE(6) : null;
      s.destroy();
      if (header !== LATENCY_PING_RESPONSE) fail(`Expected header ${LATENCY_PING_RESPONSE}, got ${header}.`);
      if (echoed !== id) fail(`Expected requestId ${id} echoed back, got ${echoed}.`);
      log(`game socket OK: sent ${LATENCY_PING_REQUEST}(${id}) -> got ${header}(${echoed})`);
      resolve();
    });
  });
}

// ---------------------------------------------------------------- http surfaces

async function cmdHealth() {
  const health = await request(`http://127.0.0.1:${WEBAPI}/health`);
  log(`GET :${WEBAPI}/health -> ${health.status} ${health.body}`);
  const hello = await request(`http://127.0.0.1:${WEBAPI}/api/public/info/hello`);
  log(`GET :${WEBAPI}/api/public/info/hello -> ${hello.status} ${hello.body}`);
  const metrics = await request(`http://127.0.0.1:${WEBAPI}/metrics`);
  const gauges = metrics.body
    .split('\n')
    .filter((l) => /^Vortex_(sessions_active|players_online|rooms_active)/.test(l))
    .join('  ');
  log(`GET :${WEBAPI}/metrics -> ${metrics.status}  ${gauges || '(no Vortex_* gauges yet)'}`);
  const dash = await request(`http://127.0.0.1:${DASH}/`);
  log(`GET :${DASH}/ -> ${dash.status} (${dash.body.length} bytes of SPA shell)`);
}

// Registration mints the bcrypt hash, so no hand-crafted password SQL. The role grant is the one
// step the HTTP surface does not expose -- the dashboard authorizes on capabilities, and role 5
// (owner) is the one carrying `*`.
async function cmdLogin() {
  const reg = await request(`http://127.0.0.1:${WEBAPI}/api/public/registration/new`, {
    method: 'POST',
    body: JSON.stringify({
      email: OPERATOR.email,
      password: OPERATOR.password,
      passwordRepeated: OPERATOR.password,
    }),
  });
  if (reg.status !== 200 && reg.status !== 409) die(`Registration failed: ${reg.status} ${reg.body}`);
  log(reg.status === 200 ? `Registered ${OPERATOR.email}.` : `${OPERATOR.email} already exists, reusing.`);

  const id = sql(`SELECT id FROM player_accounts WHERE email='${OPERATOR.email}' LIMIT 1;`);
  if (!id) die('Account row not found after registration.');
  sql(
    `INSERT INTO player_account_roles (account_id, role_id, created_at, updated_at) ` +
      `SELECT ${id}, ${OWNER_ROLE_ID}, NOW(6), NOW(6) FROM DUAL ` +
      `WHERE NOT EXISTS (SELECT 1 FROM player_account_roles WHERE account_id=${id} AND role_id=${OWNER_ROLE_ID});`,
  );

  const res = await request(`http://127.0.0.1:${DASH}/api/login`, {
    method: 'POST',
    body: JSON.stringify({ email: OPERATOR.email, password: OPERATOR.password }),
  });
  if (res.status !== 200) die(`Dashboard login failed: ${res.status} ${res.body}`);
  const setCookie = [].concat(res.headers['set-cookie'] || []).find((c) => c.startsWith('dash_session='));
  if (!setCookie) die('No dash_session cookie in the login response.');
  const cookie = setCookie.split(';')[0];
  fs.writeFileSync(COOKIE_FILE, cookie);
  log(`Dashboard session for account ${id} (role ${OWNER_ROLE_ID}) -> ${COOKIE_FILE}`);
}

function cookie() {
  if (!fs.existsSync(COOKIE_FILE)) die('No session. Run: node .../driver.mjs login');
  return fs.readFileSync(COOKIE_FILE, 'utf8').trim();
}

async function cmdApi(p) {
  if (!p) die('usage: api /api/<path>');
  const res = await request(`http://127.0.0.1:${DASH}${p}`, { headers: { cookie: cookie() } });
  log(`GET :${DASH}${p} -> ${res.status}`);
  if (res.status === 401)
    // Sessions live in a ConcurrentDictionary, not the database: a restart of the host voids every
    // cookie, and the only symptom is a 401 on a path that worked a minute ago.
    log('  (401 -- the emulator restarted since `login`. Re-run `login`.)');
  log(res.body.length > 4000 ? res.body.slice(0, 4000) + `\n... (${res.body.length} bytes)` : res.body);
}

function cmdCleanup() {
  const id = sql(`SELECT id FROM player_accounts WHERE email='${OPERATOR.email}' LIMIT 1;`);
  if (!id) return log('Nothing to clean up.');
  sql(`DELETE FROM player_account_roles WHERE account_id=${id};`);
  sql(`DELETE FROM player_accounts WHERE id=${id};`);
  fs.rmSync(COOKIE_FILE, { force: true });
  log(`Deleted the throwaway operator account ${id}.`);
}

// ---------------------------------------------------------------- screenshots

function edgeExe() {
  const candidates = [
    process.env.VORTEX_EDGE,
    'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
    'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
  ].filter(Boolean);
  return candidates.find((c) => fs.existsSync(c)) ?? null;
}

// Headless Edge driven over CDP. `--screenshot` alone cannot photograph a dashboard page: every
// route but the login screen is behind dash_session, and a plain headless load only ever captures
// the login form. Injecting the cookie needs a debugger connection, so that is what this does.
async function cmdShot(route = '/', out = null) {
  const exe = edgeExe();
  if (!exe) die('Edge not found. Set VORTEX_EDGE to msedge.exe.');
  const outFile = out ?? path.join(STATE, `shot${route.replace(/[^a-z0-9]+/gi, '-')}.png`);
  const port = 9500 + Math.floor(Math.random() * 400);
  const profile = path.join(STATE, `edge-profile-${port}`);

  const edge = spawn(
    exe,
    [
      '--headless=new',
      '--disable-gpu',
      '--hide-scrollbars',
      '--window-size=1600,1000',
      `--remote-debugging-port=${port}`,
      `--user-data-dir=${profile}`,
      'about:blank',
    ],
    { stdio: 'ignore' },
  );

  try {
    let target = null;
    for (let i = 0; i < 40 && !target; i++) {
      await sleep(250);
      try {
        const list = JSON.parse((await request(`http://127.0.0.1:${port}/json/list`)).body);
        target = list.find((t) => t.type === 'page');
      } catch {
        /* devtools not listening yet */
      }
    }
    if (!target) die('Edge devtools endpoint never came up.');

    const ws = new WebSocket(target.webSocketDebuggerUrl);
    await new Promise((r, j) => {
      ws.onopen = r;
      ws.onerror = j;
    });
    let seq = 0;
    const pending = new Map();
    const events = new Map();
    ws.onmessage = (m) => {
      const msg = JSON.parse(m.data);
      if (msg.id && pending.has(msg.id)) {
        pending.get(msg.id)(msg.result);
        pending.delete(msg.id);
      } else if (msg.method && events.has(msg.method)) {
        events.get(msg.method)();
        events.delete(msg.method);
      }
    };
    const send = (method, params = {}) =>
      new Promise((r) => {
        const id = ++seq;
        pending.set(id, r);
        ws.send(JSON.stringify({ id, method, params }));
      });

    await send('Network.enable');
    await send('Page.enable');
    if (fs.existsSync(COOKIE_FILE)) {
      const raw = cookie();
      const name = raw.slice(0, raw.indexOf('='));
      const value = raw.slice(raw.indexOf('=') + 1);
      // `url` rather than `domain`: with domain alone Edge accepts the call and stores nothing,
      // and the only symptom is a screenshot of the login form. secure:false because the dashboard
      // drops Secure when HTTPS is off, and this browser talks plain http to loopback.
      const set = await send('Network.setCookie', {
        name,
        value,
        url: `http://127.0.0.1:${DASH}/`,
        path: '/',
        secure: false,
        sameSite: 'Lax',
      });
      if (!set.success) die('CDP refused the dash_session cookie; re-run `login`.');
    }
    // The theme is a localStorage preference, not an account setting, and a fresh profile has
    // none -- so every screenshot came out in the default blue while the operator complaining
    // about the page was looking at dark. Seed it before the SPA boots and reads it once.
    if (process.env.VORTEX_DASH_THEME) {
      await send('Page.addScriptToEvaluateOnNewDocument', {
        source: `try { localStorage.setItem('turbo-dashboard-theme', ${JSON.stringify(process.env.VORTEX_DASH_THEME)}); } catch {}`,
      });
    }
    const loaded = new Promise((r) => events.set('Page.loadEventFired', r));
    await send('Page.navigate', { url: `http://127.0.0.1:${DASH}${route}` });
    await loaded;
    await sleep(2500); // the SPA hydrates and fetches after load; without this the shot is a skeleton
    // Half the things worth photographing are behind a click -- an expanded row, an open editor --
    // and they live in component state, not in the URL, so no route reaches them.
    //   VORTEX_DASH_CLICK="[...document.querySelectorAll('button')].find(b => /Produits/i.test(b.textContent))?.click()"
    if (process.env.VORTEX_DASH_CLICK) {
      const r = await send('Runtime.evaluate', { expression: process.env.VORTEX_DASH_CLICK, awaitPromise: true, returnByValue: true });
      // Logged, so the same hook answers "why is this button the wrong colour" with a
      // getComputedStyle expression instead of another round of reading the stylesheet.
      if (r?.result?.value !== undefined) log(`eval -> ${JSON.stringify(r.result.value)}`);
      if (r?.exceptionDetails) log(`eval threw: ${r.exceptionDetails.text}`);
      await sleep(1500);
    }
    const shot = await send('Page.captureScreenshot', { format: 'png' });
    fs.writeFileSync(outFile, Buffer.from(shot.data, 'base64'));
    ws.close();
    log(`Screenshot -> ${outFile}`);
  } finally {
    edge.kill();
  }
}

// ---------------------------------------------------------------- smoke

async function cmdSmoke() {
  await cmdStart();
  await cmdHealth();
  await cmdPing();
  await cmdLogin();
  await cmdApi('/api/v1/monitoring/overview');
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
  login: cmdLogin,
  api: cmdApi,
  shot: cmdShot,
  cleanup: cmdCleanup,
  smoke: cmdSmoke,
};

if (!cmd || !table[cmd]) {
  log(`usage: node driver.mjs <command>

  smoke              start + health + ping + login + one dashboard read
  mysql              start Laragon MySQL if 3306 is closed
  start              launch Vortex.Main (reuses one already running; never stops it)
  status             pids and the four ports
  stop               stop ONLY an emulator this driver started
  logs [n]           tail the console log of the emulator this driver started
  health             /health, /api/public/info/hello, /metrics, dashboard shell
  ping [id]          raw Habbo frame on :${GAME_TCP} -- 544 out, 188 back
  login              throwaway owner account + dashboard session cookie
  api <path>         authenticated GET against the dashboard API
  shot [route] [png] headless Edge screenshot of a dashboard route (uses the cookie).
                     The SPA is HASH-routed: '#/rooms', not '/rooms'.
  cleanup            delete the throwaway operator account`);
  process.exit(cmd ? 1 : 0);
}

await table[cmd](...args);
