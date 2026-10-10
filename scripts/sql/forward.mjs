#!/usr/bin/env node
// Applies the forward scripts (scripts/sql/forward/NNN-<name>.sql) to a live database, in order,
// each one once. The schema of a database that cannot be recreated moves by these scripts, not by
// the regenerated InitialCreate; until this runner, their order lived in comments and nothing told
// a database which one it had skipped (audit P0-8).
//
//   node scripts/sql/forward.mjs            apply what is pending
//   node scripts/sql/forward.mjs --list     show the chain and what the database has
//   node scripts/sql/forward.mjs --dry-run  say what would run, run nothing
//
// The database is VORTEX__Vortex__Database__ConnectionString, else Vortex.Main's user-secrets (the
// same rule as the driver). The password travels in MYSQL_PWD and is never printed. The mysql client
// is VORTEX_MYSQL, else Laragon's, else `mysql` on the PATH (CI).
//
// A script already in `vortex_forward_history` is skipped; the first failure stops the run with
// nothing recorded for it. The scripts stay re-runnable on their own (IF NOT EXISTS, guarded
// procedures): the history is a belt, not the trousers.
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HISTORY_TABLE = 'vortex_forward_history';
const NAME_PATTERN = /^(\d{3})-[a-z0-9-]+\.sql$/;

/**
 * The chain, validated: every file is `NNN-<name>.sql`, numbered from 001 without a gap or a
 * duplicate, returned in order. Anything else is a chain nobody can apply with confidence.
 */
export function parseChain(fileNames) {
  const scripts = fileNames
    .filter((n) => n.endsWith('.sql'))
    .map((name) => {
      const m = NAME_PATTERN.exec(name);
      if (!m) throw new Error(`forward script '${name}' is not named NNN-<name>.sql`);
      return { number: Number(m[1]), name };
    })
    .sort((a, b) => a.number - b.number);

  scripts.forEach((s, i) => {
    if (s.number !== i + 1) {
      throw new Error(
        `forward scripts must be numbered 001, 002… without a gap or a duplicate: got '${s.name}' at position ${i + 1}`,
      );
    }
  });

  return scripts.map((s) => s.name);
}

/** What the database still has to run, in chain order; an applied name the chain no longer has is reported. */
export function pending(chain, applied) {
  const known = new Set(chain);
  const unknown = [...applied].filter((a) => !known.has(a));
  return { toRun: chain.filter((name) => !applied.has(name)), unknown };
}

/** The pieces of a connection string this runner needs; the password stays in the object, never in a log. */
export function parseConnection(connectionString) {
  const part = (k) => connectionString.match(new RegExp(`(?:^|;)\\s*${k}\\s*=\\s*([^;]*)`, 'i'))?.[1]?.trim();
  return {
    host: part('Server') ?? part('Host') ?? '127.0.0.1',
    port: part('Port') ?? '3306',
    user: part('User Id') ?? part('User') ?? part('Uid') ?? 'root',
    password: part('Password') ?? part('Pwd') ?? '',
    database: part('Database') ?? part('Initial Catalog') ?? '',
  };
}

function repoRoot() {
  return path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
}

function connectionString(root) {
  const fromEnv = process.env.VORTEX__Vortex__Database__ConnectionString;
  if (fromEnv) return fromEnv;
  const out = spawnSync('dotnet', ['user-secrets', 'list', '--project', path.join(root, 'Vortex.Main')], {
    encoding: 'utf8',
  });
  const cs = (out.stdout || '').match(/Vortex:Database:ConnectionString = (.*)/)?.[1];
  if (!cs) {
    throw new Error(
      'No database: set VORTEX__Vortex__Database__ConnectionString or Vortex:Database:ConnectionString in Vortex.Main user-secrets.',
    );
  }
  return cs.trim();
}

function mysqlExe() {
  if (process.env.VORTEX_MYSQL) return process.env.VORTEX_MYSQL;
  const laragon = 'C:\\laragon\\bin\\mysql';
  if (fs.existsSync(laragon)) {
    for (const dir of fs.readdirSync(laragon)) {
      const exe = path.join(laragon, dir, 'bin', 'mysql.exe');
      if (fs.existsSync(exe)) return exe;
    }
  }
  return 'mysql';
}

function mysql(conn, args, input) {
  const out = spawnSync(
    mysqlExe(),
    [`--host=${conn.host}`, `--port=${conn.port}`, `--user=${conn.user}`, '--batch', '--skip-column-names', ...args],
    { encoding: 'utf8', input, env: { ...process.env, MYSQL_PWD: conn.password } },
  );
  if (out.error) throw new Error(`mysql could not start: ${out.error.message}`);
  if (out.status !== 0) throw new Error(`mysql failed: ${out.stderr.trim()}`);
  return out.stdout;
}

function main(argv) {
  const list = argv.includes('--list');
  const dryRun = argv.includes('--dry-run');
  const root = repoRoot();
  const dir = path.join(root, 'scripts', 'sql', 'forward');
  const chain = parseChain(fs.readdirSync(dir));
  const conn = parseConnection(connectionString(root));
  if (!conn.database) throw new Error('The connection string names no database.');

  mysql(conn, [conn.database, '-e',
    `CREATE TABLE IF NOT EXISTS \`${HISTORY_TABLE}\` (` +
      '`name` varchar(128) NOT NULL PRIMARY KEY, ' +
      '`applied_at` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)) CHARACTER SET = utf8mb4;']);

  const applied = new Set(
    mysql(conn, [conn.database, '-e', `SELECT name FROM \`${HISTORY_TABLE}\` ORDER BY name;`])
      .split(/\r?\n/)
      .filter(Boolean),
  );
  const { toRun, unknown } = pending(chain, applied);

  console.log(`database ${conn.database} on ${conn.host}:${conn.port} — ${chain.length} script(s) in the chain, ${applied.size} applied, ${toRun.length} pending`);
  for (const name of chain) console.log(`  ${applied.has(name) ? 'applied' : 'PENDING'}  ${name}`);
  for (const name of unknown) console.log(`  unknown  ${name}  (in the history, no longer in the chain)`);

  if (list || dryRun || toRun.length === 0) return 0;

  for (const name of toRun) {
    const sql = fs.readFileSync(path.join(dir, name), 'utf8');
    process.stdout.write(`applying ${name}… `);
    // The file is fed to the client as a script: DELIMITER lines and multi-statement procedures work
    // there, which no driver-side execution of the text would do.
    mysql(conn, [conn.database], sql);
    mysql(conn, [conn.database, '-e', `INSERT INTO \`${HISTORY_TABLE}\` (name) VALUES ('${name}');`]);
    console.log('ok');
  }

  return 0;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    process.exit(main(process.argv.slice(2)));
  } catch (error) {
    console.error(`forward: ${error.message}`);
    process.exit(1);
  }
}
