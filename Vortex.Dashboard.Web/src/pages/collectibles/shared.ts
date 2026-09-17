// What every Collectibles tab needs and none of them owns.
//
// The page used to be one 1,947-line file holding eight tabs and eight editors. That is what hid
// the bug this split came out of: all eight submit buttons sat outside their own `<form>`, so not
// one editor on the page could save, and nothing said so. Forty lines apart in a short file, that
// is obvious; a thousand lines apart it is invisible.
//
// So each tab is now its own component with its table, its editor drawer and its picker together,
// and this module holds only what genuinely crosses them.

import type { createWriteOps } from '../../lib/writeOps';
import type { CollectiblesOverview } from '../../lib/apiTypes';

/** One choice of a fixed vocabulary a tab draws as a select. */
export type Choice<T> = { value: T; key: string };

/**
 * A form field is not the column it saves to: every number binds to an input that hands back a
 * string while it is being typed, and every date binds to a datetime-local, which speaks local time
 * with no zone. Hence `number | string` throughout, and the Number()/isoLocal() on each read.
 */
export type Num = number | string;

/** What the page hands every tab: the payload, the permission, and the write channel. */
export type TabProps = {
  data: CollectiblesOverview;
  canManage: boolean;
  ops: ReturnType<typeof createWriteOps>;
};

/**
 * A datetime-local input speaks local time with no zone, and the API takes instants. This is the
 * outbound half; a row loaded for editing has to come back the other way round.
 */
export const isoLocal = (date: Date) =>
  new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16);

/**
 * Only the statuses mean something, and only on this side: the client parses the field and never
 * reads it, so Draft is a collection the server withholds rather than one the client hides.
 */
export const STATUS_OPTIONS: Choice<number>[] = [
  { value: 0, key: 'collectibles.statusDraft' },
  { value: 1, key: 'collectibles.statusVisible' },
  { value: 2, key: 'collectibles.statusArchived' },
];

/**
 * The client's own list, from its rarity colour table. Anything outside it still renders, but in the
 * default grey — so typing a rarity of one's own invention is how an item ends up looking broken for
 * no visible reason.
 */
export const RARITY_OPTIONS = ['common', 'uncommon', 'rare', 'epic', 'legendary', 'legendary+'];

/**
 * The three the client knows. It switches on this exact string to pick the caption and the tile
 * colours and has no branch for anything else — an unknown one prints the word "null" above the
 * avatar, so the field is a list rather than a text box.
 */
export const COLLECTION_OPTIONS: Choice<string>[] = [
  { value: 'habbo:avatar', key: 'collectibles.collectionAvatar' },
  { value: 'habbo:clothes', key: 'collectibles.collectionClothes' },
  { value: 'habbo:avatar_genesis', key: 'collectibles.collectionGenesis' },
];
