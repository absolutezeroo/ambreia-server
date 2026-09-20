<script lang="ts">
  // Reward-track campaigns.
  //
  // The whole point of the engine is that a campaign is content, so this page is where one gets
  // built: the track, its tasks and their stages, its milestones and what each hands over, and the
  // lifecycle that puts it in front of the hotel. Nothing here needs a rebuild to take effect.
  import { apiGet } from '../lib/api';
  import { createResource } from '../lib/resource';
  import { createWriteOps } from '../lib/writeOps';
  import { hasDashboardCapability } from '../lib/permissions';
  import { CAPABILITIES } from '../lib/dashboardPermissions';
  import { identity } from '../lib/session';
  import { formatDate, formatNumber } from '../lib/format';
  import { t, translate } from '../lib/i18n';
  import { Blocks, Route, Users } from '@lucide/svelte';
  import type {
    FactOption,
    PlayerRewardTracks,
    RewardKindOption,
    RewardKindOptions,
    RewardTrackActionOption,
    RewardTrackActionOptions,
    RewardTrackList,
    RewardTrackRow,
  } from '../lib/apiTypes';
  import type { PickerRow } from '../lib/pickers/directories';

  /** One choice of a fixed vocabulary the page draws as a select. */
  type Choice = { value: number; key: string };

  /** The player whose progress the second tab reads. */
  type PickedPlayer = { id: number; name: string };

  /**
   * The four editors' forms. Every number binds to an input that hands back a string while it
   * is being typed, which is why each read below goes through Number().
   */
  type TrackForm = {
    trackId: string;
    theme: string;
    sortOrder: number | string;
    startsAt: string;
    progressEndsAt: string;
    claimEndsAt: string;
    unlockKind: number | string;
    unlockValue: string;
    completionPolicy: number | string;
    premiumEnabled: boolean;
    premiumBoostPerMille: number | string;
    premiumInstantPoints: number | string;
    premiumCostCredits: number | string;
    premiumCostDiamonds: number | string;
    hidden: boolean;
    campaignCode: string;
  };

  type LevelForm = {
    requiredCount: number | string;
    pointsReward: number | string;
    premium: boolean;
  };

  type StepForm = { actionCode: string; tree: TreeNode[] };

  type TaskForm = {
    trackRowId: number;
    taskId: string;
    actionCode: string;
    parameter: string;
    mode: number | string;
    premium: boolean;
    sortOrder: number | string;
    levels: LevelForm[];
    steps: StepForm[];
  };

  type RewardForm = {
    kind: number | string;
    rewardTypeId: string;
    amount: number | string;
    extraParams: string;
    sortOrder: number | string;
  };

  type PrizeForm = {
    trackRowId: number;
    prizeId: string;
    requiredPoints: number | string;
    premium: boolean;
    sortOrder: number | string;
    rewards: RewardForm[];
  };

  /** An editor open on a row (id set) or on a new one (id null). */
  type Draft<T> = { id: number | null; form: T };

  /**
   * Which condition's value the picker is filling, addressed by where it sits: the step, and the
   * path down that step's condition tree.
   */
  type FilterTarget = { stepIndex: number; path: NodePath; kind: string };

  /** What the last pick was called, so the drawer can show a name beside the stored id. */
  type PickedLabel = { name: string; iconUrl?: string | null };

  import AccessDeniedNotice from '../components/AccessDeniedNotice.svelte';
  import LoadingOverlay from '../components/LoadingOverlay.svelte';
  import Pagination from '../components/Pagination.svelte';
  import { pageOf, pageCountOf, PAGE_SIZE } from '../lib/tableView';
  import ConfirmReasonModal from '../components/ConfirmReasonModal.svelte';
  import Drawer from '../components/Drawer.svelte';
  import EmptyState from '../components/EmptyState.svelte';
  import OpResult from '../components/OpResult.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import PickerModal from '../components/PickerModal.svelte';
  import SequenceWorkspace from '../components/sequence/SequenceWorkspace.svelte';
  import {
    TEST,
    fromRows,
    nodeAt,
    pathKey,
    replaceAt,
    toRows,
    tests,
    type NodePath,
    type TreeNode,
  } from '../lib/sequence/tree';
  import StatCard from '../components/StatCard.svelte';
  import Tabs from '../components/Tabs.svelte';

  // The client's five palettes. Anything else renders blue, so this is a select rather than a text
  // field.
  const THEMES = ['blue', 'orange', 'forest_green', 'red', 'cyan'];

  // Mirrors TaskProgressMode. Four modes cover every task the official track has; the labels say
  // what each counts rather than naming the enum.
  const MODES: Choice[] = [
    { value: 0, key: 'rewardTracks.modeCounter' },
    { value: 1, key: 'rewardTracks.modeDistinct' },
    { value: 2, key: 'rewardTracks.modeAbsolute' },
    { value: 3, key: 'rewardTracks.modeHighest' },
  ];

  const UNLOCK_KINDS: Choice[] = [
    { value: 0, key: 'rewardTracks.unlockAlways' },
    { value: 1, key: 'rewardTracks.unlockTrackCompleted' },
    { value: 2, key: 'rewardTracks.unlockPrizeClaimed' },
    { value: 3, key: 'rewardTracks.unlockBadge' },
    { value: 4, key: 'rewardTracks.unlockAccountAge' },
    { value: 5, key: 'rewardTracks.unlockFeatureFlag' },
  ];

  // Mirrors StepFilterOperator. Which of these a given fact may use is decided by the server and
  // sent with the fact: `contains` is meaningless on a room id and an exact match is useless on a
  // line a player typed, and the validator refuses both -- so the editor must not offer them.
  const FILTER_OPERATORS: Choice[] = [
    { value: 0, key: 'rewardTracks.filterOpEquals' },
    { value: 1, key: 'rewardTracks.filterOpNotEquals' },
    { value: 2, key: 'rewardTracks.filterOpOneOf' },
    { value: 3, key: 'rewardTracks.filterOpContains' },
    { value: 4, key: 'rewardTracks.filterOpAtLeast' },
    { value: 5, key: 'rewardTracks.filterOpAtMost' },
    { value: 6, key: 'rewardTracks.filterOpNotContains' },
    { value: 7, key: 'rewardTracks.filterOpNotOneOf' },
    { value: 8, key: 'rewardTracks.filterOpHasAnyOf' },
    { value: 9, key: 'rewardTracks.filterOpHasNoneOf' },
  ];

  /** The operators this fact accepts, in the order the server ranked them. */
  function operatorsFor(actionCode: string, factKey: string): Choice[] {
    const allowed = factMeta(actionCode, factKey)?.operators;

    if (!allowed?.length) return FILTER_OPERATORS;

    return allowed
      .map((value) => FILTER_OPERATORS.find((op) => op.value === value))
      .filter((op): op is Choice => op !== undefined);
  }

  /**
   * Which fact kinds have a directory behind them, so their value can be picked instead of typed.
   * The kind now arrives from the server with the fact itself, so this maps kinds rather than keys:
   * any action carrying a room id gets the room picker without this list being touched again.
   * `OpaqueId` deliberately has none -- a placed item or a pet has no catalogue to pick from.
   */
  const PICKER_FOR_KIND: Record<string, string> = {
    FurnitureId: 'furniture',
    RoomId: 'room',
    PlayerId: 'user',
    CategoryId: 'category',
    OfferId: 'offer',
    BadgeCode: 'badge',
    FigureSetId: 'figureSet',
  };

  /**
   * Facts whose kind is generic but whose meaning is not.
   * <p>
   * A guild id, a Habbicon and a pet species are all OpaqueId as far as the engine is concerned --
   * it only ever compares strings. The editor knows better, because it knows which fact it is
   * looking at, and a value picked from a list cannot be misspelled.
   * </p>
   */
  const PICKER_FOR_FACT: Record<string, string> = {
    group: 'group',
    habbicon: 'habbicon',
    collection: 'collection',
    pet_type: 'petSpecies',
    pet_command: 'petCommand',
    poll: 'poll',
    quiz: 'quiz',
    campaign: 'campaign',
    voucher: 'voucher',
    club_gift: 'clubGift',
    nft_product: 'nftProduct',
    targeted_offer: 'targetedOffer',
    thread: 'thread',
    effect: 'effect',
    item: 'placedItem',
  };

  /** The directory this filter can pick from, by fact first and kind second. */
  function pickerFor(meta: FactOption | null) {
    if (!meta) return null;

    return PICKER_FOR_FACT[meta.key] ?? PICKER_FOR_KIND[meta.kind] ?? null;
  }

  /**
   * The facts a given action emits, straight from the server: each carries its kind, its label and
   * -- for a closed fact -- the values it accepts. Anything not in here can never match.
   */
  function factsFor(actionCode: string): FactOption[] {
    return actionOptions.find((a) => a.name === actionCode)?.facts ?? [];
  }

  /** One fact's metadata, by key. */
  function factMeta(actionCode: string, factKey: string): FactOption | null {
    return factsFor(actionCode).find((f) => f.key === factKey) ?? null;
  }

  /** A closed-vocabulary fact starts on a value it accepts; everything else starts empty. */
  function defaultFilterValue(actionCode: string, factKey: string) {
    const meta = factMeta(actionCode, factKey);

    return meta?.values?.length ? meta.values[0].value : '';
  }

  /**
   * The earlier steps a filter on this fact may point back at — only those whose own action
   * records that fact, because $N resolves to the same fact key. "The same furniture" only works
   * between two steps that both talk about furniture.
   */
  function referencesFor(steps: StepForm[], index: number, factKey: string) {
    return steps
      .slice(0, index)
      .map((step, i) => ({ value: `$${i}`, index: i, action: step.actionCode }))
      .filter((r) => factsFor(r.action).some((f) => f.key === factKey));
  }

  const COMPLETION_POLICIES: Choice[] = [
    { value: 0, key: 'rewardTracks.policyFreeClaimed' },
    { value: 1, key: 'rewardTracks.policyAllClaimed' },
    { value: 2, key: 'rewardTracks.policyMaxPoints' },
    { value: 3, key: 'rewardTracks.policyAllTasks' },
  ];

  let tab = $state('tracks');
  let search = $state('');
  let expanded = $state<number | null>(null);

  let player = $state<PickedPlayer | null>(null);
  let pickingPlayer = $state(false);

  // Which filter's value the picker is filling, and what the last pick was called. The name and the
  // sprite are shown, never stored: a filter holds the id the handler emits, and the drawer reopens
  // on that id alone.
  let sequenceOpen = $state(false);
  let pickingFilter = $state<FilterTarget | null>(null);
  let pickedLabels = $state<Record<string, PickedLabel>>({});

  let trackDraft = $state<Draft<TrackForm> | null>(null);
  let taskDraft = $state<Draft<TaskForm> | null>(null);
  let prizeDraft = $state<Draft<PrizeForm> | null>(null);
  let cloneDraft = $state<{ id: number; newTrackId: string } | null>(null);

  const ops = createWriteOps();

  let canManage = $derived(hasDashboardCapability($identity, CAPABILITIES.opsRewardTracksManage));

  const tracks = createResource(
    () => ['reward-tracks', search.trim()],
    async () => {
      const params = search.trim() ? `?search=${encodeURIComponent(search.trim())}` : '';
      const [list, actions, kinds] = await Promise.all([
        apiGet<RewardTrackList>(`/api/v1/reward-tracks${params}`),
        apiGet<RewardTrackActionOptions>('/api/v1/reward-tracks/actions'),
        apiGet<RewardKindOptions>('/api/v1/reward-tracks/reward-kinds'),
      ]);

      return { items: list.items ?? [], actions: actions.items ?? [], kinds: kinds.items ?? [] };
    }
  );

  const progress = createResource(
    () => ['reward-tracks-player', player?.id ?? null],
    () => apiGet<PlayerRewardTracks>(`/api/v1/reward-tracks/players/${player!.id}`),
    { enabled: () => player !== null }
  );

  let items = $derived(tracks.data?.items ?? []);

  // Same as HabbiconsPage: the endpoint has no Take, so every track was drawn.
  let page = $state(1);
  let pageCount = $derived(pageCountOf(items));
  let pageItems = $derived(pageOf(items, page));

  $effect(() => {
    void search;
    page = 1;
  });
  // NOT `actions`: every Drawer footer here is an `{#snippet actions()}`, and a snippet is hoisted
  // to its enclosing block -- so inside `{#if taskDraft}` the name resolved to the footer snippet
  // instead of this array. Iterating a function yields nothing and throws nothing, which is how the
  // action picker shipped as an empty menu.
  let actionOptions: RewardTrackActionOption[] = $derived(tracks.data?.actions ?? []);
  let kinds: RewardKindOption[] = $derived(tracks.data?.kinds ?? []);
  let live = $derived(items.filter((it) => it.status === 'Active').length);
  let participants = $derived(items.reduce((sum, it) => sum + it.participants, 0));
  let premiumHolders = $derived(items.reduce((sum, it) => sum + it.premiumHolders, 0));

  function emptyTrack(): TrackForm {
    return {
      trackId: '',
      theme: 'blue',
      sortOrder: 0,
      startsAt: '',
      progressEndsAt: '',
      claimEndsAt: '',
      unlockKind: 0,
      unlockValue: '',
      completionPolicy: 0,
      premiumEnabled: false,
      premiumBoostPerMille: 1200,
      premiumInstantPoints: 0,
      premiumCostCredits: 0,
      premiumCostDiamonds: 25,
      hidden: false,
      campaignCode: '',
    };
  }

  function emptyTask(trackRowId: number): TaskForm {
    return {
      trackRowId,
      taskId: '',
      actionCode: actionOptions[0]?.name ?? '',
      parameter: '',
      mode: 0,
      premium: false,
      sortOrder: 0,
      levels: [{ requiredCount: 1, pointsReward: 10, premium: false }],
      // One step, mirroring the action above: a plain task is a sequence of one, and this is what
      // the engine builds for a task with no stored steps.
      steps: [{ actionCode: actionOptions[0]?.name ?? '', tree: [] }],
    };
  }

  function emptyPrize(trackRowId: number): PrizeForm {
    return {
      trackRowId,
      prizeId: '',
      requiredPoints: 0,
      premium: false,
      sortOrder: 0,
      rewards: [{ kind: 8, rewardTypeId: '0', amount: 100, extraParams: '', sortOrder: 0 }],
    };
  }

  function toLocal(iso: string | null | undefined) {
    if (!iso) return '';
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) return '';
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
  }

  function fromLocal(value: string) {
    if (!value) return null;
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? null : date.toISOString();
  }

  function unlockKindValue(name: string) {
    const index = ['Always', 'TrackCompleted', 'PrizeClaimed', 'BadgeOwned', 'AccountAgeDays', 'FeatureFlag'].indexOf(name);
    return index < 0 ? 0 : index;
  }

  function policyValue(name: string) {
    const index = ['AllFreePrizesClaimed', 'AllPrizesClaimed', 'MaxPointsReached', 'AllTasksCompleted'].indexOf(name);
    return index < 0 ? 0 : index;
  }

  function modeValue(name: string) {
    const index = ['Counter', 'Distinct', 'Absolute', 'Highest'].indexOf(name);
    return index < 0 ? 0 : index;
  }

  function trackBody(form: TrackForm, rowId: number | null) {
    const body = {
      trackId: form.trackId.trim(),
      theme: form.theme,
      sortOrder: Number(form.sortOrder) || 0,
      startsAt: fromLocal(form.startsAt),
      progressEndsAt: fromLocal(form.progressEndsAt),
      claimEndsAt: fromLocal(form.claimEndsAt),
      unlockKind: Number(form.unlockKind) || 0,
      unlockValue: form.unlockValue.trim(),
      completionPolicy: Number(form.completionPolicy) || 0,
      premiumEnabled: form.premiumEnabled,
      premiumBoostPerMille: Number(form.premiumBoostPerMille) || 1000,
      premiumInstantPoints: Number(form.premiumInstantPoints) || 0,
      premiumCostCredits: Number(form.premiumCostCredits) || 0,
      premiumCostDiamonds: Number(form.premiumCostDiamonds) || 0,
      hidden: form.hidden,
      campaignCode: form.campaignCode.trim(),
    };

    return rowId === null ? body : { trackRowId: rowId, ...body };
  }

  function saveTrack() {
    if (!canManage || !trackDraft) return;

    const { id, form } = trackDraft;

    ops.ask(
      id === null ? '/api/v1/operations/reward-tracks' : '/api/v1/operations/reward-tracks/update',
      trackBody(form, id),
      id === null ? $t('rewardTracks.newTrack') : $t('rewardTracks.editTrack'),
      form.trackId,
      {
        key: 'trackForm',
        valid: Boolean(form.trackId.trim()),
        invalidMessage: translate('rewardTracks.trackIdRequired'),
        onSuccess: async () => {
          trackDraft = null;
          await tracks.refresh();
        },
      }
    );
  }

  function saveTask() {
    if (!canManage || !taskDraft) return;

    const form = taskDraft.form;

    ops.ask(
      '/api/v1/operations/reward-tracks/tasks',
      {
        trackRowId: form.trackRowId,
        taskId: form.taskId.trim(),
        // Follows the first block. The engine already reads Steps[0] when a sequence exists, so
        // this is only the client's artwork key -- and an icon that disagrees with what the task
        // counts is exactly the divergence removing the separate field prevents.
        actionCode: form.steps[0]?.actionCode || form.actionCode,
        parameter: form.parameter.trim(),
        mode: Number(form.mode) || 0,
        premium: form.premium,
        sortOrder: Number(form.sortOrder) || 0,
        levels: form.levels.map((l) => ({
          requiredCount: Number(l.requiredCount) || 1,
          pointsReward: Number(l.pointsReward) || 0,
          premium: l.premium,
        })),
        steps: form.steps.map((step) => ({
          actionCode: step.actionCode,
          // The tree is what the editor holds and what the engine reads; the flat list stays in
          // the contract for the callers that still send one, and this is not one of them.
          filters: [],
          tree: toRows(step.tree ?? []),
        })),
      },
      $t('rewardTracks.saveTask'),
      form.taskId,
      {
        key: 'taskForm',
        valid: Boolean(form.taskId.trim()) && form.levels.length > 0,
        invalidMessage: translate('rewardTracks.taskIdRequired'),
        onSuccess: async () => {
          taskDraft = null;
          await tracks.refresh();
        },
      }
    );
  }

  function savePrize() {
    if (!canManage || !prizeDraft) return;

    const form = prizeDraft.form;

    ops.ask(
      '/api/v1/operations/reward-tracks/prizes',
      {
        trackRowId: form.trackRowId,
        prizeId: form.prizeId.trim(),
        requiredPoints: Number(form.requiredPoints) || 0,
        premium: form.premium,
        sortOrder: Number(form.sortOrder) || 0,
        rewards: form.rewards.map((r) => ({
          kind: Number(r.kind),
          rewardTypeId: String(r.rewardTypeId).trim(),
          amount: Number(r.amount) || 1,
          extraParams: r.extraParams ?? '',
          sortOrder: Number(r.sortOrder) || 0,
        })),
      },
      $t('rewardTracks.savePrize'),
      form.prizeId,
      {
        key: 'prizeForm',
        valid: Boolean(form.prizeId.trim()) && form.rewards.length > 0,
        invalidMessage: translate('rewardTracks.prizeIdRequired'),
        onSuccess: async () => {
          prizeDraft = null;
          await tracks.refresh();
        },
      }
    );
  }

  function lifecycle(path: string, track: RewardTrackRow, title: string) {
    ops.ask(path, { trackRowId: track.id }, title, track.trackId, {
      onSuccess: () => tracks.refresh(),
    });
  }

  function clone() {
    if (!cloneDraft) return;

    ops.ask(
      '/api/v1/operations/reward-tracks/clone',
      { trackRowId: cloneDraft.id, newTrackId: cloneDraft.newTrackId.trim() },
      $t('rewardTracks.clone'),
      cloneDraft.newTrackId,
      {
        key: 'cloneForm',
        valid: Boolean(cloneDraft.newTrackId.trim()),
        invalidMessage: translate('rewardTracks.trackIdRequired'),
        onSuccess: async () => {
          cloneDraft = null;
          await tracks.refresh();
        },
      }
    );
  }

  function addLevel() {
    const levels = taskDraft!.form.levels;
    const last = levels[levels.length - 1];

    levels.push({
      requiredCount: (Number(last?.requiredCount) || 1) * 2,
      pointsReward: Number(last?.pointsReward) || 10,
      premium: false,
    });
  }

  function addReward() {
    prizeDraft!.form.rewards.push({
      kind: 8,
      rewardTypeId: '0',
      amount: 100,
      extraParams: '',
      sortOrder: prizeDraft!.form.rewards.length,
    });
  }

  function kindHint(kindValue: number | string) {
    return kinds.find((k) => k.value === Number(kindValue))?.target ?? '';
  }
</script>

<section class="panel">
  <PageHeader title={$t('rewardTracks.title')} description={$t('rewardTracks.subtitle')}>
    {#snippet actions()}
      <button type="button" class="warning" onclick={tracks.refresh}>{$t('common.refresh')}</button>
    {/snippet}
  </PageHeader>
</section>

{#if tracks.forbidden}
  <AccessDeniedNotice />
{:else}
  <Tabs
    bind:active={tab}
    storageKey="rewardTracks"
    tabs={[
      { id: 'tracks', label: $t('rewardTracks.tabTracks'), icon: Route, count: items.length },
      { id: 'players', label: $t('rewardTracks.tabPlayers'), icon: Users },
    ]}
  />

  {#if tab === 'tracks'}
    <div class="metric-grid">
      <StatCard label={$t('rewardTracks.statTracks')} value={formatNumber(items.length)} />
      <StatCard label={$t('rewardTracks.statLive')} value={formatNumber(live)} />
      <StatCard label={$t('rewardTracks.statParticipants')} value={formatNumber(participants)} />
      <StatCard label={$t('rewardTracks.statPremium')} value={formatNumber(premiumHolders)} />
    </div>

    <div class="panel">
      <div class="panel-head">
        <h2>{$t('rewardTracks.tracks')}</h2>
        {#if canManage}
          <button
            type="button"
            class="success"
            onclick={() => (trackDraft = { id: null, form: emptyTrack() })}
          >
            {$t('rewardTracks.newTrack')}
          </button>
        {/if}
      </div>

      <div class="filters">
        <input
          autocomplete="off"
          spellcheck="false"
          type="search"
          bind:value={search}
          placeholder={$t('rewardTracks.searchPlaceholder')}
        />
      </div>

      <!-- See HabbiconsPage: an empty list while the read is in flight is not an empty list. -->
      {#if items.length === 0 && !tracks.loading}
        <EmptyState message={$t('rewardTracks.noTracks')} />
      {:else}
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{$t('rewardTracks.trackId')}</th>
                <th>{$t('rewardTracks.status')}</th>
                <th>{$t('rewardTracks.tasks')}</th>
                <th>{$t('rewardTracks.prizes')}</th>
                <th>{$t('rewardTracks.ceiling')}</th>
                <th>{$t('rewardTracks.participants')}</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {#each pageItems as track (track.id)}
                <tr>
                  <td>
                    <strong>{track.trackId}</strong>
                    {#if track.hidden}<span class="op-chip">{$t('rewardTracks.hidden')}</span>{/if}
                    <div class="muted small">{track.localizationKey}</div>
                  </td>
                  <td>{track.status}</td>
                  <td>{formatNumber(track.tasks.length)}</td>
                  <td>{formatNumber(track.prizes.length)}</td>
                  <td>
                    {formatNumber(track.freePointCeiling)}
                    {#if track.premiumEnabled}
                      / {formatNumber(track.premiumPointCeiling)}
                    {/if}
                  </td>
                  <td>
                    {formatNumber(track.participants)}
                    {#if track.premiumHolders}
                      <span class="muted small">
                        {$t('rewardTracks.premiumCount', { count: track.premiumHolders })}
                      </span>
                    {/if}
                  </td>
                  <td class="row-actions">
                    <button
                      type="button"
                      class="ghost-button"
                      onclick={() => (expanded = expanded === track.id ? null : track.id)}
                    >
                      {expanded === track.id ? $t('rewardTracks.hide') : $t('rewardTracks.content')}
                    </button>
                    {#if canManage}
                      <button
                        type="button"
                        class="ghost-button"
                        onclick={() =>
                          (trackDraft = {
                            id: track.id,
                            form: {
                              ...track,
                              unlockKind: unlockKindValue(track.unlockKind),
                              completionPolicy: policyValue(track.completionPolicy),
                              startsAt: toLocal(track.startsAt),
                              progressEndsAt: toLocal(track.progressEndsAt),
                              claimEndsAt: toLocal(track.claimEndsAt),
                            },
                          })}
                      >
                        {$t('common.edit')}
                      </button>
                      {#if track.status === 'Draft' || track.status === 'Archived'}
                        <button
                          type="button"
                          class="success"
                          onclick={() =>
                            lifecycle(
                              '/api/v1/operations/reward-tracks/publish',
                              track,
                              translate('rewardTracks.publish')
                            )}
                        >
                          {$t('rewardTracks.publish')}
                        </button>
                      {:else}
                        <button
                          type="button"
                          class="warning"
                          onclick={() =>
                            lifecycle(
                              '/api/v1/operations/reward-tracks/archive',
                              track,
                              translate('rewardTracks.archive')
                            )}
                        >
                          {$t('rewardTracks.archive')}
                        </button>
                      {/if}
                      <button
                        type="button"
                        class="ghost-button"
                        onclick={() => (cloneDraft = { id: track.id, newTrackId: '' })}
                      >
                        {$t('rewardTracks.clone')}
                      </button>
                      <button
                        type="button"
                        class="danger"
                        onclick={() =>
                          lifecycle(
                            '/api/v1/operations/reward-tracks/delete',
                            track,
                            translate('rewardTracks.deleteTrack')
                          )}
                      >
                        {$t('common.delete')}
                      </button>
                    {/if}
                  </td>
                </tr>
                {#if expanded === track.id}
                  <tr>
                    <td colspan="7">
                      <div class="panel-head">
                        <h3>{$t('rewardTracks.tasksOf', { track: track.trackId })}</h3>
                        {#if canManage}
                          <button
                            type="button"
                            class="success"
                            onclick={() => (taskDraft = { id: null, form: emptyTask(track.id) })}
                          >
                            {$t('rewardTracks.newTask')}
                          </button>
                        {/if}
                      </div>
                      <table>
                        <thead>
                          <tr>
                            <th>{$t('rewardTracks.taskId')}</th>
                            <th>{$t('rewardTracks.action')}</th>
                            <th>{$t('rewardTracks.mode')}</th>
                            <th>{$t('rewardTracks.stages')}</th>
                            <th>{$t('rewardTracks.matched')}</th>
                            <th></th>
                          </tr>
                        </thead>
                        <tbody>
                          {#each track.tasks as task (task.id)}
                            <tr>
                              <td>
                                {task.taskId}
                                {#if task.premium}<span class="tag-premium">{$t('rewardTracks.premium')}</span>{/if}
                              </td>
                              <td>
                                {task.actionCode}
                                {#if !task.wired}
                                  <span class="status-badge status-badge--bad">{$t('rewardTracks.notWired')}</span>
                                {/if}
                              </td>
                              <td>{task.mode}</td>
                              <td>
                                <div class="chips">
                                  {#each task.levels as level (level.levelIndex)}
                                    <span class="op-chip">
                                      {level.requiredCount} → {level.pointsReward}
                                    </span>
                                  {/each}
                                </div>
                              </td>
                              <td>
                                <!-- The number that says whether this works. `wired` says the
                                     action is raised by something; this says the task itself,
                                     with its filters, has been satisfied by a real player. -->
                                {#if task.players > 0}
                                  <span
                                    title={task.lastMatchedAt
                                      ? new Date(task.lastMatchedAt).toLocaleString()
                                      : ''}
                                  >
                                    {$t('rewardTracks.matchedPlayers', { n: task.players })}
                                  </span>
                                {:else if task.wired}
                                  <span class="status-badge status-badge--bad">
                                    {$t('rewardTracks.neverMatched')}
                                  </span>
                                {:else}
                                  <span class="muted">—</span>
                                {/if}
                              </td>
                              <td class="row-actions">
                                {#if canManage}
                                  <button
                                    type="button"
                                    class="ghost-button"
                                    onclick={() =>
                                      (taskDraft = {
                                        id: null,
                                        form: {
                                          ...task,
                                          trackRowId: track.id,
                                          mode: modeValue(task.mode),
                                          levels: task.levels.map((l) => ({ ...l })),
                                          // Copied, not aliased: the drawer edits these in place
                                          // and a cancel has to leave the row alone. A task with
                                          // no stored steps gets the one the engine would build.
                                          steps: (task.steps ?? []).length
                                            ? task.steps.map((st) => ({
                                                actionCode: st.actionCode,
                                                tree: fromRows(st.tree),
                                              }))
                                            : [{ actionCode: task.actionCode, tree: [] }],
                                        },
                                      })}
                                  >
                                    {$t('common.edit')}
                                  </button>
                                  <button
                                    type="button"
                                    class="danger"
                                    onclick={() =>
                                      ops.ask(
                                        '/api/v1/operations/reward-tracks/tasks/delete',
                                        { taskRowId: task.id },
                                        translate('rewardTracks.deleteTask'),
                                        task.taskId,
                                        { onSuccess: () => tracks.refresh() }
                                      )}
                                  >
                                    {$t('common.delete')}
                                  </button>
                                {/if}
                              </td>
                            </tr>
                          {/each}
                        </tbody>
                      </table>

                      <div class="panel-head">
                        <h3>{$t('rewardTracks.prizesOf', { track: track.trackId })}</h3>
                        {#if canManage}
                          <button
                            type="button"
                            class="success"
                            onclick={() => (prizeDraft = { id: null, form: emptyPrize(track.id) })}
                          >
                            {$t('rewardTracks.newPrize')}
                          </button>
                        {/if}
                      </div>
                      <table>
                        <thead>
                          <tr>
                            <th>{$t('rewardTracks.prizeId')}</th>
                            <th>{$t('rewardTracks.requiredPoints')}</th>
                            <th>{$t('rewardTracks.rewards')}</th>
                            <th></th>
                          </tr>
                        </thead>
                        <tbody>
                          {#each track.prizes as prize (prize.id)}
                            <tr>
                              <td>
                                {prize.prizeId}
                                {#if prize.premium}<span class="tag-premium">{$t('rewardTracks.premium')}</span>{/if}
                              </td>
                              <td>
                                {formatNumber(prize.requiredPoints)}
                                {#if !prize.reachable}
                                  <span class="status-badge status-badge--bad">{$t('rewardTracks.unreachable')}</span>
                                {/if}
                              </td>
                              <td>
                                <div class="chips">
                                  {#each prize.rewards as reward (reward.id)}
                                    <span class="op-chip">
                                      {reward.kind}:{reward.rewardTypeId} ×{reward.amount}
                                    </span>
                                  {/each}
                                </div>
                              </td>
                              <td class="row-actions">
                                {#if canManage}
                                  <button
                                    type="button"
                                    class="ghost-button"
                                    onclick={() =>
                                      (prizeDraft = {
                                        id: null,
                                        form: {
                                          ...prize,
                                          trackRowId: track.id,
                                          rewards: prize.rewards.map((r) => ({
                                            ...r,
                                            kind: r.kindValue,
                                          })),
                                        },
                                      })}
                                  >
                                    {$t('common.edit')}
                                  </button>
                                  <button
                                    type="button"
                                    class="danger"
                                    onclick={() =>
                                      ops.ask(
                                        '/api/v1/operations/reward-tracks/prizes/delete',
                                        { prizeRowId: prize.id },
                                        translate('rewardTracks.deletePrize'),
                                        prize.prizeId,
                                        { onSuccess: () => tracks.refresh() }
                                      )}
                                  >
                                    {$t('common.delete')}
                                  </button>
                                {/if}
                              </td>
                            </tr>
                          {/each}
                        </tbody>
                      </table>
                    </td>
                  </tr>
                {/if}
              {/each}
            </tbody>
          </table>
        </div>

        {#if pageCount > 1}
          <Pagination
            {page}
            {pageCount}
            total={items.length}
            pageSize={PAGE_SIZE}
            label={$t('rewardTracks.paginationLabel')}
            pageWord={$t('common.page')}
            prevLabel={$t('common.prev')}
            nextLabel={$t('common.next')}
            onchange={(next) => (page = next)}
          />
        {/if}
      {/if}
    </div>
  {/if}

  {#if tab === 'players'}
    <div class="panel">
      <div class="panel-head">
        <h2>{$t('rewardTracks.playerLookup')}</h2>
        <button type="button" class="ghost-button" onclick={() => (pickingPlayer = true)}>
          {player ? player.name : $t('rewardTracks.pickPlayer')}
        </button>
      </div>

      {#if !player}
        <EmptyState message={$t('rewardTracks.pickPlayerHint')} />
      {:else if progress.loading}
        <p class="muted">{$t('common.loading')}</p>
      {:else if (progress.data?.items ?? []).length === 0}
        <EmptyState message={$t('rewardTracks.noProgress')} />
      {:else}
        {#each progress.data?.items ?? [] as row (row.trackId)}
          {@const target = player!}
          <div class="panel">
            <div class="panel-head">
              <h3>{row.trackId}</h3>
              {#if canManage}
                <div class="row-actions">
                  {#if !row.premiumUnlocked}
                    <button
                      type="button"
                      class="success"
                      onclick={() =>
                        ops.ask(
                          '/api/v1/operations/reward-tracks/players/grant-premium',
                          { playerId: target.id, trackId: row.trackId },
                          translate('rewardTracks.grantPremium'),
                          `${target.name} · ${row.trackId}`,
                          { onSuccess: () => progress.refresh() }
                        )}
                    >
                      {$t('rewardTracks.grantPremium')}
                    </button>
                  {/if}
                  <button
                    type="button"
                    class="danger"
                    onclick={() =>
                      ops.ask(
                        '/api/v1/operations/reward-tracks/players/reset',
                        { playerId: target.id, trackId: row.trackId },
                        translate('rewardTracks.reset'),
                        `${target.name} · ${row.trackId}`,
                        { onSuccess: () => progress.refresh() }
                      )}
                  >
                    {$t('rewardTracks.reset')}
                  </button>
                </div>
              {/if}
            </div>

            <p class="muted">
              {$t('rewardTracks.playerSummary', {
                points: formatNumber(row.points),
                premium: row.premiumUnlocked ? $t('common.yes') : $t('common.no'),
                completed: row.completedAt ? formatDate(row.completedAt) : '—',
              })}
            </p>

            <table>
              <thead>
                <tr>
                  <th>{$t('rewardTracks.taskId')}</th>
                  <th>{$t('rewardTracks.progress')}</th>
                  <th>{$t('rewardTracks.highestPaid')}</th>
                </tr>
              </thead>
              <tbody>
                {#each row.tasks as task (task.taskId)}
                  <tr>
                    <td>{task.taskId}</td>
                    <td>{formatNumber(task.progressCount)}</td>
                    <td>{task.highestPaidLevelIndex}</td>
                  </tr>
                {/each}
              </tbody>
            </table>

            {#if row.claims.length > 0}
              <table>
                <thead>
                  <tr>
                    <th>{$t('rewardTracks.prizeId')}</th>
                    <th>{$t('rewardTracks.claimedAt')}</th>
                    <th>{$t('rewardTracks.pointsAtClaim')}</th>
                    <th>{$t('rewardTracks.granted')}</th>
                  </tr>
                </thead>
                <tbody>
                  {#each row.claims as claim (claim.prizeId)}
                    <tr>
                      <td>{claim.prizeId}</td>
                      <td>{formatDate(claim.claimedAt)}</td>
                      <td>{formatNumber(claim.pointsAtClaim)}</td>
                      <td class="muted small">{claim.granted}</td>
                    </tr>
                  {/each}
                </tbody>
              </table>
            {/if}
          </div>
        {/each}
      {/if}
    </div>
  {/if}
{/if}

{#if trackDraft}
  <Drawer
    title={trackDraft.id === null ? $t('rewardTracks.newTrack') : $t('rewardTracks.editTrack')}
    eyebrow={$t('rewardTracks.title')}
    onclose={() => (trackDraft = null)}
  >
    <label>
      {$t('rewardTracks.trackId')}
      <input type="text" bind:value={trackDraft.form.trackId} disabled={trackDraft.id !== null} />
    </label>
    <p class="muted small">{$t('rewardTracks.trackIdHint')}</p>
    <label>
      {$t('rewardTracks.theme')}
      <select bind:value={trackDraft.form.theme}>
        {#each THEMES as theme (theme)}
          <option value={theme}>{theme}</option>
        {/each}
      </select>
    </label>
    <label>
      {$t('rewardTracks.campaign')}
      <input type="text" bind:value={trackDraft.form.campaignCode} />
    </label>
    <label>
      {$t('rewardTracks.sortOrder')}
      <input type="number" bind:value={trackDraft.form.sortOrder} />
    </label>
    <label>
      {$t('rewardTracks.startsAt')}
      <input type="datetime-local" bind:value={trackDraft.form.startsAt} />
    </label>
    <label>
      {$t('rewardTracks.progressEndsAt')}
      <input type="datetime-local" bind:value={trackDraft.form.progressEndsAt} />
    </label>
    <label>
      {$t('rewardTracks.claimEndsAt')}
      <input type="datetime-local" bind:value={trackDraft.form.claimEndsAt} />
    </label>
    <p class="muted small">{$t('rewardTracks.windowHint')}</p>
    <label>
      {$t('rewardTracks.unlock')}
      <select bind:value={trackDraft.form.unlockKind}>
        {#each UNLOCK_KINDS as kind (kind.value)}
          <option value={kind.value}>{$t(kind.key)}</option>
        {/each}
      </select>
    </label>
    {#if Number(trackDraft.form.unlockKind) !== 0}
      <label>
        {$t('rewardTracks.unlockValue')}
        <input type="text" bind:value={trackDraft.form.unlockValue} />
      </label>
    {/if}
    <label>
      {$t('rewardTracks.completionPolicy')}
      <select bind:value={trackDraft.form.completionPolicy}>
        {#each COMPLETION_POLICIES as policy (policy.value)}
          <option value={policy.value}>{$t(policy.key)}</option>
        {/each}
      </select>
    </label>
    <label class="checkbox">
      <input type="checkbox" bind:checked={trackDraft.form.hidden} />
      {$t('rewardTracks.hiddenLabel')}
    </label>
    <label class="checkbox">
      <input type="checkbox" bind:checked={trackDraft.form.premiumEnabled} />
      {$t('rewardTracks.premiumEnabled')}
    </label>
    {#if trackDraft.form.premiumEnabled}
      <label>
        {$t('rewardTracks.boost')}
        <input type="number" bind:value={trackDraft.form.premiumBoostPerMille} />
      </label>
      <p class="muted small">{$t('rewardTracks.boostHint')}</p>
      <label>
        {$t('rewardTracks.instantPoints')}
        <input type="number" bind:value={trackDraft.form.premiumInstantPoints} />
      </label>
      <label>
        {$t('rewardTracks.costCredits')}
        <input type="number" bind:value={trackDraft.form.premiumCostCredits} />
      </label>
      <label>
        {$t('rewardTracks.costDiamonds')}
        <input type="number" bind:value={trackDraft.form.premiumCostDiamonds} />
      </label>
    {/if}

    {#snippet actions()}
      <button type="button" onclick={saveTrack}>{$t('common.save')}</button>
      <button type="button" class="ghost-button" onclick={() => (trackDraft = null)}>
        {$t('common.cancel')}
      </button>
    {/snippet}
  </Drawer>
{/if}

{#if taskDraft}
  {@const draft = taskDraft}
  <Drawer
    title={$t('rewardTracks.saveTask')}
    eyebrow={$t('rewardTracks.title')}
    onclose={() => (taskDraft = null)}
  >
    <label>
      {$t('rewardTracks.taskId')}
      <input type="text" bind:value={draft.form.taskId} />
    </label>
    <p class="muted small">{$t('rewardTracks.taskIdHint')}</p>
    <label>
      {$t('rewardTracks.mode')}
      <select bind:value={draft.form.mode}>
        {#each MODES as mode (mode.value)}
          <option value={mode.value}>{$t(mode.key)}</option>
        {/each}
      </select>
    </label>
    <label>
      {$t('rewardTracks.parameter')}
      <input type="text" bind:value={draft.form.parameter} />
    </label>
    <p class="muted small">{$t('rewardTracks.parameterHint')}</p>

    <!--
      The sequence. A plain task is one step; adding a second is what turns "place a sofa" into
      "place a sofa, then walk on it". Filters inside a step are ANDed, and a filter's value can
      point back at an earlier step with $N -- which is what "the same furniture" means.
    -->
    <h4 class="drawer-section">{$t('rewardTracks.sequence')}</h4>
    <p class="muted small">{$t('rewardTracks.sequenceHint')}</p>

    <!-- A summary and a button. The editor itself is full screen: a condition is four controls on
         one line, and the drawer is a column narrow enough that every one of them wrapped. -->
    <div class="sequence-summary">
      <span>
        {$t('rewardTracks.sequenceSummary', {
          actions: draft.form.steps.length,
          conditions: draft.form.steps.reduce((n, s) => n + tests(s.tree ?? []).length, 0),
        })}
      </span>
      <button type="button" class="ghost-button" onclick={() => (sequenceOpen = true)}>
        <Blocks size={14} />
        {$t('rewardTracks.openSequence')}
      </button>
    </div>

    <label class="checkbox premium-task">
      <input type="checkbox" bind:checked={draft.form.premium} />
      {$t('rewardTracks.premiumTask')}
    </label>

    <h4 class="drawer-section">{$t('rewardTracks.stages')}</h4>
    {#each draft.form.levels as level, index (index)}
      <div class="level-row">
        <span class="row-label">#{index + 1}</span>
        <input type="number" bind:value={level.requiredCount} placeholder={$t('rewardTracks.required')} />
        <input type="number" bind:value={level.pointsReward} placeholder={$t('rewardTracks.points')} />
        <label class="checkbox">
          <input type="checkbox" bind:checked={level.premium} />
          {$t('rewardTracks.premium')}
        </label>
        <button
          type="button"
          class="ghost-button block-remove"
          title={$t('common.remove')}
          onclick={() => draft.form.levels.splice(index, 1)}
          disabled={draft.form.levels.length === 1}
        >
          ×
        </button>
      </div>
    {/each}
    <button type="button" class="ghost-button" onclick={addLevel}>{$t('rewardTracks.addStage')}</button>

    {#snippet actions()}
      <button type="button" onclick={saveTask}>{$t('common.save')}</button>
      <button type="button" class="ghost-button" onclick={() => (taskDraft = null)}>
        {$t('common.cancel')}
      </button>
    {/snippet}
  </Drawer>
{/if}

{#if prizeDraft}
  {@const draft = prizeDraft}
  <Drawer
    title={$t('rewardTracks.savePrize')}
    eyebrow={$t('rewardTracks.title')}
    onclose={() => (prizeDraft = null)}
  >
    <label>
      {$t('rewardTracks.prizeId')}
      <input type="text" bind:value={draft.form.prizeId} />
    </label>
    <p class="muted small">{$t('rewardTracks.prizeIdHint')}</p>
    <label>
      {$t('rewardTracks.requiredPoints')}
      <input type="number" bind:value={draft.form.requiredPoints} />
    </label>
    <label class="checkbox">
      <input type="checkbox" bind:checked={draft.form.premium} />
      {$t('rewardTracks.premiumPrize')}
    </label>

    <h4>{$t('rewardTracks.rewards')}</h4>
    <p class="muted small">{$t('rewardTracks.bundleHint')}</p>
    {#each draft.form.rewards as reward, index (index)}
      <div class="reward-row">
        <select bind:value={reward.kind}>
          {#each kinds as kind (kind.value)}
            <option value={kind.value}>{kind.name}</option>
          {/each}
        </select>
        <input type="text" bind:value={reward.rewardTypeId} placeholder={kindHint(reward.kind)} />
        <input type="number" bind:value={reward.amount} placeholder={$t('rewardTracks.amount')} />
        <button
          type="button"
          class="danger"
          onclick={() => draft.form.rewards.splice(index, 1)}
          disabled={draft.form.rewards.length === 1}
        >
          {$t('common.remove')}
        </button>
        <span class="muted small">{kindHint(reward.kind)}</span>
      </div>
    {/each}
    <button type="button" class="ghost-button" onclick={addReward}>
      {$t('rewardTracks.addReward')}
    </button>

    {#snippet actions()}
      <button type="button" onclick={savePrize}>{$t('common.save')}</button>
      <button type="button" class="ghost-button" onclick={() => (prizeDraft = null)}>
        {$t('common.cancel')}
      </button>
    {/snippet}
  </Drawer>
{/if}

{#if cloneDraft}
  <Drawer
    title={$t('rewardTracks.clone')}
    eyebrow={$t('rewardTracks.title')}
    onclose={() => (cloneDraft = null)}
  >
    <label>
      {$t('rewardTracks.newTrackId')}
      <input type="text" bind:value={cloneDraft.newTrackId} />
    </label>
    <p class="muted small">{$t('rewardTracks.cloneHint')}</p>

    {#snippet actions()}
      <button type="button" onclick={clone}>{$t('rewardTracks.clone')}</button>
      <button type="button" class="ghost-button" onclick={() => (cloneDraft = null)}>
        {$t('common.cancel')}
      </button>
    {/snippet}
  </Drawer>
{/if}

{#if taskDraft}
  {@const draft = taskDraft}
  <SequenceWorkspace
    open={sequenceOpen}
    title={$t('rewardTracks.sequence')}
    onclose={() => (sequenceOpen = false)}
    steps={draft.form.steps}
    actions={actionOptions}
    {canManage}
    {factsFor}
    {operatorsFor}
    {referencesFor}
    {defaultFilterValue}
    {pickerFor}
    {pickedLabels}
    onchange={(next: StepForm[]) => {
      if (taskDraft) draft.form.steps = next;
    }}
    onpick={(stepIndex: number, path: NodePath, kind: string) =>
      (pickingFilter = { stepIndex, path, kind })}
  />
{/if}

{#if pickingFilter}
  <PickerModal
    kind={pickingFilter.kind}
    title={$t('rewardTracks.pickValue')}
    onSelect={(item: PickerRow) => {
      const { stepIndex, path } = pickingFilter!;
      const step = taskDraft!.form.steps[stepIndex];
      const node = nodeAt(step.tree ?? [], path);

      // The tree is rebuilt on every edit, so the path is the only thing that still names the
      // condition the operator opened this picker from. It can name nothing if they deleted it
      // while the picker was open.
      if (!node || node.kind !== TEST) {
        pickingFilter = null;
        return;
      }

      // What the signal carries, which is not always the row's id: a Habbicon collection is
      // filtered by its code, and a badge by the code itself.
      const picked = String(item.value ?? item.id);

      taskDraft!.form.steps[stepIndex] = {
        ...step,
        tree: replaceAt(step.tree ?? [], path, {
          ...node,
          // "One of" is a list, so a pick adds to it; the other operators hold a single value.
          value: Number(node.op) === 2 && node.value ? `${node.value},${picked}` : picked,
        }),
      };

      pickedLabels[pathKey(stepIndex, path)] = { name: item.name, iconUrl: item.iconUrl };
      pickingFilter = null;
    }}
    onClose={() => (pickingFilter = null)}
  />
{/if}

{#if pickingPlayer}
  <PickerModal
    kind="user"
    title={$t('rewardTracks.pickPlayer')}
    onSelect={(picked: PickerRow) => {
      player = { id: Number(picked.id), name: picked.name };
      pickingPlayer = false;
    }}
    onClose={() => (pickingPlayer = false)}
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

<OpResult result={$ops.result} />

<LoadingOverlay show={tracks.loading} />

<style>
  /* `.row-actions`, `.table-wrap` and the chips come from styles.css; only what the sheet has no
     opinion about is here. */
  .small {
    font-size: 0.8em;
  }

  .filters {
    display: flex;
    gap: 0.5rem;
  }

  /* Same section rule as the article editor's, so a drawer that grew a second half still reads as
     one form rather than two stacked ones. */
  .drawer-section {
    margin: 18px 0 6px;
    padding-top: 14px;
    border-top: 1px solid var(--line);
  }

  /* A premium mark is a tag on an id, not a control. `.op-chip` is sized for a pill in a chip row
     -- 2px border, 11px padding, body text -- so beside a task id it outweighed the id it labels.
     Gold, because that is what premium is in the hotel. */
  .tag-premium {
    display: inline-block;
    margin-left: 6px;
    padding: 1px 6px;
    border: 1px solid rgba(var(--gold-rgb), 0.45);
    border-radius: 4px;
    background: var(--gold-soft);
    color: var(--gold);
    font-size: 0.66rem;
    font-weight: 700;
    letter-spacing: 0.07em;
    text-transform: uppercase;
    vertical-align: middle;
    white-space: nowrap;
  }

  /* Two tables live under one expanded track. The milestones heading sat flush against the last
     task row, so nothing said where the tasks ended and the rewards began. */
  table + .panel-head {
    margin-top: 26px;
    padding-top: 18px;
    border-top: 2px solid var(--line-strong);
  }

  /* The sequence is assembled, not filled in: an action is a block, each of its conditions is a
     block clipped under it, and the two "add" buttons are the empty slots they drop into. Notch,
     indent and hue carry the structure. No drag-and-drop: a filter can point back at an earlier
     step with $N, so reordering would have to rewrite those references -- reading order is the
     order. */
    .level-row,
  .reward-row {
    display: flex;
    gap: 0.5rem;
    align-items: center;
    margin-bottom: 0.4rem;
    flex-wrap: wrap;
  }

  /* One card per stage. Bare, the drawer stretches every field to full width, so three stages came
     out as nine stacked inputs with nothing saying where one stage ended. Here the two numbers
     share the line and the card is the boundary. */
  .level-row {
    margin-bottom: 8px;
    padding: 8px 10px;
    border: 1px solid var(--line);
    border-radius: 6px;
    background: var(--surface-strong);
  }

  .level-row input[type='number'] {
    flex: 1 1 4rem;
    width: auto;
    min-width: 0;
  }

  .row-label {
    flex: 0 0 auto;
    font-size: 0.72rem;
    font-weight: 700;
    color: var(--muted);
  }

  /* Its own line: the sequence's last slot is a button, and inline-flex put this checkbox beside
     it as if it belonged to the block. */
  .premium-task {
    margin-top: 12px;
  }

  /* A row of pills in a table cell. Without this they butt together and "1 → 10" "5 → 20" reads as
     one number: the stage ladder was rendering as "1 → 105 → 20". */
  .chips {
    display: flex;
    gap: 4px;
    flex-wrap: wrap;
  }
</style>
