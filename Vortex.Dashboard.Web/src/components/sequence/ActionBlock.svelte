<script lang="ts">
  // One action and the conditions clipped under it. The unit an operator drags.
  import { GripVertical, X } from '@lucide/svelte';
  import { t, translate } from '../../lib/i18n';

  /**
   * @type {{
   *   step: any,
   *   index: number,
   *   actions: any[],
   *   canManage: boolean,
   *   removable: boolean,
   *   dragging: boolean,
   *   ondragstart: (event: DragEvent) => void,
   *   ondragend: () => void,
   *   onactionchange: () => void,
   *   onremove: () => void,
   *   children?: any,
   * }}
   */
  let {
    step,
    index,
    actions,
    canManage,
    removable,
    dragging,
    ondragstart,
    ondragend,
    onactionchange,
    onremove,
    children,
  } = $props();
</script>

<div class="step-card" class:dragging>
  <div class="step-head block block--action">
    <span
      class="block-grip"
      draggable={canManage}
      ondragstart={ondragstart}
      ondragend={ondragend}
      title={$t('rewardTracks.dragAction')}
      aria-hidden="true"
    >
      <GripVertical size={14} />
    </span>
    <span class="block-label">{$t('rewardTracks.stepN', { n: index + 1 })}</span>
    <select bind:value={step.actionCode} onchange={onactionchange} disabled={!canManage}>
      {#each actions as action (action.name)}
        <option value={action.name}>
          {action.name}{action.wired ? '' : ` — ${translate('rewardTracks.notWired')}`}
        </option>
      {/each}
    </select>
    {#if removable}
      <button
        type="button"
        class="ghost-button block-remove"
        title={$t('common.remove')}
        disabled={!canManage}
        onclick={onremove}
      >
        <X size={14} />
      </button>
    {/if}
  </div>

  {@render children?.()}
</div>

<style>
  .step-card {
    margin-bottom: 14px;
  }

  .block-grip {
    flex: 0 0 auto;
    display: inline-flex;
    align-items: center;
    cursor: grab;
    color: var(--accent-strong);
  }

  .block-grip:active {
    cursor: grabbing;
  }

  /*
    The shared .block-remove pins the button to 24px square, and inside that the icon measured
    0px wide -- drawn, sized, and squeezed to nothing. An empty bordered box is what an operator
    saw where the delete was.

    Sized to its content instead, which is what every other icon button in this editor does.
    The colour is set because the ghost button's ink is chosen against the page, and this one sits
    on the action block's own tint.
  */
  .step-head .block-remove {
    width: auto;
    padding: 3px 5px;
    color: var(--muted);
  }

  .step-head .block-remove:hover:not(:disabled) {
    color: var(--danger);
  }

  .dragging {
    opacity: 0.4;
  }
</style>
