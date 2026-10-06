<script lang="ts">
  // The five player operations, bound to a player the page already knows. They used to live on
  // /operations, where each of the five carried its own "Select user" button: giving someone credits
  // and then kicking them meant picking the same player twice, from two cards, with nothing on
  // screen tying the two actions to one person. Here the player is the context, not a field.
  //
  //   <PlayerOperationsPanel playerId={player.id} playerName={player.name} online={player.online} />
  import OpResult from './OpResult.svelte';
  import AccessDeniedNotice from './AccessDeniedNotice.svelte';
  import ConfirmStagedModal from './ConfirmStagedModal.svelte';
  import PickerModal from './PickerModal.svelte';
  import CurrencyIcon from './CurrencyIcon.svelte';
  import CurrencySelect from './CurrencySelect.svelte';
  import Modal from './Modal.svelte';
  import { Coins, Zap, Gem, Package, UserX } from '@lucide/svelte';
  import { hasDashboardCapability } from '../lib/permissions';
  import { createWriteOps } from '../lib/writeOps';
  import { OPERATION_CAPABILITIES, MODERATION_OPERATION_CAPABILITIES } from '../lib/dashboardPermissions';
  import { positive, nonNegative } from '../lib/validation';
  import { identity } from '../lib/session';
  import { t, translate } from '../lib/i18n';

  type Props = {
    playerId: number|string;
    playerName?: string;
    online?: boolean;
    /** called after a write lands, so the page can refresh its timeline */
    onDone?: () => void;
  };

  let { playerId, playerName = '', online = false, onDone }: Props = $props();

  const ops = createWriteOps(() => onDone?.());

  // One bag per action; the player is not in them -- it comes from the page.
  let credits = $state({ amount: '', reason: '' });
  // `type` is a number: CurrencySelect binds the reward type it writes, and the request sends
  // Number(type) either way. It was initialised as a string, so it was one until the operator
  // touched the control and a number after -- the same field holding two types.
  let activity = $state({ type: 0, amount: '', reason: '' });
  // Silver and emeralds share one form: same grant, different currency.
  let collectibles = $state({ currency: 'silver', amount: '', reason: '' });
  let item = $state({ definitionId: '', defName: '', defSprite: '', defIcon: '', extraData: '', reason: '' });
  let kick = $state({ reason: '' });
  let ban = $state({ permanent: false, durationSeconds: '', reason: '' });
  let unban = $state({ reason: '' });
  let mute = $state({ durationSeconds: '', reason: '' });
  let tradingLock = $state({ permanent: false, durationSeconds: '', reason: '' });
  let tradingUnlock = $state({ reason: '' });
  let forensicsPurge = $state({ reason: '' });

  /** Which picker is open, and what to do with what it returns. */
  type OpenPicker = {
    kind: string;
    title: string;
    onSelect: (chosen: Record<string, any>) => void;
  };

  let picker = $state<OpenPicker | null>(null);
  // Which operation is open. One at a time: five forms at once was five reasons to fill in for
  // one decision, and the heights never matched.
  let openAction = $state<string | null>(null);

  let canCredits = $derived(hasDashboardCapability($identity, OPERATION_CAPABILITIES.credits));
  let canActivity = $derived(hasDashboardCapability($identity, OPERATION_CAPABILITIES.activity));
  let canCollectibles = $derived(hasDashboardCapability($identity, OPERATION_CAPABILITIES.collectibles));
  let canItem = $derived(hasDashboardCapability($identity, OPERATION_CAPABILITIES.item));
  let canKick = $derived(hasDashboardCapability($identity, OPERATION_CAPABILITIES.kick));
  let canBan = $derived(hasDashboardCapability($identity, MODERATION_OPERATION_CAPABILITIES.ban));
  let canUnban = $derived(hasDashboardCapability($identity, MODERATION_OPERATION_CAPABILITIES.unban));
  let canMute = $derived(hasDashboardCapability($identity, MODERATION_OPERATION_CAPABILITIES.mute));
  let canTradingLock = $derived(hasDashboardCapability($identity, MODERATION_OPERATION_CAPABILITIES.tradingLock));
  let canTradingUnlock = $derived(hasDashboardCapability($identity, MODERATION_OPERATION_CAPABILITIES.tradingUnlock));
  let canForensicsPurge = $derived(hasDashboardCapability($identity, OPERATION_CAPABILITIES.forensicsPurge));

  // The list the chooser renders. Permission decides whether an entry can be opened at all,
  // rather than opening it onto an access-denied notice.
  let ACTIONS = $derived([
    { key: 'credits', label: $t('operations.giveCredits'), hint: $t('operations.giveCreditsHint'), allowed: canCredits },
    { key: 'activity', label: $t('operations.giveActivityPoints'), hint: $t('operations.giveActivityPointsHint'), allowed: canActivity },
    { key: 'collectibles', label: $t('operations.giveCollectiblesCurrency'), hint: $t('operations.giveCollectiblesHint'), allowed: canCollectibles },
    { key: 'item', label: $t('operations.giveFurniture'), hint: $t('operations.giveFurnitureHint'), allowed: canItem },
    {
      key: 'kick',
      label: $t('operations.kickPlayer'),
      hint: online ? $t('operations.kickPlayerHint') : $t('playerOps.offlineKickHint'),
      allowed: canKick && online,
    },
    { key: 'ban', label: $t('moderationActions.banAccount'), hint: $t('moderationActions.banAccountHint'), allowed: canBan },
    { key: 'unban', label: $t('moderationActions.liftAccountBan'), hint: $t('moderationActions.liftAccountBanHint'), allowed: canUnban },
    { key: 'mute', label: $t('moderationActions.mutePlayer'), hint: $t('moderationActions.mutePlayerHint'), allowed: canMute },
    { key: 'tradingLock', label: $t('moderationActions.lockTrading'), hint: $t('moderationActions.lockTradingHint'), allowed: canTradingLock },
    { key: 'tradingUnlock', label: $t('moderationActions.liftTradingLock'), hint: $t('moderationActions.liftTradingLockHint'), allowed: canTradingUnlock },
    { key: 'forensicsPurge', label: $t('operations.purgeForensics'), hint: $t('operations.purgeForensicsHint'), allowed: canForensicsPurge },
  ]);

  let who = $derived(playerName || translate('operations.player'));

  // The reason is generated, not demanded. `writeOps.confirm` builds what actually gets audited from
  // the action itself -- every summary here already names the amount, the target and the id -- and
  // treats the operator's text as a note appended to it. This panel used to gate on `reasonOk` before
  // the modal even opened, so all eleven actions refused with "fill the fields" until a reason was
  // typed, and the generated one was never reached.
  //
  // `|| undefined` is the switch between the two paths in `confirm`: a typed reason becomes the
  // audited base, an empty one falls through to the summary. Either way the server's three-character
  // floor is met by a real sentence rather than by whatever the operator could be bothered to type.
  const stage = (
    id: string,
    title: string,
    endpoint: string,
    valid: boolean,
    body: Record<string, unknown>,
    summary: string,
  ) =>
    ops.ask(endpoint, { playerId: Number(playerId), ...body }, title, summary, {
      key: id,
      valid: valid && positive(playerId),
      invalidMessage: translate('operations.fillFields'),
      reason: (body.reason as string) || undefined,
    });

  function stageCredits() {
    if (!canCredits) return void ops.fail('credits', translate('operations.creditsAccessDenied'));

    stage(
      'credits',
      translate('operations.giveCredits'),
      '/api/v1/operations/currency/credits',
      positive(credits.amount),
      { amount: Number(credits.amount), reason: credits.reason.trim() },
      translate('operations.creditsSummary', { amount: credits.amount, name: who, id: playerId }),
    );
  }

  function stageActivity() {
    if (!canActivity) return void ops.fail('activity', translate('operations.activityAccessDenied'));

    stage(
      'activity',
      translate('operations.giveActivityPoints'),
      '/api/v1/operations/currency/activity-points',
      nonNegative(activity.type) && positive(activity.amount),
      { type: Number(activity.type), amount: Number(activity.amount), reason: activity.reason.trim() },
      translate('operations.activitySummary', {
        amount: activity.amount,
        type: activity.type,
        name: who,
        id: playerId,
      }),
    );
  }

  function stageCollectibles() {
    if (!canCollectibles) {
      return void ops.fail('collectibles', translate('operations.collectiblesAccessDenied'));
    }

    stage(
      'collectibles',
      translate('operations.giveCollectiblesCurrency'),
      '/api/v1/operations/currency/collectibles',
      positive(collectibles.amount),
      {
        currency: collectibles.currency,
        amount: Number(collectibles.amount),
        reason: collectibles.reason.trim(),
      },
      translate('operations.collectiblesSummary', {
        amount: collectibles.amount,
        currency: translate(
          collectibles.currency === 'emeralds'
            ? 'operations.currencyEmeralds'
            : 'operations.currencySilver',
        ),
        name: who,
        id: playerId,
      }),
    );
  }

  function stageItem() {
    if (!canItem) return void ops.fail('item', translate('operations.itemAccessDenied'));

    stage(
      'item',
      translate('operations.giveFurniture'),
      '/api/v1/operations/items/grant',
      positive(item.definitionId),
      {
        definitionId: Number(item.definitionId),
        extraData: item.extraData.trim() ? item.extraData.trim() : null,
        reason: item.reason.trim(),
      },
      translate('operations.furnitureSummary', {
        name: item.defName || translate('operations.furniture'),
        id: item.definitionId,
        playerName: who,
        playerId,
      }),
    );
  }

  function stageKick() {
    if (!canKick) return void ops.fail('kick', translate('operations.kickAccessDenied'));

    stage(
      'kick',
      translate('operations.kickPlayer'),
      '/api/v1/operations/players/kick',
      true,
      { reason: kick.reason.trim() },
      translate('operations.kickSummary', { name: who, id: playerId }),
    );
  }

  function stageForensicsPurge() {
    if (!canForensicsPurge)
      return void ops.fail('forensicsPurge', translate('operations.purgeForensicsAccessDenied'));

    stage(
      'forensicsPurge',
      translate('operations.purgeForensics'),
      '/api/v1/operations/players/forensics-purge',
      true,
      { reason: forensicsPurge.reason.trim() },
      translate('operations.purgeForensicsSummary', { name: who, id: playerId }),
    );
  }

  function stageBan() {
    if (!canBan) return void ops.fail('ban', translate('moderationActions.banAccessDenied'));

    stage(
      'ban',
      translate('moderationActions.banAccount'),
      '/api/v1/operations/players/ban',
      ban.permanent || positive(ban.durationSeconds),
      {
        permanent: ban.permanent,
        durationSeconds: ban.permanent ? null : Number(ban.durationSeconds),
        reason: ban.reason.trim(),
      },
      translate('moderationActions.banSummary', {
        action: ban.permanent
          ? translate('moderationActions.permanentlyBan')
          : translate('moderationActions.banFor', { seconds: ban.durationSeconds }),
        name: who,
        id: playerId,
      }),
    );
  }

  function stageUnban() {
    if (!canUnban) return void ops.fail('unban', translate('moderationActions.unbanAccessDenied'));

    stage(
      'unban',
      translate('moderationActions.liftAccountBan'),
      '/api/v1/operations/players/unban',
      true,
      { reason: unban.reason.trim() },
      translate('moderationActions.liftBanSummary', { name: who, id: playerId }),
    );
  }

  function stageMute() {
    if (!canMute) return void ops.fail('mute', translate('moderationActions.muteAccessDenied'));

    stage(
      'mute',
      translate('moderationActions.mutePlayer'),
      '/api/v1/operations/players/mute',
      positive(mute.durationSeconds),
      { durationSeconds: Number(mute.durationSeconds), reason: mute.reason.trim() },
      translate('moderationActions.muteSummary', { name: who, id: playerId, seconds: mute.durationSeconds }),
    );
  }

  function stageTradingLock() {
    if (!canTradingLock) return void ops.fail('tradingLock', translate('moderationActions.lockAccessDenied'));

    stage(
      'tradingLock',
      translate('moderationActions.lockTrading'),
      '/api/v1/operations/players/trading-lock',
      tradingLock.permanent || positive(tradingLock.durationSeconds),
      {
        permanent: tradingLock.permanent,
        durationSeconds: tradingLock.permanent ? null : Number(tradingLock.durationSeconds),
        reason: tradingLock.reason.trim(),
      },
      translate('moderationActions.lockTradingSummary', {
        action: tradingLock.permanent
          ? translate('moderationActions.permanentlyLock')
          : translate('moderationActions.lockFor', { seconds: tradingLock.durationSeconds }),
        name: who,
        id: playerId,
      }),
    );
  }

  function stageTradingUnlock() {
    if (!canTradingUnlock) return void ops.fail('tradingUnlock', translate('moderationActions.unlockAccessDenied'));

    stage(
      'tradingUnlock',
      translate('moderationActions.liftTradingLock'),
      '/api/v1/operations/players/trading-unlock',
      true,
      { reason: tradingUnlock.reason.trim() },
      translate('moderationActions.liftLockSummary', { name: who, id: playerId }),
    );
  }

  async function copy(value: string | null | undefined) {
    try {
      await navigator.clipboard.writeText(value || '');
    } catch {
      // Clipboard is best-effort; the id is also visible on screen.
    }
  }
</script>

{#snippet creditsForm()}
    {#if !canCredits}
      <AccessDeniedNotice message={$t('operations.creditsAccessDenied')} />
    {:else}
      <div class="op-field">
        <label for="credits-amount">{$t('operations.amount')}</label>
        <input
          id="credits-amount"
          type="number"
          min="1"
          autocomplete="off"
          spellcheck="false"
          bind:value={credits.amount}
          placeholder="100"
        />
      </div>
      <div class="op-field">
        <label for="credits-reason">{$t('common.reasonOptional')}</label>
        <input
          id="credits-reason"
          autocomplete="off"
          spellcheck="false"
          bind:value={credits.reason}
          placeholder={$t('common.reasonPlaceholder')}
          list="reason-history"
        />
      </div>
      <div class="op-actions">
        <button type="button" onclick={stageCredits} disabled={$ops.busyKeys.credits}>
          {$t('common.run')}
        </button>
      </div>
      {#if $ops.errors.credits}
        <p class="empty-state danger" role="alert">{$ops.errors.credits}</p>
      {/if}
      {#if $ops.results.credits}
        <OpResult result={$ops.results.credits} onCopy={copy} copyLabel={$t('common.copy')} />
      {/if}
    {/if}
{/snippet}

{#snippet activityForm()}
    {#if !canActivity}
      <AccessDeniedNotice message={$t('operations.activityAccessDenied')} />
    {:else}
      <div class="op-field">
        <label for="activity-type">{$t('operations.activityPointType')}</label>
        <CurrencySelect id="activity-type" credits={false} bind:value={activity.type} />
      </div>
      <div class="op-field">
        <label for="activity-amount">{$t('operations.amount')}</label>
        <input
          id="activity-amount"
          type="number"
          min="1"
          autocomplete="off"
          spellcheck="false"
          bind:value={activity.amount}
          placeholder="50"
        />
      </div>
      <div class="op-field">
        <label for="activity-reason">{$t('common.reasonOptional')}</label>
        <input
          id="activity-reason"
          autocomplete="off"
          spellcheck="false"
          bind:value={activity.reason}
          placeholder={$t('common.reasonPlaceholder')}
          list="reason-history"
        />
      </div>
      <div class="op-actions">
        <button type="button" onclick={stageActivity} disabled={$ops.busyKeys.activity}>
          {$t('common.run')}
        </button>
      </div>
      {#if $ops.errors.activity}
        <p class="empty-state danger" role="alert">{$ops.errors.activity}</p>
      {/if}
      {#if $ops.results.activity}
        <OpResult result={$ops.results.activity} onCopy={copy} copyLabel={$t('common.copy')} />
      {/if}
    {/if}
{/snippet}

{#snippet collectiblesForm()}
    {#if !canCollectibles}
      <AccessDeniedNotice message={$t('operations.collectiblesAccessDenied')} />
    {:else}
      <div class="op-field">
        <label for="collectibles-currency">{$t('operations.collectiblesCurrency')}</label>
        <span class="currency-select">
          <CurrencyIcon kind={collectibles.currency} />
          <select id="collectibles-currency" bind:value={collectibles.currency}>
            <option value="silver">{$t('operations.currencySilver')}</option>
            <option value="emeralds">{$t('operations.currencyEmeralds')}</option>
          </select>
        </span>
      </div>
      <div class="op-field">
        <label for="collectibles-amount">{$t('operations.amount')}</label>
        <input
          id="collectibles-amount"
          type="number"
          min="1"
          autocomplete="off"
          spellcheck="false"
          bind:value={collectibles.amount}
          placeholder="100"
        />
      </div>
      <div class="op-field">
        <label for="collectibles-reason">{$t('common.reasonOptional')}</label>
        <input
          id="collectibles-reason"
          autocomplete="off"
          spellcheck="false"
          bind:value={collectibles.reason}
          placeholder={$t('common.reasonPlaceholder')}
          list="reason-history"
        />
      </div>
      <div class="op-actions">
        <button type="button" onclick={stageCollectibles} disabled={$ops.busyKeys.collectibles}>
          {$t('common.run')}
        </button>
      </div>
      {#if $ops.errors.collectibles}
        <p class="empty-state danger" role="alert">{$ops.errors.collectibles}</p>
      {/if}
      {#if $ops.results.collectibles}
        <OpResult result={$ops.results.collectibles} onCopy={copy} copyLabel={$t('common.copy')} />
      {/if}
    {/if}
{/snippet}

{#snippet itemForm()}
    {#if !canItem}
      <AccessDeniedNotice message={$t('operations.itemAccessDenied')} />
    {:else}
      <div class="op-field">
        <span class="op-label">{$t('common.selectFurniture')} *</span>
        <div class="op-pick">
          <button
            class="ghost-button"
            type="button"
            onclick={() =>
              (picker = {
                kind: 'furniture',
                title: translate('operations.selectFurnitureTitle'),
                onSelect: (f: Record<string, any>) =>
                  (item = {
                    ...item,
                    definitionId: f.id,
                    defName: f.name,
                    defSprite: f.spriteId,
                    defIcon: f.iconUrl,
                  }),
              })}
          >
            {$t('common.selectFurniture')}
          </button>
          {#if item.definitionId}
            <span class="op-chip">
              {#if item.defIcon}
                <img class="op-sprite" src={item.defIcon} alt="" loading="lazy" />
              {:else}
                <span class="op-sprite">{item.defSprite}</span>
              {/if}
              {item.defName} <small>#{item.definitionId}</small>
            </span>
          {:else}
            <span class="muted">{$t('common.noFurnitureSelected')}</span>
          {/if}
        </div>
      </div>
      <div class="op-field">
        <label for="item-extra">{$t('operations.extraDataOptional')}</label>
        <input
          id="item-extra"
          autocomplete="off"
          spellcheck="false"
          bind:value={item.extraData}
          placeholder={$t('operations.extraDataPlaceholder')}
        />
      </div>
      <div class="op-field">
        <label for="item-reason">{$t('common.reasonOptional')}</label>
        <input
          id="item-reason"
          autocomplete="off"
          spellcheck="false"
          bind:value={item.reason}
          placeholder={$t('common.reasonPlaceholder')}
          list="reason-history"
        />
      </div>
      <div class="op-actions">
        <button type="button" onclick={stageItem} disabled={$ops.busyKeys.item}>
          {$t('common.run')}
        </button>
      </div>
      {#if $ops.errors.item}
        <p class="empty-state danger" role="alert">{$ops.errors.item}</p>
      {/if}
      {#if $ops.results.item}
        <OpResult result={$ops.results.item} onCopy={copy} copyLabel={$t('common.copy')} />
      {/if}
    {/if}
{/snippet}

{#snippet kickForm()}
    {#if !canKick}
      <AccessDeniedNotice message={$t('operations.kickAccessDenied')} />
    {:else}
      <div class="op-field">
        <label for="kick-reason">{$t('common.reasonOptional')}</label>
        <input
          id="kick-reason"
          autocomplete="off"
          spellcheck="false"
          bind:value={kick.reason}
          placeholder={$t('common.reasonPlaceholder')}
          list="reason-history"
        />
      </div>
      <div class="op-actions">
        <button type="button" onclick={stageKick} disabled={$ops.busyKeys.kick || !online}>
          {$t('common.run')}
        </button>
      </div>
      {#if $ops.errors.kick}
        <p class="empty-state danger" role="alert">{$ops.errors.kick}</p>
      {/if}
      {#if $ops.results.kick}
        <OpResult result={$ops.results.kick} onCopy={copy} copyLabel={$t('common.copy')} />
      {/if}
    {/if}
{/snippet}

{#snippet forensicsPurgeForm()}
    {#if !canForensicsPurge}
      <AccessDeniedNotice message={$t('operations.purgeForensicsAccessDenied')} />
    {:else}
      <!-- Spelled out rather than left to the confirm dialog: this is the one operation on the panel
           that destroys evidence, and the operator should read what survives before running it. -->
      <p class="muted">{$t('operations.purgeForensicsScope')}</p>
      <div class="op-field">
        <label for="forensics-purge-reason">{$t('common.reasonOptional')}</label>
        <input
          id="forensics-purge-reason"
          autocomplete="off"
          spellcheck="false"
          bind:value={forensicsPurge.reason}
          placeholder={$t('operations.purgeForensicsReasonPlaceholder')}
          list="reason-history"
        />
      </div>
      <div class="op-actions">
        <button
          type="button"
          class="danger"
          onclick={stageForensicsPurge}
          disabled={$ops.busyKeys.forensicsPurge}
        >
          {$t('common.run')}
        </button>
      </div>
      {#if $ops.errors.forensicsPurge}
        <p class="empty-state danger" role="alert">{$ops.errors.forensicsPurge}</p>
      {/if}
      {#if $ops.results.forensicsPurge}
        <OpResult result={$ops.results.forensicsPurge} onCopy={copy} copyLabel={$t('common.copy')} />
      {/if}
    {/if}
{/snippet}

{#snippet banForm()}
    {#if !canBan}
      <AccessDeniedNotice message={$t('moderationActions.banAccessDenied')} />
    {:else}
      <div class="op-checkbox-field">
        <input id="ban-permanent" type="checkbox" autocomplete="off" bind:checked={ban.permanent} />
        <label for="ban-permanent">{$t('common.permanent')}</label>
      </div>
      {#if !ban.permanent}
      <div class="op-field">
        <label for="ban-duration">{$t('moderationActions.durationSeconds')}</label>
        <input
          id="ban-duration"
          type="number"
          min="1"
          autocomplete="off"
          spellcheck="false"
          bind:value={ban.durationSeconds}
          placeholder="86400"
        />
      </div>
      {/if}
      <div class="op-field">
        <label for="ban-reason">{$t('common.reasonOptional')}</label>
        <input
          id="ban-reason"
          autocomplete="off"
          spellcheck="false"
          bind:value={ban.reason}
          placeholder={$t('common.reasonPlaceholder')}
          list="reason-history"
        />
      </div>
      <div class="op-actions">
        <button type="button" onclick={stageBan} disabled={$ops.busyKeys.ban}>{$t('common.run')}</button>
      </div>
      {#if $ops.errors.ban}<p class="empty-state danger" role="alert">{$ops.errors.ban}</p>{/if}
      {#if $ops.results.ban}
        <OpResult result={$ops.results.ban} onCopy={copy} copyLabel={$t('common.copy')} />
      {/if}
    {/if}
{/snippet}

{#snippet unbanForm()}
    {#if !canUnban}
      <AccessDeniedNotice message={$t('moderationActions.unbanAccessDenied')} />
    {:else}

      <div class="op-field">
        <label for="unban-reason">{$t('common.reasonOptional')}</label>
        <input
          id="unban-reason"
          autocomplete="off"
          spellcheck="false"
          bind:value={unban.reason}
          placeholder={$t('common.reasonPlaceholder')}
          list="reason-history"
        />
      </div>
      <div class="op-actions">
        <button type="button" onclick={stageUnban} disabled={$ops.busyKeys.unban}>{$t('common.run')}</button>
      </div>
      {#if $ops.errors.unban}<p class="empty-state danger" role="alert">{$ops.errors.unban}</p>{/if}
      {#if $ops.results.unban}
        <OpResult result={$ops.results.unban} onCopy={copy} copyLabel={$t('common.copy')} />
      {/if}
    {/if}
{/snippet}

{#snippet muteForm()}
    {#if !canMute}
      <AccessDeniedNotice message={$t('moderationActions.muteAccessDenied')} />
    {:else}
      <p class="muted plain">{$t('moderationActions.roomScopedNote')}</p>
      <div class="op-field">
        <label for="mute-duration">{$t('moderationActions.durationSeconds')}</label>
        <input
          id="mute-duration"
          type="number"
          min="1"
          autocomplete="off"
          spellcheck="false"
          bind:value={mute.durationSeconds}
          placeholder="600"
        />
      </div>
      <div class="op-field">
        <label for="mute-reason">{$t('common.reasonOptional')}</label>
        <input
          id="mute-reason"
          autocomplete="off"
          spellcheck="false"
          bind:value={mute.reason}
          placeholder={$t('common.reasonPlaceholder')}
          list="reason-history"
        />
      </div>
      <div class="op-actions">
        <button type="button" onclick={stageMute} disabled={$ops.busyKeys.mute}>{$t('common.run')}</button>
      </div>
      {#if $ops.errors.mute}<p class="empty-state danger" role="alert">{$ops.errors.mute}</p>{/if}
      {#if $ops.results.mute}
        <OpResult result={$ops.results.mute} onCopy={copy} copyLabel={$t('common.copy')} />
      {/if}
    {/if}
{/snippet}

{#snippet tradingLockForm()}
    {#if !canTradingLock}
      <AccessDeniedNotice message={$t('moderationActions.lockAccessDenied')} />
    {:else}
      <div class="op-checkbox-field">
        <input id="tradingLock-permanent" type="checkbox" autocomplete="off" bind:checked={tradingLock.permanent} />
        <label for="tradingLock-permanent">{$t('common.permanent')}</label>
      </div>
      {#if !tradingLock.permanent}
      <div class="op-field">
        <label for="tradingLock-duration">{$t('moderationActions.durationSeconds')}</label>
        <input
          id="tradingLock-duration"
          type="number"
          min="1"
          autocomplete="off"
          spellcheck="false"
          bind:value={tradingLock.durationSeconds}
          placeholder="86400"
        />
      </div>
      {/if}
      <div class="op-field">
        <label for="tradingLock-reason">{$t('common.reasonOptional')}</label>
        <input
          id="tradingLock-reason"
          autocomplete="off"
          spellcheck="false"
          bind:value={tradingLock.reason}
          placeholder={$t('common.reasonPlaceholder')}
          list="reason-history"
        />
      </div>
      <div class="op-actions">
        <button type="button" onclick={stageTradingLock} disabled={$ops.busyKeys.tradingLock}>{$t('common.run')}</button>
      </div>
      {#if $ops.errors.tradingLock}<p class="empty-state danger" role="alert">{$ops.errors.tradingLock}</p>{/if}
      {#if $ops.results.tradingLock}
        <OpResult result={$ops.results.tradingLock} onCopy={copy} copyLabel={$t('common.copy')} />
      {/if}
    {/if}
{/snippet}

{#snippet tradingUnlockForm()}
    {#if !canTradingUnlock}
      <AccessDeniedNotice message={$t('moderationActions.unlockAccessDenied')} />
    {:else}

      <div class="op-field">
        <label for="tradingUnlock-reason">{$t('common.reasonOptional')}</label>
        <input
          id="tradingUnlock-reason"
          autocomplete="off"
          spellcheck="false"
          bind:value={tradingUnlock.reason}
          placeholder={$t('common.reasonPlaceholder')}
          list="reason-history"
        />
      </div>
      <div class="op-actions">
        <button type="button" onclick={stageTradingUnlock} disabled={$ops.busyKeys.tradingUnlock}>{$t('common.run')}</button>
      </div>
      {#if $ops.errors.tradingUnlock}<p class="empty-state danger" role="alert">{$ops.errors.tradingUnlock}</p>{/if}
      {#if $ops.results.tradingUnlock}
        <OpResult result={$ops.results.tradingUnlock} onCopy={copy} copyLabel={$t('common.copy')} />
      {/if}
    {/if}
{/snippet}

<div class="op-list">
  {#each ACTIONS as action}
    <button
      type="button"
      class="op-choice"
      disabled={!action.allowed}
      onclick={() => (openAction = action.key)}
    >
      <span class="op-choice-icon">
        {#if action.key === 'credits'}<Coins size={17} strokeWidth={2} aria-hidden="true" />
        {:else if action.key === 'activity'}<Zap size={17} strokeWidth={2} aria-hidden="true" />
        {:else if action.key === 'collectibles'}<Gem size={17} strokeWidth={2} aria-hidden="true" />
        {:else if action.key === 'item'}<Package size={17} strokeWidth={2} aria-hidden="true" />
        {:else}<UserX size={17} strokeWidth={2} aria-hidden="true" />{/if}
      </span>
      <span class="op-choice-copy">
        <strong>{action.label}</strong>
        {#if !action.allowed}<small class="muted">{$t('operations.notPermitted')}</small>
        {:else if action.hint}<small class="muted">{action.hint}</small>{/if}
      </span>
    </button>
  {/each}
</div>

{#if openAction}
  <Modal
    title={ACTIONS.find((a) => a.key === openAction)?.label ?? ''}
    eyebrow={$t('operations.confirmEyebrow')}
    width={520}
    column
    onclose={() => (openAction = null)}
  >
    {#if openAction === 'credits'}{@render creditsForm()}
    {:else if openAction === 'activity'}{@render activityForm()}
    {:else if openAction === 'collectibles'}{@render collectiblesForm()}
    {:else if openAction === 'item'}{@render itemForm()}
    {:else if openAction === 'kick'}{@render kickForm()}
    {:else if openAction === 'ban'}{@render banForm()}
    {:else if openAction === 'unban'}{@render unbanForm()}
    {:else if openAction === 'mute'}{@render muteForm()}
    {:else if openAction === 'tradingLock'}{@render tradingLockForm()}
    {:else if openAction === 'tradingUnlock'}{@render tradingUnlockForm()}
    {:else if openAction === 'forensicsPurge'}{@render forensicsPurgeForm()}
    {/if}
  </Modal>
{/if}


{#if picker}
  <PickerModal
    kind={picker.kind}
    title={picker.title}
    onSelect={picker.onSelect}
    onClose={() => (picker = null)}
    canSelect={canItem}
  />
{/if}

<ConfirmStagedModal {ops} eyebrow={$t('operations.confirmEyebrow')} />
