// Where an operator put the blocks of one sequence, kept between visits.
//
// The graph used to hold the arrangement in component state, with the reasoning that "a position is
// how one operator likes to look at a sequence, not something the hotel should store". That half is
// right and is why this is not on the task: nobody inherits anybody's arrangement, and nothing about
// it reaches the database or another account.
//
// The other half was wrong in practice. Dropping it on close meant tidying a canvas of a dozen
// blocks, leaving, coming back, and finding the automatic layout again -- so nobody tidied, and a
// sequence of any size read as wires crossing the screen. Per browser, per operator, per task is
// the reading that keeps the principle and fixes the room.

const PREFIX = 'vortex-dashboard-sequence-layout:';

/** Where one node sits, in canvas coordinates. */
export type NodePoint = { x: number; y: number };

/**
 * An entry may be missing: the graph's own layout type leaves a node out until it has been moved,
 * and reading one back that way is not an error. Writing skips them.
 */
export type GraphLayout = Record<string, NodePoint | undefined>;

/**
 * The stored arrangement for a sequence, or nothing.
 *
 * Every failure is an empty layout rather than a throw: a private window, blocked site data or a
 * half-written entry must leave the editor working and auto-arranged, which is exactly what it did
 * before any of this existed.
 */
export function readLayout(key: string | null | undefined): GraphLayout {
  if (!key || typeof localStorage === 'undefined') return {};

  try {
    const raw = localStorage.getItem(PREFIX + key);
    const parsed = raw ? JSON.parse(raw) : null;

    if (!parsed || typeof parsed !== 'object') return {};

    const layout: GraphLayout = {};

    for (const [id, point] of Object.entries(parsed as Record<string, unknown>)) {
      const p = point as NodePoint;

      // A node that lost one of its coordinates is dropped rather than placed at zero, where it
      // would sit under the first block and look like a bug in the editor.
      if (p && Number.isFinite(p.x) && Number.isFinite(p.y)) layout[id] = { x: p.x, y: p.y };
    }

    return layout;
  } catch {
    return {};
  }
}

export function writeLayout(key: string | null | undefined, layout: GraphLayout): void {
  if (!key || typeof localStorage === 'undefined') return;

  try {
    const placed: Record<string, NodePoint> = {};

    for (const [id, point] of Object.entries(layout)) {
      if (point) placed[id] = point;
    }

    localStorage.setItem(PREFIX + key, JSON.stringify(placed));
  } catch {
    // Best effort. A full quota or a locked-down browser costs the arrangement, nothing else.
  }
}
