// Wire-level int values for Vortex.Primitives.Furniture.Enums.ProductType /
// Vortex.Primitives.Rooms.Enums.FurnitureUsageType / Vortex.Primitives.Furniture.StuffData.StuffDataType
// and the FurnitureCategory column on furniture_definitions. Kept as plain int values (not strings)
// because the dashboard API deserializes enums as numbers (System.Text.Json default), matching how
// CreateCatalogProductRequest.ProductType etc. are already sent elsewhere.

export const PRODUCT_TYPES = [
  { value: 0, label: 'Floor' },
  { value: 1, label: 'Wall' },
  { value: 2, label: 'Effect' },
  { value: 3, label: 'Badge' },
  { value: 4, label: 'Robot' },
  { value: 5, label: 'HabboClub' },
  { value: 6, label: 'Pet' },
  // Extra param is the id in the client's own chat-style library. Only 1000 and up can be granted:
  // below that the client gates the bubble itself and never asks the server about it.
  { value: 7, label: 'Chat style' },
];

export const FURNITURE_CATEGORIES = [
  { value: 1, label: 'Default' },
  { value: 2, label: 'Wall paper' },
  { value: 3, label: 'Floor' },
  { value: 4, label: 'Landscape' },
  { value: 5, label: 'Post it' },
  { value: 6, label: 'Poster' },
  { value: 7, label: 'Sound set' },
  { value: 8, label: 'Trax song' },
  { value: 9, label: 'Present' },
  { value: 10, label: 'Ecotron box' },
  { value: 11, label: 'Trophy' },
  { value: 12, label: 'Credit furni' },
  { value: 13, label: 'Pet shampoo' },
  { value: 14, label: 'Pet custom part' },
  { value: 15, label: 'Pet custom part shampoo' },
  { value: 16, label: 'Pet saddle' },
  { value: 17, label: 'Guild furni' },
  { value: 18, label: 'Game furni' },
  { value: 23, label: 'Figure purchasable set' },
];

export const USAGE_POLICIES = [
  { value: 0, label: 'Nobody' },
  { value: 1, label: 'Controller' },
  { value: 2, label: 'Everybody' },
];

// The real, complete set of registered furniture "logic" keys -- there is no enum for this column
// (it's a free string matched at runtime against every [RoomObjectLogic("...")]-attributed class
// found by assembly scanning, see RoomObjectLogicFeatureProcessor/RoomObjectLogicProvider). Typing
// one from memory means guessing; this list is grepped directly from those attributes so every
// value here is guaranteed to resolve to a real behavior instead of silently falling back to
// default_floor at runtime. Keys without a hand-written label are shown raw, since that's what
// every other catalog tool references them by too.
export const LOGIC_GROUPS = [
  {
    label: 'Basic',
    options: [
      { value: 'default_avatar', label: 'default_avatar - Default (avatar)' },
      { value: 'default_floor', label: 'default_floor - Default (floor item)' },
      { value: 'default_wall', label: 'default_wall - Default (wall item)' },
      { value: 'dice', label: 'dice - Dice' },
      { value: 'fireworks', label: 'fireworks - Fireworks' },
      { value: 'gate', label: 'gate - Gate (blocks/unblocks a tile)' },
      { value: 'room_invisible_click_tile', label: 'room_invisible_click_tile - Invisible click tile' },
      { value: 'vending', label: 'vending' },
      { value: 'vendingmachine', label: 'vendingmachine' },
      { value: 'vendingmachine_no_sides', label: 'vendingmachine_no_sides' },
      { value: 'wf_highscore', label: 'wf_highscore' },
    ],
  },
  {
    label: 'Furniture',
    options: [
      { value: 'furniture_background_color', label: 'furniture_background_color' },
      { value: 'furniture_basic', label: 'furniture_basic' },
      { value: 'furniture_clothing_change', label: 'furniture_clothing_change' },
      { value: 'furniture_custom_stack_height', label: 'furniture_custom_stack_height' },
      { value: 'furniture_dice', label: 'furniture_dice' },
      { value: 'furniture_fireworks', label: 'furniture_fireworks' },
      { value: 'furniture_group_forum_terminal', label: 'furniture_group_forum_terminal' },
      { value: 'furniture_guild_customized', label: 'furniture_guild_customized' },
      { value: 'furniture_guild_gate', label: 'furniture_guild_gate' },
      { value: 'furniture_habbowheel', label: 'furniture_habbowheel' },
      { value: 'furniture_hockey_score', label: 'furniture_hockey_score' },
      { value: 'furniture_muItistate', label: 'furniture_muItistate' },
      { value: 'furniture_multistate', label: 'furniture_multistate' },
      { value: 'furniture_one_way_door', label: 'furniture_one_way_door' },
      { value: 'furniture_present', label: 'furniture_present' },
      { value: 'furniture_purchasable_clothing', label: 'furniture_purchasable_clothing' },
      { value: 'furniture_roomdimmer', label: 'furniture_roomdimmer' },
      { value: 'furniture_score', label: 'furniture_score' },
      { value: 'furniture_static', label: 'furniture_static' },
      { value: 'furniture_window', label: 'furniture_window' },
    ],
  },
  {
    label: 'Games: other',
    options: [
      { value: 'game_timer', label: 'game_timer' },
    ],
  },
];

export const STUFF_DATA_TYPES = [
  { value: 0, label: 'Legacy key' },
  { value: 1, label: 'Map key' },
  { value: 2, label: 'String key' },
  { value: 3, label: 'Vote key' },
  { value: 4, label: 'Empty key' },
  { value: 5, label: 'Number key' },
  { value: 6, label: 'Highscore key' },
  { value: 7, label: 'Crackable key' },
];
