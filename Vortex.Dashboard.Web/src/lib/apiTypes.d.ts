// Generated from the C# response contracts by ApiTypeScriptContractTests.
// Do not edit: change the records, run the test, copy what it writes.

export interface AchievementCategoryStats {
  category: string;
  achievements: number;
  levels: number;
  badgesAwarded: number;
}

export interface AchievementDetail {
  id: number;
  name: string;
  category: string;
  displayMethod: number;
  triggered: boolean;
  levelCount: number;
  completedPlayers: number;
  ladder: AchievementLadderRung[];
  levelDistribution: AchievementLevelCount[];
  topPlayers: AchievementTopPlayer[];
}

export interface AchievementLadderRung {
  id: number;
  level: number;
  badgeCode: string;
  badgeUrl: string | null;
  progressRequirement: number;
  rewardAmount: number;
  rewardType: number;
  rewardKind: string;
  scorePoints: number;
}

export interface AchievementLevel {
  level: number;
  badgeCode: string;
  badgeUrl: string | null;
  progressRequirement: number;
  rewardAmount: number;
  rewardType: number;
  rewardKind: string;
  scorePoints: number;
}

export interface AchievementLevelCount {
  level: number;
  players: number;
}

export interface AchievementListItem {
  id: number;
  name: string;
  category: string;
  displayMethod: number;
  triggered: boolean;
  levelCount: number;
  totalScore: number;
  creditsPayout: number;
  pointsPayout: number;
  finalRequirement: number;
  playersTracked: number;
  playersStarted: number;
  playersCompleted: number;
  badgesAwarded: number;
  highestLevelReached: number;
  badgeUrl: string | null;
  levels: AchievementLevel[];
}

export interface AchievementListResponse {
  count: number;
  categories: string[];
  items: AchievementListItem[];
}

export interface AchievementResolutions {
  offers: ResolutionOffer[];
  challenges: ResolutionChallenge[];
  totals: ResolutionTotals;
  truncated: boolean;
}

export interface AchievementScorePlayer {
  playerId: number;
  playerName: string | null;
  score: number;
  badges: number;
}

export interface AchievementStats {
  totals: AchievementStatsTotals;
  byCategory: AchievementCategoryStats[];
  untouched: UntouchedAchievement[];
  topPlayers: AchievementScorePlayer[];
  badgeImageTemplate: string | null;
}

export interface AchievementStatsTotals {
  totalAchievements: number;
  totalLevels: number;
  triggeredCount: number;
  untriggeredCount: number;
  badgesAwarded: number;
  playersWithProgress: number;
  maxScoreAvailable: number;
}

export interface AchievementTopPlayer {
  playerId: number;
  playerName: string | null;
  level: number;
  progress: number;
  updatedAt: string;
}

export interface ApiDomainGroup {
  domain: string;
  routeCount: number;
  methods: string[];
}

export interface ApiMethodUsage {
  method: string;
  count: number;
}

export interface ApiRouteCatalog {
  version: string;
  generatedAt: string;
  routes: ApiRouteDescriptor[];
  groups: ApiDomainGroup[];
  methodUsage: ApiMethodUsage[];
}

export interface ApiRouteDescriptor {
  domain: string;
  path: string;
  methods: string[];
  tags: string[];
  capabilities: string[];
  requiresAuth: boolean;
  displayName: string | null;
}

export interface ArticleCategoryOption {
  id: number;
  code: string;
  labels: string;
  sortOrder: number;
  enabled: boolean;
}

export interface ArticleDetail {
  id: number;
  slug: string;
  category: string;
  status: string;
  publishAt: string | null;
  pinned: boolean;
  author: string;
  translations: ArticleTranslation[];
}

export interface ArticleFormMeta {
  categories: ArticleCategoryOption[];
  languages: ArticleLanguageOption[];
  imageBase: string | null;
  imageDirectories: string[];
  blockTypes: string[];
}

export interface ArticleImage {
  path: string;
  thumb: string;
}

export interface ArticleImageBrowse {
  total: number;
  page: number;
  pageSize: number;
  count: number;
  items: ArticleImage[];
  error: string | null;
}

export interface ArticleLanguageOption {
  id: number;
  code: string;
  label: string;
  isDefault: boolean;
  enabled: boolean;
  sortOrder: number;
}

export interface ArticleListItem {
  id: number;
  slug: string;
  category: string;
  status: string;
  scheduled: boolean;
  publishAt: string | null;
  pinned: boolean;
  author: string;
  title: string;
  languages: string[];
}

export interface ArticleListResponse {
  total: number;
  page: number;
  pageSize: number;
  count: number;
  items: ArticleListItem[];
}

export interface ArticleTranslation {
  lang: string;
  title: string;
  summary: string;
  body: string;
  headerImage: string | null;
  thumbnail: string | null;
}

export interface AuditCategoryCount {
  category: string;
  count: number;
}

export interface AuditEntry {
  id: number;
  occurredAt: string;
  category: string;
  action: string;
  severity: string;
  result: string;
  actorPlayerId: number | null;
  actorName: string | null;
  targetPlayerId: number | null;
  targetName: string | null;
  roomId: number | null;
  itemId: number | null;
  ipHash: string | null;
  correlationId: string | null;
  data: string | null;
}

export interface AuditPage {
  count: number;
  page: number;
  limit: number;
  total: number;
  offset: number;
  items: AuditEntry[];
}

export interface AvatarBatch {
  items: AvatarBatchRow[];
}

export interface AvatarBatchRow {
  id: number;
  avatarUrl: string | null;
}

export interface BackupFile {
  fileName: string;
  sizeBytes: number;
  createdUtc: string;
}

export interface BackupList {
  configured: boolean;
  items: BackupFile[];
}

export interface BadgeCollector {
  playerId: number;
  playerName: string | null;
  badges: number;
}

export interface BadgeHolderCount {
  badgeCode: string;
  badgeUrl: string | null;
  holders: number;
  equipped: number;
}

export interface BotDetail {
  id: number;
  name: string;
  motto: string;
  figure: string;
  avatarUrl: string | null;
  gender: string;
  ownerId: number;
  ownerName: string | null;
  roomId: number | null;
  roomName: string | null;
  placed: boolean;
  x: number;
  y: number;
  z: number;
  rotation: number;
  skills: number[];
  skillNames: string[];
  phrases: string[];
  autoChat: boolean;
  chatDelaySeconds: number;
  mixSentences: boolean;
  wanders: boolean;
  dances: boolean;
  rawSkillsJson: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface BotGenderCount {
  gender: string;
  count: number;
}

export interface BotGrowthPoint {
  bucket: string;
  label: string;
  botsCreated: number;
}

export interface BotListItem {
  id: number;
  name: string;
  motto: string;
  figure: string;
  avatarUrl: string | null;
  gender: string;
  ownerId: number;
  ownerName: string | null;
  roomId: number | null;
  roomName: string | null;
  placed: boolean;
  x: number;
  y: number;
  z: number;
  rotation: number;
  skills: number[];
  skillNames: string[];
  phraseCount: number;
  autoChat: boolean;
  chatDelaySeconds: number;
  wanders: boolean;
  dances: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface BotListResponse {
  page: number;
  limit: number;
  offset: number;
  total: number;
  count: number;
  items: BotListItem[];
}

export interface BotOwnerCount {
  ownerId: number;
  ownerName: string | null;
  botCount: number;
}

export interface BotRoomCount {
  roomId: number;
  roomName: string | null;
  botCount: number;
}

export interface BotStats {
  window: ReportWindow;
  totals: BotTotals;
  byGender: BotGenderCount[];
  growth: BotGrowthPoint[];
  topOwners: BotOwnerCount[];
  topRooms: BotRoomCount[];
}

export interface BotTotals {
  totalBots: number;
  placedBots: number;
  inventoryBots: number;
  configuredBots: number;
  chattyBots: number;
  autoChatBots: number;
  wanderingBots: number;
  dancingBots: number;
  distinctOwners: number;
  roomsWithBots: number;
}

export interface BuildersClubTierRow {
  id: number;
  level: number;
  furniLimit: number;
}

export interface CapabilityGroup {
  area: string;
  capabilities: string[];
}

export interface CatalogCurrencyList {
  count: number;
  items: TargetedOfferCurrency[];
}

export interface CatalogIconTemplate {
  template: string | null;
}

export interface CatalogOfferDetail {
  id: number;
  offer: CatalogOfferView;
  products: CatalogProductRow[];
}

export interface CatalogOfferRow {
  id: number;
  localizationId: string;
  costCredits: number;
  costCurrency: number;
  currencyTypeId: number | null;
  currencyName: string | null;
  canGift: boolean;
  canBundle: boolean;
  clubLevel: number;
  discountPercent: number;
  visible: boolean;
  productCount: number;
  singleProduct: CatalogProductSummary | null;
}

export interface CatalogOfferSales {
  offerId: number;
  offerName: string;
  furniIconUrl: string | null;
  catalogType: string;
  purchaseCount: number;
  quantity: number;
  creditsSpent: number;
}

export interface CatalogOfferView {
  id: number;
  catalogPageEntityId: number;
  pageLocalization: string;
  catalogType: number;
  localizationId: string;
  costCredits: number;
  costCurrency: number;
  currencyTypeId: number | null;
  currencyName: string | null;
  canGift: boolean;
  canBundle: boolean;
  clubLevel: number;
  discountPercent: number;
  visible: boolean;
}

export interface CatalogPageDetail {
  id: number;
  catalogType: number;
  parentEntityId: number | null;
  parentLocalization: string | null;
  localization: string;
  name: string | null;
  icon: number;
  iconUrl: string | null;
  layout: string;
  imageData: string[] | null;
  textData: string[] | null;
  sortOrder: number;
  visible: boolean;
  offers: CatalogOfferRow[];
}

export interface CatalogPageList {
  catalogType: number;
  parentId: number | null;
  count: number;
  items: CatalogPageRow[];
}

export interface CatalogPageRow {
  id: number;
  parentEntityId: number | null;
  localization: string;
  name: string | null;
  icon: number;
  iconUrl: string | null;
  layout: string;
  sortOrder: number;
  visible: boolean;
  childCount: number;
  offerCount: number;
}

export interface CatalogProductRow {
  id: number;
  productType: number;
  productTypeLabel: string;
  furnitureDefinitionEntityId: number | null;
  furnitureName: string | null;
  furnitureSpriteId: number | null;
  furnitureIconUrl: string | null;
  extraParam: string | null;
  quantity: number;
  uniqueSize: number;
  uniqueRemaining: number;
  buildersClubEligible: boolean;
}

export interface CatalogProductSummary {
  id: number;
  productType: number;
  productTypeLabel: string;
  furnitureName: string | null;
  furnitureIconUrl: string | null;
  quantity: number;
  uniqueSize: number;
  uniqueRemaining: number;
  buildersClubEligible: boolean;
}

export interface CatalogPurchasePoint {
  bucket: string;
  label: string;
  purchaseCount: number;
  creditsSpent: number;
}

export interface CatalogPurchaseStats {
  window: ReportWindow;
  totals: CatalogPurchaseTotals;
  timeline: CatalogPurchasePoint[];
  topOffers: CatalogOfferSales[];
}

export interface CatalogPurchaseTotals {
  purchaseCount: number;
  totalCreditsSpent: number;
  totalQuantity: number;
}

export interface CfhAppealSnapshot {
  issueId: number;
  topicId: number;
  message: string;
  reporterPlayerId: number;
  reporterName: string;
  reportedPlayerId: number;
  reportedName: string;
  closedAt: string | null;
  closeReason: number | null;
  appealCreatedAt: string | null;
}

export interface CfhCloseReasonCount {
  reason: string;
  count: number;
}

export interface CfhIssueQueueEntrySnapshot {
  issueId: number;
  state: number;
  categoryId: number;
  issueAgeMs: number;
  priority: number;
  reporterUserId: number;
  reporterUserName: string;
  reportedUserId: number;
  reportedUserName: string;
  pickerUserId: number;
  pickerUserName: string;
  message: string;
}

export interface CfhReportedPlayer {
  playerId: number;
  playerName: string | null;
  reportCount: number;
}

export interface CfhStats {
  window: ReportWindow;
  totals: CfhTotals;
  timeline: CfhTimelinePoint[];
  byCloseReason: CfhCloseReasonCount[];
  topTopics: CfhTopicCount[];
  topReportedPlayers: CfhReportedPlayer[];
}

export interface CfhTicketCloseReason {
}

export interface CfhTicketState {
}

export interface CfhTimelinePoint {
  bucket: string;
  label: string;
  ticketsCreated: number;
}

export interface CfhTopicCount {
  topicId: number;
  topicName: string;
  count: number;
}

export interface CfhTotals {
  totalTickets: number;
  openCount: number;
  pickedCount: number;
  closedCount: number;
  sanctionedCount: number;
  sanctionRate: number;
  avgResolutionMinutes: number;
}

export interface ChatStyleRow {
  id: number;
  clientStyleId: number;
  owners: number;
}

export interface ChatlogEntry {
  id: number;
  createdAt: string;
  roomId: number;
  roomName: string | null;
  playerId: number;
  playerName: string | null;
  targetPlayerId: number | null;
  targetPlayerName: string | null;
  message: string;
}

export interface ChatlogFilters {
  q: string | null;
  player: number | null;
  room: number | null;
}

export interface ChatlogPage {
  count: number;
  page: number;
  limit: number;
  total: number;
  offset: number;
  window: ChatlogWindow;
  filters: ChatlogFilters;
  items: ChatlogEntry[];
}

export interface ChatlogWindow {
  since: string;
  until: string;
}

export interface ClubExpiringSubscription {
  playerId: number;
  playerName: string | null;
  type: string;
  level: number;
  totalMonths: number;
  expiresAt: string;
  remainingDays: number;
}

export interface ClubLifecyclePoint {
  bucket: string;
  label: string;
  purchases: number;
  renewals: number;
  expired: number;
}

export interface ClubLifecycleTotals {
  purchases: number;
  renewals: number;
  expired: number;
  renewalShare: number;
}

export interface ClubMonthsBreakdown {
  months: number;
  total: number;
  purchases: number;
  renewals: number;
  expired: number;
}

export interface ClubSubscriptionEventRow {
  occurredAt: string;
  action: string;
  actorPlayerId: number | null;
  actorPlayerName: string | null;
  months: number | null;
  totalMonths: number | null;
  creditCost: number | null;
  isRenewal: boolean | null;
  isVip: boolean | null;
}

export interface ClubSubscriptionLifecycle {
  totals: ClubLifecycleTotals;
  byMonths: ClubMonthsBreakdown[];
  recentEvents: ClubSubscriptionEventRow[];
  timeline: ClubLifecyclePoint[];
}

export interface ClubSubscriptionTotals {
  totalSubscriptions: number;
  activeSubscriptions: number;
  inactiveSubscriptions: number;
  expiringIn7Days: number;
  expiringIn30Days: number;
  activeRate: number;
}

export interface ClubSubscriptionTypeBreakdown {
  type: string;
  total: number;
  active: number;
  inactive: number;
  averageRemainingDays: number;
  averageTotalMonths: number;
}

export interface ClubSubscriptions {
  window: ModerationWindow;
  totals: ClubSubscriptionTotals;
  byType: ClubSubscriptionTypeBreakdown[];
  topExpiring: ClubExpiringSubscription[];
  lifecycle: ClubSubscriptionLifecycle;
}

export interface CodeDirectoryPage {
  count: number;
  total: number;
  offset: number;
  hasMore: boolean;
  items: CodeDirectoryRow[];
}

export interface CodeDirectoryRow {
  id: string;
  value: string;
  name: string;
  description: string | null;
}

export interface CommunityGoalLevelRow {
  id: number;
  levelNumber: number;
  scoreThreshold: number;
  rewardUserLimit: number;
  reached: boolean;
}

export interface CommunityGoalList {
  count: number;
  items: CommunityGoalRow[];
}

export interface CommunityGoalRow {
  id: number;
  code: string;
  campaignCode: string;
  scorePerQuest: number;
  enabled: boolean;
  endsAt: string | null;
  sortOrder: number;
  expired: boolean;
  isActive: boolean;
  totalScore: number;
  contributors: number;
  reachedLevel: number;
  levels: CommunityGoalLevelRow[];
}

export interface ConfigEntryDto {
  key: string;
  group: string;
  description: string;
  kind: string;
  defaultValue: string;
  currentValue: string | null;
  isOverridden: boolean;
}

export interface ConfigList {
  count: number;
  items: ConfigEntryDto[];
}

export interface ConsoleCommandInfo {
  name: string;
  usage: string;
  description: string;
  requiredCapability: string | null;
  allowed: boolean;
}

export interface CorrelationAuditRow {
  occurredAt: string;
  category: string;
  action: string;
  actorPlayerId: number | null;
  actorName: string | null;
}

export interface CorrelationItemRow {
  occurredAt: string;
  itemId: number;
  eventType: string;
}

export interface CorrelationLedgerRow {
  occurredAt: string;
  playerId: number;
  currency: string;
  delta: number;
  balanceAfter: number;
  activityPointType: number | null;
}

export interface CorrelationSearch {
  kind: "correlationId";
  term: string;
  page: number;
  limit: number;
  offset: number;
  audit: CorrelationAuditRow[];
  ledger: CorrelationLedgerRow[];
  items: CorrelationItemRow[];
}

export interface CurrencyTypeRow {
  id: number;
  name: string | null;
  currencyType: string;
  activityPointType: number | null;
  enabled: boolean;
  startingAmount: number;
  walletRows: number;
  totalHeld: number;
}

export interface DailyTaskList {
  count: number;
  items: DailyTaskRow[];
  questTypes: string[];
}

export interface DailyTaskRewardRow {
  id: number;
  productItemTypeId: number;
  rewardTypeId: string;
  extraParams: string;
  amount: number;
}

export interface DailyTaskRow {
  id: number;
  taskCode: string;
  questTypeCode: string;
  isBonus: boolean;
  imageVersion: string;
  catalogName: string;
  requiredRepeats: number;
  enabled: boolean;
  sortOrder: number;
  assigned: number;
  completed: number;
  claimed: number;
  completionRate: number;
  rewards: DailyTaskRewardRow[];
}

export interface DashboardInventory {
  generatedAt: string;
  groups: InventoryGroup[];
}

export interface DashboardOverview {
  status: string;
  health: InfrastructureHealthSnapshot;
  uptimeSeconds: number;
  managedMemoryMb: number;
  activeSessions: number;
  activeRooms: number;
  activeClubSubscribers: number;
  incidents: IncidentDetectionSnapshot;
  live: OverviewLive;
  auditLastHourByCategory: AuditCategoryCount[];
  totals: OverviewTotals;
}

export interface DirectoryPage {
  count: number;
  total: number;
  offset: number;
  hasMore: boolean;
  items: DirectoryRow[];
}

export interface DirectoryRow {
  id: number;
  value: string;
  name: string;
  description: string | null;
}

export type DirectorySearch = CorrelationSearch | IdSearch | UnknownSearch;

export interface EconomyCurrencyTotals {
  spend: number;
  earned: number;
  net: number;
  transactionCount: number;
}

export interface EconomyExtras {
  totals: EconomyExtrasTotals;
  ltdSeries: LtdSeriesRow[];
  rentableSpaces: RentableSpaceRow[];
  rentableTerms: RentableSpaceTermRow[];
  currencies: CurrencyTypeRow[];
  buildersClub: BuildersClubTierRow[];
}

export interface EconomyExtrasTotals {
  ltdSeries: number;
  runningSeries: number;
  rentableSpaces: number;
  rentedNow: number;
  rentableTerms: number;
  currencies: number;
  buildersClubTiers: number;
}

export interface EconomyLedgerEntry {
  id: number;
  occurredAt: string;
  playerId: number;
  playerName: string | null;
  currency: string;
  activityPointType: number | null;
  delta: number;
  balanceAfter: number;
  reason: string;
  refId: number | null;
  correlationId: string | null;
}

export interface EconomyLedgerPage {
  count: number;
  page: number;
  limit: number;
  total: number;
  offset: number;
  items: EconomyLedgerEntry[];
}

export interface EconomySpendCategory {
  currency: string;
  action: string;
  spend: number;
  transactionCount: number;
}

export interface EconomyTrendPoint {
  bucket: string;
  label: string;
  spend: number;
  earned: number;
  net: number;
  transactionCount: number;
}

export interface EconomyTrendSeries {
  currency: string;
  points: EconomyTrendPoint[];
}

export interface EconomyTrends {
  window: ReportWindow;
  currencies: string[];
  series: EconomyTrendSeries[];
  totals: Record<string, EconomyCurrencyTotals>;
  categories: EconomySpendCategory[];
}

export interface EffectOwnerCount {
  effectId: number;
  imageUrl: string | null;
  owners: number;
  activated: number;
  selected: number;
}

export interface FishingActivity {
  records: FishingRecordRow[];
  derbies: FishingDerbyRow[];
  anglers: number;
}

export interface FishingContent {
  zones: FishingZoneRow[];
  species: FishingSpeciesRow[];
  rodTiers: FishingRodTierRow[];
  levels: FishingLevelRow[];
}

export interface FishingDerbyRow {
  id: number;
  nameKey: string;
  startsAt: string;
  endsAt: string;
  entries: number;
}

export interface FishingLevelRow {
  id: number;
  level: number;
  xpThreshold: number;
}

export interface FishingRecordRow {
  id: number;
  playerId: number;
  playerName: string | null;
  speciesId: number;
  speciesNameKey: string | null;
  bestWeight: number;
  caughtCount: number;
  bestAt: string;
}

export interface FishingRodTierRow {
  id: number;
  quality: number;
  xpThreshold: number;
  nameKey: string;
  handItemId: number;
  catchMultiplier: number;
  goldenMultiplier: number;
  hookHavocChance: number;
}

export interface FishingSpeciesRow {
  id: number;
  zoneId: number;
  nameKey: string;
  requiredLevel: number;
  rarityStars: number;
  catchRate: number;
  catchRatePercent: number;
  rarityWeight: number;
  drawSharePercent: number;
  minWeight: number;
  maxWeight: number;
  xpReward: number;
  goldenXpBonus: number;
  currencyReward: number;
  activeHours: number;
  activeWeekdays: number;
  activeSeasons: number;
  allHours: boolean;
  allWeekdays: boolean;
}

export interface FishingZoneRow {
  id: number;
  nameKey: string;
  furniClass: string;
  furniIconUrl: string | null;
  requiredLevel: number;
  minCatches: number;
  maxCatches: number;
  speciesCount: number;
}

export interface ForumGroupRanking {
  groupId: number;
  groupName: string | null;
  badgeUrl: string | null;
  threads: number;
  postCount: number;
  lastPostAt: string | null;
}

export interface ForumStateCount {
  state: string;
  count: number;
}

export interface ForumThreadSummary {
  id: number;
  groupId: number;
  groupName: string | null;
  badgeUrl: string | null;
  subject: string;
  state: string;
  isPinned: boolean;
  postCount: number;
  lastPostAt: string | null;
  createdAt: string;
  authorId: number;
  authorName: string | null;
}

export interface FurnitureDefinitionPage {
  page: number;
  limit: number;
  offset: number;
  total: number;
  count: number;
  items: FurnitureDefinitionRow[];
}

export interface FurnitureDefinitionRow {
  id: number;
  spriteId: number;
  name: string;
  productType: number;
  productTypeLabel: string;
  furniCategory: number;
  furniCategoryLabel: string;
  logic: string;
  totalStates: number;
  width: number;
  length: number;
  stackHeight: number;
  canStack: boolean;
  canWalk: boolean;
  canSit: boolean;
  canLay: boolean;
  canRecycle: boolean;
  canTrade: boolean;
  canGroup: boolean;
  canSell: boolean;
  usagePolicy: number;
  usagePolicyLabel: string;
  extraData: string | null;
  stuffDataType: number;
  stuffDataTypeLabel: string;
  vendingIds: string | null;
  iconUrl: string | null;
}

export interface FurnitureDirectoryPage {
  count: number;
  total: number;
  offset: number;
  hasMore: boolean;
  items: FurnitureDirectoryRow[];
}

export interface FurnitureDirectoryRow {
  id: number;
  spriteId: number;
  name: string;
  logic: string;
  type: string;
  category: string;
  width: number;
  length: number;
  canTrade: boolean;
  canSell: boolean;
  iconUrl: string | null;
}

export interface GamedataEntry {
  key: string | null;
  value: string | null;
  kind: string | null;
  index: number | null;
  id: string | null;
  classname: string | null;
  name: string | null;
  description: string | null;
  category: string | null;
  xdim: string | null;
  ydim: string | null;
  iconUrl: string | null;
  code: string | null;
}

export interface GamedataEntryPage {
  file: string;
  error: string | null;
  modifiedUtc: string | null;
  total: number;
  page: number;
  pageSize: number;
  entries: GamedataEntry[];
}

export interface GamedataFileList {
  available: boolean;
  files: GamedataFileRow[];
}

export interface GamedataFileRow {
  file: string;
  name: string;
  localised: boolean;
  entries: number;
  parses: boolean;
  modifiedUtc: string | null;
  categories: string[];
}

export interface GamedataLanguageList {
  available: boolean;
  modifiedUtc: string | null;
  languages: GamedataLanguageRow[];
}

export interface GamedataLanguageRow {
  id: string;
  code: string;
  name: string;
  url: string;
  hasFile: boolean;
  command: string;
}

export interface GiftWrappingList {
  wrappingEnabled: boolean;
  items: GiftWrappingRow[];
}

export interface GiftWrappingRow {
  id: number;
  kind: string;
  furnitureName: string | null;
  furnitureIconUrl: string | null;
  spriteId: number | null;
  artIndex: number | null;
  isDefault: boolean;
  sortOrder: number;
}

export interface GroupActivityEvent {
  occurredAt: string;
  action: string;
  actorPlayerId: number | null;
  actorPlayerName: string | null;
  result: string;
  data: string | null;
}

export interface GroupForumRanking {
  groupId: number;
  name: string;
  threadCount: number;
  postCount: number;
}

export interface GroupGrowthPoint {
  bucket: string;
  label: string;
  groupsCreated: number;
}

export interface GroupMemberRanking {
  groupId: number;
  name: string;
  badge: string;
  badgeUrl: string | null;
  ownerId: number;
  ownerName: string;
  memberCount: number;
  roomId: number;
}

export interface GroupStats {
  window: ReportWindow;
  totals: GroupTotals;
  growth: GroupGrowthPoint[];
  topGroupsByMembers: GroupMemberRanking[];
  topGroupsByForumActivity: GroupForumRanking[];
  recentActivity: GroupActivityEvent[];
}

export interface GroupTotals {
  totalGroups: number;
  totalMembers: number;
  totalThreads: number;
  totalPosts: number;
  avgMembersPerGroup: number;
}

export interface GuildBanRow {
  playerId: number;
  playerName: string | null;
  blockedById: number;
  blockedByName: string | null;
  blockedAt: string;
}

export interface GuildDirectoryPage {
  page: number;
  limit: number;
  offset: number;
  total: number;
  count: number;
  items: GuildDirectoryRow[];
}

export interface GuildDirectoryRow {
  id: number;
  name: string;
  badgeUrl: string | null;
  type: string;
  ownerId: number;
  ownerName: string | null;
  roomId: number;
  roomName: string | null;
  members: number;
  pendingRequests: number;
  bans: number;
  threads: number;
  createdAt: string;
}

export interface GuildIdentity {
  id: number;
  name: string;
  description: string | null;
  badgeUrl: string | null;
  type: string;
  ownerId: number;
  ownerName: string | null;
  roomId: number;
  roomName: string | null;
  forumEnabled: boolean;
  memberCount: number;
  createdAt: string;
}

export interface GuildMemberRow {
  playerId: number;
  playerName: string | null;
  rank: string;
  isOwner: boolean;
  joinedAt: string;
}

export interface GuildModeration {
  guild: GuildIdentity;
  members: GuildMemberRow[];
  pendingRequests: GuildRequestRow[];
  bans: GuildBanRow[];
  threads: GuildThreadRow[];
}

export interface GuildPostRow {
  id: number;
  threadId: number;
  message: string;
  state: string;
  deleted: boolean;
  authorId: number;
  authorName: string | null;
  adminId: number | null;
  adminName: string | null;
  createdAt: string;
  adminOperationAt: string | null;
}

export interface GuildRequestRow {
  playerId: number;
  playerName: string | null;
  requestedAt: string;
}

export interface GuildThreadDetail {
  guildId: number;
  guildName: string;
  thread: GuildThreadRow;
  posts: GuildPostRow[];
}

export interface GuildThreadRow {
  id: number;
  subject: string;
  state: string;
  isPinned: boolean;
  postCount: number;
  authorId: number;
  authorName: string | null;
  createdAt: string;
  lastPostAt: string | null;
}

export interface HandItemList {
  count: number;
  consumableCount: number;
  imageTemplate: string | null;
  items: HandItemRow[];
}

export interface HandItemRow {
  id: number;
  handItemId: number;
  name: string;
  nutrition: number;
  thirst: number;
  consumable: boolean;
  imageUrl: string | null;
}

export interface HealthComponentSnapshot {
  name: string;
  status: string;
  detail: string;
  latencyMs: number | null;
}

export interface HotelViewArgument {
  name: string;
  kind: string;
  optional: boolean;
}

export interface HotelViewBackground {
  layer: string;
  uri: string;
  visible: boolean;
}

export interface HotelViewCampaign {
  code: string;
  widget: string;
  elements: HotelViewElement[];
  layout: HotelViewLayoutValue[];
  usedBy: number[];
}

export interface HotelViewCommon {
  textColor: string;
  etchingColor: string;
  etchingPosition: string;
  leftPaneWidth: string;
  rightPaneWidth: string;
  layoutXml: string;
  roomCategory: string;
  rightPaneDimmerHidden: boolean;
  sceneSchedule: HotelViewScheduleEntry[];
}

export interface HotelViewConfig {
  available: boolean;
  error: string | null;
  modifiedUtc: string | null;
  textsModifiedUtc: string | null;
  common: HotelViewCommon;
  slots: HotelViewSlot[];
  campaigns: HotelViewCampaign[];
  scenes: HotelViewScene[];
  extras: HotelViewExtra[];
  placeholders: Record<string, string>;
  vocabulary: HotelViewVocabulary;
}

export interface HotelViewElement {
  type: string;
  args: string[];
  text: string | null;
}

export interface HotelViewElementType {
  type: string;
  arguments: HotelViewArgument[];
  verified: boolean;
}

export interface HotelViewExtra {
  key: string;
  value: string;
  read: boolean;
}

export interface HotelViewLayoutValue {
  key: string;
  value: string;
}

export interface HotelViewMotionType {
  motion: string;
  assetPrefix: string;
  fields: HotelViewArgument[];
}

export interface HotelViewObject {
  index: number;
  asset: string;
  motion: string;
  fields: string[];
}

export interface HotelViewScene {
  code: string;
  layers: HotelViewBackground[];
  objects: HotelViewObject[];
}

export interface HotelViewScheduleEntry {
  startsAt: string;
  code: string;
}

export interface HotelViewSlot {
  number: number;
  widget: string;
  schedule: HotelViewScheduleEntry[];
  elements: HotelViewElement[];
  layout: HotelViewLayoutValue[];
  separator: boolean;
  separatorTitle: string;
  ignore: boolean;
}

export interface HotelViewSlotShape {
  number: number;
  column: string;
  wide: boolean;
  canSeparate: boolean;
  canIgnore: boolean;
}

export interface HotelViewVocabulary {
  widgetTypes: string[];
  elementTypes: HotelViewElementType[];
  layoutKeys: string[];
  backgroundLayers: string[];
  motionTypes: HotelViewMotionType[];
  slotShapes: HotelViewSlotShape[];
  maxObjects: number;
}

export interface IdSearch {
  kind: "id";
  term: string;
  page: number;
  limit: number;
  offset: number;
  asActor: PlayerAuditRow[];
  playerProfile: PlayerProfile | null;
  ledger: PlayerLedgerRow[];
  itemHistory: PlayerItemRow[];
  chats: PlayerChatRow[];
}

export interface IncidentDetectionSnapshot {
  overallSeverity: string;
  signals: IncidentSignal[];
  topErrorGroups: TopErrorGroupSnapshot[];
  errorSpikesPerMinute: number;
  loginFailedSpikesPerMinute: number;
  generatedAt: string;
}

export interface IncidentSignal {
  code: string;
  severity: string;
  title: string;
  summary: string;
  observed: number;
  threshold: number;
  detectedAt: string;
}

export interface InfrastructureHealthSnapshot {
  overall: string;
  database: HealthComponentSnapshot;
  orleans: HealthComponentSnapshot;
  runtime: RuntimeHealthSnapshot;
  orleansCluster: OrleansClusterSnapshot;
}

export interface InventoryGroup {
  key: string;
  rows: InventoryRow[];
}

export interface InventoryRow {
  key: string;
  count: number;
  route: string | null;
  empty: boolean;
}

export interface ItemAnomaly {
  itemId: number;
  kind: string;
  detail: string;
  occurrences: number;
  firstSeen: string;
  lastSeen: string;
  definitionId: number | null;
  definitionName: string | null;
  ownerPlayerId: number | null;
  ownerName: string | null;
}

export interface ItemAnomalyScan {
  since: string;
  until: string;
  itemsScanned: number;
  count: number;
  items: ItemAnomaly[];
}

export interface ItemEventRow {
  id: number;
  occurredAt: string;
  eventType: string;
  actorPlayerId: number | null;
  actorPlayerName: string | null;
  fromOwnerId: number | null;
  fromOwnerName: string | null;
  toOwnerId: number | null;
  toOwnerName: string | null;
  roomId: number | null;
  roomName: string | null;
  correlationId: string | null;
  data: string | null;
}

export interface ItemProfile {
  itemId: number;
  snapshot: ItemSnapshot | null;
  page: number;
  limit: number;
  total: number;
  offset: number;
  count: number;
  history: ItemEventRow[];
}

export interface ItemSnapshot {
  id: number;
  definitionId: number | null;
  definitionName: string | null;
  furniIconUrl: string | null;
  ownerPlayerId: number | null;
  ownerName: string | null;
  roomId: number | null;
  roomName: string | null;
  roomX: number | null;
  roomY: number | null;
  roomZ: number;
  extraData: string | null;
  updatedAt: string;
}

export interface LiveAbuserSnapshot {
  playerId: number;
  packetsPerMinute: number;
}

export interface LivePacketOperationSnapshot {
  operation: string;
  packetsPerMinute: number;
}

export interface LiveRoomSnapshot {
  roomId: number;
  packetsPerMinute: number;
}

export interface LtdRaffleResultCount {
  result: string;
  count: number;
}

export interface LtdSeriesRow {
  id: number;
  productId: number;
  productName: string | null;
  iconUrl: string | null;
  totalQuantity: number;
  remainingQuantity: number;
  sold: number;
  costCredits: number;
  raffleWindowSeconds: number;
  isActive: boolean;
  hasRaffleFinished: boolean;
  startsAt: string | null;
  endsAt: string | null;
  running: boolean;
  pendingEntries: number;
  entriesByResult: LtdRaffleResultCount[];
}

export interface MarketplaceSalePoint {
  bucket: string;
  label: string;
  sales: number;
  volume: number;
}

export interface MarketplaceSeller {
  sellerId: number;
  sellerName: string | null;
  sales: number;
  volume: number;
}

export interface MarketplaceSummary {
  window: ReportWindow;
  totals: MarketplaceTotals;
  timeline: MarketplaceSalePoint[];
  topSellers: MarketplaceSeller[];
}

export interface MarketplaceTotals {
  activeListings: number;
  soldCount: number;
  totalVolume: number;
  averagePrice: number;
}

export interface ModerationActionCount {
  action: string;
  count: number;
}

export interface ModerationActorCount {
  actorPlayerId: number;
  actorName: string | null;
  count: number;
}

export interface ModerationDistribution {
  byAction: ModerationActionCount[];
  byResult: ModerationResultCount[];
}

export interface ModerationResultCount {
  result: string;
  count: number;
}

export interface ModerationRoomCount {
  roomId: number;
  roomName: string | null;
  count: number;
}

export interface ModerationRow {
  id: number;
  occurredAt: string;
  action: string;
  result: string;
  actorPlayerId: number | null;
  actorName: string | null;
  targetPlayerId: number | null;
  targetName: string | null;
  roomId: number | null;
  roomName: string | null;
  durationSeconds: number | null;
  duration: string | null;
  reason: string | null;
  isRenewal: boolean;
  correlationId: string | null;
}

export interface ModerationStats {
  window: ModerationWindow;
  totals: ModerationTotals;
  distribution: ModerationDistribution;
  timeline: ModerationTimelinePoint[];
  topActors: ModerationActorCount[];
  topTargets: ModerationTargetCount[];
  topRooms: ModerationRoomCount[];
  rows: ModerationRow[];
}

export interface ModerationTargetCount {
  targetPlayerId: number;
  targetName: string | null;
  count: number;
}

export interface ModerationTimelinePoint {
  bucket: string;
  label: string;
  count: number;
}

export interface ModerationTotals {
  total: number;
  limit: number;
  page: number;
  offset: number;
  success: number;
  denied: number;
  failed: number;
  retentionRate: number;
  activeBans: number;
  inactiveBans: number;
  totalBans: number;
  renewalCount: number;
  averageDurationSeconds: number;
}

export interface ModerationWindow {
  since: string;
  until: string;
}

export interface NavigatorContextRow {
  id: number;
  searchCode: string;
  visible: boolean;
  queryType: number;
  queryTypeLabel: string;
  orderNum: number;
  knownCode: boolean;
  quickLinks: NavigatorQuickLinkRow[];
}

export interface NavigatorEmptyTab {
  id: number;
  searchCode: string;
}

export interface NavigatorEventCategoryRow {
  id: number;
  name: string;
  visible: boolean;
  activeAdCount: number;
}

export interface NavigatorFlatCategoryRow {
  id: number;
  name: string;
  visible: boolean;
  automatic: boolean;
  automaticCategory: string | null;
  globalCategory: string | null;
  staffOnly: boolean;
  minRank: number;
  orderNum: number;
  roomCount: number;
}

export interface NavigatorHealth {
  contextCount: number;
  quickLinkCount: number;
  flatCategoryCount: number;
  eventCategoryCount: number;
  missingTabs: string[];
  emptyTabs: NavigatorEmptyTab[];
  seeded: boolean;
}

export interface NavigatorPromotedCategoryRow {
  id: number;
  code: string;
  clientText: string;
  searchCode: string;
  filter: string;
  orderNum: number;
  visible: boolean;
  roomCount: number;
}

export interface NavigatorPromotedCodeOption {
  code: string;
  clientText: string;
  taken: boolean;
}

export interface NavigatorQueryTypeOption {
  value: number;
  label: string;
}

export interface NavigatorQuickLinkRow {
  id: number;
  searchCode: string;
  filter: string;
  localization: string;
  queryType: number;
  queryTypeLabel: string;
  orderNum: number;
  forceClosed: boolean;
  knownCode: boolean;
}

export interface NavigatorSearchCodeOption {
  code: string;
  queryType: number;
  queryTypeLabel: string;
  topLevel: boolean;
}

export interface NavigatorSetup {
  health: NavigatorHealth;
  contexts: NavigatorContextRow[];
  flatCategories: NavigatorFlatCategoryRow[];
  eventCategories: NavigatorEventCategoryRow[];
  promotedCategories: NavigatorPromotedCategoryRow[];
  searchCodes: NavigatorSearchCodeOption[];
  queryTypes: NavigatorQueryTypeOption[];
  promotedCodes: NavigatorPromotedCodeOption[];
}

export interface OrleansClusterSnapshot {
  status: string;
  detail: string;
  siloCount: number;
  activeSiloCount: number;
  silos: OrleansSiloSnapshot[];
}

export interface OrleansSiloSnapshot {
  address: string;
  status: string;
}

export interface OverviewLive {
  packetsPerSecond: number;
  errorsPerMinute: number;
  latencyP50Ms: number;
  latencyP95Ms: number;
  topAbusers: LiveAbuserSnapshot[];
  topRooms: LiveRoomSnapshot[];
}

export interface OverviewTotals {
  audit: number;
  ledger: number;
  items: number;
  performanceSamplesSinceStart: number;
  asOf: string;
}

export interface PacketStats {
  packetsPerSecond: number;
  errorsPerMinute: number;
  latencyP50Ms: number;
  latencyP95Ms: number;
  topOperations: LivePacketOperationSnapshot[];
  topFailedOperations: LivePacketOperationSnapshot[];
}

export interface PetGrowthPoint {
  bucket: string;
  label: string;
  petsCreated: number;
}

export interface PetOwnerCount {
  ownerId: number;
  ownerName: string | null;
  petCount: number;
}

export interface PetRaceCount {
  type: number;
  race: number;
  count: number;
}

export interface PetStats {
  window: ReportWindow;
  totals: PetTotals;
  byType: PetTypeCount[];
  byRace: PetRaceCount[];
  growth: PetGrowthPoint[];
  topOwners: PetOwnerCount[];
}

export interface PetTotals {
  totalPets: number;
  avgLevel: number;
}

export interface PetTypeCount {
  type: number;
  count: number;
}

export interface PlayerAuditRow {
  occurredAt: string;
  category: string;
  action: string;
  actorPlayerId: number | null;
  actorPlayerName: string | null;
  targetPlayerId: number | null;
  targetPlayerName: string | null;
  roomId: number | null;
  roomName: string | null;
  result: number;
  data: string | null;
}

export interface PlayerBadgeRow {
  id: number;
  badgeCode: string;
  badgeUrl: string | null;
  slotId: number | null;
  createdAt: string;
}

export interface PlayerChatRow {
  createdAt: string;
  roomId: number;
  roomName: string | null;
  message: string;
  targetPlayerId: number | null;
  targetPlayerName: string | null;
}

export interface PlayerChatStyleRow {
  id: number;
  clientStyleId: number;
}

export interface PlayerDirectoryPage {
  count: number;
  total: number;
  offset: number;
  hasMore: boolean;
  online: number;
  items: PlayerDirectoryRow[];
}

export interface PlayerDirectoryRow {
  id: number;
  name: string;
  avatarUrl: string | null;
  online: boolean;
}

export interface PlayerEffectRow {
  id: number;
  effectId: number;
  imageUrl: string | null;
  subType: number;
  totalDuration: number;
  activatedAt: string | null;
  isSelected: boolean;
}

export interface PlayerItemRow {
  occurredAt: string;
  eventType: string;
  itemId: number;
  roomId: number | null;
  roomName: string | null;
  actorPlayerId: number | null;
  actorPlayerName: string | null;
  fromOwnerId: number | null;
  fromOwnerName: string | null;
  toOwnerId: number | null;
  toOwnerName: string | null;
  correlationId: string | null;
  data: string | null;
}

export interface PlayerLedgerRow {
  occurredAt: string;
  currency: string;
  delta: number;
  balanceAfter: number;
  activityPointType: number | null;
  correlationId: string | null;
}

export interface PlayerOutfitRow {
  id: number;
  slotId: number;
  figure: string;
  gender: string;
}

export interface PlayerProfile {
  id: number;
  name: string;
  motto: string | null;
  figure: string;
  avatarUrl: string | null;
  online: boolean;
  createdAt: string;
  updatedAt: string;
  gender: string;
  perks: string;
  window: ProfileWindow;
  ownedRooms: ProfileRooms;
  wallets: ProfileWallet[];
  inventory: ProfileInventory;
  activity: ProfileActivity;
  timeline: ProfileTimeline;
}

export interface PlayerRewardDetail {
  playerId: number;
  playerName: string;
  avatarUrl: string | null;
  badges: PlayerBadgeRow[];
  effects: PlayerEffectRow[];
  chatStyles: PlayerChatStyleRow[];
  outfits: PlayerOutfitRow[];
}

export interface PlayerRewardStats {
  totals: PlayerRewardTotals;
  effectImageTemplate: string | null;
  badgeImageTemplate: string | null;
  topBadges: BadgeHolderCount[];
  topEffects: EffectOwnerCount[];
  chatStyles: ChatStyleRow[];
  topCollectors: BadgeCollector[];
}

export interface PlayerRewardTotals {
  totalBadges: number;
  equippedBadges: number;
  playersWithBadges: number;
  distinctBadgeCodes: number;
  totalEffects: number;
  activatedEffects: number;
  selectedEffects: number;
  chatStyleCount: number;
  wardrobeOutfits: number;
  wardrobeUsers: number;
}

export interface PollChoiceDetail {
  id: number;
  value: string;
  choiceText: string;
  choiceType: number;
  sortOrder: number;
}

export interface PollDetail {
  id: number;
  code: string;
  pollType: string;
  headline: string;
  summary: string;
  startMessage: string;
  endMessage: string;
  npsPoll: boolean;
  enabled: boolean;
  offerOnRoomEntry: boolean;
  roomId: number | null;
  roomName: string | null;
  sortOrder: number;
  questions: PollQuestionNode[];
}

export interface PollFreeTextAnswer {
  playerId: number;
  playerName: string | null;
  avatarUrl: string | null;
  answer: string;
  answeredAt: string;
}

export interface PollFunnel {
  offered: number;
  pending: number;
  started: number;
  completed: number;
  rejected: number;
  completionRate: number;
  rejectionRate: number;
}

export interface PollListItem {
  id: number;
  code: string;
  pollType: string;
  headline: string;
  summary: string;
  startMessage: string;
  endMessage: string;
  npsPoll: boolean;
  enabled: boolean;
  offerOnRoomEntry: boolean;
  roomId: number | null;
  roomName: string | null;
  roomMissing: boolean;
  sortOrder: number;
  rootQuestionCount: number;
  followUpCount: number;
  offeredCount: number;
  startedCount: number;
  completedCount: number;
  rejectedCount: number;
  completionRate: number;
  offerable: boolean;
}

export interface PollListResponse {
  count: number;
  items: PollListItem[];
}

export interface PollQuestionNode {
  id: number;
  sortOrder: number;
  questionType: number;
  questionTypeName: string;
  questionText: string;
  questionCategory: number;
  questionAnswerType: number;
  answerCount: number;
  choices: PollChoiceDetail[];
  children: PollQuestionNode[];
}

export interface PollQuestionResult {
  id: number;
  questionText: string;
  questionType: number;
  questionTypeName: string;
  isFollowUp: boolean;
  parentQuestionId: number | null;
  questionCategory: number;
  respondents: number;
  answerCount: number;
  tally: PollTallyEntry[];
  freeText: PollFreeTextAnswer[];
  freeTextTruncated: boolean;
}

export interface PollQuestionTypeOption {
  id: number;
  name: string;
  supported: boolean;
  takesChoices: boolean;
}

export interface PollQuestionTypeOptions {
  count: number;
  items: PollQuestionTypeOption[];
}

export interface PollResults {
  id: number;
  code: string;
  headline: string;
  npsPoll: boolean;
  funnel: PollFunnel;
  questions: PollQuestionResult[];
}

export interface PollTallyEntry {
  value: string;
  text: string;
  choiceType: number;
  count: number;
  share: number;
  retired: boolean;
}

export interface PrizeEntryDraws {
  entryId: number;
  draws: number;
}

export interface PrizePoolBindingList {
  count: number;
  items: PrizePoolBindingRow[];
}

export interface PrizePoolBindingRow {
  id: number;
  furnitureDefinitionId: number;
  pool: string;
  hitsRequired: number;
  enabled: boolean;
  furnitureName: string | null;
  furnitureLogic: string | null;
  furnitureIconUrl: string | null;
}

export interface PrizePoolContent {
  pools: PrizePoolList;
  entries: PrizePoolEntryList;
  totals: PrizePoolWeightTotal[];
  bindings: PrizePoolBindingList;
  productTypes: string[];
}

export interface PrizePoolDraws {
  pool: string;
  draws: number;
  entries: PrizeEntryDraws[];
  sources: PrizeSourceDraws[];
}

export interface PrizePoolEntryList {
  count: number;
  items: PrizePoolEntryRow[];
}

export interface PrizePoolEntryRow {
  id: number;
  poolId: number;
  pool: string;
  variant: string | null;
  productType: string;
  furnitureDefinitionId: number | null;
  extraParam: string | null;
  weight: number;
  enabled: boolean;
  furnitureName: string | null;
  furnitureIconUrl: string | null;
}

export interface PrizePoolList {
  count: number;
  items: PrizePoolRow[];
}

export interface PrizePoolRow {
  id: number;
  code: string;
  name: string;
  variants: string | null;
  notes: string | null;
  enabled: boolean;
}

export interface PrizePoolStats {
  days: number;
  totalDraws: number;
  pools: PrizePoolDraws[];
}

export interface PrizePoolWeightTotal {
  poolId: number;
  variant: string | null;
  totalWeight: number;
  entries: number;
}

export interface PrizeSourceDraws {
  source: string;
  draws: number;
}

export interface ProfileActivity {
  auditEvents: number;
  ledgerEvents: number;
  itemEvents: number;
}

export interface ProfileChatRow {
  createdAt: string;
  roomId: number;
  roomName: string | null;
  message: string;
}

export interface ProfileEntryRow {
  createdAt: string;
  roomId: number;
  roomName: string | null;
}

export interface ProfileInventory {
  total: number;
  latest: ProfileItemRow[];
}

export interface ProfileItemRow {
  itemId: number;
  definitionId: number | null;
  definitionName: string | null;
  furniIconUrl: string | null;
  roomEntityId: number | null;
  roomName: string | null;
  roomX: number | null;
  roomY: number | null;
}

export interface ProfileRoomRow {
  roomId: number;
  roomName: string;
  usersNow: number;
  playersMax: number;
  lastActive: string;
  model: string;
}

export interface ProfileRooms {
  total: number;
  latest: ProfileRoomRow[];
}

export interface ProfileTimeline {
  entries: ProfileEntryRow[];
  chats: ProfileChatRow[];
  items: PlayerItemRow[];
}

export interface ProfileWallet {
  currencyTypeEntityId: number;
  amount: number;
  currency: string;
  activityPointType: number | null;
}

export interface ProfileWindow {
  since: string;
  until: string;
}

export interface QuestCompletionCount {
  questId: number;
  name: string;
  completions: number;
}

export interface QuestCompletionPoint {
  bucket: string;
  label: string;
  completions: number;
}

export interface QuestDetail {
  id: number;
  campaignCode: string;
  chainCode: string;
  localizationCode: string;
  questType: string;
  targetType: string;
  targetValue: string;
  enabled: boolean;
  totalSteps: number;
  rewardType: number;
  rewardAmount: number;
  rewardKind: string;
  catalogPageName: string;
  imageVersion: string;
  imageUrl: string | null;
  sortOrder: number;
  easy: boolean;
  seasonal: boolean;
  seasonalSeconds: number;
  endsAt: string | null;
  expired: boolean;
  acceptedCount: number;
  completedCount: number;
}

export interface QuestList {
  count: number;
  campaigns: string[];
  items: QuestRow[];
}

export interface QuestRow {
  id: number;
  imageUrl: string | null;
  campaignCode: string;
  chainCode: string;
  localizationCode: string;
  questType: string;
  targetType: string;
  targetValue: string;
  enabled: boolean;
  totalSteps: number;
  rewardType: number;
  rewardAmount: number;
  rewardKind: string;
  sortOrder: number;
  easy: boolean;
  seasonal: boolean;
  seasonalSeconds: number;
  endsAt: string | null;
  expired: boolean;
  acceptedCount: number;
  completedCount: number;
}

export interface QuestStats {
  window: ReportWindow;
  totals: QuestTotals;
  timeline: QuestCompletionPoint[];
  topQuests: QuestCompletionCount[];
}

export interface QuestTotals {
  totalCompletions: number;
  totalAccepted: number;
  activePlayers: number;
}

export interface QuestTypeOption {
  name: string;
  wired: boolean;
}

export interface QuestTypeOptions {
  count: number;
  items: QuestTypeOption[];
}

export interface RentableSpaceAuditEntry {
  id: number;
  occurredAt: string;
  action: string;
  actorPlayerId: number | null;
  actorName: string | null;
  targetPlayerId: number | null;
  targetName: string | null;
  roomId: number | null;
  itemId: number | null;
  data: string | null;
  correlationId: string | null;
}

export interface RentableSpaceAuditPage {
  activeRentals: number;
  count: number;
  page: number;
  limit: number;
  total: number;
  offset: number;
  items: RentableSpaceAuditEntry[];
}

export interface RentableSpaceRow {
  id: number;
  furnitureId: number;
  furnitureName: string | null;
  iconUrl: string | null;
  renterId: number | null;
  renterName: string | null;
  rentedUntil: string | null;
  rented: boolean;
  hasTerms: boolean;
}

export interface RentableSpaceTermRow {
  id: number;
  furnitureEntityId: number;
  price: number;
  currencyTypeEntityId: number;
  rentDurationSeconds: number;
  requiresHc: boolean;
}

export interface ReportWindow {
  since: string;
  until: string;
  granularity: string;
}

export interface ResolutionChallenge {
  id: number;
  playerId: number;
  playerName: string | null;
  itemId: number;
  achievementId: number;
  achievementName: string | null;
  targetLevel: number;
  reachedLevel: number;
  startedAt: string;
  endsAt: string;
  completedAt: string | null;
  badgeCode: string | null;
  badgeUrl: string | null;
  state: string;
}

export interface ResolutionOffer {
  id: number;
  achievementId: number;
  achievementName: string | null;
  category: string | null;
  orphaned: boolean;
  levelCount: number;
  targetLevelOffset: number;
  sortOrder: number;
  enabled: boolean;
  taken: number;
  completed: number;
  live: number;
  expired: number;
  completionRate: number;
}

export interface ResolutionTotals {
  offers: number;
  enabledOffers: number;
  orphanedOffers: number;
  taken: number;
  completed: number;
  live: number;
  expired: number;
  completionRate: number;
  players: number;
}

export interface RoomDirectoryPage {
  count: number;
  total: number;
  offset: number;
  hasMore: boolean;
  items: RoomDirectoryRow[];
}

export interface RoomDirectoryRow {
  id: number;
  name: string;
  ownerName: string | null;
  usersNow: number;
  playersMax: number;
  lastActive: string;
}

export interface RoomOccupantSnapshot {
  playerId: number;
  name: string;
}

export interface RoomPerformanceSeriesStats {
  name: string;
  count: number;
  p50Ms: number;
  p95Ms: number;
  p99Ms: number;
  sumMs: number;
  shareOfTickPercent: number;
}

export interface RoomPerformanceSnapshot {
  windowSeconds: number;
  tick: RoomPerformanceSeriesStats;
  steps: RoomPerformanceSeriesStats[];
  directoryCalls: RoomPerformanceSeriesStats[];
}

export interface RoomSummaryDto {
  roomId: number;
  name: string;
  ownerId: number;
  ownerName: string;
  population: number;
  lastUpdatedUtc: string;
  raidIncidentActive: boolean;
}

export interface RoomTimeline {
  room: RoomTimelineHeader;
  page: number;
  limit: number;
  offset: number;
  count: number;
  total: number;
  totals: RoomTimelineTotals;
  timeline: RoomTimelineRow[];
}

export interface RoomTimelineHeader {
  roomId: number;
  name: string;
  description: string | null;
  roomOwnerId: number;
  roomOwnerName: string | null;
  ownerPlayerId: number;
  usersNow: number;
  playersMax: number;
  lastActive: string;
  modelName: string;
  staffPick: boolean;
}

export interface RoomTimelineRow {
  createdAt: string;
  eventType: string;
  playerId: number | null;
  playerName: string | null;
  message: string | null;
  targetPlayerId: number | null;
  targetPlayerName: string | null;
  itemId: number | null;
}

export interface RoomTimelineTotals {
  entries: number;
  chats: number;
  items: number;
}

export interface RunConsoleCommandResponse {
  ok: boolean;
  correlationId: string;
  message: string;
  output: string[];
}

export interface RuntimeHealthSnapshot {
  status: string;
  startedAtUtc: string;
  uptimeSeconds: number;
  machineName: string;
  environmentName: string;
  processId: number;
  frameworkDescription: string;
  osDescription: string;
  processorCount: number;
  workingSetMb: number;
  managedMemoryMb: number;
}

export interface SanctionPresetKindOption {
  value: number;
  label: string;
}

export interface SanctionPresetRow {
  id: number;
  kind: string;
  presetIndex: number;
  name: string;
  durationSeconds: number | null;
  message: string | null;
  permanent: boolean;
}

export interface SocialForums {
  threadsByState: ForumStateCount[];
  postsByState: ForumStateCount[];
  topGroups: ForumGroupRanking[];
  recentThreads: ForumThreadSummary[];
}

export interface SocialFriendedCount {
  playerId: number;
  playerName: string | null;
  friends: number;
}

export interface SocialSenderCount {
  playerId: number;
  playerName: string | null;
  messages: number;
}

export interface SocialStats {
  window: ReportWindow;
  totals: SocialTotals;
  timeline: SocialTimelinePoint[];
  topSenders: SocialSenderCount[];
  topFriended: SocialFriendedCount[];
  forums: SocialForums;
}

export interface SocialTimelinePoint {
  bucket: string;
  label: string;
  messages: number;
}

export interface SocialTotals {
  friendships: number;
  friendRows: number;
  playersWithFriends: number;
  pendingRequests: number;
  blockedPairs: number;
  ignoredPairs: number;
  totalMessages: number;
  undelivered: number;
  windowMessages: number;
  threads: number;
  posts: number;
}

export interface SongListItem {
  id: number;
  name: string;
  creator: string;
  lengthMs: number;
  lengthSeconds: number;
  officialSongId: string;
  data: string;
  diskCount: number;
  loadedInJukeboxes: number;
}

export interface SongListResponse {
  total: number;
  page: number;
  pageSize: number;
  items: SongListItem[];
}

export interface StaffAccountMatch {
  id: number;
  email: string;
  playerNames: string[];
  roleIds: number[];
}

export interface StaffAccountSearch {
  count: number;
  items: StaffAccountMatch[];
}

export interface StaffMember {
  id: number;
  email: string;
  createdAt: string;
  playerNames: string[];
  players: StaffPlayer[];
  roles: string[];
  roleIds: number[];
}

export interface StaffOverview {
  totals: StaffTotals;
  roles: StaffRole[];
  staff: StaffMember[];
  presets: SanctionPresetRow[];
  ungrantedCapabilities: string[];
  wildcardExists: boolean;
  allCapabilities: CapabilityGroup[];
  wildcard: string;
  presetKinds: SanctionPresetKindOption[];
}

export interface StaffPlayer {
  id: number;
  name: string;
  avatarUrl: string | null;
}

export interface StaffRole {
  id: number;
  key: string;
  name: string;
  capabilityCount: number;
  capabilities: string[];
  unknownCapabilities: string[];
  wildcard: boolean;
  holders: number;
}

export interface StaffTotals {
  roleCount: number;
  staffAccounts: number;
  declaredCapabilities: number;
  grantedCapabilities: number;
  ungrantedCapabilities: number;
  presetCount: number;
  activeBans: number;
}

export interface TargetedOfferCurrency {
  id: number;
  name: string | null;
  type: string;
  activityPointType: number | null;
}

export interface TargetedOfferDetail {
  id: number;
  identifier: string;
  offerType: number;
  title: string;
  description: string;
  imageUrl: string;
  iconImageUrl: string;
  productCode: string;
  priceInCredits: number;
  priceInActivityPoints: number;
  activityPointType: number;
  purchaseLimit: number;
  expiresAt: string | null;
  expired: boolean;
  active: boolean;
  sortOrder: number;
  buyerCount: number;
  totalPurchases: number;
  products: TargetedOfferProduct[];
}

export interface TargetedOfferFormMeta {
  imageTemplate: string | null;
  currencyTypes: TargetedOfferCurrency[];
}

export interface TargetedOfferImage {
  file: string;
  url: string;
  thumbUrl: string;
}

export interface TargetedOfferImageList {
  count: number;
  items: TargetedOfferImage[];
}

export interface TargetedOfferList {
  count: number;
  items: TargetedOfferRow[];
}

export interface TargetedOfferPoint {
  bucket: string;
  label: string;
  purchaseCount: number;
  creditsSpent: number;
}

export interface TargetedOfferProduct {
  id: number;
  productCode: string;
  furnitureDefinitionEntityId: number | null;
  furnitureName: string | null;
  furnitureIconUrl: string | null;
  quantity: number;
}

export interface TargetedOfferRow {
  id: number;
  identifier: string;
  offerType: number;
  title: string;
  imageUrl: string;
  iconImageUrl: string;
  productCode: string;
  priceInCredits: number;
  priceInActivityPoints: number;
  activityPointType: number;
  purchaseLimit: number;
  expiresAt: string | null;
  expired: boolean;
  active: boolean;
  sortOrder: number;
  productCount: number;
  buyerCount: number;
  totalPurchases: number;
}

export interface TargetedOfferSales {
  offerId: number;
  offerName: string;
  furniIconUrl: string | null;
  purchaseCount: number;
  quantity: number;
  creditsSpent: number;
  activityPointsSpent: number;
}

export interface TargetedOfferStats {
  window: ReportWindow;
  totals: TargetedOfferTotals;
  timeline: TargetedOfferPoint[];
  topOffers: TargetedOfferSales[];
}

export interface TargetedOfferTotals {
  purchaseCount: number;
  totalCreditsSpent: number;
  totalActivityPointsSpent: number;
  totalQuantity: number;
}

export interface TopErrorGroupSnapshot {
  fingerprint: string;
  source: string;
  operation: string;
  exceptionType: string;
  messageSignature: string;
  sampleMessage: string;
  totalOccurrences: number;
  firstSeenAt: string;
  lastSeenAt: string;
  lastActorPlayerId: number | null;
  lastRoomId: number | null;
  lastCorrelationId: string | null;
}

export interface UnknownSearch {
  kind: "unknown";
  term: string;
  hint: string;
}

export interface UntouchedAchievement {
  id: number;
  name: string;
  category: string;
  triggered: boolean;
  levels: number;
}

export interface VoucherSnapshot {
  exists: boolean;
  code: string;
  isActive: boolean;
  currencyType: number;
  activityPointType: number | null;
  amount: number;
  maxRedemptions: number | null;
  redemptionCount: number;
  expiresAt: string | null;
}

export interface WordFilterAction {
}

export interface WordFilterHitPage {
  page: number;
  limit: number;
  total: number;
  items: WordFilterHitRow[];
}

export interface WordFilterHitRow {
  id: number;
  playerId: number;
  playerName: string | null;
  avatarUrl: string | null;
  source: number;
  wordId: number;
  word: string | null;
  action: number;
  originalText: string;
  occurredAt: string;
}

export interface WordFilterMatch {
}

export interface WordFilterSource {
}

export interface WordFilterTestResult {
  text: string;
  refused: boolean;
  changed: boolean;
}

export interface WordFilterWordList {
  items: WordFilterWordRow[];
}

export interface WordFilterWordRow {
  id: number;
  word: string;
  action: number;
  match: number;
  replacement: string | null;
  createdBy: string | null;
  createdAt: string;
}

