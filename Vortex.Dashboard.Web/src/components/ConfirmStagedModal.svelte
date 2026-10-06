<script lang="ts">
  // The confirm step for the authoring pages, whose forms already contain the audited reason: this
  // reads it back with the summary so the operator sees exactly what is about to be written and
  // under what justification. (The pages that collect the reason at confirm time use
  // ConfirmReasonModal instead -- that one has the input, this one has the read-back.)
  //
  // Eight pages carried a character-for-character copy of this dialog, each with its own
  // `<domain>.confirmEyebrow` and its own copy of the "Reason: {reason}" string.
  //
  //   <ConfirmStagedModal {ops} eyebrow={$t('common.confirm')} />
  import Modal from './Modal.svelte';
  import { t } from '../lib/i18n';

  
  type Props = {
    /** The createWriteOps store driving this dialog. */
    ops: any;
    eyebrow?: string;
  };

  let { ops, eyebrow = '' }: Props = $props();
</script>

{#if $ops.pending}
  <Modal
    title={$ops.pending.title}
    {eyebrow}
    width={460}
    labelledBy="confirm-staged-title"
    onclose={() => ops.cancel()}
  >
    <p>{$ops.pending.summary}</p>
    {#if $ops.pending.changes?.length}
      <!-- Same before/after read-back as ConfirmReasonModal: the line that catches a mistyped field
           before it is written, not after. -->
      <ul class="change-list">
        {#each $ops.pending.changes as change}
          <li>
            <span class="change-label">{change.label}</span>
            <span class="change-from">{change.from}</span>
            <span aria-hidden="true">→</span>
            <span class="change-to">{change.to}</span>
          </li>
        {/each}
      </ul>
    {/if}
    {#if $ops.pending.reason}
      <p class="muted">{$t('common.reasonLabel', { reason: $ops.pending.reason })}</p>
    {/if}
    {#if $ops.error}<p class="empty-state danger" role="alert">{$ops.error}</p>{/if}

    {#snippet actions()}

      <button type="button" onclick={() => ops.confirm()} disabled={$ops.busy}>
        {$t('common.confirm')}
      </button>
      <button class="ghost-button" type="button" onclick={() => ops.cancel()}>
        {$t('common.cancel')}
      </button>

    {/snippet}
  </Modal>
{/if}

<style>
  .change-list {
    list-style: none;
    margin: 12px 0;
    padding: 10px 12px;
    display: grid;
    gap: 6px;
    border: 1px solid var(--border, rgba(128, 128, 128, 0.3));
    border-radius: 8px;
    font-size: 0.86rem;
  }

  .change-list li {
    display: flex;
    align-items: baseline;
    gap: 8px;
    flex-wrap: wrap;
  }

  .change-label {
    flex: 1 1 40%;
    min-width: 0;
    opacity: 0.75;
  }

  .change-from {
    text-decoration: line-through;
    opacity: 0.6;
  }

  .change-to {
    font-weight: 600;
  }
</style>
