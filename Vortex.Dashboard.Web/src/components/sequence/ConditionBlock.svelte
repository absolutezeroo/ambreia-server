<script lang="ts">
  // One condition, or a group of them. The block recurses into itself, because the content does.
  //
  // A test is one sentence on one line -- subject, verb, value -- and a group is a coloured frame
  // around the blocks it joins, with its word written down the left edge. That is the whole idea
  // borrowed from Scratch: the shape carries the meaning, so an operator reads "either ... or ..."
  // without decoding a list of rows that all look alike.
  //
  // It renders the tree it is given and asks the editor to change it. It never edits the tree
  // itself: every shape rule lives in lib/sequence/tree.ts, which is checked, and a component that
  // spliced its own arrays would be a second copy of those rules that nothing checks.
  import { ChevronDown, ChevronUp, Search, X } from '@lucide/svelte';
  import AssetImage from '../AssetImage.svelte';
  import RelationGrid from './RelationGrid.svelte';
  import Self from './ConditionBlock.svelte';
  import {
    ALL,
    ANY,
    NOT,
    TEST,
    type FactOption,
    type NodePath,
    type TreeNode,
  } from '../../lib/sequence/tree';
  import { t } from '../../lib/i18n';

  type Props = {
    node: TreeNode;
    path: NodePath;
    /** How many siblings it has, so the first and last do not offer a move with nowhere to go. */
    siblings: number;
    canManage: boolean;
    facts: FactOption[];
    operatorsFor: (factKey: string) => { value: number; key: string }[];
    /** The earlier steps this fact may point back at. Per fact, because `$N` resolves per fact. */
    referencesFor: (factKey: string) => { value: string; index: number }[];
    picked: { name?: string; iconUrl?: string } | undefined;
    picker: string | null;
    onchange: (path: NodePath, node: TreeNode) => void;
    onfactchange: (path: NodePath, factKey: string) => void;
    onremove: (path: NodePath) => void;
    onwrap: (path: NodePath, kind: 1 | 2 | 3) => void;
    onunwrap: (path: NodePath) => void;
    onshift: (path: NodePath, by: -1 | 1) => void;
    onadd: (path: NodePath) => void;
    onaddgroup: (path: NodePath, kind: 1 | 2 | 3) => void;
    onpick: (path: NodePath, kind: string) => void;
    /** Everything above, forwarded to a child block unchanged. */
    keyFor: (path: NodePath) => string;
    pickedFor: (path: NodePath) => { name?: string; iconUrl?: string } | undefined;
    pickerFor: (factKey: string) => string | null;
  } & Record<string, unknown>;

  let {
    node,
    path,
    siblings,
    canManage,
    facts,
    operatorsFor,
    referencesFor,
    picked,
    picker,
    onchange,
    onfactchange,
    onremove,
    onwrap,
    onunwrap,
    onshift,
    onadd,
    onaddgroup,
    onpick,
    keyFor,
    pickedFor,
    pickerFor,
  }: Props = $props();

  const GROUPS: { kind: 1 | 2 | 3; labelKey: string }[] = [
    { kind: ALL, labelKey: 'rewardTracks.groupAll' },
    { kind: ANY, labelKey: 'rewardTracks.groupAny' },
    { kind: NOT, labelKey: 'rewardTracks.groupNot' },
  ];

  let test = $derived(node.kind === TEST ? (node as any) : null);
  let meta = $derived(test ? (facts.find((f) => f.key === test.factKey) ?? null) : null);
  let children = $derived(node.kind === TEST ? [] : ((node as any).children ?? []));

  /** A `not` says what one condition is not, so a second one under it means nothing. */
  let full = $derived(node.kind === NOT && children.length >= 1);

  let label = (fact: { labelKey?: string; key?: string; value?: string; fallbackLabel: string }) => {
    const key = fact.labelKey ?? fact.key ?? '';
    const translated = $t(key);

    return !key || translated === key ? fact.fallbackLabel : translated;
  };

  function groupClass(kind: number) {
    return kind === ANY ? 'group--any' : kind === NOT ? 'group--not' : 'group--all';
  }
</script>

{#if test}
  <!-- The repository's own block chrome: the tint, the edge, and the notch underneath that makes
       two stacked blocks read as clipped together rather than merely adjacent. -->
  <div class="condition-row block block--filter cond" role="listitem">
    <span class="cond-hex" aria-hidden="true"></span>

    <select
      class="cond-subject"
      bind:value={test.factKey}
      onchange={() => onfactchange(path, test.factKey)}
      disabled={!canManage}
    >
      {#each facts as fact (fact.key)}
        <option value={fact.key}>{label(fact)}</option>
      {/each}
    </select>

    <select
      class="cond-verb"
      value={test.op}
      onchange={(e) =>
        onchange(path, { ...test, op: Number((e.currentTarget as HTMLSelectElement).value) })}
      disabled={!canManage}
    >
      {#each operatorsFor(test.factKey) as op (op.value)}
        <option value={op.value}>{$t(op.key)}</option>
      {/each}
    </select>

    <!-- A reference is only offered where it can resolve, so an impossible one cannot be picked;
         the server refuses the same thing on save. -->
    {#if referencesFor(test.factKey).length > 0}
      <select
        class="cond-source"
        value={String(test.value).startsWith('$') ? test.value : ''}
        onchange={(e) =>
          onchange(path, { ...test, value: (e.currentTarget as HTMLSelectElement).value })}
        disabled={!canManage}
      >
        <option value="">{$t('rewardTracks.filterLiteral')}</option>
        {#each referencesFor(test.factKey) as ref (ref.value)}
          <option value={ref.value}>
            {$t('rewardTracks.filterSameAsStep', { n: ref.index + 1 })}
          </option>
        {/each}
      </select>
    {/if}

    {#if meta?.key === 'relation'}
      <!-- The one closed fact a list cannot express: its sides are named from the furniture, so
           "right side" reads backwards against a picture of the thing facing you. -->
      <RelationGrid
        value={test.value}
        {canManage}
        onchange={(value: string, op: number) => onchange(path, { ...test, value, op })}
      />
    {:else if meta?.values?.length}
      <!-- A closed fact: the server declares what it accepts, so a typo cannot be entered. -->
      <select
        class="cond-value"
        value={test.value}
        onchange={(e) =>
          onchange(path, { ...test, value: (e.currentTarget as HTMLSelectElement).value })}
        disabled={!canManage}
      >
        {#each meta.values as allowed (allowed.value)}
          <option value={allowed.value}>{label(allowed)}</option>
        {/each}
      </select>
    {:else if !String(test.value).startsWith('$')}
      <input
        class="cond-value"
        type="text"
        value={test.value}
        oninput={(e) =>
          onchange(path, { ...test, value: (e.currentTarget as HTMLInputElement).value })}
        disabled={!canManage}
        placeholder={$t(
          Number(test.op) === 2
            ? 'rewardTracks.conditionListPlaceholder'
            : 'rewardTracks.conditionValuePlaceholder'
        )}
      />
      {#if picker}
        <button
          type="button"
          class="ghost-button cond-icon"
          title={$t('rewardTracks.pickValue')}
          disabled={!canManage}
          onclick={() => onpick(path, picker)}
        >
          <Search size={14} />
        </button>
      {/if}
    {/if}

    {#if picked?.name}
      <span class="muted small cond-picked">
        {#if picked.iconUrl}
          <AssetImage src={picked.iconUrl} alt="" size={20} />
        {/if}
        {picked.name}
      </span>
    {/if}

    <span class="cond-tools">
      {#each GROUPS as g (g.kind)}
        <button
          type="button"
          class="ghost-button cond-wrap"
          title={$t('rewardTracks.wrapIn', { word: $t(g.labelKey) })}
          disabled={!canManage}
          onclick={() => onwrap(path, g.kind)}
        >
          {$t(g.labelKey)}
        </button>
      {/each}

      {#if siblings > 1}
        <button
          type="button"
          class="ghost-button cond-icon"
          title={$t('common.moveUp')}
          disabled={!canManage || path[path.length - 1] === 0}
          onclick={() => onshift(path, -1)}
        >
          <ChevronUp size={14} />
        </button>
        <button
          type="button"
          class="ghost-button cond-icon"
          title={$t('common.moveDown')}
          disabled={!canManage || path[path.length - 1] === siblings - 1}
          onclick={() => onshift(path, 1)}
        >
          <ChevronDown size={14} />
        </button>
      {/if}

      <button
        type="button"
        class="ghost-button cond-icon"
        title={$t('common.remove')}
        disabled={!canManage}
        onclick={() => onremove(path)}
      >
        <X size={14} />
      </button>
    </span>
  </div>
{:else}
  <div class="group {groupClass(node.kind)}" role="group">
    <div class="group-head">
      <span class="group-word">{$t(GROUPS.find((g) => g.kind === node.kind)?.labelKey ?? '')}</span>

      <span class="cond-tools">
        {#if siblings > 1}
          <button
            type="button"
            class="ghost-button cond-icon"
            title={$t('common.moveUp')}
            disabled={!canManage || path[path.length - 1] === 0}
            onclick={() => onshift(path, -1)}
          >
            <ChevronUp size={14} />
          </button>
          <button
            type="button"
            class="ghost-button cond-icon"
            title={$t('common.moveDown')}
            disabled={!canManage || path[path.length - 1] === siblings - 1}
            onclick={() => onshift(path, 1)}
          >
            <ChevronDown size={14} />
          </button>
        {/if}

        <!-- Removes the frame and keeps what was inside it, which is what an operator means by
             deleting a group. The X below removes the group AND its conditions. -->
        <button
          type="button"
          class="ghost-button cond-wrap"
          title={$t('rewardTracks.unwrapHint')}
          disabled={!canManage}
          onclick={() => onunwrap(path)}
        >
          {$t('rewardTracks.unwrap')}
        </button>

        <button
          type="button"
          class="ghost-button cond-icon"
          title={$t('common.remove')}
          disabled={!canManage}
          onclick={() => onremove(path)}
        >
          <X size={14} />
        </button>
      </span>
    </div>

    <div class="group-body">
      {#each children as child, index (index)}
        <Self
          node={child}
          path={[...path, index]}
          siblings={children.length}
          {canManage}
          {facts}
          {operatorsFor}
          {referencesFor}
          picked={pickedFor([...path, index])}
          picker={child.kind === TEST ? pickerFor(child.factKey) : null}
          {onchange}
          {onfactchange}
          {onremove}
          {onwrap}
          {onunwrap}
          {onshift}
          {onadd}
          {onaddgroup}
          {onpick}
          {keyFor}
          {pickedFor}
          {pickerFor}
        />
      {/each}

      {#if children.length === 0}
        <!-- Said out loud because the engine does not read it the way it looks: an empty `any` is
             satisfied by nothing at all, and the server refuses to save one. -->
        <p class="muted small group-empty">{$t('rewardTracks.groupEmpty')}</p>
      {/if}

      {#if !full}
        <div class="group-add">
          <button
            type="button"
            class="ghost-button"
            disabled={!canManage || facts.length === 0}
            onclick={() => onadd(path)}
          >
            {$t('rewardTracks.addFilter')}
          </button>

          {#each GROUPS as g (g.kind)}
            <button
              type="button"
              class="ghost-button cond-wrap"
              disabled={!canManage}
              onclick={() => onaddgroup(path, g.kind)}
            >
              + {$t(g.labelKey)}
            </button>
          {/each}
        </div>
      {/if}
    </div>
  </div>
{/if}

<style>
  /* Sized by the global .condition-row rules, which already make a select take the width of what
     is in it. Only the wrap is added here: a condition that breaks onto a second line reads as
     two conditions, so it breaks only when the viewport leaves no choice. */
  .cond {
    flex-wrap: wrap;
  }

  /* Scratch's boolean shape. A condition answers yes or no, and the pointed ends are how that
     reads at a glance against the square blocks around it. */
  .cond-hex {
    flex: 0 0 auto;
    width: 16px;
    height: 13px;
    background: rgba(var(--gold-rgb), 0.85);
    clip-path: polygon(25% 0, 75% 0, 100% 50%, 75% 100%, 25% 100%, 0 50%);
  }

  .cond-picked {
    display: inline-flex;
    align-items: center;
    gap: 5px;
  }

  /*
    Pushed to the far edge so the sentence reads without its controls in the middle of it, and
    kept out of sight until the row is touched.

    That second part is the whole lesson of the version before this one: every condition carried
    three visible "put me in a group" buttons, and thirty of them down a task is the wall an
    operator said they could not find their way around. The tools are one hover away, and the
    sentence is what is on screen.
  */
  .cond-tools {
    display: inline-flex;
    align-items: center;
    gap: 2px;
    margin-left: auto;
    opacity: 0;
    transition: opacity 90ms ease;
  }

  .cond:hover .cond-tools,
  .cond:focus-within .cond-tools,
  .group:hover > .group-head .cond-tools,
  .group:focus-within > .group-head .cond-tools {
    opacity: 1;
  }

  /* The value is one word or one id, not a paragraph. Left to fill the row it made every
     condition read as a form rather than a sentence. */
  .cond input {
    max-width: 20rem;
  }

  .cond-icon {
    padding: 3px 5px;
  }

  .cond-wrap {
    padding: 2px 7px;
    font-size: 0.72rem;
    text-transform: uppercase;
    letter-spacing: 0.04em;
  }

  /* A group is a frame, and the frame IS the meaning -- so it is drawn, not implied by indent. */
  .group {
    margin: 6px 0;
    border: 1px solid var(--group);
    border-left: 4px solid var(--group);
    border-radius: 10px;
    background: color-mix(in srgb, var(--group) 10%, var(--surface));
  }

  .group--all {
    --group: #6cb2d1;
  }

  .group--any {
    --group: #dbb15e;
  }

  .group--not {
    --group: #e37c88;
  }

  .group-head {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 4px 8px 4px 10px;
  }

  .group-word {
    font-size: 0.74rem;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.08em;
    color: var(--group);
  }

  /* The indent that says "inside". Left only: a frame indented on both sides wastes the width a
     condition needs to stay on one line, and three levels of that leaves nothing. */
  .group-body {
    padding: 0 8px 8px 10px;
  }

  /* The frame already says "inside", so the block's own indent would say it twice and cost the
     width three levels of nesting cannot spare. A nested group needs no such rule: its own
     margin has never had a left. */
  .group-body :global(.block--filter) {
    margin-left: 0;
  }

  .group-empty {
    margin: 2px 0 6px;
  }

  .group-add {
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
    margin-top: 4px;
  }
</style>
