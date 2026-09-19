<script lang="ts">
  import Modal from '../components/Modal.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import LoadingOverlay from '../components/LoadingOverlay.svelte';
  import OpResult from '../components/OpResult.svelte';
  import { onMount } from 'svelte';
  import { apiGet, apiPost, describeApiError } from '../lib/api';
  import { createWriteOps } from '../lib/writeOps';
  import { isPermissionDeniedError, hasDashboardCapability } from '../lib/permissions';
  import { formatDuration } from '../lib/format';
  import { CAPABILITIES } from '../lib/dashboardPermissions';
  import { reasonOk, positive } from '../lib/validation';
  import AccessDeniedNotice from '../components/AccessDeniedNotice.svelte';
  import EntityLink from '../components/EntityLink.svelte';
  import { identity, openPlayer, openItem } from '../lib/session';
  import FilterBar from '../components/FilterBar.svelte';
  import { readFilterValues } from '../lib/filters';
  import type { FilterField } from '../lib/filters';
  import SortTh from '../components/SortTh.svelte';
  import Tabs from '../components/Tabs.svelte';
  import Pagination from '../components/Pagination.svelte';
  import { filterRows, sortRows, pageOf, pageCountOf, PAGE_SIZE } from '../lib/tableView';
  import { t, translate } from '../lib/i18n';
  import type { CfhAppealSnapshot, CfhIssueQueueEntrySnapshot } from '../lib/apiTypes';
  import type { Sort } from '../lib/tableView';

  /** The inline ban form, opened on one row of the queue. */
  type BanDraft = {
    playerId: number;
    playerName: string;
    permanent: boolean;
    durationSeconds: number | string;
    reason: string;
  };

  /**
   * Vortex.Primitives.Moderation.CfhTicketCloseReason. The wire carries the number.
   *
   * This was a list of {value, label} pairs that nothing read, while the buttons below passed 1 and
   * 3 as literals -- so the hotel's vocabulary was written here twice and used neither time. Named
   * here until the generated contracts carry the enum itself.
   *
   * `sanctioned` has no button: closing a ticket that way is not offered anywhere on this page.
   */
  const CLOSE_REASON = { useless: 1, sanctioned: 2, resolved: 3 } as const;

  let loading = $state(false);
  let forbidden = $state(false);
  let error = $state('');
  let queue = $state<CfhIssueQueueEntrySnapshot[]>([]);

  // An appeal is a report the reporter contested after it was closed without a sanction. It is not
  // a queue ticket -- it stays Closed and never comes back through pick/release -- so it gets its
  // own list rather than a state on the one above.
  let appeals = $state<CfhAppealSnapshot[]>([]);
  let tab = $state('queue');

  // The queue is one request, so the filtering is over what the page already holds. The text box was
  // all there was, which on a busy hotel means scrolling to find the one ticket somebody asked about.
  const FILTERS: FilterField[] = [
    { id: 'q', label: translate('cfh.filterText'), kind: 'text' },
    { id: 'state', label: translate('cfh.colState'), kind: 'select', options: [] },
    { id: 'reported', label: translate('cfh.colReported'), kind: 'entity', picker: 'user' },
    { id: 'reporter', label: translate('cfh.colReporter'), kind: 'entity', picker: 'user' },
    { id: 'unpicked', label: translate('cfh.filterUnpicked'), kind: 'bool' },
  ];

  let filters = $state(readFilterValues(FILTERS));

  // The states that exist are the states the queue answered with, so a hotel whose vocabulary grows
  // does not need this list edited.
  let stateOptions = $derived(
    [...new Set(queue.map((entry) => entry.state))].sort().map((state) => ({
      value: String(state),
      label: String(state),
    })),
  );

  let fields = $derived(
    FILTERS.map((field) => (field.id === 'state' ? { ...field, options: stateOptions } : field)),
  );

  let queueSort = $state<Sort>({ key: '', dir: 'desc' });
  let queueView = $derived(
    sortRows(
      filterRows(queue, filters.q).filter((entry) => {
        if (filters.state && String(entry.state) !== filters.state) return false;
        if (filters.reported && entry.reportedUserId !== Number(filters.reported)) return false;
        if (filters.reporter && entry.reporterUserId !== Number(filters.reporter)) return false;
        if (filters.unpicked && entry.pickerUserId) return false;

        return true;
      }),
      queueSort,
    ),
  );

  let page = $state(1);
  let pageCount = $derived(pageCountOf(queueView));
  let rows = $derived(pageOf(queueView, page));

  // Back to the first page whenever the list underneath changes shape, so a filter does not leave
  // the operator on a page that no longer exists.
  $effect(() => {
    void filters;
    page = 1;
  });

  // Row-scoped action state, keyed by issueId.
  let rowBusy = $state<Record<number, boolean>>({});
  let rowError = $state<Record<number, string>>({});

  // Inline "ban reported player" panel — opened per row, closed after confirm/cancel.
  // The ban IS an audited sanction, unlike the queue moves above: it goes through the shared write
  // store so its reason reaches the audit log and the reason-history datalist, and so a 403 mid-
  // session reads the same here as everywhere else. The draft holds the form; the store holds the
  // write.
  let banDraft = $state<BanDraft | null>(null);
  const banOps = createWriteOps();

  let canManage = $derived(hasDashboardCapability($identity, CAPABILITIES.opsCfhManage));
  let canBan = $derived(hasDashboardCapability($identity, CAPABILITIES.opsBanAccount));

  async function refresh() {
    loading = true;
    error = '';
    forbidden = false;

    try {
      // Both lists in one pass: the appeal tab's badge has to be right before the operator opens
      // it, or nobody finds out an appeal is waiting.
      [queue, appeals] = await Promise.all([
        apiGet<CfhIssueQueueEntrySnapshot[]>('/api/v1/operations/cfh/queue'),
        apiGet<CfhAppealSnapshot[]>('/api/v1/operations/cfh/appeals'),
      ]);
    } catch (err) {
      if (isPermissionDeniedError(err)) {
        forbidden = true;
        queue = [];
        appeals = [];
        return;
      }

      error = (err as Error).message;
      queue = [];
      appeals = [];
    } finally {
      loading = false;
    }
  }

  // pick / release / close are queue moves, not audited writes: the moderator's identity is already
  // on the ticket and there is nothing to justify, which is why they do not go through the shared
  // reason modal like the sanctions below do. They did each carry their own copy of this
  // busy/refresh/report-the-403 dance -- it lives here once instead.
  async function rowAction(
    issueId: number,
    endpoint: string,
    body: Record<string, unknown> = {},
  ) {
    rowBusy = { ...rowBusy, [issueId]: true };
    rowError = { ...rowError, [issueId]: '' };

    try {
      await apiPost(endpoint, { issueIds: [issueId], ...body });
      await refresh();
    } catch (err) {
      rowError = {
        ...rowError,
        [issueId]: isPermissionDeniedError(err)
          ? translate('common.insufficientRights')
          : describeApiError(err),
      };
    } finally {
      rowBusy = { ...rowBusy, [issueId]: false };
    }
  }

  const pick = (issueId: number) => rowAction(issueId, '/api/v1/operations/cfh/pick');
  const release = (issueId: number) => rowAction(issueId, '/api/v1/operations/cfh/release');
  const close = (issueId: number, reason: number, sanctioned: boolean) =>
    rowAction(issueId, '/api/v1/operations/cfh/close', { reason, sanctioned });

  // Upholding marks the report sanctioned after all; dismissing leaves the original decision. Both
  // settle the appeal, which is what takes the reporter's row out of "appealed".
  const resolveAppeal = (issueId: number, upheld: boolean) =>
    rowAction(issueId, '/api/v1/operations/cfh/appeals/resolve', { upheld });

  const formatWhen = (value: string | null) =>
    value ? new Date(value).toLocaleString() : '-';

  function openBanDraft(entry: CfhIssueQueueEntrySnapshot) {
    banDraft = {
      playerId: entry.reportedUserId,
      playerName: entry.reportedUserName,
      permanent: false,
      durationSeconds: '',
      reason: '',
    };
    banOps.clear();
  }

  function cancelBanDraft() {
    banDraft = null;
    banOps.clear();
  }

  async function confirmBanDraft() {
    if (!banDraft) {
      return;
    }

    const validDuration = banDraft.permanent || positive(banDraft.durationSeconds);
    const valid = positive(banDraft.playerId) && validDuration && reasonOk(banDraft.reason);

    const staged = banOps.ask(
      '/api/v1/operations/players/ban',
      {
        playerId: Number(banDraft.playerId),
        permanent: banDraft.permanent,
        durationSeconds: banDraft.permanent ? null : Number(banDraft.durationSeconds),
      },
      translate('cfh.confirmBan'),
      banDraft.playerName || '',
      { valid, invalidMessage: translate('cfh.banValidation'), reason: banDraft.reason.trim() },
    );

    // The draft form is its own confirmation step, so the write commits straight away rather than
    // opening a second dialog on top of it.
    if (staged) {
      await banOps.confirm();
    }
  }

  onMount(() => {
    void refresh();
  });
</script>

<section class="panel">
  <PageHeader title={$t('cfh.title')} description={$t('cfh.description')}>
    {#snippet actions()}
      <button type="button" class="warning" onclick={refresh} disabled={loading}>{$t('common.refresh')}</button>
    {/snippet}
  </PageHeader>

  {#if forbidden}
    <AccessDeniedNotice message={$t('cfh.accessDenied')} />
  {:else if error}
    <p class="empty-state danger" role="alert">{error}</p>
  {/if}

  <Tabs
    tabs={[
      { id: 'queue', label: $t('cfh.tabQueue'), count: queue.length },
      { id: 'appeals', label: $t('cfh.tabAppeals'), count: appeals.length },
    ]}
    bind:active={tab}
    storageKey="cfh"
  />
</section>

{#if tab === 'queue'}
<section class="panel">
  <FilterBar {fields} bind:values={filters} />
</section>

<section class="panel" style="margin-top: 12px;">
  <p class="muted">{$t('cfh.shown', { shown: queueView.length, total: queue.length })}</p>

  <table>
    <thead>
      <tr>
        <SortTh label="#" key="issueId" bind:sort={queueSort} initialDir="asc" />
        <SortTh label={$t('cfh.colState')} key="state" bind:sort={queueSort} initialDir="asc" />
        <SortTh label={$t('cfh.colAge')} key="issueAgeMs" bind:sort={queueSort} />
        <SortTh label={$t('cfh.colReporter')} key="reporterUserName" bind:sort={queueSort} initialDir="asc" />
        <SortTh label={$t('cfh.colReported')} key="reportedUserName" bind:sort={queueSort} initialDir="asc" />
        <SortTh label={$t('cfh.colPickedBy')} key="pickerUserName" bind:sort={queueSort} initialDir="asc" />
        <th>{$t('cfh.colMessage')}</th>
        <th>{$t('cfh.colActions')}</th>
      </tr>
    </thead>
    <tbody>
      {#each rows as entry (entry.issueId)}
        <tr>
          <td>#{entry.issueId}</td>
          <td>{entry.state}</td>
          <td>{formatDuration(entry.issueAgeMs / 1000)}</td>
          <td><EntityLink id={entry.reporterUserId} label={entry.reporterUserName} {openPlayer} {openItem} /></td>
          <td><EntityLink id={entry.reportedUserId} label={entry.reportedUserName} {openPlayer} {openItem} /></td>
          <td>{entry.pickerUserName || '-'}</td>
          <td class="truncate">{entry.message || '-'}</td>
          <td>
            {#if canManage}
              <div class="op-actions">
                <button type="button" class="ghost-button" onclick={() => pick(entry.issueId)} disabled={rowBusy[entry.issueId]}>{$t('cfh.pick')}</button>
                <button type="button" class="ghost-button" onclick={() => close(entry.issueId, CLOSE_REASON.resolved, false)} disabled={rowBusy[entry.issueId]}>{$t('cfh.resolve')}</button>
                <button type="button" class="ghost-button" onclick={() => close(entry.issueId, CLOSE_REASON.useless, false)} disabled={rowBusy[entry.issueId]}>{$t('cfh.useless')}</button>
                <button type="button" class="ghost-button" onclick={() => release(entry.issueId)} disabled={rowBusy[entry.issueId]}>{$t('cfh.release')}</button>
                {#if canBan}
                  <button type="button" onclick={() => openBanDraft(entry)}>{$t('cfh.banReportedPlayer')}</button>
                {/if}
              </div>
              {#if rowError[entry.issueId]}<p class="empty-state danger" role="alert">{rowError[entry.issueId]}</p>{/if}
            {:else}
              <span class="muted">{$t('cfh.readOnly')}</span>
            {/if}
          </td>
        </tr>
      {:else}
        <tr><td colspan="8" class="muted">{$t('cfh.noTickets')}</td></tr>
      {/each}
    </tbody>
  </table>

  {#if pageCount > 1}
    <Pagination
      {page}
      {pageCount}
      total={queueView.length}
      pageSize={PAGE_SIZE}
      label={$t('cfh.paginationLabel')}
      pageWord={$t('common.page')}
      prevLabel={$t('common.prev')}
      nextLabel={$t('common.next')}
      onchange={(next) => (page = next)}
    />
  {/if}
</section>
{/if}

{#if tab === 'appeals'}
<section class="panel" style="margin-top: 12px;">
  <p class="muted">{$t('cfh.appealsDescription')}</p>

  <table>
    <thead>
      <tr>
        <th>#</th>
        <th>{$t('cfh.colReporter')}</th>
        <th>{$t('cfh.colReported')}</th>
        <th>{$t('cfh.colClosedAt')}</th>
        <th>{$t('cfh.colAppealedAt')}</th>
        <th>{$t('cfh.colMessage')}</th>
        <th>{$t('cfh.colActions')}</th>
      </tr>
    </thead>
    <tbody>
      {#each appeals as appeal (appeal.issueId)}
        <tr>
          <td>#{appeal.issueId}</td>
          <td><EntityLink id={appeal.reporterPlayerId} label={appeal.reporterName} {openPlayer} {openItem} /></td>
          <td><EntityLink id={appeal.reportedPlayerId} label={appeal.reportedName} {openPlayer} {openItem} /></td>
          <td>{formatWhen(appeal.closedAt)}</td>
          <td>{formatWhen(appeal.appealCreatedAt)}</td>
          <td class="truncate">{appeal.message || '-'}</td>
          <td>
            {#if canManage}
              <div class="op-actions">
                <button type="button" onclick={() => resolveAppeal(appeal.issueId, true)} disabled={rowBusy[appeal.issueId]}>{$t('cfh.upholdAppeal')}</button>
                <button type="button" class="ghost-button" onclick={() => resolveAppeal(appeal.issueId, false)} disabled={rowBusy[appeal.issueId]}>{$t('cfh.dismissAppeal')}</button>
              </div>
              {#if rowError[appeal.issueId]}<p class="empty-state danger" role="alert">{rowError[appeal.issueId]}</p>{/if}
            {:else}
              <span class="muted">{$t('cfh.readOnly')}</span>
            {/if}
          </td>
        </tr>
      {:else}
        <tr><td colspan="7" class="muted">{$t('cfh.noAppeals')}</td></tr>
      {/each}
    </tbody>
  </table>
</section>
{/if}

{#if banDraft}
  <Modal
    title={$t('cfh.banReportedPlayer')}
    eyebrow={$t('cfh.sanctionEyebrow')}
    width={460}
    labelledBy="cfh-ban-title"
    onclose={cancelBanDraft}
  >
    <p class="muted">{banDraft.playerName || $t('cfh.player')} (#{banDraft.playerId})</p>
    <div class="op-checkbox-field">
      <input autocomplete="off" spellcheck="false" id="cfh-ban-permanent" type="checkbox" bind:checked={banDraft.permanent} />
      <label for="cfh-ban-permanent">{$t('common.permanent')}</label>
    </div>
    {#if !banDraft.permanent}
      <div class="op-field">
        <label for="cfh-ban-duration">{$t('cfh.durationSeconds')}</label>
        <input autocomplete="off" spellcheck="false" id="cfh-ban-duration" type="number" min="1" bind:value={banDraft.durationSeconds} placeholder="86400" />
      </div>
    {/if}
    <div class="op-field">
      <label for="cfh-ban-reason">{$t('common.reasonRequired')}</label>
      <input autocomplete="off" spellcheck="false" id="cfh-ban-reason" bind:value={banDraft.reason} placeholder={$t('common.reasonPlaceholder')} list="reason-history" />
    </div>
    {#if $banOps.error}<p class="empty-state danger" role="alert">{$banOps.error}</p>{/if}
    {#if $banOps.result}
      <OpResult result={$banOps.result} />
    {/if}

    {#snippet actions()}

      <button type="button" onclick={confirmBanDraft} disabled={$banOps.busy}>{$t('cfh.confirmBan')}</button>
      <button class="ghost-button" type="button" onclick={cancelBanDraft}>{$t('cfh.close')}</button>

    {/snippet}
  </Modal>
{/if}

<LoadingOverlay show={loading} label={$t('cfh.loadingQueue')} />
