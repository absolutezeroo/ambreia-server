// Central route table for the dashboard SPA. Each section declares the capability (or capabilities)
// required to view it; svelte-spa-router enforces it through a `conditions` guard and the nav uses
// the same rule so operators only ever see authorized tools. A failed guard raises the router's
// `conditionsFailed` event, which App.svelte turns into the /access-denied view.
//
// Pages are loaded on demand. `load` is a thunk returning a dynamic import, which Vite turns into one
// chunk per page: opening /overview no longer downloads the catalogue editor, the furni picker and
// the survey authoring tool along with it. Only the two pages that must render without a round trip
// are imported eagerly -- the overview (every session lands on it) and the access-denied fallback
// (it is what the router falls back TO, including when a chunk cannot be fetched).

import type { Identity } from './permissions';
import { wrap } from 'svelte-spa-router/wrap';
import { get } from 'svelte/store';
import { identity } from './session';
import { hasDashboardCapability } from './permissions';
import { ROUTE_PERMISSIONS } from './dashboardPermissions';

import OverviewPage from '../pages/OverviewPage.svelte';
import AccessDeniedPage from '../pages/AccessDeniedPage.svelte';
import RouteLoading from '../components/RouteLoading.svelte';

// Accent- and case-insensitive folding for every nav search (sidebar filter and Ctrl+K alike).
// An operator types "quete" and "peche"; the labels read "Quêtes" and "Pêche", and a plain
// `includes` on the raw strings finds neither.
/** One entry of the sidebar, and of the command palette. */
export type NavItem = {
  path: string;
  labelKey: string;
  shortKey?: string;
  group: string;
  keywords: string;
  caps: string | string[];
  /** The overview is bundled eagerly; everything else resolves its chunk on demand. */
  component?: unknown;
  load?: () => Promise<unknown>;
  writes?: boolean;
};

export const foldSearch = (value: string | null | undefined): string =>
  (value || '')
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLowerCase();

// Display + permission metadata for the navigation sidebar. `group` buckets items in the sidebar
// (see AppShell.svelte) and is the DOMAIN the page is about -- players, rooms, economy -- not the
// kind of tool it is. Grouping by kind (investigate / stats / act) scattered every domain across
// three places: the quest editor sat under "Act" while quest stats sat under "Stats", and finding
// anything meant knowing which bucket a page had been filed under rather than what it was about.
// `writes: true` marks a page that can change server state; the sidebar badges it, which is the
// distinction the old grouping was really carrying.
//
// No group may exceed ~8 entries. "Content" once held 14 -- quests, songs, fishing, collectibles,
// gamedata and the furni definitions in one bucket -- which is not a group, it is the pile of
// everything that did not fit elsewhere. Progression and Prizes were cut out of it; Social (2
// entries) folded into Players, because a two-item group is a header you have to open to learn it
// was not what you wanted.
//
// `keywords` is free text matched by the sidebar filter and by the command palette on top of the
// label and the short. It is what an operator TYPES, not what the page is called: "ban" must find
// moderation, "ducats" the wallet ledger, "jukebox" the song catalogue. Both languages go in the
// same string -- the search is not locale-aware and an English operator on a French dashboard is
// the normal case here. Accents are folded, so writing them here is optional.
//
// Order within a group is the nav order. Keep GROUP_ORDER in AppShell.svelte in sync.
// label/short are i18n keys (resolved via $t in AppShell.svelte), not display strings -- see
// lib/locales/{en,fr}.js's `nav` namespace, which must have a matching entry for every key here.
// `load` must be a literal `() => import('../pages/X.svelte')` -- Vite only code-splits imports it
// can see statically, so a computed path would silently fold every page back into one chunk.
export const NAV = [
  { path: '/overview', labelKey: 'nav.overview', shortKey: 'nav.overviewShort', group: 'Live', keywords: 'accueil home sante health statut status resume summary tableau de bord', caps: ROUTE_PERMISSIONS.overview, component: OverviewPage },
  { path: '/infrastructure', labelKey: 'nav.infrastructure', shortKey: 'nav.infrastructureShort', group: 'Live', keywords: 'orleans silo grain cluster runtime memoire memory uptime redemarrage restart', caps: ROUTE_PERMISSIONS.infrastructure, load: () => import('../pages/InfrastructurePage.svelte') },
  { path: '/performance', labelKey: 'nav.performance', shortKey: 'nav.performanceShort', group: 'Live', keywords: 'tick lag latence latency cpu lenteur slow salle room profil', caps: ROUTE_PERMISSIONS.performance, load: () => import('../pages/PerformancePage.svelte') },
  { path: '/console', labelKey: 'nav.console', shortKey: 'nav.consoleShort', group: 'Live', keywords: 'console commande command shell terminal log serveur server', caps: ROUTE_PERMISSIONS.console, load: () => import('../pages/ConsolePage.svelte'), writes: true },
  { path: '/packets', labelKey: 'nav.packets', shortKey: 'nav.packetsShort', group: 'Live', keywords: 'paquet packet protocole protocol header wire trafic traffic composer parser', caps: ROUTE_PERMISSIONS.packets, load: () => import('../pages/PacketsPage.svelte') },
  { path: '/incidents', labelKey: 'nav.incidents', shortKey: 'nav.incidentsShort', group: 'Live', keywords: 'incident erreur error exception crash alerte alert signal panne', caps: ROUTE_PERMISSIONS.incidents, load: () => import('../pages/IncidentsPage.svelte') },

  { path: '/investigation', labelKey: 'nav.investigation', shortKey: 'nav.investigationShort', group: 'Players', keywords: 'joueur player compte account profil profile fiche inventaire inventory ip email mot de passe password sanction ban', caps: ROUTE_PERMISSIONS.investigation, load: () => import('../pages/InvestigationPage.svelte'), writes: true },
  { path: '/subscriptions', labelKey: 'nav.subscriptions', shortKey: 'nav.subscriptionsShort', group: 'Players', keywords: 'hc habbo club builders bc abonnement subscription vip premium expiration', caps: ROUTE_PERMISSIONS.economy, load: () => import('../pages/SubscriptionsPage.svelte') },
  { path: '/social', labelKey: 'nav.social', shortKey: 'nav.socialShort', group: 'Players', keywords: 'ami friend amitie relation message prive forum thread discussion', caps: ROUTE_PERMISSIONS.social, load: () => import('../pages/SocialPage.svelte'), writes: true },
  { path: '/groups-stats', labelKey: 'nav.groupsStats', shortKey: 'nav.groupsStatsShort', group: 'Players', keywords: 'guilde guild groupe group forum badge membre population', caps: ROUTE_PERMISSIONS.groupsStats, load: () => import('../pages/GroupsStatsPage.svelte') },
  { path: '/guilds', labelKey: 'nav.guilds', shortKey: 'nav.guildsShort', group: 'Players', keywords: 'guilde guild groupe group forum post thread message membre member demande request ban banni supprimer delete moderation', caps: ROUTE_PERMISSIONS.guilds, load: () => import('../pages/GuildsPage.svelte'), writes: true },

  // Moderation is a job, not a property of a player: a CFH ticket belongs to the queue a moderator
  // works through, not to the account page of whoever happened to file it. The chat search sits here
  // for the same reason -- it is the evidence tool that ticket triage runs on. /investigation stays
  // under Players: it is the identity page, and its ban controls are one tab of many.
  { path: '/cfh', labelKey: 'nav.cfh', shortKey: 'nav.cfhShort', group: 'Moderation', keywords: 'cfh ticket signalement report call for help plainte aide appel file queue', caps: ROUTE_PERMISSIONS.cfh, load: () => import('../pages/CfhQueuePage.svelte'), writes: true },
  { path: '/chatlogs', labelKey: 'nav.chatlogs', shortKey: 'nav.chatlogsShort', group: 'Moderation', keywords: 'chat message parole dit said conversation historique log insulte preuve', caps: ROUTE_PERMISSIONS.chatlogs, load: () => import('../pages/ChatlogsPage.svelte') },
  { path: '/wordfilter', labelKey: 'nav.wordFilter', shortKey: 'nav.wordFilterShort', group: 'Moderation', keywords: 'filtre mot interdit bobba insulte censure filter word banned swear profanity journal', caps: ROUTE_PERMISSIONS.wordFilter, load: () => import('../pages/WordFilterPage.svelte'), writes: true },
  { path: '/item-anomalies', labelKey: 'nav.itemAnomalies', shortKey: 'nav.itemAnomaliesShort', group: 'Moderation', keywords: 'duplication dupe duplicat item objet furni anomalie anomaly fraude fraud triche cheat forensics', caps: ROUTE_PERMISSIONS.itemAnomalies, load: () => import('../pages/ItemAnomaliesPage.svelte') },
  { path: '/moderation', labelKey: 'nav.moderation', shortKey: 'nav.moderationShort', group: 'Moderation', keywords: 'ban bannir mute kick sanction blocage trading lock moderation punition', caps: ROUTE_PERMISSIONS.moderation, load: () => import('../pages/ModerationPage.svelte') },
  { path: '/cfh-stats', labelKey: 'nav.cfhStats', shortKey: 'nav.cfhStatsShort', group: 'Moderation', keywords: 'cfh ticket signalement volume stats moderation delai', caps: ROUTE_PERMISSIONS.cfhStats, load: () => import('../pages/CfhStatsPage.svelte') },
  // Staff sits with Moderation rather than System: it is the roster of the people who work this
  // group, and it is opened to answer "why can this moderator not close a ticket". The audit feed
  // stays under System -- it records what every operator did, moderators included.
  { path: '/staff', labelKey: 'nav.staff', shortKey: 'nav.staffShort', group: 'Moderation', keywords: 'staff role permission capability droit rang rank grade admin moderateur qui peut acces', caps: ROUTE_PERMISSIONS.staff, load: () => import('../pages/StaffPage.svelte'), writes: true },

  { path: '/rooms', labelKey: 'nav.rooms', shortKey: 'nav.roomsShort', group: 'Rooms', keywords: 'salle room appartement proprietaire owner historique timeline inspecteur visite', caps: ROUTE_PERMISSIONS.rooms, load: () => import('../pages/RoomsPage.svelte') },
  { path: '/room-control', labelKey: 'nav.roomControl', shortKey: 'nav.roomControlShort', group: 'Rooms', keywords: 'salle room active live fermer close vider kick expulser occupant', caps: ROUTE_PERMISSIONS.roomControl, load: () => import('../pages/RoomControlPage.svelte'), writes: true },
  { path: '/navigator-config', labelKey: 'nav.navigatorConfig', shortKey: 'nav.navigatorConfigShort', group: 'Rooms', keywords: 'navigateur navigator onglet tab categorie category recherche search promo', caps: ROUTE_PERMISSIONS.navigatorConfig, load: () => import('../pages/NavigatorConfigPage.svelte'), writes: true },
  { path: '/bots', labelKey: 'nav.bots', shortKey: 'nav.botsShort', group: 'Rooms', keywords: 'bot pnj npc serveur waiter effectif objet en main chatter', caps: ROUTE_PERMISSIONS.bots, load: () => import('../pages/BotsPage.svelte'), writes: true },
  { path: '/pets-stats', labelKey: 'nav.petsStats', shortKey: 'nav.petsStatsShort', group: 'Rooms', keywords: 'pet animal familier chien chat elevage breeding monsterplant nid', caps: ROUTE_PERMISSIONS.petsStats, load: () => import('../pages/PetsStatsPage.svelte') },
  { path: '/wired-stats', labelKey: 'nav.wiredStats', shortKey: 'nav.wiredStatsShort', group: 'Rooms', keywords: 'wired trigger declencheur effet effect condition cable placement automatisation', caps: ROUTE_PERMISSIONS.wiredStats, load: () => import('../pages/WiredStatsPage.svelte') },

  { path: '/economy', labelKey: 'nav.economy', shortKey: 'nav.economyShort', group: 'Economy', keywords: 'ducats diamants pixels monnaie currency wallet portefeuille credit transaction registre ledger solde', caps: ROUTE_PERMISSIONS.economy, load: () => import('../pages/EconomyPage.svelte') },
  { path: '/economy-trends', labelKey: 'nav.economyTrends', shortKey: 'nav.economyTrendsShort', group: 'Economy', keywords: 'depense spend tendance trend graphique chart devise currency inflation', caps: ROUTE_PERMISSIONS.economy, load: () => import('../pages/EconomyTrendsPage.svelte') },
  { path: '/catalog', labelKey: 'nav.catalog', shortKey: 'nav.catalogShort', group: 'Economy', keywords: 'catalogue catalog boutique shop page offre offer produit product prix price vendre furni', caps: ROUTE_PERMISSIONS.catalog, load: () => import('../pages/CatalogPage.svelte'), writes: true },
  { path: '/catalog-purchases', labelKey: 'nav.catalogPurchases', shortKey: 'nav.catalogPurchasesShort', group: 'Economy', keywords: 'achat purchase vente sale meilleur top seller revenu recette', caps: ROUTE_PERMISSIONS.catalogPurchases, load: () => import('../pages/CatalogPurchasesStatsPage.svelte') },
  { path: '/marketplace', labelKey: 'nav.marketplace', shortKey: 'nav.marketplaceShort', group: 'Economy', keywords: 'marche marketplace revente resale annonce offre echange trade bourse', caps: ROUTE_PERMISSIONS.economy, load: () => import('../pages/MarketplacePage.svelte') },
  { path: '/targeted-offers', labelKey: 'nav.targetedOffers', shortKey: 'nav.targetedOffersShort', group: 'Economy', keywords: 'offre ciblee targeted promo bundle lot campagne pack reduction', caps: ROUTE_PERMISSIONS.targetedOffers, load: () => import('../pages/TargetedOffersPage.svelte'), writes: true },
  { path: '/targeted-offers-stats', labelKey: 'nav.targetedOffersStats', shortKey: 'nav.targetedOffersStatsShort', group: 'Economy', keywords: 'offre ciblee targeted promo vente sale stats conversion', caps: ROUTE_PERMISSIONS.targetedOffersStats, load: () => import('../pages/TargetedOffersStatsPage.svelte') },
  { path: '/economy-extras', labelKey: 'nav.economyExtras', shortKey: 'nav.economyExtrasShort', group: 'Economy', keywords: 'ltd limite limited rare location rental espace space serie raffle type de devise currency types', caps: ROUTE_PERMISSIONS.economyExtras, load: () => import('../pages/EconomyExtrasPage.svelte'), writes: true },

  { path: '/quests', labelKey: 'nav.quests', shortKey: 'nav.questsShort', group: 'Progression', keywords: 'quete quest campagne mission objectif goal completion saison', caps: ROUTE_PERMISSIONS.quests, load: () => import('../pages/QuestsPage.svelte'), writes: true },
  { path: '/achievements', labelKey: 'nav.achievements', shortKey: 'nav.achievementsShort', group: 'Progression', keywords: 'succes achievement badge palier ladder niveau level progression trophee', caps: ROUTE_PERMISSIONS.achievements, load: () => import('../pages/AchievementsPage.svelte'), writes: true },
  // Same capability as /achievements: the statue is a view onto achievement progress, so a new one
  // would only mean granting it to every role before anyone could open the page.
  { path: '/achievement-resolutions', labelKey: 'nav.achievementResolutions', shortKey: 'nav.achievementResolutionsShort', group: 'Progression', keywords: 'resolution statue defi challenge nouvel an new year succes', caps: ROUTE_PERMISSIONS.achievements, load: () => import('../pages/AchievementResolutionsPage.svelte') },
  { path: '/player-rewards', labelKey: 'nav.playerRewards', shortKey: 'nav.playerRewardsShort', group: 'Progression', keywords: 'badge effet effect recompense reward accorder grant donner give retirer', caps: ROUTE_PERMISSIONS.playerRewards, load: () => import('../pages/PlayerRewardsPage.svelte'), writes: true },

  { path: '/mystery-box', labelKey: 'nav.mysteryBox', shortKey: 'nav.mysteryBoxShort', group: 'Prizes', keywords: 'coffre mystere mystery box cle key lot prize loot ouverture', caps: ROUTE_PERMISSIONS.mysteryBox, load: () => import('../pages/MysteryBoxPage.svelte'), writes: true },
  { path: '/prize-pools', labelKey: 'nav.prizePools', shortKey: 'nav.prizePoolsShort', group: 'Prizes', keywords: 'pool lot prize gain chance odds probabilite tirage roll rattachement', caps: ROUTE_PERMISSIONS.prizePools, load: () => import('../pages/PrizePoolsPage.svelte'), writes: true },
  { path: '/vouchers', labelKey: 'nav.vouchers', shortKey: 'nav.vouchersShort', group: 'Prizes', keywords: 'bon voucher code promo echangeable redeem cadeau gift coupon', caps: ROUTE_PERMISSIONS.vouchers, load: () => import('../pages/VouchersPage.svelte'), writes: true },

  { path: '/furniture-definitions', labelKey: 'nav.furnitureDefinitions', shortKey: 'nav.furnitureDefinitionsShort', group: 'Content', keywords: 'mobilier furni furniture meuble definition sprite logic logique physique classname interaction', caps: ROUTE_PERMISSIONS.furnitureDefinitions, load: () => import('../pages/FurnitureDefinitionsPage.svelte'), writes: true },
  { path: '/songs', labelKey: 'nav.songs', shortKey: 'nav.songsShort', group: 'Content', keywords: 'chanson song trax musique music jukebox disque disc audio piste', caps: ROUTE_PERMISSIONS.songs, load: () => import('../pages/SongsPage.svelte'), writes: true },
  { path: '/gift-wrappings', labelKey: 'nav.giftWrappings', shortKey: 'nav.giftWrappingsShort', group: 'Content', keywords: 'cadeau gift emballage wrapping paquet parcel boite box ruban ribbon present', caps: ROUTE_PERMISSIONS.giftWrappings, load: () => import('../pages/GiftWrappingsPage.svelte'), writes: true },
  { path: '/fishing', labelKey: 'nav.fishing', shortKey: 'nav.fishingShort', group: 'Content', keywords: 'peche fishing poisson fish zone espece species canne rod niveau appat', caps: ROUTE_PERMISSIONS.fishing, load: () => import('../pages/FishingPage.svelte'), writes: true },
  { path: '/polls', labelKey: 'nav.polls', shortKey: 'nav.pollsShort', group: 'Content', keywords: 'sondage poll questionnaire survey question reponse answer vote', caps: ROUTE_PERMISSIONS.polls, load: () => import('../pages/PollsPage.svelte'), writes: true },
  { path: '/articles', labelKey: 'nav.articles', shortKey: 'nav.articlesShort', group: 'Content', keywords: 'actualite news article site web presse billet post editeur redaction', caps: ROUTE_PERMISSIONS.articles, load: () => import('../pages/ArticlesPage.svelte'), writes: true },
  { path: '/gamedata', labelKey: 'nav.gamedata', shortKey: 'nav.gamedataShort', group: 'Content', keywords: 'gamedata client fichier file external variables texts furnidata productdata figuredata', caps: ROUTE_PERMISSIONS.gamedata, load: () => import('../pages/GamedataPage.svelte'), writes: true },
  { path: '/hotel-view', labelKey: 'nav.hotelView', shortKey: 'nav.hotelViewShort', group: 'Content', keywords: 'hotel view accueil landing reception promo campagne campaign slot widget fond background neige snowflake bgobject', caps: ROUTE_PERMISSIONS.hotelView, load: () => import('../pages/HotelViewPage.svelte'), writes: true },

  { path: '/audit', labelKey: 'nav.audit', shortKey: 'nav.auditShort', group: 'System', keywords: 'audit journal log securite security trace historique qui a fait quoi', caps: ROUTE_PERMISSIONS.audit, load: () => import('../pages/AuditPage.svelte') },
  { path: '/config', labelKey: 'nav.config', shortKey: 'nav.configShort', group: 'System', keywords: 'config reglage setting parametre serveur runtime a chaud variable option', caps: ROUTE_PERMISSIONS.config, load: () => import('../pages/ConfigPage.svelte'), writes: true },
  { path: '/api-explorer', labelKey: 'nav.apiExplorer', shortKey: 'nav.apiExplorerShort', group: 'System', keywords: 'api route endpoint contrat swagger explorer requete http', caps: ROUTE_PERMISSIONS.apiExplorer, load: () => import('../pages/ApiExplorerPage.svelte') },
];

const canSee = (caps: string | string[]) => () => hasDashboardCapability(get(identity), caps);

// Pass the identity explicitly (e.g. from a reactive `$identity`) to recompute on changes; falls
// back to the current store value for non-reactive callers such as the router guards.
export function hasRouteAccess(item: NavItem, who: Identity | null = get(identity)): boolean {
  return hasDashboardCapability(who, item.caps);
}

// A chunk that 404s means the operator's tab predates a redeploy: the index they booted from points
// at hashed filenames the server no longer has (the build writes with emptyOutDir). One reload picks
// up the new index; the sessionStorage flag stops that from becoming a reload loop if the chunk is
// genuinely broken, in which case the router falls through to the access-denied view.
const RELOADED_KEY = 'vortex.dashboard.chunkReload';

async function loadPage(item: NavItem) {
  try {
    const module = await item.load!();
    sessionStorage.removeItem(RELOADED_KEY);
    return module;
  } catch (err) {
    if (sessionStorage.getItem(RELOADED_KEY) !== '1') {
      sessionStorage.setItem(RELOADED_KEY, '1');
      window.location.reload();
      // Never resolves -- the reload is already under way and rendering anything would flash.
      return new Promise(() => {});
    }

    console.error(`Failed to load page chunk for ${item.path}`, err);
    return AccessDeniedPage;
  }
}

// svelte-spa-router route table. The empty/root hash redirects to the overview entry point.
// `wrap` is typed for Svelte 4 component constructors; Svelte 5 components are functions, which
// the router accepts at runtime and its types do not describe yet.
export const routes: Record<string, unknown> = {};

for (const item of NAV) {
  routes[item.path] = wrap({
    // The overview is bundled eagerly; every other entry resolves its chunk on first navigation.
    ...(item.component
      ? { component: item.component as never }
      : {
          asyncComponent: () => loadPage(item) as never,
          loadingComponent: RouteLoading as never,
        }),
    conditions: [canSee(item.caps)],
    userData: { route: item.path },
  });
}

// Root hash normalises to the overview entry point (App replaces '/' with '/overview' on boot, but
// guarding it here keeps the redirect honest if a user lands on '#/' directly).
routes['/'] = wrap({
  component: OverviewPage as never,
  conditions: [canSee(ROUTE_PERMISSIONS.overview)],
  userData: { route: '/overview' },
});
routes['/access-denied'] = AccessDeniedPage;
routes['*'] = AccessDeniedPage;
