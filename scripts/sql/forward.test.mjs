// node --test scripts/sql/forward.test.mjs
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseChain, parseConnection, pending } from './forward.mjs';

test('the chain is the numbered scripts, in order, whatever the directory listing order', () => {
  assert.deepEqual(parseChain(['002-b.sql', 'README.md', '001-a.sql', '003-c.sql']), [
    '001-a.sql',
    '002-b.sql',
    '003-c.sql',
  ]);
});

test('a gap in the numbering is refused: a skipped number is a skipped script', () => {
  assert.throws(() => parseChain(['001-a.sql', '003-c.sql']), /gap or a duplicate/);
});

test('a duplicate number is refused: two scripts cannot share a place in the order', () => {
  assert.throws(() => parseChain(['001-a.sql', '001-b.sql']), /gap or a duplicate/);
});

test('a script named outside the convention is refused', () => {
  assert.throws(() => parseChain(['2026-10-08-account-tickets.sql']), /NNN-<name>\.sql/);
});

test('pending is the chain minus what the database applied, in chain order', () => {
  const { toRun, unknown } = pending(['001-a.sql', '002-b.sql', '003-c.sql'], new Set(['001-a.sql', '003-c.sql']));
  assert.deepEqual(toRun, ['002-b.sql']);
  assert.deepEqual(unknown, []);
});

test('an applied script the chain no longer has is reported, not hidden', () => {
  const { unknown } = pending(['001-a.sql'], new Set(['001-a.sql', '002-gone.sql']));
  assert.deepEqual(unknown, ['002-gone.sql']);
});

test('the connection string gives host, port, user, database; the password stays a field', () => {
  assert.deepEqual(
    parseConnection('Server=db.local;Port=3307;Database=vortex;User Id=vortex;Password=s3cret;'),
    { host: 'db.local', port: '3307', user: 'vortex', password: 's3cret', database: 'vortex' },
  );
  assert.equal(parseConnection('Database=x').host, '127.0.0.1');
  // The short spellings MySqlConnector accepts too, with the spaces people type.
  assert.deepEqual(parseConnection('Host=h; Uid = u ; Pwd=p; Database = d'), {
    host: 'h',
    port: '3306',
    user: 'u',
    password: 'p',
    database: 'd',
  });
});
