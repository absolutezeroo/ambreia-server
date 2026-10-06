<script lang="ts">

  import { onMount } from 'svelte';
  import { apiGet } from '../lib/api';
  import { compactCorrelation, formatDate, summarizeData } from '../lib/format';
  import EntityLink from '../components/EntityLink.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import AccessDeniedNotice from '../components/AccessDeniedNotice.svelte';
  import AssetImage from '../components/AssetImage.svelte';
  import { User } from '@lucide/svelte';
  import { isPermissionDeniedError } from '../lib/permissions';
  import { openPlayer, openItem } from '../lib/session';
  import PickerModal from '../components/PickerModal.svelte';
  import Pagination from '../components/Pagination.svelte';
  import TableFilter from '../components/TableFilter.svelte';
  import CurrencyIcon from '../components/CurrencyIcon.svelte';
  import { currencyChipClass, currencyKindFromName, currencyLabel } from '../lib/currency';
  import { formatNumber } from '../lib/format';
  import { filterRows } from '../lib/tableView';
  import PlayerOperationsPanel from '../components/PlayerOperationsPanel.svelte';
  import Tabs from '../components/Tabs.svelte';
  import { Wrench, ScrollText } from '@lucide/svelte';
  import { readParam, readNumberParam, writeParams } from '../lib/urlState';
  import { t, translate } from '../lib/i18n';
  import type {
    DirectorySearch,
    IdSearch,
    PlayerProfile,
  } from '../lib/apiTypes';
  import type { PickerRow } from '../lib/pickers/directories';

  /** Which journal a timeline line came from. The filter above the table offers exactly these. */
  type TimelineKind = 'audit' | 'ledger' | 'item' | 'chat';

  /**
   * One line of the merged timeline.
   *
   * Four different server rows flattened into one bag, plus what push() adds. Written as one shape
   * with optional fields rather than a union of the four, because the table reads across them --
   * `row.action || row.eventType || row.category` is one column, not three -- and a union would
   * have to be narrowed in every cell to say the same thing.
   */
  type TimelineRow = {
    kind: TimelineKind;
    /** Whichever of occurredAt / createdAt the source row carried. */
    time?: string | null;
    sortTime: number;
    /** Whose line this is; only rendered while two accounts are being compared. */
    account?: string;
    category?: string | null;
    action?: string | null;
    eventType?: string | null;
    correlationId?: string | null;
    itemId?: number | null;
    /** The searched term for the rows that carry no player id of their own. */
    playerId?: number | string | null;
    playerName?: string | null;
    actorPlayerId?: number | null;
    actorPlayerName?: string | null;
    fromOwnerId?: number | null;
    fromOwnerName?: string | null;
    targetPlayerId?: number | null;
    targetPlayerName?: string | null;
    roomId?: number | null;
    roomName?: string | null;
    currency?: string | null;
    activityPointType?: number | null;
    delta?: number | null;
    balanceAfter?: number | null;
    data?: string | null;
    [field: string]: unknown;
  };

  /** A server row on its way in, before push() stamps the time it is sorted by. */
  type SourceRow = Partial<TimelineRow> & {
    kind: TimelineKind;
    occurredAt?: string;
    createdAt?: string;
  };

  // ?player= makes this page linkable, and is how the command palette hands a player over.
  let query = $state(readParam('player'));
  let rows = $state<TimelineRow[]>([]);
  let player = $state<PlayerProfile | null>(null);
  let summary = $state('');
  $effect(() => {
    if (!summary) summary = translate('investigation.hint');
  });
  let error = $state('');
  let forbidden = $state(false);
  // The search box takes an id or a correlation id; a name is what an operator actually remembers,
  // and the shared picker already knows how to find one. Picking fills the box and runs the search,
  // so both paths land on the same page state.
  let picking = $state<'user' | 'furniture' | null>(null);

  // The second account of a multi-account case. Empty is the ordinary single-player investigation;
  // filled, the timeline carries both and says which line belongs to whom.
  let compareQuery = $state(readParam('compare'));
  let comparePlayer = $state<PlayerProfile | null>(null);

  // The timeline is paged client-side: the search already returns the whole window, so paging
  // is a reading aid, not another round trip.
  const PAGE_SIZE = 25;
  let page = $state(readNumberParam('page', 1));

  // The search returns the whole window in one go, so narrowing it is a reading operation, not
  // another request: kind, free text and a date range, all applied to what is already here.
  let kindFilter = $state(readParam('kind'));
  let categoryFilter = $state(readParam('category'));
  let textFilter = $state('');
  let fromFilter = $state('');
  let toFilter = $state('');

  // Built from what actually came back rather than a hardcoded list: the server grows categories
  // (Progression was the last one) and a list written here would quietly stop offering the newest.
  let categories = $derived(
    [
      ...new Set(
        rows
          .map((row) => row.category)
          .filter((category): category is string => Boolean(category)),
      ),
    ].sort(),
  );

  let visibleRows = $derived(
    filterRows(
      rows.filter((row) => {
        if (kindFilter && row.kind !== kindFilter) return false;
        if (categoryFilter && row.category !== categoryFilter) return false;
        if (fromFilter && row.sortTime < Date.parse(fromFilter)) return false;
        // An end date with no time means the whole of that day.
        if (toFilter && row.sortTime > Date.parse(toFilter) + 86_399_000) return false;

        return true;
      }),
      textFilter,
    ),
  );

  let reasonByCorrelation = $derived(
    new Map(
      rows.flatMap((row): [string, string][] => {
        const reason = row.action || row.eventType;

        return row.kind !== 'ledger' && row.correlationId && reason
          ? [[row.correlationId, reason]]
          : [];
      }),
    ),
  );

  let pageCount = $derived(Math.max(1, Math.ceil(visibleRows.length / PAGE_SIZE)));
  let pageRows = $derived(visibleRows.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE));

  // Narrowing the list moves the ground under the pager, so it goes back to the first page.
  function narrowed() {
    page = 1;
    writeParams({ page: '', kind: kindFilter || '', category: categoryFilter || '' });
  }

  // Exports what is on screen, not everything loaded: the operator narrowed the list on purpose,
  // and an export that quietly ignores their filters is a different question's answer.
  function exportCsv() {
    const columns = ['time', 'account', 'kind', 'category', 'action', 'actor', 'target', 'room', 'item', 'correlationId', 'detail'];
    const cell = (value: unknown) => {
      const text = value === null || value === undefined ? '' : String(value);

      // Excel and every other reader treat a bare quote as a delimiter, so a chat line containing
      // one would shift every column after it.
      return /[",\n\r]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
    };

    const lines = [
      columns.join(','),
      ...visibleRows.map((row) =>
        [
          row.time ?? '',
          row.account ?? '',
          row.kind ?? '',
          row.category ?? '',
          row.action ?? row.eventType ?? '',
          row.actorPlayerName ?? row.actorPlayerId ?? row.playerId ?? '',
          row.targetPlayerName ?? row.targetPlayerId ?? '',
          row.roomName ?? row.roomId ?? '',
          row.itemId ?? '',
          row.correlationId ?? '',
          summarizeData(row.data),
        ]
          .map(cell)
          .join(','),
      ),
    ];

    // A BOM, because the export is opened in Excel more often than anywhere else and Excel reads a
    // UTF-8 file without one as the local codepage -- which mangles every accented player name.
    // Built from its code point rather than typed: the character itself is invisible in an editor,
    // and eslint rejects it on sight as irregular whitespace.
    const bom = String.fromCharCode(0xfeff);
    const blob = new Blob([bom + lines.join('\r\n')], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');

    link.href = url;
    link.download = `investigation-${player?.id ?? (query.trim() || 'export')}.csv`;
    link.click();
    URL.revokeObjectURL(url);
  }
  let active = $state('actions');

  // $derived, not const: translate() reads the locale once, and a const would keep the tab strip in
  // whatever language the page happened to mount in after the operator switches.
  let tabs = $derived([
    { id: 'actions', label: $t('playerOps.tabActions'), icon: Wrench },
    { id: 'timeline', label: $t('investigation.tabTimeline'), icon: ScrollText },
  ]);

  function pick(chosen: PickerRow) {
    query = String(chosen.id);
    picking = null;
    search();
  }

  onMount(() => {
    if (query.trim()) search();
  });

  function push(rows: TimelineRow[], row: SourceRow) {
    const at = row.occurredAt || row.createdAt;

    rows.push({ ...row, time: at, sortTime: Date.parse(at || '') || 0 });
  }

  // One account's rows, tagged with whose they are. Split out of search() so the comparison run can
  // feed the same list a second time instead of duplicating every mapping.
  function collectPlayerRows(
    data: IdSearch,
    into: TimelineRow[],
    term: string,
    account: string,
  ) {
    data.asActor.forEach((row) => push(into, { kind: 'audit', account, ...row }));
    data.ledger.forEach((row) => push(into, { kind: 'ledger', playerId: term, account, ...row }));
    data.itemHistory.forEach((row) => push(into, { kind: 'item', account, ...row }));
    data.chats.forEach((row) =>
      push(into, {
        kind: 'chat',
        playerId: term,
        action: 'chat.said',
        data: row.message,
        account,
        ...row,
      }),
    );
  }

  async function search() {
    const term = query.trim();
    if (!term) {
      return;
    }

    forbidden = false;
    error = '';
    player = null;
    comparePlayer = null;
    try {
      // limit applies per source, and the noisy ones (room entries, sessions) would otherwise fill
      // the default 50 on their own and push everything rarer -- a badge, an achievement --
      // off a timeline that pages client-side and so never asks for a second window.
      const data = await apiGet<DirectorySearch>(
        `/api/v1/directory/search?q=${encodeURIComponent(term)}&limit=200`,
      );
      const nextRows: TimelineRow[] = [];

      if (data.kind === 'id') {
        player = data.playerProfile;
        collectPlayerRows(data, nextRows, term, player?.name || term);

        // The second account, interleaved into the same list rather than shown beside it. Two
        // columns of timestamps is exactly the comparison a person cannot do by eye; one ordered
        // list with a name against each line is the whole point of asking.
        const otherTerm = compareQuery.trim();

        if (otherTerm && otherTerm !== term) {
          const other = await apiGet<DirectorySearch>(
            `/api/v1/directory/search?q=${encodeURIComponent(otherTerm)}&limit=200`,
          );

          if (other.kind === 'id') {
            comparePlayer = other.playerProfile;
            collectPlayerRows(other, nextRows, otherTerm, comparePlayer?.name || otherTerm);
          }
        }

        summary = comparePlayer
          ? translate('investigation.eventsForPair', {
              count: nextRows.length,
              term,
              other: otherTerm,
            })
          : player
            ? translate('investigation.eventsForPlayer', { count: nextRows.length, term })
            : translate('investigation.eventsForId', { count: nextRows.length, term });
      } else if (data.kind === 'correlationId') {
        data.audit.forEach((row) => push(nextRows, { kind: 'audit', ...row }));
        data.ledger.forEach((row) => push(nextRows, { kind: 'ledger', ...row }));
        data.items.forEach((row) => push(nextRows, { kind: 'item', ...row }));
        summary = translate('investigation.linkedEventsFor', { count: nextRows.length, term });
      } else {
        summary = data.hint || translate('investigation.noStructuredResult');
      }

      rows = nextRows.sort((left, right) => right.sortTime - left.sortTime);
      // Only a player search is worth putting in the URL: a correlation id is a one-off lookup, not
      // a place someone comes back to.
      // Keep the page only while it still exists in the new result set.
      if (page > Math.ceil(nextRows.length / PAGE_SIZE)) page = 1;

      writeParams({
        player: player ? String(player.id) : '',
        compare: compareQuery.trim(),
        page: page > 1 ? page : '',
      });
    } catch (err) {
      if (isPermissionDeniedError(err)) {
        forbidden = true;
        rows = [];
        player = null;
        error = '';
        return;
      }

      error = (err as Error).message;
      rows = [];
      player = null;
    }
  }
</script>

<section class="panel">
  <PageHeader title={$t('investigation.title')} description={$t('investigation.description')}>
    {#snippet actions()}
      <button type="button" onclick={search} class="warning">{$t('common.refresh')}</button>
    {/snippet}
  </PageHeader>
</section>

<section class="panel">
  <form class="toolbar" onsubmit={(event) => { event.preventDefault(); search(); }}>
    <input autocomplete="off" spellcheck="false" bind:value={query} placeholder={$t('investigation.searchPlaceholder')} />
    <!-- Optional second account. A multi-account case is the one question the single-player
         timeline cannot answer, and it is answered by ordering both together, not by opening the
         page twice. -->
    <input
      autocomplete="off"
      spellcheck="false"
      bind:value={compareQuery}
      placeholder={$t('investigation.comparePlaceholder')}
    />
    <button type="submit">{$t('investigation.load')}</button>
    <button type="button" class="ghost-button" onclick={() => (picking = 'user')}>
      {$t('investigation.findPlayer')}
    </button>
    <button type="button" class="ghost-button" onclick={() => (picking = 'furniture')}>
      {$t('investigation.findItem')}
    </button>
  </form>

</section>

<section class="panel">
  <p class="muted">{summary}</p>

  {#if forbidden}
    <AccessDeniedNotice message={$t('investigation.accessDenied')} />
  {:else if error}
    <p class="empty-state danger" role="alert">{error}</p>
  {/if}

  {#if player}
    <div class="player-headline">
      <AssetImage src={player.avatarUrl} alt={player.name} size={56} fallbackIcon={User} />
      <div class="player-headline-text">
        <strong>{player.name} #{player.id}</strong>
        {#if player.motto}<small class="muted">{player.motto}</small>{/if}
        <!-- Live presence, not `players.status`: that column is written at account creation and
             never again, so it reads "Offline" for a player who is standing in a room. -->
        <small class="muted">
          {player.online ? $t('investigation.online') : $t('investigation.offline')} - {player.gender}
        </small>
      </div>
    </div>

    <Tabs {tabs} bind:active storageKey="player" />
  {/if}

  {#if player && active === 'actions'}
    <PlayerOperationsPanel
      playerId={player.id}
      playerName={player.name}
      online={player.online}
      onDone={search}
    />
  {/if}

</section>

<!-- With no player loaded there is no tab strip, so the timeline is simply what the page shows. -->
{#if !player || active === 'timeline'}
  <section class="panel" style="margin-top: 12px;">
    <div class="timeline-filters">
      <label>
        {$t('investigation.filterKind')}
        <select bind:value={kindFilter} onchange={narrowed}>
          <option value="">{$t('investigation.filterKindAll')}</option>
          <option value="audit">{$t('investigation.kindAudit')}</option>
          <option value="ledger">{$t('investigation.kindLedger')}</option>
          <option value="item">{$t('investigation.kindItem')}</option>
          <option value="chat">{$t('investigation.kindChat')}</option>
        </select>
      </label>
      <!-- Only the audit rows carry a category, so the control stays out of the way until the
           result set actually has some to choose between. -->
      {#if categories.length > 1}
        <label>
          {$t('investigation.filterCategory')}
          <select bind:value={categoryFilter} onchange={narrowed}>
            <option value="">{$t('investigation.filterCategoryAll')}</option>
            {#each categories as category}
              <option value={category}>{category}</option>
            {/each}
          </select>
        </label>
      {/if}
      <label>
        {$t('common.since')}
        <input autocomplete="off" spellcheck="false" type="date" bind:value={fromFilter} onchange={narrowed} />
      </label>
      <label>
        {$t('common.until')}
        <input autocomplete="off" spellcheck="false" type="date" bind:value={toFilter} onchange={narrowed} />
      </label>
      <button type="button" class="ghost-button" onclick={exportCsv} disabled={visibleRows.length === 0}>
        {$t('investigation.exportCsv')}
      </button>
    </div>

  </section>

  <section class="panel" style="margin-top: 12px;">
    <TableFilter bind:query={textFilter} shown={visibleRows.length} total={rows.length} />

    <table>
    <thead><tr><th>{$t('investigation.colTime')}</th>{#if comparePlayer}<th>{$t('investigation.colAccount')}</th>{/if}<th>{$t('investigation.colType')}</th><th>{$t('investigation.colActor')}</th><th>{$t('investigation.colDetails')}</th></tr></thead>
    <tbody>
      {#each pageRows as row}
        <tr>
          <td>{formatDate(row.time)}</td>
          <!-- Only while comparing: a column that reads the same on every line of a single-player
               investigation is a column that costs width and says nothing. -->
          {#if comparePlayer}<td>{row.account ?? ''}</td>{/if}
          <td>{row.kind}</td>
          <td>
            {#if row.kind === 'item' && row.itemId}
              <EntityLink type="item" id={row.itemId} label={`item #${row.itemId}`} {openPlayer} {openItem} />
            {:else}
              <EntityLink id={row.actorPlayerId || row.playerId || row.fromOwnerId || query} label={row.actorPlayerName || row.playerName || row.fromOwnerName || ''} {openPlayer} {openItem} />
            {/if}
          </td>
          <td>
            {#if row.kind === 'ledger'}
              <div class="ledger-move">
                <span class={currencyChipClass(currencyKindFromName(row.currency, row.activityPointType))}>
                  <CurrencyIcon kind={currencyKindFromName(row.currency, row.activityPointType)} size={13} />
                  {(row.delta ?? 0) > 0 ? '+' : ''}{formatNumber(row.delta)}
                </span>
                <b>{currencyLabel(row.currency, row.activityPointType)}</b>
                {#if row.balanceAfter != null}
                  <span class="muted">{$t('investigation.balanceAfter', { balance: formatNumber(row.balanceAfter) })}</span>
                {/if}
              </div>
              <div class="ledger-why">
                {reasonByCorrelation.get(row.correlationId ?? '') ?? $t('investigation.noReason')}
              </div>
            {:else}
              {row.action || row.eventType || row.category || $t('investigation.event')}
            {/if}
            {#if row.roomId}
              - <EntityLink type="room" id={row.roomId} label={row.roomName || ''} {openPlayer} {openItem} />
            {/if}
            {#if row.itemId}
              - <EntityLink type="item" id={row.itemId} label={`item #${row.itemId}`} {openPlayer} {openItem} />
            {/if}
            <span class="muted">{compactCorrelation(row.correlationId)} {summarizeData(row.data)}</span>
          </td>
        </tr>
      {:else}
        <tr><td colspan="4" class="muted">{$t('investigation.noRows')}</td></tr>
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
{/if}

{#if picking}
  <PickerModal
    kind={picking}
    title={picking === 'furniture' ? $t('investigation.findItem') : $t('investigation.findPlayer')}
    onSelect={pick}
    onClose={() => (picking = null)}
  />
{/if}

<style>
  .player-headline {
    display: flex;
    align-items: center;
    gap: 12px;
    margin: 4px 0 12px;
  }

  .player-headline-text {
    display: grid;
    gap: 2px;
    min-width: 0;
  }
</style>
