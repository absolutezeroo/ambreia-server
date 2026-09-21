<script lang="ts">
  /**
   * The four files the game client downloads at boot, edited here.
   *
   * Two things about them are worth knowing before reading the rest:
   *
   *   - These are not settings. A malformed save is not a rejected form, it is a client that will
   *     not start. The server keeps a dated copy, writes beside the file, parses the bytes back and
   *     only then moves them into place — so the worst case here is a refused write, never a broken
   *     hotel. The `modifiedUtc` sent back with every save is what turns two operators editing at
   *     once into a refusal instead of a silently dropped edit.
   *   - Only the texts exist per language. The client's registry (`localization.<n>` in
   *     external_variables) gives each language its own texts URL, and a player switches with the
   *     chat command `:lang <code>`. furnidata and productdata have no such mechanism in the client,
   *     so their names stay single-language — a limit of the client, not of this page.
   */
  import { apiGet } from '../lib/api';
  import { createResource } from '../lib/resource';
  import { createWriteOps } from '../lib/writeOps';
  import { hasDashboardCapability } from '../lib/permissions';
  import { CAPABILITIES } from '../lib/dashboardPermissions';
  import { identity } from '../lib/session';
  import { t } from '../lib/i18n';
  import type {
    GamedataEntry,
    GamedataEntryPage,
    GamedataFileList,
    GamedataLanguageList,
  } from '../lib/apiTypes';

  /** The furnidata columns this page is allowed to edit. */
  type FurniField = 'name' | 'description' | 'category' | 'xdim' | 'ydim';

  /** What the drawer is editing. One drawer, four shapes -- see the comment on `draft`. */
  type Draft =
    | { kind: 'entry'; creating: boolean; key: string; value: string; title: string }
    | { kind: 'furni'; row: GamedataEntry; field: FurniField; value: string; title: string }
    | { kind: 'language'; code: string; name: string; title: string };

  import AccessDeniedNotice from '../components/AccessDeniedNotice.svelte';
  import AssetImage from '../components/AssetImage.svelte';
  import ConfirmReasonModal from '../components/ConfirmReasonModal.svelte';
  import Drawer from '../components/Drawer.svelte';
  import EmptyState from '../components/EmptyState.svelte';
  import OpResult from '../components/OpResult.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import Pagination from '../components/Pagination.svelte';
  import Tabs from '../components/Tabs.svelte';

  import { Award, FileJson, Image, Languages, Package, Plus, ShoppingBag, Sofa, Type } from '@lucide/svelte';

  let canManage = $derived(hasDashboardCapability($identity, CAPABILITIES.opsGamedataManage));

  const TABS = [
    { id: 'variables', file: 'variables', icon: FileJson },
    { id: 'texts', file: 'texts', icon: Type },
    { id: 'catalogTexts', file: 'catalog-texts', icon: ShoppingBag },
    { id: 'badgesTexts', file: 'badges-texts', icon: Award },
    { id: 'furnidata', file: 'furnidata', icon: Sofa },
    { id: 'productdata', file: 'productdata', icon: Package },
    { id: 'languages', file: null, icon: Languages },
  ];

  // The files whose entries are a flat key/value map, so the drawer can add one. furnidata and
  // productdata are closed lists addressed by position -- see the button below.
  const KEY_VALUE_FILES = ['variables', 'texts', 'catalog-texts', 'badges-texts'];

  let active = $state('variables');
  let page = $state(1);

  // The filters. `search` is what the box holds; `applied` is what the read uses — typing does not
  // re-query a 55 836-entry file on every keystroke, submitting does.
  let search = $state('');
  let applied = $state('');
  let kind = $state('');
  let category = $state('');
  // Which language's texts are being edited. Empty = the default file at the root of gamedata/,
  // which is the one the client loads before any `:lang` and falls back to for a missing key.
  let lang = $state('');

  let activeFile = $derived(TABS.find((tab) => tab.id === active)?.file ?? null);

  const files = createResource(
    () => ['gamedata-files'],
    () => apiGet<GamedataFileList>('/api/v1/gamedata')
  );

  const languages = createResource(
    () => ['gamedata-languages'],
    () => apiGet<GamedataLanguageList>('/api/v1/gamedata/languages')
  );

  const entries = createResource(
    () => ['gamedata-entries', activeFile, lang, applied, kind, category, page],
    () => {
      const params = new URLSearchParams({ file: activeFile ?? '', page: String(page) });
      if (applied) params.set('search', applied);
      if (lang) params.set('lang', lang);
      if (kind) params.set('kind', kind);
      if (category) params.set('category', category);

      return apiGet<GamedataEntryPage>(`/api/v1/gamedata/entries?${params}`);
    },
    { enabled: () => Boolean(activeFile) }
  );

  let fileMeta = $derived((files.data?.files ?? []).find((f) => f.file === activeFile));
  // Only this file's rows. createResource keeps the previous page on screen while the next one
  // travels, and each file is rendered by a different table -- productdata reading a page of texts
  // keyed its rows on an index those rows do not have, and every key came out null.
  //
  // An answer that does not name a file at all predates that field: show it. A stale row is a much
  // smaller lie than an empty table under a header that says the file holds 730 entries, and
  // blanking the page is not an acceptable way to fail.
  let rows = $derived(
    entries.data && (!entries.data.file || entries.data.file === activeFile)
      ? entries.data.entries
      : [],
  );
  let total = $derived(entries.data?.total ?? 0);
  let declared = $derived(languages.data?.languages ?? []);
  let furniCategories = $derived(
    (files.data?.files ?? []).find((f) => f.file === 'furnidata')?.categories ?? []
  );

  const ops = createWriteOps(() => {
    entries.refresh();
    files.refresh();
    languages.refresh();
  });

  function applyFilters() {
    applied = search;
    page = 1;
  }

  function onTab(id: string) {
    active = id;
    page = 1;
    search = '';
    applied = '';
    kind = '';
    category = '';
    // Every localised file keeps its own per-language copy; leaving a language selected while
    // moving to a file that has none would filter against a directory that does not exist.
    const file = TABS.find((tab) => tab.id === id)?.file ?? null;
    if (!(files.data?.files ?? []).some((f) => f.file === file && f.localised)) lang = '';
  }

  // `draft` is the only editing state. Four shapes, one drawer: a page where some forms open a panel
  // and others appear inline is a page where an operator has to learn which is which.
  let draft = $state<Draft | null>(null);

  function openEntry(row: GamedataEntry) {
    draft = {
      kind: 'entry',
      creating: false,
      key: row.key ?? '',
      value: row.value ?? '',
      title: row.key ?? '',
    };
  }

  function openNewEntry() {
    draft = { kind: 'entry', creating: true, key: '', value: '', title: $t('gamedata.newEntry') };
  }

  function openFurni(row: GamedataEntry) {
    draft = {
      kind: 'furni',
      row,
      field: 'name',
      value: row.name ?? '',
      title: `${row.classname} · #${row.id}`,
    };
  }

  function openNewLanguage() {
    draft = { kind: 'language', code: '', name: '', title: $t('gamedata.enableLanguage') };
  }

  let draftValid = $derived(
    draft === null ||
      (draft.kind === 'entry' && draft.key.trim().length > 0) ||
      (draft.kind === 'furni' && draft.value.length > 0) ||
      (draft.kind === 'language' && draft.code.trim().length > 0)
  );

  function save() {
    if (draft === null) {
      return;
    }

    if (draft.kind === 'entry') {
      ops.ask(
        '/api/v1/operations/gamedata/entry',
        {
          file: activeFile,
          language: lang || null,
          key: draft.key.trim(),
          value: draft.value,
          expectedModifiedUtc: entries.data?.modifiedUtc ?? null,
        },
        draft.creating ? $t('gamedata.newEntry') : $t('gamedata.saveEntry'),
        $t('gamedata.saveEntrySummary', { key: draft.key.trim(), file: activeFile }),
        {
          onSuccess: () => {
            draft = null;
          },
        }
      );
    } else if (draft.kind === 'furni') {
      ops.ask(
        '/api/v1/operations/gamedata/furni',
        {
          kind: draft.row.kind,
          index: draft.row.index,
          field: draft.field,
          value: draft.value,
          expectedModifiedUtc: entries.data?.modifiedUtc ?? null,
        },
        $t('gamedata.saveFurni'),
        $t('gamedata.saveFurniSummary', { classname: draft.row.classname, field: draft.field }),
        {
          onSuccess: () => {
            draft = null;
          },
        }
      );
    } else {
      ops.ask(
        '/api/v1/operations/gamedata/language',
        { code: draft.code.trim(), name: draft.name.trim() },
        $t('gamedata.enableLanguage'),
        $t('gamedata.enableLanguageSummary', { code: draft.code.trim() }),
        {
          onSuccess: () => {
            draft = null;
          },
        }
      );
    }
  }

  function deleteEntry(row: GamedataEntry) {
    ops.ask(
      '/api/v1/operations/gamedata/entry/delete',
      {
        file: activeFile,
        language: lang || null,
        key: row.key,
        expectedModifiedUtc: entries.data?.modifiedUtc ?? null,
      },
      $t('gamedata.deleteEntry'),
      $t('gamedata.deleteEntrySummary', { key: row.key, file: activeFile })
    );
  }

  function disableLanguage(code: string) {
    ops.ask(
      '/api/v1/operations/gamedata/language/delete',
      { code },
      $t('gamedata.disableLanguage'),
      $t('gamedata.disableLanguageSummary', { code })
    );
  }

  let tabs = $derived(
    TABS.map((tab) => ({
      id: tab.id,
      label: $t(`gamedata.tab_${tab.id}`),
      icon: tab.icon,
      count:
        tab.file === null
          ? declared.length
          : (files.data?.files ?? []).find((f) => f.file === tab.file)?.entries,
    }))
  );
</script>

<PageHeader title={$t('gamedata.title')} description={$t('gamedata.subtitle')}>
  {#snippet icon()}
    <FileJson size={18} strokeWidth={2} aria-hidden="true" />
  {/snippet}
</PageHeader>

{#if !canManage}
  <AccessDeniedNotice />
{:else if files.data && !files.data.available}
  <EmptyState message={$t('gamedata.noAssetRoot')} />
{:else}
  <!-- storageKey: an operator translating texts comes back to this page a dozen times an hour, and
       landing on Variables every time is a click they should not have to spend. It also makes the
       tab shareable through ?tab=. -->
  <Tabs {tabs} bind:active storageKey="gamedata" onchange={onTab} />

  <!-- The filters live above the panel, never inside the table's own box: that is where every other
       page in this dashboard puts them, and an operator should not have to look for them twice. -->
  {#if active !== 'languages'}
    <form class="toolbar" onsubmit={(event) => { event.preventDefault(); applyFilters(); }}>
      <input
        autocomplete="off"
        spellcheck="false"
        bind:value={search}
        placeholder={$t('gamedata.searchPlaceholder')}
      />

      {#if fileMeta?.localised}
        <select value={lang} onchange={(event) => ((lang = event.currentTarget.value), (page = 1))}>
          <option value="">{$t('gamedata.defaultLanguage')}</option>
          {#each declared as entry (entry.code)}
            <option value={entry.code}>{entry.name} ({entry.code})</option>
          {/each}
        </select>
      {/if}

      {#if active === 'furnidata'}
        <select value={kind} onchange={(event) => ((kind = event.currentTarget.value), (page = 1))}>
          <option value="">{$t('gamedata.allKinds')}</option>
          <option value="roomitemtypes">{$t('gamedata.floorItems')}</option>
          <option value="wallitemtypes">{$t('gamedata.wallItems')}</option>
        </select>
        <select value={category} onchange={(event) => ((category = event.currentTarget.value), (page = 1))}>
          <option value="">{$t('gamedata.allCategories')}</option>
          {#each furniCategories as entry (entry)}
            <option value={entry}>{entry}</option>
          {/each}
        </select>
      {/if}

      <button type="submit" disabled={entries.loading}>{$t('gamedata.search')}</button>
    </form>
  {/if}

  {#if active === 'languages'}
    <section class="panel">
      <div class="panel-head">
        <h2><Languages size={17} strokeWidth={2} aria-hidden="true" /> {$t('gamedata.tab_languages')}</h2>
        <button type="button" class="success" onclick={openNewLanguage}>
          <Plus size={15} strokeWidth={2} aria-hidden="true" />
          {$t('gamedata.enableLanguage')}
        </button>
      </div>
      <p class="muted">{$t('gamedata.languagesHelp')}</p>

      {#if !declared.length}
        <EmptyState message={$t('gamedata.noLanguages')} />
      {:else}
        <table>
          <thead>
            <tr>
              <th>{$t('gamedata.code')}</th>
              <th>{$t('gamedata.name')}</th>
              <th>{$t('gamedata.command')}</th>
              <th>{$t('gamedata.textsFile')}</th>
              <th>{$t('common.actions')}</th>
            </tr>
          </thead>
          <tbody>
            {#each declared as entry (entry.code)}
              <tr>
                <td><span class="chip">{entry.code}</span></td>
                <td>{entry.name}</td>
                <td><code>{entry.command}</code></td>
                <td>
                  {#if entry.hasFile}
                    {$t('gamedata.filePresent')}
                  {:else}
                    <span class="muted">{$t('gamedata.fileMissing')}</span>
                  {/if}
                </td>
                <td>
                  <button type="button" class="ghost-button danger" onclick={() => disableLanguage(entry.code)}>
                    {$t('gamedata.disableLanguage')}
                  </button>
                </td>
              </tr>
            {/each}
          </tbody>
        </table>
      {/if}
    </section>
  {:else}
    <section class="panel">
      <div class="panel-head">
        <h2>{$t(`gamedata.tab_${active}`)}</h2>
        <div class="panel-head-right">
          {#if fileMeta}
            <span class="muted">{fileMeta.name} · {$t('gamedata.entryCount', { count: fileMeta.entries })}</span>
          {/if}
          <!-- furnidata has no "add": an entry is addressed by its position, and appending one would
               be the only row nobody could then find by id. productdata is a closed list too. -->
          {#if activeFile && KEY_VALUE_FILES.includes(activeFile)}
            <button type="button" class="success" onclick={openNewEntry}>
              <Plus size={15} strokeWidth={2} aria-hidden="true" />
              {$t('gamedata.newEntry')}
            </button>
          {/if}
        </div>
      </div>

      {#if fileMeta && !fileMeta.parses}
        <!-- Said plainly rather than shown as an empty table: a file that does not parse is already
             broken for the client, and "no results" would read as "nothing to edit". -->
        <EmptyState message={$t('gamedata.unparseable', { name: fileMeta.name })} />
      {:else if entries.loading && !rows.length}
        <!-- The rows of the file being left are not shown while the next one travels, so this gap is
             a real one and has to say "loading" rather than "nothing matches". -->
        <p class="muted">{$t('common.loading')}</p>
      {:else if !rows.length}
        <!-- Two different empty states: a file with nothing in it, and a filter that excluded
             everything. Saying "nothing matches that search" with an empty search box was the page
             blaming the operator for its own state. -->
        <EmptyState
          message={applied || kind || category
            ? $t('gamedata.noEntries')
            : $t('gamedata.fileEmpty')}
        />
      {:else if active === 'furnidata'}
        <table>
          <thead>
            <tr>
              <th>{$t('gamedata.preview')}</th>
              <th>{$t('gamedata.classname')}</th>
              <th>{$t('gamedata.name')}</th>
              <th>{$t('gamedata.category')}</th>
              <th>{$t('gamedata.size')}</th>
              <th>{$t('common.actions')}</th>
            </tr>
          </thead>
          <tbody>
            {#each rows as row (`${row.kind}:${row.index}`)}
              <tr>
                <td><AssetImage src={row.iconUrl} alt="" size={38} fallbackIcon={Image} /></td>
                <td><code>{row.classname}</code> <span class="muted">#{row.id}</span></td>
                <td>{row.name}</td>
                <td>{row.category}</td>
                <td>{row.xdim}×{row.ydim}</td>
                <td>
                  <button type="button" class="ghost-button" onclick={() => openFurni(row)}>
                    {$t('common.edit')}
                  </button>
                </td>
              </tr>
            {/each}
          </tbody>
        </table>
      {:else if active === 'productdata'}
        <table>
          <thead>
            <tr><th>{$t('gamedata.code')}</th><th>{$t('gamedata.name')}</th><th>{$t('gamedata.description')}</th></tr>
          </thead>
          <tbody>
            {#each rows as row (row.index)}
              <tr><td><code>{row.code}</code></td><td>{row.name}</td><td>{row.description}</td></tr>
            {/each}
          </tbody>
        </table>
      {:else}
        <table>
          <thead>
            <tr><th>{$t('gamedata.key')}</th><th>{$t('gamedata.value')}</th><th>{$t('common.actions')}</th></tr>
          </thead>
          <tbody>
            {#each rows as row (row.key)}
              <tr>
                <td><code>{row.key}</code></td>
                <td class="value-cell">{row.value}</td>
                <td>
                  <button type="button" class="ghost-button" onclick={() => openEntry(row)}>
                    {$t('common.edit')}
                  </button>
                  <button type="button" class="ghost-button danger" onclick={() => deleteEntry(row)}>
                    {$t('common.delete')}
                  </button>
                </td>
              </tr>
            {/each}
          </tbody>
        </table>
      {/if}

      {#if rows.length}
        <Pagination
          page={page}
          pageCount={Math.max(1, Math.ceil(total / (entries.data?.pageSize ?? 50)))}
          total={total}
          pageSize={entries.data?.pageSize ?? 50}
          label={$t(`gamedata.tab_${active}`)}
          prevLabel={$t('common.prev')}
          nextLabel={$t('common.next')}
          pageWord={$t('common.page')}
          disabled={entries.loading}
          onchange={(next) => (page = next)}
        />
      {/if}
    </section>
  {/if}

  {#if draft}
    <Drawer title={draft.title} eyebrow={$t(`gamedata.tab_${active}`)} onclose={() => (draft = null)}>
      {#if draft.kind === 'entry'}
        <div class="op-field">
          <label for="entry-key">{$t('gamedata.key')}</label>
          <!-- The key is the identity of the row, so it is fixed once the row exists: renaming it
               here would silently create a second entry and leave the first behind. -->
          <input
            autocomplete="off"
            spellcheck="false"
            id="entry-key"
            bind:value={draft.key}
            disabled={!draft.creating}
          />
        </div>
        <div class="op-field">
          <label for="entry-value">{$t('gamedata.value')}</label>
          <textarea id="entry-value" rows="6" bind:value={draft.value}></textarea>
        </div>
        {#if lang}
          <p class="muted">{$t('gamedata.editingLanguage', { language: lang })}</p>
        {/if}
      {:else if draft.kind === 'furni'}
        {@const furni = draft}
        <div class="furni-row">
          <AssetImage src={draft.row.iconUrl} alt="" size={48} fallbackIcon={Image} />
          <span><strong>{draft.row.name}</strong><br /><small class="muted">{draft.row.classname}</small></span>
        </div>
        <div class="op-field">
          <label for="furni-field">{$t('gamedata.field')}</label>
          <select id="furni-field" value={furni.field} onchange={(event) => {
            const field = event.currentTarget.value as FurniField;

            draft = { ...furni, field, value: furni.row[field] ?? '' };
          }}>
            <option value="name">name</option>
            <option value="description">description</option>
            <option value="category">category</option>
            <option value="xdim">xdim</option>
            <option value="ydim">ydim</option>
          </select>
        </div>
        <div class="op-field">
          <label for="furni-value">{$t('gamedata.value')}</label>
          <input autocomplete="off" id="furni-value" bind:value={draft.value} />
        </div>
        <!-- xdim/ydim also live in furniture_definitions as Width/Length. Changing one and not the
             other makes the client draw a 2x1 whose server-side footprint is 1x1, and nothing
             reports it. -->
        {#if draft.field === 'xdim' || draft.field === 'ydim'}
          <p class="muted">{$t('gamedata.dimensionWarning')}</p>
        {/if}
      {:else}
        <p class="muted">{$t('gamedata.enableLanguageHelp')}</p>
        <div class="op-field">
          <label for="lang-code">{$t('gamedata.code')}</label>
          <input autocomplete="off" spellcheck="false" id="lang-code" bind:value={draft.code} placeholder="fr" />
          <small class="muted">{$t('gamedata.codeHelp')}</small>
        </div>
        <div class="op-field">
          <label for="lang-name">{$t('gamedata.name')}</label>
          <input autocomplete="off" id="lang-name" bind:value={draft.name} placeholder="Français" />
        </div>
      {/if}

      {#if $ops.results?.['']}<OpResult result={$ops.results['']} />{/if}

      {#snippet actions()}
        <button type="button" onclick={save} disabled={!draftValid}>{$t('common.save')}</button>
      {/snippet}
    </Drawer>
  {/if}

  <ConfirmReasonModal
    open={Boolean($ops.pending)}
    title={$ops.pending?.title ?? ''}
    summary={$ops.pending?.summary ?? ''}
    busy={$ops.busy}
    error={$ops.error}
    onconfirm={ops.confirm}
    oncancel={() => ops.cancel()}
  />
{/if}

<style>
  .panel-head-right {
    display: flex;
    align-items: center;
    gap: 12px;
  }

  /* These values are raw client strings — some are sentences, some are URLs a hundred characters
     long. Left unwrapped they push the actions column off the panel. */
  .value-cell {
    max-width: 520px;
    overflow-wrap: anywhere;
  }

  .furni-row {
    display: flex;
    align-items: center;
    gap: 12px;
    margin-bottom: 14px;
  }
</style>
