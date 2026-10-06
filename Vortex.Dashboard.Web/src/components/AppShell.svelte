<script lang="ts">
  import type { Component } from 'svelte';

  import { location, push } from 'svelte-spa-router';
  import CommandPalette from './CommandPalette.svelte';
  import ToastHost from './ToastHost.svelte';
  import { SvelteSet } from 'svelte/reactivity';
  import {
    Activity,
    BarChart3,
    Box,
    ChevronDown,
    ChevronRight,
    Coins,
    DoorOpen,
    Gavel,
    Home,
    LineChart,
    MessageCircleWarning,
    Package,
    PawPrint,
    ScrollText,
    Search,
    Server,
    ShieldAlert,
    ShoppingBag,
    ShoppingCart,
    Sparkles,
    Store,
    Terminal,
    Ticket,
    Users,
    Wrench,
    Lock,
    Trophy,
    Bot,
    Compass,
    MessagesSquare,
    ShieldCheck,
    Award,
    Flag,
    Gift,
    SlidersHorizontal,
    Gauge,
    PencilLine,
    MessageSquareText,
    Music,
    Fish,
    ClipboardList,
    Newspaper,
    FileCode,
    SquareTerminal,
    Medal,
  } from '@lucide/svelte';
  import { identity } from '../lib/session';
  import MfaModal from './MfaModal.svelte';
  import { NAV, hasRouteAccess, foldSearch } from '../lib/routes';
  import { reasonSuggestions } from '../lib/reasonHistory';
  import { theme, setTheme, THEMES } from '../lib/theme';
  import { t, locale, setLocale, LOCALES } from '../lib/i18n';

  type Props = {
    logout: any;
    logoutBusy?: boolean;
    children?: import('svelte').Snippet;
  };

  let { logout, logoutBusy = false, children }: Props = $props();

  // Keep in sync with the `group` field on NAV entries (routes.js). Groups are domains, not tool
  // kinds: an operator looks for "the quest thing", not for "the editing thing". None may exceed
  // ~8 entries -- see the note above NAV for why Content was split and Social folded away.
  const GROUP_ORDER = [
    'Live',
    'Players',
    'Moderation',
    'Rooms',
    'Economy',
    'Progression',
    'Prizes',
    'Content',
    'System',
  ];

  const routeIcons: Record<string, Component<Record<string, unknown>>> = {
    '/overview': Activity,
    '/infrastructure': Server,
    '/investigation': Search,
    '/rooms': Home,
    '/packets': Package,
    '/economy': Coins,
    '/incidents': ShieldAlert,
    '/audit': ScrollText,
    '/moderation': Gavel,
    '/api-explorer': Terminal,
    '/operations': Wrench,
    '/cfh': MessageCircleWarning,
    '/room-control': DoorOpen,
    '/vouchers': Ticket,
    '/catalog': Store,
    '/furniture-definitions': Box,
    '/economy-trends': LineChart,
    '/marketplace': ShoppingCart,
    '/subscriptions': Sparkles,
    '/groups-stats': Users,
    '/pets-stats': PawPrint,
    '/cfh-stats': BarChart3,
    '/catalog-purchases': ShoppingBag,
    '/achievements': Trophy,
    '/bots': Bot,
    '/navigator-config': Compass,
    '/social': MessagesSquare,
    '/staff': ShieldCheck,
    '/economy-extras': Store,
    '/player-rewards': Award,
    '/quests': Flag,
    '/prize-pools': Gift,
    '/targeted-offers': Ticket,
    '/targeted-offers-stats': BarChart3,
    '/config': SlidersHorizontal,
    '/performance': Gauge,
    // Every entry below used to fall through to the generic Activity icon, which made a dozen rows
    // look identical in a sidebar you scan rather than read.
    '/chatlogs': MessageSquareText,
    '/songs': Music,
    '/fishing': Fish,
    '/polls': ClipboardList,
    '/articles': Newspaper,
    '/gamedata': FileCode,
    '/console': SquareTerminal,
    '/achievement-resolutions': Medal,
  };

  let query = $state('');

  // Which nav groups are collapsed, keyed by the stable GROUP_ORDER id (not the translated label,
  // which changes with $locale and would silently reset saved state on a language switch).
  // Persisted so a collapse choice survives reloads. The key carries a version: the v1 sets were
  // written against the old seven groups, and replaying one after the Content split would leave
  // Progression and Prizes expanded for every returning operator (an id absent from the saved set
  // reads as "not collapsed"). Bumping it re-seeds instead.
  const COLLAPSE_STORAGE_KEY = 'vortex-dashboard-nav-collapsed.v2';

  function loadCollapsedGroups() {
    try {
      const raw = localStorage.getItem(COLLAPSE_STORAGE_KEY);

      if (raw) {
        return new SvelteSet(JSON.parse(raw));
      }
    } catch {
      // Ignore storage failures (private browsing, quota) -- fall through to the default below.
    }

    // First visit: everything folded except the group the current route belongs to. Forty entries
    // open at once is the state that made the sidebar unreadable; one open group is a menu.
    const active = NAV.find((item) => item.path === window.location.hash.slice(1));

    return new SvelteSet(GROUP_ORDER.filter((group) => group !== (active?.group ?? 'Live')));
  }

  // SvelteSet, not Set: `$state` deep-proxies plain objects and arrays but leaves a Set alone, so
  // `.add()` / `.delete()` on a plain one signal nothing. Svelte 4 papered over that with a
  // `collapsedGroups = collapsedGroups` self-assignment, which is a no-op under runes -- assigning
  // an identical reference is skipped. That is what silently killed every nav group toggle.
  /** A nav row as the shell builds it: the route plus what the sidebar needs to draw it. */
  type NavEntry = {
    path: string;
    label: string;
    short: string;
    keywords?: string;
    group: string;
    allowed: boolean;
    /** True for a page that can change something; the sidebar marks those. */
    writes?: boolean;
  };

  const collapsedGroups = loadCollapsedGroups();

  function toggleGroup(id: string) {
    if (collapsedGroups.has(id)) {
      collapsedGroups.delete(id);
    } else {
      collapsedGroups.add(id);
    }

    try {
      localStorage.setItem(COLLAPSE_STORAGE_KEY, JSON.stringify([...collapsedGroups]));
    } catch {
      // Ignore storage failures (private browsing, quota) -- collapse still works this session.
    }
  }

  // A collapsed group still expands while actively searching, so filter results stay visible.
  // (The set is passed in rather than closed over for readability; under runes either tracks, since
  // a SvelteSet reports its own reads wherever they happen.)
  function isCollapsed(
    group: { id: string },
    q: string,
    groupsCollapsed: { has: (id: string) => boolean },
  ) {
    return groupsCollapsed.has(group.id) && !q.trim();
  }



  function revealActiveGroup(path: string) {
    const active = NAV.find((item) => item.path === path);

    if (active && collapsedGroups.has(active.group)) {
      collapsedGroups.delete(active.group);
    }
  }

  // Matches the label, the short AND the route's `keywords` (routes.js), so the box answers what an
  // operator wants to DO -- "ban", "ducats", "jukebox" -- and not only the name of the page they
  // would have to know already. Everything is accent-folded on both sides.
  function filterItems(list: NavEntry[], q: string) {
    const needle = foldSearch(q.trim());

    if (!needle) {
      return list;
    }

    return list.filter((item: NavEntry) =>
      foldSearch(`${item.label} ${item.short} ${item.keywords || ''}`).includes(needle),
    );
  }

  function iconFor(item: NavEntry) {
    return routeIcons[item.path] || Activity;
  }

  // The sidebar's own artwork, named `ui_navigator_icon_<route>.png` after the route it belongs to
  // (`/quests` -> ui_navigator_icon_quests.png, `/pets-stats` -> ui_navigator_icon_pets-stats.png).
  // Drawn by tools/build-nav-icons.mjs, which is where the pixels are edited.
  //
  // Globbed rather than imported one by one, and that is the whole point: a static import of a file
  // that has not been drawn yet fails the build, so fifty-odd imports would mean drawing fifty-odd
  // icons before any of them could ship. This picks up whatever exists at build time and leaves the
  // rest on their lucide glyph, so the set can be filled in one icon at a time.
  // Keyed by slug rather than by path so the extension does not matter: a route's art can be a PNG
  // or an SVG, and swapping one for the other is dropping in a file, not editing this.
  const navImages: Record<string, string> = Object.fromEntries(
    Object.entries(
      import.meta.glob('../assets/images/ui_navigator_icon_*.{png,svg}', {
        eager: true,
        query: '?url',
        import: 'default',
      }) as Record<string, string>,
    ).map(([key, url]) => [key.replace(/^.*ui_navigator_icon_|\.(png|svg)$/g, ''), url]),
  );

  function imageFor(item: NavEntry) {
    return navImages[item.path.replace(/^\//, '')] ?? null;
  }

  // An icon whose filename does not match a route is the one failure this mechanism has, and it is
  // a silent one: the file is bundled, no route ever asks for it, and the nav entry quietly keeps
  // its lucide glyph. A misplaced underscore or a missing plural looks exactly like "not drawn
  // yet". So the mismatches are named out loud, once, while developing.
  if (import.meta.env.DEV) {
    const known = new Set(NAV.map((item) => item.path.replace(/^\//, '')));
    const orphans = Object.keys(navImages).filter((slug) => !known.has(slug));

    if (orphans.length > 0) {
      console.warn(
        `[nav] ${orphans.length} sidebar icon(s) match no route and will never be shown: ` +
          `${orphans.join(', ')}. The filename must be the route path, e.g. /pets-stats -> ` +
          'ui_navigator_icon_pets-stats.png',
      );
    }
  }

  function go(item: NavEntry) {
    if (item.allowed) {
      push(item.path);
    }
  }
  // Re-evaluate access whenever the identity changes (login / logout / role swap). label/short are
  // resolved here (not read directly off NAV) so they re-translate whenever $locale changes too --
  // $t is referenced directly in this statement's own expression for that reason (see the Svelte
  // reactivity note on ApiExplorerPage's `filtered` for why that matters).
  let items = $derived(NAV.map((item) => ({
    ...item,
    label: $t(item.labelKey),
    short: $t(item.shortKey),
    allowed: hasRouteAccess(item, $identity),
  })));
  let groupLabels: Record<string, string> = $derived({
    Live: $t('nav.groupLive'),
    Players: $t('nav.groupPlayers'),
    Moderation: $t('nav.groupModeration'),
    Rooms: $t('nav.groupRooms'),
    Economy: $t('nav.groupEconomy'),
    Progression: $t('nav.groupProgression'),
    Prizes: $t('nav.groupPrizes'),
    Content: $t('nav.groupContent'),
    System: $t('nav.groupSystem'),
  });
  // Navigating into a folded group -- a deep link, a search result, the access-denied redirect --
  // opens it. A page whose own nav entry stays hidden reads as a dead end.
  $effect(() => {
    revealActiveGroup($location);
  });
  let filteredItems = $derived(filterItems(items, query));
  let groups = $derived(GROUP_ORDER.map((name) => ({
    id: name,
    name: groupLabels[name] || name,
    items: filteredItems.filter((item) => (item.group || 'Other') === name),
  })).filter((group) => group.items.length > 0));
  let email = $derived($identity?.email || '');
  let mfaOpen = $state(false);
  let activeLabel = $derived(items.find((item) => item.path === $location)?.label || $t('nav.dashboardFallback'));
</script>

<!-- First tab stop on every page: the sidebar is ~40 links, and a keyboard operator should not
     have to walk all of them to reach the table they came for. -->
<a class="skip-link" href="#main-content">{$t('nav.skipToContent')}</a>

<CommandPalette />
<ToastHost />

<main class="app-shell">
  <aside class="sidebar">
    <div class="brand">
      <span class="brand-mark" aria-hidden="true">
        <svg width="22" height="22" viewBox="0 0 24 24" fill="none">
          <path d="M3 11 12 4l9 7v8a1 1 0 0 1-1 1h-5v-6H9v6H4a1 1 0 0 1-1-1z" fill="currentColor" />
        </svg>
      </span>
      <div>
        <strong>{$t('nav.brandTitle')}</strong>
        <small>{$t('nav.brandSubtitle')}</small>
      </div>
    </div>

    <div class="nav-search">
      <Search size={15} strokeWidth={1.9} aria-hidden="true" />
      <input
        autocomplete="off"
        spellcheck="false"
        class="bare"
        type="search"
        name="nav-filter"
        id="nav-filter"
        placeholder={$t('nav.searchPlaceholder')}
        aria-label={$t('nav.searchPlaceholder')}
        bind:value={query}
      />
      <!-- This box only filters the sidebar. The palette is what searches players, rooms and
           furniture, and a shortcut nobody is told about is a shortcut nobody uses. -->
      <kbd class="nav-kbd">{$t('palette.shortcutHint')}</kbd>
    </div>

    <nav aria-label={$t('nav.sectionsLabel')}>
      {#each groups as group}
        {@const collapsed = isCollapsed(group, query, collapsedGroups)}
        {@const SvelteComponent = collapsed ? ChevronRight : ChevronDown}
        <button
          type="button"
          class="nav-group-label"
          onclick={() => toggleGroup(group.id)}
          aria-expanded={!collapsed}
        >
          <SvelteComponent size={13} strokeWidth={2.2} aria-hidden="true" />
          <span>{group.name}</span>
          <span class="nav-group-count">{group.items.length}</span>
        </button>
        {#if !collapsed}
          {#each group.items as item}
            {@const Icon = iconFor(item)}
            {@const art = imageFor(item)}
            <a
              href={`#${item.path}`}
              class:active={$location === item.path}
              class:disabled={!item.allowed}
              aria-disabled={!item.allowed}
              tabindex={item.allowed ? 0 : -1}
              onclick={(event) => { event.preventDefault(); go(item); }}
            >
              <span class="nav-icon" class:nav-icon--art={art} aria-hidden="true">
                {#if art}
                  <img src={art} alt="" width="24" height="24" />
                {:else}
                  <Icon size={18} strokeWidth={1.9} />
                {/if}
              </span>
              <span class="nav-copy">
                {#if !item.allowed}
                  <span class="nav-lock" aria-hidden="true">
                    <Lock size={13} />
                  </span>
                {/if}
                <span>
                  {item.label}
                  {#if item.writes}
                    <span class="nav-writes" title={$t('nav.writesHint')} aria-label={$t('nav.writesHint')}>
                      <PencilLine size={11} strokeWidth={2.2} />
                    </span>
                  {/if}
                </span>
                <small>{item.short}</small>
              </span>
            </a>
          {/each}
        {/if}
      {/each}
      {#if groups.length === 0}
        <p class="nav-empty">{$t('nav.noMatch', { query })}</p>
      {/if}
    </nav>
  </aside>

  <section class="workspace" id="main-content" tabindex="-1">
    <header class="topline">
      <div>
        <p class="eyebrow">Vortex Cloud</p>
        <h1>{activeLabel}</h1>
      </div>
      <div class="session-area">
        <div class="locale-switch" role="radiogroup" aria-label={$t('nav.languageLabel')}>
          {#each LOCALES as loc}
            <button
              type="button"
              role="radio"
              aria-checked={$locale === loc.value}
              class:active={$locale === loc.value}
              onclick={() => setLocale(loc.value)}
            >
              {loc.label}
            </button>
          {/each}
        </div>
        <div class="theme-switch" role="radiogroup" aria-label={$t('nav.themeLabel')}>
          {#each THEMES as themeOption}
            <button
              type="button"
              role="radio"
              aria-checked={$theme === themeOption.value}
              class:active={$theme === themeOption.value}
              onclick={() => setTheme(themeOption.value)}
            >
              {themeOption.label}
            </button>
          {/each}
        </div>
        <div class="status-pill ok">{email}</div>
        <button
          type="button"
          class="logout-btn"
          title={$t('mfa.title')}
          onclick={() => (mfaOpen = true)}
        >
          <span>{$identity?.mfaEnabled ? $t('mfa.short2faOn') : $t('mfa.short2faOff')}</span>
        </button>
        <button
          type="button"
          class="logout-btn"
          title={$t('common.signOut')}
          disabled={logoutBusy}
          aria-busy={logoutBusy}
          onclick={() => logout()}
        >
          <span>{logoutBusy ? $t('common.signingOut') : $t('common.signOut')}</span>
        </button>
      </div>
    </header>

    {#if mfaOpen}
      <MfaModal {logout} onclose={() => (mfaOpen = false)} />
    {/if}

    {@render children?.()}
  </section>

  <datalist id="reason-history">
    {#each $reasonSuggestions as suggestion}
      <option value={suggestion}></option>
    {/each}
  </datalist>
</main>

<style>
  .nav-kbd {
    flex: 0 0 auto;
    border: 1px solid var(--line);
    border-radius: 8px;
    padding: 1px 5px;
    color: var(--muted);
    font-size: 0.66rem;
    white-space: nowrap;
  }

  .nav-search {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 8px 10px;
    border: 1px solid var(--line);
    border-radius: 8px;
    background: var(--input-bg);
    color: var(--muted);
  }

  .nav-search input {
    flex: 1;
    min-width: 0;
    border: 0;
    background: transparent;
    color: var(--ink);
    outline: none;
    font-size: 0.86rem;
  }

  .nav-search input::placeholder {
    color: var(--muted);
  }

  .nav-search:focus-within {
    border-color: rgba(var(--accent-rgb), 0.58);
    box-shadow: 0 0 0 3px rgba(var(--accent-rgb), 0.12);
  }

  .nav-group-label {
    display: flex;
    align-items: center;
    gap: 5px;
    width: 100%;
    margin: 10px 0 2px;
    padding: 4px;
    background: none;
    border: none;
    color: var(--muted);
    text-transform: uppercase;
    font-size: 0.68rem;
    font-weight: 700;
    letter-spacing: 0.04em;
    cursor: pointer;
    border-radius: 8px;
  }

  .nav-group-label:hover {
    color: var(--ink);
    background: var(--surface-hover, var(--surface));
  }

  .sidebar nav > .nav-group-label:first-child {
    margin-top: 0;
  }

  .nav-group-count {
    margin-left: auto;
    padding: 0 5px;
    border-radius: 999px;
    background: var(--surface-strong);
    font-size: 0.62rem;
    line-height: 1.5;
  }

  /* Marks a section that can change server state -- the distinction the old "Act" group carried,
     kept now that grouping is by domain. */
  .nav-writes {
    display: inline-flex;
    vertical-align: -1px;
    margin-left: 4px;
    color: var(--accent);
    opacity: 0.75;
  }

  .nav-empty {
    margin: 8px 4px;
    color: var(--muted);
    font-size: 0.82rem;
  }

  .session-area {
    display: flex;
    align-items: center;
    justify-content: flex-end;
    flex-wrap: wrap;
    gap: 10px;
  }

  .theme-switch,
  .locale-switch {
    display: inline-flex;
    gap: 2px;
    padding: 3px;
    border: 1px solid var(--line-strong);
    border-radius: 8px;
    background: var(--surface-strong);
  }

  .theme-switch button,
  .locale-switch button {
    border: 0;
    border-radius: 8px;
    background: transparent;
    color: var(--muted);
    padding: 6px 11px;
    font-size: 12px;
    font-weight: 700;
    cursor: pointer;
  }

  .theme-switch button:hover,
  .locale-switch button:hover {
    color: var(--ink);
  }

  .theme-switch button.active,
  .locale-switch button.active {
    background: var(--surface-raised);
    color: var(--ink);
    box-shadow: inset 0 0 0 1px var(--line-strong);
  }

  .logout-btn {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    padding: 7px 12px;
    border-radius: 8px;
    border: 1px solid var(--line-strong);
    background: var(--surface-strong);
    color: inherit;
    cursor: pointer;
    font-size: 13px;
  }

  .logout-btn:hover {
    background: var(--surface-hover);
  }

  .logout-btn:disabled {
    opacity: 0.62;
    cursor: default;
  }

  .disabled {
    opacity: 0.5;
    cursor: not-allowed;
    pointer-events: none;
  }

  .disabled:hover {
    background: transparent;
    transform: none;
    border-color: transparent;
  }

  .nav-copy {
    display: grid;
    gap: 2px;
  }

  /* Off-screen until focused -- it is for keyboard and screen-reader users, and showing it to
     everyone else would be a permanent orphan link in the corner. */
  .skip-link {
    position: absolute;
    left: -9999px;
    top: 0;
    z-index: 100;
    padding: 10px 14px;
    border-radius: 0 0 9px 0;
    background: var(--surface-strong);
    color: var(--ink);
    border: 1px solid var(--line-strong);
  }

  .skip-link:focus-visible {
    left: 0;
  }

  .workspace:focus-visible {
    outline: none;
  }

  .nav-lock {
    margin-right: 2px;
    display: inline-flex;
    align-items: center;
    color: var(--warning);
  }
</style>
