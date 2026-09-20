/** One picker directory: where its rows come from and how they are drawn. */
export type Directory = {
  endpoint: string;
  row: string;
  /** Absent when the directory offers no ordering of its own. */
  sorts?: string[];
  filter?: string;
};

export const DIRECTORIES: Record<string, Directory> = {
  // The three that predate the reward-track filters, each with its own layout and controls.
  furniture: {
    endpoint: '/api/v1/directory/furniture',
    row: 'furniture',
    sorts: ['relevance', 'name', 'id', 'idDesc', 'sprite', 'logic'],
    filter: 'logic',
  },
  room: {
    endpoint: '/api/v1/directory/rooms',
    row: 'room',
    sorts: ['relevance', 'name', 'id', 'idDesc'],
  },
  user: {
    endpoint: '/api/v1/directory/players',
    row: 'player',
    sorts: ['relevance', 'name', 'id', 'idDesc'],
    filter: 'online',
  },

  // Everything a filter value could otherwise be typed from memory. Each replaces a code or an id
  // that saves cleanly when misspelled and then never matches anything.
  group: { endpoint: '/api/v1/directory/groups', row: 'plain' },
  habbicon: { endpoint: '/api/v1/directory/habbicons', row: 'plain' },
  collection: { endpoint: '/api/v1/directory/habbicon-collections', row: 'plain' },
  offer: { endpoint: '/api/v1/directory/catalog-offers', row: 'plain' },
  category: { endpoint: '/api/v1/directory/navigator-categories', row: 'plain' },
  badge: { endpoint: '/api/v1/directory/badges', row: 'plain' },
  petSpecies: { endpoint: '/api/v1/directory/pet-species', row: 'plain' },
  poll: { endpoint: '/api/v1/directory/polls', row: 'plain' },
  quiz: { endpoint: '/api/v1/directory/quizzes', row: 'plain' },
  campaign: { endpoint: '/api/v1/directory/quest-campaigns', row: 'plain' },
  voucher: { endpoint: '/api/v1/directory/vouchers', row: 'plain' },
  clubGift: { endpoint: '/api/v1/directory/club-gifts', row: 'plain' },
  nftProduct: { endpoint: '/api/v1/directory/nft-store', row: 'plain' },
  targetedOffer: { endpoint: '/api/v1/directory/targeted-offers', row: 'plain' },
  thread: { endpoint: '/api/v1/directory/forum-threads', row: 'plain' },
  effect: { endpoint: '/api/v1/directory/avatar-effects', row: 'plain' },
  placedItem: { endpoint: '/api/v1/directory/placed-furniture', row: 'plain' },

  // Its own row, because a garment has no name: figuredata numbers wearables and never labels them,
  // so the picture and the part type are the whole of what an operator has to recognise one by.
  figureSet: {
    endpoint: '/api/v1/directory/figure-sets',
    row: 'figureSet',
    filter: 'type',
  },
};

/**
 * One row a directory returns, as the shared picker sees it.
 *
 * Twenty endpoints answer this component and the server now names all five of their row shapes --
 * DirectoryRow, CodeDirectoryRow, PlayerDirectoryRow, RoomDirectoryRow, FurnitureDirectoryRow, all
 * in apiTypes. They are not one type and cannot be intersected into one: a table-backed directory
 * sends a numeric id and the two built from distinct codes send the code itself, so `id` is the one
 * field where the five genuinely disagree. This is the reading view over all of them -- id and name
 * always, and each layout extra optional because only the directory that draws it sends it.
 *
 * A caller that needs a numeric id coerces, because the picker cannot know which directory answered.
 */
export type PickerRow = {
  id: number | string;
  name: string;
  /** What a filter stores when this row is picked. Only the signal directories send one. */
  value?: string;
  /** plain */
  description?: string | null;
  /** player */
  avatarUrl?: string | null;
  online?: boolean;
  /** furniture */
  iconUrl?: string | null;
  spriteId?: number | null;
  logic?: string | null;
  type?: string | null;
  canTrade?: boolean;
  /** room */
  ownerName?: string | null;
  usersNow?: number | null;
  playersMax?: number | null;
  /** Whatever else the directory thought worth saying about it. */
  [field: string]: unknown;
};

/** The directory for a kind, or null. Null is a bug the picker reports rather than papers over. */
export function directoryFor(kind: string): Directory | null {
  return DIRECTORIES[kind] ?? null;
}
