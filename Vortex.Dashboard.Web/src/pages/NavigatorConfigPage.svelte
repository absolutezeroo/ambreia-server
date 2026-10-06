<script lang="ts">
  // The navigator's own configuration. Unlike every other admin page, the interesting state here is
  // emptiness: an unseeded hotel renders an empty left pane in the client whatever the room list
  // looks like, and a tab with no blocks renders blank. Both are called out at the top, with the
  // one-click seed that fills in the client's own default tabs.
  //
  // Every mutation funnels through the same ConfirmReasonModal, so the audited reason cannot be
  // skipped by a stray Enter key on one of the many small forms.
  import { onMount } from 'svelte';
  import { apiGet } from '../lib/api';
  import { createWriteOps } from '../lib/writeOps';
  import { formatNumber } from '../lib/format';
  import { isPermissionDeniedError, hasDashboardCapability } from '../lib/permissions';
  import { CAPABILITIES } from '../lib/dashboardPermissions';
  import { identity } from '../lib/session';
  import AccessDeniedNotice from '../components/AccessDeniedNotice.svelte';
  import LoadingOverlay from '../components/LoadingOverlay.svelte';
  import Drawer from '../components/Drawer.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import ConfirmReasonModal from '../components/ConfirmReasonModal.svelte';
  import EmptyState from '../components/EmptyState.svelte';
  import OpResult from '../components/OpResult.svelte';
  import StatCard from '../components/StatCard.svelte';
  import Tabs from '../components/Tabs.svelte';
  import { Compass, LayoutList, FolderTree } from '@lucide/svelte';
  import { t, type Translator } from '../lib/i18n';
  import type {
    NavigatorQueryTypeOption,
    NavigatorSearchCodeOption,
    NavigatorSetup,
  } from '../lib/apiTypes';

  /** Which of the three tables the one drawer is editing. */
  type NavigatorKind = 'context' | 'quickLink' | 'category';

  /**
   * A draft in the drawer.
   *
   * One drawer serves three tables, so this is the union of what they hold: `visible` is on all of
   * them, everything else belongs to one or two. The kind alongside it says which fields the
   * markup will read, and the operation body reads them back by name.
   */
  type NavigatorDraft = {
    id?: number;
    visible?: boolean;
    /** context, quickLink */
    searchCode?: string;
    queryType?: number | string;
    orderNum?: number | string;
    /** quickLink */
    contextId?: number;
    filter?: string;
    localization?: string;
    forceClosed?: boolean;
    /** category */
    name?: string;
    /** category */
    automatic?: boolean;
    automaticCategory?: string | null;
    globalCategory?: string | null;
    staffOnly?: boolean;
    minRank?: number | string;
  };

  let loading = $state(false);
  let forbidden = $state(false);
  let error = $state('');
  let data = $state<NavigatorSetup | null>(null);
  // Every write goes through createWriteOps: the modal collects the reason, the store posts it,
  // remembers it and refreshes -- this callback is only the page's own "clear the drafts" step.
  // What the one drawer calls itself, per kind. Passed the translator so the title follows a
  // language change like every other label on the page.
  function editorTitle(kind: NavigatorKind, translator: Translator) {
    if (kind === 'context') return translator('navigatorConfig.tabsTitle');
    if (kind === 'quickLink') return translator('navigatorConfig.quickLinksTitle');
    return translator('navigatorConfig.categoriesTitle');
  }

  // The same drawer, opened to create rather than to edit.
  function addTitle(kind: NavigatorKind, translator: Translator) {
    if (kind === 'context') return translator('navigatorConfig.addTab');
    if (kind === 'quickLink') return translator('navigatorConfig.addBlock');
    return translator('navigatorConfig.addCategory');
  }

  const ops = createWriteOps(async () => {
    editing = null;
    adding = null;
    addingContext = null;
    contextForm = newContext();
    categoryForm = newCategory();
    await refresh();
  });

  let canManage = $derived(hasDashboardCapability($identity, CAPABILITIES.opsNavigatorManage));
  let queryTypes: NavigatorQueryTypeOption[] = $derived(data?.queryTypes || []);
  let searchCodes: NavigatorSearchCodeOption[] = $derived(data?.searchCodes || []);

  const newContext = (): NavigatorDraft => ({ searchCode: '', visible: true, queryType: 0, orderNum: 0 });
  const newQuickLink = (contextId: number): NavigatorDraft => ({
    contextId,
    searchCode: '',
    filter: '',
    localization: '',
    queryType: 0,
    orderNum: 0,
    forceClosed: false,
  });
  const newCategory = (): NavigatorDraft => ({
    name: '',
    visible: true,
    automatic: false,
    automaticCategory: '',
    globalCategory: '',
    staffOnly: false,
    minRank: 1,
    orderNum: 0,
  });

  let contextForm = $state(newContext());
  let quickLinkForms = $state<Record<number, NavigatorDraft>>({});
  let categoryForm = $state(newCategory());
  let editing = $state<{
    kind: NavigatorKind;
    id: number;
    parentId: number | null;
    draft: NavigatorDraft;
  } | null>(null);
  // Which creation form the drawer is showing, if any. Adding used to be a form
  // permanently open at the foot of each section; it opens where editing opens now.
  let adding = $state<NavigatorKind | null>(null);
  // Which context a new block belongs to. The block editor is the one creator that is
  // per-parent, and the drawer has no `context` in scope once it is out of the loop.
  let addingContext = $state<number | null>(null);

  // These sections are independent jobs that were stacked vertically, so reaching the last one
  // meant scrolling past every other. Nothing here is read against anything else -- which is
  // both what makes tabs right and what would have made them wrong.
  let tab = $state('contexts');

  async function refresh() {
    loading = true;
    error = '';
    forbidden = false;

    try {
      data = await apiGet<NavigatorSetup>('/api/v1/navigator/config');
      quickLinkForms = Object.fromEntries(
        (data?.contexts || []).map((c) => [c.id, newQuickLink(c.id)]),
      );
    } catch (err) {
      if (isPermissionDeniedError(err)) {
        forbidden = true;
        data = null;
        return;
      }

      error = (err as Error).message;
      data = null;
    } finally {
      loading = false;
    }
  }

  const ask = (
    endpoint: string,
    body: Record<string, unknown>,
    title: string,
    summary: string,
  ) => ops.ask(endpoint, body, title, summary);

  function startEdit(kind: NavigatorKind, row: { id: number }, parentId: number | null = null) {
    // parentId: a quick link belongs to a tab, and the drawer no longer sits inside that tab's
    // markup, so the parent it posts against has to travel with the edit.
    editing = { kind, id: row.id, parentId, draft: { ...row } };
  }

  function queryTypeLabel(value: number) {
    return queryTypes.find((q) => q.value === value)?.label ?? value;
  }

  // Picking a known code should preselect what that code means to the client; the operator can still
  // override it, which is the whole point of the table being configurable.
  function onCodePicked(draft: NavigatorDraft, code: string) {
    draft.searchCode = code;
    const known = searchCodes.find((s) => s.code === code);
    if (known) draft.queryType = known.queryType;
  }

  onMount(() => {
    void refresh();
  });
</script>

<section class="panel">
  <PageHeader title={$t('navigatorConfig.title')} description={$t('navigatorConfig.description')}>
    {#snippet actions()}
      <button type="button" onclick={refresh} disabled={loading} class="warning">{$t('common.refresh')}</button>
      {#if canManage}
        <button
          type="button"
          class="ghost-button"
          onclick={() =>
            ask(
              '/api/v1/operations/navigator/seed-defaults',
              {},
              $t('navigatorConfig.seedTitle'),
              $t('navigatorConfig.seedSummary')
            )}
        >
          {$t('navigatorConfig.seedButton')}
        </button>
      {/if}
    {/snippet}
  </PageHeader>

  {#if forbidden}
    <AccessDeniedNotice message={$t('navigatorConfig.accessDenied')} />
  {:else if error}
    <p class="empty-state danger" role="alert">{error}</p>
  {/if}

  {#if $ops.result}
    <OpResult result={$ops.result} />
  {/if}
</section>

{#if data}
  <div class="metric-grid" style="margin-top: 12px;">
    <StatCard label={$t('navigatorConfig.tabs')} value={formatNumber(data?.health?.contextCount)}>
      {#snippet icon()}
        <Compass size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard label={$t('navigatorConfig.blocks')} value={formatNumber(data?.health?.quickLinkCount)}>
      {#snippet icon()}
        <LayoutList size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard label={$t('navigatorConfig.categories')} value={formatNumber(data?.health?.flatCategoryCount)}>
      {#snippet icon()}
        <FolderTree size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
  </div>

  {#if !data?.health.seeded || data?.health.missingTabs.length > 0 || data?.health.emptyTabs.length > 0}
    <section class="panel warn-panel" style="margin-top: 12px;">
      <div class="panel-head"><h2>{$t('navigatorConfig.healthTitle')}</h2></div>
      {#if !data?.health.seeded}
        <p>{$t('navigatorConfig.notSeeded')}</p>
      {/if}
      {#if data?.health.missingTabs.length > 0}
        <p>
          {$t('navigatorConfig.missingTabs')}
          {#each data?.health.missingTabs as code}<code class="chip">{code}</code>{/each}
        </p>
      {/if}
      {#if data?.health.emptyTabs.length > 0}
        <p>
          {$t('navigatorConfig.emptyTabs')}
          {#each data?.health.emptyTabs as tab}<code class="chip">{tab.searchCode}</code>{/each}
        </p>
      {/if}
    </section>
  {/if}

  <Tabs
    bind:active={tab}
    storageKey="navigatorConfig"
    tabs={[
      { id: 'contexts', label: $t('navigatorConfig.tabContexts'), icon: Compass, count: data?.contexts?.length },
      { id: 'categories', label: $t('navigatorConfig.tabCategories'), icon: FolderTree, count: data?.flatCategories?.length },
    ]}
  />

  {#if tab === 'contexts'}
  <section class="panel" style="margin-top: 12px;">
    <div class="panel-head"><h2>{$t('navigatorConfig.tabsTitle')}</h2>
      {#if canManage}
        <button type="button" class="success" onclick={() => (adding = 'context')}>{$t('navigatorConfig.addTab')}</button>
      {/if}
    </div>

    {#if (data?.contexts || []).length === 0}
      <EmptyState message={$t('navigatorConfig.noTabs')} />
    {/if}

    {#each data?.contexts || [] as context}
      <article class="tab-card">
        <header>
          <div>
            <strong><code>{context.searchCode}</code></strong>
            {#if !context.knownCode}
              <span class="status-badge status-badge--warn">{$t('navigatorConfig.unknownCode')}</span>
            {/if}
            {#if !context.visible}
              <span class="status-badge status-badge--unknown">{$t('navigatorConfig.hidden')}</span>
            {/if}
            <small class="muted">
              {queryTypeLabel(context.queryType)} · {$t('navigatorConfig.order')} {context.orderNum}
            </small>
          </div>
          {#if canManage}
            <div class="row-actions">
              <button type="button" class="ghost-button" onclick={() => startEdit('context', context)}>
                {$t('navigatorConfig.edit')}
              </button>
              <button
                type="button"
                class="ghost-button danger"
                onclick={() =>
                  ask(
                    '/api/v1/operations/navigator/contexts/delete',
                    { contextId: context.id },
                    $t('navigatorConfig.deleteTab'),
                    $t('navigatorConfig.deleteTabSummary', { code: context.searchCode })
                  )}
              >
                {$t('navigatorConfig.delete')}
              </button>
            </div>
          {/if}
        </header>


        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{$t('navigatorConfig.colBlockCode')}</th>
                <th>{$t('navigatorConfig.colQueryType')}</th>
                <th>{$t('navigatorConfig.colFilter')}</th>
                <th>{$t('navigatorConfig.colLocalization')}</th>
                <th>{$t('navigatorConfig.colForceClosed')}</th>
                <th>{$t('navigatorConfig.colOrder')}</th>
                {#if canManage}<th>{$t('common.actions')}</th>{/if}
              </tr>
            </thead>
            <tbody>
              {#each context.quickLinks || [] as link}
                <tr>
                  <td>
                    <code>{link.searchCode}</code>
                    {#if !link.knownCode}
                      <span class="status-badge status-badge--warn">{$t('navigatorConfig.unknownCode')}</span>
                    {/if}
                  </td>
                  <td>{queryTypeLabel(link.queryType)}</td>
                  <td>{link.filter || '—'}</td>
                  <td>{link.localization || '—'}</td>
                  <td>{link.forceClosed ? $t('common.yes') : $t('common.no')}</td>
                  <td>{link.orderNum}</td>
                  {#if canManage}
                    <td class="row-actions">
                      <button type="button" class="ghost-button" onclick={() => startEdit('quickLink', link, context.id)}>
                        {$t('navigatorConfig.edit')}
                      </button>
                      <button
                        type="button"
                        class="ghost-button danger"
                        onclick={() =>
                          ask(
                            '/api/v1/operations/navigator/quick-links/delete',
                            { quickLinkId: link.id },
                            $t('navigatorConfig.deleteBlock'),
                            $t('navigatorConfig.deleteBlockSummary', { code: link.searchCode })
                          )}
                      >
                        {$t('navigatorConfig.delete')}
                      </button>
                    </td>
                  {/if}
                </tr>
              {:else}
                <tr>
                  <td colspan={canManage ? 6 : 5} class="muted">{$t('navigatorConfig.noBlocks')}</td>
                </tr>
              {/each}
            </tbody>
          </table>
        </div>

        {#if canManage}
          <button type="button" class="success" onclick={() => { adding = 'quickLink'; addingContext = context.id; }}>
            {$t('navigatorConfig.addBlock')}
          </button>
        {/if}
      </article>
    {/each}

  </section>
  {/if}

  {#if tab === 'categories'}
  <section class="panel" style="margin-top: 12px;">
    <div class="panel-head"><h2>{$t('navigatorConfig.categoriesTitle')}</h2>
      {#if canManage}
        <button type="button" class="success" onclick={() => (adding = 'category')}>{$t('navigatorConfig.addCategory')}</button>
      {/if}
    </div>
    <p class="muted">{$t('navigatorConfig.categoriesDescription')}</p>
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>{$t('navigatorConfig.colId')}</th>
            <th>{$t('navigatorConfig.colName')}</th>
            <th>{$t('navigatorConfig.colRooms')}</th>
            <th>{$t('navigatorConfig.colVisible')}</th>
            <th>{$t('navigatorConfig.colStaffOnly')}</th>
            <th>{$t('navigatorConfig.colMinRank')}</th>
            <th>{$t('navigatorConfig.colOrder')}</th>
            {#if canManage}<th>{$t('common.actions')}</th>{/if}
          </tr>
        </thead>
        <tbody>
          {#each data?.flatCategories || [] as category}
            <tr>
              <td>{category.id}</td>
              <td>{category.name}</td>
              <td>{formatNumber(category.roomCount)}</td>
              <td>{category.visible ? $t('common.yes') : $t('common.no')}</td>
              <td>{category.staffOnly ? $t('common.yes') : $t('common.no')}</td>
              <td>{category.minRank}</td>
              <td>{category.orderNum}</td>
              {#if canManage}
                <td class="row-actions">
                  <button type="button" class="ghost-button" onclick={() => startEdit('category', category)}>
                    {$t('navigatorConfig.edit')}
                  </button>
                  <button
                    type="button"
                    class="ghost-button danger"
                    onclick={() =>
                      ask(
                        '/api/v1/operations/navigator/categories/delete',
                        { categoryId: category.id },
                        $t('navigatorConfig.deleteCategory'),
                        $t('navigatorConfig.deleteCategorySummary', {
                          name: category.name,
                          rooms: category.roomCount,
                        })
                      )}
                  >
                    {$t('navigatorConfig.delete')}
                  </button>
                </td>
              {/if}
            </tr>
          {:else}
            <tr>
              <td colspan={canManage ? 8 : 7} class="muted">{$t('navigatorConfig.noCategories')}</td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>

  </section>
  {/if}

{/if}

{#if editing}
  {@const open = editing}
  <!-- One drawer for all three editors: `editing` was already a single {kind, id, draft},
       so what used to unfold inside four different lists is one panel that switches on kind. -->
  <Drawer
    title={editorTitle(open.kind, $t)}
    eyebrow={$t('navigatorConfig.title')}
    onclose={() => (editing = null)}
  >
    {#if open.kind === 'context'}
            <form
              class="edit-grid" id="navigator-editor"
              onsubmit={(event) => {
                event.preventDefault();
                ask(
                  '/api/v1/operations/navigator/contexts/update',
                  { contextId: open.id, ...open.draft },
                  $t('navigatorConfig.updateTab'),
                  $t('navigatorConfig.updateTabSummary', { code: open.draft.searchCode })
                );
              }}
            >
              <label>
                {$t('navigatorConfig.searchCode')}
                <input autocomplete="off" spellcheck="false" bind:value={open.draft.searchCode} required />
              </label>
              <label>
                {$t('navigatorConfig.queryType')}
                <select bind:value={open.draft.queryType}>
                  {#each queryTypes as q}
                    <option value={q.value}>{q.label}</option>
                  {/each}
                </select>
              </label>
              <label>
                {$t('navigatorConfig.order')}
                <input autocomplete="off" spellcheck="false" type="number" bind:value={open.draft.orderNum} />
              </label>
              <label class="check">
                <input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={open.draft.visible} />
                {$t('navigatorConfig.visible')}
              </label>
            </form>
    {:else if open.kind === 'quickLink'}
                        <form
                          class="edit-grid" id="navigator-editor"
                          onsubmit={(event) => {
                            event.preventDefault();
                            ask(
                              '/api/v1/operations/navigator/quick-links/update',
                              {
                                quickLinkId: open.id,
                                contextId: open.parentId,
                                searchCode: open.draft.searchCode,
                                filter: open.draft.filter || '',
                                localization: open.draft.localization || '',
                                queryType: open.draft.queryType,
                                orderNum: open.draft.orderNum,
                                forceClosed: open.draft.forceClosed ?? false,
                              },
                              $t('navigatorConfig.updateBlock'),
                              $t('navigatorConfig.updateBlockSummary', { code: open.draft.searchCode })
                            );
                          }}
                        >
                          <label>
                            {$t('navigatorConfig.searchCode')}
                            <input autocomplete="off" spellcheck="false" bind:value={open.draft.searchCode} required />
                          </label>
                          <label>
                            {$t('navigatorConfig.queryType')}
                            <select bind:value={open.draft.queryType}>
                              {#each queryTypes as q}
                                <option value={q.value}>{q.label}</option>
                              {/each}
                            </select>
                          </label>
                          <label>
                            {$t('navigatorConfig.colFilter')}
                            <input autocomplete="off" spellcheck="false" bind:value={open.draft.filter} />
                          </label>
                          <label>
                            {$t('navigatorConfig.colLocalization')}
                            <input autocomplete="off" spellcheck="false" bind:value={open.draft.localization} />
                          </label>
                          <label>
                            {$t('navigatorConfig.order')}
                            <input autocomplete="off" spellcheck="false" type="number" bind:value={open.draft.orderNum} />
                          </label>
                          <label class="check">
                            <input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={open.draft.forceClosed} />
                            {$t('navigatorConfig.colForceClosed')}
                          </label>
                        </form>
    {:else if open.kind === 'category'}
                    <form
                      class="edit-grid" id="navigator-editor"
                      onsubmit={(event) => {
                        event.preventDefault();
                        ask(
                          '/api/v1/operations/navigator/categories/update',
                          { categoryId: open.id, ...open.draft },
                          $t('navigatorConfig.updateCategory'),
                          $t('navigatorConfig.updateCategorySummary', { name: open.draft.name })
                        );
                      }}
                    >
                      <label>
                        {$t('navigatorConfig.colName')}
                        <input autocomplete="off" spellcheck="false" bind:value={open.draft.name} required />
                      </label>
                      <label>
                        {$t('navigatorConfig.colMinRank')}
                        <input autocomplete="off" spellcheck="false" type="number" bind:value={open.draft.minRank} />
                      </label>
                      <label>
                        {$t('navigatorConfig.order')}
                        <input autocomplete="off" spellcheck="false" type="number" bind:value={open.draft.orderNum} />
                      </label>
                      <label class="check">
                        <input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={open.draft.visible} />
                        {$t('navigatorConfig.visible')}
                      </label>
                      <label class="check">
                        <input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={open.draft.staffOnly} />
                        {$t('navigatorConfig.colStaffOnly')}
                      </label>
                    </form>
    {/if}

    {#snippet actions()}
      <button type="submit" form="navigator-editor">{$t('navigatorConfig.save')}</button>
      <button type="button" class="ghost-button" onclick={() => (editing = null)}>{$t('common.cancel')}</button>
    {/snippet}
  </Drawer>
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

<LoadingOverlay show={loading} />

<style>
  .tab-card {
    border: 1px solid var(--border, rgba(255, 255, 255, 0.08));
    border-radius: 8px;
    padding: 12px;
    margin-bottom: 12px;
  }

  .tab-card header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    flex-wrap: wrap;
    margin-bottom: 8px;
  }

  .tab-card header small {
    display: block;
  }

  .row-actions {
    display: inline-flex;
    gap: 6px;
    flex-wrap: wrap;
  }

  .edit-grid {
    display: flex;
    flex-wrap: wrap;
    align-items: flex-end;
    gap: 10px;
    margin-top: 10px;
  }

  .edit-grid label {
    display: flex;
    flex-direction: column;
    gap: 4px;
    font-size: 0.8rem;
  }

  .edit-grid label.check {
    flex-direction: row;
    align-items: center;
    gap: 6px;
  }

  .chip {
    margin-right: 6px;
  }

  .warn-panel {
    border-left: 3px solid var(--warn, #e0a33e);
  }

</style>

{#if adding}
  {@const kind = adding}
  <Drawer title={addTitle(kind, $t)} eyebrow={$t('navigatorConfig.title')} onclose={() => (adding = null)}>
    {#if kind === 'context'}
    <form
      class="edit-grid" id="navigator-creator"
      onsubmit={(event) => {
        event.preventDefault();
        ask(
          '/api/v1/operations/navigator/contexts',
          contextForm,
          $t('navigatorConfig.addTab'),
          $t('navigatorConfig.addTabSummary', { code: contextForm.searchCode })
        );
      }}
    >
      <label>
        {$t('navigatorConfig.searchCode')}
        <select value={contextForm.searchCode} onchange={(e) => onCodePicked(contextForm, e.currentTarget.value)}>
          <option value="">{$t('navigatorConfig.pickCode')}</option>
          {#each searchCodes.filter((c) => c.topLevel) as code}
            <option value={code.code}>{code.code} — {code.queryTypeLabel}</option>
          {/each}
        </select>
      </label>
      <label>
        {$t('navigatorConfig.queryType')}
        <select bind:value={contextForm.queryType}>
          {#each queryTypes as q}
            <option value={q.value}>{q.label}</option>
          {/each}
        </select>
      </label>
      <label>
        {$t('navigatorConfig.order')}
        <input autocomplete="off" spellcheck="false" type="number" bind:value={contextForm.orderNum} />
      </label>
      <label class="check">
        <input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={contextForm.visible} />
        {$t('navigatorConfig.visible')}
      </label>
      </form>
    {:else if kind === 'category'}
    <form
      class="edit-grid" id="navigator-creator"
      onsubmit={(event) => {
        event.preventDefault();
        ask(
          '/api/v1/operations/navigator/categories',
          categoryForm,
          $t('navigatorConfig.addCategory'),
          $t('navigatorConfig.addCategorySummary', { name: categoryForm.name })
        );
      }}
    >
      <label>
        {$t('navigatorConfig.colName')}
        <input autocomplete="off" spellcheck="false" bind:value={categoryForm.name} placeholder={$t('navigatorConfig.categoryPlaceholder')} />
      </label>
      <label>
        {$t('navigatorConfig.colMinRank')}
        <input autocomplete="off" spellcheck="false" type="number" bind:value={categoryForm.minRank} />
      </label>
      <label>
        {$t('navigatorConfig.order')}
        <input autocomplete="off" spellcheck="false" type="number" bind:value={categoryForm.orderNum} />
      </label>
      <label class="check">
        <input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={categoryForm.staffOnly} />
        {$t('navigatorConfig.colStaffOnly')}
      </label>
      </form>
    {:else if kind === 'quickLink'}
          <form
            class="edit-grid" id="navigator-creator"
            onsubmit={(event) => {
              event.preventDefault();
              ask(
                '/api/v1/operations/navigator/quick-links',
                {
                  contextId: addingContext!,
                  searchCode: quickLinkForms[addingContext!].searchCode,
                  filter: quickLinkForms[addingContext!].filter || '',
                  localization: quickLinkForms[addingContext!].localization || '',
                  queryType: quickLinkForms[addingContext!].queryType,
                  orderNum: quickLinkForms[addingContext!].orderNum,
                  forceClosed: quickLinkForms[addingContext!].forceClosed ?? false,
                },
                $t('navigatorConfig.addBlock'),
                $t('navigatorConfig.addBlockSummary', {
                  code: quickLinkForms[addingContext!].searchCode,
                  // The creator form lives outside the {#each ... as context} loop, so it has the
                  // id the button stored and not the row it came from. Reading `context` here was
                  // an undefined identifier: `npm run build` compiles it to a global lookup and
                  // ships a confirmation dialog that throws when an operator opens it.
                  tab: (data?.contexts || []).find((c) => c.id === addingContext)?.searchCode ?? '',
                })
              );
            }}
          >
            <label>
              {$t('navigatorConfig.searchCode')}
              <select
                value={quickLinkForms[addingContext!].searchCode}
                onchange={(e) => onCodePicked(quickLinkForms[addingContext!], e.currentTarget.value)}
              >
                <option value="">{$t('navigatorConfig.pickCode')}</option>
                {#each searchCodes as code}
                  <option value={code.code}>{code.code} — {code.queryTypeLabel}</option>
                {/each}
              </select>
            </label>
            <label>
              {$t('navigatorConfig.queryType')}
              <select bind:value={quickLinkForms[addingContext!].queryType}>
                {#each queryTypes as q}
                  <option value={q.value}>{q.label}</option>
                {/each}
              </select>
            </label>
            <label>
              {$t('navigatorConfig.order')}
              <input autocomplete="off" spellcheck="false" type="number" bind:value={quickLinkForms[addingContext!].orderNum} />
            </label>
            <label class="check">
              <input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={quickLinkForms[addingContext!].forceClosed} />
              {$t('navigatorConfig.colForceClosed')}
            </label>
          </form>
    {/if}

    {#snippet actions()}
      <button
        type="submit"
        form="navigator-creator"
        class="success"
        disabled={kind === 'context'
          ? !contextForm.searchCode
          : kind === 'category'
            ? !categoryForm.name?.trim()
            : !quickLinkForms[addingContext!]?.searchCode}
      >{addTitle(kind, $t)}</button>
      <button type="button" class="ghost-button" onclick={() => (adding = null)}>{$t('common.cancel')}</button>
    {/snippet}
  </Drawer>
{/if}
