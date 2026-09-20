<script lang="ts">
  // The sequence as blocks you pick up, line up and clip together.
  //
  // Reordering is not cosmetic here: a condition's value may be `$N`, naming an earlier step, and
  // the server refuses one that points at itself or forwards. So every drop runs through
  // lib/sequence/steps.ts, which rewrites the references for the new positions and clears the ones
  // the move invalidates -- and says how many it cleared, so the operator is told rather than
  // finding out on save.
  //
  // Native HTML5 drag rather than a library: it is four handlers, it gives keyboard-free drag out
  // of the box, and the repository ships no drag dependency to reuse.
  //
  // Every change to a step's conditions goes through lib/sequence/tree.ts and comes back as a new
  // tree. Nothing here splices an array: the shape rules are checked in one place, and a component
  // that kept its own copy of them would be a second set nothing checks.
  import ActionBlock from './ActionBlock.svelte';
  import ConditionBlock from './ConditionBlock.svelte';
  import {
    COUNT_OF,
    FACT,
    LITERAL,
    STEP_FACT,
    TEST,
    appendTo,
    moveStep,
    nodeAt,
    pathKey,
    removeAt,
    replaceAt,
    shiftAt,
    unwrapAt,
    wrapAt,
    type FactOption,
    type NodePath,
    type SideValue,
    type TreeNode,
  } from '../../lib/sequence/tree';
  import { t } from '../../lib/i18n';

  /**
   * What a number answers, mirroring the server's own table for FactKind.Number.
   *
   * Spelled here because a count has no fact behind it to carry the server's list — every other
   * side gets its operators from the fact the server described.
   */
  const NUMBER_OPERATORS = [
    { value: 0, key: 'rewardTracks.filterOpEquals' },
    { value: 1, key: 'rewardTracks.filterOpNotEquals' },
    { value: 4, key: 'rewardTracks.filterOpAtLeast' },
    { value: 5, key: 'rewardTracks.filterOpAtMost' },
  ];

  type StepForm = { actionCode: string; tree: TreeNode[] };

  type Props = {
    steps: StepForm[];
    actions: { name: string; wired: boolean }[];
    canManage: boolean;
    factsFor: (actionCode: string) => FactOption[];
    operatorsFor: (actionCode: string, factKey: string) => { value: number; key: string }[];
    defaultFilterValue: (actionCode: string, factKey: string) => string;
    pickerFor: (meta: FactOption | null) => string | null;
    pickedLabels: Record<string, { name?: string; iconUrl?: string }>;
    onchange: (steps: StepForm[]) => void;
    onpick: (stepIndex: number, path: NodePath, kind: string) => void;
  };

  let {
    steps,
    actions,
    canManage,
    factsFor,
    operatorsFor,
    defaultFilterValue,
    pickerFor,
    pickedLabels,
    onchange,
    onpick,
  }: Props = $props();

  let carrying = $state<number | null>(null);
  let dropIndex = $state<number | null>(null);
  let notice = $state('');

  /** Replaces one step's tree, leaving every other step the object it was. */
  function edit(stepIndex: number, next: TreeNode[]) {
    onchange(steps.map((step, i) => (i === stepIndex ? { ...step, tree: next } : step)));
  }

  /** A fresh condition: the first fact its action records, against something to type. */
  function newTest(actionCode: string): TreeNode {
    const first = factsFor(actionCode)[0];
    const key = first?.key ?? '';

    return {
      kind: TEST,
      op: operatorsFor(actionCode, key)[0]?.value ?? 0,
      left: { kind: FACT, arg: key, step: -1 },
      right: { kind: LITERAL, arg: defaultFilterValue(actionCode, key), step: -1 },
    };
  }

  const label = (fact: FactOption) => {
    const key = fact.labelKey ?? fact.key;
    const translated = $t(key);

    return translated === key ? fact.fallbackLabel : translated;
  };

  /**
   * Everything one side of a condition may be, in this step.
   *
   * Three groups, in the order an operator reaches for them: what this action reports, how many of
   * a list it reported, and what an earlier step reported. A step that records nothing useful
   * simply contributes no options — an impossible choice is better not offered than refused on
   * save, which is the same rule the reference dropdown followed before this.
   */
  function sideChoicesFor(step: StepForm, stepIndex: number) {
    const own = factsFor(step.actionCode);

    return [
      ...own.map((fact) => ({
        group: $t('rewardTracks.sideThisAction'),
        label: label(fact),
        value: { kind: FACT, arg: fact.key, step: -1 },
      })),
      // Offered for every fact rather than only the list-shaped ones: the server knows which facts
      // carry a list and refuses a count of one that does not, with a message that says so.
      ...own.map((fact) => ({
        group: $t('rewardTracks.sideCount'),
        // Marked, because the group heading is only visible while the menu is open. Labelled with
        // the fact alone, a count and the fact itself read identically once chosen -- two
        // different conditions, the same row on screen.
        label: $t('rewardTracks.sideCountLabel', { fact: label(fact) }),
        value: { kind: COUNT_OF, arg: fact.key, step: -1 },
      })),
      ...steps.slice(0, stepIndex).flatMap((earlier, i) =>
        factsFor(earlier.actionCode).map((fact) => ({
          group: $t('rewardTracks.sideFromStep', { n: i + 1 }),
          label: label(fact),
          value: { kind: STEP_FACT, arg: fact.key, step: i },
        }))
      ),
    ];
  }

  /**
   * The operators that mean anything about what is on the left.
   *
   * A count is a number however long the list behind it is, so ordering applies to it even though
   * the server refuses ordering over the set itself. A typed value on the left has no kind, so
   * nothing is narrowed and the server has the last word.
   */
  function operatorsForSide(step: StepForm, left: SideValue) {
    if (left.kind === COUNT_OF) return NUMBER_OPERATORS;

    if (left.kind === STEP_FACT) {
      return operatorsFor(steps[left.step]?.actionCode ?? step.actionCode, left.arg);
    }

    return operatorsFor(step.actionCode, left.arg);
  }

  function over(event: DragEvent, index: number) {
    if (carrying === null) return;

    event.preventDefault();
    dropIndex = index;
  }

  function drop(event: DragEvent, index: number) {
    if (carrying === null) return;

    event.preventDefault();

    const result = moveStep(steps, carrying, index);

    if (result.steps !== steps) onchange(result.steps as StepForm[]);

    notice =
      result.clearedReferences > 0
        ? $t('rewardTracks.referencesCleared', { count: result.clearedReferences })
        : '';

    carrying = null;
    dropIndex = null;
  }
</script>

<div class="sequence" role="list">
  {#each steps as step, stepIndex (stepIndex)}
    <!-- The gap that opens above each block: the drop point, and the only thing that moves while
         the cursor travels. -->
    <div
      class="slot"
      class:open={dropIndex === stepIndex}
      ondragover={(e) => over(e, stepIndex)}
      ondrop={(e) => drop(e, stepIndex)}
      role="presentation"
    ></div>

    <ActionBlock
      {step}
      index={stepIndex}
      {actions}
      {canManage}
      removable={steps.length > 1}
      dragging={carrying === stepIndex}
      ondragstart={() => {
        carrying = stepIndex;
        notice = '';
      }}
      ondragend={() => {
        carrying = null;
        dropIndex = null;
      }}
      onactionchange={() => {
        // The facts belong to the action, so none of the old conditions can still be tested.
        edit(stepIndex, []);
      }}
      onremove={() => onchange(steps.filter((_, i) => i !== stepIndex))}
    >
      <div class="step-body">
        {#if factsFor(step.actionCode).length === 0}
          <p class="muted small no-facts">{$t('rewardTracks.actionHasNoFacts')}</p>
        {:else}
          {#each step.tree ?? [] as node, index (index)}
            <ConditionBlock
              {node}
              path={[index]}
              siblings={(step.tree ?? []).length}
              {canManage}
              facts={factsFor(step.actionCode)}
              sideChoices={sideChoicesFor(step, stepIndex)}
              operatorsFor={(left: SideValue) => operatorsForSide(step, left)}
              picked={pickedLabels[pathKey(stepIndex, [index])]}
              picker={node.kind === TEST && (node as any).left.kind === FACT
                ? pickerFor(
                    factsFor(step.actionCode).find((f) => f.key === (node as any).left.arg) ?? null
                  )
                : null}
              onchange={(path: NodePath, next: TreeNode) =>
                edit(stepIndex, replaceAt(step.tree ?? [], path, next))}
              onsidechange={(path: NodePath, which: 'left' | 'right', value: SideValue) => {
                const node = nodeAt(step.tree ?? [], path);

                if (!node || node.kind !== TEST) return;

                // Choosing "a value you type" when it already was one must not wipe what is in it:
                // the option identifies the kind, and the text belongs to the operator.
                const kept =
                  which === 'right' &&
                  value.kind === LITERAL &&
                  node.right.kind === LITERAL &&
                  value.arg === ''
                    ? node.right
                    : value;

                const next = { ...node, [which]: kept };

                // Changing the left side changes what the comparison is about, so the operator and
                // the typed value belonged to the old one. Kept, they would be a comparison the
                // server refuses or a value nothing can match.
                if (which === 'left') {
                  next.op = operatorsForSide(step, value)[0]?.value ?? 0;

                  if (next.right.kind === LITERAL) {
                    next.right = {
                      ...next.right,
                      arg:
                        value.kind === FACT
                          ? defaultFilterValue(step.actionCode, value.arg)
                          : '',
                    };
                  }
                }

                edit(stepIndex, replaceAt(step.tree ?? [], path, next));
              }}
              onremove={(path: NodePath) => edit(stepIndex, removeAt(step.tree ?? [], path))}
              onwrap={(path: NodePath, kind: 1 | 2 | 3) =>
                edit(stepIndex, wrapAt(step.tree ?? [], path, kind))}
              onunwrap={(path: NodePath) => edit(stepIndex, unwrapAt(step.tree ?? [], path))}
              onshift={(path: NodePath, by: -1 | 1) =>
                edit(stepIndex, shiftAt(step.tree ?? [], path, by))}
              onadd={(path: NodePath) =>
                edit(stepIndex, appendTo(step.tree ?? [], path, newTest(step.actionCode)))}
              onaddgroup={(path: NodePath, kind: 1 | 2 | 3) =>
                edit(
                  stepIndex,
                  appendTo(step.tree ?? [], path, {
                    kind,
                    // Never empty: the server refuses a group with nothing in it, and an editor
                    // that offered one would be offering a save that fails.
                    children: [newTest(step.actionCode)],
                  })
                )}
              onpick={(path: NodePath, kind: string) => onpick(stepIndex, path, kind)}
              keyFor={(path: NodePath) => pathKey(stepIndex, path)}
              pickedFor={(path: NodePath) => pickedLabels[pathKey(stepIndex, path)]}
              pickerFor={(factKey: string) =>
                pickerFor(factsFor(step.actionCode).find((f) => f.key === factKey) ?? null)}
            />
          {/each}

          <div class="step-add">
            <button
              type="button"
              class="ghost-button"
              disabled={!canManage}
              onclick={() =>
                edit(stepIndex, appendTo(step.tree ?? [], [], newTest(step.actionCode)))}
            >
              {$t('rewardTracks.addFilter')}
            </button>

            <!-- No "+ and" here: the step itself is the "and". Everything at this level already
                 has to hold, which is what a step has always meant. -->
            {#each [{ kind: 2, labelKey: 'rewardTracks.groupAny' }, { kind: 3, labelKey: 'rewardTracks.groupNot' }] as g (g.kind)}
              <button
                type="button"
                class="ghost-button add-group"
                disabled={!canManage}
                onclick={() =>
                  edit(
                    stepIndex,
                    appendTo(step.tree ?? [], [], {
                      kind: g.kind as 2 | 3,
                      children: [newTest(step.actionCode)],
                    })
                  )}
              >
                + {$t(g.labelKey)}
              </button>
            {/each}
          </div>

          {#if (step.tree ?? []).length === 0}
            <!-- What an empty step means, said out loud: it is not a mistake, it is "any time this
                 action happens", and that is a real thing to want. -->
            <p class="muted small no-facts">{$t('rewardTracks.stepHasNoConditions')}</p>
          {/if}
        {/if}
      </div>
    </ActionBlock>
  {/each}

  <!-- The slot after the last block, so a step can be dropped at the end. -->
  <div
    class="slot"
    class:open={dropIndex === steps.length - 1}
    ondragover={(e) => over(e, steps.length - 1)}
    ondrop={(e) => drop(e, steps.length - 1)}
    role="presentation"
  ></div>

  <button
    type="button"
    class="ghost-button block-add block-add--action"
    disabled={!canManage}
    onclick={() => onchange([...steps, { actionCode: actions[0]?.name ?? '', tree: [] }])}
  >
    {$t('rewardTracks.addStep')}
  </button>

  {#if notice}
    <p class="muted small notice" role="status">{notice}</p>
  {/if}
</div>

<style>
  .sequence {
    display: flex;
    flex-direction: column;
    max-width: 1100px;
  }

  /* Closed, a slot is invisible. Open, it is the gap the block will fall into -- which is what
     makes the arrangement feel like blocks clipping together rather than a list reordering. */
  .slot {
    height: 0;
    border-radius: 6px;
    transition:
      height 90ms ease,
      background 90ms ease;
  }

  .slot.open {
    height: 28px;
    margin: 4px 0;
    background: rgba(var(--accent-rgb), 0.18);
    border: 1px dashed rgba(var(--accent-rgb), 0.6);
  }

  /* Indented under its action, which is the only thing that says the conditions belong to it. */
  .step-body {
    padding: 6px 10px 10px 16px;
  }

  .step-add {
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
    margin-top: 6px;
  }

  .add-group {
    font-size: 0.72rem;
    text-transform: uppercase;
    letter-spacing: 0.04em;
  }

  .notice {
    margin-top: 8px;
  }

  .no-facts {
    margin: 6px 0 0;
  }
</style>
