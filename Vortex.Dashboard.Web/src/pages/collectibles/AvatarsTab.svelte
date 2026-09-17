<script lang="ts">
  // The whole-avatar collectibles: put one on and you appear as that character, rather than wearing
  // a piece of it. With no chain to ask, the row in `player_nft_avatars` IS the ownership — which is
  // why these are given out from here and never bought.
  //
  // Two editors: the avatar itself, and giving a copy of one to a player.
  import AssetImage from '../../components/AssetImage.svelte';
  import Drawer from '../../components/Drawer.svelte';
  import EmptyState from '../../components/EmptyState.svelte';
  import EntityLink from '../../components/EntityLink.svelte';
  import PickerModal from '../../components/PickerModal.svelte';
  import type { NftAvatarRow } from '../../lib/apiTypes';
  import { formatNumber, formatDate } from '../../lib/format';
  import { t } from '../../lib/i18n';
  import type { PickerRow } from '../../lib/pickers/directories';
  import { openPlayer } from '../../lib/session';
  import { COLLECTION_OPTIONS, type Num, type TabProps } from './shared';

  let { data, canManage, ops }: TabProps = $props();

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

  // Kept beside the avatar it belongs to rather than in one form at the bottom: the note is about
  // this avatar, and an admin handing out prizes at an event is looking at the row, not at a form.
  let grantAvatar = $state<NftAvatarRow | null>(null);
  let grantForm = $state<GrantForm | null>(null);
  let pickingPlayer = $state(false);

  const startGrant = (avatar: NftAvatarRow) => {
    grantAvatar = avatar;
    grantForm = { playerId: '', playerName: '', note: '' };
  };

  const editAvatar = (row: NftAvatarRow) => {
    avatarForm = { ...row };
  };
</script>

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

{#if pickingPlayer}
  <PickerModal
    kind="user"
    title={$t('operations.selectPlayerTitle')}
    onSelect={(picked: PickerRow) => {
      if (grantForm) {
        grantForm.playerId = Number(picked.id);
        grantForm.playerName = picked.name;
      }

      pickingPlayer = false;
    }}
    onClose={() => (pickingPlayer = false)}
  />
{/if}

{#if canManage && grantForm && grantAvatar}
  {@const form = grantForm}
  {@const avatar = grantAvatar}
  <Drawer title={$t('collectibles.grantTitle')} eyebrow={$t('collectibles.title')} onclose={() => (grantForm = null)}>
    <!-- `id` here and `form=` on the submit button below. This is the editor the whole split came
         out of: its button sat in Drawer's footer, outside the form, so GIVE A COPY did nothing at
         all — no request, no message, no console line. See CollectionsTab. -->
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
          <button class="ghost-button" type="button" onclick={() => (pickingPlayer = true)}>
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

  .cell {
    display: inline-flex;
    align-items: center;
    gap: 8px;
  }
</style>
