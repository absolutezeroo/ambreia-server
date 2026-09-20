// Dragging a block must not quietly break the references inside it.
//
// A condition can name an earlier step on EITHER side, and the server refuses one that points at
// itself or forwards. So reordering by array splice produces content that fails validation on save
// at best, and silently asks a different question at worst -- the condition still reads correctly,
// it just names a different step. That is invisible in any screenshot of the editor, which is why
// this is checked rather than looked at.
//
// A reference nested inside an `any`, and a reference on the left rather than the right, are both
// exactly as invisible as the plain case. Both are checked beside it rather than trusted to the
// same walk.

import assert from 'node:assert/strict';
import {
  ANY,
  FACT,
  LITERAL,
  NOT,
  STEP_FACT,
  TEST,
  moveStep,
  nodeAt,
} from '../src/lib/sequence/tree.ts';

const fact = (arg) => ({ kind: FACT, arg, step: -1 });
const literal = (arg) => ({ kind: LITERAL, arg, step: -1 });
const fromStep = (step, arg = 'item') => ({ kind: STEP_FACT, arg, step });

/** A condition reading an earlier step on its right, which is what `$N` used to be. */
const reads = (step) => ({ kind: TEST, op: 0, left: fact('item'), right: fromStep(step) });
const plain = (value) => ({ kind: TEST, op: 0, left: fact('item'), right: literal(value) });

const step = (action, ...nodes) => ({ actionCode: action, tree: nodes });

// --- a reference follows the step it names ---------------------------------------------------
{
  // place, walk-on-step-0, pick-up-step-0  ->  move the walk to the end
  const steps = [
    step('place_item'),
    step('walk_on_furni', reads(0)),
    step('pick_up_item', reads(0)),
  ];
  const { steps: moved, clearedReferences } = moveStep(steps, 1, 2);

  assert.equal(clearedReferences, 0, 'both references still point backwards');
  assert.equal(moved[0].actionCode, 'place_item');
  assert.equal(moved[1].actionCode, 'pick_up_item');
  assert.equal(moved[2].actionCode, 'walk_on_furni');
  assert.equal(moved[1].tree[0].right.step, 0, 'pick-up still names the placement');
  assert.equal(moved[2].tree[0].right.step, 0, 'so does the walk');
}

// --- a reference that would point forwards is cleared, not retargeted ------------------------
{
  // Move the placement to the end: the two steps that read it now come first, and there is no
  // honest value for them. Cleared, and counted, so the operator is told.
  const steps = [
    step('place_item'),
    step('walk_on_furni', reads(0)),
    step('pick_up_item', reads(0)),
  ];
  const { steps: moved, clearedReferences } = moveStep(steps, 0, 2);

  assert.equal(clearedReferences, 2);
  assert.equal(moved[0].tree[0].right.kind, FACT, 'back to a plain fact the operator can see');
  assert.equal(moved[1].tree[0].right.kind, FACT);
  assert.equal(moved[2].actionCode, 'place_item');
}

// --- a reference on the LEFT is rewritten too ------------------------------------------------
{
  // The side that could never hold one before. A walk that only visited the right would leave
  // this naming whatever step ends up at index 0.
  const steps = [
    step('place_item'),
    step('chat'),
    {
      actionCode: 'walk_on_furni',
      tree: [{ kind: TEST, op: 0, left: fromStep(0), right: fact('item') }],
    },
  ];

  const { steps: moved, clearedReferences } = moveStep(steps, 1, 0);

  assert.equal(clearedReferences, 0);
  assert.equal(moved[1].actionCode, 'place_item');
  assert.equal(moved[2].tree[0].left.step, 1, 'the reference followed the step it names');
  assert.equal(moved[2].tree[0].right.kind, FACT, 'the plain side beside it is untouched');
}

// --- a reference buried in a group is rewritten too ------------------------------------------
{
  const steps = [
    step('place_item'),
    step('chat'),
    {
      actionCode: 'walk_on_furni',
      tree: [
        { kind: ANY, children: [reads(0), { kind: NOT, children: [plain('900')] }] },
      ],
    },
  ];

  const { steps: moved, clearedReferences } = moveStep(steps, 1, 0);

  assert.equal(clearedReferences, 0, 'the placement is still earlier, just at a new index');
  assert.equal(nodeAt(moved[2].tree, [0, 0]).right.step, 1, 'the nested reference followed it');
  assert.equal(nodeAt(moved[2].tree, [0, 1, 0]).right.arg, '900', 'the literal beside it is untouched');
}

{
  // And the nested one is cleared when the move invalidates it, exactly like a flat one.
  const steps = [
    step('place_item'),
    { actionCode: 'walk_on_furni', tree: [{ kind: ANY, children: [reads(0)] }] },
  ];

  const { steps: moved, clearedReferences } = moveStep(steps, 0, 1);

  assert.equal(clearedReferences, 1);
  assert.equal(nodeAt(moved[0].tree, [0, 0]).right.kind, FACT);
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
  const steps = [{ actionCode: 'dance' }, step('wave', reads(0))];
  const { steps: moved } = moveStep(steps, 0, 1);

  assert.deepEqual(moved[1].tree, [], 'no tree reads as no conditions, not a crash');
}

console.log('sequence steps: ok');
