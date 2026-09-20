<script lang="ts">
  import type { Fact } from '../../lib/graph/model';
  // A condition, as a module of its own.
  //
  // It was a row buried inside an action node, which is where a filter looks like a property of
  // the action rather than a test somebody wrote. As a node it can be dragged out of the palette,
  // moved to another action by re-plugging one wire, and read on its own -- and its two wires say
  // the two things a filter actually has: which action it constrains, and where its value comes
  // from.
  import { Search, X } from '@lucide/svelte';
  import AssetImage from '../AssetImage.svelte';
  import { portColour } from '../../lib/graph/model';
  import { t } from '../../lib/i18n';

  /**
   * @type {{
   *   node: any, facts: any[], operators: any[], canManage: boolean, selected: boolean,
   *   picked: any, picker: string | null, candidate: boolean, blocked: boolean,
   *   onmovestart: (e: PointerEvent) => void, onchange: () => void, onremove: () => void,
   *   onpick: () => void, onvaluedown: (e: PointerEvent) => void, onvalueup: () => void,
   *   onappliesdown: (e: PointerEvent) => void, oncut: () => void,
   * }}
   */
  let {
    node,
    facts,
    operators,
    canManage,
    selected,
    picked,
    picker,
    candidate,
    blocked,
    onmovestart,
    onchange,
    onremove,
    onpick,
    onvaluedown,
    onvalueup,
    onappliesdown,
    oncut,
  } = $props();

  // Read from the filter rather than the render snapshot: the select below binds straight into it,
  // so the values list has to follow the fact within the same interaction.
  let meta = $derived(facts.find((f: Fact) => f.key === node.filter.factKey) ?? null);

  function label(fact: Fact) {
    const translated = $t(fact.labelKey ?? fact.key);

    return translated === (fact.labelKey ?? fact.key) ? fact.fallbackLabel : translated;
  }
</script>

<div class="node condition" class:selected style:left="{node.x}px" style:top="{node.y}px">
  <header class="node-head" onpointerdown={canManage ? onmovestart : undefined}>
    <span class="node-kind">{$t('rewardTracks.conditionNode')}</span>
    {#if canManage}
      <button type="button" class="node-close" title={$t('common.remove')} onclick={onremove}>
        <X size={13} />
      </button>
    {/if}
  </header>

  <!-- Where the value comes from: empty means a literal typed below, wired means an earlier
       action's recorded value. -->
  <span
    class="port value"
    class:wired={node.wiredTo >= 0}
    class:candidate
    class:blocked
    style:--port={portColour(meta?.kind)}
    data-port="{node.id}:value"
    onpointerdown={(e) => canManage && node.wiredTo < 0 && onvaluedown(e)}
    onpointerup={onvalueup}
    onclick={() => node.wiredTo >= 0 && oncut()}
    role="presentation"
    title={node.wiredTo >= 0
      ? $t('rewardTracks.cutWire', { n: node.wiredTo + 1 })
      : $t('rewardTracks.wireHint')}
  ></span>

  <div class="node-body">
    <!-- One line, read left to right: "The furniture — is — that sofa". It was a labelled field
         stacked above a row, which is a form about a filter rather than the sentence the filter
         actually is, and three of them stacked read as a wall on a canvas. -->
    <div class="node-row sentence">
      <select
        class="subject"
        bind:value={node.filter.factKey}
        disabled={!canManage}
        onchange={onchange}
      >
        {#each facts as fact (fact.key)}
          <option value={fact.key}>{label(fact)}</option>
        {/each}
      </select>

      <select class="verb" bind:value={node.filter.op} disabled={!canManage} onchange={onchange}>
        {#each operators as op (op.value)}
          <option value={op.value}>{$t(op.key)}</option>
        {/each}
      </select>

      {#if node.wiredTo >= 0}
        <span class="wired-value">
          {$t('rewardTracks.filterSameAsStep', { n: node.wiredTo + 1 })}
        </span>
      {:else if meta?.values?.length}
        <select bind:value={node.filter.value} disabled={!canManage} onchange={onchange}>
          {#each meta.values as allowed (allowed.value)}
            <option value={allowed.value}>{label(allowed)}</option>
          {/each}
        </select>
      {:else}
        <input
          type="text"
          bind:value={node.filter.value}
          disabled={!canManage}
          onchange={onchange}
          placeholder={$t('rewardTracks.conditionValuePlaceholder')}
        />
        {#if picker}
          <button
            type="button"
            class="node-icon-button"
            title={$t('rewardTracks.pickValue')}
            disabled={!canManage}
            onclick={onpick}
          >
            <Search size={13} />
          </button>
        {/if}
      {/if}
    </div>

    {#if picked?.name}
      <span class="picked">
        {#if picked.iconUrl}<AssetImage src={picked.iconUrl} alt="" size={18} />{/if}
        {picked.name}
      </span>
    {/if}
  </div>

  <!-- Which action this test applies to. Dragging this is how a condition changes owner. -->
  <div class="applies">
    <span class="applies-label">{$t('rewardTracks.appliesTo', { n: node.index + 1 })}</span>
    <span
      class="port applies-port"
      data-port="{node.id}:applies"
      onpointerdown={(e) => canManage && onappliesdown(e)}
      role="presentation"
      title={$t('rewardTracks.dragApplies')}
    ></span>
  </div>
</div>

<style>
  .node {
    position: absolute;
    /* Wide enough for the sentence to stay on one line. It was 300px, which is a column width --
       the node lived in a drawer once -- and on a canvas there is nothing a narrow block buys:
       it only makes the three parts of one condition stack into three rows. */
    width: 420px;
    border: 2px solid var(--line-strong);
    border-radius: 5px;
    background: var(--surface);
    box-shadow: var(--panel-shadow);
    user-select: none;
  }

  /* Gold, because that is already what a condition looks like in this dashboard: the filter blocks
     in the editor it replaces were gold, and an operator should not have to learn a second colour
     for the same idea. */
  .condition .node-head {
    border-left: 3px solid var(--gold);
  }

  .node.selected {
    border-color: var(--gold);
    box-shadow: var(--panel-shadow), 0 0 0 1px rgba(var(--gold-rgb), 0.35);
  }

  .node-head {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
    padding: 7px 10px;
    background: var(--surface-strong);
    border-bottom: 2px solid var(--line-strong);
    border-radius: 3px 3px 0 0;
    cursor: grab;
  }

  .node-head:active {
    cursor: grabbing;
  }

  .node-kind {
    color: var(--gold);
    font-size: 0.68rem;
    font-weight: 700;
    letter-spacing: 0.08em;
    text-transform: uppercase;
  }

  .node-close {
    display: inline-flex;
    padding: 2px;
    border: 0;
    border-radius: 3px;
    background: transparent;
    color: var(--muted);
    cursor: pointer;
  }

  .node-body {
    display: flex;
    flex-direction: column;
    gap: 7px;
    padding: 9px 10px;
  }

  .node-row {
    display: flex;
    align-items: center;
    gap: 5px;
  }

  /* One line, read left to right. It wraps only when a value field genuinely cannot fit, which at
     this width means a long free-text value and not the ordinary picked one. */
  .sentence {
    flex-wrap: wrap;
  }

  /* Sized by what each part says. The subject is the longest -- "Room description", "Where they
     stood" -- and the verb is two words and should never take a share of the line for it. */
  .sentence .subject {
    flex: 1 1 9rem;
  }

  .sentence .verb {
    flex: 0 0 auto;
    width: auto;
    min-width: 5rem;
  }

  .node-row select,
  .node-row input {
    flex: 1 1 4rem;
    min-width: 0;
    width: auto;
    font-size: 0.74rem;
    padding: 3px 6px;
  }

  .node-row select:first-child {
    flex: 0 1 auto;
  }

  .wired-value {
    flex: 1 1 auto;
    color: var(--accent-strong);
    font-size: 0.74rem;
    font-weight: 600;
  }

  .picked {
    display: flex;
    align-items: center;
    gap: 5px;
    color: var(--muted);
    font-size: 0.7rem;
  }

  .node-icon-button {
    flex: 0 0 auto;
    display: inline-flex;
    padding: 3px;
    border: 1px solid var(--line);
    border-radius: 4px;
    background: transparent;
    color: var(--muted);
    cursor: pointer;
  }

  .applies {
    display: flex;
    align-items: center;
    justify-content: flex-end;
    gap: 7px;
    padding: 7px 10px 9px;
    border-top: 2px solid var(--line);
    background: var(--surface-strong);
    border-radius: 0 0 3px 3px;
    color: var(--muted);
    font-size: 0.7rem;
  }

  .port {
    width: 10px;
    height: 10px;
    border-radius: 50%;
    border: 2px solid var(--port, var(--muted));
    background: var(--surface);
    cursor: crosshair;
  }

  .port.value {
    position: absolute;
    left: -6px;
    top: 46px;
  }

  .port.wired {
    background: var(--port, var(--gold));
  }

  .port.candidate {
    border-color: var(--gold);
    background: var(--gold-soft);
    box-shadow: 0 0 0 4px rgba(var(--gold-rgb), 0.22);
  }

  .port.blocked {
    opacity: 0.25;
  }

  .applies-port {
    position: relative;
    margin-right: -16px;
    border-color: var(--accent);
    background: var(--accent);
  }
</style>
