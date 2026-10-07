<script lang="ts">
  // The economy's smaller tables that have no surface of their own: today the currency catalogue,
  // with how much of each is held.
  import { onMount } from 'svelte';
  import { apiGet } from '../lib/api';
  import { createWriteOps } from '../lib/writeOps';
  import { hasDashboardCapability } from '../lib/permissions';
  import { CAPABILITIES } from '../lib/dashboardPermissions';
  import { identity } from '../lib/session';
  import ConfirmReasonModal from '../components/ConfirmReasonModal.svelte';
  import LoadingOverlay from '../components/LoadingOverlay.svelte';
  import OpResult from '../components/OpResult.svelte';
  import { formatNumber } from '../lib/format';
  import { isPermissionDeniedError } from '../lib/permissions';
  import AccessDeniedNotice from '../components/AccessDeniedNotice.svelte';
  import Drawer from '../components/Drawer.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import StatCard from '../components/StatCard.svelte';
  import { Coins } from '@lucide/svelte';
  import { t } from '../lib/i18n';
  import type { EconomyExtras } from '../lib/apiTypes';

  /**
   * The currency editor's form. Every number binds to a <input type="number">, which hands back a
   * string while it is being typed and a number once it is not -- hence both, and the Number()
   * on every read below.
   */
  type CurrencyForm = {
    id: number;
    name: string;
    currencyType: number | string;
    activityPointType: number | string | null;
    enabled: boolean;
    startingAmount: number | string;
  };


  let loading = $state(false);
  let forbidden = $state(false);
  let error = $state('');
  let data = $state<EconomyExtras | null>(null);


  const ops = createWriteOps(refresh);


  let canManage = $derived(hasDashboardCapability($identity, CAPABILITIES.opsContentManage));

  const emptyCurrency = (): CurrencyForm => ({
    id: 0,
    name: '',
    currencyType: 0,
    activityPointType: null,
    enabled: true,
    startingAmount: 0,
  });

  let currencyForm = $state<CurrencyForm | null>(null);


  async function refresh() {
    loading = true;
    error = '';
    forbidden = false;

    try {
      data = await apiGet<EconomyExtras>('/api/v1/economy/extras');
    } catch (err) {
      if (isPermissionDeniedError(err)) {
        forbidden = true;
        data = null;
        return;
      }

      error = (err as Error).message;
      data = null;
    } finally {
      loading = false;
    }
  }

  onMount(() => {
    void refresh();
  });
</script>

<section class="panel">
  <PageHeader title={$t('economyExtras.title')} description={$t('economyExtras.description')}>
    {#snippet actions()}
      <button type="button" onclick={refresh} disabled={loading} class="warning">{$t('common.refresh')}</button>
    {/snippet}
  </PageHeader>

  {#if forbidden}
    <AccessDeniedNotice message={$t('economyExtras.accessDenied')} />
  {:else if error}
    <p class="empty-state danger" role="alert">{error}</p>
  {/if}
</section>

{#if data}
  <div class="metric-grid" style="margin-top: 12px;">
    <StatCard label={$t('economyExtras.currencies')} value={formatNumber(data.totals.currencies)}>
      {#snippet icon()}
        <Coins size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
  </div>

  <section class="panel" style="margin-top: 12px;">
    <div class="panel-head">
      <h2>{$t('economyExtras.currenciesTitle')}</h2>
      {#if canManage}
        <button type="button" class="success" onclick={() => (currencyForm = emptyCurrency())}>{$t('economyExtras.newCurrency')}</button>
      {/if}
    </div>
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>{$t('economyExtras.colCurrency')}</th>
            <th>{$t('economyExtras.colType')}</th>
            <th>{$t('economyExtras.colPointType')}</th>
            <th>{$t('economyExtras.colEnabled')}</th>
            <th>{$t('economyExtras.colStarting')}</th>
            <th>{$t('economyExtras.colWallets')}</th>
            <th>{$t('economyExtras.colHeld')}</th>
            {#if canManage}<th>{$t('common.actions')}</th>{/if}
          </tr>
        </thead>
        <tbody>
          {#each data.currencies || [] as row}
            <tr>
              <td>{row.name || `#${row.id}`}</td>
              <td>{row.currencyType}</td>
              <td>{row.activityPointType ?? '—'}</td>
              <td>{row.enabled ? $t('common.yes') : $t('common.no')}</td>
              <td>{formatNumber(row.startingAmount)}</td>
              <td>{formatNumber(row.walletRows)}</td>
              <td>{formatNumber(row.totalHeld)}</td>
              {#if canManage}
                <td class="row-actions">
                  <button type="button" class="ghost-button" onclick={() => (currencyForm = { ...row, name: row.name ?? '' })}>
                    {$t('economyExtras.edit')}
                  </button>
                </td>
              {/if}
            </tr>
          {:else}
            <tr><td colspan="7" class="muted">{$t('economyExtras.noCurrencies')}</td></tr>
          {/each}
        </tbody>
      </table>
    </div>
  </section>

{#if canManage && currencyForm}
  {@const form = currencyForm}
  <Drawer title={form.id ? $t('economyExtras.updateCurrency') : $t('economyExtras.addCurrency')} eyebrow={$t('economyExtras.currenciesTitle')} onclose={() => (currencyForm = null)}>
        <form id="currency-form"
          class="inline-form"
          onsubmit={(event) => {
            event.preventDefault();
            ops.ask(
              '/api/v1/operations/content/currencies',
              {
                currencyId: Number(form.id) || 0,
                name: form.name,
                currencyType: Number(form.currencyType) || 0,
                activityPointType:
                  form.activityPointType === null || form.activityPointType === ''
                    ? null
                    : Number(form.activityPointType),
                enabled: Boolean(form.enabled),
                startingAmount: Number(form.startingAmount) || 0,
              },
              form.id ? $t('economyExtras.updateCurrency') : $t('economyExtras.addCurrency'),
              $t('economyExtras.saveCurrencySummary', { name: form.name })
            );
          }}
        >
          <label>
            {$t('economyExtras.colCurrency')}
            <input autocomplete="off" spellcheck="false" bind:value={form.name} />
          </label>
          <label>
            {$t('economyExtras.colType')}
            <input autocomplete="off" spellcheck="false" type="number" bind:value={form.currencyType} min="0" />
          </label>
          <label>
            {$t('economyExtras.colPointType')}
            <input autocomplete="off" spellcheck="false" type="number" bind:value={form.activityPointType} />
          </label>
          <label>
            {$t('economyExtras.colStarting')}
            <input autocomplete="off" spellcheck="false" type="number" bind:value={form.startingAmount} min="0" />
          </label>
          <label class="check">
            <input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={form.enabled} />
            {$t('economyExtras.colEnabled')}
          </label>
          <button class="success" type="submit" disabled={!form.name.trim()}>
            {form.id ? $t('economyExtras.updateCurrency') : $t('economyExtras.addCurrency')}
          </button>
        </form>

    {#if $ops.error}<p class="empty-state danger" role="alert">{$ops.error}</p>{/if}
    {#if $ops.result}<OpResult result={$ops.result} />{/if}

    {#snippet actions()}
      <button class="success" type="submit" form="currency-form" disabled={!form.name.trim()}>{form.id ? $t('economyExtras.updateCurrency') : $t('economyExtras.addCurrency')}</button>
      <button type="button" class="ghost-button" onclick={() => (currencyForm = null)}>{$t('common.cancel')}</button>
    {/snippet}
  </Drawer>
{/if}

{/if}

<ConfirmReasonModal
  open={Boolean($ops.pending)}
  title={$ops.pending?.title ?? ''}
  changes={$ops.pending?.changes ?? []}
  noteOnly={$ops.pending?.noteOnly ?? false}
  summary={$ops.pending?.summary ?? ''}
  confirmLabel={$ops.pending?.title ?? $t('common.confirm')}
  busy={$ops.busy}
  error={$ops.error}
  onconfirm={ops.confirm}
  oncancel={() => ops.cancel()}
/>

<LoadingOverlay show={loading} />

<style>
  .inline-form label.check {
    flex-direction: row;
    align-items: center;
    gap: 6px;
  }

  .cell {
    display: inline-flex;
    align-items: center;
    gap: 8px;
  }
</style>
