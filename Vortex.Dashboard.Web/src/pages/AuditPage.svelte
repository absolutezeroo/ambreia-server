<script lang="ts">
  import { readNumberParam, writeParams } from '../lib/urlState';
  import FilterBar from '../components/FilterBar.svelte';
  import LoadingOverlay from '../components/LoadingOverlay.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import { readFilterValues } from '../lib/filters';
  import type { FilterField } from '../lib/filters';
  import { onMount } from 'svelte';
  import { apiGet } from '../lib/api';
  import { compactCorrelation, formatDate } from '../lib/format';
  import AuditDetail from '../components/AuditDetail.svelte';
  import { summarizeAudit } from '../lib/auditData';
  import EntityLink from '../components/EntityLink.svelte';
  import AccessDeniedNotice from '../components/AccessDeniedNotice.svelte';
  import Pagination from '../components/Pagination.svelte';
  import { isPermissionDeniedError } from '../lib/permissions';
  import { openPlayer, openItem } from '../lib/session';
  import { t, translate, type Translator } from '../lib/i18n';
  import type { AuditEntry, AuditPage } from '../lib/apiTypes';

  const categoryOptions = [
    '',
    'Auth',
    'Staff',
    'Moderation',
    'Economy',
    'Item',
    'Room',
    'Security',
    'Social',
    'System',
    'Progression',
  ];

  const categoryColors: Record<string, string> = {
    Auth: 'var(--accent)',
    Staff: '#9f6ce1',
    Moderation: 'var(--danger)',
    Economy: 'var(--ok)',
    Item: 'var(--warning)',
    Room: '#e0995e',
    Security: '#df6f7b',
    Social: '#4fb3bf',
    System: '#64748b',
    Progression: '#c9a227',
    other: '#64748b',
  };

  const resultBadgeClass: Record<string, string> = {
    Success: 'status-badge--ok',
    Denied: 'status-badge--warn',
    Failed: 'status-badge--bad',
  };

  // The page's own filter row, replaced by the shared one: the controls were the same six a
  // hand-rolled form drew, minus the address bar and minus any way to see what was on.
  const FILTERS: FilterField[] = [
    { id: 'since', label: translate('audit.since'), kind: 'datetime' },
    { id: 'until', label: translate('audit.until'), kind: 'datetime' },
    { id: 'actor', label: translate('audit.actor'), kind: 'entity', picker: 'user' },
    { id: 'target', label: translate('audit.target'), kind: 'entity', picker: 'user' },
    {
      id: 'category',
      label: translate('audit.category'),
      kind: 'select',
      anyLabel: translate('audit.allCategories'),
      options: categoryOptions
        .filter(Boolean)
        .map((value) => ({ value, label: translate(`audit.categories.${value}`) })),
    },
    {
      id: 'action',
      label: translate('audit.action'),
      kind: 'text',
      placeholder: translate('audit.actionPlaceholder'),
    },
  ];

  let filters = $state(readFilterValues(FILTERS));
  // Which row is open, by index. Reset on every reload -- an index kept across a refetch would
  // expand whatever event happens to land in that slot.
  let expanded = $state<number | null>(null);

  function toggle(index: number) {
    expanded = expanded === index ? null : index;
  }
  let limit = $state(50);
  let page = $state(readNumberParam('page', 1));

  $effect(() => {
    writeParams({ page: page > 1 ? page : '' });
  });

  let rows = $state<AuditEntry[]>([]);
  let total = $state(0);
  let loading = $state(false);
  let error = $state('');
  let forbidden = $state(false);

  let totalPages = $derived(Math.max(1, Math.ceil(total / limit)));

  function categoryColor(value: string) {
    return categoryColors[value] || categoryColors.other;
  }

  // Takes the translator function itself (not just a locale flag) so template call sites can pass
  // $t explicitly and stay reactive -- see the ApiExplorerPage `filtered` reactivity note: a
  // helper that reads $t only inside its body, with $t absent from the template expression's own
  // text, is invisible to Svelte's per-expression dependency tracking and won't re-render on
  // locale change.
  function categoryLabel(value: string, translator: Translator) {
    return value ? translator(`audit.categories.${value}`) : translator('audit.allCategories');
  }

  function resultLabel(value: string, translator: Translator) {
    if (value === 'Success') return translator('common.resultSuccess');
    if (value === 'Denied') return translator('common.resultDenied');
    if (value === 'Failed') return translator('common.resultFailed');
    return value;
  }

  function buildParams() {
    const params = new URLSearchParams({ limit: String(limit), page: String(page) });

    if (filters.since) params.set('since', new Date(filters.since).toISOString());
    if (filters.until) params.set('until', new Date(filters.until).toISOString());
    if (filters.actor.trim()) params.set('actor', filters.actor.trim());
    if (filters.target.trim()) params.set('target', filters.target.trim());
    if (filters.category) params.set('category', filters.category);
    if (filters.action.trim()) params.set('action', filters.action.trim());

    return params;
  }

  async function refresh() {
    loading = true;
    error = '';
    forbidden = false;

    try {
      const data = await apiGet<AuditPage>(`/api/v1/forensics/audit?${buildParams()}`);
      rows = data.items || [];
      expanded = null;
      total = data.total || 0;
    } catch (err) {
      if (isPermissionDeniedError(err)) {
        forbidden = true;
        rows = [];
        expanded = null;
        total = 0;
        return;
      }

      error = (err as Error).message;
      rows = [];
      total = 0;
    } finally {
      loading = false;
    }
  }

  function applyFilters() {
    page = 1;
    void refresh();
  }

  function goToPage(next: number) {
    page = Math.min(totalPages, Math.max(1, next));
    void refresh();
  }

  onMount(refresh);
</script>

<section class="panel">
  <PageHeader title={$t('audit.title')} description={$t('audit.description')}>
    {#snippet actions()}
      <button type="button" onclick={refresh} disabled={loading} class="warning">{$t('common.refresh')}</button>
    {/snippet}
  </PageHeader>
</section>

<section class="panel">

  <FilterBar fields={FILTERS} bind:values={filters} onchange={applyFilters} />

  <!-- Not a filter: it does not narrow the trail, it decides how much of it arrives per request.
       Left out of the bar so it never appears as a chip beside the things that do narrow it. -->
  <label class="page-size">
    {$t('audit.pageSize')}
    <input
      autocomplete="off"
      spellcheck="false"
      type="number"
      min="10"
      max="500"
      bind:value={limit}
      onchange={applyFilters}
    />
  </label>
</section>

<section class="panel">

  {#if forbidden}
    <AccessDeniedNotice message={$t('audit.accessDenied')} />
  {:else if error}
    <p class="empty-state danger" role="alert">{error}</p>
  {:else}
    <!-- The count stays put while a reload is in flight; the overlay says the page is busy. The
         sentence that used to replace it here moved the line under it on every filter. -->
    <p class="muted">{$t('audit.eventsFound', { count: total })}</p>
  {/if}

  <div class="table-wrap">
    <table>
      <thead><tr><th>{$t('audit.colTime')}</th><th>{$t('audit.colCategory')}</th><th>{$t('audit.colAction')}</th><th>{$t('audit.colActor')}</th><th>{$t('audit.colTarget')}</th><th>{$t('audit.colResult')}</th><th>{$t('audit.colData')}</th><th>{$t('audit.colCid')}</th></tr></thead>
      <tbody>
        {#each rows as row, i}
          <tr
            class="audit-row"
            class:expanded={expanded === i}
            tabindex="0"
            role="button"
            aria-expanded={expanded === i}
            onclick={() => toggle(i)}
            onkeydown={(e) => (e.key === 'Enter' || e.key === ' ') && (e.preventDefault(), toggle(i))}
          >
            <td>{formatDate(row.occurredAt)}</td>
            <td>
              <span class="category-badge" style={`border-left-color: ${categoryColor(row.category)};`}>
                {categoryLabel(row.category, $t)}
              </span>
            </td>
            <td><code>{row.action}</code></td>
            <td><EntityLink id={row.actorPlayerId} label={row.actorName || ''} {openPlayer} {openItem} /></td>
            <td><EntityLink id={row.targetPlayerId} label={row.targetName || ''} {openPlayer} {openItem} /></td>
            <td>
              <span class={`status-badge ${resultBadgeClass[row.result] || 'status-badge--unknown'}`}>
                {resultLabel(row.result, $t)}
              </span>
            </td>
            <td class="truncate" title={summarizeAudit(row.data)}>{summarizeAudit(row.data)}</td>
            <td>{compactCorrelation(row.correlationId)}</td>
          </tr>
          {#if expanded === i}
            <tr class="audit-detail-row">
              <td colspan="8"><AuditDetail data={row.data} /></td>
            </tr>
          {/if}
        {:else}
          <tr><td colspan="8" class="muted">{$t('audit.noRows')}</td></tr>
        {/each}
      </tbody>
    </table>
  </div>

  {#if total > 0}
    <Pagination
      page={page}
      pageCount={totalPages}
      pageWord={$t('common.page')}
      prevLabel={$t('common.prev')}
      nextLabel={$t('common.next')}
      disabled={loading}
      onchange={goToPage}
    />
  {/if}
</section>

<LoadingOverlay show={loading} label={$t('audit.loadingEvents')} />

<style>
  /* The page size sits under the filter row, quieter than the fields above it: it is a setting, not
     a question about the data. */
  .page-size {
    display: inline-flex;
    align-items: center;
    gap: 8px;
    margin-top: 10px;
    color: var(--muted);
    font-size: 0.82rem;
  }

  .page-size input {
    width: 96px;
  }

  .audit-row {
    cursor: pointer;
  }

  .audit-row:hover,
  .audit-row.expanded {
    background: var(--surface-hover, rgba(128, 128, 128, 0.08));
  }

  .audit-detail-row > td {
    padding: 0;
  }

  .category-badge {
    display: inline-flex;
    align-items: center;
    border-left: 3px solid var(--muted);
    padding: 2px 8px;
    font-size: 0.8rem;
    color: var(--muted-strong);
  }
</style>

