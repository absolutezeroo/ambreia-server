<script lang="ts">
  import { onMount } from 'svelte';
  import OpResult from '../components/OpResult.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import EmptyState from '../components/EmptyState.svelte';
  import AccessDeniedNotice from '../components/AccessDeniedNotice.svelte';
  import AssetImage from '../components/AssetImage.svelte';
  import Drawer from '../components/Drawer.svelte';
  import PickerModal from '../components/PickerModal.svelte';
  import { apiGet } from '../lib/api';
  import { createWriteOps } from '../lib/writeOps';
  import { diffFields } from '../lib/changes';
  import { isPermissionDeniedError, hasDashboardCapability } from '../lib/permissions';
  import { CAPABILITIES } from '../lib/dashboardPermissions';
  import { identity } from '../lib/session';
  import { t } from '../lib/i18n';
  import type { PickerRow } from '../lib/pickers/directories';
  import type { GiftWrappingList, GiftWrappingRow } from '../lib/apiTypes';

  type Kind = 'Parcel' | 'Box' | 'Ribbon';

  /**
   * The drawer's form. The numbers are strings while typed into a number input and numbers once
   * loaded from a row, so they hold both rather than lying about either.
   */
  type WrappingForm = {
    kind: Kind;
    furnitureName: string;
    furnitureIconUrl: string | null;
    artIndex: string | number;
    isDefault: boolean;
    sortOrder: string | number;
  };

  type WrappingDrawer =
    | { mode: 'create'; id: null; before?: undefined; form: WrappingForm }
    | { mode: 'edit'; id: number; before: WrappingForm; form: WrappingForm };

  const SECTIONS: { kind: Kind; title: string; hint: string }[] = [
    { kind: 'Parcel', title: 'giftWrappings.parcels', hint: 'giftWrappings.parcelsHint' },
    { kind: 'Box', title: 'giftWrappings.boxes', hint: 'giftWrappings.boxesHint' },
    { kind: 'Ribbon', title: 'giftWrappings.ribbons', hint: 'giftWrappings.ribbonsHint' },
  ];

  const KIND_LABELS: Record<Kind, string> = {
    Parcel: 'giftWrappings.kindParcel',
    Box: 'giftWrappings.kindBox',
    Ribbon: 'giftWrappings.kindRibbon',
  };

  // What the audit line says an edit actually changed, rather than just which row was touched.
  const WRAPPING_FIELDS = [
    { key: 'kind', label: 'Kind' },
    { key: 'furnitureName', label: 'Furniture' },
    { key: 'artIndex', label: 'Art index' },
    { key: 'isDefault', label: 'Default' },
    { key: 'sortOrder', label: 'Order' },
  ];

  let rows = $state<GiftWrappingRow[]>([]);
  let wrappingEnabled = $state(true);
  let loading = $state(false);
  let denied = $state(false);
  let error = $state('');

  // Editing happens in the drawer, never in the page: a form spliced under the tables pushes the
  // list off screen, which is the thing you were looking at when you decided to change it.
  let drawer = $state<WrappingDrawer | null>(null);
  let picking = $state(false);

  const ops = createWriteOps(async () => {
    drawer = null;
    await load();
  });

  let canManage = $derived(hasDashboardCapability($identity, CAPABILITIES.opsGiftWrappingsManage));
  let canSave = $derived(
    drawer === null
      ? false
      : drawer.form.kind === 'Parcel'
        ? Boolean(drawer.form.furnitureName.trim())
        : drawer.form.artIndex !== '' && Number(drawer.form.artIndex) >= 0,
  );

  function rowsOf(kind: Kind) {
    return rows.filter((row) => row.kind === kind);
  }

  function nextOrder(kind: Kind) {
    const orders = rowsOf(kind).map((row) => row.sortOrder);

    return orders.length === 0 ? 0 : Math.max(...orders) + 1;
  }

  function openCreate(kind: Kind) {
    drawer = {
      mode: 'create',
      id: null,
      form: {
        kind,
        furnitureName: '',
        furnitureIconUrl: null,
        artIndex: '',
        isDefault: false,
        sortOrder: nextOrder(kind),
      },
    };
  }

  function openEdit(row: GiftWrappingRow) {
    const form: WrappingForm = {
      kind: row.kind as Kind,
      furnitureName: row.furnitureName ?? '',
      furnitureIconUrl: row.furnitureIconUrl ?? null,
      artIndex: row.artIndex ?? '',
      isDefault: row.isDefault,
      sortOrder: row.sortOrder,
    };

    // The values as loaded, kept so the diff can say what they WERE.
    drawer = { mode: 'edit', id: row.id, before: { ...form }, form };
  }

  function pickFurniture(item: PickerRow) {
    if (drawer) {
      drawer.form.furnitureName = item.name;
      drawer.form.furnitureIconUrl = item.iconUrl ?? null;
    }

    picking = false;
  }

  function save() {
    // The button that calls this only exists inside the drawer.
    const open = drawer!;
    const isParcel = open.form.kind === 'Parcel';
    const body = {
      kind: open.form.kind,
      furnitureName: isParcel ? open.form.furnitureName.trim() : null,
      artIndex: isParcel ? null : Number(open.form.artIndex),
      isDefault: isParcel && open.form.isDefault,
      sortOrder: Number(open.form.sortOrder) || 0,
    };

    if (open.mode === 'edit') {
      ops.ask(
        '/api/v1/operations/gift-wrappings/update',
        { ...body, wrappingId: open.id },
        `${$t('giftWrappings.editWrapping')} #${open.id}`,
        $t('giftWrappings.updated'),
        { changes: diffFields(open.before, open.form, WRAPPING_FIELDS) },
      );
    } else {
      ops.ask(
        '/api/v1/operations/gift-wrappings',
        body,
        $t('giftWrappings.newWrapping'),
        $t('giftWrappings.created'),
      );
    }
  }

  function remove(row: GiftWrappingRow) {
    ops.ask(
      '/api/v1/operations/gift-wrappings/delete',
      { wrappingId: row.id },
      `${$t('giftWrappings.delete')} — ${row.furnitureName ?? `#${row.artIndex}`}`,
      $t('giftWrappings.deleteConfirm'),
      { danger: true },
    );
  }

  async function load() {
    loading = true;
    error = '';
    try {
      const data = await apiGet<GiftWrappingList>('/api/v1/gift-wrappings');

      rows = data.items ?? [];
      wrappingEnabled = data.wrappingEnabled;
      denied = false;
    } catch (e) {
      if (isPermissionDeniedError(e)) denied = true;
      else error = (e as Error)?.message ?? String(e);
    } finally {
      loading = false;
    }
  }

  onMount(load);
</script>

<section class="panel">
  <PageHeader title={$t('giftWrappings.title')} description={$t('giftWrappings.description')}>
    {#snippet actions()}
      <button type="button" class="warning" onclick={load} disabled={loading}>
        {$t('common.refresh')}
      </button>
      {#if canManage}
        <button
          type="button"
          class="ghost-button"
          onclick={() =>
            ops.ask(
              '/api/v1/operations/gift-wrappings/reload',
              {},
              $t('giftWrappings.reload'),
              $t('giftWrappings.reloaded'),
            )}
        >
          {$t('giftWrappings.reload')}
        </button>
      {/if}
    {/snippet}
  </PageHeader>
</section>

{#if denied}
  <AccessDeniedNotice message={$t('giftWrappings.accessDenied')} />
{:else}
  {#if error}
    <EmptyState kind="error" message={error} />
  {/if}
  {#if !loading && !wrappingEnabled}
    <EmptyState kind="error" message={$t('giftWrappings.disabled')} />
  {/if}
  <OpResult result={$ops.result} />

  {#each SECTIONS as section (section.kind)}
    {@const sectionRows = rowsOf(section.kind)}
    <section class="panel">
      <div class="panel-head">
        <div>
          <h2>{$t(section.title)} <span class="muted">({sectionRows.length})</span></h2>
          <p class="muted field-hint">{$t(section.hint)}</p>
        </div>
        {#if canManage}
          <button type="button" class="success" onclick={() => openCreate(section.kind)}>
            {$t('giftWrappings.newWrapping')}
          </button>
        {/if}
      </div>

      {#if loading}
        <EmptyState kind="loading" />
      {:else if sectionRows.length === 0}
        <EmptyState message={$t('giftWrappings.empty')} />
      {:else}
        <div class="table-scroll">
          <table>
            <thead>
              <tr>
                <th>{$t('giftWrappings.sortOrder')}</th>
                {#if section.kind === 'Parcel'}
                  <th>{$t('giftWrappings.furniture')}</th>
                  <th>{$t('giftWrappings.spriteId')}</th>
                {:else}
                  <th>{$t('giftWrappings.artIndex')}</th>
                {/if}
                {#if canManage}<th></th>{/if}
              </tr>
            </thead>
            <tbody>
              {#each sectionRows as row (row.id)}
                <tr>
                  <td class="muted">{row.sortOrder}</td>
                  {#if section.kind === 'Parcel'}
                    <td>
                      <span class="furni">
                        <AssetImage src={row.furnitureIconUrl} alt={row.furnitureName ?? ''} />
                        <span class="mono">{row.furnitureName}</span>
                        {#if row.isDefault}
                          <span class="status-badge">{$t('giftWrappings.defaultBadge')}</span>
                        {/if}
                      </span>
                    </td>
                    <td>
                      {#if row.spriteId == null}
                        <span
                          class="status-badge status-badge--warn"
                          title={$t('giftWrappings.notFoundHint')}
                        >
                          {$t('giftWrappings.notFound')}
                        </span>
                      {:else}
                        <span class="mono">{row.spriteId}</span>
                      {/if}
                    </td>
                  {:else}
                    <td class="mono">{row.artIndex}</td>
                  {/if}
                  {#if canManage}
                    <td class="row-actions">
                      <button type="button" class="ghost-button" onclick={() => openEdit(row)}>
                        {$t('common.edit')}
                      </button>
                      <button type="button" class="danger" onclick={() => remove(row)}>
                        {$t('giftWrappings.delete')}
                      </button>
                    </td>
                  {/if}
                </tr>
              {/each}
            </tbody>
          </table>
        </div>
      {/if}
    </section>
  {/each}
{/if}

{#if drawer}
  <Drawer
    title={drawer.mode === 'create'
      ? $t('giftWrappings.newWrapping')
      : $t('giftWrappings.editWrapping')}
    eyebrow={$t('giftWrappings.title')}
    onclose={() => (drawer = null)}
  >
    <div class="drawer-form">
      <div class="op-field">
        <label for="wrapping-kind">{$t('giftWrappings.kind')}</label>
        <select id="wrapping-kind" bind:value={drawer.form.kind}>
          {#each SECTIONS as section (section.kind)}
            <option value={section.kind}>{$t(KIND_LABELS[section.kind])}</option>
          {/each}
        </select>
      </div>

      {#if drawer.form.kind === 'Parcel'}
        <div class="op-field">
          <span class="label">{$t('giftWrappings.furniture')}</span>
          <button type="button" class="ghost-button picked" onclick={() => (picking = true)}>
            {#if drawer.form.furnitureName}
              <AssetImage src={drawer.form.furnitureIconUrl} alt={drawer.form.furnitureName} />
              <span class="mono">{drawer.form.furnitureName}</span>
            {:else}
              {$t('giftWrappings.choose')}
            {/if}
          </button>
        </div>
        <label class="check">
          <input type="checkbox" bind:checked={drawer.form.isDefault} />
          {$t('giftWrappings.isDefault')}
        </label>
        <p class="field-hint">{$t('giftWrappings.isDefaultHint')}</p>
      {:else}
        <div class="op-field">
          <label for="wrapping-art">{$t('giftWrappings.artIndex')}</label>
          <input
            autocomplete="off"
            id="wrapping-art"
            type="number"
            min="0"
            bind:value={drawer.form.artIndex}
          />
          <p class="field-hint">{$t('giftWrappings.artIndexHint')}</p>
        </div>
      {/if}

      <div class="op-field">
        <label for="wrapping-order">{$t('giftWrappings.sortOrder')}</label>
        <input
          autocomplete="off"
          id="wrapping-order"
          type="number"
          bind:value={drawer.form.sortOrder}
        />
        <p class="field-hint">{$t('giftWrappings.sortOrderHint')}</p>
      </div>
    </div>

    {#snippet actions()}
      <button
        type="button"
        class={drawer?.mode === 'create' ? 'success' : ''}
        onclick={save}
        disabled={!canSave}
      >
        {$t('giftWrappings.save')}
      </button>
      <button type="button" class="ghost-button" onclick={() => (drawer = null)}>
        {$t('giftWrappings.cancel')}
      </button>
    {/snippet}
  </Drawer>
{/if}

{#if picking}
  <PickerModal
    kind="furniture"
    title={$t('giftWrappings.pickFurniture')}
    onSelect={pickFurniture}
    onClose={() => (picking = false)}
    canSelect={canManage}
  />
{/if}

<style>
  .panel-head .field-hint {
    margin: 0.2rem 0 0;
  }

  .drawer-form {
    display: grid;
    gap: 14px;
  }

  .row-actions {
    display: flex;
    gap: 0.4rem;
    justify-content: flex-end;
  }

  .furni,
  .picked {
    display: inline-flex;
    align-items: center;
    gap: 0.5rem;
  }

  .check {
    display: flex;
    align-items: center;
    gap: 0.5rem;
  }

  .field-hint {
    margin: 0.25rem 0 0;
    font-size: 0.78rem;
    opacity: 0.7;
  }

  .mono {
    font-family: var(--font-mono, monospace);
  }
</style>
