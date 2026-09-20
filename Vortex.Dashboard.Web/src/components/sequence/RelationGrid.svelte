<script lang="ts">
  import { t } from '../../lib/i18n';

  /**
   * Which tiles around a piece of furniture the player has to be standing on.
   *
   * A diagram rather than a <select>, because the sides are named from the FURNITURE and reading
   * that off a list gets it backwards: the piece is drawn facing you, so the tile on its right is
   * the one on your left. Nobody gets that right from the words "right side".
   *
   * The operator is written, never read: the picture decides it, so a filter cannot end up saying
   * "is" over three tiles.
   *
   * @type {{ value: string, canManage: boolean,
   *          onchange: (value: string, op: number) => void }}
   */
  let { value = '', canManage = true, onchange } = $props();

  // Screen layout, furniture in the middle facing DOWN. Left column is the piece's own right.
  const LAYOUT = [
    'back_right', 'back', 'back_left',
    'right', null, 'left',
    'front_right', 'front', 'front_left',
  ];

  let chosen = $derived(
    new Set(
      String(value ?? '')
        .split(',')
        .map((v) => v.trim())
        .filter(Boolean),
    ),
  );

  function toggle(side: string) {
    if (!canManage) return;

    const next = new Set(chosen);

    if (next.has(side)) next.delete(side);
    else next.add(side);

    // One tile is an equality, several is a list -- the operator follows the picture rather than
    // being a second thing to remember to change.
    const ordered = LAYOUT.filter((s) => s && next.has(s)) as string[];

    if (next.has('on')) ordered.unshift('on');

    onchange?.(ordered.join(','), ordered.length > 1 ? 2 : 0);
  }

  const label = (side: string) => $t(`rewardTracks.relation_${side}`);
</script>

<div class="relation" role="group" aria-label={$t('rewardTracks.fact_relation')}>
  {#each LAYOUT as side (side ?? 'centre')}
    {#if side === null}
      <button
        type="button"
        class="cell centre"
        class:sel={chosen.has('on')}
        disabled={!canManage}
        title={label('on')}
        onclick={() => toggle('on')}
      >
        <span class="piece"></span>
        <span class="facing" aria-hidden="true">▼</span>
      </button>
    {:else}
      <button
        type="button"
        class="cell"
        class:sel={chosen.has(side)}
        disabled={!canManage}
        onclick={() => toggle(side)}
      >
        {label(side)}
      </button>
    {/if}
  {/each}
</div>

<style>
  .relation {
    display: grid;
    grid-template-columns: repeat(3, 76px);
    grid-auto-rows: 44px;
    gap: 3px;
    padding: 3px;
    background: var(--line);
    border-radius: 8px;
  }

  .cell {
    display: grid;
    place-items: center;
    padding: 2px;
    border: 2px dashed var(--line-strong);
    border-radius: 5px;
    background: var(--input-bg);
    color: var(--muted);
    font-size: 10px;
    font-weight: 600;
    line-height: 1.15;
    text-align: center;
    cursor: pointer;
  }

  .cell:disabled {
    cursor: default;
  }

  .cell.sel {
    border-style: solid;
    border-color: var(--ok, #3fbf6f);
    background: color-mix(in srgb, var(--ok, #3fbf6f) 14%, transparent);
    color: var(--ink);
  }

  .centre {
    border-style: solid;
    border-color: var(--accent);
    background: color-mix(in srgb, var(--accent) 10%, transparent);
    gap: 1px;
  }

  .piece {
    width: 30px;
    height: 16px;
    border-radius: 3px;
    background: var(--accent);
    opacity: 0.85;
  }

  .facing {
    color: var(--accent);
    font-size: 10px;
    line-height: 1;
  }
</style>
