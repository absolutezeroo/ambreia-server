<script lang="ts">
  // NFT collections, their items and the collector scores. The one thing worth flagging: an item
  // whose product code matches no furniture definition makes its collection impossible to complete,
  // and nothing else in the hotel would ever say so.
  import { onMount } from 'svelte';
  import { apiGet } from '../lib/api';
  import type {
    CollectiblesOverview,
    MintableTypeRow,
    NftAvatarRow,
  } from '../lib/apiTypes';
  import type { PickerRow } from '../lib/pickers/directories';

  /** One choice of a fixed vocabulary the page draws as a select. */
  type Choice<T> = { value: T; key: string };

  /**
   * The seven editors' forms.
   *
   * A form field is not the column it saves to: every number binds to an input that hands back a
   * string while it is being typed, and every date binds to a datetime-local, which speaks local
   * time with no zone. Hence `number | string` throughout and the Number()/isoLocal() on each read.
   */
  type Num = number | string;

  type CollectionForm = {
    id: number;
    collectionCode: string;
    name: string;
    boostScore: Num;
    status: Num;
    rewardProductCode: string;
    bonusProductCode: string;
  };

  type ItemForm = {
    id: number;
    productCode: string;
    itemTypeId: string;
    productTypeId: Num;
    score: Num;
    rarity: string;
    sortOrder: Num;
  };

  type OfferForm = {
    id: number;
    productCode: string;
    emeraldPrice: Num;
    isFeatured: boolean;
    isLimited: boolean;
    mintLimit: Num;
    itemTypeId: string;
    productTypeId: Num;
    score: Num;
    rarity: string;
    enabled: boolean;
    sortOrder: Num;
  };

  type ClaimForm = {
    playerId: Num;
    playerName: string;
    productCode: string;
    setId: string;
    defaultCollectionName: string;
    collection: string;
    claimLimit: Num;
    validTo: string;
  };

  type MintableForm = {
    id: number;
    productCode: string;
    stampPrice: Num;
    startsAt: string;
    endsAt: string;
    regionLocked: boolean;
    limitedEdition: boolean;
    editionSize: Num;
    enabled: boolean;
    sortOrder: Num;
  };

  type TokenOfferForm = {
    id: number;
    productCode: string;
    silverPrice: Num;
    amountTokens: Num;
    enabled: boolean;
    sortOrder: Num;
  };

  type AvatarForm = {
    id: number;
    avatarCode: string;
    name: string;
    figure: string;
    gender: string;
    contractKey: string;
    editionSize: Num;
    enabled: boolean;
    sortOrder: Num;
  };

  /** Who is being given a copy of an avatar, and what for. */
  type GrantForm = { playerId: Num; playerName: string; note: string };

  import { createWriteOps } from '../lib/writeOps';
  import { hasDashboardCapability } from '../lib/permissions';
  import { CAPABILITIES } from '../lib/dashboardPermissions';
  import { identity } from '../lib/session';
  import ConfirmReasonModal from '../components/ConfirmReasonModal.svelte';
  import LoadingOverlay from '../components/LoadingOverlay.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import OpResult from '../components/OpResult.svelte';

  import { formatNumber, formatDate } from '../lib/format';
  import { CURRENCY_KIND, currencyChipClass } from '../lib/currency';
  import CurrencyIcon from '../components/CurrencyIcon.svelte';
  import { isPermissionDeniedError } from '../lib/permissions';
  import { openPlayer } from '../lib/session';
  import AccessDeniedNotice from '../components/AccessDeniedNotice.svelte';
  import Drawer from '../components/Drawer.svelte';
  import AssetImage from '../components/AssetImage.svelte';
  import EmptyState from '../components/EmptyState.svelte';
  import EntityLink from '../components/EntityLink.svelte';
  import PickerModal from '../components/PickerModal.svelte';
  import StatCard from '../components/StatCard.svelte';
  import Tabs from '../components/Tabs.svelte';
  import { Gem, Boxes, TriangleAlert, Trophy, Store, Gift, Hammer, Ticket, Sparkles, Shirt } from '@lucide/svelte';
  import { t } from '../lib/i18n';

  // Three jobs on one page -- the collections, the shop and the collector standings -- and stacking
  // them meant scrolling past two to reach the third.
  let tab = $state('collections');

  let loading = $state(false);
  let forbidden = $state(false);
  let error = $state('');
  let data = $state<CollectiblesOverview | null>(null);
  let expanded = $state<number | null>(null);

  const ops = createWriteOps(refresh);

  let canManage = $derived(hasDashboardCapability($identity, CAPABILITIES.opsContentManage));

  const emptyCollection = (): CollectionForm => ({
    id: 0,
    collectionCode: '',
    name: '',
    boostScore: 0,
    status: 0,
    rewardProductCode: '',
    bonusProductCode: '',
  });
  const emptyItem = (): ItemForm => ({
    id: 0,
    productCode: '',
    itemTypeId: '',
    productTypeId: 0,
    score: 1,
    rarity: '',
    sortOrder: 0,
  });

  let collectionForm = $state<CollectionForm | null>(null);
  let itemForm = $state<ItemForm | null>(null);

  // Which furniture picker is open, if any: the collection's two prizes and the collection item all
  // pick from the same catalogue, so they share one modal rather than three.
  let picking = $state<string | null>(null);

  // The icons of the two prizes, remembered from the pick. They are not in `data` like the item
  // icons are -- a prize is a classname on the collection, not a row in its item list -- so without
  // this the admin picks a chair and sees only the word.
  let rewardIconUrl = $state<string | null>(null);
  let bonusIconUrl = $state<string | null>(null);

  // Only the statuses mean something, and only on this side: the client parses the field and never
  // reads it, so Draft is a collection the server withholds rather than one the client hides.
  const STATUS_OPTIONS: Choice<number>[] = [
    { value: 0, key: 'collectibles.statusDraft' },
    { value: 1, key: 'collectibles.statusVisible' },
    { value: 2, key: 'collectibles.statusArchived' },
  ];

  // The client's own list, from its rarity colour table. Anything outside it still renders, but in
  // the default grey, so typing a rarity of one's own invention is how an item ends up looking
  // broken for no visible reason.
  const RARITY_OPTIONS = ['common', 'uncommon', 'rare', 'epic', 'legendary', 'legendary+'];

  const emptyOffer = (): OfferForm => ({
    id: 0,
    productCode: '',
    emeraldPrice: 0,
    isFeatured: false,
    isLimited: false,
    mintLimit: 0,
    itemTypeId: '',
    productTypeId: 0,
    score: 0,
    rarity: '',
    enabled: true,
    sortOrder: 0,
  });

  let offerForm = $state<OfferForm | null>(null);

  const emptyClaim = (): ClaimForm => ({
    playerId: '',
    playerName: '',
    productCode: '',
    setId: '',
    defaultCollectionName: '',
    collection: '',
    claimLimit: 1,
    validTo: '',
  });

  let claimForm = $state<ClaimForm | null>(null);
  let claimIconUrl = $state<string | null>(null);

  // A window is not optional: the client greys the convert button out once the end date has passed,
  // and says nothing about why. A new type therefore opens now and runs for a year rather than
  // starting empty and being saved unusable.
  const isoLocal = (date: Date) => new Date(date.getTime() - date.getTimezoneOffset() * 60000)
    .toISOString()
    .slice(0, 16);

  const emptyMintable = (): MintableForm => {
    const now = new Date();
    const inAYear = new Date(now.getTime() + 365 * 24 * 60 * 60 * 1000);

    return {
      id: 0,
      productCode: '',
      stampPrice: 1,
      startsAt: isoLocal(now),
      endsAt: isoLocal(inAYear),
      regionLocked: false,
      limitedEdition: false,
      editionSize: 0,
      enabled: true,
      sortOrder: 0,
    };
  };

  let mintableForm = $state<MintableForm | null>(null);

  const emptyTokenOffer = (): TokenOfferForm => ({
    id: 0,
    productCode: '',
    silverPrice: 10,
    amountTokens: 1,
    enabled: true,
    sortOrder: 0,
  });

  let tokenOfferForm = $state<TokenOfferForm | null>(null);

  // The three the client knows. It switches on this exact string to pick the caption and the tile
  // colours, and has no branch for anything else -- an unknown one prints the word "null" above the
  // avatar, so the field is a list rather than a text box.
  const COLLECTION_OPTIONS: Choice<string>[] = [
    { value: 'habbo:avatar', key: 'collectibles.collectionAvatar' },
    { value: 'habbo:clothes', key: 'collectibles.collectionClothes' },
    { value: 'habbo:avatar_genesis', key: 'collectibles.collectionGenesis' },
  ];

  const emptyAvatar = (): AvatarForm => ({
    id: 0,
    avatarCode: '',
    name: '',
    figure: '',
    gender: 'M',
    contractKey: 'habbo:avatar',
    editionSize: 0,
    enabled: true,
    sortOrder: 0,
  });

  let avatarForm = $state<AvatarForm | null>(null);

  // Who is being given a copy, and what for. Kept beside the avatar it belongs to rather than in one
  // form at the bottom: the note is about this avatar, and an admin handing out prizes at an event
  // is looking at the row, not at a form.
  let grantAvatar = $state<NftAvatarRow | null>(null);
  let grantForm = $state<GrantForm | null>(null);

  const startGrant = (avatar: NftAvatarRow) => {
    grantAvatar = avatar;
    grantForm = { playerId: '', playerName: '', note: '' };
  };

  const editAvatar = (row: NftAvatarRow) => {
    avatarForm = { ...row };
  };

  let mintableIconUrl =
    $derived((data?.mintableTypes || []).find((t) => t.productCode === mintableForm?.productCode)?.iconUrl ??
    null);

  // The editors bind to datetime-local inputs, which speak local time with no zone; the API takes
  // instants. A row loaded for editing therefore has to come back the other way round.
  const editMintable = (row: MintableTypeRow) => {
    mintableForm = {
      ...row,
      startsAt: isoLocal(new Date(row.startsAt)),
      endsAt: isoLocal(new Date(row.endsAt)),
    };
  };

  // The shop's own icon, looked up in the listing: unlike the two prizes, an offer is a row, so its
  // image is already on the page once it has been saved.
  let offerIconUrl =
    $derived((data?.storeOffers || []).find((o) => o.productCode === offerForm?.productCode)?.iconUrl ?? null);

  const statusLabel = (value: number | string) =>
    $t(STATUS_OPTIONS.find((o) => o.value === Number(value))?.key ?? 'collectibles.statusUnknown');

  // The product code is a furniture classname, so it is picked from the real catalogue rather than
  // typed: a code that matches nothing is exactly what makes a collection uncompletable.
  let itemPreviewUrl =
    $derived((data?.collections || [])
      .flatMap((c) => c.items || [])
      .find((i) => i.productCode === itemForm?.productCode)?.iconUrl ?? null);

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
  <section class="panel" style="margin-top: 12px;">
    <div class="panel-head">
      <h2>{$t('collectibles.collectionsTitle')}</h2>
      {#if canManage}
        <button type="button" class="success" onclick={() => (collectionForm = emptyCollection())}>{$t('collectibles.newCollection')}</button>
      {/if}
    </div>
    {#if (data.collections || []).length === 0}
      <EmptyState message={$t('collectibles.noCollections')} />
    {:else}
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>{$t('collectibles.colCollection')}</th>
              <th>{$t('collectibles.colCode')}</th>
              <th>{$t('collectibles.colItems')}</th>
              <th>{$t('collectibles.colScore')}</th>
              <th>{$t('collectibles.colBoost')}</th>
              <th>{$t('collectibles.colStatus')}</th>
              <th>{$t('collectibles.colReleased')}</th>
              {#if canManage}<th>{$t('common.actions')}</th>{/if}
            </tr>
          </thead>
          <tbody>
            {#each data.collections as collection}
              <tr
                onclick={() => (expanded = expanded === collection.id ? null : collection.id)}
                style="cursor: pointer;"
                class:selected={expanded === collection.id}
              >
                <td>{collection.name}</td>
                <td><code>{collection.collectionCode}</code></td>
                <td>
                  {formatNumber(collection.itemCount)}
                  {#if collection.unresolvedItems > 0}
                    <span class="status-badge status-badge--bad">
                      {$t('collectibles.unresolvedCount', { count: collection.unresolvedItems })}
                    </span>
                  {/if}
                </td>
                <td>{formatNumber(collection.totalScore)}</td>
                <td>{formatNumber(collection.boostScore)}</td>
                <td>
                  <span
                    class="status-badge"
                    class:status-badge--ok={Number(collection.status) === 1}
                  >
                    {statusLabel(collection.status)}
                  </span>
                </td>
                <td>{collection.releasedAt ? formatDate(collection.releasedAt) : '—'}</td>
                {#if canManage}
                  <td class="row-actions">
                    <button
                      type="button"
                      class="ghost-button"
                      onclick={(event) => {
                        event.stopPropagation();
                        collectionForm = {
                          id: collection.id,
                          collectionCode: collection.collectionCode,
                          name: collection.name,
                          boostScore: collection.boostScore,
                          status: collection.status,
                          rewardProductCode: collection.rewardProductCode ?? '',
                          bonusProductCode: collection.bonusProductCode ?? '',
                        };
                        // The prize icons are not in the listing, so they stay blank until the
                        // admin picks again rather than showing the previous collection's.
                        rewardIconUrl = null;
                        bonusIconUrl = null;
                      }}
                    >
                      {$t('collectibles.edit')}
                    </button>
                    <button
                      type="button"
                      class="ghost-button danger"
                      onclick={(event) => {
                        event.stopPropagation();
                        ops.ask(
                          '/api/v1/operations/content/collections/delete',
                          { collectionId: collection.id },
                          $t('collectibles.deleteCollection'),
                          $t('collectibles.deleteCollectionSummary', { name: collection.name })
                        );
                      }}
                    >
                      {$t('collectibles.delete')}
                    </button>
                  </td>
                {/if}
              </tr>
              {#if expanded === collection.id}
                <tr>
                  <td colspan={canManage ? 8 : 7}>
                    <div class="table-wrap">
                      <table>
                        <thead>
                          <tr>
                            <th>{$t('collectibles.colItem')}</th>
                            <th>{$t('collectibles.colRarity')}</th>
                            <th>{$t('collectibles.colItemScore')}</th>
                            <th>{$t('collectibles.colResolved')}</th>
                            {#if canManage}<th>{$t('common.actions')}</th>{/if}
                          </tr>
                        </thead>
                        <tbody>
                          {#each collection.items || [] as item}
                            <tr>
                              <td>
                                <span class="cell">
                                  <AssetImage src={item.iconUrl} alt={item.productCode} size={32} />
                                  <code>{item.productCode}</code>
                                </span>
                              </td>
                              <td>{item.rarity || '—'}</td>
                              <td>{formatNumber(item.score)}</td>
                              <td>
                                {#if item.resolved}
                                  <span class="status-badge status-badge--ok">{$t('collectibles.resolved')}</span>
                                {:else}
                                  <span class="status-badge status-badge--bad">{$t('collectibles.missingFurni')}</span>
                                {/if}
                              </td>
                              {#if canManage}
                                <td class="row-actions">
                                  <button type="button" class="ghost-button" onclick={() => (itemForm = { ...item })}>
                                    {$t('collectibles.edit')}
                                  </button>
                                  <button
                                    type="button"
                                    class="ghost-button danger"
                                    onclick={() =>
                                      ops.ask(
                                        '/api/v1/operations/content/collections/items/delete',
                                        { itemId: item.id },
                                        $t('collectibles.deleteItem'),
                                        $t('collectibles.deleteItemSummary', { code: item.productCode })
                                      )}
                                  >
                                    {$t('collectibles.delete')}
                                  </button>
                                </td>
                              {/if}
                            </tr>
                          {:else}
                            <tr><td colspan="4" class="muted">{$t('collectibles.noItems')}</td></tr>
                          {/each}
                        </tbody>
                      </table>
                    </div>
                  </td>
                </tr>
              {/if}
            {/each}
          </tbody>
        </table>
      </div>
    {/if}
  </section>

  {#if canManage}
    <section class="panel" style="margin-top: 12px;">
      <div class="panel-head"><h2>{$t('collectibles.editorTitle')}</h2></div>

      {#if expanded}
        <h3 class="subhead">{$t('collectibles.itemEditorTitle')}</h3>
      {:else}
        <p class="muted">{$t('collectibles.pickToEditItems')}</p>
      {/if}

      {#if $ops.result}
        <OpResult result={$ops.result} />
      {/if}
    </section>
  {/if}
  {/if}

  {#if tab === 'shop'}
  <section class="panel" style="margin-top: 12px;">
    <div class="panel-head">
      <h2>{$t('collectibles.shopTitle')}</h2>
      {#if canManage}
        <button type="button" class="success" onclick={() => (offerForm = emptyOffer())}>{$t('collectibles.newOffer')}</button>
      {/if}
    </div>
    <p class="muted">{$t('collectibles.shopDescription')}</p>
    {#if (data.storeOffers || []).length === 0}
      <EmptyState message={$t('collectibles.noOffers')} />
    {:else}
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>{$t('collectibles.colItem')}</th>
              <th>{$t('collectibles.colPrice')}</th>
              <th>{$t('collectibles.colItemScore')}</th>
              <th>{$t('collectibles.colRarity')}</th>
              <th>{$t('collectibles.colStock')}</th>
              <th>{$t('collectibles.colOnSale')}</th>
              {#if canManage}<th>{$t('common.actions')}</th>{/if}
            </tr>
          </thead>
          <tbody>
            {#each data.storeOffers as offer}
              <tr>
                <td>
                  <span class="cell">
                    <AssetImage src={offer.iconUrl} alt={offer.productCode} size={32} />
                    <code>{offer.productCode}</code>
                    {#if !offer.isNft}
                      <span class="status-badge status-badge--bad" title={$t('collectibles.notNftHelp')}>
                        {$t('collectibles.notNft')}
                      </span>
                    {/if}
                    {#if offer.isFeatured}
                      <span class="status-badge">{$t('collectibles.featured')}</span>
                    {/if}
                  </span>
                </td>
                <td>
                  <span class={currencyChipClass(CURRENCY_KIND.emeralds)}>
                    <CurrencyIcon kind={CURRENCY_KIND.emeralds} /> {formatNumber(offer.emeraldPrice)}
                  </span>
                </td>
                <td>{formatNumber(offer.score)}</td>
                <td>{offer.rarity || '—'}</td>
                <td>
                  {#if offer.mintLimit > 0}
                    {formatNumber(offer.soldCount)} / {formatNumber(offer.mintLimit)}
                  {:else}
                    {formatNumber(offer.soldCount)} / ∞
                  {/if}
                </td>
                <td>
                  {#if !offer.resolved}
                    <span class="status-badge status-badge--bad">{$t('collectibles.missingFurni')}</span>
                  {:else if !offer.enabled}
                    <span class="status-badge">{$t('collectibles.offerDisabled')}</span>
                  {:else if offer.soldOut}
                    <span class="status-badge status-badge--bad">{$t('collectibles.soldOut')}</span>
                  {:else}
                    <span class="status-badge status-badge--ok">{$t('collectibles.onSale')}</span>
                  {/if}
                </td>
                {#if canManage}
                  <td class="row-actions">
                    <button type="button" class="ghost-button" onclick={() => (offerForm = { ...offer })}>
                      {$t('collectibles.edit')}
                    </button>
                    <button
                      type="button"
                      class="ghost-button danger"
                      onclick={() =>
                        ops.ask(
                          '/api/v1/operations/content/store-offers/delete',
                          { offerId: offer.id },
                          $t('collectibles.deleteOffer'),
                          $t('collectibles.deleteOfferSummary', { code: offer.productCode })
                        )}
                    >
                      {$t('collectibles.delete')}
                    </button>
                  </td>
                {/if}
              </tr>
            {/each}
          </tbody>
        </table>
      </div>
    {/if}

    {#if canManage}
      <h3 class="subhead">{$t('collectibles.offerEditorTitle')}</h3>
    {/if}
  </section>
  {/if}

  {#if tab === 'minting'}
  <section class="panel" style="margin-top: 12px;">
    <div class="panel-head">
      <h2>{$t('collectibles.mintingTitle')}</h2>
      {#if canManage}
        <button type="button" class="success" onclick={() => (mintableForm = emptyMintable())}>{$t('collectibles.newMintable')}</button>
      {/if}
    </div>
    <p class="muted">{$t('collectibles.mintingDescription')}</p>
    {#if (data.mintableTypes || []).length === 0}
      <EmptyState message={$t('collectibles.noMintableTypes')} />
    {:else}
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>{$t('collectibles.colItem')}</th>
              <th>{$t('collectibles.colStampPrice')}</th>
              <th>{$t('collectibles.colEdition')}</th>
              <th>{$t('collectibles.colWindow')}</th>
              <th>{$t('collectibles.colOnSale')}</th>
              {#if canManage}<th>{$t('common.actions')}</th>{/if}
            </tr>
          </thead>
          <tbody>
            {#each data.mintableTypes as type}
              <tr>
                <td>
                  <span class="cell">
                    <AssetImage src={type.iconUrl} alt={type.productCode} size={32} />
                    <code>{type.productCode}</code>
                    {#if !type.isNft}
                      <span class="status-badge status-badge--bad" title={$t('collectibles.notNftHelp')}>
                        {$t('collectibles.notNft')}
                      </span>
                    {/if}
                    {#if type.limitedEdition}
                      <span class="status-badge">{$t('collectibles.limitedEdition')}</span>
                    {/if}
                  </span>
                </td>
                <td>
                  {#if type.editionSize > 0}
                    {formatNumber(type.mintedCount)} / {formatNumber(type.editionSize)}
                  {:else}
                    {formatNumber(type.mintedCount)} / ∞
                  {/if}
                </td>
                <td>{formatDate(type.startsAt)} → {formatDate(type.endsAt)}</td>
                <td>
                  {#if !type.resolved}
                    <span class="status-badge status-badge--bad">{$t('collectibles.missingFurni')}</span>
                  {:else if !type.enabled}
                    <span class="status-badge">{$t('collectibles.offerDisabled')}</span>
                  {:else if type.exhausted}
                    <span class="status-badge status-badge--bad">{$t('collectibles.editionGone')}</span>
                  {:else if type.expired}
                    <span class="status-badge status-badge--bad">{$t('collectibles.windowClosed')}</span>
                  {:else if !type.open}
                    <span class="status-badge">{$t('collectibles.windowNotOpen')}</span>
                  {:else}
                    <span class="status-badge status-badge--ok">{$t('collectibles.mintable')}</span>
                  {/if}
                </td>
                {#if canManage}
                  <td class="row-actions">
                    <button type="button" class="ghost-button" onclick={() => editMintable(type)}>
                      {$t('collectibles.edit')}
                    </button>
                    <button
                      type="button"
                      class="ghost-button danger"
                      onclick={() =>
                        ops.ask(
                          '/api/v1/operations/content/mintable-types/delete',
                          { typeId: type.id },
                          $t('collectibles.deleteMintable'),
                          $t('collectibles.deleteMintableSummary', { code: type.productCode })
                        )}
                    >
                      {$t('collectibles.delete')}
                    </button>
                  </td>
                {/if}
              </tr>
            {/each}
          </tbody>
        </table>
      </div>
    {/if}

    {#if canManage}
      <h3 class="subhead">{$t('collectibles.mintableEditorTitle')}</h3>
    {/if}
  </section>
  {/if}

  {#if tab === 'stamps'}
  <section class="panel" style="margin-top: 12px;">
    <div class="panel-head">
      <h2>{$t('collectibles.stampsTitle')}</h2>
      {#if canManage}
        <button type="button" class="success" onclick={() => (tokenOfferForm = emptyTokenOffer())}>{$t('collectibles.newTokenOffer')}</button>
      {/if}
    </div>
    <p class="muted">{$t('collectibles.stampsDescription')}</p>
    {#if (data.tokenOffers || []).length === 0}
      <EmptyState message={$t('collectibles.noTokenOffers')} />
    {:else}
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>{$t('collectibles.colBundle')}</th>
              <th>{$t('collectibles.colStamps')}</th>
              <th>{$t('collectibles.colSilverPrice')}</th>
              <th>{$t('collectibles.colOnSale')}</th>
              {#if canManage}<th>{$t('common.actions')}</th>{/if}
            </tr>
          </thead>
          <tbody>
            {#each data.tokenOffers as offer}
              <tr>
                <td><code>{offer.productCode}</code></td>
                <td>{formatNumber(offer.amountTokens)}</td>
                <td>
                  <span class={currencyChipClass(CURRENCY_KIND.silver)}>
                    <CurrencyIcon kind={CURRENCY_KIND.silver} /> {formatNumber(offer.silverPrice)}
                  </span>
                </td>
                <td>
                  {#if offer.enabled}
                    <span class="status-badge status-badge--ok">{$t('collectibles.onSale')}</span>
                  {:else}
                    <span class="status-badge">{$t('collectibles.offerDisabled')}</span>
                  {/if}
                </td>
                {#if canManage}
                  <td class="row-actions">
                    <button type="button" class="ghost-button" onclick={() => (tokenOfferForm = { ...offer })}>
                      {$t('collectibles.edit')}
                    </button>
                    <button
                      type="button"
                      class="ghost-button danger"
                      onclick={() =>
                        ops.ask(
                          '/api/v1/operations/content/mint-token-offers/delete',
                          { offerId: offer.id },
                          $t('collectibles.deleteTokenOffer'),
                          $t('collectibles.deleteTokenOfferSummary', { code: offer.productCode })
                        )}
                    >
                      {$t('collectibles.delete')}
                    </button>
                  </td>
                {/if}
              </tr>
            {/each}
          </tbody>
        </table>
      </div>
    {/if}

    {#if canManage}
      <h3 class="subhead">{$t('collectibles.tokenOfferEditorTitle')}</h3>
    {/if}
  </section>
  {/if}

  {#if tab === 'relics'}
  <section class="panel" style="margin-top: 12px;">
    <div class="panel-head"><h2>{$t('collectibles.relicsTitle')}</h2></div>
    <p class="muted">{$t('collectibles.relicsDescription')}</p>
    {#if (data.assets || []).length === 0}
      <EmptyState message={$t('collectibles.noRelics')} />
    {:else}
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>{$t('collectibles.colPlayer')}</th>
              <th>{$t('collectibles.colItem')}</th>
              <th>{$t('collectibles.colSerial')}</th>
              <th>{$t('collectibles.colStampPrice')}</th>
              <th>{$t('collectibles.colMintedAt')}</th>
              <th>{$t('collectibles.colHistory')}</th>
            </tr>
          </thead>
          <tbody>
            {#each data.assets as asset}
              <tr>
                <td>
                  <EntityLink type="player" id={asset.playerId} label={asset.playerName} {openPlayer} />
                </td>
                <td>
                  <span class="cell">
                    <AssetImage src={asset.iconUrl} alt={asset.productCode} size={32} />
                    <code>{asset.productCode}</code>
                  </span>
                </td>
                <td>
                  {#if asset.editionSize > 0}
                    #{formatNumber(asset.serialNumber)} / {formatNumber(asset.editionSize)}
                  {:else}
                    #{formatNumber(asset.serialNumber)}
                  {/if}
                </td>
                <td>{formatNumber(asset.stampCost)}</td>
                <td>{formatDate(asset.mintedAt)}</td>
                <td class="history">
                  {#each asset.history as move}
                    <div>
                      <span class="status-badge">{move.reason}</span>
                      {move.fromPlayer ?? '—'} → {move.toPlayer}
                      <span class="muted">{formatDate(move.at)}</span>
                    </div>
                  {/each}
                </td>
              </tr>
            {/each}
          </tbody>
        </table>
      </div>
    {/if}
  </section>
  {/if}

  {#if tab === 'claims'}
  <section class="panel" style="margin-top: 12px;">
    <div class="panel-head">
      <h2>{$t('collectibles.claimsTitle')}</h2>
      {#if canManage}
        <button type="button" class="success" onclick={() => (claimForm = emptyClaim())}>{$t('collectibles.newClaim')}</button>
      {/if}
    </div>
    <p class="muted">{$t('collectibles.claimsDescription')}</p>
    {#if (data.claims || []).length === 0}
      <EmptyState message={$t('collectibles.noClaims')} />
    {:else}
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>{$t('collectibles.colPlayer')}</th>
              <th>{$t('collectibles.colItem')}</th>
              <th>{$t('collectibles.colRemaining')}</th>
              <th>{$t('collectibles.colSet')}</th>
              <th>{$t('collectibles.colExpires')}</th>
              {#if canManage}<th>{$t('common.actions')}</th>{/if}
            </tr>
          </thead>
          <tbody>
            {#each data.claims as claim}
              <tr>
                <td><EntityLink type="player" id={claim.playerId} label={claim.playerName} {openPlayer} /></td>
                <td>
                  <span class="cell">
                    <AssetImage src={claim.iconUrl} alt={claim.productCode} size={32} />
                    <code>{claim.productCode}</code>
                    {#if !claim.isNft}
                      <span class="status-badge status-badge--bad" title={$t('collectibles.notNftHelp')}>
                        {$t('collectibles.notNft')}
                      </span>
                    {/if}
                  </span>
                </td>
                <td>{formatNumber(claim.remaining)} / {formatNumber(claim.claimLimit)}</td>
                <td>{claim.setId || '—'}</td>
                <td>{claim.validTo ? formatDate(claim.validTo) : '—'}</td>
                {#if canManage}
                  <td class="row-actions">
                    <button
                      type="button"
                      class="ghost-button danger"
                      onclick={() =>
                        ops.ask(
                          '/api/v1/operations/content/claims/delete',
                          { claimId: claim.id },
                          $t('collectibles.deleteClaim'),
                          $t('collectibles.deleteClaimSummary', { code: claim.productCode, name: claim.playerName })
                        )}
                    >
                      {$t('collectibles.delete')}
                    </button>
                  </td>
                {/if}
              </tr>
            {/each}
          </tbody>
        </table>
      </div>
    {/if}

    {#if canManage}
      <h3 class="subhead">{$t('collectibles.claimEditorTitle')}</h3>
    {/if}
  </section>
  {/if}

  {#if tab === 'avatars'}
  <section class="panel" style="margin-top: 12px;">
    <div class="panel-head">
      <h2>{$t('collectibles.avatarsTitle')}</h2>
      {#if canManage}
        <button type="button" class="success" onclick={() => (avatarForm = emptyAvatar())}>{$t('collectibles.newAvatar')}</button>
      {/if}
    </div>
    <p class="muted">{$t('collectibles.avatarsDescription')}</p>

    {#if (data.nftAvatars || []).length === 0}
      <EmptyState message={$t('collectibles.noAvatars')} />
    {:else}
      <div class="catalog-list catalog-list--cards">
        {#each data.nftAvatars as avatar}
          <article class="catalog-card avatar-card">
            <header class="avatar-card__head">
              <AssetImage src={avatar.avatarImageUrl} alt={avatar.avatarCode} size={64} />
              <div class="avatar-card__title">
                <strong>{avatar.name || avatar.avatarCode}</strong>
                <code>{avatar.avatarCode}</code>
                <span class="cell">
                  <span class="status-badge">{avatar.gender}</span>
                  {#if !avatar.enabled}
                    <span class="status-badge status-badge--bad">{$t('collectibles.avatarDisabled')}</span>
                  {/if}
                  {#if !avatar.knownCollection}
                    <span class="status-badge status-badge--bad" title={$t('collectibles.unknownCollectionHelp')}>
                      {$t('collectibles.unknownCollection')}
                    </span>
                  {/if}
                  {#if avatar.exhausted}
                    <span class="status-badge status-badge--warn">{$t('collectibles.editionExhausted')}</span>
                  {/if}
                </span>
              </div>
            </header>

            <dl class="avatar-card__meta">
              <div>
                <dt>{$t('collectibles.colCollection')}</dt>
                <dd><code>{avatar.contractKey}</code></dd>
              </div>
              <div>
                <dt>{$t('collectibles.colEdition')}</dt>
                <dd>
                  {formatNumber(avatar.grantedCount)}
                  {avatar.editionSize > 0 ? `/ ${formatNumber(avatar.editionSize)}` : `/ ${$t('collectibles.unlimited')}`}
                </dd>
              </div>
            </dl>

            {#if (avatar.holders || []).length > 0}
              <div class="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>{$t('collectibles.colSerial')}</th>
                      <th>{$t('collectibles.colPlayer')}</th>
                      <th>{$t('collectibles.colGrantNote')}</th>
                      <th>{$t('collectibles.colGrantedAt')}</th>
                      {#if canManage}<th>{$t('common.actions')}</th>{/if}
                    </tr>
                  </thead>
                  <tbody>
                    {#each avatar.holders as holder}
                      <tr>
                        <td>
                          <code>#{holder.id}</code>
                          <small class="muted">
                            {$t('collectibles.serialOf', {
                              serial: holder.serialNumber,
                              size: avatar.editionSize > 0 ? avatar.editionSize : $t('collectibles.unlimited'),
                            })}
                          </small>
                        </td>
                        <td>
                          <span class="cell">
                            <EntityLink type="player" id={holder.playerId} label={holder.playerName} {openPlayer} />
                            {#if holder.worn}
                              <span class="status-badge status-badge--ok">{$t('collectibles.wearing')}</span>
                            {/if}
                          </span>
                        </td>
                        <td>{holder.grantNote || '—'}</td>
                        <td>{formatDate(holder.grantedAt)}</td>
                        {#if canManage}
                          <td class="row-actions">
                            <button
                              type="button"
                              class="ghost-button danger"
                              onclick={() =>
                                ops.ask(
                                  '/api/v1/operations/content/nft-avatars/revoke',
                                  { copyId: holder.id },
                                  $t('collectibles.revokeAvatar'),
                                  $t('collectibles.revokeAvatarSummary', {
                                    code: avatar.avatarCode,
                                    name: holder.playerName,
                                  })
                                )}
                            >
                              {$t('collectibles.revoke')}
                            </button>
                          </td>
                        {/if}
                      </tr>
                    {/each}
                  </tbody>
                </table>
              </div>
            {:else}
              <p class="muted">{$t('collectibles.noHolders')}</p>
            {/if}

            {#if canManage}
              <div class="form-actions">
                <button type="button" class="ghost-button" onclick={() => editAvatar(avatar)}>
                  {$t('collectibles.edit')}
                </button>
                <button type="button" class="ghost-button" onclick={() => startGrant(avatar)}>
                  {$t('collectibles.grantAvatar')}
                </button>
                <button
                  type="button"
                  class="ghost-button danger"
                  disabled={avatar.grantedCount > 0}
                  title={avatar.grantedCount > 0 ? $t('collectibles.deleteAvatarBlocked') : ''}
                  onclick={() =>
                    ops.ask(
                      '/api/v1/operations/content/nft-avatars/delete',
                      { avatarId: avatar.id },
                      $t('collectibles.deleteAvatar'),
                      $t('collectibles.deleteAvatarSummary', { code: avatar.avatarCode })
                    )}
                >
                  {$t('collectibles.delete')}
                </button>
              </div>

            {/if}
          </article>
        {/each}
      </div>
    {/if}

    {#if canManage}
      <h3 class="subhead">{$t('collectibles.avatarEditorTitle')}</h3>
    {/if}
  </section>
  {/if}

  {#if tab === 'collectors'}
  <section class="panel" style="margin-top: 12px;">
    <div class="panel-head"><h2>{$t('collectibles.leaderboardTitle')}</h2></div>
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>{$t('collectibles.colPlayer')}</th>
            <th>{$t('collectibles.colCollectorScore')}</th>
          </tr>
        </thead>
        <tbody>
          {#each data.topCollectors || [] as row}
            <tr>
              <td><EntityLink type="player" id={row.playerId} label={row.playerName} {openPlayer} /></td>
              <td>{formatNumber(row.score)}</td>
            </tr>
          {:else}
            <tr><td colspan="2" class="muted">{$t('collectibles.noCollectors')}</td></tr>
          {/each}
        </tbody>
      </table>
    </div>
  </section>
  {/if}
{/if}

{#if picking === 'claimPlayer' || picking === 'grantPlayer'}
  <PickerModal
    kind="user"
    title={$t('operations.selectPlayerTitle')}
    onSelect={(picked: PickerRow) => {
      const target = picking === 'grantPlayer' ? grantForm : claimForm;

      if (target) {
        target.playerId = Number(picked.id);
        target.playerName = picked.name;
      }

      picking = null;
    }}
    onClose={() => (picking = null)}
  />
{:else if picking}
  <PickerModal
    kind="furniture"
    title={$t('collectibles.pickFurniture')}
    onSelect={(picked: PickerRow) => {
      if (picking === 'reward' && collectionForm) {
        collectionForm.rewardProductCode = picked.name;
        rewardIconUrl = picked.iconUrl ?? null;
      } else if (picking === 'bonus' && collectionForm) {
        collectionForm.bonusProductCode = picked.name;
        bonusIconUrl = picked.iconUrl ?? null;
      } else if (picking === 'offer' && offerForm) {
        offerForm.productCode = picked.name;
      } else if (picking === 'mintable' && mintableForm) {
        mintableForm.productCode = picked.name;
      } else if (picking === 'claim' && claimForm) {
        claimForm.productCode = picked.name;
        claimIconUrl = picked.iconUrl ?? null;
      } else if (itemForm) {
        itemForm.productCode = picked.name;
      }

      picking = null;
    }}
    onClose={() => (picking = null)}
  />
{/if}

{#if canManage && collectionForm}
  {@const form = collectionForm}
  <Drawer title={$t('collectibles.collectionsTitle')} eyebrow={$t('collectibles.title')} onclose={() => (collectionForm = null)}>
    <!-- Every form on this page carries an id and every submit button names it with `form=`. Drawer
         pins its `actions` snippet to the panel footer, OUTSIDE `children` where the form lives, and
         a `type="submit"` button only submits the form it is INSIDE. Without the pairing the button
         does nothing whatsoever: no request, no validation message, no console line. All eight
         drawers here were built without it and all eight were dead. EconomyExtrasPage and
         NavigatorConfigPage have done it correctly since theirs were written. -->
    <form
      id="collection-form"
      class="inline-form editor-form"
      onsubmit={(event) => {
        event.preventDefault();
        ops.ask(
          '/api/v1/operations/content/collections',
          {
            collectionId: Number(form.id) || 0,
            collectionCode: form.collectionCode,
            name: form.name,
            boostScore: Number(form.boostScore) || 0,
            status: Number(form.status) || 0,
            rewardProductCode: form.rewardProductCode || null,
            bonusProductCode: form.bonusProductCode || null,
          },
          form.id ? $t('collectibles.updateCollection') : $t('collectibles.addCollection'),
          $t('collectibles.saveCollectionSummary', { name: form.name })
        );
      }}
    >
      <label>
        {$t('collectibles.colCode')}
        <input autocomplete="off" spellcheck="false" bind:value={form.collectionCode} />
      </label>
      <label>
        {$t('collectibles.colCollection')}
        <input autocomplete="off" spellcheck="false" bind:value={form.name} />
      </label>
      <label>
        {$t('collectibles.colBoost')}
        <input autocomplete="off" spellcheck="false" type="number" bind:value={form.boostScore} />
      </label>
      <label>
        {$t('collectibles.colStatus')}
        <select bind:value={form.status}>
          {#each STATUS_OPTIONS as option}
            <option value={option.value}>{$t(option.key)}</option>
          {/each}
        </select>
        <small class="muted">{$t('collectibles.statusHelp')}</small>
      </label>
      <label>
        {$t('collectibles.rewardProduct')}
        <span class="cell">
          <AssetImage src={rewardIconUrl} alt={form.rewardProductCode} size={32} />
          <input autocomplete="off" spellcheck="false" bind:value={form.rewardProductCode} placeholder="classname" readonly />
          <button type="button" class="ghost-button" onclick={() => (picking = 'reward')}>
            {$t('collectibles.pickFurniture')}
          </button>
          {#if form.rewardProductCode}
            <button
              type="button"
              class="ghost-button"
              onclick={() => {
                form.rewardProductCode = '';
                rewardIconUrl = null;
              }}
            >
              {$t('collectibles.clearPrize')}
            </button>
          {/if}
        </span>
        <small class="muted">{$t('collectibles.rewardProductHelp')}</small>
      </label>
      <label>
        {$t('collectibles.bonusProduct')}
        <span class="cell">
          <AssetImage src={bonusIconUrl} alt={form.bonusProductCode} size={32} />
          <input autocomplete="off" spellcheck="false" bind:value={form.bonusProductCode} placeholder="classname" readonly />
          <button type="button" class="ghost-button" onclick={() => (picking = 'bonus')}>
            {$t('collectibles.pickFurniture')}
          </button>
          {#if form.bonusProductCode}
            <button
              type="button"
              class="ghost-button"
              onclick={() => {
                form.bonusProductCode = '';
                bonusIconUrl = null;
              }}
            >
              {$t('collectibles.clearPrize')}
            </button>
          {/if}
        </span>
        <small class="muted">{$t('collectibles.bonusProductHelp')}</small>
      </label>
    </form>
  
    {#snippet actions()}
      <button class="success" type="submit" form="collection-form" disabled={!form.collectionCode.trim() || !form.name.trim()}>
      {form.id ? $t('collectibles.updateCollection') : $t('collectibles.addCollection')}
      </button>
      {#if form.id}
      <button
      type="button"
      class="success"
      onclick={() => {
      collectionForm = emptyCollection();
      rewardIconUrl = null;
      bonusIconUrl = null;
      }}>
      {$t('collectibles.newCollection')}
      </button>
      {/if}
    {/snippet}
  </Drawer>
{/if}

{#if canManage && itemForm}
  {@const form = itemForm}
  <Drawer title={$t('collectibles.itemsTitle')} eyebrow={$t('collectibles.title')} onclose={() => (itemForm = null)}>
    <form
      id="collection-item-form"
      class="inline-form editor-form"
      onsubmit={(event) => {
        event.preventDefault();
        ops.ask(
          '/api/v1/operations/content/collections/items',
          {
            itemId: Number(form.id) || 0,
            collectionId: expanded,
            productCode: form.productCode,
            itemTypeId: form.itemTypeId || '',
            productTypeId: Number(form.productTypeId) || 0,
            score: Number(form.score) || 0,
            rarity: form.rarity || '',
            sortOrder: Number(form.sortOrder) || 0,
          },
          $t('collectibles.saveItem'),
          $t('collectibles.saveItemSummary', { code: form.productCode })
        );
      }}
    >
      <label>
        {$t('collectibles.colItem')}
        <span class="cell">
          <AssetImage src={itemPreviewUrl} alt={form.productCode} size={32} />
          <input autocomplete="off" spellcheck="false" bind:value={form.productCode} placeholder="classname" readonly />
          <button type="button" class="ghost-button" onclick={() => (picking = 'item')}>
            {$t('collectibles.pickFurniture')}
          </button>
        </span>
        <small class="muted">{$t('collectibles.itemProductHelp')}</small>
      </label>
      <label>
        {$t('collectibles.colRarity')}
        <select bind:value={form.rarity}>
          <option value="">{$t('collectibles.rarityNone')}</option>
          {#each RARITY_OPTIONS as rarity}
            <option value={rarity}>{rarity}</option>
          {/each}
        </select>
        <small class="muted">{$t('collectibles.rarityHelp')}</small>
      </label>
      <label>
        {$t('collectibles.colItemScore')}
        <input autocomplete="off" spellcheck="false" type="number" bind:value={form.score} min="0" />
      </label>
      <label>
        {$t('collectibles.sortOrder')}
        <input autocomplete="off" spellcheck="false" type="number" bind:value={form.sortOrder} />
      </label>
    </form>
  
    {#snippet actions()}
      <button type="submit" form="collection-item-form" disabled={!form.productCode.trim()}>{$t('collectibles.saveItem')}</button>
      <button type="button" class="success" onclick={() => (itemForm = emptyItem())}>
      {$t('collectibles.newItem')}
      </button>
    {/snippet}
  </Drawer>
{/if}

{#if canManage && offerForm}
  {@const form = offerForm}
  <Drawer title={$t('collectibles.shopTitle')} eyebrow={$t('collectibles.title')} onclose={() => (offerForm = null)}>
    <form
      id="store-offer-form"
      class="inline-form editor-form"
      onsubmit={(event) => {
        event.preventDefault();
        ops.ask(
          '/api/v1/operations/content/store-offers',
          {
            offerId: Number(form.id) || 0,
            productCode: form.productCode,
            emeraldPrice: Number(form.emeraldPrice) || 0,
            isFeatured: Boolean(form.isFeatured),
            isLimited: Boolean(form.isLimited),
            mintLimit: Number(form.mintLimit) || 0,
            itemTypeId: form.itemTypeId || '',
            productTypeId: Number(form.productTypeId) || 0,
            score: Number(form.score) || 0,
            rarity: form.rarity || '',
            enabled: Boolean(form.enabled),
            sortOrder: Number(form.sortOrder) || 0,
          },
          form.id ? $t('collectibles.updateOffer') : $t('collectibles.addOffer'),
          $t('collectibles.saveOfferSummary', { code: form.productCode })
        );
      }}
    >
      <label>
        {$t('collectibles.colItem')}
        <span class="cell">
          <AssetImage src={offerIconUrl} alt={form.productCode} size={32} />
          <input autocomplete="off" spellcheck="false" bind:value={form.productCode} placeholder="classname" readonly />
          <button type="button" class="ghost-button" onclick={() => (picking = 'offer')}>
            {$t('collectibles.pickFurniture')}
          </button>
        </span>
        <small class="muted">{$t('collectibles.offerProductHelpLong')}</small>
      </label>
      <label>
        {$t('collectibles.colPrice')}
        <input autocomplete="off" spellcheck="false" type="number" min="0" bind:value={form.emeraldPrice} />
        <small class="muted">{$t('collectibles.priceHelp')}</small>
      </label>
      <label>
        {$t('collectibles.colItemScore')}
        <input autocomplete="off" spellcheck="false" type="number" min="0" bind:value={form.score} />
        <small class="muted">{$t('collectibles.offerScoreHelp')}</small>
      </label>
      <label>
        {$t('collectibles.colRarity')}
        <select bind:value={form.rarity}>
          <option value="">{$t('collectibles.rarityNone')}</option>
          {#each RARITY_OPTIONS as rarity}
            <option value={rarity}>{rarity}</option>
          {/each}
        </select>
      </label>
      <label>
        {$t('collectibles.mintLimit')}
        <input autocomplete="off" spellcheck="false" type="number" min="0" bind:value={form.mintLimit} />
        <small class="muted">{$t('collectibles.mintLimitHelp')}</small>
      </label>
      <label>
        {$t('collectibles.sortOrder')}
        <input autocomplete="off" spellcheck="false" type="number" bind:value={form.sortOrder} />
      </label>
      <label class="check">
        <input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={form.isFeatured} />
        {$t('collectibles.featured')}
      </label>
      <label class="check">
        <input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={form.isLimited} />
        {$t('collectibles.limitedEdition')}
      </label>
      <label class="check">
        <input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={form.enabled} />
        {$t('collectibles.offerEnabled')}
      </label>
    </form>
  
    {#snippet actions()}
      <button class="success" type="submit" form="store-offer-form" disabled={!form.productCode.trim()}>
      {form.id ? $t('collectibles.updateOffer') : $t('collectibles.addOffer')}
      </button>
      <button type="button" class="success" onclick={() => (offerForm = emptyOffer())}>
      {$t('collectibles.newOffer')}
      </button>
    {/snippet}
  </Drawer>
{/if}

{#if canManage && mintableForm}
  {@const form = mintableForm}
  <Drawer title={$t('collectibles.mintablesTitle')} eyebrow={$t('collectibles.title')} onclose={() => (mintableForm = null)}>
    <form
      id="mintable-type-form"
      class="inline-form editor-form"
      onsubmit={(event) => {
        event.preventDefault();
        ops.ask(
          '/api/v1/operations/content/mintable-types',
          {
            typeId: Number(form.id) || 0,
            productCode: form.productCode,
            stampPrice: Number(form.stampPrice) || 0,
            startsAt: new Date(form.startsAt).toISOString(),
            endsAt: new Date(form.endsAt).toISOString(),
            regionLocked: Boolean(form.regionLocked),
            limitedEdition: Boolean(form.limitedEdition),
            editionSize: Number(form.editionSize) || 0,
            enabled: Boolean(form.enabled),
            sortOrder: Number(form.sortOrder) || 0,
          },
          form.id ? $t('collectibles.updateMintable') : $t('collectibles.addMintable'),
          $t('collectibles.saveMintableSummary', { code: form.productCode })
        );
      }}
    >
      <label>
        {$t('collectibles.colItem')}
        <span class="cell">
          <AssetImage src={mintableIconUrl} alt={form.productCode} size={32} />
          <input autocomplete="off" spellcheck="false" bind:value={form.productCode} placeholder="classname" readonly />
          <button type="button" class="ghost-button" onclick={() => (picking = 'mintable')}>
            {$t('collectibles.pickFurniture')}
          </button>
        </span>
        <small class="muted">{$t('collectibles.mintableProductHelp')}</small>
      </label>
      <label>
        {$t('collectibles.colStampPrice')}
        <input autocomplete="off" spellcheck="false" type="number" min="0" bind:value={form.stampPrice} />
        <small class="muted">{$t('collectibles.stampPriceHelp')}</small>
      </label>
      <label>
        {$t('collectibles.opensAt')}
        <input autocomplete="off" spellcheck="false" type="datetime-local" bind:value={form.startsAt} />
      </label>
      <label>
        {$t('collectibles.closesAt')}
        <input autocomplete="off" spellcheck="false" type="datetime-local" bind:value={form.endsAt} />
        <small class="muted">{$t('collectibles.windowHelp')}</small>
      </label>
      <label>
        {$t('collectibles.editionSize')}
        <input autocomplete="off" spellcheck="false" type="number" min="0" bind:value={form.editionSize} />
        <small class="muted">{$t('collectibles.editionSizeHelp')}</small>
      </label>
      <label>
        {$t('collectibles.sortOrder')}
        <input autocomplete="off" spellcheck="false" type="number" bind:value={form.sortOrder} />
      </label>
      <label class="check">
        <input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={form.limitedEdition} />
        {$t('collectibles.limitedEdition')}
      </label>
      <label class="check">
        <input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={form.regionLocked} />
        {$t('collectibles.regionLocked')}
      </label>
      <label class="check">
        <input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={form.enabled} />
        {$t('collectibles.offerEnabled')}
      </label>
    </form>
  
    {#snippet actions()}
      <button class="success" type="submit" form="mintable-type-form" disabled={!form.productCode.trim()}>
      {form.id ? $t('collectibles.updateMintable') : $t('collectibles.addMintable')}
      </button>
      <button type="button" class="success" onclick={() => (mintableForm = emptyMintable())}>
      {$t('collectibles.newMintable')}
      </button>
    {/snippet}
  </Drawer>
{/if}

{#if canManage && tokenOfferForm}
  {@const form = tokenOfferForm}
  <Drawer title={$t('collectibles.tokenOffersTitle')} eyebrow={$t('collectibles.title')} onclose={() => (tokenOfferForm = null)}>
    <form
      id="mint-token-offer-form"
      class="inline-form editor-form"
      onsubmit={(event) => {
        event.preventDefault();
        ops.ask(
          '/api/v1/operations/content/mint-token-offers',
          {
            offerId: Number(form.id) || 0,
            productCode: form.productCode,
            silverPrice: Number(form.silverPrice) || 0,
            amountTokens: Number(form.amountTokens) || 0,
            enabled: Boolean(form.enabled),
            sortOrder: Number(form.sortOrder) || 0,
          },
          form.id ? $t('collectibles.updateTokenOffer') : $t('collectibles.addTokenOffer'),
          $t('collectibles.saveTokenOfferSummary', { code: form.productCode })
        );
      }}
    >
      <label>
        {$t('collectibles.colBundle')}
        <input autocomplete="off" spellcheck="false" bind:value={form.productCode} placeholder="stamps_10" />
        <small class="muted">{$t('collectibles.tokenProductHelp')}</small>
      </label>
      <label>
        {$t('collectibles.colStamps')}
        <input autocomplete="off" spellcheck="false" type="number" min="1" bind:value={form.amountTokens} />
        <small class="muted">{$t('collectibles.amountTokensHelp')}</small>
      </label>
      <label>
        {$t('collectibles.colSilverPrice')}
        <input autocomplete="off" spellcheck="false" type="number" min="0" bind:value={form.silverPrice} />
        <small class="muted">{$t('collectibles.silverPriceHelp')}</small>
      </label>
      <label>
        {$t('collectibles.sortOrder')}
        <input autocomplete="off" spellcheck="false" type="number" bind:value={form.sortOrder} />
      </label>
      <label class="check">
        <input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={form.enabled} />
        {$t('collectibles.offerEnabled')}
      </label>
    </form>
  
    {#snippet actions()}
      <button class="success" type="submit" form="mint-token-offer-form" disabled={!form.productCode.trim()}>
      {form.id ? $t('collectibles.updateTokenOffer') : $t('collectibles.addTokenOffer')}
      </button>
      <button type="button" class="success" onclick={() => (tokenOfferForm = emptyTokenOffer())}>
      {$t('collectibles.newTokenOffer')}
      </button>
    {/snippet}
  </Drawer>
{/if}

{#if canManage && claimForm}
  {@const form = claimForm}
  <Drawer title={$t('collectibles.claimsTitle')} eyebrow={$t('collectibles.title')} onclose={() => (claimForm = null)}>
    <form
      id="claim-form"
      class="inline-form editor-form"
      onsubmit={(event) => {
        event.preventDefault();
        ops.ask(
          '/api/v1/operations/content/claims',
          {
            playerId: Number(form.playerId) || 0,
            productCode: form.productCode,
            setId: form.setId || '',
            defaultCollectionName: form.defaultCollectionName || '',
            collection: form.collection || '',
            claimLimit: Number(form.claimLimit) || 1,
            validFrom: null,
            validTo: form.validTo ? new Date(form.validTo).toISOString() : null,
          },
          $t('collectibles.addClaim'),
          $t('collectibles.saveClaimSummary', { code: form.productCode, name: form.playerName || form.playerId })
        );
      }}
    >
      <label>
        {$t('common.playerRequired')}
        <span class="cell">
          <button class="ghost-button" type="button" onclick={() => (picking = 'claimPlayer')}>
            {$t('common.selectUser')}
          </button>
          {#if form.playerId}
            <span class="op-chip">{form.playerName} <small>#{form.playerId}</small></span>
          {:else}
            <span class="muted">{$t('common.noUserSelected')}</span>
          {/if}
        </span>
      </label>
      <label>
        {$t('collectibles.colItem')}
        <span class="cell">
          <AssetImage src={claimIconUrl} alt={form.productCode} size={32} />
          <input autocomplete="off" spellcheck="false" bind:value={form.productCode} placeholder="classname" readonly />
          <button type="button" class="ghost-button" onclick={() => (picking = 'claim')}>
            {$t('collectibles.pickFurniture')}
          </button>
        </span>
        <small class="muted">{$t('collectibles.claimProductHelp')}</small>
      </label>
      <label>
        {$t('collectibles.colSet')}
        <input autocomplete="off" spellcheck="false" bind:value={form.setId} placeholder="2025_icy_christmas" />
        <small class="muted">{$t('collectibles.setIdHelp')}</small>
      </label>
      <label>
        {$t('collectibles.claimLimit')}
        <input autocomplete="off" spellcheck="false" type="number" min="1" bind:value={form.claimLimit} />
        <small class="muted">{$t('collectibles.claimLimitHelp')}</small>
      </label>
      <label>
        {$t('collectibles.colExpires')}
        <input autocomplete="off" spellcheck="false" type="date" bind:value={form.validTo} />
        <small class="muted">{$t('collectibles.expiresHelp')}</small>
      </label>
    </form>
  
    {#snippet actions()}
      <button type="submit" form="claim-form" disabled={!form.productCode.trim() || !form.playerId} class="success">
      {$t('collectibles.addClaim')}
      </button>
      <button
      type="button"
      class="success"
      onclick={() => {
      claimForm = emptyClaim();
      claimIconUrl = null;
      }}
      >
      {$t('collectibles.newClaim')}
      </button>
    {/snippet}
  </Drawer>
{/if}

{#if canManage && grantForm && grantAvatar}
  {@const form = grantForm}
  {@const avatar = grantAvatar}
  <Drawer title={$t('collectibles.grantTitle')} eyebrow={$t('collectibles.title')} onclose={() => (grantForm = null)}>
    <form
      id="avatar-grant-form"
      class="inline-form editor-form"
      onsubmit={(event) => {
        event.preventDefault();
        ops.ask(
          '/api/v1/operations/content/nft-avatars/grant',
          {
            avatarId: avatar.id,
            playerId: Number(form.playerId) || 0,
            note: form.note || '',
          },
          $t('collectibles.grantAvatar'),
          $t('collectibles.grantAvatarSummary', {
            code: avatar.avatarCode,
            name: form.playerName || form.playerId,
          })
        );
      }}
    >
      <label>
        {$t('common.playerRequired')}
        <span class="cell">
          <button class="ghost-button" type="button" onclick={() => (picking = 'grantPlayer')}>
            {$t('common.selectUser')}
          </button>
          {#if form.playerId}
            <span class="op-chip">{form.playerName} <small>#{form.playerId}</small></span>
          {:else}
            <span class="muted">{$t('common.noUserSelected')}</span>
          {/if}
        </span>
      </label>
      <label>
        {$t('collectibles.colGrantNote')}
        <input autocomplete="off" spellcheck="false" bind:value={form.note} placeholder={$t('collectibles.grantNotePlaceholder')} />
        <small class="muted">{$t('collectibles.grantNoteHelp')}</small>
      </label>
    </form>
  
    {#snippet actions()}
      <button type="submit" form="avatar-grant-form" disabled={!form.playerId}>{$t('collectibles.grantAvatar')}</button>
      <button type="button" class="ghost-button" onclick={() => (grantAvatar = null)}>
      {$t('common.cancel')}
      </button>
    {/snippet}
  </Drawer>
{/if}

{#if canManage && avatarForm}
  {@const form = avatarForm}
  <Drawer title={$t('collectibles.avatarsTitle')} eyebrow={$t('collectibles.title')} onclose={() => (avatarForm = null)}>
    <form
      id="avatar-form"
      class="inline-form editor-form"
      onsubmit={(event) => {
        event.preventDefault();
        ops.ask(
          form.id
            ? '/api/v1/operations/content/nft-avatars/update'
            : '/api/v1/operations/content/nft-avatars',
          {
            ...(form.id ? { avatarId: form.id } : {}),
            avatarCode: form.avatarCode,
            name: form.name || '',
            figure: form.figure,
            gender: form.gender,
            contractKey: form.contractKey,
            editionSize: Number(form.editionSize) || 0,
            enabled: form.enabled,
            sortOrder: Number(form.sortOrder) || 0,
          },
          form.id ? $t('collectibles.updateAvatar') : $t('collectibles.addAvatar'),
          $t('collectibles.saveAvatarSummary', { code: form.avatarCode })
        );
      }}
    >
      <label>
        {$t('collectibles.colCode')}
        <input autocomplete="off" spellcheck="false" bind:value={form.avatarCode} placeholder="halloween_2026_vampire" />
        <small class="muted">{$t('collectibles.avatarCodeHelp')}</small>
      </label>
      <label>
        {$t('collectibles.colName')}
        <input autocomplete="off" spellcheck="false" bind:value={form.name} />
      </label>
      <label>
        {$t('collectibles.colFigure')}
        <input autocomplete="off" spellcheck="false" bind:value={form.figure} placeholder="hd-180-1.ch-210-66.lg-270-82" />
        <small class="muted">{$t('collectibles.avatarFigureHelp')}</small>
      </label>
      <label>
        {$t('collectibles.colGender')}
        <select bind:value={form.gender}>
          <option value="M">M</option>
          <option value="F">F</option>
        </select>
        <small class="muted">{$t('collectibles.avatarGenderHelp')}</small>
      </label>
      <label>
        {$t('collectibles.colCollection')}
        <select bind:value={form.contractKey}>
          {#each COLLECTION_OPTIONS as option}
            <option value={option.value}>{$t(option.key)}</option>
          {/each}
        </select>
        <small class="muted">{$t('collectibles.avatarCollectionHelp')}</small>
      </label>
      <label>
        {$t('collectibles.colEdition')}
        <input autocomplete="off" spellcheck="false" type="number" min="0" bind:value={form.editionSize} />
        <small class="muted">{$t('collectibles.avatarEditionHelp')}</small>
      </label>
      <label>
        {$t('collectibles.colSort')}
        <input autocomplete="off" spellcheck="false" type="number" bind:value={form.sortOrder} />
      </label>
      <label class="checkbox">
        <input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={form.enabled} />
        {$t('collectibles.enabled')}
      </label>
    </form>
  
    {#snippet actions()}
      <button class="success" type="submit" form="avatar-form" disabled={!form.avatarCode.trim() || !form.figure.trim()}>
      {form.id ? $t('collectibles.updateAvatar') : $t('collectibles.addAvatar')}
      </button>
      <button type="button" class="success" onclick={() => (avatarForm = emptyAvatar())}>
      {$t('collectibles.newAvatar')}
      </button>
    {/snippet}
  </Drawer>
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

  /* An avatar card is a record with a picture and a list under it, not a row: the figure is the
     thing being judged, so it leads, and the holders sit below where the eye lands after it. */
  .avatar-card {
    display: flex;
    flex-direction: column;
    gap: 10px;
    padding: 12px;
  }

  .avatar-card__head {
    display: flex;
    align-items: flex-start;
    gap: 12px;
  }

  .avatar-card__title {
    display: grid;
    gap: 4px;
    min-width: 0;
  }

  .avatar-card__meta {
    display: flex;
    flex-wrap: wrap;
    gap: 6px 22px;
    margin: 0;
  }

  .avatar-card__meta dt {
    font-size: 0.8em;
    opacity: 0.7;
  }

  .avatar-card__meta dd {
    margin: 0;
  }

  tr.selected {
    background: var(--surface-raised, rgba(255, 255, 255, 0.04));
  }

  .cell {
    display: inline-flex;
    align-items: center;
    gap: 8px;
  }

  /* A Relic's whole history in one cell: short lines, and the oldest at the bottom, so the most
     recent hand it passed through is the one read first. */
  .history {
    font-size: 0.85em;
    line-height: 1.5;
  }

</style>
