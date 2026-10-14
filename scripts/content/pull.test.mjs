// node --test scripts/content/pull.test.mjs
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { planPull } from './pull.mjs';

const exported = (files, texts = { fr: 'item.weapon.sword.name=Épée\n' }, migrations = '[]\n') => ({
  version: 7,
  files,
  migrations,
  texts,
});

test('a path that would leave content/ is refused, and nothing is planned', () => {
  for (const path of ['../x.json', 'item/../../x.json', '/etc/x.json', 'C:/x.json', 'item\\x.json', 'item/X.json', 'migrations.json']) {
    assert.throws(() => planPull(exported([{ path, text: '{}' }]), []), /refused/, path);
  }
});

test('a text language that is no language code is refused', () => {
  assert.throws(() => planPull(exported([], { '../fr': 'x=y\n' }), []), /refused/);
});

test('the pull writes every document, the texts, the migrations and the version; it removes what left', () => {
  const plan = planPull(exported([{ path: 'item/sword.json', text: '{ "id": "item.weapon.sword" }\n' }]), [
    'item/sword.json',
    'item/old.json',
  ]);

  assert.deepEqual(
    plan.writes.map((w) => w.path),
    ['item/sword.json', 'texts/fr.txt', 'migrations.json', '.pack-version'],
  );
  assert.equal(plan.writes.at(-1).text, '7\n');
  assert.deepEqual(plan.removes, ['item/old.json']);
});

test('no migrations in the export leaves the repository file alone', () => {
  const plan = planPull(exported([], {}, null), []);

  assert.deepEqual(
    plan.writes.map((w) => w.path),
    ['.pack-version'],
  );
});
