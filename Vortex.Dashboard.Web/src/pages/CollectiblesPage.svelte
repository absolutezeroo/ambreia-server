<script lang="ts">
  // The Collectors Guild, whole: collections, the emerald shop, what can be converted into a Relic,
  // the stamp bundles that pay for it, the Relics themselves, the prizes waiting to be claimed, the
  // wearable avatars, and the standings.
  //
  // This file is the shell and nothing else — it loads the payload, draws the totals and the tab
  // bar, and hands each tab its slice. Each tab owns its own table, its own editor drawer and its
  // own picker, in `pages/collectibles/`.
  //
  // It used to be all eight in one 1,947-line file, and that is what hid the bug this split came out
  // of: every submit button on the page sat outside its own `<form>` — Drawer pins `actions` to the
  // panel footer, outside `children` — so not one editor could save, and nothing said so. No error,
  // no console line, just a button that did nothing. Forty lines apart in a short file that is
  // obvious; a thousand lines apart it is invisible.
  import { onMount } from 'svelte';
  import { apiGet } from '../lib/api';
  import type { CollectiblesOverview } from '../lib/apiTypes';
  import { createWriteOps } from '../lib/writeOps';
  import { hasDashboardCapability, isPermissionDeniedError } from '../lib/permissions';
  import { CAPABILITIES } from '../lib/dashboardPermissions';
  import { identity } from '../lib/session';
  import AccessDeniedNotice from '../components/AccessDeniedNotice.svelte';
  import ConfirmReasonModal from '../components/ConfirmReasonModal.svelte';
  import LoadingOverlay from '../components/LoadingOverlay.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import StatCard from '../components/StatCard.svelte';
  import Tabs from '../components/Tabs.svelte';
  import { formatNumber } from '../lib/format';
  import { t } from '../lib/i18n';
  import { Gem, Boxes, TriangleAlert, Trophy, Store, Gift, Hammer, Ticket, Sparkles, Shirt } from '@lucide/svelte';

  import CollectionsTab from './collectibles/CollectionsTab.svelte';
  import ShopTab from './collectibles/ShopTab.svelte';
  import MintingTab from './collectibles/MintingTab.svelte';
  import StampsTab from './collectibles/StampsTab.svelte';
  import RelicsTab from './collectibles/RelicsTab.svelte';
  import RewardsTab from './collectibles/RewardsTab.svelte';
  import AvatarsTab from './collectibles/AvatarsTab.svelte';
  import CollectorsTab from './collectibles/CollectorsTab.svelte';

  // Eight jobs on one page, and stacking them meant scrolling past seven to reach the eighth.
  let tab = $state('collections');

  let loading = $state(false);
  let forbidden = $state(false);
  let error = $state('');
  let data = $state<CollectiblesOverview | null>(null);

  const ops = createWriteOps(refresh);

  let canManage = $derived(hasDashboardCapability($identity, CAPABILITIES.opsContentManage));

  async function refresh() {
    loading = true;
    error = '';
    forbidden = false;

    try {
      data = await apiGet<CollectiblesOverview>('/api/v1/collectibles');
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
  <PageHeader title={$t('collectibles.title')} description={$t('collectibles.description')}>
    {#snippet actions()}
      <button type="button" onclick={refresh} disabled={loading} class="warning">{$t('common.refresh')}</button>
    {/snippet}
  </PageHeader>

  {#if forbidden}
    <AccessDeniedNotice message={$t('collectibles.accessDenied')} />
  {:else if error}
    <p class="empty-state danger" role="alert">{error}</p>
  {/if}
</section>

{#if data}
  <div class="metric-grid" style="margin-top: 12px;">
    <StatCard label={$t('collectibles.collections')} value={formatNumber(data.totals.collections)}>
      {#snippet icon()}
        <Gem size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard label={$t('collectibles.items')} value={formatNumber(data.totals.items)}>
      {#snippet icon()}
        <Boxes size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard label={$t('collectibles.completable')} value={formatNumber(data.totals.completableCollections)}>
      {#snippet icon()}
        <Gem size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard label={$t('collectibles.unresolved')} value={formatNumber(data.totals.unresolvedItems)}>
      {#snippet icon()}
        <TriangleAlert size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard label={$t('collectibles.trackedPlayers')} value={formatNumber(data.totals.trackedPlayers)}>
      {#snippet icon()}
        <Trophy size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard label={$t('collectibles.mintableOpen')} value={formatNumber(data.totals.mintableTypesOpen)}>
      {#snippet icon()}
        <Hammer size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard label={$t('collectibles.mintedRelics')} value={formatNumber(data.totals.mintedRelics)}>
      {#snippet icon()}
        <Sparkles size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard label={$t('collectibles.stampsHeld')} value={formatNumber(data.totals.stampsHeld)}>
      {#snippet icon()}
        <Ticket size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
  </div>

  <Tabs
    bind:active={tab}
    storageKey="collectibles"
    tabs={[
      { id: 'collections', label: $t('collectibles.tabCollections'), icon: Gem, count: (data.collections || []).length },
      { id: 'shop', label: $t('collectibles.tabShop'), icon: Store, count: (data.storeOffers || []).length },
      { id: 'minting', label: $t('collectibles.tabMinting'), icon: Hammer, count: (data.mintableTypes || []).length },
      { id: 'stamps', label: $t('collectibles.tabStamps'), icon: Ticket, count: (data.tokenOffers || []).length },
      { id: 'relics', label: $t('collectibles.tabRelics'), icon: Sparkles, count: (data.assets || []).length },
      { id: 'claims', label: $t('collectibles.tabClaims'), icon: Gift, count: (data.claims || []).length },
      { id: 'avatars', label: $t('collectibles.tabAvatars'), icon: Shirt, count: (data.nftAvatars || []).length },
      { id: 'collectors', label: $t('collectibles.tabCollectors'), icon: Trophy, count: (data.topCollectors || []).length },
    ]}
  />

  {#if tab === 'collections'}
    <CollectionsTab {data} {canManage} {ops} />
  {:else if tab === 'shop'}
    <ShopTab {data} {canManage} {ops} />
  {:else if tab === 'minting'}
    <MintingTab {data} {canManage} {ops} />
  {:else if tab === 'stamps'}
    <StampsTab {data} {canManage} {ops} />
  {:else if tab === 'relics'}
    <RelicsTab {data} {canManage} {ops} />
  {:else if tab === 'claims'}
    <RewardsTab {data} {canManage} {ops} />
  {:else if tab === 'avatars'}
    <AvatarsTab {data} {canManage} {ops} />
  {:else if tab === 'collectors'}
    <CollectorsTab {data} {canManage} {ops} />
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
