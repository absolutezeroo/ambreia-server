<script lang="ts">
  // The canvas: pan, zoom, nodes you place, and wires you draw between them.
  //
  // What a wire means lives in lib/graph/model.js and is checked there. This file is only the
  // surface: where things are, what the cursor is doing, and which port it is over. It decides
  // nothing about validity -- it asks canWire and refuses to draw what the engine would refuse.
  import ActionNode from './ActionNode.svelte';
  import ConditionNode from './ConditionNode.svelte';
  import GraphWires from './GraphWires.svelte';
  import NodePalette from './NodePalette.svelte';
  import { canWire, connect, disconnect, readerOf, toGraph } from '../../lib/graph/model';
  import { moveFilter, moveStep } from '../../lib/sequence/steps';
  import type { ActionNode as ActionNodeShape, Fact, Layout, Step, Wire } from '../../lib/graph/model';
  import { t } from '../../lib/i18n';
  import { readLayout, writeLayout } from '../../lib/graph/graphLayout';

  type Props = {
    steps: Step[];
    actions: { name: string; [key: string]: unknown }[];
    canManage: boolean;
    factsFor: (action: string) => Fact[];
    operatorsFor: (action: string, factKey: string) => { value: number | string }[];
    defaultFilterValue: (action: string, factKey: string) => string;
    /** Which picker a fact opens, or null when it is typed by hand. */
    pickerFor: (meta: Fact | null) => string | null;
    pickedLabels: Record<string, unknown>;
    /** Identifies whose arrangement this is. Absent for a task with no id yet: nothing is kept. */
    layoutKey?: string;
    onchange: (steps: Step[]) => void;
    onpick: (stepIndex: number, filterIndex: number, kind: string) => void;
  };

  /** A point in canvas coordinates, which is what everything here works in. */
  type Point = { x: number; y: number };

  /** Where a wire is anchored and where the pointer has dragged it to. */
  type PullEnds = { anchor: Point; to: Point };

  /** Started at an action's fact port, so it knows which fact it carries. */
  type OutputPull = {
    from: 'output';
    node: number;
    fact: string;
    label: string;
    anchorPort: string;
  };

  /** Started at a condition, which already knows its own fact. */
  type NodePull = { from: 'value' | 'applies'; node: any; anchorPort: string };

  /** What startPull is handed, before the ends are measured. */
  type PullSeed = OutputPull | NodePull;

  /** The wire currently held by the pointer. */
  type Pull = PullSeed & PullEnds;

  let {
    steps,
    actions,
    canManage,
    factsFor,
    operatorsFor,
    defaultFilterValue,
    pickerFor,
    pickedLabels,
    layoutKey,
    onchange,
    onpick,
  }: Props = $props();

  // Where each node sits. Still not on the task -- nobody inherits anybody's arrangement, and none
  // of it reaches the database -- but no longer thrown away on close either: see lib/graph/
  // graphLayout, which keeps it per browser, per operator, per task. Dropping it meant the
  // automatic layout came back every visit, so nothing was ever worth tidying.
  let layout = $state<Layout>(readLayout(layoutKey));
  let pan = $state<Point>({ x: 0, y: 0 });
  let zoom = $state(1);
  let selected = $state<string | null>(null);
  let pulling = $state<Pull | null>(null);
  let notice = $state('');
  let ports = $state(0); // bumped to re-measure after anything that moves a port

  let canvasEl: HTMLElement | undefined;

  // Written on every arrangement rather than on close: the editor is a full-screen layer that can
  // be dismissed with Escape or a reload, and an arrangement kept only until a tidy exit is the
  // one that gets lost precisely when somebody was in the middle of tidying.
  $effect(() => {
    writeLayout(layoutKey, layout);
  });

  let graph = $derived(toGraph(steps, factsFor, layout));
  let actionNodes = $derived(
    graph.nodes.filter((n): n is ActionNodeShape => n.type === 'action'),
  );
  let conditionNodes = $derived(graph.nodes.filter((n) => n.type === 'condition') as any[]);

  /** The fact a condition currently tests, read live rather than from the render snapshot. */
  /** The fact the wire in hand carries, or null when it is not that kind of wire. */
  let pulledFact = $derived(pulling?.from === 'output' ? pulling.fact : null);

  const metaOf = (node: any): Fact | null =>
    factsFor(node.action).find((f) => f.key === node.filter.factKey) ?? null;

  // Wires are measured off the DOM, and on the first render there is nothing to measure: the SVG is
  // drawn before the nodes it connects exist, so every wire came out null and the canvas opened
  // wireless until something moved a node and forced a second pass. This is that second pass, asked
  // for on purpose. It reads the node ids and not `ports`, so writing `ports` here cannot loop.
  $effect(() => {
    const nodes = graph.nodes.map((n) => n.id).join('|');

    if (!canvasEl || !nodes) return;

    const frame = requestAnimationFrame(() => (ports += 1));

    return () => cancelAnimationFrame(frame);
  });

  /** Where a port ended up, in canvas coordinates. Measured, because node height varies. */
  function portAt(id: string): Point | null {
    void ports;

    if (!canvasEl) return null;

    const el = canvasEl.querySelector(`[data-port="${id}"]`);

    if (!el) return null;

    const box = el.getBoundingClientRect();
    const frame = canvasEl.getBoundingClientRect();

    return {
      x: (box.left + box.width / 2 - frame.left - pan.x) / zoom,
      y: (box.top + box.height / 2 - frame.top - pan.y) / zoom,
    };
  }

  function geometry(wire: Wire) {
    const ends = {
      flow: [`${wire.from}:flow-out`, `${wire.to}:flow-in`],
      applies: [`${wire.from}:applies`, `${wire.to}:applies`],
      data: [`${wire.from}:out:${wire.fact}`, `${wire.to}:value`],
    }[wire.kind] as [string, string];

    const from = portAt(ends[0]);
    const to = portAt(ends[1]);

    if (!from || !to) return null;

    // Both an `applies` and a `data` wire carry one fact, and the action at one of its ends is the
    // one that names its kind -- so a room wire reads as a room whichever way it was drawn.
    const owner = wire.kind === 'data' ? wire.from : wire.to;
    const kind = actionNodes.find((n) => n.id === owner)?.outputs.find((p) => p.key === wire.fact)?.kind;

    return { from, to, kind };
  }

  function toCanvas(event: { clientX: number; clientY: number }): Point {
    const frame = canvasEl!.getBoundingClientRect();

    return {
      x: (event.clientX - frame.left - pan.x) / zoom,
      y: (event.clientY - frame.top - pan.y) / zoom,
    };
  }

  /** Follows the pointer until it is released, then cleans up after itself. */
  function track(onmove: (e: PointerEvent) => void, onup?: (e: PointerEvent) => void) {
    function move(e: PointerEvent) {
      onmove(e);
    }

    function up(e: PointerEvent) {
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', up);
      onup?.(e);
    }

    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up);
  }

  function startMove(node: any, event: PointerEvent) {
    event.preventDefault();
    selected = node.id;

    const origin = toCanvas(event);
    const start = { x: node.x, y: node.y };

    track((e) => {
      const at = toCanvas(e);

      layout = {
        ...layout,
        [node.id]: { x: start.x + (at.x - origin.x), y: start.y + (at.y - origin.y) },
      };
      ports += 1;
    });
  }

  /**
   * A wire can be started from either end.
   *
   * People reach for the empty end first -- "this condition needs a value, where does it come
   * from" -- so accepting only the other direction made the one move nobody tries the only one
   * that worked.
   */
  function startPull(shape: PullSeed) {
    const anchor = portAt(shape.anchorPort);

    if (!anchor) return;

    pulling = { ...shape, anchor, to: anchor };
    track(
      (e) => (pulling = { ...pulling!, to: toCanvas(e) }),
      () => {
        // Still held at release means it landed on nothing. For a fact port that is not a mistake
        // -- it is the normal way to use one -- so it makes the condition rather than doing nothing.
        if (pulling?.from === 'output') dropOnCanvas(pulling);

        pulling = null;
      }
    );
  }

  function startFromOutput(node: ActionNodeShape, factKey: string, event: PointerEvent) {
    event.preventDefault();
    event.stopPropagation();
    startPull({
      from: 'output',
      node: node.index,
      fact: factKey,
      label: node.outputs.find((p) => p.key === factKey)?.label ?? factKey,
      anchorPort: `${node.id}:out:${factKey}`,
    });
  }

  function startFromValue(node: any, event: PointerEvent) {
    event.preventDefault();
    event.stopPropagation();
    startPull({ from: 'value', node, anchorPort: `${node.id}:value` });
  }

  function startFromApplies(node: any, event: PointerEvent) {
    event.preventDefault();
    event.stopPropagation();
    startPull({ from: 'applies', node, anchorPort: `${node.id}:applies` });
  }

  /** A wire from an action's fact port landed on a condition's value socket. */
  function dropOnValue(node: any) {
    if (pulling?.from !== 'output') return;

    if (canWire(pulling.node, node.index, pulling.fact, node.filter)) {
      onchange(connect(steps, pulling.node, node.index, node.filterIndex));
      ports += 1;
    }

    pulling = null;
  }

  /** The same wire pulled the other way: from the socket, dropped on an action's fact port. */
  function dropOnOutput(actionNode: ActionNodeShape, factKey: string) {
    if (pulling?.from !== 'value') return;

    const condition = pulling.node;

    if (canWire(actionNode.index, condition.index, factKey, condition.filter)) {
      onchange(connect(steps, actionNode.index, condition.index, condition.filterIndex));
      ports += 1;
    }

    pulling = null;
  }

  /** The node a fact port can reach, if any. The rule itself lives in the model. */
  function readerNode(fromIndex: number, factKey: string) {
    const index = readerOf(steps, fromIndex, factKey, factsFor);

    return index < 0 ? null : actionNodes.find((n) => n.index === index);
  }

  /** Adds a condition to an action, optionally already reading an earlier one, and selects it. */
  function addConditionOn(target: any, factKey: string, value: string, at?: Point) {
    const filterIndex = (steps[target.index].filters ?? []).length;
    const id = `c:${target.index}:${filterIndex}`;

    // Dropped where the pointer let go, so the node appears under the cursor rather than in the row
    // the default layout would have stacked it into.
    if (at) layout = { ...layout, [id]: at };

    onchange(
      steps.map((step, i) =>
        i === target.index
          ? {
              ...step,
              filters: [
                ...(step.filters ?? []),
                { factKey, op: operatorsFor(target.action, factKey)[0]?.value ?? 0, value },
              ],
            }
          : step
      )
    );
    selected = id;
    notice = '';
    ports += 1;
  }

  /** A fact port released over open canvas: write the filter it was reaching for. */
  function dropOnCanvas(wire: OutputPull & PullEnds) {
    // A click that never moved is a click, not a drag, and must not leave a node behind.
    if (Math.hypot(wire.to.x - wire.anchor.x, wire.to.y - wire.anchor.y) < 12) return;

    const target = readerNode(wire.node, wire.fact);

    if (!target) {
      notice = $t('rewardTracks.noLaterActionReports', { fact: wire.label });

      return;
    }

    addConditionOn(target, wire.fact, `$${wire.node}`, wire.to);
  }

  /**
   * A condition re-plugged onto another action.
   *
   * This is the move the node form exists for: a test written under one action becomes a test on
   * another by dragging one wire. An action that never reports that fact could never match it, so
   * that drop is refused out loud rather than silently accepted and dead.
   */
  function dropOnApplies(actionNode: ActionNodeShape) {
    // A fact port dropped here names its reader outright instead of taking the first one.
    if (pulling?.from === 'output') {
      const wire = pulling;
      const emits = factsFor(actionNode.action).some((f) => f.key === wire.fact);

      if (!emits || actionNode.index <= wire.node) {
        notice = $t('rewardTracks.actionDoesNotEmit', { action: actionNode.action });
      } else {
        addConditionOn(actionNode, wire.fact, `$${wire.node}`);
      }

      pulling = null;

      return;
    }

    if (pulling?.from !== 'applies') return;

    const condition = pulling.node;
    const emits = factsFor(actionNode.action).some((f) => f.key === condition.filter.factKey);

    if (!emits) {
      notice = $t('rewardTracks.actionDoesNotEmit', { action: actionNode.action });
      pulling = null;

      return;
    }

    const result = moveFilter(steps, condition.index, condition.filterIndex, actionNode.index);

    onchange(result.steps);
    notice = result.clearedReferences
      ? $t('rewardTracks.referencesCleared', { count: result.clearedReferences })
      : '';
    pulling = null;
    ports += 1;
  }

  /** Whether an action's fact port could take the wire currently in hand. */
  function candidateOutput(actionNode: ActionNodeShape, factKey: string) {
    return (
      pulling?.from === 'value' &&
      canWire(actionNode.index, pulling.node.index, factKey, pulling.node.filter)
    );
  }

  function startPan(event: PointerEvent) {
    // Middle button, or the background with the left one: the two ways every node editor pans.
    const target = event.target as HTMLElement;

    if (event.button !== 1 && target !== canvasEl && !target.classList.contains('grid'))
      return;

    event.preventDefault();
    selected = null;

    const origin = { x: event.clientX - pan.x, y: event.clientY - pan.y };

    track((e) => (pan = { x: e.clientX - origin.x, y: e.clientY - origin.y }));
  }

  function onwheel(event: WheelEvent) {
    event.preventDefault();

    const next = Math.min(2, Math.max(0.4, zoom * (event.deltaY < 0 ? 1.1 : 1 / 1.1)));
    const frame = canvasEl!.getBoundingClientRect();
    const cx = event.clientX - frame.left;
    const cy = event.clientY - frame.top;

    // Zoom towards the cursor rather than the origin, so the thing being looked at stays put.
    pan = { x: cx - ((cx - pan.x) / zoom) * next, y: cy - ((cy - pan.y) / zoom) * next };
    zoom = next;
    ports += 1;
  }

  function addAction(name: string) {
    onchange([...steps, { actionCode: name, filters: [] }]);
    notice = '';
    ports += 1;
  }

  /**
   * Drops a condition on the canvas, attached to the selected action.
   *
   * A condition tests one action's signal, so a floating one is not a thing the model can express.
   * Rather than inventing an unattached state, it lands on whichever action is selected -- or the
   * last one -- and is then moved by dragging its wire, which is the gesture it exists for.
   */
  function addCondition() {
    const target = actionNodes.find((n) => n.id === selected) ?? actionNodes.at(-1);

    if (!target) {
      notice = $t('rewardTracks.conditionNeedsAction');

      return;
    }

    const first = factsFor(target.action)[0];

    if (!first) {
      notice = $t('rewardTracks.actionHasNoFacts');

      return;
    }

    addConditionOn(target, first.key, defaultFilterValue(target.action, first.key));
  }

  /**
   * Moves an action earlier or later in the sequence.
   *
   * Where a node sits on the canvas is decoration; the ORDER is the array, and it is the only thing
   * the engine reads. Without this the number on a node could not be changed at all from here --
   * you could arrange the picture any way you liked and never touch the sequence it pictures.
   *
   * moveStep rewrites every `$N` for the new positions and clears the ones the move invalidates, so
   * dragging an action in front of the thing it depended on says so instead of failing on save.
   */
  function reorder(node: any, delta: number) {
    const { steps: moved, clearedReferences } = moveStep(steps, node.index, node.index + delta);

    if (moved === steps) return;

    onchange(moved);
    // The two nodes swapped places in the array, so their stored positions swap with them --
    // otherwise the picture would stop matching the numbers on it.
    layout = {
      ...layout,
      [`a:${node.index}`]: layout[`a:${node.index + delta}`],
      [`a:${node.index + delta}`]: layout[`a:${node.index}`],
    };
    selected = `a:${node.index + delta}`;
    notice = clearedReferences
      ? $t('rewardTracks.referencesCleared', { count: clearedReferences })
      : '';
    ports += 1;
  }

  /**
   * Throws the layout away.
   *
   * Positions are a convenience, never a meaning, so losing them costs nothing -- and once a graph
   * has been dragged around for a while, the fastest way to read it again is to have it laid back
   * out in the order it actually runs in.
   */
  function tidy() {
    layout = {};
    notice = '';
    ports += 1;
  }

  function removeAction(node: any) {
    // Removing a step renumbers everything after it, and a `$N` pointing past the gap would then
    // name the wrong action. moveStep already keeps references honest, so the removal is done by
    // moving the node to the end and dropping it.
    const { steps: shuffled } = moveStep(steps, node.index, steps.length - 1);

    onchange(shuffled.slice(0, -1));
    selected = null;
    ports += 1;
  }

  function removeCondition(node: any) {
    onchange(
      steps.map((step, i) =>
        i === node.index
          ? {
              ...step,
              filters: (step.filters ?? []).filter((_, f) => f !== node.filterIndex),
            }
          : step
      )
    );
    selected = null;
    ports += 1;
  }

  /**
   * A condition changed.
   *
   * Changing the fact invalidates both of the other two fields: an operator the new kind does not
   * allow is refused by the server, and a `$N` left behind would now read a fact the named step
   * never recorded. So both are reset with the fact rather than left to be discovered on save.
   */
  function conditionChanged(node: any) {
    if (node.filter.factKey !== node.fact) {
      node.filter.op = operatorsFor(node.action, node.filter.factKey)[0]?.value ?? 0;
      node.filter.value = defaultFilterValue(node.action, node.filter.factKey);
    }

    onchange(steps);
    ports += 1;
  }
</script>

<div class="graph">
  <NodePalette {actions} {canManage} onadd={addAction} onaddcondition={addCondition} />

  <div class="canvas" bind:this={canvasEl} onpointerdown={startPan} {onwheel} role="presentation">
    <div class="grid" style:background-position="{pan.x}px {pan.y}px" style:--cell="{24 * zoom}px"></div>

    <div class="viewport" style:transform="translate({pan.x}px, {pan.y}px) scale({zoom})">
      <GraphWires
        wires={graph.wires}
        {geometry}
        dragging={pulling && { from: pulling.anchor, to: pulling.to }}
      />

      {#each actionNodes as node (node.id)}
        <ActionNode
          {node}
          {actions}
          {canManage}
          {pulling}
          selected={selected === node.id}
          appliesLit={pulling?.from === 'applies' ||
            (pulling?.from === 'output' &&
              pulling.node < node.index &&
              factsFor(node.action).some((f) => f.key === pulledFact))}
          candidateFor={(factKey: string) => candidateOutput(node, factKey)}
          usableFor={(factKey: string) => readerOf(steps, node.index, factKey, factsFor) >= 0}
          onmovestart={(e: PointerEvent) => startMove(node, e)}
          onchange={() => {
            onchange(steps);
            ports += 1;
          }}
          onremove={() => removeAction(node)}
          onreorder={(delta: number) => reorder(node, delta)}
          last={node.index === actionNodes.length - 1}
          onportdown={(factKey: string, e: PointerEvent) => startFromOutput(node, factKey, e)}
          onportup={(factKey: string) => dropOnOutput(node, factKey)}
          onappliesup={() => dropOnApplies(node)}
        />
      {/each}

      {#each conditionNodes as node (node.id)}
        <ConditionNode
          {node}
          {canManage}
          facts={factsFor(node.action)}
          operators={operatorsFor(node.action, node.filter.factKey)}
          selected={selected === node.id}
          picked={pickedLabels[`${node.index}:${node.filterIndex}`]}
          picker={pickerFor(metaOf(node))}
          candidate={pulling?.from === 'output' &&
            canWire(pulling.node, node.index, pulling.fact, node.filter)}
          blocked={pulling?.from === 'output' &&
            !canWire(pulling.node, node.index, pulling.fact, node.filter)}
          onmovestart={(e: PointerEvent) => startMove(node, e)}
          onchange={() => conditionChanged(node)}
          onremove={() => removeCondition(node)}
          onpick={() => onpick(node.index, node.filterIndex, pickerFor(metaOf(node)) ?? '')}
          onvaluedown={(e: PointerEvent) => startFromValue(node, e)}
          onvalueup={() => dropOnValue(node)}
          onappliesdown={(e: PointerEvent) => startFromApplies(node, e)}
          oncut={() => {
            onchange(disconnect(steps, node.index, node.filterIndex));
            ports += 1;
          }}
        />
      {/each}
    </div>

    {#if notice}
      <p class="notice" role="status">{notice}</p>
    {/if}

    <div class="hints">
      <span>{$t('rewardTracks.graphHintPan')}</span>
      <span>{$t('rewardTracks.graphHintWire')}</span>
      <button type="button" class="chip" onclick={tidy}>{$t('rewardTracks.tidyGraph')}</button>
      <span class="chip">{Math.round(zoom * 100)}%</span>
    </div>
  </div>
</div>

<style>
  .graph {
    display: flex;
    height: 100%;
    min-height: 0;
  }

  .canvas {
    position: relative;
    flex: 1 1 auto;
    overflow: hidden;
    background: var(--page);
    cursor: grab;
  }

  .canvas:active {
    cursor: grabbing;
  }

  /* The grid is the thing that tells you the canvas moved. It scrolls with the pan and scales with
     the zoom, which is the whole reason it is a background rather than an element. */
  .grid {
    position: absolute;
    inset: 0;
    background-image:
      linear-gradient(to right, var(--line) 1px, transparent 1px),
      linear-gradient(to bottom, var(--line) 1px, transparent 1px);
    opacity: 0.55;
    background-size: var(--cell) var(--cell);
  }

  .viewport {
    position: absolute;
    inset: 0;
    transform-origin: 0 0;
  }

  .notice {
    position: absolute;
    left: 12px;
    bottom: 10px;
    margin: 0;
    padding: 5px 10px;
    border: 2px solid var(--line-strong);
    border-radius: 5px;
    background: var(--surface);
    box-shadow: var(--panel-shadow);
    color: var(--muted);
    font-size: 0.72rem;
  }

  .hints {
    position: absolute;
    right: 12px;
    bottom: 10px;
    display: flex;
    gap: 14px;
    align-items: center;
    color: var(--muted);
    font-size: 0.7rem;
    pointer-events: none;
  }

  .chip {
    padding: 2px 7px;
    border: 2px solid var(--line-strong);
    border-radius: 5px;
    background: var(--surface);
    box-shadow: var(--panel-shadow);
    color: var(--muted);
    font: inherit;
  }

  /* The hint row ignores the pointer so it never eats a drag; the one thing in it you can press
     has to opt back in. */
  button.chip {
    pointer-events: auto;
    cursor: pointer;
  }

  button.chip:hover {
    border-color: var(--gold);
    color: var(--ink);
  }
</style>
