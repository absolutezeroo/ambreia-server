<script lang="ts">
  // The emerald shop: what the Collectors Guild sells directly, priced in emeralds rather than
  // converted from furni you already own.
  import AssetImage from '../../components/AssetImage.svelte';
  import CurrencyIcon from '../../components/CurrencyIcon.svelte';
  import Drawer from '../../components/Drawer.svelte';
  import EmptyState from '../../components/EmptyState.svelte';
  import PickerModal from '../../components/PickerModal.svelte';
  import { CURRENCY_KIND, currencyChipClass } from '../../lib/currency';
  import { formatNumber } from '../../lib/format';
  import { t } from '../../lib/i18n';
  import type { PickerRow } from '../../lib/pickers/directories';
  import { RARITY_OPTIONS, type Num, type TabProps } from './shared';

  let { data, canManage, ops }: TabProps = $props();

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
  let picking = $state(false);

  // The shop's own icon, looked up in the listing: unlike a collection's two prizes, an offer is a
  // row, so its image is already on the page once it has been saved.
  let offerIconUrl = $derived(
    (data.storeOffers || []).find((o) => o.productCode === offerForm?.productCode)?.iconUrl ?? null
  );
</script>

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

{#if picking}
  <PickerModal
    kind="furniture"
    title={$t('collectibles.pickFurniture')}
    onSelect={(picked: PickerRow) => {
      if (offerForm) {
        offerForm.productCode = picked.name;
      }

      picking = false;
    }}
    onClose={() => (picking = false)}
  />
{/if}

{#if canManage && offerForm}
  {@const form = offerForm}
  <Drawer title={$t('collectibles.shopTitle')} eyebrow={$t('collectibles.title')} onclose={() => (offerForm = null)}>
    <!-- `id` here and `form=` on the submit button below: Drawer pins `actions` outside this form,
         and a submit button only submits the form it is inside. See CollectionsTab. -->
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
          <button type="button" class="ghost-button" onclick={() => (picking = true)}>
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

<style>
  .cell {
    display: inline-flex;
    align-items: center;
    gap: 8px;
  }
</style>
