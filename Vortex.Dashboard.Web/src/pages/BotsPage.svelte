<script lang="ts">
  import { readNumberParam, writeParams } from '../lib/urlState';

  // Bots are authored from inside the client, so this page reads. What it adds over the raw table is
  // the decoded skill blob: a bot whose menu shows no buttons, or one configured to chat but with
  // zero phrases, looks identical in `bots` and completely different here.
  import { apiGet } from '../lib/api';
  import { createResource } from '../lib/resource';
  import { createWriteOps } from '../lib/writeOps';
  import { hasDashboardCapability } from '../lib/permissions';
  import { CAPABILITIES } from '../lib/dashboardPermissions';
  import { identity } from '../lib/session';
  import ConfirmReasonModal from '../components/ConfirmReasonModal.svelte';
  import LoadingOverlay from '../components/LoadingOverlay.svelte';
  import OpResult from '../components/OpResult.svelte';

  import { formatNumber } from '../lib/format';
  import { openPlayer } from '../lib/session';
  import AccessDeniedNotice from '../components/AccessDeniedNotice.svelte';
  import Drawer from '../components/Drawer.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import AssetImage from '../components/AssetImage.svelte';
  import EmptyState from '../components/EmptyState.svelte';
  import EntityLink from '../components/EntityLink.svelte';
  import LineChart from '../components/LineChart.svelte';
  import Pagination from '../components/Pagination.svelte';
  import PickerModal from '../components/PickerModal.svelte';
  import StatCard from '../components/StatCard.svelte';
  import Tabs from '../components/Tabs.svelte';
  import { Bot, MessageSquare, MapPin, Users } from '@lucide/svelte';
  import { t } from '../lib/i18n';
  import type { PickerRow } from '../lib/pickers/directories';
  import type {
    BotDetail,
    BotListItem,
    BotListResponse,
    BotStats,
  } from '../lib/apiTypes';

  /** The owner filter, held as an {id, name} pair so the button can name who was picked. */
  type PickedOwner = { id: number; name: string };

  /** The only four fields a bot can be edited on from here. */
  type BotDraft = { id: number; name: string; motto: string; figure: string };

  const PAGE_SIZE = 40;

  let term = $state('');
  let owner = $state<PickedOwner | null>(null);
  let placedFilter = $state('');
  let page = $state(readNumberParam('page', 1));

  $effect(() => {
    writeParams({ page: page > 1 ? page : '' });
  });
  let selected = $state<number | null>(null);

  // These sections are independent jobs that were stacked vertically, so reaching the last one
  // meant scrolling past every other. Nothing here is read against anything else -- which is
  // both what makes tabs right and what would have made them wrong.
  let tab = $state('roster');
  let pickingOwner = $state(false);

  // Two endpoints, one screen, so one resource: the roster is meaningless without the stats header.
  const bots = createResource(
    () => ['bots', page, term.trim(), owner?.id ?? null, placedFilter],
    async () => {
      const params = new URLSearchParams({ page: String(page), limit: String(PAGE_SIZE) });
      if (term.trim()) params.set('q', term.trim());
      if (owner) params.set('ownerId', String(owner.id));
      if (placedFilter) params.set('placed', placedFilter);

      const [list, stats] = await Promise.all([
        apiGet<BotListResponse>(`/api/v1/bots?${params}`),
        apiGet<BotStats>('/api/v1/bots/stats'),
      ]);

      return { list, stats };
    }
  );

  let { list, stats } = $derived(bots.data ?? { list: undefined, stats: undefined });

  // The expanded row's bot, keyed by which row is open: selecting a bot IS the request, and a bot
  // looked at once reopens from cache. `enabled` is what keeps it from firing with no selection.
  const detail = createResource(
    () => ['bot', selected],
    () => apiGet<BotDetail>(`/api/v1/bots/${selected}`),
    { enabled: () => selected !== null }
  );

  const ops = createWriteOps(bots.refresh);

  let canManage = $derived(hasDashboardCapability($identity, CAPABILITIES.opsContentManage));

  let botDraft = $state<BotDraft | null>(null);

  let totalPages = $derived(list ? Math.max(1, Math.ceil((list.total || 0) / (list.limit || PAGE_SIZE))) : 1);

  function goToPage(next: number) {
    page = next;
  }

  function search() {
    page = 1;
  }

  // The server returns the client's own skill identifiers; map them to copy an operator can read,
  // and show anything unrecognised as-is rather than swallowing it.
  const SKILL_LABELS: Record<string, string> = {
    dressUp: 'bots.skillDressUp',
    chatter: 'bots.skillChatter',
    randomWalk: 'bots.skillRandomWalk',
    dance: 'bots.skillDance',
    changeName: 'bots.skillChangeName',
    noPickUp: 'bots.skillNoPickUp',
  };

  function skillLabel(skill: string) {
    const key = SKILL_LABELS[skill];
    return key ? $t(key) : skill;
  }

  function clearOwner() {
    owner = null;
    search();
  }

  function saveBot() {
    const draft = botDraft!;

    ops.ask(
      '/api/v1/operations/content/bots',
      {
        botId: draft.id,
        name: draft.name,
        motto: draft.motto || '',
        figure: draft.figure || '',
      },
      $t('bots.updateBot'),
      $t('bots.updateBotSummary', { name: draft.name }),
      {
        onSuccess: () => {
          botDraft = null;
        },
      },
    );
  }

  function select(row: BotListItem) {
    selected = selected === row.id ? null : row.id;
  }

  let growthSeries = $derived(stats
    ? [
        {
          name: $t('bots.totalBots'),
          color: 'var(--accent)',
          points: (stats.growth || []).map((p) => ({ label: p.label, value: p.botsCreated })),
        },
      ]
    : []);
</script>

<section class="panel">
  <PageHeader title={$t('bots.title')} description={$t('bots.description')}>
    {#snippet actions()}
      <button type="button" onclick={bots.refresh} disabled={bots.loading} class="warning">{$t('common.refresh')}</button>
    {/snippet}
  </PageHeader>
</section>

<section class="panel">

  <form class="toolbar-grid" onsubmit={(event) => { event.preventDefault(); search(); }}>
    <label>
      {$t('bots.search')}
      <input autocomplete="off" spellcheck="false" type="search" bind:value={term} placeholder={$t('bots.searchPlaceholder')} />
    </label>
    <label>
      {$t('bots.owner')}
      <button type="button" class="ghost-button" onclick={() => (pickingOwner = true)}>
        {owner ? owner.name : $t('bots.anyOwner')}
      </button>
    </label>
    <label>
      {$t('bots.placement')}
      <select bind:value={placedFilter}>
        <option value="">{$t('bots.placementAny')}</option>
        <option value="true">{$t('bots.placementInRoom')}</option>
        <option value="false">{$t('bots.placementInventory')}</option>
      </select>
    </label>
    {#if owner}
      <button type="button" class="ghost-button" onclick={clearOwner}>{$t('bots.clearOwner')}</button>
    {/if}
  </form>

  {#if bots.forbidden}
    <AccessDeniedNotice message={$t('bots.accessDenied')} />
  {:else if bots.error}
    <p class="empty-state danger" role="alert">{bots.error}</p>
  {/if}
</section>

{#if stats}
  <div class="metric-grid" style="margin-top: 12px;">
    <StatCard label={$t('bots.totalBots')} value={formatNumber(stats.totals.totalBots)}>
      {#snippet icon()}
        <Bot size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard label={$t('bots.placedBots')} value={formatNumber(stats.totals.placedBots)} sub={$t('bots.inventoryBots', { count: formatNumber(stats.totals.inventoryBots) })}>
      {#snippet icon()}
        <MapPin size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard label={$t('bots.chattyBots')} value={formatNumber(stats.totals.chattyBots)} sub={$t('bots.autoChatBots', { count: formatNumber(stats.totals.autoChatBots) })}>
      {#snippet icon()}
        <MessageSquare size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard label={$t('bots.wanderingBots')} value={formatNumber(stats.totals.wanderingBots)}>
      {#snippet icon()}
        <Bot size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard label={$t('bots.distinctOwners')} value={formatNumber(stats.totals.distinctOwners)}>
      {#snippet icon()}
        <Users size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
    <StatCard label={$t('bots.roomsWithBots')} value={formatNumber(stats.totals.roomsWithBots)}>
      {#snippet icon()}
        <MapPin size={15} strokeWidth={2} aria-hidden="true" />
      {/snippet}
    </StatCard>
  </div>

  <div class="panel" style="margin-top: 12px;">
    <div class="panel-head"><h2>{$t('bots.growthTitle')}</h2></div>
    <LineChart series={growthSeries} valueFormatter={(v) => formatNumber(v)} />
  </div>
{/if}

{#if list}
  <Tabs
    bind:active={tab}
    storageKey="bots"
    tabs={[
      { id: 'roster', label: $t('bots.tabRoster'), icon: Bot, count: list?.items?.length },
    ]}
  />

  {#if tab === 'roster'}
  <section class="panel" style="margin-top: 12px;">
    <div class="panel-head"><h2>{$t('bots.rosterTitle')}</h2></div>
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>{$t('bots.colBot')}</th>
            <th>{$t('bots.colOwner')}</th>
            <th>{$t('bots.colRoom')}</th>
            <th>{$t('bots.colPosition')}</th>
            <th>{$t('bots.colSkills')}</th>
            <th>{$t('bots.colPhrases')}</th>
            <th>{$t('bots.colAutoChat')}</th>
            {#if canManage}<th>{$t('common.actions')}</th>{/if}
          </tr>
        </thead>
        <tbody>
          {#each list.items || [] as row}
            <tr
                class:selected={selected === row.id}
                onclick={() => select(row)}
                onkeydown={(e) => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    select(row);
                  }
                }}
                tabindex="0"
                aria-selected={selected === row.id}
                style="cursor: pointer;"
              >
              <td>
                <span class="bot-cell">
                  <AssetImage src={row.avatarUrl} alt={row.name} size={36} fallbackIcon={Bot} />
                  <span>
                    <strong>{row.name}</strong>
                    {#if row.motto}<small class="muted">{row.motto}</small>{/if}
                  </span>
                </span>
              </td>
              <td><EntityLink type="player" id={row.ownerId} label={row.ownerName} {openPlayer} /></td>
              <td>
                {#if row.roomId}
                  {row.roomName || `#${row.roomId}`}
                {:else}
                  <span class="muted">{$t('bots.inHand')}</span>
                {/if}
              </td>
              <td>{row.placed ? `${row.x}, ${row.y}` : '—'}</td>
              <td>
                {#each row.skillNames || [] as skill}
                  <span class="status-badge status-badge--ok skill-chip">{skillLabel(skill)}</span>
                {:else}
                  <span class="status-badge status-badge--warn">{$t('bots.noSkills')}</span>
                {/each}
              </td>
              <td>{formatNumber(row.phraseCount)}</td>
              <td>{row.autoChat ? $t('common.yes') : $t('common.no')}</td>
              {#if canManage}
                <td class="row-actions">
                  <button
                    type="button"
                    class="ghost-button"
                    disabled={row.placed}
                    title={row.placed ? $t('bots.placedLocked') : ''}
                    onclick={(event) => {
                      event.stopPropagation();
                      botDraft = {
                        id: row.id,
                        name: row.name,
                        motto: row.motto,
                        figure: row.figure,
                      };
                    }}
                  >
                    {$t('bots.edit')}
                  </button>
                  <button
                    type="button"
                    class="ghost-button danger"
                    disabled={row.placed}
                    title={row.placed ? $t('bots.placedLocked') : ''}
                    onclick={(event) => {
                      event.stopPropagation();
                      ops.ask(
                        '/api/v1/operations/content/bots/delete',
                        { botId: row.id },
                        $t('bots.deleteBot'),
                        $t('bots.deleteBotSummary', { name: row.name })
                      );
                    }}
                  >
                    {$t('bots.delete')}
                  </button>
                </td>
              {/if}
            </tr>
            {#if selected === row.id}
              <tr>
                <td colspan={canManage ? 8 : 7}>
                  {#if detail.loading}
                    <p class="muted">{$t('common.loading')}</p>
                  {:else if detail.error}
                    <p class="empty-state danger" role="alert">{detail.error}</p>
                  {:else if detail.data}
                    <dl class="detail-grid">
                      <div><dt>{$t('bots.detailFigure')}</dt><dd><code>{detail.data.figure}</code></dd></div>
                      <div><dt>{$t('bots.detailGender')}</dt><dd>{detail.data.gender}</dd></div>
                      <div><dt>{$t('bots.detailDelay')}</dt><dd>{detail.data.chatDelaySeconds}s</dd></div>
                      <div><dt>{$t('bots.detailMix')}</dt><dd>{detail.data.mixSentences ? $t('common.yes') : $t('common.no')}</dd></div>
                      <div><dt>{$t('bots.detailCreated')}</dt><dd>{new Date(detail.data.createdAt).toLocaleString()}</dd></div>
                    </dl>
                    {#if (detail.data.phrases || []).length > 0}
                      <ul class="phrase-list">
                        {#each detail.data.phrases as phrase}
                          <li>{phrase}</li>
                        {/each}
                      </ul>
                    {:else}
                      <EmptyState message={$t('bots.noPhrases')} />
                    {/if}
                  {/if}
                </td>
              </tr>
            {/if}
          {:else}
            <tr><td colspan={canManage ? 8 : 7} class="muted">{$t('bots.noBots')}</td></tr>
          {/each}
        </tbody>
      </table>
    </div>


    {#if $ops.result}
      <OpResult result={$ops.result} />
    {/if}

    {#if totalPages > 1}
      <Pagination
        {page}
        pageCount={totalPages}
        total={list.total}
        pageSize={list.limit}
        label={$t('bots.paginationLabel')}
        pageWord={$t('common.page')}
        prevLabel={$t('common.prev')}
        nextLabel={$t('common.next')}
        disabled={bots.loading}
        onchange={goToPage}
      />
    {/if}
  </section>
  {/if}
{/if}

{#if canManage && botDraft}
  <Drawer title={$t('bots.updateBot')} eyebrow={$t('bots.title')} onclose={() => (botDraft = null)}>
    <div class="op-field">
      <label for="bot-name">{$t('bots.colBot')}</label>
      <input autocomplete="off" spellcheck="false" id="bot-name" bind:value={botDraft.name} required />
    </div>
    <div class="op-field">
      <label for="bot-motto">{$t('bots.motto')}</label>
      <input autocomplete="off" spellcheck="false" id="bot-motto" bind:value={botDraft.motto} />
    </div>
    <div class="op-field">
      <label for="bot-figure">{$t('bots.detailFigure')}</label>
      <input autocomplete="off" spellcheck="false" id="bot-figure" bind:value={botDraft.figure} />
    </div>
    <p class="muted">{$t('bots.placedHint')}</p>

    {#snippet actions()}
      <button
        type="button"
        onclick={saveBot}
      >
        {$t('bots.save')}
      </button>
      <button type="button" class="ghost-button" onclick={() => (botDraft = null)}>{$t('bots.cancel')}</button>
    {/snippet}
  </Drawer>
{/if}

{#if pickingOwner}
  <PickerModal
    kind="user"
    title={$t('bots.pickOwner')}
    onSelect={(picked: PickerRow) => {
      owner = { id: Number(picked.id), name: picked.name };
      pickingOwner = false;
      search();
    }}
    onClose={() => (pickingOwner = false)}
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

<LoadingOverlay show={bots.loading} />

<style>
  tr.selected {
    background: var(--surface-raised, rgba(255, 255, 255, 0.04));
  }

  .bot-cell {
    display: inline-flex;
    align-items: center;
    gap: 8px;
  }

  .bot-cell small {
    display: block;
  }

  .skill-chip {
    margin-right: 4px;
  }

  .detail-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
    gap: 10px;
    margin: 0 0 12px;
  }

  .detail-grid dt {
    font-size: 0.75rem;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    opacity: 0.7;
  }

  .detail-grid dd {
    margin: 2px 0 0;
  }

  .phrase-list {
    margin: 0;
    padding-left: 18px;
  }
</style>
