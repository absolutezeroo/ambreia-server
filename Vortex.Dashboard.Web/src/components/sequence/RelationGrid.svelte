<script lang="ts">
  import AssetImage from '../AssetImage.svelte';
  import { t } from '../../lib/i18n';

  /**
   * Which tiles around a piece of furniture the player has to be standing on.
   *
   * Drawn as the room's own floor: isometric, because that is what an operator is looking at when
   * they picture somebody standing in front of a sofa. A flat 3x3 of labelled boxes made the sides
   * words to decode -- and the words get it backwards, since the sides are named from the
   * FURNITURE: the piece faces you, so the tile on its right is the one on your left. On a diamond
   * lattice with the piece in the middle, nobody has to decode anything.
   *
   * `iconUrl` is the furniture a condition beside this one names, when it names one. A generic
   * block in the middle makes "in front" a word; the piece itself makes it a picture, and the
   * picture is the entire reason this is a diagram rather than a list.
   *
   * The operator is written, never read: the picture decides it, so a filter cannot end up saying
   * "is" over three tiles.
   *
   * @type {{ value: string, canManage: boolean, iconUrl?: string | null,
   *          onchange: (value: string, op: number) => void }}
   */
  let { value = '', canManage = true, iconUrl = null, onchange } = $props();

  // Half a tile, in pixels. Habbo's floor is 2:1, so the diamond is twice as wide as it is tall
  // and the lattice below reads as a floor rather than as a rotated square.
  const HALF_W = 34;
  const HALF_H = 17;

  /**
   * The nine tiles, by their position on the lattice.
   *
   * `x` runs down-RIGHT on screen and `y` down-LEFT, which is what puts `back` at the top and
   * `front` at the bottom -- toward the viewer, where the piece is facing. `right` lands on the
   * screen's left for the reason above: it is the furniture's right, and the furniture faces out.
   */
  const TILES: { side: string; x: number; y: number }[] = [
    { side: 'back', x: 0, y: 0 },
    { side: 'back_right', x: 0, y: 1 },
    { side: 'back_left', x: 1, y: 0 },
    { side: 'right', x: 0, y: 2 },
    { side: 'on', x: 1, y: 1 },
    { side: 'left', x: 2, y: 0 },
    { side: 'front_right', x: 1, y: 2 },
    { side: 'front_left', x: 2, y: 1 },
    { side: 'front', x: 2, y: 2 },
  ];

  // Screen order: the tiles nearer the viewer are drawn last, so a sprite standing on one overlaps
  // the tile behind it rather than being cut by it.
  const DRAWN = [...TILES].sort((a, b) => a.x + a.y - (b.x + b.y));

  const left = (t: { x: number; y: number }) => (t.x - t.y) * HALF_W;
  const top = (t: { x: number; y: number }) => (t.x + t.y) * HALF_H;

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
    const ordered = TILES.map((t) => t.side).filter((s) => next.has(s));

    onchange?.(ordered.join(','), ordered.length > 1 ? 2 : 0);
  }

  const label = (side: string) => $t(`rewardTracks.relation_${side}`);

  // Named under the grid rather than inside the tiles: a 68x34 diamond has no room for "Back
  // right", and a label that does not fit is a label nobody reads.
  let caption = $derived(
    TILES.map((t) => t.side)
      .filter((s) => chosen.has(s))
      .map(label)
      .join(' · '),
  );
</script>

<div class="relation">
  <div class="floor" role="group" aria-label={$t('rewardTracks.fact_relation')}>
    {#each DRAWN as tile (tile.side)}
      <button
        type="button"
        class="tile"
        class:sel={chosen.has(tile.side)}
        class:centre={tile.side === 'on'}
        style="left: calc(50% + {left(tile)}px); top: {top(tile)}px"
        disabled={!canManage}
        title={label(tile.side)}
        aria-label={label(tile.side)}
        aria-pressed={chosen.has(tile.side)}
        onclick={() => toggle(tile.side)}
      ></button>
    {/each}

    <!-- On the middle tile and standing on it, so the diagram reads as a room rather than as a
         grid with a picture pasted over it. Not a button: the tile underneath is. -->
    <div class="piece" style="left: calc(50% + {left(TILES[4])}px); top: {top(TILES[4])}px">
      {#if iconUrl}
        <AssetImage src={iconUrl} alt="" size={40} />
      {:else}
        <span class="block"></span>
      {/if}
    </div>
  </div>

  <p class="caption muted small">
    {caption || $t('rewardTracks.relationNone')}
  </p>
</div>

<style>
  .relation {
    display: inline-block;
  }

  /* Tall enough for four half-tiles of lattice plus one tile, and wide enough for two either
     side of the middle. Both are the lattice's own arithmetic, not numbers picked by eye. */
  .floor {
    position: relative;
    width: 208px;
    height: 102px;
    margin: 2px 0 4px;
  }

  /* One floor tile: a diamond, 2:1, the proportion Habbo's own floor is drawn at. */
  .tile {
    position: absolute;
    width: 68px;
    height: 34px;
    margin-left: -34px;
    padding: 0;
    border: 0;
    background: var(--input-bg);
    clip-path: polygon(50% 0, 100% 50%, 50% 100%, 0 50%);
    cursor: pointer;
    transition: background 90ms ease;
  }

  /* The seam between tiles. A border cannot follow a clip-path, so the gap is drawn by insetting
     the fill of each diamond by a pixel of the surface behind it. */
  .tile::after {
    content: '';
    position: absolute;
    inset: 1px;
    background: var(--surface);
    clip-path: polygon(50% 0, 100% 50%, 50% 100%, 0 50%);
  }

  .tile:hover:not(:disabled)::after {
    background: var(--surface-hover);
  }

  .tile.sel::after {
    background: rgba(var(--accent-rgb), 0.85);
  }

  /* The tile the piece stands on. Selectable like the rest -- "on it" is a real answer -- but
     dimmer, because the sprite above is what the eye should land on. */
  .tile.centre::after {
    background: var(--surface-strong);
  }

  .tile.centre.sel::after {
    background: rgba(var(--accent-rgb), 0.85);
  }

  .tile:disabled {
    cursor: default;
  }

  /* Anchored to the middle of its tile and sitting ON it: the sprite's feet are at the tile's
     centre, so it stands rather than floats. */
  .piece {
    position: absolute;
    display: grid;
    place-items: end center;
    width: 68px;
    height: 34px;
    margin-left: -34px;
    pointer-events: none;
  }

  .piece :global(img) {
    margin-bottom: 4px;
  }

  .block {
    width: 26px;
    height: 20px;
    margin-bottom: 6px;
    border-radius: 3px;
    background: rgba(var(--accent-rgb), 0.75);
  }

  .caption {
    margin: 0;
    max-width: 208px;
  }
</style>
