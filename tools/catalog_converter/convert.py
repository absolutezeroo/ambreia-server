#!/usr/bin/env python3
"""
Arcturus → Vortex catalog converter
=====================================
Input  (./input/):
    items_base.sql       → output/furniture_definitions.sql
    catalog_pages.sql    → output/catalog_pages.sql
    catalog_items.sql    → output/catalog_offers.sql
                         → output/catalog_products.sql
    FurnitureData.json   → output/furnidata.xml   (optional)
    ProductData.json     → output/productdata.txt (optional)

Run: python convert.py [--replace]

    --replace   Generate `INSERT ... ON DUPLICATE KEY UPDATE` instead of
                `INSERT IGNORE`, so re-importing UPDATES existing rows in place
                (matched by primary/unique key) rather than skipping them. Lets
                you re-run the import to apply fixes without purging the tables.
"""

import re
import json
import sys
from pathlib import Path

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------
BASE_DIR = Path(__file__).parent
INPUT_DIR = BASE_DIR / "input"
OUTPUT_DIR = BASE_DIR / "output"
OUTPUT_DIR.mkdir(exist_ok=True)

BATCH_SIZE = 500

# Set from --replace in main(). When True, write_bulk_inserts emits upserts.
UPSERT = False

# ---------------------------------------------------------------------------
# Layout mapping  Arcturus string → Vortex wire string
# ---------------------------------------------------------------------------
LAYOUT_MAP: dict[str, str] = {
    "default_3x3": "default_3x3",
    "default_3x3_color_grouping": "default_3x3",
    "club_buy": "club_buy",
    "club_gift": "club_gifts",
    "frontpage": "frontpage4",
    "frontpage_featured": "frontpage_featured",
    "spaces": "spaces_new",
    "spaces_new": "spaces_new",
    "recycler": "recycler",
    "recycler_info": "recycler_info",
    "recycler_prizes": "recycler_prizes",
    "trophies": "trophies",
    "plasto": "default_3x3",
    "marketplace": "marketplace",
    "marketplace_own_items": "marketplace_own_items",
    "soundmachine": "soundmachine",
    "guilds": "guild_frontpage",
    "guild_furni": "guild_custom_furni",
    "info_duckets": "info_duckets",
    "info_rentables": "info_rentables",
    "info_pets": "default_3x3",
    "roomads": "roomads",
    "single_bundle": "single_bundle",
    "sold_ltd_items": "default_3x3",
    "badge_display": "badge_display",
    "bots": "default_3x3",
    "pets": "pets",
    "pets2": "pets2",
    "pets3": "pets3",
    "productpage1": "default_3x3",
    "room_bundle": "single_bundle",
    "recent_purchases": "default_3x3",
    "guild_forum": "guild_forum",
    "vip_buy": "vip_buy",
    "info_loyalty": "info_loyalty",
    "loyalty_vip_buy": "loyalty_vip_buy",
    "collectibles": "default_3x3",
    "petcustomization": "petcustomization",
}

# ---------------------------------------------------------------------------
# Arcturus items_base.type → Vortex ProductType int
# ---------------------------------------------------------------------------
PRODUCT_TYPE_MAP: dict[str, int] = {
    "s": 0,  # Floor
    "i": 1,  # Wall
    "e": 2,  # Effect
    "b": 3,  # Badge
    "r": 4,  # Robot
}

# ---------------------------------------------------------------------------
# FurnitureCategory based on (type, interaction_type)
# Default=1, WallPaper=2, Floor=3, Landscape=4, PostIt=5, Poster=6
# ---------------------------------------------------------------------------
# The three decorations are keyed on the item NAME too: Arcturus gives them interaction_type
# "roomeffect", so the interaction test alone imported them as Default and the client placed them
# as ordinary furni instead of applying them to the room.
DECORATION_CATEGORIES = {"wallpaper": 2, "floor": 3, "landscape": 4}


def get_furni_category(item_type: str, interaction_type: str, item_name: str = "") -> int:
    if item_type == "i":
        by_name = DECORATION_CATEGORIES.get(item_name.lower())
        if by_name is not None: return by_name
        it = interaction_type.lower()
        if it in DECORATION_CATEGORIES: return DECORATION_CATEGORIES[it]
        if it == "postit":      return 5
        if it == "poster":      return 6
    return 1


def get_logic(interaction_type: str) -> str:
    return "none" if interaction_type in ("default", "") else interaction_type


# ---------------------------------------------------------------------------
# Asset-derived logic binding
# ---------------------------------------------------------------------------
# items_base has no logic column: it carries an `interaction_type`, which get_logic copies verbatim.
# Almost none of those strings is a registered Vortex logic -- on the dump this converter was built
# for, 53 782 of 55 279 definitions resolved to nothing and fell through to the family default, so
# vending machines, beds, teleports and pressure plates imported inert.
#
# The shipped `.nitro` assets carry the real binding. scan_asset_logic.py reads it out of them, and
# also lists which logic names Vortex actually registers. Regenerate both with:
#
#     python scan_asset_logic.py <furni-pack-dir>
#
# THE RULE: keep the dump's interaction_type when it is already a registered Vortex logic, otherwise
# take the asset's. Never the reverse. The client's names are not a superset of ours -- it calls a
# gate `furniture_multistate` because Flash derives blocking from the visualization, while Vortex
# resolves walkability server-side. Preferring the asset unconditionally overwrites 744 working
# definitions, 375 of them gates that would stop blocking, plus every wired furni, whose Vortex logic
# names mirror Arcturus on purpose (`wf_act_*`, `wf_cnd_*`, ...).
#
# SECOND RULE: the client has no logic for behaviour that is purely server-side. A vending machine, a
# bed, a teleport and a pressure plate all look plain to it, so their assets say `furniture_basic` or
# `furniture_multistate`. Taking that would replace the only surviving record that the furni is meant
# to do something -- 598 vending machines, 145 teleports, 140 switches, 136 pressure plates, 80 beds
# -- with a name meaning "ordinary furni". So a plain asset logic only ever replaces "none".
ASSET_LOGIC_FILE = BASE_DIR / "data" / "asset_logic.json"
VORTEX_LOGICS_FILE = BASE_DIR / "data" / "vortex_logics.json"

# Client logics that mean "an ordinary furni, possibly with states" -- exactly what the family
# default already does.
PLAIN_ASSET_LOGICS = frozenset(
    {
        "furniture_basic",
        "furniture_multistate",
        # Not a typo on our side: the shipped assets misspell it with a capital i on seven furni.
        "furniture_muItistate",
        "furniture_static",
    }
)

# Client logic name -> stuff_data_type, derived from the StuffData class each client logic casts to.
# Ids are the client's own (0 legacy, 1 map, 2 string array, 3 vote, 4 empty, 5 number, 6 high score,
# 7 crackable), which match Vortex's StuffDataType one for one.
#
# furniture_crackable is absent on purpose: it reads its state through the generic model controller,
# so it names no StuffData class, and it also needs a prize_pool_bindings row for its hit count.
# Setting the format alone would produce half-configured crackables; crackable.sql handles them with
# their bindings.
STUFF_DATA_TYPE_BY_LOGIC: dict[str, int] = {
    "furniture_bb": 1,
    "furniture_bg": 1,
    "furniture_coinschest": 1,
    "furniture_furnichest": 1,
    "furniture_mannequin": 1,
    "furniture_present": 1,
    "furniture_achievement_resolution": 2,
    "furniture_badge_display": 2,
    "furniture_group_forum_terminal": 2,
    "furniture_guild_customized": 2,
    "furniture_guild_gate": 2,
    "furniture_hween_lovelock": 2,
    "furniture_lovelock": 2,
    "furniture_wildwest_wanted": 2,
    "furniture_vote_counter": 3,
    "furniture_vote_majority": 3,
    "furniture_area_hide": 5,
    "furniture_background_color": 5,
}


def load_json_data(path: Path, what: str) -> dict:
    if not path.exists():
        print(
            f"  [WARN] {path.name} not found -- {what}. Definitions will import with the raw\n"
            f"         interaction_type and mostly resolve to the family default, which is the bug\n"
            f"         this file exists to fix. Run scan_asset_logic.py to generate it.",
            file=sys.stderr,
        )
        return {}

    return json.loads(path.read_text(encoding="utf-8"))


# ---------------------------------------------------------------------------
# XML helpers
# ---------------------------------------------------------------------------
def xml_escape(s: str) -> str:
    return (
        s.replace("&", "&amp;")
         .replace("<", "&lt;")
         .replace(">", "&gt;")
         .replace('"', "&quot;")
    )


def xml_tag(name: str, val: str) -> str:
    return f"<{name}/>" if not val else f"<{name}>{xml_escape(val)}</{name}>"


def xml_bool(v) -> str:
    if isinstance(v, bool):
        return "1" if v else "0"
    try:
        return "1" if int(v) else "0"
    except (ValueError, TypeError):
        return "0"


# ---------------------------------------------------------------------------
# SQL helpers
# ---------------------------------------------------------------------------
def sql_str(value) -> str:
    if value is None:
        return "NULL"
    if isinstance(value, bool):
        return "1" if value else "0"
    if isinstance(value, (int, float)):
        return str(value)
    s = str(value)
    s = s.replace("\\", "\\\\")
    s = s.replace("'", "\\'")
    s = s.replace("\n", "\\n")
    s = s.replace("\r", "\\r")
    s = s.replace("\0", "\\0")
    return f"'{s}'"


def sql_bool(value) -> str:
    return "1" if value else "0"


def to_int(value, default: int = 0) -> int:
    try:
        return int(value) if value is not None else default
    except (ValueError, TypeError):
        return default


def to_float(value, default: float = 0.0) -> float:
    try:
        return float(value) if value is not None else default
    except (ValueError, TypeError):
        return default


def json_array(items: list) -> str:
    cleaned = [str(x) for x in items if x is not None and str(x).strip() != ""]
    if not cleaned:
        return "NULL"
    return sql_str(json.dumps(cleaned, ensure_ascii=False))


def write_bulk_inserts(
    lines: list[str],
    table: str,
    columns: list[str],
    value_rows: list[str],
    ignore: bool = False,
) -> None:
    if not value_rows:
        return
    # In --replace mode, upsert wins over IGNORE (the two are mutually exclusive in MySQL):
    # existing rows are UPDATEd from the inserted values instead of skipped. id / created_at
    # are preserved so re-imports don't churn primary keys or lose original insert times.
    kw = " IGNORE" if (ignore and not UPSERT) else ""
    col_str = ", ".join(f"`{c}`" for c in columns)
    header = f"INSERT{kw} INTO `{table}` ({col_str}) VALUES"
    dup_clause = ""
    if UPSERT:
        upd_cols = [c for c in columns if c not in ("id", "created_at")]
        dup_clause = "ON DUPLICATE KEY UPDATE " + ", ".join(
            f"`{c}`=VALUES(`{c}`)" for c in upd_cols
        )
    for i in range(0, len(value_rows), BATCH_SIZE):
        batch = value_rows[i : i + BATCH_SIZE]
        lines.append(header)
        for j, row in enumerate(batch):
            if j < len(batch) - 1:
                sep = ","
            else:
                sep = f"\n{dup_clause};" if UPSERT else ";"
            lines.append(f"  {row}{sep}")
        lines.append("")


# ---------------------------------------------------------------------------
# MySQL INSERT parser
# ---------------------------------------------------------------------------
def parse_insert_values(line: str) -> list | None:
    m = re.search(r"VALUES\s*\((.+)\)\s*;?\s*$", line, re.IGNORECASE | re.DOTALL)
    if not m:
        return None

    raw = m.group(1)
    values: list = []
    i = 0
    n = len(raw)

    while i < n:
        while i < n and (raw[i].isspace() or raw[i] == ","):
            i += 1
        if i >= n:
            break

        if raw[i] == "'":
            i += 1
            buf: list[str] = []
            while i < n:
                c = raw[i]
                if c == "\\" and i + 1 < n:
                    nxt = raw[i + 1]
                    esc = {"n": "\n", "t": "\t", "r": "\r", "\\": "\\", "'": "'", '"': '"', "0": "\0"}
                    buf.append(esc.get(nxt, nxt))
                    i += 2
                elif c == "'" and i + 1 < n and raw[i + 1] == "'":
                    buf.append("'")
                    i += 2
                elif c == "'":
                    i += 1
                    break
                else:
                    buf.append(c)
                    i += 1
            values.append("".join(buf))
        elif raw[i : i + 4].upper() == "NULL":
            values.append(None)
            i += 4
        else:
            end = i
            depth = 0
            while end < n:
                if raw[end] == "(":
                    depth += 1
                elif raw[end] == ")":
                    if depth == 0:
                        break
                    depth -= 1
                elif raw[end] == "," and depth == 0:
                    break
                end += 1
            token = raw[i:end].strip()
            if token:
                if "." in token:
                    try:
                        values.append(float(token))
                    except ValueError:
                        values.append(token)
                else:
                    try:
                        values.append(int(token))
                    except ValueError:
                        values.append(token)
            i = end

    return values


def iter_inserts(sql_file: Path) -> list[list]:
    rows: list[list] = []
    with open(sql_file, encoding="utf-8", errors="replace") as f:
        for line in f:
            line = line.strip()
            if line.upper().startswith("INSERT INTO"):
                row = parse_insert_values(line)
                if row is not None:
                    rows.append(row)
    return rows


# ---------------------------------------------------------------------------
# 1. items_base → furniture_definitions
# ---------------------------------------------------------------------------
def convert_items_base(src: Path, dst: Path) -> tuple[dict[int, dict], dict[int, int]]:
    rows = iter_inserts(src)
    lookup:   dict[int, dict]  = {}
    id_remap: dict[int, int]   = {}
    seen_key: dict[tuple, int] = {}

    COLS = [
        "id", "sprite_id", "name", "type", "category", "logic", "total_states",
        "width", "length", "stack_height", "can_stack", "can_walk", "can_sit", "can_lay",
        "can_recycle", "can_trade", "can_group", "can_sell", "usage_policy",
        "extra_data", "created_at", "updated_at", "deleted_at", "stuff_data_type",
    ]

    asset_logic = load_json_data(ASSET_LOGIC_FILE, "asset logic bindings are missing")
    vortex_logics = load_json_data(VORTEX_LOGICS_FILE, "the Vortex logic registry is missing")
    rebound = 0

    lines: list[str] = [
        "-- furniture_definitions converted from Arcturus items_base",
        "-- Generated by tools/catalog_converter/convert.py",
        "-- Rows with duplicate (sprite_id, type, category) are merged;",
        "-- catalog_products.definition_id is remapped accordingly.",
        "",
        "SET NAMES utf8mb4;",
        "SET FOREIGN_KEY_CHECKS = 0;",
        "",
    ]
    value_rows: list[str] = []
    written = 0
    dupes   = 0

    for row in rows:
        if len(row) < 27:
            print(f"  [WARN] items_base row too short ({len(row)} cols), skipping", file=sys.stderr)
            continue

        # Column order per this dump's own CREATE TABLE `items_base` -- NOT stock Arcturus:
        #   id, sprite_id, item_name, public_name, type, width, length, stack_height,
        #   allow_stack, allow_sit, allow_lay, allow_walk, allow_gift, allow_trade,
        #   allow_recycle, allow_marketplace_sell, allow_inventory_stack, interaction_type,
        #   interaction_modes_count, vending_ids, multiheight, customparams,
        #   effect_id_male, effect_id_female, clothing_on_walk, page_id, rare
        # This dump puts `type` at index 4 (stock Arcturus puts it at 16). Unpacking with
        # the stock layout shifted every field from index 4 on by one, so can_sit read
        # allow_stack (~1 everywhere -> sit on everything), product_type read
        # allow_inventory_stack (everything became Floor), and width/length/stack_height
        # were all off by one.
        (
            ib_id, sprite_id, item_name, public_name, item_type,
            width, length, stack_height,
            allow_stack, allow_sit, allow_lay, allow_walk,
            allow_gift, allow_trade, allow_recycle, allow_marketplace_sell,
            allow_inventory_stack, interaction_type,
            interaction_modes_count, vending_ids, multiheight, customparams,
            effect_id_male, effect_id_female, clothing_on_walk, page_id, rare
        ) = row[:27]

        item_type        = str(item_type or "s").strip().lower()
        interaction_type = str(interaction_type or "default").strip().lower()

        product_type = PRODUCT_TYPE_MAP.get(item_type, 0)
        category     = get_furni_category(item_type, interaction_type, str(item_name or ""))
        logic        = get_logic(interaction_type)
        extra_data   = str(customparams).strip() if customparams else None

        # Keep a logic the server actually registers; otherwise take what the asset declares.
        # See THE RULE above -- doing this the other way round breaks gates and wired furni.
        if logic not in vortex_logics:
            from_asset = asset_logic.get(str(item_name or ""))
            plain = from_asset in PLAIN_ASSET_LOGICS
            keep_intent = plain and logic != "none"

            if from_asset and from_asset != logic and not keep_intent:
                logic = from_asset
                rebound += 1

        # 0 = StuffDataType.LegacyKey, the entity default.
        stuff_data_type = STUFF_DATA_TYPE_BY_LOGIC.get(logic, 0)

        ib_id_int  = int(ib_id)
        sprite_int = int(sprite_id) if sprite_id is not None else 0

        lookup[ib_id_int] = {
            "sprite_id":    sprite_int,
            "item_name":    str(item_name or ""),
            "type":         item_type,
            "product_type": product_type,
            "category":     category,
            "allow_gift":   bool(to_int(allow_gift, 1)),
        }

        key = (sprite_int, product_type, category)
        if key in seen_key:
            id_remap[ib_id_int] = seen_key[key]
            dupes += 1
            continue
        seen_key[key] = ib_id_int

        width_val  = to_int(width, 1)
        length_val = to_int(length, 1)
        sh_val     = to_float(stack_height, 0.0)

        value_rows.append(
            f"({sql_str(ib_id_int)}, {sql_str(sprite_int)}, "
            f"{sql_str(str(item_name or ''))}, {sql_str(product_type)}, "
            f"{sql_str(category)}, {sql_str(logic)}, "
            f"{sql_str(to_int(interaction_modes_count, 1))}, "
            f"{sql_str(width_val)}, {sql_str(length_val)}, {sql_str(sh_val)}, "
            f"{sql_bool(to_int(allow_stack, 1))}, "
            f"{sql_bool(to_int(allow_walk, 0))}, "
            f"{sql_bool(to_int(allow_sit, 0))}, "
            f"{sql_bool(to_int(allow_lay, 0))}, "
            f"{sql_bool(to_int(allow_recycle, 0))}, "
            f"{sql_bool(to_int(allow_trade, 1))}, "
            f"1, "  # can_group
            f"{sql_bool(to_int(allow_marketplace_sell, 0))}, "  # can_sell
            # usage_policy: 1=Controller (matches the entity default). items_base carries no
            # usage field, so 0=Nobody was disabling interaction on every multi-state furni
            # (FurnitureLogic falls back to the stored policy when total_states > 0).
            f"1, "
            f"{sql_str(extra_data if extra_data else None)}, "
            f"NOW(), NOW(), NULL, "
            f"{sql_str(stuff_data_type)})"
        )
        written += 1

    write_bulk_inserts(lines, "furniture_definitions", COLS, value_rows, ignore=True)
    dst.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(
        f"  -> {dst.name}: {written} definitions written, {dupes} duplicates remapped, "
        f"{rebound} logic bindings taken from the assets"
    )
    return lookup, id_remap


# ---------------------------------------------------------------------------
# 2. catalog_pages → catalog_pages (Vortex)
# ---------------------------------------------------------------------------
def convert_catalog_pages(src: Path, dst: Path) -> None:
    rows = iter_inserts(src)

    COLS = [
        "id", "parent_id", "localization", "name", "icon", "layout",
        "image_data", "text_data", "sort_order", "visible",
        "created_at", "updated_at", "deleted_at",
    ]

    lines: list[str] = [
        "-- catalog_pages converted from Arcturus",
        "-- Generated by tools/catalog_converter/convert.py",
        "-- Pages are topologically sorted: parents always precede children.",
        "",
        "SET NAMES utf8mb4;",
        "SET FOREIGN_KEY_CHECKS = 0;",
        "",
    ]

    # First pass: parse all pages into a lookup so we can topologically sort.
    pages: dict[int, dict] = {}
    skipped = 0
    dupes   = 0

    for row in rows:
        if len(row) < 22:
            skipped += 1
            continue

        page_id_int = int(row[0])
        if page_id_int in pages:
            dupes += 1
            continue

        (
            page_id, parent_id, caption_save, caption, page_layout,
            icon_color, icon_image, min_rank, order_num,
            visible, enabled, club_only, vip_only,
            page_headline, page_teaser, page_special,
            page_text1, page_text2, page_text_details, page_text_teaser,
            room_id, includes
        ) = row[:22]

        parent_int = None if (parent_id is None or int(parent_id) < 0) else int(parent_id)
        parent_val = "NULL" if parent_int is None else sql_str(parent_int)

        localization = str(caption_save or "").strip()
        if not localization:
            localization = str(caption or "").strip().lower().replace(" ", "_")[:50]

        name_val     = str(caption or "").strip()[:50]
        layout_str   = str(page_layout or "default_3x3").strip().lower()
        vortex_layout = LAYOUT_MAP.get(layout_str, "default_3x3")
        icon_val     = int(icon_image) if icon_image is not None else 0
        is_visible   = (str(visible or "0") == "1") and (str(enabled or "0") == "1")
        image_data   = json_array([page_headline, page_teaser, page_special])
        text_data    = json_array([page_text1, page_text2, page_text_details, page_text_teaser])
        sort         = int(order_num) if order_num is not None else 0

        pages[page_id_int] = {
            "parent_id": parent_int,
            "value": (
                f"({sql_str(page_id_int)},{parent_val},{sql_str(localization[:50])},"
                f"{sql_str(name_val)},{sql_str(icon_val)},{sql_str(vortex_layout)},"
                f"{image_data},{text_data},{sql_str(sort)},{sql_bool(is_visible)},"
                f"NOW(),NOW(),NULL)"
            ),
        }

    # Topological sort (BFS) — parents before children.
    from collections import defaultdict, deque

    children: dict[int | None, list[int]] = defaultdict(list)
    for pid, page in pages.items():
        children[page["parent_id"]].append(pid)

    sorted_ids: list[int] = []
    queue: deque[int] = deque(children[None])
    while queue:
        pid = queue.popleft()
        sorted_ids.append(pid)
        queue.extend(children.get(pid, []))

    # Orphans (parent references a non-existent page) go at the end.
    reached = set(sorted_ids)
    orphans = 0
    for pid in pages:
        if pid not in reached:
            sorted_ids.append(pid)
            orphans += 1

    value_rows = [pages[pid]["value"] for pid in sorted_ids]

    write_bulk_inserts(lines, "catalog_pages", COLS, value_rows, ignore=True)
    lines.append("SET FOREIGN_KEY_CHECKS = 1;")
    dst.write_text("\n".join(lines) + "\n", encoding="utf-8")
    written = len(pages)
    if orphans:
        print(f"  [WARN] {orphans} orphan pages (parent not found) appended at end", file=sys.stderr)
    print(f"  -> {dst.name}: {written} pages written, {dupes} duplicates skipped, {skipped} malformed skipped")


# ---------------------------------------------------------------------------
# 3. catalog_items → catalog_offers + catalog_products
# ---------------------------------------------------------------------------
def convert_catalog_items(
    src: Path,
    offers_dst: Path,
    products_dst: Path,
    lookup: dict[int, dict],
    id_remap: dict[int, int],
) -> None:
    rows = iter_inserts(src)

    OFFER_COLS = [
        "id", "page_id", "localization_id", "cost_credits", "cost_currency",
        "currency_type_id", "can_gift", "can_bundle", "club_level",
        "discount_percent", "visible", "created_at", "updated_at", "deleted_at",
    ]
    PRODUCT_COLS = [
        "id", "offer_id", "product_type", "definition_id", "extra_param",
        "quantity", "unique_size", "unique_remaining",
        "created_at", "updated_at", "deleted_at",
    ]

    offer_rows: list[str]  = []
    badge_rows: list[str]  = []
    furni_rows: list[str]  = []
    song_notes: list[str]  = []

    product_id = 1
    skipped    = 0
    warn_currency: set[int] = set()

    for row in rows:
        if len(row) < 18:
            skipped += 1
            continue

        # Column order per the source dump's own CREATE TABLE `catalog_items`:
        #   id, item_ids, page_id, offer_id, catalog_name, song_id, order_number,
        #   cost_credits, cost_points, points_type, amount, limited_sells, limited_stack,
        #   extradata, badge, have_offer, club_only, rate
        # (note this dump declares limited_sells BEFORE limited_stack, unlike stock Arcturus).
        (
            item_id, item_ids_raw, page_id, offer_id_arc,
            catalog_name, song_id, order_number,
            cost_credits, cost_points, points_type, amount,
            limited_sells, limited_stack, extradata, badge,
            have_offer, club_only, rate
        ) = row[:18]

        item_id_val   = to_int(item_id)
        page_id_val   = to_int(page_id)
        credits_val   = to_int(cost_credits)
        currency_val  = to_int(cost_points)
        points_type_v = to_int(points_type)
        amount_val    = to_int(amount, 1)
        ltd_stack     = to_int(limited_stack)
        ltd_sells     = to_int(limited_sells)
        ltd_remaining = max(0, ltd_stack - ltd_sells)
        extra_param   = str(extradata or "").strip() or None
        is_visible    = str(have_offer or "1") == "1"
        club_level    = 1 if str(club_only or "0") == "1" else 0
        loc_id        = str(catalog_name or "").strip()

        # currency_type_id holds the activity-point type (0=duckets, 5=diamonds, ...) -- that
        # is what the purchase path debits and what the catalog wire sends to the client. It
        # is NULL only when the offer has no activity-point cost. A zero points_type is NOT
        # "no currency": duckets legitimately use type 0, so it must survive here (the old
        # `or points_type_v == 0` nulled every duckets offer).
        if currency_val == 0:
            currency_type_id = "NULL"
        else:
            if points_type_v not in warn_currency:
                warn_currency.add(points_type_v)
                print(
                    f"  [INFO] points_type={points_type_v} used as currency_type_id -> ensure a "
                    f"currency_types row exists with activity_point_type={points_type_v} "
                    f"(0=duckets, 5=diamonds)",
                    file=sys.stderr,
                )
            currency_type_id = sql_str(points_type_v)

        can_gift    = True
        item_id_list: list[int] = []
        for tid in str(item_ids_raw or "").strip().split(","):
            tid = tid.strip()
            if tid.isdigit():
                item_id_list.append(int(tid))
        if item_id_list and item_id_list[0] in lookup:
            can_gift = lookup[item_id_list[0]]["allow_gift"]

        offer_rows.append(
            f"({sql_str(item_id_val)},{sql_str(page_id_val)},{sql_str(loc_id)},"
            f"{sql_str(credits_val)},{sql_str(currency_val)},"
            f"{currency_type_id},{sql_bool(can_gift)},1,{sql_str(club_level)},"
            f"0,{sql_bool(is_visible)},NOW(),NOW(),NULL)"
        )

        badge_val = str(badge or "").strip() if badge is not None else ""
        if badge_val:
            badge_rows.append(
                f"({sql_str(product_id)},{sql_str(item_id_val)},3,NULL,"
                f"{sql_str(badge_val)},1,0,0,NOW(),NOW(),NULL)"
            )
            product_id += 1

        song_id_v = to_int(song_id)
        if song_id_v:
            song_notes.append(
                f"-- [MANUAL REVIEW] song_id={song_id_v} for offer {item_id_val} — "
                f"Vortex has no direct sound track product_type; adjust manually."
            )

        for def_id in item_id_list:
            canonical_id = id_remap.get(def_id, def_id)
            furni_info   = lookup.get(def_id)
            prod_type    = furni_info["product_type"] if furni_info else 0
            furni_rows.append(
                f"({sql_str(product_id)},{sql_str(item_id_val)},{sql_str(prod_type)},"
                f"{sql_str(canonical_id)},{sql_str(extra_param)},"
                f"{sql_str(amount_val)},{sql_str(ltd_stack)},{sql_str(ltd_remaining)},"
                f"NOW(),NOW(),NULL)"
            )
            product_id += 1

    offer_lines: list[str] = [
        "-- catalog_offers converted from Arcturus catalog_items",
        "-- Generated by tools/catalog_converter/convert.py",
        "",
        "SET NAMES utf8mb4;",
        "SET FOREIGN_KEY_CHECKS = 0;",
        "",
    ]
    write_bulk_inserts(offer_lines, "catalog_offers", OFFER_COLS, offer_rows)
    offers_dst.write_text("\n".join(offer_lines) + "\n", encoding="utf-8")

    product_lines: list[str] = [
        "-- catalog_products converted from Arcturus catalog_items",
        "-- Generated by tools/catalog_converter/convert.py",
        "-- NOTE: currency_type_id references currency_types.id.",
        "--       Insert your currency_types rows first, then update",
        "--       catalog_offers.currency_type_id to match.",
        "",
        "SET NAMES utf8mb4;",
        "SET FOREIGN_KEY_CHECKS = 0;",
        "",
    ]
    write_bulk_inserts(product_lines, "catalog_products", PRODUCT_COLS, badge_rows)
    write_bulk_inserts(product_lines, "catalog_products", PRODUCT_COLS, furni_rows, ignore=True)
    if song_notes:
        product_lines.append("-- Songs requiring manual review:")
        product_lines.extend(song_notes)
    products_dst.write_text("\n".join(product_lines) + "\n", encoding="utf-8")

    valid = len(rows) - skipped
    print(f"  -> {offers_dst.name}: {valid} offers ({skipped} skipped)")
    print(f"  -> {products_dst.name}: {product_id - 1} products")


# ---------------------------------------------------------------------------
# 4. FurnitureData.json → furnidata.xml
# ---------------------------------------------------------------------------
def convert_furnidata_json(src: Path, dst: Path) -> None:
    with open(src, encoding="utf-8", errors="replace") as f:
        data = json.load(f)

    out: list[str] = ['<?xml version="1.0" encoding="UTF-8"?>', "<furnidata>"]

    for section, is_wall in (("roomitemtypes", False), ("wallitemtypes", True)):
        items = data.get(section, {}).get("furnitype", [])
        out.append(f"<{section}>")
        for ft in items:
            cname = xml_escape(str(ft.get("classname", "")))
            out.append(f'<furnitype id="{ft["id"]}" classname="{cname}">')
            out.append(f'<revision>{ft.get("revision", 0)}</revision>')
            out.append(xml_tag("category", str(ft.get("category", ""))))
            if not is_wall:
                out.append(f'<defaultdir>{int(ft.get("defaultdir", 0))}</defaultdir>')
                out.append(f'<xdim>{ft.get("xdim", 1)}</xdim>')
                out.append(f'<ydim>{ft.get("ydim", 1)}</ydim>')
            colors = ft.get("partcolors") or {}
            color_list = colors.get("color", []) if isinstance(colors, dict) else []
            if color_list:
                out.append("<partcolors>")
                for c in color_list:
                    out.append(f"<color>{xml_escape(str(c))}</color>")
                out.append("</partcolors>")
            out.append(xml_tag("name", str(ft.get("name", ""))))
            out.append(xml_tag("description", str(ft.get("description", ""))))
            out.append(xml_tag("adurl", str(ft.get("adurl", ""))))
            out.append(f'<offerid>{ft.get("offerid", -1)}</offerid>')
            out.append(f'<buyout>{xml_bool(ft.get("buyout", False))}</buyout>')
            out.append(f'<rentofferid>{ft.get("rentofferid", -1)}</rentofferid>')
            out.append(f'<rentbuyout>{xml_bool(ft.get("rentbuyout", False))}</rentbuyout>')
            out.append(f'<bc>{xml_bool(ft.get("bc", False))}</bc>')
            out.append(f'<excludeddynamic>{xml_bool(ft.get("excludeddynamic", False))}</excludeddynamic>')
            out.append(xml_tag("customparams", str(ft.get("customparams", ""))))
            out.append(f'<specialtype>{ft.get("specialtype", 1)}</specialtype>')
            if not is_wall:
                out.append(f'<canstandon>{xml_bool(ft.get("canstandon", False))}</canstandon>')
                out.append(f'<cansiton>{xml_bool(ft.get("cansiton", False))}</cansiton>')
                out.append(f'<canlayon>{xml_bool(ft.get("canlayon", False))}</canlayon>')
            out.append(xml_tag("furniline", str(ft.get("furniline", ""))))
            out.append(xml_tag("environment", str(ft.get("environment", ""))))
            out.append(f'<rare>{xml_bool(ft.get("rare", False))}</rare>')
            out.append("</furnitype>")
        out.append(f"</{section}>")

    out.append("</furnidata>")
    dst.write_text("\n".join(out), encoding="utf-8")
    total = sum(len(data.get(s, {}).get("furnitype", [])) for s in ("roomitemtypes", "wallitemtypes"))
    print(f"  -> {dst.name}: {total} furnitype entries written")


# ---------------------------------------------------------------------------
# 5. ProductData.json → productdata.txt
# ---------------------------------------------------------------------------
def convert_productdata_json(src: Path, dst: Path) -> None:
    with open(src, encoding="utf-8", errors="replace") as f:
        data = json.load(f)

    products = data.get("productdata", {}).get("product", [])
    rows = [
        [str(p.get("code", "")), str(p.get("name", "")), str(p.get("description", ""))]
        for p in products
    ]
    dst.write_text(json.dumps(rows, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"  -> {dst.name}: {len(rows)} product entries written")


# ---------------------------------------------------------------------------
# 6. FurnitureData.json → furnidata.json  (AS3 JSON client)
# ---------------------------------------------------------------------------
def convert_furnidata_to_json(src: Path, dst: Path) -> None:
    with open(src, encoding="utf-8", errors="replace") as f:
        data = json.load(f)

    def clean_furnitype(ft: dict, is_wall: bool) -> dict:
        entry: dict = {
            "id":              ft.get("id"),
            "classname":       str(ft.get("classname", "")),
            "revision":        ft.get("revision", 0),
            "category":        str(ft.get("category", "")),
            "name":            str(ft.get("name", "")),
            "description":     str(ft.get("description", "")),
            "offerid":         ft.get("offerid", -1),
            "buyout":          bool(ft.get("buyout", False)),
            "rentofferid":     ft.get("rentofferid", -1),
            "rentbuyout":      bool(ft.get("rentbuyout", False)),
            "bc":              bool(ft.get("bc", False)),
            "excludeddynamic": bool(ft.get("excludeddynamic", False)),
            "customparams":    str(ft.get("customparams", "")),
            "specialtype":     ft.get("specialtype", 1),
            "furniline":       str(ft.get("furniline", "")),
            "environment":     str(ft.get("environment", "")),
            "rare":            bool(ft.get("rare", False)),
        }
        if not is_wall:
            entry["defaultdir"] = int(ft.get("defaultdir", 0))
            entry["xdim"]       = ft.get("xdim", 1)
            entry["ydim"]       = ft.get("ydim", 1)
            entry["canstandon"] = bool(ft.get("canstandon", False))
            entry["cansiton"]   = bool(ft.get("cansiton", False))
            entry["canlayon"]   = bool(ft.get("canlayon", False))
        colors = ft.get("partcolors") or {}
        entry["partcolors"] = colors.get("color", []) if isinstance(colors, dict) else []
        return entry

    output = {
        "roomitemtypes": [
            clean_furnitype(ft, is_wall=False)
            for ft in data.get("roomitemtypes", {}).get("furnitype", [])
        ],
        "wallitemtypes": [
            clean_furnitype(ft, is_wall=True)
            for ft in data.get("wallitemtypes", {}).get("furnitype", [])
        ],
    }

    dst.write_text(json.dumps(output, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    total = len(output["roomitemtypes"]) + len(output["wallitemtypes"])
    print(f"  -> {dst.name}: {total} furnitype entries written")


# ---------------------------------------------------------------------------
# 7. ProductData.json → productdata.json  (AS3 JSON client)
# ---------------------------------------------------------------------------
def convert_productdata_to_json(src: Path, dst: Path) -> None:
    with open(src, encoding="utf-8", errors="replace") as f:
        data = json.load(f)

    products = data.get("productdata", {}).get("product", [])
    output = [
        {"code": str(p.get("code", "")), "name": str(p.get("name", "")), "description": str(p.get("description", ""))}
        for p in products
    ]
    dst.write_text(json.dumps(output, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"  -> {dst.name}: {len(output)} product entries written")


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
def main() -> None:
    global UPSERT
    UPSERT = "--replace" in sys.argv[1:]

    items_base_sql    = INPUT_DIR / "items_base.sql"
    catalog_pages_sql = INPUT_DIR / "catalog_pages.sql"
    catalog_items_sql = INPUT_DIR / "catalog_items.sql"
    furnidata_json    = INPUT_DIR / "FurnitureData.json"
    productdata_json  = INPUT_DIR / "ProductData.json"

    missing = [p for p in (items_base_sql, catalog_pages_sql, catalog_items_sql) if not p.exists()]
    if missing:
        print("Missing input files:", [str(p) for p in missing])
        sys.exit(1)

    if UPSERT:
        print("Mode: --replace (INSERT ... ON DUPLICATE KEY UPDATE) -- re-import updates in place")

    print("Converting items_base ...")
    lookup, id_remap = convert_items_base(items_base_sql, OUTPUT_DIR / "furniture_definitions.sql")

    print("Converting catalog_pages ...")
    convert_catalog_pages(catalog_pages_sql, OUTPUT_DIR / "catalog_pages.sql")

    print("Converting catalog_items ...")
    convert_catalog_items(
        catalog_items_sql,
        OUTPUT_DIR / "catalog_offers.sql",
        OUTPUT_DIR / "catalog_products.sql",
        lookup,
        id_remap,
    )

    if furnidata_json.exists():
        print("Converting FurnitureData.json -> furnidata.xml ...")
        convert_furnidata_json(furnidata_json, OUTPUT_DIR / "furnidata.xml")
        print("Converting FurnitureData.json -> furnidata.json ...")
        convert_furnidata_to_json(furnidata_json, OUTPUT_DIR / "furnidata.json")
    else:
        print(f"  [SKIP] {furnidata_json.name} not found in input/")

    if productdata_json.exists():
        print("Converting ProductData.json -> productdata.txt ...")
        convert_productdata_json(productdata_json, OUTPUT_DIR / "productdata.txt")
        print("Converting ProductData.json -> productdata.json ...")
        convert_productdata_to_json(productdata_json, OUTPUT_DIR / "productdata.json")
    else:
        print(f"  [SKIP] {productdata_json.name} not found in input/")

    print("\nDone. Output files are in:", OUTPUT_DIR)
    print("Import order: furniture_definitions -> catalog_pages -> catalog_offers -> catalog_products")
    print()
    print("Notes:")
    print("  • furniture_definitions: public_name, multiheight, vending_ids, effects dropped")
    print("  • catalog_pages: min_rank, club_only, vip_only, room_id, includes dropped")
    print("  • catalog_offers: order_number dropped (no column in Vortex)")
    print("  • catalog_products: song_id rows marked [MANUAL REVIEW] at end of file")
    print("  • currency_type_id: set to the points_type value as a placeholder;")
    print("    update it after inserting your currency_types rows")


if __name__ == "__main__":
    main()
