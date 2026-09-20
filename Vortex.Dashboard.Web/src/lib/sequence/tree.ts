/**
 * A step's conditions, as the tree the engine reads.
 *
 * A step used to hold a flat list that ANDed. It now holds a tree: a test, or a group of nodes
 * joined by all / any / not. The list is one of the shapes -- a list of tests under an implicit
 * `all` -- which is why nothing written before this has to change to be read by it.
 *
 * Everything here is pure and addresses a node by its **path**: the child index at each level, so
 * `[1, 0]` is the first condition inside the second thing in the step. Paths rather than object
 * identity because the editor rebuilds these objects on every keystroke, and an editor that lost
 * track of which block it was editing would write the operator's change into a different condition.
 */

/** The four kinds, by the number the editor, the validator and the engine share. */
export const TEST = 0;
export const ALL = 1;
export const ANY = 2;
export const NOT = 3;

/** Where one side of a comparison gets its value. Mirrors StepValueKind. */
export const FACT = 0;
export const LITERAL = 1;
export const STEP_FACT = 2;
export const COUNT_OF = 3;

/**
 * One side of a comparison.
 *
 * `arg` is a fact key for everything but a literal, where it is the text itself. `step` names an
 * earlier step and is -1 for every other kind.
 */
export type SideValue = { kind: number; arg: string; step: number };

/**
 * One test: two values and the operator between them.
 *
 * The wire still carries `factKey` and `value` beside the sides, because the row does. The editor
 * does not: two spellings of one thing, both editable, is two spellings that drift. They are
 * derived on the way out, in `toRows`, and dropped on the way in.
 */
export type TestNode = { kind: 0; op: number | string; left: SideValue; right: SideValue };

/** One group: the nodes it joins. A `not` holds exactly one. */
export type GroupNode = { kind: 1 | 2 | 3; children: TreeNode[] };

export type TreeNode = TestNode | GroupNode;

/** Where a node sits: the child index at each level, outermost first. */
export type NodePath = number[];

export function isTest(node: TreeNode): node is TestNode {
  return node.kind === TEST;
}

/** The children of a node, or nothing for a test. */
function childrenOf(node: TreeNode): TreeNode[] {
  return isTest(node) ? [] : (node.children ?? []);
}

/** The node at a path, or null when the path names nothing -- a stale path after a delete. */
export function nodeAt(tree: TreeNode[], path: NodePath): TreeNode | null {
  let level: TreeNode[] = tree;
  let found: TreeNode | null = null;

  for (const index of path) {
    found = level[index] ?? null;

    if (!found) return null;

    level = childrenOf(found);
  }

  return found;
}

/**
 * The tree with one node replaced, rebuilt down the path it names.
 *
 * Rebuilt rather than mutated: the editor renders from this, and Svelte only sees a change it can
 * compare. A `null` replacement removes the node, which is the same walk and not worth a second
 * copy of it.
 */
function rebuild(tree: TreeNode[], path: NodePath, make: (node: TreeNode) => TreeNode | null): TreeNode[] {
  if (path.length === 0) return tree;

  const [index, ...rest] = path;
  const node = tree[index];

  if (!node) return tree;

  const next = [...tree];

  if (rest.length === 0) {
    const made = make(node);

    if (made === null) {
      next.splice(index, 1);
    } else {
      next[index] = made;
    }

    return next;
  }

  if (isTest(node)) return tree;

  next[index] = { ...node, children: rebuild(node.children ?? [], rest, make) };

  return next;
}

/** The tree with the node at `path` replaced. */
export function replaceAt(tree: TreeNode[], path: NodePath, node: TreeNode): TreeNode[] {
  return rebuild(tree, path, () => node);
}

/** The tree with the node at `path` gone, and with it everything under it. */
export function removeAt(tree: TreeNode[], path: NodePath): TreeNode[] {
  return rebuild(tree, path, () => null);
}

/**
 * The tree with `node` added as the last child of the group at `path`.
 *
 * An empty path adds at the top level, which is the step's implicit `all`. A test cannot hold
 * children, so a path naming one is left alone rather than silently growing a shape the validator
 * would refuse.
 */
export function appendTo(tree: TreeNode[], path: NodePath, node: TreeNode): TreeNode[] {
  if (path.length === 0) return [...tree, node];

  const target = nodeAt(tree, path);

  if (!target || isTest(target)) return tree;

  return rebuild(tree, path, (group) => ({
    ...(group as GroupNode),
    children: [...childrenOf(group), node],
  }));
}

/** The tree with the node at `path` wrapped in a new group of `kind`. */
export function wrapAt(tree: TreeNode[], path: NodePath, kind: 1 | 2 | 3): TreeNode[] {
  return rebuild(tree, path, (node) => ({ kind, children: [node] }));
}

/**
 * The tree with the group at `path` replaced by its children.
 *
 * The children are spliced into the parent rather than kept under a node nobody wants, which is
 * what "remove the group, keep the conditions" has to mean. A group under a `not` is the one case
 * that cannot be unwrapped into more than one node without changing what the step asks, so a `not`
 * holding several is left alone -- the validator refuses that shape anyway.
 */
export function unwrapAt(tree: TreeNode[], path: NodePath): TreeNode[] {
  const node = nodeAt(tree, path);

  if (!node || isTest(node)) return tree;

  const kept = childrenOf(node);
  const parentPath = path.slice(0, -1);
  const parent = parentPath.length === 0 ? null : nodeAt(tree, parentPath);

  // A `not` holds exactly one condition, so spilling a group of several into one would leave a
  // shape the validator refuses. Left alone rather than half done.
  if (parent && !isTest(parent) && parent.kind === NOT && kept.length !== 1) return tree;

  const siblings = parent ? childrenOf(parent) : tree;
  const spliced = [...siblings];

  spliced.splice(path[path.length - 1], 1, ...kept);

  if (!parent) return spliced;

  return rebuild(tree, parentPath, (group) => ({
    ...(group as GroupNode),
    children: spliced,
  }));
}

/** The tree with the node at `path` moved one place earlier or later among its siblings. */
export function shiftAt(tree: TreeNode[], path: NodePath, by: -1 | 1): TreeNode[] {
  const parentPath = path.slice(0, -1);
  const parent = parentPath.length === 0 ? null : nodeAt(tree, parentPath);
  const siblings = parent ? childrenOf(parent) : tree;
  const index = path[path.length - 1];
  const to = index + by;

  if (to < 0 || to >= siblings.length) return tree;

  const moved = [...siblings];

  moved.splice(to, 0, moved.splice(index, 1)[0]);

  if (!parent) return moved;

  return rebuild(tree, parentPath, (group) => ({
    ...(group as GroupNode),
    children: moved,
  }));
}

/** Every test in the tree, with its path, outermost first. */
export function tests(tree: TreeNode[]): { node: TestNode; path: NodePath }[] {
  const found: { node: TestNode; path: NodePath }[] = [];

  const walk = (level: TreeNode[], prefix: NodePath) => {
    level.forEach((node, index) => {
      const path = [...prefix, index];

      if (isTest(node)) {
        found.push({ node, path });
      } else {
        walk(childrenOf(node), path);
      }
    });
  };

  walk(tree, []);

  return found;
}

/** The tree with every test rewritten by `map`. The shape is untouched. */
export function mapTests(tree: TreeNode[], map: (node: TestNode) => TestNode): TreeNode[] {
  return tree.map((node) =>
    isTest(node) ? map(node) : { ...node, children: mapTests(childrenOf(node), map) },
  );
}

/**
 * A stable key for one condition of one step, for anything the editor remembers per condition --
 * the label of a picked garment, the name of a picked piece of furniture.
 */
export function pathKey(stepIndex: number, path: NodePath): string {
  return `${stepIndex}:${path.join('.')}`;
}

/** Reads `$N`, the only reference syntax the engine has. Returns the step index, or -1. */
export function referencedStep(value: unknown): number {
  if (typeof value !== 'string' || value.length < 2 || value[0] !== '$') return -1;

  const n = Number(value.slice(1));

  return Number.isInteger(n) && n >= 0 ? n : -1;
}

/** One node as the API spells it: the test is a nested object rather than fields on the node. */
export type NodeRow = {
  kind: number;
  test?: {
    factKey: string;
    op: number;
    value: string;
    left?: SideValue | null;
    right?: SideValue | null;
  } | null;
  children?: NodeRow[] | null;
};

/** A side, defaulted to what a condition meant before it could name one. */
function side(value: SideValue | null | undefined, kind: number, arg: string): SideValue {
  return value ? { kind: value.kind, arg: value.arg, step: value.step ?? -1 } : { kind, arg, step: -1 };
}

/**
 * The tree the API sent, as the editor holds it.
 *
 * The server sends both sides for every condition, including the ones stored before sides existed.
 * The defaults here are for a response from an older build, and they are the same two the server
 * would have sent: the fact on the left, the typed value on the right.
 */
export function fromRows(rows: NodeRow[] | null | undefined): TreeNode[] {
  return (rows ?? []).map((row) =>
    row.kind === TEST
      ? {
          kind: TEST,
          op: row.test?.op ?? 0,
          left: side(row.test?.left, FACT, row.test?.factKey ?? ''),
          right: side(row.test?.right, LITERAL, row.test?.value ?? ''),
        }
      : { kind: row.kind as 1 | 2 | 3, children: fromRows(row.children) },
  );
}

/** One side on its way out. Trimmed here so every path to the server trims alike. */
function trimmed(value: SideValue): SideValue {
  return { kind: value.kind, arg: value.arg.trim(), step: value.step };
}

/** And back, for the save. */
export function toRows(tree: TreeNode[]): NodeRow[] {
  return tree.map((node) =>
    isTest(node)
      ? {
          kind: TEST,
          test: {
            // Derived, never edited. The row has these columns and the server writes them; the
            // sides beside them are what it reads.
            factKey: node.left.kind === FACT ? node.left.arg : '',
            op: Number(node.op) || 0,
            value: node.right.kind === LITERAL ? node.right.arg.trim() : '',
            left: trimmed(node.left),
            right: trimmed(node.right),
          },
          children: null,
        }
      : { kind: node.kind, test: null, children: toRows(node.children ?? []) },
  );
}

/**
 * One fact an action records, as the server describes it: its label, its kind, and -- for a closed
 * fact -- the only values it accepts.
 *
 * Here rather than beside the component that renders it because two components render it, and a
 * second definition of this is how the two halves of an editor start disagreeing about one.
 */
export type FactOption = {
  key: string;
  labelKey?: string;
  fallbackLabel: string;
  kind: string;
  values?: { value: string; labelKey?: string; fallbackLabel: string }[];
};

/**
 * One side as a single string, so a dropdown can offer every choice in one list.
 *
 * Three fields in one option value rather than three controls per side: a condition is a sentence,
 * and asking an operator to set a kind, then a step, then a fact would make choosing "the room of
 * step 1" a three-step errand.
 */
export function encodeSide(value: SideValue): string {
  return `${value.kind}:${value.step}:${value.arg}`;
}

/** And back. Anything unparseable reads as an empty literal, which the server refuses by name. */
export function decodeSide(encoded: string): SideValue {
  const first = encoded.indexOf(':');
  const second = encoded.indexOf(':', first + 1);

  if (first < 0 || second < 0) return { kind: LITERAL, arg: '', step: -1 };

  const kind = Number(encoded.slice(0, first));
  const step = Number(encoded.slice(first + 1, second));

  return {
    kind: Number.isInteger(kind) ? kind : LITERAL,
    step: Number.isInteger(step) ? step : -1,
    // The argument may hold a colon of its own -- everything after the second one is it.
    arg: encoded.slice(second + 1),
  };
}

/** One step of a task's sequence, as the editor holds it. */
export type Step = { actionCode: string; tree?: TreeNode[] };

/**
 * One side after a reorder: pointing at the same step it did, or cleared.
 *
 * Cleared rather than silently repointed. The operator moved a block past the thing it depended
 * on, and quietly naming a different step would be worse than a field they can see is empty --
 * the condition would go on reading correctly and asking something else.
 */
function retarget(
  value: SideValue,
  newIndexOf: Map<number, number>,
  readerIndex: number,
): { value: SideValue; cleared: boolean } {
  if (value.kind !== STEP_FACT || value.step < 0) return { value, cleared: false };

  const moved = newIndexOf.get(value.step);

  return moved !== undefined && moved < readerIndex
    ? { value: { ...value, step: moved }, cleared: false }
    : { value: { kind: FACT, arg: value.arg, step: -1 }, cleared: true };
}

/**
 * Moves one step, rewriting every reference so each still points at the same step it did.
 *
 * Here rather than in a module of its own because it is the same walk: a `$N` three levels down
 * inside an `any` is exactly as invisible as one at the top, and a reorder that only visited the
 * top level would leave it naming whatever step now sits at that index.
 *
 * A reference that would end up pointing at its own step or at a later one is cleared to a literal
 * rather than silently retargeted: the operator moved a block past the thing it depended on, and
 * quietly repointing it at a different step would be worse than an empty field they can see.
 */
export function moveStep(
  steps: Step[],
  from: number,
  to: number,
): { steps: Step[]; clearedReferences: number } {
  if (from === to || from < 0 || to < 0 || from >= steps.length || to >= steps.length) {
    return { steps, clearedReferences: 0 };
  }

  const order = steps.map((_, i) => i);

  order.splice(to, 0, order.splice(from, 1)[0]);

  // oldIndex -> newIndex, so a reference can be rewritten to wherever its step ended up.
  const newIndexOf = new Map(order.map((oldIndex, newIndex) => [oldIndex, newIndex]));
  let clearedReferences = 0;

  const moved = order.map((oldIndex, newIndex) => {
    const step = steps[oldIndex];

    return {
      ...step,
      tree: mapTests(step.tree ?? [], (node) => {
        // Both sides, because a step can be named from either. A rewrite that visited only one
        // would leave the other pointing at whatever step now sits at that index -- a condition
        // that still reads correctly and asks a different question.
        const left = retarget(node.left, newIndexOf, newIndex);
        const right = retarget(node.right, newIndexOf, newIndex);

        clearedReferences += (left.cleared ? 1 : 0) + (right.cleared ? 1 : 0);

        return { ...node, left: left.value, right: right.value };
      }),
    };
  });

  return { steps: moved, clearedReferences };
}
