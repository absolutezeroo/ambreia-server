#!/usr/bin/env python3
"""
Translate our external_flash_texts from an official Habbo texts file
====================================================================
Takes the keys we already have and gives them the official wording for a language. It never adds a
key: habbo.fr publishes 52 183 of them against our 13 027, and importing the rest would ship forty
thousand strings for features this hotel does not have.

Ours wins wherever the official file has nothing to say -- custom badges, NFT content, our own
catalogue pages -- so a key we invented keeps its value instead of disappearing.

Input:
    ours      the file we serve, JSON  ({"key": "value"})
    official  a Habbo gamedata texts file, `key=value` per line, UTF-8

Output:
    the same JSON with the same keys, translated where the official file knows them

Run:
    python merge_official_texts.py ours.json official.txt -o merged.json
    python merge_official_texts.py ours.json official.txt -o merged.json --keep catalog.page. hotel.
"""

import argparse
import collections
import json
import re
import sys
from pathlib import Path


def parse_official(path):
    """`key=value` lines. A value may itself contain `=`, so only the first one splits."""
    texts = {}

    for line in path.read_text(encoding="utf-8").split("\n"):
        line = line.rstrip("\r")

        if not line or line.startswith("#") or "=" not in line:
            continue

        key, _, value = line.partition("=")
        # The file escapes newlines; the client unescapes them when it parses, so we do too.
        texts[key.strip()] = value.replace("\\n", "\n")

    return texts


def merge(ours, official, keep_prefixes):
    merged, changed, kept = {}, [], []

    for key, value in ours.items():
        if any(key.startswith(p) for p in keep_prefixes):
            merged[key] = value
            if key in official and official[key] != value:
                kept.append(key)
            continue

        if key in official and official[key] != value:
            merged[key] = official[key]
            changed.append(key)
        else:
            merged[key] = value

    return merged, changed, kept


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("ours")
    parser.add_argument("official")
    parser.add_argument("-o", "--out", required=True)
    parser.add_argument("--delta", help="also write only the keys this translated. That is what a "
                                        "gamedata/<code>/ file should hold: the client loads the "
                                        "root file first and merges the language over it, so a "
                                        "language ships what it translates and nothing else.")
    parser.add_argument("--keep", nargs="*", default=[],
                        help="key prefixes to leave untranslated, for families where the official "
                             "wording is about Habbo's hotel and not ours")
    args = parser.parse_args()

    ours = json.loads(Path(args.ours).read_text(encoding="utf-8"))
    official = parse_official(Path(args.official))

    if not official:
        print("the official file parsed to nothing -- wrong format?", file=sys.stderr)
        sys.exit(1)

    merged, changed, kept = merge(ours, official, args.keep)

    if set(merged) != set(ours):
        print("BUG: the key set moved, refusing to write", file=sys.stderr)
        sys.exit(2)

    Path(args.out).write_text(json.dumps(merged, ensure_ascii=False, indent=2), encoding="utf-8")

    if args.delta:
        delta = {k: merged[k] for k in changed}
        Path(args.delta).write_text(json.dumps(delta, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"delta      : {len(delta)} keys -> {Path(args.delta).resolve()}")

    untouched = len(ours) - len(changed)
    print(f"ours       : {len(ours)} keys")
    print(f"official   : {len(official)} keys")
    print(f"translated : {len(changed)}")
    print(f"left as is : {untouched}  ({len(set(ours) - set(official))} the official file does not have)")

    if kept:
        print(f"kept by --keep: {len(kept)} that would otherwise have changed")

    families = collections.Counter(re.split(r"[._]", k)[0] for k in changed)
    print("\ntranslated by family:")
    for family, count in families.most_common(10):
        print(f"   {family:<22} {count}")

    print(f"\nwritten to {Path(args.out).resolve()}")


if __name__ == "__main__":
    main()
