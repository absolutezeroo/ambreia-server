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
  // Extra param is the amount: days of membership, or furni added to the placement limit. Sold from
  // the Builders Club tree's builders_club_loyalty / builders_club_addons pages.
  { value: 8, label: 'Builders Club days' },
  { value: 9, label: 'Builders Club furni' },
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
  { value: 19, label: 'Monsterplant seed' },
  { value: 20, label: 'Monsterplant revival' },
  { value: 21, label: 'Monsterplant rebreed' },
  { value: 22, label: 'Monsterplant fertilize' },
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
      { value: 'nest', label: 'nest' },
      { value: 'petdrink', label: 'petdrink' },
      { value: 'roller', label: 'roller - Roller (conveyor belt)' },
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
      { value: 'furniture_crackable', label: 'furniture_crackable' },
      { value: 'furniture_custom_stack_height', label: 'furniture_custom_stack_height' },
      { value: 'furniture_dice', label: 'furniture_dice' },
      { value: 'furniture_ecotron_box', label: 'furniture_ecotron_box' },
      { value: 'furniture_effectbox', label: 'furniture_effectbox' },
      { value: 'furniture_fireworks', label: 'furniture_fireworks' },
      { value: 'furniture_group_forum_terminal', label: 'furniture_group_forum_terminal' },
      { value: 'furniture_guild_customized', label: 'furniture_guild_customized' },
      { value: 'furniture_guild_gate', label: 'furniture_guild_gate' },
      { value: 'furniture_habbowheel', label: 'furniture_habbowheel' },
      { value: 'furniture_hockey_score', label: 'furniture_hockey_score' },
      { value: 'furniture_monsterplant_seed', label: 'furniture_monsterplant_seed' },
      { value: 'furniture_muItistate', label: 'furniture_muItistate' },
      { value: 'furniture_multistate', label: 'furniture_multistate' },
      { value: 'furniture_mysterybox', label: 'furniture_mysterybox' },
      { value: 'furniture_mysterytrophy', label: 'furniture_mysterytrophy' },
      { value: 'furniture_nft_reward_box', label: 'furniture_nft_reward_box' },
      { value: 'furniture_one_way_door', label: 'furniture_one_way_door' },
      { value: 'furniture_present', label: 'furniture_present' },
      { value: 'furniture_purchasable_clothing', label: 'furniture_purchasable_clothing' },
      { value: 'furniture_pushable', label: 'furniture_pushable' },
      { value: 'furniture_roomdimmer', label: 'furniture_roomdimmer' },
      { value: 'furniture_score', label: 'furniture_score' },
      { value: 'furniture_static', label: 'furniture_static' },
      { value: 'furniture_welcome_gift', label: 'furniture_welcome_gift' },
      { value: 'furniture_window', label: 'furniture_window' },
    ],
  },
  {
    label: 'Pets',
    options: [
      { value: 'monsterplant_seed', label: 'monsterplant_seed - Monsterplant seed' },
      { value: 'pet_drink', label: 'pet_drink - Pet drink bowl' },
      { value: 'pet_food', label: 'pet_food - Pet food bowl' },
      { value: 'pet_nest', label: 'pet_nest - Pet nest' },
      { value: 'pet_toy', label: 'pet_toy' },
      { value: 'pet_trampoline', label: 'pet_trampoline' },
    ],
  },
  {
    label: 'Games: Battle Banzai',
    options: [
      { value: 'battlebanzai_gate_blue', label: 'battlebanzai_gate_blue' },
      { value: 'battlebanzai_gate_green', label: 'battlebanzai_gate_green' },
      { value: 'battlebanzai_gate_red', label: 'battlebanzai_gate_red' },
      { value: 'battlebanzai_gate_yellow', label: 'battlebanzai_gate_yellow' },
      { value: 'battlebanzai_random_teleport', label: 'battlebanzai_random_teleport' },
      { value: 'battlebanzai_random_teleport_exclude', label: 'battlebanzai_random_teleport_exclude' },
      { value: 'battlebanzai_tile', label: 'battlebanzai_tile' },
    ],
  },
  {
    label: 'Games: Freeze',
    options: [
      { value: 'freeze_block', label: 'freeze_block' },
      { value: 'freeze_counter_blue', label: 'freeze_counter_blue' },
      { value: 'freeze_counter_green', label: 'freeze_counter_green' },
      { value: 'freeze_counter_red', label: 'freeze_counter_red' },
      { value: 'freeze_counter_yellow', label: 'freeze_counter_yellow' },
      { value: 'freeze_exit', label: 'freeze_exit' },
      { value: 'freeze_gate_blue', label: 'freeze_gate_blue' },
      { value: 'freeze_gate_green', label: 'freeze_gate_green' },
      { value: 'freeze_gate_red', label: 'freeze_gate_red' },
      { value: 'freeze_gate_yellow', label: 'freeze_gate_yellow' },
      { value: 'freeze_tile', label: 'freeze_tile' },
    ],
  },
  {
    label: 'Games: Football',
    options: [
      { value: 'football_goal_blue', label: 'football_goal_blue' },
      { value: 'football_goal_green', label: 'football_goal_green' },
      { value: 'football_goal_red', label: 'football_goal_red' },
      { value: 'football_goal_yellow', label: 'football_goal_yellow' },
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
