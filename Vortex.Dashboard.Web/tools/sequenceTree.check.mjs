// Editing a condition tree must change the condition the operator pointed at, and only that one.
//
// The editor rebuilds these objects on every keystroke, so a node is addressed by its path rather
// than by identity. A path walked wrong does not throw and does not look wrong: it writes the
// change into a different condition, one the operator is not looking at. That is what this checks.

import assert from 'node:assert/strict';
import {
  ALL,
  ANY,
  NOT,
  TEST,
  appendTo,
  fromRows,
  mapTests,
  nodeAt,
  pathKey,
  referencedStep,
  removeAt,
  replaceAt,
  shiftAt,
  tests,
  toRows,
  unwrapAt,
  wrapAt,
} from '../src/lib/sequence/tree.ts';

const test = (value) => ({ kind: TEST, factKey: 'item', op: 0, value });
const group = (kind, ...children) => ({ kind, children });

// "that sofa, and either not that other one or room 12"
const tree = () => [
  test('500'),
  group(ANY, group(NOT, test('900')), test('12')),
];

// --- finding a node ---------------------------------------------------------------------------
assert.equal(nodeAt(tree(), [0]).value, '500');
assert.equal(nodeAt(tree(), [1]).kind, ANY);
assert.equal(nodeAt(tree(), [1, 0]).kind, NOT);
assert.equal(nodeAt(tree(), [1, 0, 0]).value, '900');
assert.equal(nodeAt(tree(), [1, 1]).value, '12');
assert.equal(nodeAt(tree(), [9]), null, 'a path naming nothing is null, not a throw');
assert.equal(nodeAt(tree(), [0, 0]), null, 'a test has no children to walk into');

// --- replacing one, and nothing else -----------------------------------------------------------
{
  const next = replaceAt(tree(), [1, 0, 0], test('901'));

  assert.equal(nodeAt(next, [1, 0, 0]).value, '901');
  assert.equal(nodeAt(next, [0]).value, '500', 'its cousins are untouched');
  assert.equal(nodeAt(next, [1, 1]).value, '12');
  assert.notEqual(next, tree(), 'a new array, so the editor can see the change');
}

// --- removing one takes what is under it -------------------------------------------------------
{
  const next = removeAt(tree(), [1, 0]);

  assert.equal(nodeAt(next, [1]).children.length, 1);
  assert.equal(nodeAt(next, [1, 0]).value, '12', 'the sibling slid down into the gap');
}

// --- adding -------------------------------------------------------------------------------------
{
  assert.equal(appendTo(tree(), [], test('7')).length, 3, 'an empty path is the step itself');
  assert.equal(nodeAt(appendTo(tree(), [1], test('7')), [1, 2]).value, '7');
  assert.deepEqual(
    appendTo(tree(), [0], test('7')),
    tree(),
    'a test cannot hold children, so nothing happens rather than a shape the server refuses',
  );
}

// --- wrapping and unwrapping ---------------------------------------------------------------------
{
  const wrapped = wrapAt(tree(), [0], NOT);

  assert.equal(nodeAt(wrapped, [0]).kind, NOT);
  assert.equal(nodeAt(wrapped, [0, 0]).value, '500', 'the condition is now inside it');

  const back = unwrapAt(wrapped, [0]);

  assert.equal(nodeAt(back, [0]).value, '500', 'and out again');
  assert.equal(back.length, 2);
}

{
  // Unwrapping a group spills its children where it was, rather than leaving them nested under
  // something the operator asked to delete.
  const next = unwrapAt(tree(), [1]);

  assert.equal(next.length, 3);
  assert.equal(nodeAt(next, [1]).kind, NOT);
  assert.equal(nodeAt(next, [2]).value, '12');
}

{
  // The one case that cannot spill: a `not` holds exactly one condition, and the validator refuses
  // anything else. Better to do nothing than to leave a shape that will not save.
  const nested = [group(NOT, group(ALL, test('1'), test('2')))];

  assert.deepEqual(unwrapAt(nested, [0, 0]), nested);
}

// --- moving among siblings -------------------------------------------------------------------
{
  const next = shiftAt(tree(), [1], -1);

  assert.equal(nodeAt(next, [0]).kind, ANY);
  assert.equal(nodeAt(next, [1]).value, '500');
  assert.deepEqual(shiftAt(tree(), [0], -1), tree(), 'the first one has nowhere earlier to go');
  assert.deepEqual(shiftAt(tree(), [1], 1), tree(), 'nor the last one later');
}

{
  const next = shiftAt(tree(), [1, 1], -1);

  assert.equal(nodeAt(next, [1, 0]).value, '12', 'inside a group too');
  assert.equal(nodeAt(next, [1, 1]).kind, NOT);
}

// --- every test, wherever it is ----------------------------------------------------------------
{
  const found = tests(tree());

  assert.deepEqual(
    found.map((f) => f.node.value),
    ['500', '900', '12'],
  );
  assert.deepEqual(
    found.map((f) => f.path),
    [[0], [1, 0, 0], [1, 1]],
    'a condition three levels down is still reached',
  );
}

{
  // What the reference rewrite is built on: every test changed, the shape untouched.
  const next = mapTests(tree(), (node) => ({ ...node, value: `${node.value}!` }));

  assert.equal(nodeAt(next, [0]).value, '500!');
  assert.equal(nodeAt(next, [1, 0, 0]).value, '900!');
  assert.equal(nodeAt(next, [1, 1]).value, '12!');
  assert.equal(nodeAt(next, [1]).kind, ANY);
  assert.equal(nodeAt(next, [1, 0]).kind, NOT);
}

// --- what the editor remembers per condition ---------------------------------------------------
assert.equal(pathKey(0, [1, 0, 0]), '0:1.0.0');
assert.notEqual(pathKey(0, [1, 0]), pathKey(1, [1, 0]), 'two steps never share a key');

// --- reading a reference -----------------------------------------------------------------------
assert.equal(referencedStep('$0'), 0);
assert.equal(referencedStep('4312'), -1);
assert.equal(referencedStep('$-1'), -1);

// --- crossing the wire ---------------------------------------------------------------------------
{
  // The shape the server sends: a test's fields live under `test`, not on the node.
  const rows = [
    { kind: TEST, test: { factKey: 'definition', op: 0, value: '500' }, children: null },
    {
      kind: ANY,
      test: null,
      children: [
        { kind: NOT, test: null, children: [{ kind: TEST, test: { factKey: 'definition', op: 0, value: '900' } }] },
        { kind: TEST, test: { factKey: 'room', op: 0, value: '12' }, children: null },
      ],
    },
  ];

  const held = fromRows(rows);

  assert.equal(held[0].factKey, 'definition');
  assert.equal(held[0].value, '500');
  assert.equal(nodeAt(held, [1, 0, 0]).value, '900', 'three levels down survives the crossing');
  assert.equal(nodeAt(held, [1, 1]).factKey, 'room');

  // And back out unchanged, which is what stops an edit-and-save from quietly rewriting a task
  // nobody touched.
  const back = toRows(held);

  assert.equal(back[0].test.value, '500');
  assert.equal(back[1].kind, ANY);
  assert.equal(back[1].children[0].children[0].test.value, '900');
  assert.equal(back[1].children[1].test.factKey, 'room');
  assert.equal(back[0].children, null, 'a test carries no children across');
  assert.equal(back[1].test, null, 'nor a group a test');
}

assert.deepEqual(fromRows(null), [], 'a task with no conditions is not a crash');
assert.deepEqual(fromRows(undefined), []);

{
  // Trimmed on the way out, so the value the server stores is the value every path would store.
  const rows = toRows([{ kind: TEST, factKey: 'room', op: '4', value: '  12  ' }]);

  assert.equal(rows[0].test.value, '12');
  assert.equal(rows[0].test.op, 4, 'a select hands back a string; the server wants the number');
}

console.log('sequence tree: ok');
