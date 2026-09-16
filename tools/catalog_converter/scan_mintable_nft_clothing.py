#!/usr/bin/env python3
"""
Mintable NFT-clothing scanner
=============================
Writes ``Vortex.Database/Seeds/nft_mintable_clothing.sql``: which clothing furni may be converted
into a Relic in the Collectors Guild.

Why this exists
---------------
``nft_mintable_item_types`` ships EMPTY, and nothing seeds it -- it is written only by the dashboard,
one row at a time. That is faithful to Habbo, where what is convertible is an operator's decision.
It also means the Collectors Guild's minting half shows an empty list on a fresh hotel, and typing
459 rows into an admin form is not a decision anybody makes deliberately.

The criterion is the classname prefix ``clothing_nft``. That is Habbo's own marking for artwork tied
to their NFT collections, and it is the only signal in the shipped data that separates those pieces
from the other 2 100 clothing furni. It is a starting point an admin can then curate: every row is
editable and deletable from the dashboard, and `enabled` turns one off without losing it.

WHAT THIS DOES NOT DECIDE
-------------------------
Whether a player can afford it. Minting costs stamps, stamps are bought with silver, and silver
arrives through the shop or an admin grant. ``nft_mint_token_offers.sql`` seeds the stamp bundles;
without it this list is priced in a currency nobody can obtain, which is worse than an empty tab
because it looks like something that should work.

Prices and the window are ROUND NUMBERS, not recovered values
-------------------------------------------------------------
Habbo's own stamp price for any given item is not in the shipped assets, so nothing here is a port
of it. One stamp per conversion, an open edition, and a window that closes in 2099 -- which is how
"no deadline" has to be expressed, because ``ends_at`` is required and the client disables the
collect button the moment it passes. All of it is admin-editable.

Usage
-----
    python tools/catalog_converter/scan_mintable_nft_clothing.py [assets_gamedata_dir]

Defaults to the local Laragon asset root. Idempotent: the output is INSERT IGNORE against a unique
``product_code``, so re-running it never doubles a row and never overwrites an admin's edits.
"""

import io
import json
import os
import sys

DEFAULT_GAMEDATA = r"C:\Laragon\www\vortex-assets\gamedata"

PREFIX = "clothing_nft"

# One stamp a piece. The point of the number is that it is uniform and obviously a placeholder:
# an operator who wants a rare piece to cost more edits that one row rather than reasoning about
# why the generator gave it 7.
STAMP_PRICE = 1

# Required, and therefore a real date rather than NULL. Far enough away to mean "open", close
# enough to be a date and not an overflow: the client multiplies both times by 1000 before
# comparing them to a clock.
OPENS_AT = "2020-01-01 00:00:00"
CLOSES_AT = "2099-12-31 23:59:59"

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUTPUT = os.path.join(REPO_ROOT, "Vortex.Database", "Seeds", "nft_mintable_clothing.sql")


def load_classnames(gamedata_dir):
    """Every distinct ``clothing_nft*`` classname in the shipped furnidata, sorted."""
    with io.open(
        os.path.join(gamedata_dir, "furnidata_json.json"), "r", encoding="utf-8"
    ) as handle:
        data = json.load(handle)

    items = []

    for section in ("roomitemtypes", "wallitemtypes"):
        items.extend(data.get(section, {}).get("furnitype", []))

    # A set: 162 classnames appear more than once in the furnidata itself, and product_code is
    # unique in the target table. Sorted so a regeneration produces the same file byte for byte.
    return sorted(
        {
            item["classname"]
            for item in items
            if item.get("classname", "").startswith(PREFIX)
        }
    )


def write_seed(classnames):
    with io.open(OUTPUT, "w", encoding="utf-8", newline="\n") as out:
        out.write(
            "-- Clothing furni that may be converted into a Relic, taken from the shipped assets.\n"
            "--\n"
            "-- Regenerate with tools/catalog_converter/scan_mintable_nft_clothing.py. Do not\n"
            "-- hand-edit: an operator's changes belong in the dashboard, which writes this same\n"
            "-- table, and this file is INSERT IGNORE so it never overwrites them.\n"
            "--\n"
            "-- Selected by the classname prefix 'clothing_nft', which is Habbo's own marking for\n"
            "-- artwork tied to their NFT collections and the only signal in the shipped data that\n"
            "-- separates them from the other clothing furni.\n"
            "--\n"
            "-- The prices and the window are round placeholders and not recovered values: Habbo's\n"
            "-- own stamp price for a given item is in none of the shipped assets. One stamp, an\n"
            "-- open edition, and a window that closes in 2099 -- which is how 'no deadline' has to\n"
            "-- be written, because ends_at is required and the client disables the collect button\n"
            "-- the moment it passes.\n"
            "--\n"
            "-- Minting also costs stamps the player has to own: see nft_mint_token_offers.sql,\n"
            "-- without which this list is priced in a currency nobody can obtain.\n"
            "--\n"
            "-- %d classnames.\n"
            "\n" % len(classnames)
        )

        out.write(
            "INSERT IGNORE INTO `nft_mintable_item_types`\n"
            "  (`product_code`, `stamp_price`, `starts_at`, `ends_at`, `region_locked`,\n"
            "   `limited_edition`, `edition_size`, `enabled`, `sort_order`,\n"
            "   `created_at`, `updated_at`)\n"
            "VALUES\n"
        )

        rows = []

        for index, name in enumerate(classnames):
            rows.append(
                "  ('%s', %d, '%s', '%s', 0, 0, 0, 1, %d, UTC_TIMESTAMP(), UTC_TIMESTAMP())"
                % (name.replace("'", "''"), STAMP_PRICE, OPENS_AT, CLOSES_AT, index)
            )

        out.write(",\n".join(rows))
        out.write(";\n")


def main():
    gamedata_dir = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_GAMEDATA

    if not os.path.isdir(gamedata_dir):
        raise SystemExit("No gamedata directory at %s" % gamedata_dir)

    classnames = load_classnames(gamedata_dir)

    if not classnames:
        raise SystemExit(
            "No '%s*' classnames in the furnidata at %s; refusing to write an empty seed."
            % (PREFIX, gamedata_dir)
        )

    write_seed(classnames)

    print("%d mintable clothing types -> %s" % (len(classnames), OUTPUT))


if __name__ == "__main__":
    main()
