<script lang="ts">
  import { onMount } from 'svelte';
  import { CalendarCheck, Gift, Target } from '@lucide/svelte';
  import AccessDeniedNotice from '../AccessDeniedNotice.svelte';
  import ConfirmReasonModal from '../ConfirmReasonModal.svelte';
  import ConfirmStagedModal from '../ConfirmStagedModal.svelte';
  import OpResult from '../OpResult.svelte';
  import { apiGet } from '../../lib/api';
  import { formatNumber } from '../../lib/format';
  import { CAPABILITIES } from '../../lib/dashboardPermissions';
  import { hasDashboardCapability, isPermissionDeniedError } from '../../lib/permissions';
  import { identity } from '../../lib/session';
  import { t, translate } from '../../lib/i18n';
  import { createWriteOps } from '../../lib/writeOps';
  import type { DailyTaskList, DailyTaskRow } from '../../lib/apiTypes';

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

  // Daily tasks live under the quest capability: same domain, same operators.

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

  let tasks = $state<DailyTaskRow[]>([]);
  let questTypes = $state<string[]>([]);
  let loading = $state(false);
  let error = $state('');
  let forbidden = $state(false);

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
      const taskData = await apiGet<DailyTaskList>('/api/v1/daily-tasks');

      tasks = taskData.items || [];
      questTypes = taskData.questTypes || [];
    } catch (err) {
      if (isPermissionDeniedError(err)) {
        forbidden = true;
        tasks = [];
        return;
      }

      error = (err as Error).message;
    } finally {
      loading = false;
    }
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
  // refused delete (task_has_assignments) keeps the modal open with the
  // code showing, which is the whole point of routing it through the store.
  function askDelete(id: number, label: string) {
    deleteOps.ask(
      '/api/v1/operations/daily-tasks/delete',
      { taskId: id },
      translate('questContent.deleteTask'),
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
                  onclick={() => askDelete(task.id, task.taskCode)}
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
