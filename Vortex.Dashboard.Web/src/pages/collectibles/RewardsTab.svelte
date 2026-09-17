<script lang="ts">
  // The prizes waiting in the Collectors Guild's Rewards tab: "this player may take this piece of
  // furniture, this many times". Two pickers here rather than one — a player and a classname — so
  // this tab's picker state is a name rather than a boolean.
  import AssetImage from '../../components/AssetImage.svelte';
  import Drawer from '../../components/Drawer.svelte';
  import EmptyState from '../../components/EmptyState.svelte';
  import EntityLink from '../../components/EntityLink.svelte';
  import PickerModal from '../../components/PickerModal.svelte';
  import { formatNumber, formatDate } from '../../lib/format';
  import { t } from '../../lib/i18n';
  import type { PickerRow } from '../../lib/pickers/directories';
  import { openPlayer } from '../../lib/session';
  import type { Num, TabProps } from './shared';

  let { data, canManage, ops }: TabProps = $props();

  type ClaimForm = {
    playerId: Num;
    playerName: string;
    productCode: string;
    setId: string;
    defaultCollectionName: string;
    collection: string;
    claimLimit: Num;
    validTo: string;
    minCollectorLevel: Num;
  };

  const emptyClaim = (): ClaimForm => ({
    playerId: '',
    playerName: '',
    productCode: '',
    setId: '',
    defaultCollectionName: '',
    collection: '',
    claimLimit: 1,
    validTo: '',
    minCollectorLevel: 0,
  });

  let claimForm = $state<ClaimForm | null>(null);
  let claimIconUrl = $state<string | null>(null);

  // 'player' or 'furniture' — the two modals this editor can open, never both at once.
  let picking = $state<string | null>(null);
</script>

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
            <th>{$t('collectibles.colMinLevel')}</th>
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
              <td>{claim.minCollectorLevel > 0 ? formatNumber(claim.minCollectorLevel) : '—'}</td>
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

{#if picking === 'player'}
  <PickerModal
    kind="user"
    title={$t('operations.selectPlayerTitle')}
    onSelect={(picked: PickerRow) => {
      if (claimForm) {
        claimForm.playerId = Number(picked.id);
        claimForm.playerName = picked.name;
      }

      picking = null;
    }}
    onClose={() => (picking = null)}
  />
{:else if picking === 'furniture'}
  <PickerModal
    kind="furniture"
    title={$t('collectibles.pickFurniture')}
    onSelect={(picked: PickerRow) => {
      if (claimForm) {
        claimForm.productCode = picked.name;
        claimIconUrl = picked.iconUrl ?? null;
      }

      picking = null;
    }}
    onClose={() => (picking = null)}
  />
{/if}

{#if canManage && claimForm}
  {@const form = claimForm}
  <Drawer title={$t('collectibles.claimsTitle')} eyebrow={$t('collectibles.title')} onclose={() => (claimForm = null)}>
    <!-- `id` here and `form=` on the submit button below: Drawer pins `actions` outside this form,
         and a submit button only submits the form it is inside. See CollectionsTab. -->
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
            minCollectorLevel: Number(form.minCollectorLevel) || 0,
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
          <button class="ghost-button" type="button" onclick={() => (picking = 'player')}>
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
          <button type="button" class="ghost-button" onclick={() => (picking = 'furniture')}>
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
        {$t('collectibles.colMinLevel')}
        <input autocomplete="off" spellcheck="false" type="number" min="0" bind:value={form.minCollectorLevel} />
        <small class="muted">{$t('collectibles.minLevelHelp')}</small>
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

<style>
  .cell {
    display: inline-flex;
    align-items: center;
    gap: 8px;
  }
</style>
