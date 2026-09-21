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
   * The piece occupies its real footprint. A 2x6 drawn on one tile is a picture that disagrees
   * with itself -- the sprite says six tiles and the diagram says one -- and the engine measures
   * against the whole rectangle, so the diagram has to as well or the two describe different rooms.
   *
   * A side is still ONE answer however many tiles it spans: "in front of" a 2x6 is two tiles, and
   * clicking either picks the side. The engine resolves the side against the footprint; making an
   * operator choose one of two tiles would be asking them a question that has one answer.
   *
   * The operator is written, never read: the picture decides it, so a filter cannot end up saying
   * "is" over three tiles.
   *
   * @type {{ value: string, canManage: boolean, iconUrl?: string | null,
   *          width?: number, length?: number,
   *          onchange: (value: string, op: number) => void }}
   */
  let {
    value = '',
    canManage = true,
    iconUrl = null,
    width = 1,
    length = 1,
    onchange,
  } = $props();

  // Half a tile, in pixels. Habbo's floor is 2:1, so the diamond is twice as wide as it is tall
  // and the lattice below reads as a floor rather than as a rotated square.
  const HALF_W = 34;
  const HALF_H = 17;

  let w = $derived(Math.max(Math.min(width, 8), 1));
  let l = $derived(Math.max(Math.min(length, 8), 1));

  /**
   * Every tile of the diagram: the piece's footprint, and the ring of tiles around it.
   *
   * `x` runs down-RIGHT on screen and `y` down-LEFT, which is what puts `back` at the top and
   * `front` at the bottom -- toward the viewer, where the piece is facing. A tile's side is which
   * way it lies from the rectangle, so every tile along one edge carries the same answer.
   */
  let tiles = $derived.by(() => {
    const out: { x: number; y: number; side: string; key: string }[] = [];

    for (let x = -1; x <= w; x++) {
      for (let y = -1; y <= l; y++) {
        const onIt = x >= 0 && x < w && y >= 0 && y < l;
        // Which way this tile lies from the rectangle: -1 before it, +1 past it, 0 alongside.
        const dx = x < 0 ? -1 : x >= w ? 1 : 0;
        const dy = y < 0 ? -1 : y >= l ? 1 : 0;

        out.push({
          x,
          y,
          side: onIt ? 'on' : SIDES[`${dx},${dy}`],
          key: `${x},${y}`,
        });
      }
    }

    return out;
  });

  /**
   * Which relation each direction from the rectangle is.
   *
   * Derived from the engine rather than laid out by eye: RelativePositions takes the delta, reads
   * it as a compass rotation and subtracts the piece's facing. This table is that arithmetic for a
   * piece facing south, which is the diagram's convention -- the piece faces the viewer.
   *
   * Laid out by eye it was wrong, and a 1x1 could not show it: with one tile per side every map
   * that puts eight names on eight neighbours looks plausible. A 2x6 shows it at once, because
   * "front" landed on a single corner instead of the long edge the player actually walks along.
   */
  const SIDES: Record<string, string> = {
    '-1,-1': 'back_right',
    '0,-1': 'back',
    '1,-1': 'back_left',
    '-1,0': 'right',
    '1,0': 'left',
    '-1,1': 'front_right',
    '0,1': 'front',
    '1,1': 'front_left',
  };

  // Screen order: the tiles nearer the viewer are drawn last, so the piece standing on the middle
  // overlaps the tiles behind it rather than being cut by them.
  let drawn = $derived([...tiles].sort((a, b) => a.x + a.y - (b.x + b.y)));

  /*
    The lattice runs from -1 to w and from -1 to l, so its box is the sum of both runs plus the
    ring on either side. Arithmetic rather than numbers picked by eye -- which is what lets a 2x6
    and a 1x1 be the same component, and what made the first attempt's overflow findable by
    working the extremes out rather than by squinting at it.

    A tile is positioned by its centre and pulled back half its width in CSS, so the leftmost tile
    at x=-1,y=l has its left edge at exactly zero.
  */
  let boxWidth = $derived((w + l + 4) * HALF_W);
  let boxHeight = $derived((w + l + 2) * HALF_H + 2 * HALF_H);
  let originX = $derived((l + 2) * HALF_W);

  const left = (tile: { x: number; y: number }) => (tile.x - tile.y) * HALF_W;
  const top = (tile: { x: number; y: number }) => (tile.x + tile.y + 2) * HALF_H;

  let chosen = $derived(
    new Set(
      String(value ?? '')
        .split(',')
        .map((v) => v.trim())
        .filter(Boolean),
    ),
  );

  // The order a value is written in, so two editors of the same condition produce the same string.
  const ORDER = [
    'on',
    'back',
    'back_right',
    'back_left',
    'right',
    'left',
    'front_right',
    'front_left',
    'front',
  ];

  function toggle(side: string) {
    if (!canManage) return;

    const next = new Set(chosen);

    if (next.has(side)) next.delete(side);
    else next.add(side);

    // One side is an equality, several is a list -- the operator follows the picture rather than
    // being a second thing to remember to change.
    const ordered = ORDER.filter((s) => next.has(s));

    onchange?.(ordered.join(','), ordered.length > 1 ? 2 : 0);
  }

  const label = (side: string) => $t(`rewardTracks.relation_${side}`);

  // Named under the grid rather than inside the tiles: a 68x34 diamond has no room for "Back
  // right", and a label that does not fit is a label nobody reads.
  let caption = $derived(
    ORDER.filter((s) => chosen.has(s))
      .map(label)
      .join(' · '),
  );
</script>

<div class="relation">
  <div
    class="floor"
    role="group"
    aria-label={$t('rewardTracks.fact_relation')}
    style="width: {boxWidth}px; height: {boxHeight}px"
  >
    {#each drawn as tile (tile.key)}
      <button
        type="button"
        class="tile"
        class:sel={chosen.has(tile.side)}
        class:centre={tile.side === 'on'}
        style="left: {originX + left(tile)}px; top: {top(tile)}px"
        disabled={!canManage}
        title={label(tile.side)}
        aria-label={label(tile.side)}
        aria-pressed={chosen.has(tile.side)}
        onclick={() => toggle(tile.side)}
      ></button>
    {/each}

    <!-- Standing on the middle of its own footprint, so the diagram reads as a room rather than as
         a grid with a picture pasted over it. Not a button: the tiles underneath are. -->
    <div
      class="piece"
      style="left: {originX + ((w - 1) / 2 - (l - 1) / 2) * HALF_W}px; top: {((w - 1) / 2 +
        (l - 1) / 2 +
        2) *
        HALF_H}px"
    >
      {#if iconUrl}
        <AssetImage src={iconUrl} alt="" size={Math.min(40 + (w + l - 2) * 10, 96)} />
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

  .floor {
    position: relative;
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
    background: transparent;
    clip-path: polygon(50% 0, 100% 50%, 50% 100%, 0 50%);
    cursor: pointer;
  }

  /*
    The fill, inset by a pixel so the surface behind shows as the seam between tiles -- a border
    cannot follow a clip-path.

    A translucent white rather than a theme colour: the first version leaned on a difference
    between two navy tokens, and on a darker skin the two were the same and the whole floor
    disappeared.
  */
  .tile::after {
    content: '';
    position: absolute;
    inset: 1px;
    background: rgba(255, 255, 255, 0.09);
    clip-path: polygon(50% 0, 100% 50%, 50% 100%, 0 50%);
    transition: background 90ms ease;
  }

  .tile:hover:not(:disabled)::after {
    background: rgba(255, 255, 255, 0.2);
  }

  .tile.sel::after {
    background: rgba(var(--accent-rgb), 0.85);
  }

  /* The piece's own tiles: darker, because the sprite above is what the eye should land on. */
  .tile.centre::after {
    background: rgba(255, 255, 255, 0.03);
  }

  .tile.centre.sel::after {
    background: rgba(var(--accent-rgb), 0.85);
  }

  .tile:disabled {
    cursor: default;
  }

  /* Anchored to the middle of the footprint and sitting ON it: the sprite's feet are at the
     rectangle's centre, so it stands rather than floats. */
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
  }
</style>
