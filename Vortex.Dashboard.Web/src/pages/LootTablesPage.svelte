<script lang="ts">
  import { onMount } from 'svelte';
  import { Dices, Link2 } from '@lucide/svelte';
  import OpResult from '../components/OpResult.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import EmptyState from '../components/EmptyState.svelte';
  import AssetImage from '../components/AssetImage.svelte';
  import PickerModal from '../components/PickerModal.svelte';
  import AccessDeniedNotice from '../components/AccessDeniedNotice.svelte';
  import ConfirmReasonModal from '../components/ConfirmReasonModal.svelte';
  import { apiGet } from '../lib/api';
  import { createWriteOps } from '../lib/writeOps';
  import Tabs from '../components/Tabs.svelte';
  import Pagination from '../components/Pagination.svelte';
  import { formatNumber } from '../lib/format';
  import { isPermissionDeniedError, hasDashboardCapability } from '../lib/permissions';
  import { CAPABILITIES } from '../lib/dashboardPermissions';
  import { identity } from '../lib/session';
  import { t } from '../lib/i18n';
  import type {
    LootTableBindingRow,
    LootTableContent,
    LootTableEntryRow,
    LootTableRow,
    LootTableStats,
    LootTableWeightTotal,
  } from '../lib/apiTypes';
  
  /** The three creation forms. Number inputs hand back strings while being typed. */
  type PoolForm = {
    code: string;
    name: string;
    variants: string;
    notes: string;
    enabled: boolean;
  };

  type BindingForm = {
    furnitureDefinitionId: number | string;
    poolCode: string;
    hitsRequired: number | string;
    enabled: boolean;
  };

  type EntryForm = {
    variant: string;
    productType: string;
    furnitureDefinitionId: number | string;
    extraParam: string;
    weight: number | string;
    enabled: boolean;
  };

  // Only floor/wall entries name a furniture definition; effect and club prizes carry their target
  // in extraParam instead, so the form swaps which field it asks for.
  const FURNITURE_TYPES = ['Floor', 'Wall'];

  let pools = $state<LootTableRow[]>([]);
  let entries = $state<LootTableEntryRow[]>([]);
  let totals: LootTableWeightTotal[] = [];
  let bindings = $state<LootTableBindingRow[]>([]);

  let tab = $state('pools');

  // The bindings table is the one list here that grows with the hotel rather than with the operator:
  // every crackable furni is a row, which is a four-figure scroll on a real database. Filter first --
  // an operator arrives knowing which furni they are looking for -- and page what is left. Both are
  // client-side because the whole set already arrived with the page.
  const BINDINGS_PAGE_SIZE = 25;
  let bindingQuery = $state('');
  let bindingPool = $state('');
  let bindingPage = $state(1);

  let filteredBindings = $derived(bindings.filter((b) => {
    if (bindingPool && b.pool !== bindingPool) return false;
    if (!bindingQuery.trim()) return true;

    const needle = bindingQuery.trim().toLowerCase();

    return (
      (b.furnitureName ?? '').toLowerCase().includes(needle) ||
      (b.furnitureLogic ?? '').toLowerCase().includes(needle) ||
      String(b.furnitureDefinitionId).includes(needle)
    );
  }));

  let bindingPageCount = $derived(Math.max(1, Math.ceil(filteredBindings.length / BINDINGS_PAGE_SIZE)));
  // Narrowing the filter can strand the operator past the last page; walk them back to it.
  $effect(() => {
    if (bindingPage > bindingPageCount) bindingPage = bindingPageCount;
  });
  let pagedBindings = $derived(filteredBindings.slice(
    (bindingPage - 1) * BINDINGS_PAGE_SIZE,
    bindingPage * BINDINGS_PAGE_SIZE
  ));
  let bindingPools = $derived([...new Set(bindings.map((b) => b.pool))].sort());

  let productTypes = $state<string[]>([]);
  let stats = $state<LootTableStats | null>(null);
  let statsDays = $state(7);

  let loading = $state(false);
  let denied = $state(false);
  let error = $state('');

  let selectedPoolId = $state<number | null>(null);
  let newPool = $state(emptyPool());
  let newEntry = $state(emptyEntry());
  let newBinding = $state(emptyBinding());

  // Typing a definition id by hand means looking it up somewhere else first; the furniture
  // picker is what every other page uses for this.
  let picking = $state<'entry' | 'binding' | null>(null);

  // Every write is staged through the shared reason modal; createWriteOps posts it, remembers the
  // reason (which the hand-rolled version here used to drop) and refreshes both reads.
  const ops = createWriteOps(async () => {
    newPool = emptyPool();
    newEntry = emptyEntry();
    newBinding = emptyBinding();
    await load();
    await loadStats();
  });

  function emptyPool(): PoolForm {
    return { code: '', name: '', variants: '', notes: '', enabled: true };
  }

  function emptyBinding(): BindingForm {
    return { furnitureDefinitionId: '', poolCode: '', hitsRequired: 1, enabled: true };
  }

  function emptyEntry(): EntryForm {
    return {
      variant: '',
      productType: 'Floor',
      furnitureDefinitionId: '',
      extraParam: '',
      weight: 10,
      enabled: true,
    };
  }

  let canManage = $derived(hasDashboardCapability($identity, CAPABILITIES.opsLootTablesManage));
  let selectedPool = $derived(pools.find((p) => p.id === selectedPoolId) ?? pools[0] ?? null);
  let poolEntries = $derived(selectedPool ? entries.filter((e) => e.poolId === selectedPool.id) : []);
  let poolStats = $derived(stats?.pools?.find((p) => p.pool === selectedPool?.code) ?? null);

  // The share a draw really sees: an entry competes with the pool entries that can be drawn beside
  // it, which for a variantless one is every variant of the pool. The server groups them the same
  // way the picker does, so this only reads the number back.
  function expectedShare(entry: LootTableEntryRow) {
    if (!entry.enabled) return null;
    const group = totals.find((g) => g.poolId === entry.poolId && g.variant === entry.variant);
    if (!group || group.totalWeight <= 0) return null;
    return Math.round((entry.weight / group.totalWeight) * 1000) / 10;
  }

  function drawsOf(entry: LootTableEntryRow) {
    return poolStats?.entries.find((e) => e.entryId === entry.id)?.draws ?? 0;
  }

  function actualShare(entry: LootTableEntryRow) {
    if (!poolStats || poolStats.draws <= 0) return null;
    return Math.round((drawsOf(entry) / poolStats.draws) * 1000) / 10;
  }

  function entryTarget(entry: LootTableEntryRow) {
    if (FURNITURE_TYPES.includes(entry.productType)) {
      return entry.furnitureName ?? `#${entry.furnitureDefinitionId}`;
    }
    return entry.extraParam || '—';
  }

  function percent(value: number | null) {
    return value === null ? '—' : `${value}%`;
  }

  async function load() {
    loading = true;
    error = '';
    try {
      const data = await apiGet<LootTableContent>('/api/v1/loot-tables');
      pools = data.pools?.items ?? [];
      entries = data.entries?.items ?? [];
      totals = data.totals ?? [];
      bindings = data.bindings?.items ?? [];
      productTypes = data.productTypes ?? [];
      if (selectedPoolId === null && pools.length > 0) selectedPoolId = pools[0].id;
      denied = false;
    } catch (e) {
      if (isPermissionDeniedError(e)) denied = true;
      else error = (e as Error)?.message ?? String(e);
    } finally {
      loading = false;
    }
  }

  async function loadStats() {
    try {
      stats = await apiGet<LootTableStats>(`/api/v1/loot-tables/stats?days=${statsDays}`);
    } catch {
      stats = null;
    }
  }

  const stage = (
    title: string,
    summary: string,
    endpoint: string,
    body: Record<string, unknown>,
    danger = false,
  ) =>
    ops.ask(endpoint, body, title, summary, { danger });

  onMount(async () => {
    await load();
    await loadStats();
  });
</script>

<section class="panel">
  <PageHeader title={$t('lootTables.title')} description={$t('lootTables.description')}>
    {#snippet actions()}
      <button type="button" class="warning" onclick={load} disabled={loading}>
        {$t('common.refresh')}
      </button>
      {#if canManage}
        <button
          type="button"
          class="ghost-button"
          onclick={() =>
            stage(
              $t('lootTables.reload'),
              $t('lootTables.reloaded'),
              '/api/v1/operations/loot-tables/reload',
              {}
            )}
        >
          {$t('lootTables.reload')}
        </button>
      {/if}
    {/snippet}
  </PageHeader>
</section>

{#if denied}
  <AccessDeniedNotice message={$t('lootTables.accessDenied')} />
{:else}
  {#if error}
    <EmptyState kind="error" message={error} />
  {/if}
  <OpResult result={$ops.result} />

  <Tabs
    bind:active={tab}
    storageKey="lootTables"
    tabs={[
      { id: 'pools', label: $t('lootTables.tabPools'), icon: Dices, count: pools.length },
      { id: 'bindings', label: $t('lootTables.tabBindings'), icon: Link2, count: bindings.length },
    ]}
  />

  {#if tab === 'pools'}
  <section class="panel">
    <div class="panel-head">
      <h2>{$t('lootTables.poolsHeading')}</h2>
    </div>
    <p class="muted">{$t('lootTables.poolsHint')} {$t('lootTables.variantsHint')}</p>

    {#if loading}
      <EmptyState kind="loading" />
    {:else if pools.length === 0}
      <EmptyState message={$t('lootTables.noPools')} />
    {:else}
      <!-- A grid of equal tiles rather than a wrapped row of buttons. As a row, each chip was as
           wide as its own name and the ones carrying a BUILT-IN badge were wider still, so a list
           of near-identical crackable pools read as ragged noise you had to scan letter by letter.
           Equal columns put every name at the same left edge, which is what makes them comparable. -->
      <div class="pool-grid">
        {#each pools as pool (pool.id)}
          <button
            type="button"
            class="pool-tile"
            aria-pressed={selectedPool?.id === pool.id}
            onclick={() => (selectedPoolId = pool.id)}
          >
            <span class="pool-tile-name">{pool.name}</span>
            <span class="pool-tile-badges">
              {#if !pool.enabled}
                <span class="status-badge status-badge--warn">{$t('lootTables.enabled')}</span>
              {/if}
            </span>
          </button>
        {/each}
      </div>
    {/if}

    {#if canManage}
      <div class="op-grid">
        <div class="op-field">
          <label for="pool-code">{$t('lootTables.code')}</label>
          <input autocomplete="off" spellcheck="false" id="pool-code" bind:value={newPool.code} />
        </div>
        <div class="op-field">
          <label for="pool-name">{$t('lootTables.name')}</label>
          <input autocomplete="off" spellcheck="false" id="pool-name" bind:value={newPool.name} />
        </div>
        <div class="op-field">
          <label for="pool-variants">{$t('lootTables.variants')}</label>
          <input autocomplete="off" spellcheck="false" id="pool-variants" bind:value={newPool.variants} />
        </div>
        <div class="op-field">
          <label for="pool-notes">{$t('lootTables.notes')}</label>
          <input autocomplete="off" spellcheck="false" id="pool-notes" bind:value={newPool.notes} />
        </div>
      </div>
      <div class="op-actions">
        <button
     type="button"
     class="btn btn-primary btn-sm success"
     disabled={!newPool.code.trim() || !newPool.name.trim()}
     onclick={() =>
      stage($t('lootTables.newPool'), newPool.code, '/api/v1/operations/loot-tables', {
       ...newPool,
      })}>
          {$t('lootTables.newPool')}
        </button>
      </div>
    {/if}
  </section>

  {#if selectedPool}
    <section class="panel">
      <div class="panel-head">
        <h2>{$t('lootTables.entriesHeading')} — {selectedPool.name}</h2>
        <div class="toolbar">
          <select bind:value={statsDays} onchange={loadStats}>
            {#each [1, 7, 30, 90] as days}
              <option value={days}>{days}</option>
            {/each}
          </select>
        </div>
      </div>
      <p class="muted">{$t('lootTables.entriesHint')}</p>

      <div class="stats">
        <div class="stat">
          <span class="stat-label">{$t('lootTables.entriesHeading')}</span>
          <strong>{formatNumber(poolEntries.length)}</strong>
        </div>
        <div class="stat">
          <span class="stat-label">{$t('lootTables.draws')}</span>
          <strong>{formatNumber(poolStats?.draws ?? 0)}</strong>
        </div>
      </div>

      {#if poolEntries.length === 0}
        <EmptyState message={$t('lootTables.noEntries')} />
      {:else}
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{$t('lootTables.productType')}</th>
                <th>{$t('lootTables.variant')}</th>
                <th>{$t('lootTables.weight')}</th>
                <th>{$t('lootTables.expected')}</th>
                <th>{$t('lootTables.actual')}</th>
                <th>{$t('lootTables.draws')}</th>
                {#if canManage}<th>{$t('common.actions')}</th>{/if}
              </tr>
            </thead>
            <tbody>
              {#each poolEntries as entry (entry.id)}
                <tr>
                  <td>
                    <span class="cell-row">
                      <AssetImage
                        src={entry.furnitureIconUrl}
                        alt={entry.furnitureName ?? ''}
                        size={32}
                      />
                      <span class="cell-text">
                        <strong class="truncate">{entryTarget(entry)}</strong>
                        <span class="muted">{entry.productType}</span>
                      </span>
                    </span>
                    {#if !entry.enabled}
                      <span class="status-badge status-badge--warn"
                        >{$t('lootTables.enabled')}</span
                      >
                    {/if}
                  </td>
                  <td>{entry.variant || '—'}</td>
                  <td>{formatNumber(entry.weight)}</td>
                  <td>{percent(expectedShare(entry))}</td>
                  <td>{percent(actualShare(entry))}</td>
                  <td>{formatNumber(drawsOf(entry))}</td>
                  {#if canManage}
                    <td>
                      <button
                        type="button"
                        class="icon-btn"
                        title={$t('lootTables.remove')}
                        onclick={() =>
                          stage(
                            $t('lootTables.remove'),
                            entryTarget(entry),
                            '/api/v1/operations/loot-tables/entries/delete',
                            { entryId: entry.id },
                            true
                          )}
                      >
                        {$t('common.delete')}</button>
                    </td>
                  {/if}
                </tr>
              {/each}
            </tbody>
          </table>
        </div>
      {/if}

      {#if canManage}
        <div class="op-grid">
          <div class="op-field">
            <label for="entry-type">{$t('lootTables.productType')}</label>
            <select id="entry-type" bind:value={newEntry.productType}>
              {#each productTypes as type}
                <option value={type}>{type}</option>
              {/each}
            </select>
          </div>
          {#if FURNITURE_TYPES.includes(newEntry.productType)}
            <div class="op-field">
              <label for="entry-definition">{$t('lootTables.furnitureDefinitionId')}</label>
              <div class="op-pick">
                <input autocomplete="off" spellcheck="false"
                  id="entry-definition"
                  type="number"
                  bind:value={newEntry.furnitureDefinitionId}
                />
                <button
                  type="button" class="ghost-button"
                  onclick={() => (picking = 'entry')}>{$t('lootTables.pick')}</button
                >
              </div>
            </div>
          {:else}
            <div class="op-field">
              <label for="entry-extra">{$t('lootTables.extraParam')}</label>
              <input autocomplete="off" spellcheck="false" id="entry-extra" bind:value={newEntry.extraParam} />
            </div>
          {/if}
          <div class="op-field">
            <label for="entry-variant">{$t('lootTables.variant')}</label>
            <input autocomplete="off" spellcheck="false" id="entry-variant" bind:value={newEntry.variant} />
          </div>
          <div class="op-field">
            <label for="entry-weight">{$t('lootTables.weight')}</label>
            <input autocomplete="off" spellcheck="false" id="entry-weight" type="number" min="1" bind:value={newEntry.weight} />
          </div>
        </div>
        <div class="op-actions">
          <button
            type="button" class="btn btn-primary btn-sm success"
            onclick={() =>
              stage(
                $t('lootTables.newEntry'),
                selectedPool.code,
                '/api/v1/operations/loot-tables/entries',
                {
                  ...newEntry,
                  poolCode: selectedPool.code,
                  furnitureDefinitionId: Number(newEntry.furnitureDefinitionId) || 0,
                  weight: Number(newEntry.weight) || 1,
                }
              )}
          >
            {$t('lootTables.newEntry')}
          
            {$t('lootTables.newEntry')}
          </button>
        </div>
      {/if}
    </section>
  {/if}
  {/if}

  {#if tab === 'bindings'}
  <section class="panel">
    <div class="panel-head">
      <h2>
        <Link2 size={17} strokeWidth={2} aria-hidden="true" />
        {$t('lootTables.bindingsHeading')}
      </h2>
    </div>
    <p class="muted">{$t('lootTables.bindingsHint')}</p>

    {#if bindings.length === 0}
      <EmptyState message={$t('lootTables.noBindings')} />
    {/if}
  </section>

  <!-- The bindings filter gets its own row, like every other filter on the site: sharing a panel
       with the table it narrows is what made it read as part of the table's header. -->
  {#if bindings.length > 0}
    <section class="panel" style="margin-top: 12px;">
      <div class="toolbar-grid">
        <label>
          {$t('lootTables.searchBindings')}
          <input autocomplete="off" spellcheck="false"
            type="search"
            bind:value={bindingQuery}
            oninput={() => (bindingPage = 1)}
            placeholder={$t('lootTables.searchBindingsPlaceholder')}
          />
        </label>
        <label>
          {$t('lootTables.pool')}
          <select bind:value={bindingPool} onchange={() => (bindingPage = 1)}>
            <option value="">{$t('lootTables.allPools')}</option>
            {#each bindingPools as pool}
              <option value={pool}>{pool}</option>
            {/each}
          </select>
        </label>
      </div>
    </section>

    <section class="panel" style="margin-top: 12px;">
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>{$t('lootTables.furnitureDefinitionId')}</th>
              <th>{$t('lootTables.pool')}</th>
              <th>{$t('lootTables.hitsRequired')}</th>
              {#if canManage}<th>{$t('common.actions')}</th>{/if}
            </tr>
          </thead>
          <tbody>
            {#each pagedBindings as binding (binding.id)}
              <tr>
                <td>
                  <span class="cell-row">
                    <AssetImage
                      src={binding.furnitureIconUrl}
                      alt={binding.furnitureName ?? ''}
                      size={32}
                    />
                    <span class="cell-text">
                      <strong class="truncate"
                        >{binding.furnitureName ?? `#${binding.furnitureDefinitionId}`}</strong
                      >
                      <span class="muted">{binding.furnitureLogic ?? '—'}</span>
                    </span>
                  </span>
                </td>
                <td><span class="status-badge">{binding.pool}</span></td>
                <td>{formatNumber(binding.hitsRequired)}</td>
                {#if canManage}
                  <td>
                    <button
                      type="button"
                      class="icon-btn"
                      title={$t('lootTables.remove')}
                      onclick={() =>
                        stage(
                          $t('lootTables.remove'),
                          binding.furnitureName ?? `#${binding.furnitureDefinitionId}`,
                          '/api/v1/operations/loot-tables/bindings/delete',
                          { bindingId: binding.id },
                          true
                        )}
                    >
                      {$t('common.delete')}</button>
                  </td>
                {/if}
              </tr>
            {/each}
          </tbody>
        </table>
      </div>

      {#if filteredBindings.length === 0}
        <EmptyState message={$t('lootTables.noMatchingBindings')} />
      {:else if bindingPageCount > 1}
        <Pagination
          page={bindingPage}
          pageCount={bindingPageCount}
          total={filteredBindings.length}
          pageSize={BINDINGS_PAGE_SIZE}
          label={$t('lootTables.bindingsLabel')}
          prevLabel={$t('common.prev')}
          nextLabel={$t('common.next')}
          pageWord={$t('common.page')}
          onchange={(detail) => (bindingPage = detail)}
        />
      {/if}
    </section>
  {/if}

  {#if canManage}
    <section class="panel" style="margin-top: 12px;">
      <div class="op-grid">
        <div class="op-field">
          <label for="binding-definition">{$t('lootTables.furnitureDefinitionId')}</label>
          <div class="op-pick">
            <input autocomplete="off" spellcheck="false"
              id="binding-definition"
              type="number"
              bind:value={newBinding.furnitureDefinitionId}
            />
            <button type="button" class="ghost-button" onclick={() => (picking = 'binding')}
              >{$t('lootTables.pick')}</button
            >
          </div>
        </div>
        <div class="op-field">
          <label for="binding-pool">{$t('lootTables.pool')}</label>
          <select id="binding-pool" bind:value={newBinding.poolCode}>
            <option value="">—</option>
            {#each pools as pool (pool.id)}
              <option value={pool.code}>{pool.name}</option>
            {/each}
          </select>
        </div>
        <div class="op-field">
          <label for="binding-hits">{$t('lootTables.hitsRequired')}</label>
          <input autocomplete="off" spellcheck="false" id="binding-hits" type="number" min="1" bind:value={newBinding.hitsRequired} />
        </div>
      </div>
      <div class="op-actions">
        <button
          type="button" class="btn btn-primary btn-sm success"
          disabled={!newBinding.furnitureDefinitionId || !newBinding.poolCode}
          onclick={() =>
            stage(
              $t('lootTables.newBinding'),
              newBinding.poolCode,
              '/api/v1/operations/loot-tables/bindings',
              {
                ...newBinding,
                furnitureDefinitionId: Number(newBinding.furnitureDefinitionId) || 0,
                hitsRequired: Number(newBinding.hitsRequired) || 1,
              }
            )}
        >
          {$t('lootTables.newBinding')}
        </button>
      </div>
    </section>
  {/if}
{/if}
{/if}

{#if picking}
  <PickerModal
    kind="furniture"
    title={$t('lootTables.pickFurniture')}
    onSelect={(item) => {
      if (picking === 'entry') newEntry.furnitureDefinitionId = item.id;
      else newBinding.furnitureDefinitionId = item.id;
      picking = null;
    }}
    onClose={() => (picking = null)}
  />
{/if}

<ConfirmReasonModal
  open={Boolean($ops.pending)}
  title={$ops.pending?.title ?? ''}
  changes={$ops.pending?.changes ?? []}
  noteOnly={$ops.pending?.noteOnly ?? false}
  summary={$ops.pending?.summary ?? ''}
  confirmLabel={$ops.pending?.title ?? $t('lootTables.save')}
  busy={$ops.busy}
  error={$ops.error}
  danger={$ops.pending?.danger ?? false}
  onconfirm={ops.confirm}
  oncancel={() => ops.cancel()}
/>

<style>
  /* The global tokens style each block but not the gaps between them inside one panel, so the
     stats ran into the table and the table into the action row. */
  .panel > .stats,
  .panel > .table-wrap,
  .panel > .op-grid {
    margin-bottom: 18px;
  }


  /* Icon and label sit on one line. AssetImage and the label are both block-level here, so without
     this they stack and the row grows to two lines for no reason. */
  .cell-row {
    display: flex;
    align-items: center;
    gap: 8px;
    min-width: 0;
  }

  .cell-row .cell-text {
    display: flex;
    flex-direction: column;
    min-width: 0;
  }

  .panel > .op-actions {
    margin-top: 4px;
  }

  .pool-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(210px, 1fr));
    gap: 8px;
    margin-bottom: 12px;
  }

  .pool-tile {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
    /* Fixed height, so a tile carrying a badge does not stand taller than its neighbours. */
    min-height: 40px;
    width: 100%;
    text-align: left;
    border: 1px solid var(--line-strong);
    border-radius: 8px;
    background: var(--surface-strong);
    color: var(--ink);
    padding: 8px 12px;
    font-weight: 700;
    font-size: 0.86rem;
    transition: background 140ms ease, border-color 140ms ease;
  }

  .pool-tile:hover {
    background: var(--surface-hover);
  }

  /* The name truncates rather than wrapping: two-line tiles would reintroduce the ragged heights
     this grid exists to remove, and the full name is one hover away. */
  .pool-tile-name {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .pool-tile-badges {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    flex: none;
  }

  /* Selected pool: aria-pressed is the state, so it drives the styling rather than a second class.
     Tinting the surface as well as the border -- a border alone was easy to miss across a grid. */
  .pool-tile[aria-pressed='true'] {
    border-color: var(--accent);
    color: var(--accent);
    background: var(--accent-soft);
  }
</style>
