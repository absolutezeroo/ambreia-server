<script lang="ts">
  import { onMount } from 'svelte';
  import { apiGet } from '../lib/api';
  import { formatDate, formatNumber } from '../lib/format';
  import LineChart from '../components/LineChart.svelte';
  import EntityLink from '../components/EntityLink.svelte';
  import StatCard from '../components/StatCard.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import { Sparkles, Timer } from '@lucide/svelte';
  import { isPermissionDeniedError } from '../lib/permissions';
  import { openPlayer, openItem } from '../lib/session';
  import TableFilter from '../components/TableFilter.svelte';
  import { filterRows } from '../lib/tableView';
  import { t, translate } from '../lib/i18n';
  import type { Memberships } from '../lib/apiTypes';

  let membershipStats = $state<Memberships | null>(null);
  let membershipError = $state('');

  let byType = $derived(membershipStats?.byType || []);
  let lifecycle = $derived(membershipStats?.lifecycle?.timeline || []);
  let byMonths = $derived(membershipStats?.lifecycle?.byMonths || []);
  let recentEvents = $derived(membershipStats?.lifecycle?.recentEvents || []);
  let eventQuery = $state('');
  let eventView = $derived(filterRows(recentEvents, eventQuery));
  let topExpiring = $derived(membershipStats?.topExpiring || []);
  let byTypeScale = $derived(Math.max(1, ...byType.map((row) => Number(row.total || 0))));
  // One series per measure, sharing LineChart's x-axis and y-scale -- which is what these three
  // are: the same timeline, measured three ways. This page was the only one on the dashboard
  // drawing its own bars instead of using the shared chart primitive that ten others use, and
  // that is what made it read as a draft next to them.
  let lifecycleSeries = $derived([
    {
      key: 'purchases' as const,
      name: $t('subscriptions.purchases'),
      color: 'rgb(var(--accent-rgb))',
    },
    { key: 'renewals' as const, name: $t('subscriptions.renewals'), color: 'rgb(var(--ok-rgb))' },
    {
      key: 'expired' as const,
      name: $t('subscriptions.expirations'),
      color: 'rgb(var(--danger-rgb))',
    },
  ].map((serie) => ({
    name: serie.name,
    color: serie.color,
    points: lifecycle.map((point) => ({ label: point.label, value: point[serie.key] || 0 })),
  })));

  let byMonthsScale = $derived(Math.max(
    1,
    ...byMonths.map((point) => Math.max(point.total || 0, point.purchases || 0, point.renewals || 0)),
  ));
  let activeRate = $derived(Number(membershipStats?.totals?.activeRate || 0));

  async function refresh() {
    membershipError = '';

    try {
      membershipStats = await apiGet<Memberships>('/api/v1/economy/subscriptions');
    } catch (err) {
      if (isPermissionDeniedError(err)) {
        membershipError = translate('subscriptions.accessDenied');
      } else {
        membershipError = (err as Error).message;
      }

      membershipStats = null;
    }
  }

  onMount(refresh);
</script>

<section class="panel">
  <PageHeader title={$t('subscriptions.title')} description={$t('subscriptions.description')}>
    {#snippet actions()}
      <button type="button" onclick={refresh} class="warning">{$t('common.refresh')}</button>
    {/snippet}
  </PageHeader>
</section>

{#if membershipError}
  <p class="empty-state danger" role="alert">{membershipError}</p>
{/if}

<!-- The page reads top to bottom as one argument: how many subscriptions there are, how they
     are moving, how they break down, then the rows behind it. What was here instead was two
     half-width panels side by side, each doing several of those jobs at once -- which is why a
     trend chart ended up 540px wide next to a donut showing 0%. -->
{#if membershipError}
  <p class="empty-state danger" role="alert">{membershipError}</p>
{:else if !membershipStats}
  <p class="empty-state">{$t('subscriptions.noSubData')}</p>
{:else}
  <div class="metric-grid" style="margin-top: 12px;">
    <StatCard label={$t('subscriptions.totalSubs')} value={membershipStats?.totals?.totalSubscriptions || 0} sub={$t('subscriptions.inDatabase')} accent>
      {#snippet icon()}
        <Sparkles size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>

    <!-- The active rate was a conic-gradient donut a quarter of a panel wide, grey and empty at
         0%. A card states the same number and the two counts behind it in a line. -->
    <StatCard label={$t('subscriptions.activeRate')} sub={$t('subscriptions.activeOfTotal', { active: membershipStats?.totals?.activeSubscriptions || 0, total: membershipStats?.totals?.totalSubscriptions || 0 })} accent>
      {#snippet icon()}
        <Sparkles size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
      {#snippet value()}
        <span>{formatNumber(activeRate * 100, 2)}%</span>
      {/snippet}
    </StatCard>

    <StatCard label={$t('subscriptions.expiring7d')} value={membershipStats?.totals?.expiringIn7Days || 0} sub={$t('subscriptions.priorityRenewal')} accent>
      {#snippet icon()}
        <Timer size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>

    <StatCard label={$t('subscriptions.expiring30d')} value={membershipStats?.totals?.expiringIn30Days || 0} sub={$t('subscriptions.window30d')} accent>
      {#snippet icon()}
        <Timer size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>

    <StatCard label={$t('subscriptions.renewalRate')} sub={$t('subscriptions.actionsCount', { renewals: membershipStats?.lifecycle?.totals?.renewals || 0, purchases: membershipStats?.lifecycle?.totals?.purchases || 0 })} accent>
      {#snippet icon()}
        <Sparkles size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
      {#snippet value()}
        <span>{formatNumber((membershipStats?.lifecycle?.totals?.renewalShare || 0) * 100, 2)}%</span>
      {/snippet}
    </StatCard>
  </div>

  <!-- The trend, at the width a trend needs. Its three totals sit on the same line as the title
       rather than as a second row of cards under the five above: they are the legend's numbers,
       not five more KPIs. -->
  <section class="panel" style="margin-top: 12px;">
    <div class="panel-head">
      <h2>{$t('subscriptions.lifecycleTitle')}</h2>
      <span class="head-totals">
        <span class="head-total"><em class="dot dot--purchases"></em>{$t('subscriptions.purchases')} <b>{formatNumber(membershipStats?.lifecycle?.totals?.purchases || 0)}</b></span>
        <span class="head-total"><em class="dot dot--renewals"></em>{$t('subscriptions.renewals')} <b>{formatNumber(membershipStats?.lifecycle?.totals?.renewals || 0)}</b></span>
        <span class="head-total"><em class="dot dot--expired"></em>{$t('subscriptions.expirations')} <b>{formatNumber(membershipStats?.lifecycle?.totals?.expired || 0)}</b></span>
      </span>
    </div>
    <p class="muted">
      {$t('subscriptions.window', {
        since: formatDate(membershipStats?.window?.since),
        until: formatDate(membershipStats?.window?.until),
      })}
    </p>

    <LineChart
      series={lifecycleSeries}
      valueFormatter={(v) => formatNumber(v)}
      emptyMessage={$t('subscriptions.noPoints')}
      legend={false}
    />
  </section>

  <!-- Two categorical breakdowns, paired because that is what they are: a short list of
       labelled totals each. Neither justifies a full-width panel on its own. -->
  <section class="split-grid" style="margin-top: 12px;">
    <section class="panel">
      <div class="panel-head"><h2>{$t('subscriptions.byType')}</h2></div>
      <div class="bar-chart">
        {#each byType as row}
          <div class="bar-row">
            <div class="bar-label">{row.type}</div>
            <div class="bar-track">
              <div
                class="bar-fill"
                style={`width: ${byTypeScale > 0 ? (Number(row.total) / byTypeScale) * 100 : 0}%;`}
              ></div>
            </div>
            <span class="muted"
              >{$t('subscriptions.activeOfTotalDays', { active: row.active, total: row.total, days: formatNumber(row.averageRemainingDays, 2) })}</span>
          </div>
        {:else}
          <p class="muted">{$t('subscriptions.noTypeBreakdown')}</p>
        {/each}
      </div>
    </section>

    <section class="panel">
      <div class="panel-head"><h2>{$t('subscriptions.subscriptionDuration')}</h2></div>
      <div class="bar-chart">
        {#each byMonths as row}
          <div class="bar-row">
            <div class="bar-label">
              {row.months ? `${row.months} ${$t('subscriptions.months')}` : $t('subscriptions.unknown')}
            </div>
            <div class="bar-track">
              <div
                class="bar-fill bar-fill--duration"
                style={`width: ${byMonthsScale > 0 ? (row.total / byMonthsScale) * 100 : 0}%;`}
              ></div>
            </div>
            <span class="muted">{row.total}</span>
          </div>
        {:else}
          <p class="muted">{$t('subscriptions.noMonthBreakdown')}</p>
        {/each}
      </div>
    </section>
  </section>
{/if}

<section class="panel" style="margin-top: 12px;">
  <div class="panel-head"><h2>{$t('subscriptions.recentEvents')}</h2></div>
  {#if membershipStats}
    <TableFilter bind:query={eventQuery} shown={eventView.length} total={recentEvents.length} />
    <table>
      <thead>
        <tr>
          <th>{$t('subscriptions.colWhen')}</th>
          <th>{$t('subscriptions.colAction')}</th>
          <th>{$t('subscriptions.colActor')}</th>
          <th>{$t('subscriptions.colMonths')}</th>
          <th>{$t('subscriptions.colTotalMonths')}</th>
          <th>{$t('subscriptions.colRenewal')}</th>
          <th>{$t('subscriptions.colVip')}</th>
          <th>{$t('subscriptions.colCredit')}</th>
        </tr>
      </thead>
      <tbody>
        {#each eventView as row}
          <tr>
            <td>{formatDate(row.occurredAt)}</td>
            <td>{row.action}</td>
            <td>{row.actorPlayerName || `#${row.actorPlayerId}`}</td>
            <td>{row.months || '-'}</td>
            <td>{row.totalMonths || '-'}</td>
            <td>{row.isRenewal === true ? $t('subscriptions.yes') : row.isRenewal === false ? $t('subscriptions.no') : '-'}</td>
            <td>{row.isVip === true ? $t('subscriptions.yes') : row.isVip === false ? $t('subscriptions.no') : '-'}</td>
            <td>{row.creditCost || '-'}</td>
          </tr>
        {:else}
          <tr>
            <td colspan="8" class="muted">{$t('subscriptions.noRecentFlow')}</td>
          </tr>
        {/each}
      </tbody>
    </table>
  {/if}
</section>

<section class="panel" style="margin-top: 16px;">
  <div class="panel-head">
    <h2>{$t('subscriptions.upcomingExpirations')}</h2>
  </div>

  {#if !membershipStats || topExpiring.length === 0}
    <p class="muted">{$t('subscriptions.noUpcoming')}</p>
  {:else}
    <table>
      <thead>
        <tr>
          <th>{$t('subscriptions.colPlayer')}</th>
          <th>{$t('subscriptions.colType')}</th>
          <th>{$t('subscriptions.colLevel')}</th>
          <th>{$t('subscriptions.colExpires')}</th>
          <th>{$t('subscriptions.colDaysRemaining')}</th>
          <th>{$t('subscriptions.colTotalMonths')}</th>
        </tr>
      </thead>
      <tbody>
        {#each topExpiring as row}
          <tr>
            <td>
              <EntityLink id={row.playerId} label={row.playerName || `player #${row.playerId}`} {openPlayer} {openItem} />
            </td>
            <td>{row.type}</td>
            <td>{row.level}</td>
            <td>{formatDate(row.expiresAt)}</td>
            <td>{formatNumber(row.remainingDays, 1)}</td>
            <td>{row.totalMonths}</td>
          </tr>
        {:else}
          <tr>
            <td colspan="6" class="muted">{$t('subscriptions.noDataToRefresh')}</td>
          </tr>
        {/each}
      </tbody>
    </table>
  {/if}
</section>
