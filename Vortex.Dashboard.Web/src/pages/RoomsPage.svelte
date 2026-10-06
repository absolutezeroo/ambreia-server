<script lang="ts">

  import { apiGet } from '../lib/api';
  import { formatDate, summarizeData } from '../lib/format';
  import EntityLink from '../components/EntityLink.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import PickerModal from '../components/PickerModal.svelte';
  import AccessDeniedNotice from '../components/AccessDeniedNotice.svelte';
  import { isPermissionDeniedError } from '../lib/permissions';
  import { openPlayer, openItem } from '../lib/session';
  import { readParam, readNumberParam, writeParams } from '../lib/urlState';
  import Pagination from '../components/Pagination.svelte';
  import TableFilter from '../components/TableFilter.svelte';
  import { filterRows } from '../lib/tableView';
  import { onMount } from 'svelte';
  import { t } from '../lib/i18n';
  import type { RoomTimeline } from '../lib/apiTypes';
  import type { PickerRow } from '../lib/pickers/directories';

  // ?room= makes a room timeline a link, and is how the command palette hands one over.
  let roomId = $state(readParam('room'));
  // The room is chosen, not typed. Eleven other pages already reach for PickerModal to do
  // exactly this; asking for a raw id here was the odd one out -- nobody knows a room by
  // its number, and a typo returns an empty timeline rather than an error.
  let roomName = $state('');
  let picking = $state(false);
  let data = $state<RoomTimeline | null>(null);
  let error = $state('');
  let forbidden = $state(false);

  // The request already returns the whole window, so narrowing it is a reading operation rather
  // than another call: event type, free text and a date range, all applied to what is here.
  const PAGE_SIZE = 25;
  let page = $state(readNumberParam('page', 1));
  let kindFilter = $state(readParam('kind'));
  let textFilter = $state('');
  let fromFilter = $state('');
  let toFilter = $state('');

  let allRows = $derived(data?.timeline || []);

  // The list of event types comes from the rows themselves: a hard-coded list goes stale the day
  // the server emits a new one, and silently offers filters that match nothing.
  let kinds = $derived([...new Set(allRows.map((row) => row.eventType))].sort());

  let visibleRows = $derived(
    filterRows(
      allRows.filter((row) => {
        if (kindFilter && row.eventType !== kindFilter) return false;

        const at = Date.parse(row.createdAt);

        if (fromFilter && at < Date.parse(fromFilter)) return false;
        // An end date with no time means the whole of that day.
        if (toFilter && at > Date.parse(toFilter) + 86_399_000) return false;

        return true;
      }),
      textFilter,
    ),
  );

  let pageCount = $derived(Math.max(1, Math.ceil(visibleRows.length / PAGE_SIZE)));
  let pageRows = $derived(visibleRows.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE));

  // Replay: a moment and a window around it, sent to the server rather than filtered here. The
  // date filters below narrow what was already fetched, which is the wrong tool for "what happened
  // in this room at 21:14" -- the incident is usually older than the last 120 events.
  let replayAt = $state('');
  let replayMinutes = $state('15');

  function replayWindow() {
    const at = Date.parse(replayAt);

    if (!replayAt || Number.isNaN(at)) {
      return '';
    }

    const half = Math.max(1, Number(replayMinutes) || 15) * 60_000;
    const iso = (ms: number) => new Date(ms).toISOString();

    return `&since=${encodeURIComponent(iso(at - half))}&until=${encodeURIComponent(iso(at + half))}`;
  }

  // Narrowing the list moves the ground under the pager, so it goes back to the first page.
  function narrowed() {
    page = 1;
    writeParams({ page: '', kind: kindFilter || '' });
  }

  async function load() {
    if (!roomId.trim()) {
      return;
    }

    forbidden = false;
    error = '';
    writeParams({ room: roomId.trim() });

    try {
      data = await apiGet<RoomTimeline>(
        `/api/v1/directory/rooms/${encodeURIComponent(roomId.trim())}?limit=${replayAt ? 500 : 120}${replayWindow()}`,
      );

      // Keep the page only while it still exists in the new room's timeline.
      if (page > Math.ceil(data.timeline.length / PAGE_SIZE)) {
        page = 1;
        writeParams({ page: '' });
      }
    } catch (err) {
      if (isPermissionDeniedError(err)) {
        forbidden = true;
        error = '';
        data = null;
        return;
      }

      error = (err as Error).message;
      data = null;
    }
  }

  onMount(() => {
    if (roomId.trim()) load();
  });
</script>

<section class="panel">
  <PageHeader title={$t('roomsTimeline.title')} description={$t('roomsTimeline.description')}>
    {#snippet actions()}
      <button type="button" onclick={load} class="warning">{$t('common.refresh')}</button>
      <!-- `verbatim` keeps the ghost button exactly as it looks everywhere else and only stops the
           dark theme uppercasing the label, because this label is DATA — a room's name. It shouted
           "QSGQSG" at an operator who had named their room qsgqsg. -->
      <button type="button" class="ghost-button verbatim" onclick={() => (picking = true)}>
        {roomName || (roomId ? `#${roomId}` : $t('roomsTimeline.inspect'))}
      </button>
    {/snippet}
  </PageHeader>
</section>

<!-- Nothing chosen yet means nothing to say about a room: the panel only exists once it has a
     summary or a problem to report. -->
{#if forbidden || error || data?.room}
  <section class="panel">

    {#if forbidden}
      <AccessDeniedNotice message={$t('roomsTimeline.accessDenied')} />
    {:else if error}
      <p class="empty-state danger" role="alert">{error}</p>
    {/if}

    {#if data?.room}
      <div class="room-summary">
        <strong>{data.room.name} #{data.room.roomId}</strong>
        <span>{data.room.usersNow}/{data.room.playersMax} {$t('roomsTimeline.players')}</span>
        <span>{data.room.modelName}</span>
        <EntityLink id={data.room.roomOwnerId} label={data.room.roomOwnerName} {openPlayer} {openItem} />
      </div>
    {/if}
  </section>
{/if}

<!-- Every filter for this timeline, in one block above the table. They used to sit in two panels
     with a page of content between them — the replay window in the header, the kind/date narrowing
     down here — so an operator had to know which half of the screen held the control they wanted.
     The refresh lives here too: it is the button that applies what this block holds, and the second
     copy in the header was refreshing the same thing from a place that showed none of it. -->
{#if roomId.trim()}
  <section class="panel">
    <div class="timeline-filters">
      <!-- Replay. Left empty this is the plain "last 120 events" view; give it a moment and the
           request asks the server for the window around it instead, which is the only way to reach
           an incident older than the tail of the timeline. -->
      <label>
        {$t('roomsTimeline.replayAt')}
        <input autocomplete="off" spellcheck="false" type="datetime-local" bind:value={replayAt} />
      </label>
      <label>
        {$t('roomsTimeline.replayWindow')}
        <select bind:value={replayMinutes}>
          <option value="5">± 5 min</option>
          <option value="15">± 15 min</option>
          <option value="60">± 60 min</option>
        </select>
      </label>

      {#if allRows.length}
        <label>
          {$t('investigation.filterKind')}
          <select bind:value={kindFilter} onchange={narrowed}>
            <option value="">{$t('investigation.filterKindAll')}</option>
            {#each kinds as kind}
              <option value={kind}>{kind}</option>
            {/each}
          </select>
        </label>
        <label>
          {$t('common.since')}
          <input autocomplete="off" spellcheck="false" type="date" bind:value={fromFilter} onchange={narrowed} />
        </label>
        <label>
          {$t('common.until')}
          <input autocomplete="off" spellcheck="false" type="date" bind:value={toFilter} onchange={narrowed} />
        </label>
      {/if}

      <!-- Only once a moment has been entered, and then it says Replay. Refreshing is the header's
           job and it is always on screen; a second button doing the same thing under a different
           name is just two places to wonder about. -->
      {#if replayAt}
        <button type="button" onclick={load}>{$t('roomsTimeline.replayRun')}</button>
        <button type="button" class="ghost-button" onclick={() => { replayAt = ''; load(); }}>
          {$t('roomsTimeline.replayClear')}
        </button>
      {/if}
    </div>

    {#if allRows.length}
      <TableFilter bind:query={textFilter} shown={visibleRows.length} total={allRows.length} />
    {/if}
  </section>
{/if}

<section class="panel">

  <table>
    <thead><tr><th>{$t('roomsTimeline.colTime')}</th><th>{$t('roomsTimeline.colEvent')}</th><th>{$t('roomsTimeline.colActor')}</th><th>{$t('roomsTimeline.colTarget')}</th><th>{$t('roomsTimeline.colMessage')}</th></tr></thead>
    <tbody>
      {#each pageRows as row}
        <tr>
          <td>{formatDate(row.createdAt)}</td>
          <td><span class={`event-${row.eventType}`}>{row.eventType}</span></td>
          <td><EntityLink id={row.playerId} label={row.playerName || ''} {openPlayer} {openItem} /></td>
          <td><EntityLink id={row.targetPlayerId} label={row.targetPlayerName || ''} {openPlayer} {openItem} /></td>
          <td>
            {#if row.itemId}
              <EntityLink type="item" id={row.itemId} label={`item #${row.itemId}`} {openPlayer} {openItem} />
            {/if}
            {summarizeData(row.message)}
          </td>
        </tr>
      {:else}
        <tr><td colspan="5" class="muted">{$t('roomsTimeline.noTimeline')}</td></tr>
      {/each}
    </tbody>
  </table>

  {#if pageCount > 1}
    <Pagination
      page={page}
      pageCount={pageCount}
      total={visibleRows.length}
      pageSize={PAGE_SIZE}
      pageWord={$t('common.page')}
      prevLabel={$t('common.prev')}
      nextLabel={$t('common.next')}
      onchange={(next) => {
        page = next;
        writeParams({ page: next > 1 ? next : '' });
      }}
    />
  {/if}
</section>

{#if picking}
  <PickerModal
    kind="room"
    title={$t('roomsTimeline.title')}
    onSelect={(item: PickerRow) => {
      roomId = String(item.id);
      roomName = item.name;
      picking = false;
      load();
    }}
    onClose={() => (picking = false)}
  />
{/if}
