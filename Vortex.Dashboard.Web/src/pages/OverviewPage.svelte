<script lang="ts">
  import { onMount } from 'svelte';
  import { apiGet } from '../lib/api';
  import { formatNumber } from '../lib/format';
  import EntityLink from '../components/EntityLink.svelte';
  import AccessDeniedNotice from '../components/AccessDeniedNotice.svelte';
  import StatCard from '../components/StatCard.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import LineChart from '../components/LineChart.svelte';
  import EmptyState from '../components/EmptyState.svelte';
  import { isPermissionDeniedError } from '../lib/permissions';
  import { openPlayer, openItem } from '../lib/session';
  import { t, locale } from '../lib/i18n';
  import type { DashboardInventory, DashboardOverview } from '../lib/apiTypes';

  /** One 10s poll, kept client-side: the server answers with a window, not a history. */
  type TrendSample = {
    label: string;
    packetRate: number;
    errorRate: number;
    latencyP50: number;
    at: number;
  };
  import { get } from 'svelte/store';
  import { Activity, Users, DoorOpen, Sparkles, Cpu, Gauge, TriangleAlert, Timer } from '@lucide/svelte';

  let data = $state<DashboardOverview | null>(null);
  let error = $state('');
  let forbidden = $state(false);
  let trend = $state<TrendSample[]>([]);
  // What the hotel contains, as opposed to how it is behaving. Fetched once (it moves on the scale
  // of an admin edit, not of a tick) and never on the 10s health poll.
  let inventory = $state<DashboardInventory | null>(null);
  const maxTrendSamples = 20;

  function addTrendSample(snapshot: DashboardOverview) {
    const packetRate = snapshot.live.packetsPerSecond;
    const errorRate = snapshot.live.errorsPerMinute;
    const p50 = snapshot.live.latencyP50Ms;
    const label = new Date().toLocaleTimeString(get(locale) === 'fr' ? 'fr-FR' : 'en-US', {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });

    trend = [
      ...trend.slice(-1 * (maxTrendSamples - 1)),
      { label, packetRate, errorRate, latencyP50: p50, at: Date.now() },
    ];
  }

  // The live trend feeds the shared LineChart (one charting primitive across the whole dashboard)
  // instead of the old hand-rolled bar rows + empty donut. Sky accent matches the token palette.
  let packetsSeries = $derived([
    {
      name: $t('overview.packetsPerSec'),
      color: '#4fb0e6',
      points: trend.map((entry) => ({ label: entry.label, value: entry.packetRate })),
    },
  ]);

  async function refresh() {
    forbidden = false;
    error = '';
    try {
      data = await apiGet<DashboardOverview>('/api/v1/monitoring/overview');
      addTrendSample(data);
    } catch (err) {
      if (isPermissionDeniedError(err)) {
        forbidden = true;
        data = null;
        return;
      }

      error = (err as Error).message;
    }
  }

  async function loadInventory() {
    try {
      inventory = await apiGet<DashboardInventory>('/api/v1/monitoring/inventory');
    } catch {
      // The inventory is a secondary panel: a failure here must not blank the health page.
      inventory = null;
    }
  }

  onMount(() => {
    void refresh();
    void loadInventory();
    const interval = setInterval(refresh, 10000);
    return () => clearInterval(interval);
  });
</script>

<section class="panel">
  <PageHeader title={$t('overview.title')} description={$t('overview.description')}>
    {#snippet actions()}
      <button type="button" onclick={refresh} class="warning">{$t('common.refresh')}</button>
    {/snippet}
  </PageHeader>
</section>

{#if forbidden}
  <AccessDeniedNotice message={$t('overview.accessDenied')} />
{:else if error}
  <EmptyState kind="error" message={error} />
{/if}

<div class="stats" style="margin-top: 12px;">
    <StatCard label={$t('overview.status')} value={data?.status || '-'}>
      {#snippet icon()}
        <Activity size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard label={$t('overview.sessions')} value={data?.activeSessions ?? '-'}>
      {#snippet icon()}
        <Users size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard label={$t('overview.rooms')} value={data?.activeRooms ?? '-'}>
      {#snippet icon()}
        <DoorOpen size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard accent label={$t('overview.club')} value={data?.activeMembershipSubscribers ?? '-'}>
      {#snippet icon()}
        <Sparkles size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard label={$t('overview.memory')} value={data?.managedMemoryMb ?? '-'}>
      {#snippet icon()}
        <Cpu size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard label={$t('overview.packetsPerSec')} value={formatNumber(data?.live.packetsPerSecond, 2)}>
      {#snippet icon()}
        <Gauge size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard label={$t('overview.errorsPerMin')} value={formatNumber(data?.live.errorsPerMinute, 2)}>
      {#snippet icon()}
        <TriangleAlert size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard label={$t('overview.latencyP50')} value={`${formatNumber(data?.live.latencyP50Ms, 2)} ms`}>
      {#snippet icon()}
        <Timer size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard label={$t('overview.latencyP95')} value={`${formatNumber(data?.live.latencyP95Ms, 2)} ms`}>
      {#snippet icon()}
        <Timer size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
</div>

<section class="panel" style="margin-top: 12px;">
  <div class="panel-head">
    <h2>{$t('overview.liveTrend')}</h2>
    <small class="muted">{$t('overview.lastTicks', { count: trend.length || 0 })}</small>
  </div>

  {#if trend.length === 0}
    <EmptyState kind="loading" message={$t('overview.waitingForTrend')} />
  {:else}
    <LineChart series={packetsSeries} height={220} valueFormatter={(v) => formatNumber(v, 2)} />
  {/if}
</section>

<section class="split-grid">
  <div class="panel">
    <h2>{$t('overview.topAbusers')}</h2>
    <div class="table-wrap">
      <table>
        <thead><tr><th>{$t('overview.colPlayer')}</th><th>{$t('overview.colPacketsMin')}</th></tr></thead>
        <tbody>
          {#each data?.live.topAbusers ?? [] as row}
            <tr>
              <td><EntityLink id={row.playerId} label={`player #${row.playerId}`} {openPlayer} {openItem} /></td>
              <td class="num-cell">{formatNumber(row.packetsPerMinute, 2)}</td>
            </tr>
          {:else}
            <tr><td colspan="2" class="muted">{$t('overview.noAbuseSignal')}</td></tr>
          {/each}
        </tbody>
      </table>
    </div>
  </div>

  <div class="panel">
    <h2>{$t('overview.topRooms')}</h2>
    <div class="table-wrap">
      <table>
        <thead><tr><th>{$t('overview.colRoom')}</th><th>{$t('overview.colPacketsMin')}</th></tr></thead>
        <tbody>
          {#each data?.live.topRooms ?? [] as row}
            <tr><td>room #{row.roomId}</td><td class="num-cell">{formatNumber(row.packetsPerMinute, 2)}</td></tr>
          {:else}
            <tr><td colspan="2" class="muted">{$t('overview.noRoomTraffic')}</td></tr>
          {/each}
        </tbody>
      </table>
    </div>
  </div>
</section>

{#if inventory}
  <section class="panel">
    <div class="panel-head">
      <h2>{$t('overview.inventoryTitle')}</h2>
      <small class="muted">{$t('overview.inventoryHint')}</small>
    </div>
    <div class="inventory-grid">
      {#each inventory.groups as group}
        <div class="inventory-group">
          <h3>{$t(`overview.inventoryGroup.${group.key}`)}</h3>
          <ul>
            {#each group.rows as row}
              <li class:empty={row.empty}>
                {#if row.route}
                  <a href={`#${row.route}`}>{$t(`overview.inventoryRow.${row.key}`)}</a>
                {:else}
                  <span>{$t(`overview.inventoryRow.${row.key}`)}</span>
                {/if}
                <span class="num-cell">{formatNumber(row.count)}</span>
              </li>
            {/each}
          </ul>
        </div>
      {/each}
    </div>
  </section>
{/if}

<style>
  .inventory-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
    gap: 16px;
  }

  .inventory-group h3 {
    font-size: 0.8rem;
    text-transform: uppercase;
    letter-spacing: 0.05em;
    opacity: 0.7;
    margin: 0 0 6px;
  }

  .inventory-group ul {
    list-style: none;
    margin: 0;
    padding: 0;
  }

  .inventory-group li {
    display: flex;
    justify-content: space-between;
    gap: 12px;
    padding: 3px 0;
    font-size: 0.88rem;
  }

  /* A zero is the signal, not noise: a subsystem with no rows is unseeded or unreachable. */
  .inventory-group li.empty {
    opacity: 0.55;
  }

  /* Numeric columns line up on the condensed counter face, like the rest of the dashboard. */
  .num-cell {
    font-family: var(--numerals);
    font-variant-numeric: tabular-nums;
  }
</style>
