<script lang="ts">
  import type { Fact } from '../../lib/graph/model';
  // One condition, as a block that can be picked up and dropped under another action.
  //
  // The drag handle is its own element rather than the whole block: the row is full of selects and
  // inputs, and making the block draggable would mean an operator could not select text in the
  // value field without starting a drag.
  import { GripVertical, Search, X } from '@lucide/svelte';
  import AssetImage from '../AssetImage.svelte';
  import RelationGrid from './RelationGrid.svelte';
  import { t } from '../../lib/i18n';

  /**
   * @type {{
   *   filter: any,
   *   facts: any[],
   *   operators: any[],
   *   references: any[],
   *   picked: any,
   *   picker: string | null,
   *   canManage: boolean,
   *   dragging: boolean,
   *   onfactchange: (key: string) => void,
   *   onpick: () => void,
   *   onremove: () => void,
   *   ondragstart: (event: DragEvent) => void,
   *   ondragend: () => void,
   * }}
   */
  let {
    filter,
    facts,
    operators,
    references,
    picked,
    picker,
    canManage,
    dragging,
    onfactchange,
    onpick,
    onremove,
    ondragstart,
    ondragend,
  } = $props();

  let meta = $derived(facts.find((f: Fact) => f.key === filter.factKey) ?? null);
  let label = (fact: Fact) => {
    const translated = $t(fact.labelKey ?? fact.key);

    return translated === (fact.labelKey ?? fact.key) ? fact.fallbackLabel : translated;
  };
</script>

<div class="condition-row block block--filter" class:dragging role="listitem">
  <span
    class="block-grip"
    draggable={canManage}
    ondragstart={ondragstart}
    ondragend={ondragend}
    title={$t('rewardTracks.dragFilter')}
    aria-hidden="true"
  >
    <GripVertical size={14} />
  </span>

  <select
    bind:value={filter.factKey}
    onchange={() => onfactchange(filter.factKey)}
    disabled={!canManage}
  >
    {#each facts as fact (fact.key)}
      <option value={fact.key}>{label(fact)}</option>
    {/each}
  </select>

  <select bind:value={filter.op} disabled={!canManage}>
    {#each operators as op (op.value)}
      <option value={op.value}>{$t(op.key)}</option>
    {/each}
  </select>

  <!-- A reference is only offered where it can resolve, so an impossible one cannot be picked;
       the server refuses the same thing on save. -->
  {#if references.length > 0}
    <select bind:value={filter.value} disabled={!canManage}>
      <option value="">{$t('rewardTracks.filterLiteral')}</option>
      {#each references as ref (ref.value)}
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
      value={filter.value}
      {canManage}
      onchange={(value: string, op: number) => {
        filter.value = value;
        filter.op = op;
      }}
    />
  {:else if meta?.values?.length}
    <!-- A closed fact: the server declares what it accepts, so a typo cannot be entered. -->
    <select bind:value={filter.value} disabled={!canManage}>
      {#each meta.values as allowed (allowed.value)}
        <option value={allowed.value}>{label(allowed)}</option>
      {/each}
    </select>
  {:else if !String(filter.value).startsWith('$')}
    <input
      type="text"
      bind:value={filter.value}
      disabled={!canManage}
      placeholder={$t(
        Number(filter.op) === 2
          ? 'rewardTracks.conditionListPlaceholder'
          : 'rewardTracks.conditionValuePlaceholder'
      )}
    />
    {#if picker}
      <button
        type="button"
        class="ghost-button block-pick"
        title={$t('rewardTracks.pickValue')}
        disabled={!canManage}
        onclick={onpick}
      >
        <Search size={14} />
      </button>
    {/if}
  {/if}

  <button
    type="button"
    class="ghost-button block-remove"
    title={$t('common.remove')}
    disabled={!canManage}
    onclick={onremove}
  >
    <X size={14} />
  </button>

  {#if picked?.name}
    <span class="muted small picked-name">
      {#if picked.iconUrl}
        <AssetImage src={picked.iconUrl} alt="" size={20} />
      {/if}
      {picked.name}
    </span>
  {/if}
</div>

<style>
  .block-grip {
    flex: 0 0 auto;
    display: inline-flex;
    align-items: center;
    cursor: grab;
    color: var(--muted);
  }

  .block-grip:active {
    cursor: grabbing;
  }

  /* The block being carried stays visible but recedes, so the gap that opens under the cursor is
     what the eye follows rather than the block itself. */
  .dragging {
    opacity: 0.4;
  }
</style>
