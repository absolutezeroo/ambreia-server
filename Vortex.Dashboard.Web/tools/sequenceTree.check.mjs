// Editing a condition tree must change the condition the operator pointed at, and only that one.
//
// The editor rebuilds these objects on every keystroke, so a node is addressed by its path rather
// than by identity. A path walked wrong does not throw and does not look wrong: it writes the
// change into a different condition, one the operator is not looking at. That is what this checks.

import assert from 'node:assert/strict';
import {
  ALL,
  ANY,
  FACT,
  LITERAL,
  NOT,
  STEP_FACT,
  TEST,
  appendTo,
  decodeSide,
  encodeSide,
  fromRows,
  mapTests,
  nodeAt,
  referencedStep,
  removeAt,
  replaceAt,
  shiftAt,
  tests,
  toRows,
  unwrapAt,
  wrapAt,
} from '../src/lib/sequence/tree.ts';

/** A condition comparing the `item` fact with a typed value -- the ordinary shape. */
const test = (value) => ({
  kind: TEST,
  op: 0,
  left: { kind: FACT, arg: 'item', step: -1 },
  right: { kind: LITERAL, arg: value, step: -1 },
});

/** What that condition is comparing against, for the assertions below. */
const typed = (node) => node.right.arg;

const group = (kind, ...children) => ({ kind, children });

// "that sofa, and either not that other one or room 12"
const tree = () => [
  test('500'),
  group(ANY, group(NOT, test('900')), test('12')),
];

// --- finding a node ---------------------------------------------------------------------------
assert.equal(typed(nodeAt(tree(), [0])), '500');
assert.equal(nodeAt(tree(), [1]).kind, ANY);
assert.equal(nodeAt(tree(), [1, 0]).kind, NOT);
assert.equal(typed(nodeAt(tree(), [1, 0, 0])), '900');
assert.equal(typed(nodeAt(tree(), [1, 1])), '12');
assert.equal(nodeAt(tree(), [9]), null, 'a path naming nothing is null, not a throw');
assert.equal(nodeAt(tree(), [0, 0]), null, 'a test has no children to walk into');

// --- replacing one, and nothing else -----------------------------------------------------------
{
  const next = replaceAt(tree(), [1, 0, 0], test('901'));

  assert.equal(typed(nodeAt(next, [1, 0, 0])), '901');
  assert.equal(typed(nodeAt(next, [0])), '500', 'its cousins are untouched');
  assert.equal(typed(nodeAt(next, [1, 1])), '12');
  assert.notEqual(next, tree(), 'a new array, so the editor can see the change');
}

// --- removing one takes what is under it -------------------------------------------------------
{
  const next = removeAt(tree(), [1, 0]);

  assert.equal(nodeAt(next, [1]).children.length, 1);
  assert.equal(typed(nodeAt(next, [1, 0])), '12', 'the sibling slid down into the gap');
}

// --- adding -------------------------------------------------------------------------------------
{
  assert.equal(appendTo(tree(), [], test('7')).length, 3, 'an empty path is the step itself');
  assert.equal(typed(nodeAt(appendTo(tree(), [1], test('7')), [1, 2])), '7');
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
  assert.equal(typed(nodeAt(wrapped, [0, 0])), '500', 'the condition is now inside it');

  const back = unwrapAt(wrapped, [0]);

  assert.equal(typed(nodeAt(back, [0])), '500', 'and out again');
  assert.equal(back.length, 2);
}

{
  // Unwrapping a group spills its children where it was, rather than leaving them nested under
  // something the operator asked to delete.
  const next = unwrapAt(tree(), [1]);

  assert.equal(next.length, 3);
  assert.equal(nodeAt(next, [1]).kind, NOT);
  assert.equal(typed(nodeAt(next, [2])), '12');
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
  assert.equal(typed(nodeAt(next, [1])), '500');
  assert.deepEqual(shiftAt(tree(), [0], -1), tree(), 'the first one has nowhere earlier to go');
  assert.deepEqual(shiftAt(tree(), [1], 1), tree(), 'nor the last one later');
}

{
  const next = shiftAt(tree(), [1, 1], -1);

  assert.equal(typed(nodeAt(next, [1, 0])), '12', 'inside a group too');
  assert.equal(nodeAt(next, [1, 1]).kind, NOT);
}

// --- every test, wherever it is ----------------------------------------------------------------
{
  const found = tests(tree());

  assert.deepEqual(
    found.map((f) => typed(f.node)),
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
  const next = mapTests(tree(), (node) => ({
    ...node,
    right: { ...node.right, arg: `${node.right.arg}!` },
  }));

  assert.equal(typed(nodeAt(next, [0])), '500!');
  assert.equal(typed(nodeAt(next, [1, 0, 0])), '900!');
  assert.equal(typed(nodeAt(next, [1, 1])), '12!');
  assert.equal(nodeAt(next, [1]).kind, ANY);
  assert.equal(nodeAt(next, [1, 0]).kind, NOT);
}

// --- one side, as one dropdown option ----------------------------------------------------------
{
  const round = (value) => decodeSide(encodeSide(value));

  assert.deepEqual(round({ kind: FACT, arg: 'room', step: -1 }), {
    kind: FACT,
    arg: 'room',
    step: -1,
  });
  assert.deepEqual(round({ kind: STEP_FACT, arg: 'target', step: 2 }), {
    kind: STEP_FACT,
    arg: 'target',
    step: 2,
  });

  // A literal is whatever was typed, colons and all -- everything after the second separator is
  // the argument, or "18:30" would come back as "18".
  assert.deepEqual(round({ kind: LITERAL, arg: '18:30', step: -1 }), {
    kind: LITERAL,
    arg: '18:30',
    step: -1,
  });

  assert.equal(decodeSide('nonsense').arg, '', 'an option value from nowhere is an empty literal');
  assert.equal(decodeSide('nonsense').kind, LITERAL);
}

// --- what the editor remembers per condition ---------------------------------------------------
{
  // The name of a picked piece of furniture rides on the condition, so every operation that moves
  // a condition has to carry it. Kept in a map keyed by path, it landed on whichever condition
  // ended up at that path -- a furniture's name on a condition about where somebody stood, which
  // is what an operator reported.
  const named = { ...test('397'), picked: { name: 'chair_polyfon' } };
  const start = [test('1'), named];

  assert.equal(nodeAt(wrapAt(start, [1], NOT), [1, 0]).picked.name, 'chair_polyfon', 'wrapped');
  assert.equal(nodeAt(shiftAt(start, [1], -1), [0]).picked.name, 'chair_polyfon', 'moved');
  assert.equal(nodeAt(removeAt(start, [0]), [0]).picked.name, 'chair_polyfon', 'sibling deleted');

  const nested = appendTo([group(ANY)], [0], named);

  assert.equal(nodeAt(nested, [0, 0]).picked.name, 'chair_polyfon', 'nested');
  assert.equal(nodeAt(unwrapAt(nested, [0]), [0]).picked.name, 'chair_polyfon', 'and back out');

  // And it never reaches the server: the row stores an id, and the name is what a directory says
  // that id is called today.
  assert.equal(toRows([named])[0].test.picked, undefined);
}

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

  // A response from a build that predates sides still reads: the fact goes left, the typed value
  // goes right, which is what the condition has always meant.
  assert.equal(held[0].left.arg, 'definition');
  assert.equal(held[0].left.kind, FACT);
  assert.equal(typed(held[0]), '500');
  assert.equal(held[0].right.kind, LITERAL);
  assert.equal(typed(nodeAt(held, [1, 0, 0])), '900', 'three levels down survives the crossing');
  assert.equal(nodeAt(held, [1, 1]).left.arg, 'room');

  // And back out unchanged, which is what stops an edit-and-save from quietly rewriting a task
  // nobody touched.
  const back = toRows(held);

  assert.equal(back[0].test.value, '500');
  assert.equal(back[0].test.factKey, 'definition', 'the legacy columns stay filled');
  assert.equal(back[1].kind, ANY);
  assert.equal(back[1].children[0].children[0].test.value, '900');
  assert.equal(back[1].children[1].test.factKey, 'room');
  assert.equal(back[0].children, null, 'a test carries no children across');
  assert.equal(back[1].test, null, 'nor a group a test');
}

{
  // Both sides named, which is the shape the server sends now and the only one the editor edits.
  const rows = [
    {
      kind: TEST,
      test: {
        factKey: '',
        op: 0,
        value: '',
        left: { kind: FACT, arg: 'room_owner', step: -1 },
        right: { kind: STEP_FACT, arg: 'target', step: 0 },
      },
      children: null,
    },
  ];

  const held = fromRows(rows);

  assert.equal(held[0].left.arg, 'room_owner');
  assert.equal(held[0].right.kind, STEP_FACT);
  assert.equal(held[0].right.step, 0, 'the step is a number, not packed into the argument');

  const back = toRows(held);

  assert.equal(back[0].test.left.kind, FACT);
  assert.equal(back[0].test.right.step, 0);
  // Derived, not edited: neither side is a plain fact-and-literal, so neither legacy column has
  // anything honest to say.
  assert.equal(back[0].test.factKey, 'room_owner');
  assert.equal(back[0].test.value, '', 'a step reference is not a literal');
}

assert.deepEqual(fromRows(null), [], 'a task with no conditions is not a crash');
assert.deepEqual(fromRows(undefined), []);

{
  // Trimmed on the way out, so the value the server stores is the value every path would store.
  const rows = toRows([
    {
      kind: TEST,
      op: '4',
      left: { kind: FACT, arg: 'room', step: -1 },
      right: { kind: LITERAL, arg: '  12  ', step: -1 },
    },
  ]);

  assert.equal(rows[0].test.value, '12');
  assert.equal(rows[0].test.right.arg, '12', 'and on the side the server actually reads');
  assert.equal(rows[0].test.op, 4, 'a select hands back a string; the server wants the number');
}

console.log('sequence tree: ok');
