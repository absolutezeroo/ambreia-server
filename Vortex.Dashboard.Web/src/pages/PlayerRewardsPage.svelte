<script lang="ts">
  // Badges, avatar effects, chat styles and saved outfits. The emulator only ever reads these one
  // player at a time, so the hotel-wide view is the only place a broken grant shows: a badge held by
  // thousands, an effect nobody ever activated, a chat style owned by nobody.
  import { onMount } from 'svelte';
  import { apiGet } from '../lib/api';
  import { createWriteOps } from '../lib/writeOps';
  import { hasDashboardCapability } from '../lib/permissions';
  import { CAPABILITIES } from '../lib/dashboardPermissions';
  import { identity } from '../lib/session';
  import ConfirmReasonModal from '../components/ConfirmReasonModal.svelte';
  import LoadingOverlay from '../components/LoadingOverlay.svelte';
  import OpResult from '../components/OpResult.svelte';

  import { formatNumber, formatDate, formatDuration } from '../lib/format';
  import { isPermissionDeniedError } from '../lib/permissions';
  import { openPlayer } from '../lib/session';
  import AccessDeniedNotice from '../components/AccessDeniedNotice.svelte';
  import Drawer from '../components/Drawer.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import AssetImage from '../components/AssetImage.svelte';
  import EmptyState from '../components/EmptyState.svelte';
  import EntityLink from '../components/EntityLink.svelte';
  import PickerModal from '../components/PickerModal.svelte';
  import StatCard from '../components/StatCard.svelte';
  import { Award, Sparkles, Shirt } from '@lucide/svelte';
  import { t } from '../lib/i18n';
  import type { PlayerRewardDetail, PlayerRewardStats } from '../lib/apiTypes';
  import type { PickerRow } from '../lib/pickers/directories';

  /** The player whose rewards are open, as the picker handed them back. */
  type PickedPlayer = { id: number; name: string };

  let loading = $state(false);
  let forbidden = $state(false);
  let error = $state('');
  let data = $state<PlayerRewardStats | null>(null);
  let player = $state<PickedPlayer | null>(null);
  let detail = $state<PlayerRewardDetail | null>(null);
  let detailLoading = $state(false);
  let picking = $state(false);

  let grantOpen = $state(false);

  const ops = createWriteOps(async () => {
    await refresh();
    if (player) await loadPlayer(player);
  });

  let canManage = $derived(hasDashboardCapability($identity, CAPABILITIES.opsContentManage));

  let badgeCode = $state('');

  // Same reasoning for a badge code: it is granted before anyone holds it, so the preview is built
  // from the template. A code that names no file falls back, exactly as the client would show
  // nothing.
  let badgePreviewUrl =
    $derived(badgeCode.trim() && data?.badgeImageTemplate
      ? data.badgeImageTemplate.replace('{badge}', encodeURIComponent(badgeCode.trim()))
      : null);
  let effectId = $state(0);

  // Built from the template rather than looked up: the id being granted is usually one nobody owns
  // yet, so there would be nothing to look up. An avatar wearing it is the only picture of an effect.
  let effectPreviewUrl =
    $derived(effectId && data?.effectImageTemplate
      ? data.effectImageTemplate.replace('{effect}', String(Number(effectId)))
      : null);
  let effectDuration = $state(0);

  async function refresh() {
    loading = true;
    error = '';
    forbidden = false;

    try {
      data = await apiGet<PlayerRewardStats>('/api/v1/player-rewards');
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

  async function loadPlayer(picked: PickedPlayer) {
    player = picked;
    detail = null;
    detailLoading = true;

    try {
      detail = await apiGet<PlayerRewardDetail>(`/api/v1/player-rewards/${picked.id}`);
    } catch (err) {
      error = (err as Error).message;
    } finally {
      detailLoading = false;
    }
  }

  onMount(() => {
    void refresh();
  });
</script>

<section class="panel">
  <PageHeader title={$t('playerRewards.title')} description={$t('playerRewards.description')}>
    {#snippet actions()}
      <button type="button" onclick={refresh} disabled={loading} class="warning">{$t('common.refresh')}</button>
      <button type="button" class="ghost-button" onclick={() => (picking = true)}>
        {player ? player.name : $t('playerRewards.inspectPlayer')}
      </button>
    {/snippet}
  </PageHeader>

  {#if forbidden}
    <AccessDeniedNotice message={$t('playerRewards.accessDenied')} />
  {:else if error}
    <p class="empty-state danger" role="alert">{error}</p>
  {/if}
</section>

{#if data}
  <div class="metric-grid" style="margin-top: 12px;">
    <StatCard
      label={$t('playerRewards.badges')}
      value={formatNumber(data.totals.totalBadges)}
      sub={$t('playerRewards.equipped', { count: formatNumber(data.totals.equippedBadges) })}
    >
      {#snippet icon()}
        <Award size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard label={$t('playerRewards.distinctBadges')} value={formatNumber(data.totals.distinctBadgeCodes)}>
      {#snippet icon()}
        <Award size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard label={$t('playerRewards.playersWithBadges')} value={formatNumber(data.totals.playersWithBadges)}>
      {#snippet icon()}
        <Award size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard
      label={$t('playerRewards.effects')}
      value={formatNumber(data.totals.totalEffects)}
      sub={$t('playerRewards.activated', { count: formatNumber(data.totals.activatedEffects) })}
    >
      {#snippet icon()}
        <Sparkles size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard
      label={$t('playerRewards.outfits')}
      value={formatNumber(data.totals.wardrobeOutfits)}
      sub={$t('playerRewards.wardrobeUsers', { count: formatNumber(data.totals.wardrobeUsers) })}
    >
      {#snippet icon()}
        <Shirt size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
  </div>

  <div class="split-grid" style="margin-top: 12px;">
    <div class="panel">
      <div class="panel-head"><h2>{$t('playerRewards.topBadgesTitle')}</h2></div>
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>{$t('playerRewards.colBadge')}</th>
              <th>{$t('playerRewards.colHolders')}</th>
              <th>{$t('playerRewards.colEquipped')}</th>
            </tr>
          </thead>
          <tbody>
            {#each data.topBadges || [] as row}
              <tr>
                <td>
                  <span class="badge-cell">
                    <AssetImage src={row.badgeUrl} alt={row.badgeCode} size={32} fallbackIcon={Award} />
                    <code>{row.badgeCode}</code>
                  </span>
                </td>
                <td>{formatNumber(row.holders)}</td>
                <td>{formatNumber(row.equipped)}</td>
              </tr>
            {:else}
              <tr><td colspan="3" class="muted">{$t('playerRewards.noBadges')}</td></tr>
            {/each}
          </tbody>
        </table>
      </div>
    </div>

    <div class="panel">
      <div class="panel-head"><h2>{$t('playerRewards.topEffectsTitle')}</h2></div>
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>{$t('playerRewards.colEffect')}</th>
              <th>{$t('playerRewards.colOwners')}</th>
              <th>{$t('playerRewards.colActivated')}</th>
              <th>{$t('playerRewards.colSelected')}</th>
            </tr>
          </thead>
          <tbody>
            {#each data.topEffects || [] as row}
              <tr>
                <td>
                  <span class="badge-cell">
                    <AssetImage src={row.imageUrl} alt="" size={44} fallbackIcon={Sparkles} />
                    <span>{row.effectId}</span>
                  </span>
                </td>
                <td>{formatNumber(row.owners)}</td>
                <td>{formatNumber(row.activated)}</td>
                <td>{formatNumber(row.selected)}</td>
              </tr>
            {:else}
              <tr><td colspan="4" class="muted">{$t('playerRewards.noEffects')}</td></tr>
            {/each}
          </tbody>
        </table>
      </div>
    </div>
  </div>

  <div class="split-grid" style="margin-top: 12px;">
    <div class="panel">
      <div class="panel-head"><h2>{$t('playerRewards.topCollectorsTitle')}</h2></div>
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>{$t('playerRewards.colPlayer')}</th>
              <th>{$t('playerRewards.colBadgeCount')}</th>
            </tr>
          </thead>
          <tbody>
            {#each data.topCollectors || [] as row}
              <tr>
                <td><EntityLink type="player" id={row.playerId} label={row.playerName} {openPlayer} /></td>
                <td>{formatNumber(row.badges)}</td>
              </tr>
            {:else}
              <tr><td colspan="2" class="muted">{$t('playerRewards.noBadges')}</td></tr>
            {/each}
          </tbody>
        </table>
      </div>
    </div>
  </div>
{/if}

{#if player}
  <section class="panel" style="margin-top: 12px;">
    <div class="panel-head">
      <h2>{$t('playerRewards.playerTitle', { name: player.name })}</h2>
      {#if canManage}
        <button type="button" class="ghost-button" onclick={() => (grantOpen = true)}>
          {$t('playerRewards.grantTitle', { name: player.name })}
        </button>
      {/if}
    </div>
    {#if detailLoading}
      <p class="muted">{$t('common.loading')}</p>
    {:else if detail}
      <div class="split-grid">
        <div>
          <h3 class="subhead">{$t('playerRewards.badges')}</h3>
          {#if (detail.badges || []).length === 0}
            <EmptyState message={$t('playerRewards.noBadges')} />
          {:else}
            <div class="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>{$t('playerRewards.colBadge')}</th>
                    <th>{$t('playerRewards.colSlot')}</th>
                    <th>{$t('playerRewards.colEarned')}</th>
                  </tr>
                </thead>
                <tbody>
                  {#each detail.badges as badge}
                    <tr>
                      <td>
                        <span class="badge-cell">
                          <AssetImage
                            src={badge.badgeUrl}
                            alt={badge.badgeCode}
                            size={28}
                            fallbackIcon={Award}
                          />
                          <code>{badge.badgeCode}</code>
                        </span>
                      </td>
                      <td>{badge.slotId ?? '—'}</td>
                      <td>{formatDate(badge.createdAt)}</td>
                    </tr>
                  {/each}
                </tbody>
              </table>
            </div>
          {/if}
        </div>

        <div>
          <h3 class="subhead">{$t('playerRewards.effects')}</h3>
          {#if (detail.effects || []).length === 0}
            <EmptyState message={$t('playerRewards.noEffects')} />
          {:else}
            <div class="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>{$t('playerRewards.colEffect')}</th>
                    <th>{$t('playerRewards.colDuration')}</th>
                    <th>{$t('playerRewards.colActivated')}</th>
                    <th>{$t('playerRewards.colSelected')}</th>
                  </tr>
                </thead>
                <tbody>
                  {#each detail.effects as effect}
                    <tr>
                      <td>
                        <span class="badge-cell">
                          <AssetImage src={effect.imageUrl} alt="" size={44} fallbackIcon={Sparkles} />
                          <span>{effect.effectId}{effect.subType ? `.${effect.subType}` : ''}</span>
                        </span>
                      </td>
                      <td>{effect.totalDuration ? formatDuration(effect.totalDuration) : '—'}</td>
                      <td>{effect.activatedAt ? formatDate(effect.activatedAt) : '—'}</td>
                      <td>{effect.isSelected ? $t('common.yes') : $t('common.no')}</td>
                    </tr>
                  {/each}
                </tbody>
              </table>
            </div>
          {/if}
        </div>
      </div>

      <h3 class="subhead">{$t('playerRewards.outfitsTitle')}</h3>
      {#if (detail.outfits || []).length === 0}
        <EmptyState message={$t('playerRewards.noOutfits')} />
      {:else}
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{$t('playerRewards.colSlot')}</th>
                <th>{$t('playerRewards.colFigure')}</th>
                <th>{$t('playerRewards.colGender')}</th>
              </tr>
            </thead>
            <tbody>
              {#each detail.outfits as outfit}
                <tr>
                  <td>{outfit.slotId}</td>
                  <td><code>{outfit.figure}</code></td>
                  <td>{outfit.gender}</td>
                </tr>
              {/each}
            </tbody>
          </table>
        </div>
      {/if}
    {/if}
  </section>
{/if}

{#if canManage && player && grantOpen}
  {@const target = player}
  <Drawer
    title={$t('playerRewards.grantTitle', { name: target.name })}
    eyebrow={$t('playerRewards.title')}
    onclose={() => (grantOpen = false)}
  >
    <p class="muted">{$t('playerRewards.grantHint')}</p>

    <form
      class="inline-form"
      onsubmit={(event) => {
        event.preventDefault();
        ops.ask(
          '/api/v1/operations/content/badges/grant',
          { playerId: target.id, badgeCode },
          $t('playerRewards.grantBadge'),
          $t('playerRewards.grantBadgeSummary', { code: badgeCode, name: target.name })
        );
      }}
    >
      <label>
        {$t('playerRewards.colBadge')}
        <span class="badge-cell">
          <input autocomplete="off" spellcheck="false" bind:value={badgeCode} placeholder="ACH_RoomEntry1" list="known-badges" />
          <AssetImage src={badgePreviewUrl} alt={badgeCode} size={32} fallbackIcon={Award} />
        </span>
      </label>
      <datalist id="known-badges">
        {#each data?.topBadges || [] as known}<option value={known.badgeCode}></option>{/each}
      </datalist>
      <button type="submit" disabled={!badgeCode.trim()}>{$t('playerRewards.grantBadge')}</button>
      <button
        type="button"
        class="ghost-button danger"
        disabled={!badgeCode.trim()}
        onclick={() =>
          ops.ask(
            '/api/v1/operations/content/badges/revoke',
            { playerId: target.id, badgeCode },
            $t('playerRewards.revokeBadge'),
            $t('playerRewards.revokeBadgeSummary', { code: badgeCode, name: target.name })
          )}
      >
        {$t('playerRewards.revokeBadge')}
      </button>
    </form>

    <form
      class="inline-form"
      onsubmit={(event) => {
        event.preventDefault();
        ops.ask(
          '/api/v1/operations/content/effects/grant',
          { playerId: target.id, effectId: Number(effectId), durationSeconds: Number(effectDuration) || 0 },
          $t('playerRewards.grantEffect'),
          $t('playerRewards.grantEffectSummary', { id: effectId, name: target.name })
        );
      }}
    >
      <label>
        {$t('playerRewards.colEffect')}
        <span class="badge-cell">
          <input autocomplete="off" spellcheck="false" type="number" bind:value={effectId} min="1" />
          <AssetImage src={effectPreviewUrl} alt="" size={44} fallbackIcon={Sparkles} />
        </span>
      </label>
      <label>
        {$t('playerRewards.durationSeconds')}
        <input autocomplete="off" spellcheck="false" type="number" bind:value={effectDuration} min="0" placeholder={$t('common.permanent')} />
      </label>
      <button type="submit" disabled={!effectId}>{$t('playerRewards.grantEffect')}</button>
      <button
        type="button"
        class="ghost-button danger"
        disabled={!effectId}
        onclick={() =>
          ops.ask(
            '/api/v1/operations/content/effects/revoke',
            { playerId: target.id, effectId: Number(effectId), durationSeconds: 0 },
            $t('playerRewards.revokeEffect'),
            $t('playerRewards.revokeEffectSummary', { id: effectId, name: target.name })
          )}
      >
        {$t('playerRewards.revokeEffect')}
      </button>
    </form>

    {#if $ops.result}
      <OpResult result={$ops.result} />
    {/if}

    {#snippet actions()}
      <button type="button" class="ghost-button" onclick={() => (grantOpen = false)}>{$t('common.close')}</button>
    {/snippet}
  </Drawer>
{/if}

{#if picking}
  <PickerModal
    kind="user"
    title={$t('playerRewards.inspectPlayer')}
    onSelect={(picked: PickerRow) => {
      picking = false;
      void loadPlayer({ id: Number(picked.id), name: picked.name });
    }}
    onClose={() => (picking = false)}
  />
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
  .badge-cell {
    display: inline-flex;
    align-items: center;
    gap: 8px;
  }



  .subhead {
    margin: 14px 0 8px;
    font-size: 0.95rem;
  }
</style>
