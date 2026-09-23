<script lang="ts">
  import { onMount } from 'svelte';
  import OpResult from '../components/OpResult.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import EmptyState from '../components/EmptyState.svelte';
  import AccessDeniedNotice from '../components/AccessDeniedNotice.svelte';
  import Drawer from '../components/Drawer.svelte';
  import Pagination from '../components/Pagination.svelte';
  import Tabs from '../components/Tabs.svelte';
  import PickerModal from '../components/PickerModal.svelte';
  import PlayerCell from '../components/PlayerCell.svelte';
  import ConfirmReasonModal from '../components/ConfirmReasonModal.svelte';
  import { apiGet } from '../lib/api';
  import { createWriteOps } from '../lib/writeOps';
  import { diffFields } from '../lib/changes';
  import { formatDate, formatNumber } from '../lib/format';
  import { isPermissionDeniedError } from '../lib/permissions';
  import { t } from '../lib/i18n';
  import type { PickerRow } from '../lib/pickers/directories';
  import type {
    WordFilterHitPage,
    WordFilterHitRow,
    WordFilterTestResult,
    WordFilterWordList,
    WordFilterWordRow,
  } from '../lib/apiTypes';

  // The enums travel as their numbers (no string-enum converter on the server); these name them.
  // Vortex.Primitives/Moderation/WordFilter decides the values.
  const ACTION = { replace: 0, refuse: 1 } as const;
  const MATCH = { whole: 0, partial: 1 } as const;
  const SOURCES = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];

  type WordForm = { word: string; action: number; match: number; replacement: string };

  type WordDrawer =
    | { mode: 'create'; id: null; before?: undefined; form: WordForm }
    | { mode: 'edit'; id: number; before: WordForm; form: WordForm };

  // The whole page is one capability (read and write are one job here), so a page that loaded is a
  // page that may write: there is no separate canManage to derive.
  let active = $state('words');

  let words = $state<WordFilterWordRow[]>([]);
  let wordsLoading = $state(false);
  let filter = $state('');

  let hits = $state<WordFilterHitRow[]>([]);
  let hitsTotal = $state(0);
  let hitsPage = $state(1);
  let hitsLimit = $state(50);
  let hitsLoading = $state(false);
  let hitsPlayer = $state<{ id: number; name: string } | null>(null);
  let hitsSource = $state('');
  let picking = $state(false);

  let denied = $state(false);
  let error = $state('');

  let drawer = $state<WordDrawer | null>(null);
  let testText = $state('');
  let testResult = $state<WordFilterTestResult | null>(null);
  let testTimer: ReturnType<typeof setTimeout> | undefined;

  const ops = createWriteOps(async () => {
    drawer = null;
    await loadWords();
  });

  const WORD_FIELDS = [
    { key: 'word', label: 'Word' },
    { key: 'action', label: 'Action' },
    { key: 'match', label: 'Match' },
    { key: 'replacement', label: 'Replacement' },
  ];

  let tabs = $derived([
    { id: 'words', label: $t('wordFilter.tabWords'), count: words.length },
    { id: 'journal', label: $t('wordFilter.tabJournal'), count: hitsTotal },
  ]);

  // The list is small and arrives whole; narrowing it is the browser's job.
  let shown = $derived(
    filter.trim()
      ? words.filter((w) => w.word.toLowerCase().includes(filter.trim().toLowerCase()))
      : words,
  );

  let hitsPageCount = $derived(Math.max(1, Math.ceil(hitsTotal / hitsLimit)));
  let canSave = $derived(Boolean(drawer?.form.word.trim()));

  function actionLabel(action: number) {
    return action === ACTION.refuse ? $t('wordFilter.actionRefuse') : $t('wordFilter.actionReplace');
  }

  function matchLabel(match: number) {
    return match === MATCH.partial ? $t('wordFilter.matchPartial') : $t('wordFilter.matchWhole');
  }

  // Re-tried whenever the sentence or any field of the draft moves: changing Replace to Refuse has
  // to change the verdict under the operator's eyes, not on the next keystroke in the test box.
  $effect(() => {
    void [testText, drawer?.form.word, drawer?.form.action, drawer?.form.match, drawer?.form.replacement];
    scheduleTest();
  });

  // A drawer starts with an empty test box: a sentence tried against one word must not greet the
  // next word with a verdict that was never about it.
  function resetTest() {
    clearTimeout(testTimer);
    testText = '';
    testResult = null;
  }

  function openCreate() {
    resetTest();
    drawer = {
      mode: 'create',
      id: null,
      form: { word: '', action: ACTION.replace, match: MATCH.whole, replacement: '' },
    };
  }

  function openEdit(row: WordFilterWordRow) {
    resetTest();
    const form: WordForm = {
      word: row.word,
      action: row.action,
      match: row.match,
      replacement: row.replacement ?? '',
    };

    drawer = { mode: 'edit', id: row.id, before: { ...form }, form };
  }

  function save() {
    const open = drawer!;
    const body = {
      word: open.form.word.trim(),
      action: Number(open.form.action),
      match: Number(open.form.match),
      // A refused text is never rewritten, so a replacement on a Refuse word would be a lie.
      replacement:
        Number(open.form.action) === ACTION.refuse ? null : open.form.replacement.trim() || null,
    };

    if (open.mode === 'edit') {
      ops.ask(
        '/api/v1/operations/wordfilter/words/update',
        { ...body, id: open.id },
        `${$t('wordFilter.editWord')} — ${open.before.word}`,
        $t('wordFilter.updated'),
        { changes: diffFields(open.before, open.form, WORD_FIELDS) },
      );
    } else {
      ops.ask('/api/v1/operations/wordfilter/words', body, $t('wordFilter.newWord'), $t('wordFilter.created'));
    }
  }

  function remove(row: WordFilterWordRow) {
    ops.ask(
      '/api/v1/operations/wordfilter/words/remove',
      { id: row.id },
      `${$t('wordFilter.delete')} — ${row.word}`,
      $t('wordFilter.deleteConfirm'),
      { danger: true },
    );
  }

  // Debounced: the dry run is cheap, but one request per keystroke is not.
  function scheduleTest() {
    clearTimeout(testTimer);

    if (!testText.trim()) {
      testResult = null;
      return;
    }

    testTimer = setTimeout(async () => {
      // The draft rides along: the box exists to try the word before saving it, and the server
      // tries it beside the saved list — in place of the word being edited, when there is one.
      const params = new URLSearchParams({ text: testText });
      const open = drawer;

      if (open?.form.word.trim()) {
        params.set('word', open.form.word.trim());
        params.set('action', String(Number(open.form.action)));
        params.set('match', String(Number(open.form.match)));
        if (Number(open.form.action) !== ACTION.refuse && open.form.replacement.trim()) {
          params.set('replacement', open.form.replacement.trim());
        }
        if (open.mode === 'edit') params.set('id', String(open.id));
      }

      try {
        testResult = await apiGet<WordFilterTestResult>(`/api/v1/wordfilter/test?${params}`);
      } catch {
        testResult = null;
      }
    }, 300);
  }

  async function loadWords() {
    wordsLoading = true;
    error = '';
    try {
      words = (await apiGet<WordFilterWordList>('/api/v1/wordfilter/words')).items ?? [];
      denied = false;
    } catch (e) {
      if (isPermissionDeniedError(e)) denied = true;
      else error = (e as Error)?.message ?? String(e);
    } finally {
      wordsLoading = false;
    }
  }

  async function loadHits() {
    hitsLoading = true;
    error = '';
    try {
      const params = new URLSearchParams({ page: String(hitsPage) });

      if (hitsPlayer) params.set('playerId', String(hitsPlayer.id));
      if (hitsSource !== '') params.set('source', hitsSource);

      const data = await apiGet<WordFilterHitPage>(`/api/v1/wordfilter/hits?${params}`);

      hits = data.items ?? [];
      hitsTotal = data.total ?? 0;
      hitsLimit = data.limit ?? 50;
    } catch (e) {
      if (isPermissionDeniedError(e)) denied = true;
      else error = (e as Error)?.message ?? String(e);
    } finally {
      hitsLoading = false;
    }
  }

  function refilterHits() {
    hitsPage = 1;
    void loadHits();
  }

  onMount(() => {
    void loadWords();
    void loadHits();
  });
</script>

<section class="panel">
  <PageHeader title={$t('wordFilter.title')} description={$t('wordFilter.description')}>
    {#snippet actions()}
      <button
        type="button"
        class="warning"
        onclick={() => (active === 'words' ? loadWords() : loadHits())}
        disabled={wordsLoading || hitsLoading}
      >
        {$t('common.refresh')}
      </button>
      {#if !denied}
        <button type="button" class="success" onclick={openCreate}>{$t('wordFilter.newWord')}</button>
      {/if}
    {/snippet}
  </PageHeader>
</section>

{#if denied}
  <AccessDeniedNotice message={$t('wordFilter.accessDenied')} />
{:else}
  {#if error}
    <EmptyState kind="error" message={error} />
  {/if}
  <OpResult result={$ops.result} />

  <Tabs {tabs} bind:active storageKey="wordfilter" />

  {#if active === 'words'}
    <section class="panel">
      <div class="panel-head">
        <h2>{$t('wordFilter.tabWords')} <span class="muted">({formatNumber(words.length)})</span></h2>
      </div>

      <div class="filters">
        <input
          autocomplete="off"
          spellcheck="false"
          type="search"
          placeholder={$t('wordFilter.searchPlaceholder')}
          bind:value={filter}
        />
      </div>

      {#if wordsLoading}
        <EmptyState kind="loading" />
      {:else if words.length === 0}
        <EmptyState message={$t('wordFilter.empty')} />
      {:else}
        <div class="table-scroll">
          <table>
            <thead>
              <tr>
                <th>{$t('wordFilter.word')}</th>
                <th>{$t('wordFilter.action')}</th>
                <th>{$t('wordFilter.match')}</th>
                <th>{$t('wordFilter.replacement')}</th>
                <th>{$t('wordFilter.createdBy')}</th>
                <th>{$t('wordFilter.createdAt')}</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {#each shown as row (row.id)}
                <tr>
                  <td class="mono">{row.word}</td>
                  <td>
                    <span class="status-badge {row.action === ACTION.refuse ? 'status-badge--bad' : 'status-badge--warn'}">
                      {actionLabel(row.action)}
                    </span>
                  </td>
                  <td>{matchLabel(row.match)}</td>
                  <td>
                    {#if row.action === ACTION.refuse}
                      <span class="muted">—</span>
                    {:else if row.replacement}
                      <span class="mono">{row.replacement}</span>
                    {:else}
                      <span class="muted">{$t('wordFilter.defaultReplacement')}</span>
                    {/if}
                  </td>
                  <td>{row.createdBy ?? '—'}</td>
                  <td class="muted">{formatDate(row.createdAt)}</td>
                  <td class="row-actions">
                    <button type="button" class="ghost-button" onclick={() => openEdit(row)}>
                      {$t('common.edit')}
                    </button>
                    <button type="button" class="danger" onclick={() => remove(row)}>
                      {$t('wordFilter.delete')}
                    </button>
                  </td>
                </tr>
              {/each}
            </tbody>
          </table>
        </div>
      {/if}
    </section>
  {:else}
    <section class="panel">
      <div class="panel-head">
        <h2>{$t('wordFilter.tabJournal')} <span class="muted">({formatNumber(hitsTotal)})</span></h2>
      </div>

      <!-- Filters get their own row above the table. The player comes from the directory picker,
           never a typed id. -->
      <div class="filters">
        <button type="button" class="ghost-button" onclick={() => (picking = true)}>
          {hitsPlayer ? hitsPlayer.name : $t('wordFilter.pickPlayer')}
        </button>
        {#if hitsPlayer}
          <button
            type="button"
            class="ghost-button"
            onclick={() => {
              hitsPlayer = null;
              refilterHits();
            }}
          >
            {$t('wordFilter.allPlayers')}
          </button>
        {/if}
        <select bind:value={hitsSource} onchange={refilterHits} aria-label={$t('wordFilter.source')}>
          <option value="">{$t('wordFilter.allSources')}</option>
          {#each SOURCES as source (source)}
            <option value={String(source)}>{$t(`wordFilter.sources.${source}`)}</option>
          {/each}
        </select>
      </div>

      {#if hitsLoading}
        <EmptyState kind="loading" />
      {:else if hits.length === 0}
        <EmptyState message={$t('wordFilter.journalEmpty')} />
      {:else}
        <div class="table-scroll">
          <table>
            <thead>
              <tr>
                <th>{$t('wordFilter.occurredAt')}</th>
                <th>{$t('wordFilter.player')}</th>
                <th>{$t('wordFilter.source')}</th>
                <th>{$t('wordFilter.word')}</th>
                <th>{$t('wordFilter.action')}</th>
                <th>{$t('wordFilter.originalText')}</th>
              </tr>
            </thead>
            <tbody>
              {#each hits as hit (hit.id)}
                <tr>
                  <td class="muted nowrap">{formatDate(hit.occurredAt)}</td>
                  <td><PlayerCell name={hit.playerName ?? `#${hit.playerId}`} avatarUrl={hit.avatarUrl} /></td>
                  <td>{$t(`wordFilter.sources.${hit.source}`)}</td>
                  <td class="mono">
                    {hit.word ?? $t('wordFilter.wordRemoved')}
                  </td>
                  <td>
                    <span class="status-badge {hit.action === ACTION.refuse ? 'status-badge--bad' : 'status-badge--warn'}">
                      {actionLabel(hit.action)}
                    </span>
                  </td>
                  <td class="original">{hit.originalText}</td>
                </tr>
              {/each}
            </tbody>
          </table>
        </div>

        <Pagination
          page={hitsPage}
          pageCount={hitsPageCount}
          total={hitsTotal}
          pageSize={hitsLimit}
          label={$t('wordFilter.tabJournal')}
          prevLabel={$t('common.prev')}
          nextLabel={$t('common.next')}
          pageWord={$t('common.page')}
          disabled={hitsLoading}
          onchange={(next) => {
            hitsPage = next;
            loadHits();
          }}
        />
      {/if}
    </section>
  {/if}
{/if}

{#if drawer}
  <Drawer
    title={drawer.mode === 'create' ? $t('wordFilter.newWord') : $t('wordFilter.editWord')}
    eyebrow={$t('wordFilter.title')}
    onclose={() => (drawer = null)}
  >
    <div class="drawer-form">
      <div class="op-field">
        <label for="wf-word">{$t('wordFilter.word')}</label>
        <input autocomplete="off" spellcheck="false" id="wf-word" maxlength="64" bind:value={drawer.form.word} />
        <p class="field-hint">{$t('wordFilter.wordHint')}</p>
      </div>
      <div class="op-field">
        <label for="wf-action">{$t('wordFilter.action')}</label>
        <select id="wf-action" bind:value={drawer.form.action}>
          <option value={ACTION.replace}>{$t('wordFilter.actionReplace')}</option>
          <option value={ACTION.refuse}>{$t('wordFilter.actionRefuse')}</option>
        </select>
        <p class="field-hint">{$t('wordFilter.actionHint')}</p>
      </div>
      <div class="op-field">
        <label for="wf-match">{$t('wordFilter.match')}</label>
        <select id="wf-match" bind:value={drawer.form.match}>
          <option value={MATCH.whole}>{$t('wordFilter.matchWhole')}</option>
          <option value={MATCH.partial}>{$t('wordFilter.matchPartial')}</option>
        </select>
        <p class="field-hint">{$t('wordFilter.matchHint')}</p>
      </div>
      <div class="op-field">
        <label for="wf-replacement">{$t('wordFilter.replacement')}</label>
        <input
          autocomplete="off"
          spellcheck="false"
          id="wf-replacement"
          maxlength="64"
          disabled={Number(drawer.form.action) === ACTION.refuse}
          bind:value={drawer.form.replacement}
        />
        <p class="field-hint">{$t('wordFilter.replacementHint')}</p>
      </div>

      <div class="op-field">
        <label for="wf-test">{$t('wordFilter.test')}</label>
        <textarea
          id="wf-test"
          rows="2"
          placeholder={$t('wordFilter.testPlaceholder')}
          bind:value={testText}
        ></textarea>
        <p class="field-hint">{$t('wordFilter.testHint')}</p>
        {#if testResult}
          <p class="test-result">
            {#if testResult.refused}
              <span class="status-badge status-badge--bad">{$t('wordFilter.testRefused')}</span>
            {:else if testResult.changed}
              {$t('wordFilter.testChanged')} <span class="mono">{testResult.text}</span>
            {:else}
              <span class="muted">{$t('wordFilter.testClean')}</span>
            {/if}
          </p>
        {/if}
      </div>
    </div>

    {#snippet actions()}
      <button
        type="button"
        class={drawer?.mode === 'create' ? 'success' : ''}
        onclick={save}
        disabled={!canSave}
      >
        {$t('wordFilter.save')}
      </button>
      <button type="button" class="ghost-button" onclick={() => (drawer = null)}>
        {$t('wordFilter.cancel')}
      </button>
    {/snippet}
  </Drawer>
{/if}

{#if picking}
  <PickerModal
    kind="user"
    title={$t('wordFilter.pickPlayer')}
    onSelect={(picked: PickerRow) => {
      picking = false;
      hitsPlayer = { id: Number(picked.id), name: picked.name };
      refilterHits();
    }}
    onClose={() => (picking = false)}
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

<style>
  .filters {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
    margin-bottom: 10px;
  }

  .filters input {
    flex: 1 1 220px;
  }

  .drawer-form {
    display: grid;
    gap: 14px;
  }

  .row-actions {
    display: flex;
    gap: 0.4rem;
    justify-content: flex-end;
  }

  .field-hint {
    margin: 0.25rem 0 0;
    font-size: 0.78rem;
    opacity: 0.7;
  }

  .test-result {
    margin: 0.4rem 0 0;
  }

  .mono {
    font-family: var(--font-mono, monospace);
  }

  .nowrap {
    white-space: nowrap;
  }

  .original {
    max-width: 420px;
    overflow-wrap: anywhere;
  }
</style>
