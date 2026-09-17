<script lang="ts">
  // The stamp bundles, priced in silver. A bundle's product code is a free label, not a furniture
  // classname — nothing joins it to the catalogue — so this is the one editor on the page with a
  // plain text field where the others have a furniture picker.
  import CurrencyIcon from '../../components/CurrencyIcon.svelte';
  import Drawer from '../../components/Drawer.svelte';
  import EmptyState from '../../components/EmptyState.svelte';
  import { CURRENCY_KIND, currencyChipClass } from '../../lib/currency';
  import { formatNumber } from '../../lib/format';
  import { t } from '../../lib/i18n';
  import type { Num, TabProps } from './shared';

  let { data, canManage, ops }: TabProps = $props();

  type TokenOfferForm = {
    id: number;
    productCode: string;
    silverPrice: Num;
    amountTokens: Num;
    enabled: boolean;
    sortOrder: Num;
  };

  const emptyTokenOffer = (): TokenOfferForm => ({
    id: 0,
    productCode: '',
    silverPrice: 10,
    amountTokens: 1,
    enabled: true,
    sortOrder: 0,
  });

  let tokenOfferForm = $state<TokenOfferForm | null>(null);
</script>

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

{#if canManage && tokenOfferForm}
  {@const form = tokenOfferForm}
  <Drawer title={$t('collectibles.tokenOffersTitle')} eyebrow={$t('collectibles.title')} onclose={() => (tokenOfferForm = null)}>
    <!-- `id` here and `form=` on the submit button below: Drawer pins `actions` outside this form,
         and a submit button only submits the form it is inside. See CollectionsTab. -->
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
