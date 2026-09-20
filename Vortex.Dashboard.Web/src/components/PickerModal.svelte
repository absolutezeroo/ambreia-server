<script lang="ts">
  import Modal from './Modal.svelte';
  import { apiGet } from '../lib/api';
  import AccessDeniedNotice from './AccessDeniedNotice.svelte';
    import { isPermissionDeniedError } from '../lib/permissions';
  import { LOGIC_GROUPS } from '../lib/furnitureEnums';
  import { directoryFor, type PickerRow } from '../lib/pickers/directories';
  import { PICKER_ROWS } from './pickers/index';
  import { t } from '../lib/i18n';

    type Props = {
    /** Which directory to browse; every key of DIRECTORIES is valid. */
    kind?: string;
    title?: string;
    onSelect: (row: PickerRow) => void;
    onClose: () => void;
    /** False draws the permission notice instead of a list the caller may not read. */
    canSelect?: boolean;
  };

  let { kind = 'user', title = 'Select', onSelect, onClose, canSelect = true }: Props = $props();

  // Where the rows come from, how they sort, and which layout draws one -- all declared per
  // directory in lib/pickers/directories.js. This component owns the search, the paging and the
  // permission handling, and nothing about any particular catalogue.
  const directory = directoryFor(kind);
  const endpoint = directory?.endpoint;
  const Row = PICKER_ROWS[directory?.row ?? 'plain'];
  const sorts = directory?.sorts ?? [];

  /**
   * What every directory endpoint answers with. hasMore is absent on the ones that do not page, and
   * types only on the garment directory, which sends the part types with the first page rather than
   * making the picker ask twice for a list of twenty-three strings.
   */
  type PickerPage = { items?: PickerRow[]; hasMore?: boolean; types?: string[] };

  const SORT_LABELS: Record<string, string> = {
    relevance: 'pickerModal.sortRelevance',
    name: 'pickerModal.sortName',
    id: 'pickerModal.sortId',
    idDesc: 'pickerModal.sortIdDesc',
    sprite: 'pickerModal.sortSprite',
    logic: 'pickerModal.sortLogic',
  };

  let query = $state('');
  let sort = $state('relevance');
  let logicFilter = $state('');
  let onlineOnly = $state(false);
  let typeFilter = $state('');
  // Kept across loads: narrowing to one part type must not empty the list of types to narrow by.
  let types = $state<string[]>([]);
  let rows = $state<PickerRow[]>([]);
  let hasMore = $state(false);
  let loadingMore = $state(false);
  const PAGE_SIZE = 60;
  let loading = $state(false);
  let error = $state('');
  let forbidden = $state(false);

  $effect(() => {
    if (!canSelect) {
      forbidden = true;
      error = '';
      rows = [];
    } else {
      forbidden = false;
    }
  });

  const ACCESS_DENIED_KEYS: Record<string, string> = {
    furniture: 'pickerModal.furnitureAccessDenied',
    room: 'pickerModal.roomsAccessDenied',
    user: 'pickerModal.playersAccessDenied',
  };

  const EYEBROW_KEYS: Record<string, string> = {
    furniture: 'pickerModal.catalogFurniture',
    room: 'pickerModal.rooms',
    user: 'pickerModal.players',
  };

  const SEARCH_PLACEHOLDER_KEYS: Record<string, string> = {
    furniture: 'pickerModal.searchFurniturePlaceholder',
    room: 'pickerModal.searchRoomPlaceholder',
    user: 'pickerModal.searchPlayerPlaceholder',
  };

  let permissionMessage = $derived($t(ACCESS_DENIED_KEYS[kind] ?? ACCESS_DENIED_KEYS.user));

  // 'relevance' is the server's own default, so it is left out rather than sent as a value the
  // server's switch would have to carry a case for.
  function params(offset: number) {
    const parts = [`q=${encodeURIComponent(query.trim())}`, `limit=${PAGE_SIZE}`, `offset=${offset}`];

    if (sort !== 'relevance') parts.push(`sort=${encodeURIComponent(sort)}`);
    if (directory?.filter === 'logic' && logicFilter) parts.push(`logic=${encodeURIComponent(logicFilter)}`);
    if (directory?.filter === 'online' && onlineOnly) parts.push('online=true');
    if (directory?.filter === 'type' && typeFilter) parts.push(`type=${encodeURIComponent(typeFilter)}`);

    return parts.join('&');
  }

  async function load() {
    if (!canSelect) {
      forbidden = true;
      error = '';
      rows = [];
      return;
    }

    loading = true;
    error = '';
    forbidden = false;
    rows = [];
    hasMore = false;

    // Says what is wrong instead of listing the wrong thing. The previous fallback made an
    // unwired kind look like a working player picker.
    if (!endpoint) {
      loading = false;
      error = `PickerModal: no directory is wired for kind "${kind}".`;

      return;
    }

    try {
      const data = await apiGet<PickerPage>(`${endpoint}?${params(0)}`);
      rows = data.items || [];
      // The players endpoint does not page; absent hasMore simply means "that is everything".
      hasMore = Boolean(data.hasMore);

      if (data.types?.length) types = data.types;
    } catch (err) {
      if (isPermissionDeniedError(err)) {
        forbidden = true;
        rows = [];
        return;
      }

      error = (err as Error).message;
      rows = [];
    } finally {
      loading = false;
    }
  }

  // Appends rather than replacing: the list is browsed by scrolling, so a page jump would lose the
  // rows the operator already scrolled past.
  async function loadMore() {
    if (loadingMore || !hasMore) return;

    loadingMore = true;

    try {
      const data = await apiGet<PickerPage>(`${endpoint}?${params(rows.length)}`);
      rows = [...rows, ...(data.items || [])];
      hasMore = Boolean(data.hasMore);
    } catch (err) {
      error = (err as Error).message;
      hasMore = false;
    } finally {
      loadingMore = false;
    }
  }

  function choose(item: PickerRow) {
    onSelect?.(item);
    onClose?.();
  }

  void load();
</script>

<Modal
  title={title}
  eyebrow={$t(EYEBROW_KEYS[kind] ?? EYEBROW_KEYS.user)}
  width={620}
  labelledBy="picker-modal-title"
  onclose={onClose}
>
  {#snippet header()}
    <button class="ghost-button" type="button" onclick={onClose}>
      {$t('pickerModal.close')}
    </button>
  {/snippet}

  <form class="toolbar" onsubmit={(event) => { event.preventDefault(); load(); }}>
    <input
      autocomplete="off"
      spellcheck="false"
      type="search"
      name="picker-query"
      id="picker-query"
      bind:value={query}
      placeholder={$t(SEARCH_PLACEHOLDER_KEYS[kind] ?? SEARCH_PLACEHOLDER_KEYS.user)}
      disabled={!canSelect}
    />
    <button type="submit" disabled={!canSelect}>{$t('pickerModal.search')}</button>
  </form>

  <!-- Changing a filter re-runs the search straight away: it is a narrowing of the same question,
       not a new one, so making the operator press Search again would only cost a click. -->
  <div class="pick-filters">
    <!-- A directory that orders itself gets no sort control: a dropdown the server ignores is a
         dead one. -->
    {#if sorts.length}
      <label>
        <span>{$t('pickerModal.sortLabel')}</span>
        <select bind:value={sort} onchange={load} disabled={!canSelect}>
          {#each sorts as option (option)}
            <option value={option}>{$t(SORT_LABELS[option])}</option>
          {/each}
        </select>
      </label>
    {/if}

    {#if directory?.filter === 'logic'}
      <label>
        <span>{$t('pickerModal.logicLabel')}</span>
        <select bind:value={logicFilter} onchange={load} disabled={!canSelect}>
          <option value="">{$t('pickerModal.logicAll')}</option>
          {#each LOGIC_GROUPS as group (group.label)}
            <optgroup label={group.label}>
              {#each group.options as option (option.value)}
                <option value={option.value}>{option.label}</option>
              {/each}
            </optgroup>
          {/each}
        </select>
      </label>
    {:else if directory?.filter === 'online'}
      <label class="pick-check">
        <input type="checkbox" bind:checked={onlineOnly} onchange={load} disabled={!canSelect} />
        <span>{$t('pickerModal.onlineOnly')}</span>
      </label>
    {:else if directory?.filter === 'type'}
      <label>
        <span>{$t('pickerModal.partTypeLabel')}</span>
        <select bind:value={typeFilter} onchange={load} disabled={!canSelect || !types.length}>
          <option value="">{$t('pickerModal.partTypeAll')}</option>
          {#each types as partType (partType)}
            <option value={partType}>
              {$t(`pickerModal.partType_${partType}`) === `pickerModal.partType_${partType}`
                ? partType
                : $t(`pickerModal.partType_${partType}`)}
            </option>
          {/each}
        </select>
      </label>
    {/if}

    {#if rows.length}
      <small class="pick-count">{$t('pickerModal.resultCount', { count: rows.length })}</small>
    {/if}
  </div>

  {#if forbidden}
    <AccessDeniedNotice message={permissionMessage} />
  {:else if error}
    <p class="empty-state danger" role="alert">{error}</p>
  {:else if loading}
    <p class="empty-state">{$t('pickerModal.loading')}</p>
  {/if}

  <div class="pick-list">
    {#each rows as row}
      <Row {row} onchoose={choose} />
    {:else}
      {#if !loading}<p class="empty-state">{$t('pickerModal.noResults')}</p>{/if}
    {/each}
    {#if hasMore}
      <button type="button" class="ghost-button" onclick={loadMore} disabled={loadingMore}>
        {loadingMore ? $t('common.loading') : $t('pickerModal.loadMore')}
      </button>
    {/if}
  </div>
</Modal>

<style>
  .pick-filters {
    display: flex;
    flex-wrap: wrap;
    align-items: end;
    gap: 10px;
  }

  .pick-filters label {
    display: grid;
    gap: 4px;
    min-width: 0;
  }

  .pick-filters label span {
    color: var(--muted);
    font-size: 0.72rem;
    text-transform: uppercase;
  }

  /* No cap: the filter row grows its labels, so one filter takes the dialog's width and two
     share it. A fixed 260px left a select stranded at less than half the panel. */
  .pick-filters select {
    width: 100%;
  }

  .pick-check {
    display: flex !important;
    align-items: center;
    gap: 7px;
    padding-bottom: 9px;
  }

  .pick-check span {
    text-transform: none !important;
    font-size: 0.86rem !important;
  }

  .pick-count {
    margin-left: auto;
    color: var(--muted);
    padding-bottom: 9px;
  }

  .pick-list {
    display: grid;
    gap: 6px;
    max-height: 52vh;
    overflow: auto;
  }







</style>
