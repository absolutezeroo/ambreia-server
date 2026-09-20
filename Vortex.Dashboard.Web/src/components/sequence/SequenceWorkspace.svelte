<script lang="ts">
  // The sequence editor, full screen.
  //
  // It lived inside the task drawer, which is a narrow column: three selects and a value field do
  // not fit on one line there, so every condition wrapped and a three-step sequence read as a wall.
  // Blocks you arrange need room to be arranged in -- so this takes the whole viewport, and the
  // drawer keeps only a summary and the button that opens it.
  import { X } from '@lucide/svelte';
  import SequenceEditor from './SequenceEditor.svelte';
  import { t } from '../../lib/i18n';

  // Rendered on the body, not where it was written: a drawer sets its own stacking context, so a
  // full-screen layer inside one would be trapped in that column.
  function portal(node: HTMLElement) {
    document.body.appendChild(node);

    return { destroy: () => node.remove() };
  }

  type Props = {
    open?: boolean;
    title: string;
    onclose: () => void;
    /** Everything else is forwarded to the editor, which owns those props. */
    [key: string]: unknown;
  };

  let { open = false, title, onclose, ...editor }: Props = $props();

  function onkeydown(event: KeyboardEvent) {
    if (event.key === 'Escape') onclose();
  }
</script>

<svelte:window on:keydown={open ? onkeydown : undefined} />

{#if open}
  <div class="workspace-layer" use:portal>
    <header class="workspace-head">
      <div>
        <h2>{title}</h2>
        <p class="muted small">{$t('rewardTracks.sequenceHint')}</p>
      </div>
      <button type="button" class="ghost-button" onclick={onclose}>
        <X size={16} />
        {$t('common.close')}
      </button>
    </header>

    <!-- The editor's own props are forwarded whole; this shell adds only the chrome. -->
    <div class="workspace-body">
      <SequenceEditor {...(editor as any)} />
    </div>
  </div>
{/if}

<style>
  /*
    Above the drawer it was opened from (60), and BELOW the modal layer (80) -- because everything
    this opens is newer than it: the pickers, and with them the toasts, the loading overlay and the
    command palette, which all sit above 80.

    It was 1200, which is above the whole ladder, so a picker opened from here drew behind the
    editor that asked for it. That is the same mistake the modal layer's own comment records making
    against the drawer, one surface later: a number picked to clear one thing above it, that clears
    everything above it.

    The ladder: drawer 60 < workspace 70 < modal 80 < dropdown/tooltip 90 < loading 110 <
    palette 120 < toasts 130.
  */
  .workspace-layer {
    position: fixed;
    inset: 0;
    z-index: 70;
    display: flex;
    flex-direction: column;
    background: var(--page);
  }

  .workspace-head {
    display: flex;
    align-items: flex-start;
    justify-content: space-between;
    gap: 16px;
    padding: 18px 24px;
    border-bottom: 2px solid var(--line-strong);
    background: var(--surface);
  }

  .workspace-head h2 {
    margin: 0 0 4px;
    font-size: 1.05rem;
  }

  .workspace-head p {
    margin: 0;
    max-width: 70ch;
  }

  /* The blocks fill what is left and scroll, because a sequence is as tall as the operator made
     it. Blocks you arrange need room to be arranged in -- which is the whole reason this is a
     full-screen layer rather than a panel inside the task drawer's narrow column. */
  .workspace-body {
    flex: 1;
    min-height: 0;
    overflow: auto;
    padding: 20px 24px 40px;
  }
</style>

