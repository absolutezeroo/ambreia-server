<script lang="ts">
  import { onMount } from 'svelte';
  import { CalendarCheck, Gift, Target, Trophy } from '@lucide/svelte';
  import AccessDeniedNotice from '../AccessDeniedNotice.svelte';
  import ConfirmReasonModal from '../ConfirmReasonModal.svelte';
  import ConfirmStagedModal from '../ConfirmStagedModal.svelte';
  import OpResult from '../OpResult.svelte';
  import { apiGet } from '../../lib/api';
  import { formatDate, formatNumber } from '../../lib/format';
  import { CAPABILITIES } from '../../lib/dashboardPermissions';
  import { hasDashboardCapability, isPermissionDeniedError } from '../../lib/permissions';
  import { identity } from '../../lib/session';
  import { t, translate } from '../../lib/i18n';
  import { createWriteOps } from '../../lib/writeOps';
  import type {
    CommunityGoalList,
    CommunityGoalRow,
    DailyTaskList,
    DailyTaskRow,
  } from '../../lib/apiTypes';

  /** A community goal being edited. Number inputs hand back strings while being typed. */
  type GoalForm = {
    id?: number;
    code: string;
    campaignCode: string;
    scorePerQuest: number | string;
    enabled: boolean;
    endsAt: string;
    sortOrder: number | string;
    levels: LevelForm[];
    reason: string;
  };

  type LevelForm = {
    scoreThreshold: number | string;
    rewardUserLimit: number | string;
  };

  /** A daily task being edited. */
  type TaskForm = {
    id?: number;
    taskCode: string;
    questTypeCode: string;
    isBonus: boolean;
    imageVersion: string;
    catalogName: string;
    requiredRepeats: number | string;
    enabled: boolean;
    sortOrder: number | string;
    rewards: RewardForm[];
    reason: string;
  };

  type RewardForm = {
    productItemTypeId: number | string;
    rewardTypeId: string;
    extraParams: string;
    amount: number | string;
  };

  // Community goals and daily tasks share the quest capability: same domain, same operators, and a
  // brand new capability would have to be granted to every role before anyone could open the page.

  function emptyGoal(): GoalForm {
    return {
      code: '',
      campaignCode: '',
      scorePerQuest: 1,
      enabled: true,
      endsAt: '',
      sortOrder: 0,
      levels: [emptyLevel()],
      reason: '',
    };
  }

  function emptyLevel(): LevelForm {
    return { scoreThreshold: 0, rewardUserLimit: 0 };
  }

  function emptyTask(): TaskForm {
    return {
      taskCode: '',
      questTypeCode: '',
      isBonus: false,
      imageVersion: '',
      catalogName: '',
      requiredRepeats: 1,
      enabled: true,
      sortOrder: 0,
      rewards: [emptyReward()],
      reason: '',
    };
  }

  function emptyReward(): RewardForm {
    return {
      productItemTypeId: 0,
      rewardTypeId: 'credits',
      extraParams: '',
      amount: 0,
    };
  }

  let goals = $state<CommunityGoalRow[]>([]);
  let tasks = $state<DailyTaskRow[]>([]);
  let questTypes = $state<string[]>([]);
  let loading = $state(false);
  let error = $state('');
  let forbidden = $state(false);

  let newGoal = $state<GoalForm | null>(null);
  let editGoal = $state<GoalForm | null>(null);
  let newTask = $state<TaskForm | null>(null);
  let editTask = $state<TaskForm | null>(null);

  const ops = createWriteOps();
  const deleteOps = createWriteOps();

  let canManage = $derived(hasDashboardCapability($identity, CAPABILITIES.opsQuestsManage));

  async function load() {
    loading = true;
    error = '';
    forbidden = false;

    try {
      const [goalData, taskData] = await Promise.all([
        apiGet<CommunityGoalList>('/api/v1/community-goals'),
        apiGet<DailyTaskList>('/api/v1/daily-tasks'),
      ]);

      goals = goalData.items || [];
      tasks = taskData.items || [];
      questTypes = taskData.questTypes || [];
    } catch (err) {
      if (isPermissionDeniedError(err)) {
        forbidden = true;
        goals = [];
        tasks = [];
        return;
      }

      error = (err as Error).message;
    } finally {
      loading = false;
    }
  }

  // The API round-trips endsAt as an ISO instant (or null); <input type="datetime-local"> wants a
  // local `yyyy-MM-ddThh:mm`. An empty field means "no deadline", which is null on the wire.
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

  function goalBody(form: GoalForm, goalId: number | null) {
    const body = {
      code: form.code.trim(),
      campaignCode: form.campaignCode.trim(),
      scorePerQuest: Number(form.scorePerQuest) || 1,
      enabled: form.enabled,
      endsAt: fromLocal(form.endsAt),
      sortOrder: Number(form.sortOrder) || 0,
      // Rows the operator started and left blank are dropped rather than saved as a rung at zero.
      levels: form.levels
        .filter(
          (l) =>
            Number(l.scoreThreshold) > 0 ||
            Number(l.rewardUserLimit) > 0,
        )
        .map((l) => ({
          scoreThreshold: Number(l.scoreThreshold) || 0,
          rewardUserLimit: Number(l.rewardUserLimit) || 0,
        })),
      reason: form.reason.trim(),
    };

    return goalId === null ? body : { goalId, ...body };
  }

  function goalValid(form: GoalForm) {
    const levels = form.levels.filter((l) => Number(l.scoreThreshold) > 0);
    const thresholds = new Set(levels.map((l) => Number(l.scoreThreshold)));

    return (
      Boolean(form.code.trim()) &&
      levels.length > 0 &&
      thresholds.size === levels.length &&
      form.reason.trim().length >= 3
    );
  }

  function taskBody(form: TaskForm, taskId: number | null) {
    const body = {
      taskCode: form.taskCode.trim(),
      questTypeCode: form.questTypeCode.trim(),
      isBonus: form.isBonus,
      imageVersion: form.imageVersion.trim(),
      catalogName: form.catalogName.trim(),
      requiredRepeats: Number(form.requiredRepeats) || 1,
      enabled: form.enabled,
      sortOrder: Number(form.sortOrder) || 0,
      rewards: form.rewards
        .filter((r) => Number(r.amount) > 0)
        .map((r) => ({
          productItemTypeId: Number(r.productItemTypeId) || 0,
          rewardTypeId: r.rewardTypeId.trim(),
          extraParams: r.extraParams.trim(),
          amount: Number(r.amount) || 0,
        })),
      reason: form.reason.trim(),
    };

    return taskId === null ? body : { taskId, ...body };
  }

  function taskValid(form: TaskForm) {
    return (
      Boolean(form.taskCode.trim()) &&
      Boolean(form.questTypeCode.trim()) &&
      Number(form.requiredRepeats) >= 1 &&
      form.rewards.every((r) => Number(r.amount) <= 0 || Boolean(r.rewardTypeId.trim())) &&
      form.reason.trim().length >= 3
    );
  }

  function stage(
    key: string,
    title: string,
    endpoint: string,
    valid: boolean,
    body: Record<string, unknown> & { reason: string },
    summary: string,
    onSuccess: () => void | Promise<void>,
  ) {
    ops.ask(endpoint, body, title, summary, {
      key,
      valid,
      invalidMessage: translate('questContent.fillFields'),
      reason: body.reason,
      onSuccess,
    });
  }

  function saveGoal() {
    const isEdit = editGoal !== null;
    const form = (isEdit ? editGoal : newGoal)!;

    stage(
      isEdit ? `goal:${editGoal!.id}` : 'goalCreate',
      isEdit ? translate('questContent.editGoal') : translate('questContent.newGoal'),
      isEdit ? '/api/v1/operations/community-goals/update' : '/api/v1/operations/community-goals',
      goalValid(form),
      goalBody(form, isEdit ? editGoal!.id! : null),
      form.code.trim(),
      async () => {
        newGoal = null;
        editGoal = null;
        await load();
      },
    );
  }

  function saveTask() {
    const isEdit = editTask !== null;
    const form = (isEdit ? editTask : newTask)!;

    stage(
      isEdit ? `task:${editTask!.id}` : 'taskCreate',
      isEdit ? translate('questContent.editTask') : translate('questContent.newTask'),
      isEdit ? '/api/v1/operations/daily-tasks/update' : '/api/v1/operations/daily-tasks',
      taskValid(form),
      taskBody(form, isEdit ? editTask!.id! : null),
      form.taskCode.trim(),
      async () => {
        newTask = null;
        editTask = null;
        await load();
      },
    );
  }

  function startEditGoal(goal: CommunityGoalRow) {
    newGoal = null;
    editGoal = {
      id: goal.id,
      code: goal.code,
      campaignCode: goal.campaignCode || '',
      scorePerQuest: goal.scorePerQuest,
      enabled: goal.enabled,
      endsAt: toLocal(goal.endsAt),
      sortOrder: goal.sortOrder,
      levels:
        goal.levels?.length > 0
          ? goal.levels.map((l) => ({
              scoreThreshold: l.scoreThreshold,
              rewardUserLimit: l.rewardUserLimit,
            }))
          : [emptyLevel()],
      reason: '',
    };
  }

  function startEditTask(task: DailyTaskRow) {
    newTask = null;
    editTask = {
      id: task.id,
      taskCode: task.taskCode,
      questTypeCode: task.questTypeCode,
      isBonus: task.isBonus,
      imageVersion: task.imageVersion || '',
      catalogName: task.catalogName || '',
      requiredRepeats: task.requiredRepeats,
      enabled: task.enabled,
      sortOrder: task.sortOrder,
      rewards:
        task.rewards?.length > 0
          ? task.rewards.map((r) => ({
              productItemTypeId: r.productItemTypeId,
              rewardTypeId: r.rewardTypeId,
              extraParams: r.extraParams || '',
              amount: r.amount,
            }))
          : [emptyReward()],
      reason: '',
    };
  }

  // Staged on the click; the modal collects the reason and confirm() merges it into the body. A
  // refused delete (goal_has_contributions / task_has_assignments) keeps the modal open with the
  // code showing, which is the whole point of routing it through the store.
  function askDelete(kind: string, id: number, label: string) {
    const isGoal = kind === 'goal';

    deleteOps.ask(
      isGoal ? '/api/v1/operations/community-goals/delete' : '/api/v1/operations/daily-tasks/delete',
      isGoal ? { goalId: id } : { taskId: id },
      isGoal ? translate('questContent.deleteGoal') : translate('questContent.deleteTask'),
      label,
      { danger: true, onSuccess: load },
    );
  }

  onMount(load);
</script>

<section class="panel">
  <div class="panel-head">
    <h2>{$t('questContent.title')}</h2>
    <button type="button" class="warning" onclick={load} disabled={loading}>
      {$t('common.refresh')}
    </button>
  </div>
  <p class="muted">{$t('questContent.description')}</p>
</section>

{#if forbidden}
  <AccessDeniedNotice message={$t('questContent.accessDenied')} />
{:else}
  {#if error}
    <p class="empty-state danger" role="alert">{error}</p>
  {/if}

  <section class="panel">
    <div class="panel-head">
      <h2><Trophy size={17} strokeWidth={2} aria-hidden="true" /> {$t('questContent.goalsHeading')}</h2>
      {#if canManage}
        <button
          type="button" class="success"
          onclick={() => {
            editGoal = null;
            newGoal = newGoal ? null : emptyGoal();
          }}
        >
          {$t('questContent.newGoal')}
        </button>
      {/if}
    </div>
    <p class="muted">{$t('questContent.goalsHint')}</p>

    {#if newGoal}
      <div class="editor">
        <div class="op-field">
          <label for="goal-code">{$t('questContent.codeRequired')}</label>
          <input autocomplete="off" spellcheck="false" id="goal-code" bind:value={newGoal.code} placeholder="summer_build" />
        </div>
        <div class="op-field">
          <label for="goal-campaign">{$t('questContent.campaignCode')}</label>
          <input autocomplete="off" spellcheck="false" id="goal-campaign" bind:value={newGoal.campaignCode} />
          <small class="muted">{$t('questContent.campaignHint')}</small>
        </div>
        <div class="op-field">
          <label for="goal-ends">{$t('questContent.endsAt')}</label>
          <input autocomplete="off" spellcheck="false" id="goal-ends" type="datetime-local" bind:value={newGoal.endsAt} />
        </div>
        <div class="op-field">
          <label for="goal-sort">{$t('questContent.sortOrder')}</label>
          <input autocomplete="off" spellcheck="false" id="goal-sort" type="number" bind:value={newGoal.sortOrder} />
        </div>
        <div class="op-field">
          <label><input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={newGoal.enabled} /> {$t('questContent.enabled')}</label>
        </div>

        <fieldset class="op-subgroup">
          <legend>{$t('questContent.ladderLegend')}</legend>
          {#each newGoal!.levels as level, index}
            <div class="row-grid">
              <input autocomplete="off" spellcheck="false" type="number" min="0" placeholder={$t('questContent.threshold')} bind:value={level.scoreThreshold} />
              <input autocomplete="off" spellcheck="false" type="number" min="0" placeholder={$t('questContent.rewardLimit')} bind:value={level.rewardUserLimit} />
              <button
                type="button"
                class="ghost-button danger"
                onclick={() => {
                  newGoal!.levels = newGoal!.levels.filter((_, i) => i !== index);
                  if (newGoal!.levels.length === 0) newGoal!.levels = [emptyLevel()];
                }}
              >
                {$t('common.delete')}</button>
            </div>
          {/each}
          <button type="button" class="success" onclick={() => (newGoal!.levels = [...newGoal!.levels, emptyLevel()])}>
            {$t('questContent.addLevel')}
          </button>
          <small class="muted">{$t('questContent.ladderHint')}</small>
        </fieldset>

        <div class="op-field">
          <label for="goal-reason">{$t('common.reason')}</label>
          <input autocomplete="off" spellcheck="false" id="goal-reason" bind:value={newGoal.reason} />
        </div>
        <button type="button" onclick={saveGoal} disabled={$ops.busyKeys.goalCreate} class="success">
          {$t('questContent.create')}
        </button>
        {#if $ops.errors.goalCreate}<p class="empty-state danger" role="alert">{$ops.errors.goalCreate}</p>{/if}
        <OpResult result={$ops.results.goalCreate} />
      </div>
    {/if}

    {#if loading && goals.length === 0}
      <p class="empty-state">{$t('common.loading')}</p>
    {:else if goals.length === 0}
      <p class="empty-state">{$t('questContent.noGoals')}</p>
    {/if}

    <div class="card-list">
      {#each goals as goal (goal.id)}
        <article class="card">
          <div class="card-head">
            <span class="card-main">
              <strong>{goal.code}</strong>
              <small>
                {$t('questContent.goalSummary', {
                  level: goal.reachedLevel,
                  levels: goal.levels.length,
                  score: formatNumber(goal.totalScore),
                  contributors: formatNumber(goal.contributors),
                })}
              </small>
            </span>
            <span class="chip-row">
              {#if goal.isActive}<span class="chip ok">{$t('questContent.chipActive')}</span>{/if}
              {#if !goal.enabled}<span class="chip off">{$t('questContent.chipDisabled')}</span>{/if}
              {#if goal.expired}<span class="chip warn">{$t('questContent.chipExpired')}</span>{/if}
              {#if goal.campaignCode}<span class="chip">{goal.campaignCode}</span>{/if}
              {#if goal.endsAt}<span class="chip">{formatDate(goal.endsAt)}</span>{/if}
            </span>
            {#if canManage}
              <span class="row-actions">
                <button type="button" class="ghost-button" onclick={() => startEditGoal(goal)}>
                  {$t('common.edit')}</button>
                <button
                  type="button"
                  class="ghost-button danger"
                  onclick={() => askDelete('goal', goal.id, goal.code)}
                >
                  {$t('common.delete')}</button>
              </span>
            {/if}
          </div>

          <ul class="ladder">
            {#each goal.levels as level (level.id)}
              <li class:reached={level.reached}>
                <span>{$t('questContent.levelLabel', { level: level.levelNumber })}</span>
                <span class="muted">{formatNumber(level.scoreThreshold)}</span>
                <span class="muted">{$t('questContent.rewardsCount', { count: level.rewardUserLimit })}</span>
              </li>
            {/each}
          </ul>

          {#if editGoal?.id === goal.id}
            <div class="editor">
              <div class="op-field">
                <label for={`edit-goal-code-${goal.id}`}>{$t('questContent.codeRequired')}</label>
                <input autocomplete="off" spellcheck="false" id={`edit-goal-code-${goal.id}`} bind:value={editGoal.code} />
              </div>
              <div class="op-field">
                <label for={`edit-goal-campaign-${goal.id}`}>{$t('questContent.campaignCode')}</label>
                <input autocomplete="off" spellcheck="false" id={`edit-goal-campaign-${goal.id}`} bind:value={editGoal.campaignCode} />
              </div>
              <div class="op-field">
                <label for={`edit-goal-ends-${goal.id}`}>{$t('questContent.endsAt')}</label>
                <input autocomplete="off" spellcheck="false" id={`edit-goal-ends-${goal.id}`} type="datetime-local" bind:value={editGoal.endsAt} />
              </div>
              <div class="op-field">
                <label for={`edit-goal-sort-${goal.id}`}>{$t('questContent.sortOrder')}</label>
                <input autocomplete="off" spellcheck="false" id={`edit-goal-sort-${goal.id}`} type="number" bind:value={editGoal.sortOrder} />
              </div>
              <div class="op-field">
                <label><input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={editGoal.enabled} /> {$t('questContent.enabled')}</label>
              </div>

              <fieldset class="op-subgroup">
                <legend>{$t('questContent.ladderLegend')}</legend>
                {#each editGoal!.levels as level, index}
                  <div class="row-grid">
                    <input autocomplete="off" spellcheck="false" type="number" min="0" placeholder={$t('questContent.threshold')} bind:value={level.scoreThreshold} />
                    <input autocomplete="off" spellcheck="false" type="number" min="0" placeholder={$t('questContent.rewardLimit')} bind:value={level.rewardUserLimit} />
                    <button
                      type="button"
                      class="ghost-button danger"
                      onclick={() => {
                        editGoal!.levels = editGoal!.levels.filter((_, i) => i !== index);
                        if (editGoal!.levels.length === 0) editGoal!.levels = [emptyLevel()];
                      }}
                    >
                      {$t('common.delete')}</button>
                  </div>
                {/each}
                <button type="button" class="success" onclick={() => (editGoal!.levels = [...editGoal!.levels, emptyLevel()])}>
                  {$t('questContent.addLevel')}
                </button>
              </fieldset>

              <div class="op-field">
                <label for={`edit-goal-reason-${goal.id}`}>{$t('common.reason')}</label>
                <input autocomplete="off" spellcheck="false" id={`edit-goal-reason-${goal.id}`} bind:value={editGoal.reason} />
              </div>
              <div class="row-actions">
                <button type="button" onclick={saveGoal} disabled={$ops.busyKeys[`goal:${goal.id}`]}>
                  {$t('questContent.save')}
                </button>
                <button type="button" class="ghost-button" onclick={() => (editGoal = null)}>
                  {$t('questContent.cancel')}
                </button>
              </div>
              {#if $ops.errors[`goal:${goal.id}`]}<p class="empty-state danger" role="alert">{$ops.errors[`goal:${goal.id}`]}</p>{/if}
              <OpResult result={$ops.results[`goal:${goal.id}`]} />
            </div>
          {/if}
        </article>
      {/each}
    </div>
  </section>

  <section class="panel">
    <div class="panel-head">
      <h2>
        <CalendarCheck size={17} strokeWidth={2} aria-hidden="true" />
        {$t('questContent.tasksHeading')}
      </h2>
      {#if canManage}
        <button
          type="button" class="success"
          onclick={() => {
            editTask = null;
            newTask = newTask ? null : emptyTask();
          }}
        >
          {$t('questContent.newTask')}
        </button>
      {/if}
    </div>
    <p class="muted">{$t('questContent.tasksHint')}</p>

    {#if newTask}
      <div class="editor">
        <div class="op-field">
          <label for="task-code">{$t('questContent.taskCodeRequired')}</label>
          <input autocomplete="off" spellcheck="false" id="task-code" bind:value={newTask.taskCode} placeholder="visit_rooms" />
        </div>
        <div class="op-field">
          <label for="task-type">{$t('questContent.objectiveRequired')}</label>
          <select id="task-type" bind:value={newTask.questTypeCode}>
            <option value="">{$t('questContent.objectiveSelect')}</option>
            {#each questTypes as type (type)}
              <option value={type}>{type}</option>
            {/each}
          </select>
          <small class="muted">{$t('questContent.objectiveHint')}</small>
        </div>
        <div class="op-field">
          <label for="task-repeats">{$t('questContent.requiredRepeats')}</label>
          <input autocomplete="off" spellcheck="false" id="task-repeats" type="number" min="1" bind:value={newTask.requiredRepeats} />
        </div>
        <div class="op-field">
          <label for="task-image">{$t('questContent.imageVersion')}</label>
          <input autocomplete="off" spellcheck="false" id="task-image" bind:value={newTask.imageVersion} />
        </div>
        <div class="op-field">
          <label for="task-catalog">{$t('questContent.catalogName')}</label>
          <input autocomplete="off" spellcheck="false" id="task-catalog" bind:value={newTask.catalogName} />
        </div>
        <div class="op-field">
          <label for="task-sort">{$t('questContent.sortOrder')}</label>
          <input autocomplete="off" spellcheck="false" id="task-sort" type="number" bind:value={newTask.sortOrder} />
        </div>
        <div class="op-field">
          <label><input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={newTask.isBonus} /> {$t('questContent.isBonus')}</label>
        </div>
        <div class="op-field">
          <label><input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={newTask.enabled} /> {$t('questContent.enabled')}</label>
        </div>

        <fieldset class="op-subgroup">
          <legend><Gift size={13} strokeWidth={2} aria-hidden="true" /> {$t('questContent.rewardsLegend')}</legend>
          {#each newTask!.rewards as reward, index}
            <div class="row-grid four">
              <input autocomplete="off" spellcheck="false" placeholder={$t('questContent.rewardType')} bind:value={reward.rewardTypeId} />
              <input autocomplete="off" spellcheck="false" type="number" min="0" placeholder={$t('questContent.amount')} bind:value={reward.amount} />
              <input autocomplete="off" spellcheck="false" type="number" min="0" placeholder={$t('questContent.productType')} bind:value={reward.productItemTypeId} />
              <button
                type="button"
                class="ghost-button danger"
                onclick={() => {
                  newTask!.rewards = newTask!.rewards.filter((_, i) => i !== index);
                  if (newTask!.rewards.length === 0) newTask!.rewards = [emptyReward()];
                }}
              >
                {$t('common.delete')}</button>
            </div>
          {/each}
          <button type="button" class="success" onclick={() => (newTask!.rewards = [...newTask!.rewards, emptyReward()])}>
            {$t('questContent.addReward')}
          </button>
          <small class="muted">{$t('questContent.rewardsHint')}</small>
        </fieldset>

        <div class="op-field">
          <label for="task-reason">{$t('common.reason')}</label>
          <input autocomplete="off" spellcheck="false" id="task-reason" bind:value={newTask.reason} />
        </div>
        <button type="button" onclick={saveTask} disabled={$ops.busyKeys.taskCreate} class="success">
          {$t('questContent.create')}
        </button>
        {#if $ops.errors.taskCreate}<p class="empty-state danger" role="alert">{$ops.errors.taskCreate}</p>{/if}
        <OpResult result={$ops.results.taskCreate} />
      </div>
    {/if}

    {#if loading && tasks.length === 0}
      <p class="empty-state">{$t('common.loading')}</p>
    {:else if tasks.length === 0}
      <p class="empty-state">{$t('questContent.noTasks')}</p>
    {/if}

    <div class="card-list">
      {#each tasks as task (task.id)}
        <article class="card">
          <div class="card-head">
            <span class="card-main">
              <strong>{task.taskCode}</strong>
              <small>
                {task.questTypeCode} ×{task.requiredRepeats} —
                {$t('questContent.taskStats', {
                  assigned: formatNumber(task.assigned),
                  completed: formatNumber(task.completed),
                  rate: task.completionRate,
                })}
              </small>
            </span>
            <span class="chip-row">
              {#if task.isBonus}<span class="chip"><Target size={11} strokeWidth={2} aria-hidden="true" /> {$t('questContent.chipBonus')}</span>{/if}
              {#if !task.enabled}<span class="chip off">{$t('questContent.chipDisabled')}</span>{/if}
              {#if !questTypes.includes(task.questTypeCode)}
                <span class="chip warn">{$t('questContent.chipUnknownObjective')}</span>
              {/if}
              {#each task.rewards as reward (reward.id)}
                <span class="chip">{formatNumber(reward.amount)} {reward.rewardTypeId}</span>
              {/each}
            </span>
            {#if canManage}
              <span class="row-actions">
                <button type="button" class="ghost-button" onclick={() => startEditTask(task)}>
                  {$t('common.edit')}</button>
                <button
                  type="button"
                  class="ghost-button danger"
                  onclick={() => askDelete('task', task.id, task.taskCode)}
                >
                  {$t('common.delete')}</button>
              </span>
            {/if}
          </div>

          {#if editTask?.id === task.id}
            <div class="editor">
              <div class="op-field">
                <label for={`edit-task-code-${task.id}`}>{$t('questContent.taskCodeRequired')}</label>
                <input autocomplete="off" spellcheck="false" id={`edit-task-code-${task.id}`} bind:value={editTask.taskCode} />
              </div>
              <div class="op-field">
                <label for={`edit-task-type-${task.id}`}>{$t('questContent.objectiveRequired')}</label>
                <select id={`edit-task-type-${task.id}`} bind:value={editTask.questTypeCode}>
                  {#if !questTypes.includes(editTask.questTypeCode)}
                    <option value={editTask.questTypeCode}>
                      {$t('questContent.objectiveLegacy', { name: editTask.questTypeCode })}
                    </option>
                  {/if}
                  {#each questTypes as type (type)}
                    <option value={type}>{type}</option>
                  {/each}
                </select>
              </div>
              <div class="op-field">
                <label for={`edit-task-repeats-${task.id}`}>{$t('questContent.requiredRepeats')}</label>
                <input autocomplete="off" spellcheck="false" id={`edit-task-repeats-${task.id}`} type="number" min="1" bind:value={editTask.requiredRepeats} />
              </div>
              <div class="op-field">
                <label for={`edit-task-image-${task.id}`}>{$t('questContent.imageVersion')}</label>
                <input autocomplete="off" spellcheck="false" id={`edit-task-image-${task.id}`} bind:value={editTask.imageVersion} />
              </div>
              <div class="op-field">
                <label for={`edit-task-catalog-${task.id}`}>{$t('questContent.catalogName')}</label>
                <input autocomplete="off" spellcheck="false" id={`edit-task-catalog-${task.id}`} bind:value={editTask.catalogName} />
              </div>
              <div class="op-field">
                <label for={`edit-task-sort-${task.id}`}>{$t('questContent.sortOrder')}</label>
                <input autocomplete="off" spellcheck="false" id={`edit-task-sort-${task.id}`} type="number" bind:value={editTask.sortOrder} />
              </div>
              <div class="op-field">
                <label><input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={editTask.isBonus} /> {$t('questContent.isBonus')}</label>
              </div>
              <div class="op-field">
                <label><input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={editTask.enabled} /> {$t('questContent.enabled')}</label>
              </div>

              <fieldset class="op-subgroup">
                <legend>{$t('questContent.rewardsLegend')}</legend>
                {#each editTask!.rewards as reward, index}
                  <div class="row-grid four">
                    <input autocomplete="off" spellcheck="false" bind:value={reward.rewardTypeId} />
                    <input autocomplete="off" spellcheck="false" type="number" min="0" bind:value={reward.amount} />
                    <input autocomplete="off" spellcheck="false" type="number" min="0" bind:value={reward.productItemTypeId} />
                    <button
                      type="button"
                      class="ghost-button danger"
                      onclick={() => {
                        editTask!.rewards = editTask!.rewards.filter((_, i) => i !== index);
                        if (editTask!.rewards.length === 0) editTask!.rewards = [emptyReward()];
                      }}
                    >
                      {$t('common.delete')}</button>
                  </div>
                {/each}
                <button type="button" class="success" onclick={() => (editTask!.rewards = [...editTask!.rewards, emptyReward()])}>
                  {$t('questContent.addReward')}
                </button>
              </fieldset>

              <div class="op-field">
                <label for={`edit-task-reason-${task.id}`}>{$t('common.reason')}</label>
                <input autocomplete="off" spellcheck="false" id={`edit-task-reason-${task.id}`} bind:value={editTask.reason} />
              </div>
              <div class="row-actions">
                <button type="button" onclick={saveTask} disabled={$ops.busyKeys[`task:${task.id}`]}>
                  {$t('questContent.save')}
                </button>
                <button type="button" class="ghost-button" onclick={() => (editTask = null)}>
                  {$t('questContent.cancel')}
                </button>
              </div>
              {#if $ops.errors[`task:${task.id}`]}<p class="empty-state danger" role="alert">{$ops.errors[`task:${task.id}`]}</p>{/if}
              <OpResult result={$ops.results[`task:${task.id}`]} />
            </div>
          {/if}
        </article>
      {/each}
    </div>
  </section>
{/if}

<ConfirmStagedModal {ops} eyebrow={$t('questContent.confirmEyebrow')} />

<ConfirmReasonModal
  open={Boolean($deleteOps.pending)}
  title={$deleteOps.pending?.title ?? ''}
  changes={$deleteOps.pending?.changes ?? []}
  noteOnly={$deleteOps.pending?.noteOnly ?? false}
  summary={$deleteOps.pending?.summary ?? ''}
  confirmLabel={$t('common.confirm')}
  busy={$deleteOps.busy}
  error={$deleteOps.error}
  danger={$deleteOps.pending?.danger ?? false}
  onconfirm={deleteOps.confirm}
  oncancel={() => deleteOps.cancel()}
/>

<style>
  .card-list {
    display: grid;
    gap: 8px;
    margin-top: 10px;
  }

  .card {
    border: 1px solid var(--line);
    border-radius: 12px;
    background: var(--surface-strong);
    padding: 11px 12px;
  }

  .card-head {
    display: flex;
    align-items: center;
    gap: 12px;
    flex-wrap: wrap;
  }

  .card-main {
    display: grid;
    gap: 2px;
    min-width: 140px;
    flex: 1 1 200px;
  }

  .card-main small {
    color: var(--muted);
  }

  .chip-row,
  .row-actions {
    display: flex;
    flex-wrap: wrap;
    gap: 5px;
    align-items: center;
  }

  .chip {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    border: 1px solid var(--line-strong);
    border-radius: 999px;
    padding: 0 9px;
    font-size: 0.76rem;
    font-weight: 700;
    white-space: nowrap;
  }

  .chip.off {
    border-color: var(--line);
    color: var(--muted);
  }

  .chip.ok {
    border-color: var(--ok);
    color: var(--ok);
  }

  .chip.warn {
    border-color: var(--warning-border);
    background: var(--warning-bg);
    color: var(--warning);
  }

  .ladder {
    list-style: none;
    margin: 9px 0 0;
    padding: 0;
    display: grid;
    gap: 3px;
    font-size: 0.84rem;
  }

  .ladder li {
    display: grid;
    /* Four columns now, and the coin one is last: it is absent from most rungs, and a missing cell
       at the end of the row leaves the ones before it where they were. */
    grid-template-columns: 90px 110px 130px 1fr;
    gap: 8px;
    padding: 3px 7px;
    border-radius: 7px;
  }

  .ladder li.reached {
    background: var(--surface-hover);
    font-weight: 600;
  }

  .editor {
    border-top: 1px solid var(--line);
    margin-top: 10px;
    padding-top: 10px;
  }

  .op-subgroup {
    border: 1px solid var(--line);
    border-radius: 10px;
    padding: 10px 12px;
    margin: 4px 0 8px;
  }

  .op-subgroup legend {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    padding: 0 6px;
    font-size: 0.8rem;
    font-weight: 700;
    color: var(--muted);
  }

  .row-grid {
    display: grid;
    grid-template-columns: 1fr 1fr auto;
    gap: 6px;
    margin-bottom: 6px;
  }

  .row-grid.four {
    grid-template-columns: 1.2fr 0.8fr 0.8fr auto;
  }

  .panel-head {
    flex-wrap: wrap;
    row-gap: 8px;
  }

  .panel-head h2 {
    display: flex;
    align-items: center;
    gap: 8px;
  }
</style>
