<script lang="ts">
  // Which furni a player may convert into a Relic, at what stamp price, and for how long.
  //
  // A window is not optional: the client greys the convert button out once the end date has passed
  // and says nothing about why. A new type therefore opens now and runs for a year rather than
  // starting empty and being saved unusable.
  import AssetImage from '../../components/AssetImage.svelte';
  import Drawer from '../../components/Drawer.svelte';
  import EmptyState from '../../components/EmptyState.svelte';
  import PickerModal from '../../components/PickerModal.svelte';
  import type { MintableTypeRow } from '../../lib/apiTypes';
  import { formatNumber, formatDate } from '../../lib/format';
  import { t } from '../../lib/i18n';
  import type { PickerRow } from '../../lib/pickers/directories';
  import { isoLocal, type Num, type TabProps } from './shared';

  let { data, canManage, ops }: TabProps = $props();

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
  let picking = $state(false);

  let mintableIconUrl = $derived(
    (data.mintableTypes || []).find((t) => t.productCode === mintableForm?.productCode)?.iconUrl ?? null
  );

  // The editor binds to datetime-local inputs, which speak local time with no zone; the API takes
  // instants. A row loaded for editing therefore has to come back the other way round.
  const editMintable = (row: MintableTypeRow) => {
    mintableForm = {
      ...row,
      startsAt: isoLocal(new Date(row.startsAt)),
      endsAt: isoLocal(new Date(row.endsAt)),
    };
  };
</script>

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

{#if picking}
  <PickerModal
    kind="furniture"
    title={$t('collectibles.pickFurniture')}
    onSelect={(picked: PickerRow) => {
      if (mintableForm) {
        mintableForm.productCode = picked.name;
      }

      picking = false;
    }}
    onClose={() => (picking = false)}
  />
{/if}

{#if canManage && mintableForm}
  {@const form = mintableForm}
  <Drawer title={$t('collectibles.mintablesTitle')} eyebrow={$t('collectibles.title')} onclose={() => (mintableForm = null)}>
    <!-- `id` here and `form=` on the submit button below: Drawer pins `actions` outside this form,
         and a submit button only submits the form it is inside. See CollectionsTab. -->
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
          <button type="button" class="ghost-button" onclick={() => (picking = true)}>
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

<style>
  .cell {
    display: inline-flex;
    align-items: center;
    gap: 8px;
  }
</style>
