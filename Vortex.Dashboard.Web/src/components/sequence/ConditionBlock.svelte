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
    LITERAL,
    NOT,
    TEST,
    decodeSide,
    encodeSide,
    type FactOption,
    type NodePath,
    type SideValue,
    type TreeNode,
  } from '../../lib/sequence/tree';
  import { t } from '../../lib/i18n';

  /** One choice in a side's dropdown, and the group it is listed under. */
  type SideChoice = { group: string; label: string; value: SideValue };

  type Props = {
    node: TreeNode;
    path: NodePath;
    /** How many siblings it has, so the first and last do not offer a move with nowhere to go. */
    siblings: number;
    canManage: boolean;
    facts: FactOption[];
    /** What a side may be: this signal's facts, their counts, and earlier steps' facts. */
    sideChoices: SideChoice[];
    /** The operators that mean anything about what is on the left. */
    operatorsFor: (left: SideValue) => { value: number; key: string }[];
    picker: string | null;
    onchange: (path: NodePath, node: TreeNode) => void;
    /** Changing a side resets the operator and the other side, which belonged to the old one. */
    onsidechange: (path: NodePath, side: 'left' | 'right', value: SideValue) => void;
    onremove: (path: NodePath) => void;
    onwrap: (path: NodePath, kind: 1 | 2 | 3) => void;
    onunwrap: (path: NodePath) => void;
    onshift: (path: NodePath, by: -1 | 1) => void;
    onadd: (path: NodePath) => void;
    onaddgroup: (path: NodePath, kind: 1 | 2 | 3) => void;
    onpick: (path: NodePath, kind: string) => void;
    /** Everything above, forwarded to a child block unchanged. */
    pickerFor: (factKey: string) => string | null;
  } & Record<string, unknown>;

  let {
    node,
    path,
    siblings,
    canManage,
    facts,
    sideChoices,
    operatorsFor,
    picker,
    onchange,
    onsidechange,
    onremove,
    onwrap,
    onunwrap,
    onshift,
    onadd,
    onaddgroup,
    onpick,
    pickerFor,
  }: Props = $props();

  const GROUPS: { kind: 1 | 2 | 3; labelKey: string }[] = [
    { kind: ALL, labelKey: 'rewardTracks.groupAll' },
    { kind: ANY, labelKey: 'rewardTracks.groupAny' },
    { kind: NOT, labelKey: 'rewardTracks.groupNot' },
  ];

  let test = $derived(node.kind === TEST ? (node as any) : null);

  /**
   * The fact the left side names, when it names one.
   *
   * What it is for: a closed fact offers its values instead of a text box, and the relation grid
   * replaces the box entirely. Both are about what the LEFT side is, because that is what the
   * comparison is about.
   */
  let meta = $derived(
    test && test.left.kind === 0 ? (facts.find((f) => f.key === test.left.arg) ?? null) : null,
  );

  /** Grouped the way the dropdown lists them, keeping the order the caller chose. */
  let grouped = $derived(
    sideChoices.reduce((groups: { name: string; items: SideChoice[] }[], choice) => {
      const last = groups[groups.length - 1];

      if (last?.name === choice.group) {
        last.items.push(choice);
      } else {
        groups.push({ name: choice.group, items: [choice] });
      }

      return groups;
    }, []),
  );

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

    <!-- Both sides are the same control, because both sides are the same thing. One dropdown per
         side rather than a kind, a step and a fact: choosing "the room of step 1" is one phrase,
         not a three-step errand. -->
    <select
      class="cond-subject"
      value={encodeSide(test.left)}
      onchange={(e) =>
        onsidechange(path, 'left', decodeSide((e.currentTarget as HTMLSelectElement).value))}
      disabled={!canManage}
    >
      {#each grouped as group (group.name)}
        <optgroup label={group.name}>
          {#each group.items as choice (encodeSide(choice.value))}
            <option value={encodeSide(choice.value)}>{choice.label}</option>
          {/each}
        </optgroup>
      {/each}
    </select>

    <select
      class="cond-verb"
      value={test.op}
      onchange={(e) =>
        onchange(path, { ...test, op: Number((e.currentTarget as HTMLSelectElement).value) })}
      disabled={!canManage}
    >
      {#each operatorsFor(test.left) as op (op.value)}
        <option value={op.value}>{$t(op.key)}</option>
      {/each}
    </select>

    <!-- A typed value is ONE option however it is filled in, so the option identifies the kind and
         not the text. Encoding the text here matched no option and left the box blank, which is
         what an operator saw where their own value was. -->
    <select
      class="cond-subject"
      value={test.right.kind === LITERAL
        ? encodeSide({ kind: LITERAL, arg: '', step: -1 })
        : encodeSide(test.right)}
      onchange={(e) =>
        onsidechange(path, 'right', decodeSide((e.currentTarget as HTMLSelectElement).value))}
      disabled={!canManage}
    >
      <!--
        Listed first because it is what a condition usually compares against.

        Named for what the operator will actually do: a closed fact and the relation grid both
        offer their values, so "a value you type" was a promise of a text box that never appears.
      -->
      <option value={encodeSide({ kind: LITERAL, arg: '', step: -1 })}>
        {$t(
          meta?.values?.length || meta?.key === 'relation'
            ? 'rewardTracks.sideChosen'
            : 'rewardTracks.sideTyped'
        )}
      </option>
      {#each grouped as group (group.name)}
        <optgroup label={group.name}>
          {#each group.items as choice (encodeSide(choice.value))}
            <option value={encodeSide(choice.value)}>{choice.label}</option>
          {/each}
        </optgroup>
      {/each}
    </select>

    <!-- Only a typed value needs a box to type in. Every other side is already chosen above. -->
    {#if test.right.kind === LITERAL}
      {#if meta?.key === 'relation'}
        <!-- The one closed fact a list cannot express: its sides are named from the furniture, so
             "right side" reads backwards against a picture of the thing facing you.

             On its own line, because it is three rows tall: inline it sat beside a one-line
             sentence and left the rest of the block empty, which reads as something missing. -->
        <div class="cond-grid">
          <RelationGrid
            value={test.right.arg}
            {canManage}
            onchange={(value: string, op: number) =>
              onchange(path, { ...test, op, right: { ...test.right, arg: value } })}
          />
        </div>
      {:else if meta?.values?.length}
        <!-- A closed fact: the server declares what it accepts, so a typo cannot be entered. -->
        <select
          class="cond-value"
          value={test.right.arg}
          onchange={(e) =>
            onchange(path, {
              ...test,
              right: { ...test.right, arg: (e.currentTarget as HTMLSelectElement).value },
            })}
          disabled={!canManage}
        >
          {#each meta.values as allowed (allowed.value)}
            <option value={allowed.value}>{label(allowed)}</option>
          {/each}
        </select>
      {:else}
        <input
          class="cond-value"
          type="text"
          value={test.right.arg}
          oninput={(e) =>
            onchange(path, {
              ...test,
              // Typed over, so the name from the picker describes an id that is no longer here.
              picked: undefined,
              right: { ...test.right, arg: (e.currentTarget as HTMLInputElement).value },
            })}
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
    {/if}

    {#if test.picked?.name}
      <span class="muted small cond-picked">
        {#if test.picked.iconUrl}
          <AssetImage src={test.picked.iconUrl} alt="" size={20} />
        {/if}
        {test.picked.name}
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
          {sideChoices}
          {operatorsFor}
          picker={child.kind === TEST && child.left.kind === 0 ? pickerFor(child.left.arg) : null}
          {onchange}
          {onsidechange}
          {onremove}
          {onwrap}
          {onunwrap}
          {onshift}
          {onadd}
          {onaddgroup}
          {onpick}
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

  /* The relation grid is three rows tall. Given the whole width it sits under its sentence; left
     inline it stretched the block to its own height and left the rest of the row empty. */
  .cond-grid {
    flex: 1 0 100%;
    margin-top: 6px;
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
