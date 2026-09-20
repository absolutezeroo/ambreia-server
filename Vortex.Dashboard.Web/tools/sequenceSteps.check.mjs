// Dragging a block must not quietly break the references inside it.
//
// A condition value of `$N` names an earlier step, and the server refuses one that points at itself
// or forwards. So reordering by array splice produces content that fails validation on save at
// best, and silently matches the wrong step at worst -- which is invisible in any screenshot of the
// editor. This is the part of the drag-and-drop that has to be right.
//
// A reference nested inside an `any` is exactly as invisible as one at the top of the step, so the
// nested case is checked beside the flat one rather than trusted to the same walk.

import assert from 'node:assert/strict';
import { ANY, NOT, TEST, moveStep, nodeAt, referencedStep } from '../src/lib/sequence/tree.ts';

const test = (value) => ({ kind: TEST, factKey: 'item', op: 0, value });
const step = (action, ...values) => ({ actionCode: action, tree: values.map(test) });

// --- reading a reference -------------------------------------------------------------------
assert.equal(referencedStep('$0'), 0);
assert.equal(referencedStep('$12'), 12);
assert.equal(referencedStep('4312'), -1, 'a literal is not a reference');
assert.equal(referencedStep('$'), -1);
assert.equal(referencedStep('$-1'), -1);
assert.equal(referencedStep(''), -1);
assert.equal(referencedStep(undefined), -1);

// --- a reference follows the step it names ---------------------------------------------------
{
  // place, walk-on-$0, pick-up-$0  ->  move the walk to the end
  const steps = [step('place_item'), step('walk_on_furni', '$0'), step('pick_up_item', '$0')];
  const { steps: moved, clearedReferences } = moveStep(steps, 1, 2);

  assert.equal(clearedReferences, 0, 'both references still point backwards');
  assert.equal(moved[0].actionCode, 'place_item');
  assert.equal(moved[1].actionCode, 'pick_up_item');
  assert.equal(moved[2].actionCode, 'walk_on_furni');
  assert.equal(moved[1].tree[0].value, '$0', 'pick-up still names the placement');
  assert.equal(moved[2].tree[0].value, '$0', 'so does the walk');
}

// --- a reference that would point forwards is cleared, not retargeted ------------------------
{
  // Move the placement to the end: the two steps that read it now come first, and there is no
  // honest value for them. Cleared, and counted, so the operator is told.
  const steps = [step('place_item'), step('walk_on_furni', '$0'), step('pick_up_item', '$0')];
  const { steps: moved, clearedReferences } = moveStep(steps, 0, 2);

  assert.equal(clearedReferences, 2);
  assert.equal(moved[0].tree[0].value, '', 'an empty field the operator can see');
  assert.equal(moved[1].tree[0].value, '');
  assert.equal(moved[2].actionCode, 'place_item');
}

// --- a reference buried in a group is rewritten too ------------------------------------------
{
  // "the same furniture, or not that other one" -- the reference is two levels down, and a walk
  // that only visited the top level would leave it pointing at whatever now sits at index 0.
  const steps = [
    step('place_item'),
    step('chat'),
    {
      actionCode: 'walk_on_furni',
      tree: [{ kind: ANY, children: [test('$0'), { kind: NOT, children: [test('900')] }] }],
    },
  ];

  const { steps: moved, clearedReferences } = moveStep(steps, 1, 0);

  assert.equal(clearedReferences, 0, 'the placement is still earlier, just at a new index');
  assert.equal(moved[0].actionCode, 'chat');
  assert.equal(moved[1].actionCode, 'place_item');
  assert.equal(nodeAt(moved[2].tree, [0, 0]).value, '$1', 'the nested reference followed it');
  assert.equal(nodeAt(moved[2].tree, [0, 1, 0]).value, '900', 'the literal beside it is untouched');
}

{
  // And the nested one is cleared when the move invalidates it, exactly like a flat one.
  const steps = [
    step('place_item'),
    { actionCode: 'walk_on_furni', tree: [{ kind: ANY, children: [test('$0')] }] },
  ];

  const { steps: moved, clearedReferences } = moveStep(steps, 0, 1);

  assert.equal(clearedReferences, 1);
  assert.equal(nodeAt(moved[0].tree, [0, 0]).value, '');
}

// --- a move that is not a move ----------------------------------------------------------------
{
  const steps = [step('a'), step('b')];

  assert.equal(moveStep(steps, 1, 1).steps, steps, 'the same array back, so nothing rerenders');
  assert.equal(moveStep(steps, 5, 0).steps, steps);
  assert.equal(moveStep(steps, 0, -1).steps, steps);
}

// --- a step with no conditions survives the walk ----------------------------------------------
{
  const steps = [{ actionCode: 'dance' }, step('wave', '$0')];
  const { steps: moved } = moveStep(steps, 0, 1);

  assert.deepEqual(moved[1].tree, [], 'no tree reads as no conditions, not a crash');
}

console.log('sequence steps: ok');
