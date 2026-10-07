-- Guild-customized furniture: logic binding and stuff-data format.
--
-- Guilds were removed in the 2026-10-07 core cut. None of the logic names written below is a
-- registered Vortex logic any more, so these furni resolve to the family default; the stuff-data
-- format still decides how their stored state is parsed.
--
-- Every classname below was read out of the shipped assets: each `<classname>.nitro` embeds a JSON
-- part whose `logicType` names the client logic that renders it. Regenerate this file by scanning
-- the furni pack rather than by hand (the scanner, tools/catalog_converter, is no longer in this
-- repository).
--
-- Two things are being repaired here, and they must land together:
--
--   * `logic` shipped as the raw Arcturus `interaction_type` ('guild_furni', 'guild_gate') or as
--     'none'. Neither string is a registered Vortex room-object logic, so every one of these furni
--     silently fell back to `default_floor` (RoomObjectLogicProvider logs a warning and carries on).
--
--   * `stuff_data_type` shipped as 0 (LegacyKey). The client logic reads a StringArrayStuffData --
--     guild id, badge code and both recolours at indices 1..4 -- so the format has to be 2
--     (StringKey). While it stayed 0, the (since removed) catalog grant path refused to stamp the
--     guild identity at all (it gated on StuffDataType.StringKey), which is why guild furni rendered
--     with the client's default colours: white.
--
-- Flipping `stuff_data_type` rewrites how existing rows are parsed: a stored legacy `{"data":"5"}`
-- cannot deserialize into a string array. That is safe here only because no furniture row of these
-- definitions carries stuff data yet -- check that before replaying this against a hotel that has
-- been live longer (scripts/import-reference-data.ps1 replays it).
--
-- Statements are name-scoped and idempotent; a hotel shipping a trimmed furnidata just matches fewer
-- rows.

-- Recoloured guild furni, plus the custom packs that reuse the same client logic to become
-- freely recolourable (habbox_clr_*, shadow_clr_*, hlive_clr*, recolor_*, *_rc_a_crew).
UPDATE `furniture_definitions`
   SET `logic` = 'furniture_guild_customized',
       `stuff_data_type` = 2
 WHERE `name` IN (
    'a_m_rc_a_crew', 'a_rc_a_crew', 'army_c15_groupflag', 'b_rc_a_crew',
    'c_rc_a_crew', 'd_rc_a_crew', 'e_rc_a_crew', 'f_rc_a_crew',
    'fball_flag_grp', 'fball_grp_bench', 'fball_grp_cote', 'fball_grp_crnr',
    'fball_grp_fnc1', 'fball_grp_fnc3', 'g_rc_a_crew', 'gld_badgewall_tall',
    'gld_carpet', 'gld_dragon', 'gld_dragon2', 'gld_fan',
    'gld_fan2', 'gld_hangflag1', 'gld_hangflag2', 'gld_juillet',
    'gld_mic', 'gld_micro', 'gld_minis', 'gld_pennant',
    'gld_sofa1', 'gld_stage', 'gld_stage1_1', 'gld_stage1_2',
    'gld_stage2_1', 'gld_stage2_2', 'gld_stage3_1', 'gld_stage3_2',
    'gld_stair', 'gld_stool1', 'gld_stool2', 'gld_table1',
    'gld_tile1', 'gld_tile2', 'gld_wall_tall', 'gld_wfall',
    'guild_customized', 'h_rc_a_crew', 'habbox_clr_bc_cilinder', 'habbox_clr_bc_cone',
    'habbox_clr_bc_curveramp', 'habbox_clr_bc_glasspanel', 'habbox_clr_bc_halfcilinder', 'habbox_clr_bc_hemisphere',
    'habbox_clr_bc_layingcilinder', 'habbox_clr_bc_layingtriangle', 'habbox_clr_bc_numbers', 'habbox_clr_bc_panel2',
    'habbox_clr_bc_plainbig', 'habbox_clr_bc_plainramp', 'habbox_clr_bc_plainround', 'habbox_clr_bc_plainsmall2',
    'habbox_clr_bc_plainsmallblock', 'habbox_clr_bc_pyramid', 'habbox_clr_bc_quartercircle', 'habbox_clr_bc_quarterring',
    'habbox_clr_bc_quartertriangle', 'habbox_clr_bc_ramp', 'habbox_clr_bc_sphere', 'habbox_clr_bc_stairs',
    'habbox_clr_bc_stick', 'habbox_clr_bc_wedgecrnr', 'habbox_clr_finesse_bloc1', 'habbox_clr_finesse_bloc2',
    'habbox_clr_finesse_bloc3', 'habbox_clr_finesse_cube1', 'habbox_clr_finesse_cube2', 'habbox_clr_finesse_cube3',
    'habbox_clr_half_diner_block', 'habbox_clr_noob_chairv2', 'habbox_clr_noob_lamp', 'habbox_clr_noob_rug',
    'habbox_clr_noob_stool', 'habbox_clr_noob_table', 'habbox_clr_pura_bar2', 'habbox_clr_pura_bed1',
    'habbox_clr_pura_bed2', 'habbox_clr_pura_elevation3', 'habbox_clr_pura_fridge', 'habbox_clr_pura_lamp1',
    'habbox_clr_pura_lamp2v2', 'habbox_clr_pura_lamp3', 'habbox_clr_pura_mdl1v2', 'habbox_clr_pura_mdl2',
    'habbox_clr_pura_mdl3', 'habbox_clr_pura_mdl4', 'habbox_clr_pura_mdl5', 'habbox_clr_pura_mdl6',
    'habbox_clr_pura_ovalchair', 'habbox_clr_pura_shelves', 'habbox_clr_romantique_armchr3', 'habbox_clr_romantique_clock',
    'habbox_clr_romantique_divan', 'habbox_clr_romantique_divider2', 'habbox_clr_romantique_dresser2', 'habbox_clr_romantique_piano',
    'habbox_clr_romantique_smlltbl3', 'habbox_clr_romantique_stool', 'habbox_clr_romantique_table', 'habbox_clr_romantique_wall2',
    'habbox_clr_zengarden_sand2', 'habbox_clr_zeref_block', 'hlive_clr24_biglight', 'hlive_clr24_bloodstreak',
    'hlive_clr24_brickbigroof', 'hlive_clr24_brickinnercorner', 'hlive_clr24_bricklinecurveroof', 'hlive_clr24_bricklineroof',
    'hlive_clr24_brickroofcorner', 'hlive_clr24_brickroofcurve', 'hlive_clr24_brickroofog', 'hlive_clr24_bricksmallineroof',
    'hlive_clr24_bricksmallroof1', 'hlive_clr24_bricksmallroof2', 'hlive_clr24_brickwall', 'hlive_clr24_bricverysmalroof',
    'hlive_clr24_cherryns_lightbar', 'hlive_clr24_cherryns_lightbar2', 'hlive_clr24_cherryns_lightbox', 'hlive_clr24_cherryns_luzlaser',
    'hlive_clr24_cherryns_spotlight', 'hlive_clr24_cherryns_spotligt', 'hlive_clr24_cobblelaje', 'hlive_clr24_corner1',
    'hlive_clr24_corner2', 'hlive_clr24_groundlight', 'hlive_clr24_liquidtrail', 'hlive_clr24_norjabed',
    'hlive_clr24_norjadoublebed', 'hlive_clr24_norjafloor', 'hlive_clr24_norjahanginglight', 'hlive_clr24_norjashelf',
    'hlive_clr24_norjastandinglamp', 'hlive_clr24_plastodoublebed', 'hlive_clr24_plastolight', 'hlive_clr24_plastopc',
    'hlive_clr24_plastoshelf', 'hlive_clr24_plastowindow', 'hlive_clr24_podsofa2', 'hlive_clr24_rollerfino',
    'hlive_clr24_roodolfoantiqchair', 'hlive_clr24_roodolfocollecsofa', 'hlive_clr24_silochandelier', 'hlive_clr24_silodesklamp',
    'hlive_clr24_silofridge', 'hlive_clr24_silostandinglamp', 'hlive_clr24_stickerbat', 'hlive_clr24_stickerghost',
    'hlive_clr24_stickerpumpkin', 'hlive_clr24_stoneblockad', 'hlive_clr24_summergrill', 'hlive_clr24_vaporwavefloor',
    'hlive_clr24_vaporwavefloor2', 'hlive_clr24_virusliquid', 'hlive_clr24_wiredarrow', 'hlive_clr24_wiredarrowdown',
    'hlive_clr24_wiredbutton', 'hlive_clr24_wiredbutton2', 'hlive_clr24_wiredcircle', 'hlive_clr24_wiredcircle2',
    'hlive_clr24_wiredcolortile', 'hlive_clr24_wiredcolortile1', 'hlive_clr24_wiredcolortile1a', 'hlive_clr24_wiredcolortile1b',
    'hlive_clr24_wiredcolortile1x2', 'hlive_clr24_wiredcolortile2x2', 'hlive_clr24_wiredcolortile3x3', 'hlive_clr24_wiredcolortile4x4',
    'hlive_clr24_wiredcrystalshard', 'hlive_clr24_wiredcrystalshard2', 'hlive_clr24_wiredglassdoor', 'hlive_clr24_wiredhidecolortile',
    'hlive_clr24_wiredima', 'hlive_clr24_wiredlifebar3', 'hlive_clr24_wiredlifebar5', 'hlive_clr24_wiredlifebar7',
    'hlive_clr24_wiredmaze', 'hlive_clr24_wiredonewaydoor', 'hlive_clr24_wiredonewaydoor2', 'hlive_clr24_wiredplacarneg',
    'hlive_clr24_wiredplacarpos', 'hlive_clr24_wiredplatearrow', 'hlive_clr24_wiredplateindica', 'hlive_clr24_wiredplatepress',
    'hlive_clr24_wiredplatepress2', 'hlive_clr24_wiredplateprism', 'hlive_clr24_wiredpuzzlebox', 'hlive_clr24_wiredsphere2',
    'hlive_clr24_wiredswitch', 'hlive_clr25_annablob', 'hlive_clr25_annachair', 'hlive_clr25_annadivcrnr',
    'hlive_clr25_annadivider', 'hlive_clr25_annalamp', 'hlive_clr25_annarug', 'hlive_clr25_annasofa',
    'hlive_clr25_annastool', 'hlive_clr25_annatable', 'hlive_clr25_bonusaloeplant', 'hlive_clr25_bonuscrowncactus',
    'hlive_clr25_bonustulips', 'hlive_clr25_bonusvasestand', 'hlive_clr25_dinerbardesk', 'hlive_clr25_dinerbardeskcorner',
    'hlive_clr25_dinerbardeskgate', 'hlive_clr25_dinercashreg', 'hlive_clr25_dinerchair', 'hlive_clr25_dinergumvendor',
    'hlive_clr25_dinersofa', 'hlive_clr25_dinersofa2', 'hlive_clr25_dinertable', 'hlive_clr25_dinertable2',
    'hlive_clr25_hblooza_dvdrclrb', 'hlive_clr25_hblooza_dvdrclrb3', 'hlive_clr25_hblooza_sfnc', 'hlive_clr25_hblooza_sfnc_crnr',
    'hlive_clr25_hblooza_stage', 'hlive_clr25_hblooza_stage2', 'hlive_clr25_hblooza_tfnc', 'hlive_clr25_hblooza_tgate',
    'hlive_clr25_rodolfowaterfall', 'hlive_clr25_swdbobbabeachchair', 'hlive_clr25_swdbobbaparasol', 'hlive_clr25_swdbobbaplaschair',
    'hlive_clr25_swdbobbaplastable', 'hlive_clr25_swdbobbatoldo', 'hlive_clr25_swdbobbawoodseat', 'hlive_clr25_swdbobbawoodtable',
    'hlive_clr25_zengardendivider', 'hlive_clr25_zengardenpebbles', 'hlive_clr25_zengardenplankf', 'hlive_clr25_zengardenrock',
    'hlive_clr25_zengardenroof', 'hlive_clr25_zengardenroofc', 'i_rc_a_crew', 'iron_colorable_press_buttom_M',
    'iron_colorable_press_buttom_W', 'iron_colorable_press_buttom_W (1)', 'j_rc_a_crew', 'k_rc_a_crew',
    'l_rc_a_crew', 'lipe_clr_blackout_block', 'lipe_clr_blackout_corner_2', 'lipe_clr_blackout_corner_3',
    'lipe_clr_blackout_smallblock', 'lipe_clr_blackout_wall_3', 'lipe_clr_flower', 'lipe_clr_olympics_swissballv2',
    'lipe_clr_ornaments', 'lipe_clr_palacerug', 'lipe_clr_shaggyrug', 'lipe_clr_stoneblock_1',
    'lipe_clr_woodswitch', 'm_rc_a_crew', 'n_rc_a_crew', 'n_z_rc_a_crew',
    'number_sign_3_1_crew', 'o_rc_a_crew', 'p_rc_a_crew', 'pirate_mast2grp',
    'pirate_mast3grp', 'pirate_mast4grp', 'q_rc_a_crew', 'r_rc_a_crew',
    'recolor_a', 'recolor_boat', 'recolor_buoy_corner', 'recolor_buoy_divider',
    'recolor_bus', 'recolor_inflatable_chair', 'recolor_pool_corner', 'recolor_pool_divider',
    'recolor_pool_ladder', 'recolor_raft', 'recolor_sofa', 'recolor_surfboard',
    'recolourable_crew_hologram', 'regado_olymp_coable_yogamat', 'regado_val15_coable_water', 's_rc_a_crew',
    'shadow_bg_floordegrad3', 'shadow_bg_roomdegrad3', 'shadow_clr_bazaar_curtain', 'shadow_clr_bazaar_dye',
    'shadow_clr_bazaar_lamp', 'shadow_clr_bazaar_marquee', 'shadow_clr_bazaar_pillow', 'shadow_clr_bazaar_potionbig',
    'shadow_clr_bazaar_potionsmall', 'shadow_clr_bazaar_roofbig', 'shadow_clr_bazaar_roofsmall', 'shadow_clr_bazaar_rug',
    'shadow_clr_bazaar_sandwallv2', 'shadow_clr_bazaar_spice', 'shadow_clr_bazaar_towels', 'shadow_clr_bazaar_vase',
    'shadow_clr_bc_artdeco', 'shadow_clr_bc_brickblock', 'shadow_clr_bc_earth', 'shadow_clr_bc_flowerblock',
    'shadow_clr_bc_grass', 'shadow_clr_bc_greekmarble', 'shadow_clr_bc_industrial', 'shadow_clr_bc_lavablock',
    'shadow_clr_bc_marble', 'shadow_clr_bc_metalcrate', 'shadow_clr_bc_metalgrip', 'shadow_clr_bc_sand',
    'shadow_clr_bc_stone', 'shadow_clr_bc_tile', 'shadow_clr_bc_water', 'shadow_clr_bc_wood',
    'shadow_clr_bc_wool', 'shadow_clr_bling_block', 'shadow_clr_bling_chair', 'shadow_clr_bling_divider',
    'shadow_clr_bling_pillar', 'shadow_clr_bonus_aucuba', 'shadow_clr_bonus_bonsai', 'shadow_clr_bonus_juicemachine',
    'shadow_clr_bonus_juicemchn', 'shadow_clr_bonus_lampshade', 'shadow_clr_bonus_smallvase', 'shadow_clr_bonus_succulent',
    'shadow_clr_bonus_vase', 'shadow_clr_bonus_vasebird', 'shadow_clr_bonus_vaselotus', 'shadow_clr_cinema_benchv2',
    'shadow_clr_cinema_glasswallv5', 'shadow_clr_cinema_marblev2', 'shadow_clr_elevatedrockery2', 'shadow_clr_group_carpet',
    'shadow_clr_group_flag', 'shadow_clr_group_gate', 'shadow_clr_group_hangflag', 'shadow_clr_group_smalltile1',
    'shadow_clr_group_smalltile2', 'shadow_clr_group_sofa', 'shadow_clr_group_stool', 'shadow_clr_group_table',
    'shadow_clr_group_tile1', 'shadow_clr_group_tile2', 'shadow_clr_group_tileflag', 'shadow_clr_group_wall1',
    'shadow_clr_group_wall2', 'shadow_clr_iced_bench2', 'shadow_clr_iced_chair2', 'shadow_clr_iced_corner',
    'shadow_clr_iced_divider', 'shadow_clr_iced_gate', 'shadow_clr_iced_shelves', 'shadow_clr_iced_sofa3',
    'shadow_clr_iced_sofachair', 'shadow_clr_iced_solarium', 'shadow_clr_iced_table', 'shadow_clr_iced_venetian2',
    'shadow_clr_iced_venetiancrnr2', 'shadow_clr_lmbigrock', 'shadow_clr_lodge_bedcouple', 'shadow_clr_lodge_bedsingle',
    'shadow_clr_lodge_benchv2', 'shadow_clr_lodge_chair', 'shadow_clr_lodge_fireplacev3', 'shadow_clr_lodge_lamp',
    'shadow_clr_lodge_shelf', 'shadow_clr_lodge_smalltable', 'shadow_clr_lodge_table', 'shadow_clr_palette1',
    'shadow_clr_plastic_4legtbl', 'shadow_clr_plastic_bigtbl', 'shadow_clr_plastic_chair2', 'shadow_clr_plastic_podchair',
    'shadow_clr_plastic_roundtbl', 'shadow_clr_plastic_smalltbl', 'shadow_clr_prisonstone', 'shadow_clr_rare_1wdoor3',
    'shadow_clr_roller', 'shadow_clr_rug_bigplain', 'shadow_clr_rug_smallplain', 'shadow_clr_sports_audbench',
    'shadow_clr_sports_bench1', 'shadow_clr_sports_bench2v2', 'shadow_clr_sports_bench3', 'shadow_clr_sports_cote',
    'shadow_clr_sports_fnc1', 'shadow_clr_sports_fnc2', 'shadow_clr_sports_fnc3', 'shadow_clr_tele_bathroom2',
    'shadow_clr_tele_cabin', 'shadow_clr_tele_cabinet', 'shadow_clr_usva_chair', 'shadow_clr_usva_floorcarpet',
    'shadow_clr_usva_lamp', 'shadow_clr_usva_rug', 'shadow_clr_usva_shelf', 'shadow_clr_usva_shelf2',
    'shadow_clr_usva_shelflamp', 'shadow_clr_usva_sofa', 'shadow_clr_usva_table', 'shadow_clr_wlkrlr',
    'shadow_clr_wtrstone1', 'shadow_clr_wtrstone2', 'shadow_colour_rug_bigbear', 'shadow_colour_rug_bigsoft',
    'shadow_colour_rug_door', 'shadow_colour_rug_smallbear', 'shadow_colour_rug_smallsoft', 'shadow_ironhotel_largetube',
    'spoin_clr_butterfly', 't_rc_a_crew', 'u_rc_a_crew', 'v_rc_a_crew',
    'w_rc_a_crew', 'x_rc_a_crew', 'y_rc_a_crew', 'z_rc_a_crew',
    'zara_clothing1', 'zara_clothing3'
);

-- The forum terminal keeps the same stuff data but binds to its own client logic, which turns the
-- guild id into an internal link ('groupforum/<id>') instead of opening the guild context menu.
UPDATE `furniture_definitions`
   SET `logic` = 'furniture_group_forum_terminal',
       `stuff_data_type` = 2
 WHERE `name` IN (
    'gld_showcase', 'guild_forum'
);

-- gld_gate deliberately diverges from the asset, which declares 'furniture_guild_customized' like
-- every other guild furni: the Flash client derives blocking from the visualization, whereas Vortex
-- resolves walkability server-side and so needed a gate logic. Folding it back into the shared logic
-- would have forced gate rules onto guild carpets and tiles. FurnitureGuildGateLogic went with the
-- guilds: 'furniture_guild_gate' is unregistered and resolves to the family default.
UPDATE `furniture_definitions`
   SET `logic` = 'furniture_guild_gate',
       `stuff_data_type` = 2
 WHERE `name` = 'gld_gate';
