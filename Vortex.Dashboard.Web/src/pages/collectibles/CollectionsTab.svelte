<script lang="ts">
  // The collections and the items inside them. The one thing worth flagging: an item whose product
  // code matches no furniture definition makes its collection impossible to complete, and nothing
  // else in the hotel would ever say so — hence the Resolved column.
  //
  // The expanded row is local to this tab because nothing outside it has a collection to expand.
  import AssetImage from '../../components/AssetImage.svelte';
  import Drawer from '../../components/Drawer.svelte';
  import EmptyState from '../../components/EmptyState.svelte';
  import OpResult from '../../components/OpResult.svelte';
  import PickerModal from '../../components/PickerModal.svelte';
  import { formatNumber, formatDate } from '../../lib/format';
  import { t } from '../../lib/i18n';
  import type { PickerRow } from '../../lib/pickers/directories';
  import { RARITY_OPTIONS, STATUS_OPTIONS, type Num, type TabProps } from './shared';

  let { data, canManage, ops }: TabProps = $props();

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

  let expanded = $state<number | null>(null);
  let collectionForm = $state<CollectionForm | null>(null);
  let itemForm = $state<ItemForm | null>(null);

  // Which furniture picker is open, if any: the collection's two prizes and the collection item all
  // pick from the same catalogue, so they share one modal rather than three.
  let picking = $state<string | null>(null);

  // The icons of the two prizes, remembered from the pick. They are not in `data` like the item
  // icons are — a prize is a classname on the collection, not a row in its item list — so without
  // this the admin picks a chair and sees only the word.
  let rewardIconUrl = $state<string | null>(null);
  let bonusIconUrl = $state<string | null>(null);

  const statusLabel = (value: number | string) =>
    $t(STATUS_OPTIONS.find((o) => o.value === Number(value))?.key ?? 'collectibles.statusUnknown');

  // The product code is a furniture classname, so it is picked from the real catalogue rather than
  // typed: a code that matches nothing is exactly what makes a collection uncompletable.
  let itemPreviewUrl = $derived(
    (data.collections || [])
      .flatMap((c) => c.items || [])
      .find((i) => i.productCode === itemForm?.productCode)?.iconUrl ?? null
  );
</script>

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

{#if picking}
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
    <!-- The form carries an id and the submit button names it with `form=`. Drawer pins its
         `actions` snippet to the panel footer, OUTSIDE `children` where the form lives, and a
         `type="submit"` button only submits the form it is INSIDE. Without the pairing the button
         does nothing whatsoever: no request, no validation message, no console line. Every editor
         on this page was built without it and every one of them was dead. -->
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
          }}
        >
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

<style>
  tr.selected {
    background: var(--surface-raised, rgba(255, 255, 255, 0.04));
  }

  .cell {
    display: inline-flex;
    align-items: center;
    gap: 8px;
  }
</style>
