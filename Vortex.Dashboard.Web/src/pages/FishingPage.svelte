<script lang="ts">
  import { onMount } from 'svelte';
  import { Fish, Map, Trophy, Waves, Wrench } from '@lucide/svelte';
  import OpResult from '../components/OpResult.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import EmptyState from '../components/EmptyState.svelte';
  import AccessDeniedNotice from '../components/AccessDeniedNotice.svelte';
  import AssetImage from '../components/AssetImage.svelte';
  import PickerModal from '../components/PickerModal.svelte';
  import Drawer from '../components/Drawer.svelte';
  import Tabs from '../components/Tabs.svelte';
  import { apiGet } from '../lib/api';
  import { createWriteOps } from '../lib/writeOps';
  import { formatNumber } from '../lib/format';
  import { isPermissionDeniedError, hasDashboardCapability } from '../lib/permissions';
  import { CAPABILITIES } from '../lib/dashboardPermissions';
  import { identity } from '../lib/session';
  import { t } from '../lib/i18n';
  import type {
    FishingActivity,
    FishingContent,
    FishingLevelRow,
    FishingRodTierRow,
    FishingSpeciesRow,
    FishingZoneRow,
  } from '../lib/apiTypes';
  import type { PickerRow } from '../lib/pickers/directories';

  /** Which of the four tables the shared drawer is editing. */
  type FishingKind = 'zones' | 'species' | 'rods' | 'levels';

  /**
   * The drawer's form. One drawer serves four tables, so the form is whatever that table's row
   * is minus its id, and every number binds to an input that hands back a string mid-typing --
   * which is why the server-side Number() on each field stays.
   */
  type FishingForm = Record<string, string | number | boolean | null>;

  let zones = $state<FishingZoneRow[]>([]);
  let species = $state<FishingSpeciesRow[]>([]);
  let rodTiers = $state<FishingRodTierRow[]>([]);
  let levels = $state<FishingLevelRow[]>([]);
  let activity = $state<FishingActivity | null>(null);

  let tab = $state('zones');
  let loading = $state(false);
  let denied = $state(false);
  let error = $state('');

  // Filters live in their own row above each table, never in the heading.
  let speciesZoneFilter = $state('');
  let speciesQuery = $state('');

  // Open picker, if any. A zone is keyed by furni class, and nobody remembers a classname:
  // the picker searches the real definitions and hands back the name and its artwork.
  let picking = $state(false);

  // One drawer for all four tables: `kind` says which endpoint the save posts to, so adding a fifth
  // table is a row in ENDPOINTS rather than another form spliced into the page.
  let drawer = $state<{ kind: FishingKind; id: number | null; form: FishingForm } | null>(null);

  const ENDPOINTS: Record<FishingKind, { path: string; idField: string }> = {
    zones: { path: 'zones', idField: 'zoneId' },
    species: { path: 'species', idField: 'speciesId' },
    rods: { path: 'rod-tiers', idField: 'tierId' },
    levels: { path: 'levels', idField: 'levelId' },
  };

  const ops = createWriteOps(async () => {
    drawer = null;
    await load();
  });

  function emptyOf(kind: FishingKind): FishingForm {
    if (kind === 'zones') {
      return { nameKey: '', furniClass: '', requiredLevel: 0, minCatches: 1, maxCatches: 5 };
    }

    if (kind === 'species') {
      return {
        zoneId: zones[0]?.id ?? '',
        nameKey: '',
        requiredLevel: 0,
        rarityStars: 1,
        catchRate: 500,
        rarityWeight: 100,
        minWeight: 0,
        maxWeight: 0,
        xpReward: 0,
        goldenXpBonus: 0,
        currencyReward: 0,
        activeHours: 0xffffff,
        activeWeekdays: 0b1111111,
        activeSeasons: 0b1111,
      };
    }

    if (kind === 'rods') {
      return {
        quality: 1,
        xpThreshold: 0,
        nameKey: '',
        handItemId: 1000,
        catchMultiplier: 1000,
        goldenMultiplier: 1000,
        hookHavocChance: 0,
      };
    }

    return { level: 1, xpThreshold: 0 };
  }

  let canManage = $derived(hasDashboardCapability($identity, CAPABILITIES.opsFishingManage));
  let zoneName = $derived(
    (zoneId: number) => zones.find((z) => z.id === zoneId)?.nameKey ?? `#${zoneId}`,
  );
  let visibleSpecies = $derived(
    species.filter((fish) => {
      if (speciesZoneFilter && String(fish.zoneId) !== String(speciesZoneFilter)) return false;
      if (!speciesQuery.trim()) return true;

      return fish.nameKey.toLowerCase().includes(speciesQuery.trim().toLowerCase());
    })
  );

  function openCreate(kind: FishingKind) {
    drawer = { kind, id: null, form: emptyOf(kind) };
  }

  function openEdit(kind: FishingKind, row: { id: number }) {
    drawer = { kind, id: row.id, form: { ...row } };
  }

  function save() {
    // The save button only exists inside the drawer.
    const open = drawer!;
    const { path, idField } = ENDPOINTS[open.kind];

    if (open.id) {
      ops.ask(
        `/api/v1/operations/fishing/${path}/update`,
        { ...open.form, [idField]: open.id },
        $t('fishing.edit'),
        $t('fishing.updated'),
      );
    } else {
      ops.ask(
        `/api/v1/operations/fishing/${path}`,
        open.form,
        $t('fishing.add'),
        $t('fishing.created'),
      );
    }
  }

  function remove(kind: FishingKind, id: number, confirmKey = 'fishing.deleteConfirm') {
    const { path, idField } = ENDPOINTS[kind];

    ops.ask(
      `/api/v1/operations/fishing/${path}/delete`,
      { [idField]: id },
      $t('fishing.delete'),
      $t(confirmKey),
      { danger: true }
    );
  }

  async function load() {
    loading = true;
    error = '';
    try {
      const data = await apiGet<FishingContent>('/api/v1/fishing');

      zones = data.zones ?? [];
      species = data.species ?? [];
      rodTiers = data.rodTiers ?? [];
      levels = data.levels ?? [];
      denied = false;

      activity = await apiGet<FishingActivity>('/api/v1/fishing/activity');
    } catch (e) {
      if (isPermissionDeniedError(e)) denied = true;
      else error = (e as Error)?.message ?? String(e);
    } finally {
      loading = false;
    }
  }

  onMount(load);
</script>

<section class="panel">
  <PageHeader title={$t('fishing.title')} description={$t('fishing.description')}>
    {#snippet actions()}
      <button type="button" class="warning" onclick={load} disabled={loading}>
        {$t('common.refresh')}
      </button>
      {#if canManage}
        <button
          type="button"
          class="ghost-button"
          onclick={() =>
            ops.ask(
              '/api/v1/operations/fishing/reload',
              {},
              $t('fishing.reload'),
              $t('fishing.reloaded')
            )}
        >
          {$t('fishing.reload')}
        </button>
      {/if}
    {/snippet}
  </PageHeader>
</section>

{#if denied}
  <AccessDeniedNotice message={$t('fishing.accessDenied')} />
{:else}
  {#if error}
    <EmptyState kind="error" message={error} />
  {/if}
  <OpResult result={$ops.result} />

  <Tabs
    bind:active={tab}
    storageKey="fishing"
    tabs={[
      { id: 'zones', label: $t('fishing.tabZones'), icon: Map, count: zones.length },
      { id: 'species', label: $t('fishing.tabSpecies'), icon: Fish, count: species.length },
      { id: 'rods', label: $t('fishing.tabRods'), icon: Wrench, count: rodTiers.length },
      { id: 'levels', label: $t('fishing.tabLevels'), icon: Waves, count: levels.length },
      { id: 'activity', label: $t('fishing.tabActivity'), icon: Trophy },
    ]}
  />

  {#if tab === 'zones'}
    <section class="panel">
      <div class="panel-head">
        <h2>{$t('fishing.tabZones')}</h2>
        {#if canManage}
          <button type="button" class="success" onclick={() => openCreate('zones')}>{$t('fishing.add')}</button>
        {/if}
      </div>

      {#if loading}
        <EmptyState kind="loading" />
      {:else if zones.length === 0}
        <EmptyState message={$t('fishing.zonesEmpty')} />
      {:else}
        <div class="table-scroll">
          <table>
            <thead>
              <tr>
                <th>{$t('fishing.nameKey')}</th>
                <th>{$t('fishing.furniClass')}</th>
                <th>{$t('fishing.requiredLevel')}</th>
                <th>{$t('fishing.minCatches')} / {$t('fishing.maxCatches')}</th>
                <th>{$t('fishing.speciesCount')}</th>
                {#if canManage}<th></th>{/if}
              </tr>
            </thead>
            <tbody>
              {#each zones as zone (zone.id)}
                <tr>
                  <td>{zone.nameKey}</td>
                  <td>
                    <span class="furni-cell">
                      <AssetImage src={zone.furniIconUrl} alt={zone.furniClass} size={32} />
                      <span class="mono">{zone.furniClass}</span>
                    </span>
                  </td>
                  <td>{zone.requiredLevel}</td>
                  <td>{zone.minCatches} – {zone.maxCatches}</td>
                  <td>
                    {#if zone.speciesCount === 0}
                      <span class="status-badge status-badge--warn">{$t('fishing.noSpecies')}</span>
                    {:else}
                      {formatNumber(zone.speciesCount)}
                    {/if}
                  </td>
                  {#if canManage}
                    <td class="row-actions">
                      <button type="button" class="ghost-button" onclick={() => openEdit('zones', zone)}>
                        {$t('fishing.edit')}
                      </button>
                      <button
                        type="button"
                        class="danger"
                        onclick={() => remove('zones', zone.id, 'fishing.deleteZoneConfirm')}
                      >
                        {$t('fishing.delete')}
                      </button>
                    </td>
                  {/if}
                </tr>
              {/each}
            </tbody>
          </table>
        </div>
      {/if}
    </section>
  {/if}

  {#if tab === 'species'}
    <section class="panel">
      <div class="panel-head">
        <h2>{$t('fishing.tabSpecies')}</h2>
        {#if canManage}
          <button
            type="button"
            class="success"
            onclick={() => openCreate('species')}
            disabled={zones.length === 0}
          >
            {$t('fishing.add')}
          </button>
        {/if}
      </div>

      <div class="filters">
        <input
          autocomplete="off"
          spellcheck="false"
          type="search"
          placeholder={$t('fishing.nameKey')}
          bind:value={speciesQuery}
        />
        <select bind:value={speciesZoneFilter}>
          <option value="">{$t('fishing.zone')}</option>
          {#each zones as zone (zone.id)}
            <option value={zone.id}>{zone.nameKey}</option>
          {/each}
        </select>
      </div>

      {#if loading}
        <EmptyState kind="loading" />
      {:else if visibleSpecies.length === 0}
        <EmptyState message={$t('fishing.speciesEmpty')} />
      {:else}
        <div class="table-scroll">
          <table>
            <thead>
              <tr>
                <th>{$t('fishing.zone')}</th>
                <th>{$t('fishing.nameKey')}</th>
                <th>{$t('fishing.requiredLevel')}</th>
                <th>{$t('fishing.catchRate')}</th>
                <th>{$t('fishing.drawShare')}</th>
                <th>{$t('fishing.xpReward')}</th>
                <th>{$t('fishing.schedule')}</th>
                {#if canManage}<th></th>{/if}
              </tr>
            </thead>
            <tbody>
              {#each visibleSpecies as fish (fish.id)}
                <tr>
                  <td>{zoneName(fish.zoneId)}</td>
                  <td>{fish.nameKey}</td>
                  <td>{fish.requiredLevel}</td>
                  <td>{fish.catchRatePercent}%</td>
                  <td title={$t('fishing.drawShareHint')}>{fish.drawSharePercent}%</td>
                  <td>{formatNumber(fish.xpReward)}</td>
                  <td>
                    {#if fish.allHours && fish.allWeekdays}
                      <span class="muted">{$t('fishing.allHours')} · {$t('fishing.allWeekdays')}</span>
                    {:else}
                      <!-- A species only around at certain hours or on certain days is the one
                           setting here that makes a fish look missing rather than rare. Said in
                           words: the masks themselves are 24 and 7 bits, unreadable on a row. -->
                      <span class="status-badge status-badge--warn" title={$t('fishing.restrictedHint')}>
                        {$t('fishing.restricted')}
                      </span>
                    {/if}
                  </td>
                  {#if canManage}
                    <td class="row-actions">
                      <button type="button" class="ghost-button" onclick={() => openEdit('species', fish)}>
                        {$t('fishing.edit')}
                      </button>
                      <button type="button" class="danger" onclick={() => remove('species', fish.id)}>
                        {$t('fishing.delete')}
                      </button>
                    </td>
                  {/if}
                </tr>
              {/each}
            </tbody>
          </table>
        </div>
      {/if}
    </section>
  {/if}

  {#if tab === 'rods'}
    <section class="panel">
      <div class="panel-head">
        <h2>{$t('fishing.tabRods')}</h2>
        {#if canManage}
          <button type="button" class="success" onclick={() => openCreate('rods')}>{$t('fishing.add')}</button>
        {/if}
      </div>

      {#if loading}
        <EmptyState kind="loading" />
      {:else if rodTiers.length === 0}
        <EmptyState message={$t('fishing.rodsEmpty')} />
      {:else}
        <div class="table-scroll">
          <table>
            <thead>
              <tr>
                <th>{$t('fishing.quality')}</th>
                <th>{$t('fishing.nameKey')}</th>
                <th>{$t('fishing.xpThreshold')}</th>
                <th>{$t('fishing.catchMultiplier')}</th>
                <th>{$t('fishing.goldenMultiplier')}</th>
                <th>{$t('fishing.hookHavocChance')}</th>
                {#if canManage}<th></th>{/if}
              </tr>
            </thead>
            <tbody>
              {#each rodTiers as tier (tier.id)}
                <tr>
                  <td>{tier.quality}</td>
                  <td>{tier.nameKey}</td>
                  <td>{formatNumber(tier.xpThreshold)}</td>
                  <td>×{(tier.catchMultiplier / 1000).toFixed(2)}</td>
                  <td>×{(tier.goldenMultiplier / 1000).toFixed(2)}</td>
                  <td>{(tier.hookHavocChance / 10).toFixed(1)}%</td>
                  {#if canManage}
                    <td class="row-actions">
                      <button type="button" class="ghost-button" onclick={() => openEdit('rods', tier)}>
                        {$t('fishing.edit')}
                      </button>
                      <button type="button" class="danger" onclick={() => remove('rods', tier.id)}>
                        {$t('fishing.delete')}
                      </button>
                    </td>
                  {/if}
                </tr>
              {/each}
            </tbody>
          </table>
        </div>
      {/if}
    </section>
  {/if}

  {#if tab === 'levels'}
    <section class="panel">
      <div class="panel-head">
        <h2>{$t('fishing.tabLevels')}</h2>
        {#if canManage}
          <button type="button" class="success" onclick={() => openCreate('levels')}>{$t('fishing.add')}</button>
        {/if}
      </div>

      {#if loading}
        <EmptyState kind="loading" />
      {:else if levels.length === 0}
        <EmptyState message={$t('fishing.levelsEmpty')} />
      {:else}
        <div class="table-scroll">
          <table>
            <thead>
              <tr>
                <th>{$t('fishing.level')}</th>
                <th>{$t('fishing.xpThreshold')}</th>
                {#if canManage}<th></th>{/if}
              </tr>
            </thead>
            <tbody>
              {#each levels as row (row.id)}
                <tr>
                  <td>{row.level}</td>
                  <td>{formatNumber(row.xpThreshold)}</td>
                  {#if canManage}
                    <td class="row-actions">
                      <button type="button" class="ghost-button" onclick={() => openEdit('levels', row)}>
                        {$t('fishing.edit')}
                      </button>
                      <button type="button" class="danger" onclick={() => remove('levels', row.id)}>
                        {$t('fishing.delete')}
                      </button>
                    </td>
                  {/if}
                </tr>
              {/each}
            </tbody>
          </table>
        </div>
      {/if}
    </section>
  {/if}

  {#if tab === 'activity'}
    <section class="panel">
      <div class="panel-head">
        <h2>{$t('fishing.tabActivity')}</h2>
        <span class="muted">{formatNumber(activity?.anglers ?? 0)} {$t('fishing.anglers')}</span>
      </div>

      {#if loading}
        <EmptyState kind="loading" />
      {:else if !activity || activity.records.length === 0}
        <EmptyState message={$t('fishing.activityEmpty')} />
      {:else}
        <div class="table-scroll">
          <table>
            <thead>
              <tr>
                <th>{$t('fishing.player')}</th>
                <th>{$t('fishing.species')}</th>
                <th>{$t('fishing.bestWeight')}</th>
                <th>{$t('fishing.caughtCount')}</th>
                <th>{$t('fishing.bestAt')}</th>
              </tr>
            </thead>
            <tbody>
              {#each activity.records as record (record.id)}
                <tr>
                  <td>{record.playerName ?? `#${record.playerId}`}</td>
                  <td>{record.speciesNameKey ?? `#${record.speciesId}`}</td>
                  <td>{formatNumber(record.bestWeight)}</td>
                  <td>{formatNumber(record.caughtCount)}</td>
                  <td class="muted">{new Date(record.bestAt).toLocaleString()}</td>
                </tr>
              {/each}
            </tbody>
          </table>
        </div>
      {/if}

    </section>
  {/if}
{/if}

{#if picking}
  <PickerModal
    kind="furniture"
    title={$t('fishing.pickFurni')}
    onSelect={(item: PickerRow) => {
      if (drawer) {
        drawer.form.furniClass = item.name;
        drawer.form.furniIconUrl = item.iconUrl ?? null;
      }

      picking = false;
    }}
    onClose={() => (picking = false)}
  />
{/if}

{#if drawer}
  <Drawer
    title={drawer.id ? $t('fishing.edit') : $t('fishing.add')}
    eyebrow={$t(`fishing.tab${drawer.kind[0].toUpperCase()}${drawer.kind.slice(1)}`)}
    onclose={() => (drawer = null)}
  >
    <div class="drawer-form">
      {#if drawer.kind === 'zones'}
        <div class="op-field">
          <label for="z-name">{$t('fishing.nameKey')}</label>
          <input autocomplete="off" spellcheck="false" id="z-name" bind:value={drawer.form.nameKey} />
          <p class="field-hint">{$t('fishing.nameKeyHint')}</p>
        </div>
        <div class="op-field">
          <label for="z-class">{$t('fishing.furniClass')}</label>
          <div class="picker-row">
            <AssetImage
              src={drawer.form.furniIconUrl ?? ''}
              alt={String(drawer.form.furniClass ?? '')}
              size={32}
            />
            <input
              autocomplete="off"
              spellcheck="false"
              id="z-class"
              readonly
              value={drawer.form.furniClass}
              placeholder={$t('fishing.pickFurni')}
            />
            <button type="button" class="ghost-button" onclick={() => (picking = true)}>
              {$t('fishing.pickFurni')}
            </button>
          </div>
          <p class="field-hint">{$t('fishing.furniClassHint')}</p>
        </div>
        <div class="op-field">
          <label for="z-level">{$t('fishing.requiredLevel')}</label>
          <input id="z-level" type="number" min="0" bind:value={drawer.form.requiredLevel} />
        </div>
        <div class="op-field">
          <label for="z-min">{$t('fishing.minCatches')}</label>
          <input id="z-min" type="number" min="1" bind:value={drawer.form.minCatches} />
          <p class="field-hint">{$t('fishing.catchesHint')}</p>
        </div>
        <div class="op-field">
          <label for="z-max">{$t('fishing.maxCatches')}</label>
          <input id="z-max" type="number" min="1" bind:value={drawer.form.maxCatches} />
        </div>
      {:else if drawer.kind === 'species'}
        <div class="op-field">
          <label for="s-zone">{$t('fishing.zone')}</label>
          <select id="s-zone" bind:value={drawer.form.zoneId}>
            {#each zones as zone (zone.id)}
              <option value={zone.id}>{zone.nameKey}</option>
            {/each}
          </select>
        </div>
        <div class="op-field">
          <label for="s-name">{$t('fishing.nameKey')}</label>
          <input autocomplete="off" spellcheck="false" id="s-name" bind:value={drawer.form.nameKey} />
        </div>
        <div class="op-field">
          <label for="s-level">{$t('fishing.requiredLevel')}</label>
          <input id="s-level" type="number" min="0" bind:value={drawer.form.requiredLevel} />
        </div>
        <div class="op-field">
          <label for="s-stars">{$t('fishing.rarityStars')}</label>
          <input id="s-stars" type="number" min="1" max="5" bind:value={drawer.form.rarityStars} />
        </div>
        <div class="op-field">
          <label for="s-rate">{$t('fishing.catchRate')}</label>
          <input id="s-rate" type="number" min="0" max="1000" bind:value={drawer.form.catchRate} />
          <p class="field-hint">{$t('fishing.catchRateHint')}</p>
        </div>
        <div class="op-field">
          <label for="s-weight">{$t('fishing.rarityWeight')}</label>
          <input id="s-weight" type="number" min="1" bind:value={drawer.form.rarityWeight} />
          <p class="field-hint">{$t('fishing.drawShareHint')}</p>
        </div>
        <div class="op-field">
          <label for="s-minw">{$t('fishing.minWeight')}</label>
          <input id="s-minw" type="number" min="0" bind:value={drawer.form.minWeight} />
        </div>
        <div class="op-field">
          <label for="s-maxw">{$t('fishing.maxWeight')}</label>
          <input id="s-maxw" type="number" min="0" bind:value={drawer.form.maxWeight} />
        </div>
        <div class="op-field">
          <label for="s-xp">{$t('fishing.xpReward')}</label>
          <input id="s-xp" type="number" min="0" bind:value={drawer.form.xpReward} />
        </div>
        <div class="op-field">
          <label for="s-gxp">{$t('fishing.goldenXpBonus')}</label>
          <input id="s-gxp" type="number" min="0" bind:value={drawer.form.goldenXpBonus} />
        </div>
        <div class="op-field">
          <label for="s-cur">{$t('fishing.currencyReward')}</label>
          <input id="s-cur" type="number" min="0" bind:value={drawer.form.currencyReward} />
        </div>
      {:else if drawer.kind === 'rods'}
        <div class="op-field">
          <label for="r-quality">{$t('fishing.quality')}</label>
          <input id="r-quality" type="number" min="1" bind:value={drawer.form.quality} />
        </div>
        <div class="op-field">
          <label for="r-name">{$t('fishing.nameKey')}</label>
          <input autocomplete="off" spellcheck="false" id="r-name" bind:value={drawer.form.nameKey} />
        </div>
        <div class="op-field">
          <label for="r-xp">{$t('fishing.xpThreshold')}</label>
          <input id="r-xp" type="number" min="0" bind:value={drawer.form.xpThreshold} />
        </div>
        <div class="op-field">
          <label for="r-hand">{$t('fishing.handItemId')}</label>
          <input id="r-hand" type="number" min="0" bind:value={drawer.form.handItemId} />
          <p class="field-hint">{$t('fishing.handItemHint')}</p>
        </div>
        <div class="op-field">
          <label for="r-catch">{$t('fishing.catchMultiplier')}</label>
          <input id="r-catch" type="number" min="1" bind:value={drawer.form.catchMultiplier} />
          <p class="field-hint">{$t('fishing.multiplierHint')}</p>
        </div>
        <div class="op-field">
          <label for="r-golden">{$t('fishing.goldenMultiplier')}</label>
          <input id="r-golden" type="number" min="1" bind:value={drawer.form.goldenMultiplier} />
        </div>
        <div class="op-field">
          <label for="r-havoc">{$t('fishing.hookHavocChance')}</label>
          <input id="r-havoc" type="number" min="0" max="1000" bind:value={drawer.form.hookHavocChance} />
        </div>
      {:else}
        <div class="op-field">
          <label for="l-level">{$t('fishing.level')}</label>
          <input id="l-level" type="number" min="1" bind:value={drawer.form.level} />
        </div>
        <div class="op-field">
          <label for="l-xp">{$t('fishing.xpThreshold')}</label>
          <input id="l-xp" type="number" min="0" bind:value={drawer.form.xpThreshold} />
        </div>
      {/if}
    </div>

    {#snippet actions()}
      <button type="button" class={drawer?.id ? '' : 'success'} onclick={save}>
        {$t('fishing.save')}
      </button>
      <button type="button" class="ghost-button" onclick={() => (drawer = null)}>
        {$t('fishing.cancel')}
      </button>
    {/snippet}
  </Drawer>
{/if}

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

  .furni-cell,
  .picker-row {
    display: flex;
    align-items: center;
    gap: 8px;
  }

  .picker-row input {
    flex: 1 1 auto;
  }

  .field-hint {
    margin: 0.25rem 0 0;
    font-size: 0.78rem;
    opacity: 0.7;
  }

  .mono {
    font-family: var(--font-mono, monospace);
  }
</style>
