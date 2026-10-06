<script lang="ts">

  import { onMount } from 'svelte';
  import { apiGet } from '../lib/api';
  import type { PetStats } from '../lib/apiTypes';
  import { formatNumber } from '../lib/format';
  import { isPermissionDeniedError } from '../lib/permissions';
  import { openPlayer } from '../lib/session';
  import AccessDeniedNotice from '../components/AccessDeniedNotice.svelte';
  import LoadingOverlay from '../components/LoadingOverlay.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import EntityLink from '../components/EntityLink.svelte';
  import LineChart from '../components/LineChart.svelte';
  import StatCard from '../components/StatCard.svelte';
  import { PawPrint, Hash } from '@lucide/svelte';
  import { t } from '../lib/i18n';

  const granularities = ['day', 'month', 'year'];

  function granularityLabel(value: string, translator: (key: string) => string) {
    return translator(`common.granularity${value.charAt(0).toUpperCase()}${value.slice(1)}`);
  }

  let since = $state('');
  let until = $state('');
  let granularity = $state('day');
  let loading = $state(false);
  let forbidden = $state(false);
  let error = $state('');
  let data = $state<PetStats | null>(null);

  function toLocalDateValue(value: string | Date) {
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? '' : date.toISOString().slice(0, 10);
  }

  function setDefaultWindow() {
    const end = new Date();
    const start = new Date(end.getTime() - 30 * 24 * 60 * 60 * 1000);
    since = toLocalDateValue(start);
    until = toLocalDateValue(end);
  }

  async function refresh() {
    loading = true;
    error = '';
    forbidden = false;

    const params = new URLSearchParams({ granularity });
    if (since) params.set('since', new Date(`${since}T00:00:00`).toISOString());
    if (until) params.set('until', new Date(`${until}T23:59:59`).toISOString());

    try {
      data = await apiGet<PetStats>(`/api/v1/pets/stats?${params}`);
    } catch (err) {
      if (isPermissionDeniedError(err)) {
        forbidden = true;
        data = null;
        return;
      }

      error = err instanceof Error ? err.message : String(err);
      data = null;
    } finally {
      loading = false;
    }
  }

  let growthSeries = $derived(data
    ? [
        {
          name: $t('petsStats.totalPets'),
          color: 'var(--accent)',
          points: (data.growth || []).map((p) => ({ label: p.label, value: p.petsCreated })),
        },
      ]
    : []);

  onMount(() => {
    setDefaultWindow();
    void refresh();
  });
</script>

<section class="panel">
  <PageHeader title={$t('petsStats.title')} description={$t('petsStats.description')}>
    {#snippet actions()}
      <button type="button" onclick={refresh} class="warning">{$t('common.refresh')}</button>
    {/snippet}
  </PageHeader>
</section>

<section class="panel">

  <form class="toolbar-grid" onsubmit={(event) => { event.preventDefault(); refresh(); }}>
    <label>
      {$t('common.since')}
      <input autocomplete="off" spellcheck="false" type="date" bind:value={since} />
    </label>
    <label>
      {$t('common.until')}
      <input autocomplete="off" spellcheck="false" type="date" bind:value={until} />
    </label>
    <label>
      {$t('common.granularity')}
      <select bind:value={granularity}>
        {#each granularities as g}
          <option value={g}>{granularityLabel(g, $t)}</option>
        {/each}
      </select>
    </label>
  </form>

  {#if forbidden}
    <AccessDeniedNotice message={$t('petsStats.accessDenied')} />
  {:else if error}
    <p class="empty-state danger" role="alert">{error}</p>
  {/if}
</section>

{#if data}
  <div class="metric-grid" style="margin-top: 12px;">
    <StatCard label={$t('petsStats.totalPets')} value={formatNumber(data.totals.totalPets)}>
      {#snippet icon()}
        <PawPrint size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard label={$t('petsStats.avgLevel')} value={String(data.totals.avgLevel)}>
      {#snippet icon()}
        <Hash size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
  </div>

  <div class="panel" style="margin-top: 12px;">
    <div class="panel-head"><h2>{$t('petsStats.growthChartTitle', { granularity: granularityLabel(granularity, $t) })}</h2></div>
    <LineChart series={growthSeries} valueFormatter={(v) => formatNumber(v)} />
  </div>

  <div class="panel" style="margin-top: 12px;">
    <div class="panel-head"><h2>{$t('petsStats.byTypeTitle')}</h2></div>
    <div class="table-wrap">
      <table>
        <thead><tr><th>{$t('petsStats.colType')}</th><th>{$t('petsStats.colCount')}</th></tr></thead>
        <tbody>
          {#each data.byType || [] as row}
            <tr><td>{row.type}</td><td>{formatNumber(row.count)}</td></tr>
          {:else}
            <tr><td colspan="2" class="muted">{$t('petsStats.noPets')}</td></tr>
          {/each}
        </tbody>
      </table>
    </div>
  </div>

  <div class="panel" style="margin-top: 12px;">
    <div class="panel-head"><h2>{$t('petsStats.topOwnersTitle')}</h2></div>
    <div class="table-wrap">
      <table>
        <thead><tr><th>{$t('petsStats.colOwner')}</th><th>{$t('petsStats.colPetCount')}</th></tr></thead>
        <tbody>
          {#each data.topOwners || [] as row}
            <tr>
              <td><EntityLink type="player" id={row.ownerId} label={row.ownerName ?? undefined} {openPlayer} /></td>
              <td>{formatNumber(row.petCount)}</td>
            </tr>
          {:else}
            <tr><td colspan="2" class="muted">{$t('petsStats.noPets')}</td></tr>
          {/each}
        </tbody>
      </table>
    </div>
  </div>
{/if}

<LoadingOverlay show={loading} />
