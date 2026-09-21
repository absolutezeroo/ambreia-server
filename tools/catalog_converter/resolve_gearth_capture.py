#!/usr/bin/env python3
"""
Resolve a G-Earth catalogue capture against Habbo's own product and furniture data
==================================================================================
The capture carries keys, not text. An offer names a ProductData `code`; a product names a
FurnitureData sprite id. Both tables already sit in ./input/, so the join needs no network.

Input:
    <capture dir>/catalog_tree.json    from catalog_dumper.py
    <capture dir>/catalog_pages.json   idem, the one that carries the offers
    ./input/ProductData.json           code -> name, description
    ./input/FurnitureData.json         sprite id -> classname

Output:
    ./output/catalog_resolved.json

Run: python resolve_gearth_capture.py <capture dir>
     python resolve_gearth_capture.py --selftest
"""

import json
import sys
from pathlib import Path

BASE_DIR = Path(__file__).parent
INPUT_DIR = BASE_DIR / "input"
OUTPUT_DIR = BASE_DIR / "output"

# Wire code of a product -> the FurnitureData section that holds its sprite ids. The two sections
# share 577 ids, so the section is half of the key: id alone resolves to the wrong furni.
FURNI_SECTION = {"s": "roomitemtypes", "i": "wallitemtypes"}


def load_products(path):
    raw = json.loads(path.read_text(encoding="utf-8"))
    products = raw["productdata"]["product"]
    return {p["code"]: {"name": p.get("name", ""), "description": p.get("description", "")}
            for p in products if p.get("code")}


def load_furniture(path):
    raw = json.loads(path.read_text(encoding="utf-8"))
    table = {}
    for code, section in FURNI_SECTION.items():
        for furni in raw.get(section, {}).get("furnitype", []):
            table[(code, furni["id"])] = {
                "classname": furni.get("classname", ""),
                "name": furni.get("name", ""),
                "description": furni.get("description", ""),
            }
    return table


def resolve_product(product, furniture, stats):
    resolved = dict(product)
    section_code = product.get("productType")
    if section_code not in FURNI_SECTION:
        return resolved  # badges, effects, pets and club have no furnidata row by design

    stats["products"] += 1
    match = furniture.get((section_code, product.get("spriteId")))
    if match is None:
        stats["products_unresolved"].append(f"{section_code}:{product.get('spriteId')}")
        return resolved

    stats["products_resolved"] += 1
    resolved["classname"] = match["classname"]
    resolved["furniName"] = match["name"]
    resolved["furniDescription"] = match["description"]
    return resolved


def resolve_offer(offer, products, furniture, stats):
    stats["offers"] += 1
    resolved = dict(offer)
    resolved["products"] = [resolve_product(p, furniture, stats) for p in offer.get("products", [])]

    match = products.get(offer.get("localization"))
    if match is None:
        stats["offers_unresolved"].append(offer.get("localization"))
        return resolved

    stats["offers_resolved"] += 1
    resolved["name"] = match["name"]
    resolved["description"] = match["description"]
    return resolved


def resolve(tree, pages, products, furniture):
    stats = {"offers": 0, "offers_resolved": 0, "offers_unresolved": [],
             "products": 0, "products_resolved": 0, "products_unresolved": []}

    out = {}
    for key, node in tree.items():
        page = dict(node)
        captured = pages.get(str(node.get("pageId")))
        if captured is None:
            page["captured"] = False
        else:
            page["captured"] = True
            page["layoutCode"] = captured.get("layoutCode")
            page["images"] = captured.get("images", [])
            page["texts"] = captured.get("texts", [])
            if "offersUnread" in captured:
                page["offersUnread"] = captured["offersUnread"]
            page["offers"] = [resolve_offer(o, products, furniture, stats)
                              for o in captured.get("offers", [])]
        out[key] = page

    return out, stats


def report(stats, pages_count):
    def pct(done, total):
        return f"{100 * done / total:.1f}%" if total else "n/a"

    print(f"pages           : {pages_count}")
    print(f"offers resolved : {stats['offers_resolved']}/{stats['offers']} ({pct(stats['offers_resolved'], stats['offers'])})")
    print(f"products resolved: {stats['products_resolved']}/{stats['products']} ({pct(stats['products_resolved'], stats['products'])})")

    for label, misses in (("offer code", stats["offers_unresolved"]),
                          ("sprite id", stats["products_unresolved"])):
        if misses:
            unique = list(dict.fromkeys(misses))
            print(f"unresolved {label}s: {len(unique)} distinct, e.g. {unique[:5]}")

    if stats["offers"] and stats["offers_resolved"] == 0:
        print("\nNothing resolved at all: the capture and ProductData.json are probably not the "
              "same hotel, or the pages were captured before the offers parser existed.")


def selftest():
    products = {"xmas_c17_fencecorner": {"name": "Victorian Fence Corner", "description": "Brr."}}
    furniture = {("s", 100): {"classname": "floor_thing", "name": "Floor", "description": ""},
                 ("i", 100): {"classname": "wall_thing", "name": "Wall", "description": ""}}

    tree = {"7": {"pageId": 7, "pageName": "p", "localization": "P", "offerIds": [1], "childrenIds": []}}
    pages = {"7": {"layoutCode": "default_3x3", "images": [], "texts": [], "offers": [{
        "offerId": 1,
        "localization": "xmas_c17_fencecorner",
        "products": [{"productType": "s", "spriteId": 100},
                     {"productType": "i", "spriteId": 100},
                     {"productType": "b", "extraParam": "ACH_X"}],
    }]}}

    out, stats = resolve(tree, pages, products, furniture)
    offer = out["7"]["offers"][0]
    assert offer["name"] == "Victorian Fence Corner", offer
    assert offer["products"][0]["classname"] == "floor_thing", offer["products"][0]
    assert offer["products"][1]["classname"] == "wall_thing", offer["products"][1]
    assert "classname" not in offer["products"][2], "a badge has no furnidata row"
    assert stats["products"] == 2 and stats["products_resolved"] == 2, stats

    missing, stats = resolve(tree, {"7": {"offers": [{"offerId": 1, "localization": "nope",
                                                     "products": []}]}}, products, furniture)
    assert stats["offers_resolved"] == 0 and stats["offers_unresolved"] == ["nope"], stats

    print("selftest: keys resolve, room and wall sprite ids stay apart, misses are counted")


def main():
    if "--selftest" in sys.argv:
        selftest()
        return

    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)

    capture_dir = Path(sys.argv[1])
    tree = json.loads((capture_dir / "catalog_tree.json").read_text(encoding="utf-8"))
    pages = json.loads((capture_dir / "catalog_pages.json").read_text(encoding="utf-8"))
    products = load_products(INPUT_DIR / "ProductData.json")
    furniture = load_furniture(INPUT_DIR / "FurnitureData.json")

    print(f"tables: {len(products)} products, {len(furniture)} furniture entries")

    out, stats = resolve(tree, pages, products, furniture)

    OUTPUT_DIR.mkdir(exist_ok=True)
    destination = OUTPUT_DIR / "catalog_resolved.json"
    destination.write_text(json.dumps(out, ensure_ascii=False, indent=2), encoding="utf-8")

    report(stats, len(out))
    print(f"\nwritten: {destination}")


if __name__ == "__main__":
    main()
