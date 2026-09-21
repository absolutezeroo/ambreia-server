#!/usr/bin/env python3
"""
Extract the catalogue's display text into external_catalog_text.json
====================================================================
The client shows a caption verbatim unless it has the form `${key}` -- TextLabelController tests
`charAt(0) === '$' && charAt(1) === '{'` and nothing else. So a catalogue that is to be translated
holds keys in the database and words in a texts file, which is what this produces.

The key is the page's own `localization` slug, which is what that column is for. Two pages that
share a slug **and** a title share a key on purpose: the same word is translated once. What is not
allowed is a slug covering different titles -- this refuses to generate then, and writes the repair
instead, because picking one of the titles would silently rename the other pages.

Input:
    a mysqldump holding `catalog_pages` (id, localization, name, text_data, deleted_at)

Output, when every slug is unambiguous:
    external_catalog_text.json   the keys and today's English text
    catalog_texts.sql            points the rows at those keys

Output, when some are not:
    catalog_slug_conflicts.sql   a proposed unique slug per offending row, to review and run first

Run: python extract_catalog_texts.py <dump.sql> [--out <dir>]
"""

import argparse
import collections
import json
import re
import sys
from pathlib import Path

COLUMNS = ["id", "parent_id", "localization", "name", "icon", "layout", "image_data",
           "text_data", "sort_order", "visible", "created_at", "updated_at", "deleted_at",
           "catalog_type"]

# `localization` is varchar(50) and carries the token, not the bare key.
MAX_TOKEN = 50


def rows_of(sql):
    """Every catalog_pages row. Hand-tokenised: text_data holds HTML with commas and quotes in it,
    which is precisely what a regex over the VALUES list gets wrong."""
    rows = []

    for block in re.findall(r"INSERT INTO `catalog_pages`.*?VALUES\s*(.*?);\s*(?:\n|$)", sql, re.S):
        i, row, field, in_string = 0, [], [], False

        while i < len(block):
            c = block[i]

            if in_string:
                if c == "\\":
                    field.append(block[i:i + 2])
                    i += 2
                    continue
                if c == "'":
                    in_string = False
                else:
                    field.append(c)
            elif c == "'":
                in_string = True
            elif c == "(":
                row, field = [], []
            elif c in ",)":
                raw = "".join(field).strip()
                row.append(None if raw == "NULL" else raw)
                field = []
                if c == ")":
                    rows.append(row)
                    row = []
            else:
                field.append(c)

            i += 1

    return [dict(zip(COLUMNS, r)) for r in rows if len(r) == len(COLUMNS)]


def unescape(value):
    if value is None:
        return ""
    return (value.replace("\\'", "'").replace('\\"', '"')
                 .replace("\\n", "\n").replace("\\r", "\r").replace("\\\\", "\\"))


def sql_quote(value):
    return "'" + value.replace("\\", "\\\\").replace("'", "\\'") + "'"


def slugify(text):
    slug = re.sub(r"[^a-z0-9]+", "_", text.lower()).strip("_")
    return slug or "page"


def blurbs_of(row):
    try:
        parsed = json.loads(unescape(row["text_data"]) or "[]")
    except (ValueError, TypeError):
        return []
    return [str(b) for b in parsed] if isinstance(parsed, list) else []


def conflicts(pages):
    """Slugs whose rows disagree about what they say. A shared slug is fine; a shared slug over two
    different titles is a page that would be renamed by whichever row happened to be written last."""
    by_slug = collections.defaultdict(list)

    for row in pages:
        by_slug[unescape(row["localization"]).strip()].append(row)

    bad = {}

    for slug, rows in by_slug.items():
        titles = {unescape(r["name"]).strip() for r in rows}
        texts = {tuple(blurbs_of(r)) for r in rows}

        if not slug:
            bad[slug or "<empty>"] = (rows, "the slug is empty")
        elif len(titles) > 1:
            bad[slug] = (rows, f"{len(titles)} different titles")
        elif len(texts) > 1:
            bad[slug] = (rows, f"{len(texts)} different blurb sets")
        elif slug.isdigit():
            # Unique, so nothing would break -- but `catalog.page.11` for "Windows" is a key a
            # translator cannot place, which is the whole reason for keying on the slug.
            bad[slug] = (rows, "the slug is a bare number")

    return by_slug, bad


def repair(bad, taken, parent_slug=None):
    """One proposed slug per offending row, derived from its own title so it stays readable."""
    statements = []
    parent_slug = parent_slug or {}

    for slug, (rows, _) in sorted(bad.items()):
        for row in rows:
            # Derived from the row's own title, so the slug says what the page is. Two rows with
            # the same title differ only by a counter: prefixing the old slug produced things like
            # `habbo_club_habbo_club`, which names nothing.
            base = slugify(unescape(row["name"]))

            # A page titled "2013" derives the slug `2013`, which is the bare number we are trying
            # to get rid of -- it would be flagged again on the next run, forever. The parent names
            # it instead, which is also the thing a translator needs to tell two "2013" apart.
            if base.isdigit():
                parent = parent_slug.get(row["parent_id"])
                base = f"{parent}_{base}" if parent and not parent.isdigit() else f"page_{base}"

            candidate = base
            suffix = 2

            while candidate in taken:
                candidate = f"{base}_{suffix}"
                suffix += 1

            taken.add(candidate)
            statements.append(
                f"UPDATE catalog_pages SET localization = {sql_quote(candidate)} "
                f"WHERE id = {row['id']};   -- was {slug!r}, {unescape(row['name'])!r}"
            )

    return statements


def extract(by_slug):
    texts, statements = {}, []

    for slug, rows in sorted(by_slug.items()):
        title = unescape(rows[0]["name"]).strip()
        updates = []

        # Habbo's own naming, not ours: external_flash_texts already ships 94 keys in this exact
        # shape -- `catalog.page.bundle_pirates` for the caption, `.text_0` and `.image_0` beside
        # it. Reusing it means a page whose slug the hotel already knows inherits the official
        # wording instead of being given a second, competing key for the same thing.
        key = f"catalog.page.{slug}"

        if title:
            texts[key] = title
            updates.append(("localization", "${" + key + "}"))

        blurbs = blurbs_of(rows[0])

        if any(b.strip() for b in blurbs):
            tokens = []
            for index, blurb in enumerate(blurbs):
                if not blurb.strip():
                    tokens.append(blurb)
                    continue
                texts[f"{key}.text_{index}"] = blurb
                tokens.append("${" + f"{key}.text_{index}" + "}")
            updates.append(("text_data", json.dumps(tokens, ensure_ascii=False)))

        if not updates:
            continue

        assignments = ", ".join(f"{column} = {sql_quote(value)}" for column, value in updates)
        ids = ", ".join(str(r["id"]) for r in rows)
        statements.append(f"UPDATE catalog_pages SET {assignments} WHERE id IN ({ids});")

    return texts, statements


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("dump")
    parser.add_argument("--out", default=".")
    parser.add_argument("--known", help="an existing texts file, to report which keys it already "
                                        "carries and where the wording differs")
    parser.add_argument("--repair", action="store_true",
                        help="apply the proposed slug repair in memory and emit the texts anyway. "
                             "The texts file is then only correct once the repair SQL has been run "
                             "against the database -- both are written from the same decision.")
    args = parser.parse_args()

    rows = rows_of(Path(args.dump).read_text(encoding="utf-8", errors="replace"))

    if not rows:
        print("no catalog_pages rows found -- is this the right dump?", file=sys.stderr)
        sys.exit(1)

    pages = [r for r in rows if not r["deleted_at"]]
    by_slug, bad = conflicts(pages)

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    print(f"rows       : {len(rows)} ({len(rows) - len(pages)} soft-deleted, skipped)")
    print(f"slugs      : {len(by_slug)} over {len(pages)} pages")

    if bad:
        parent_slug = {r["id"]: unescape(r["localization"]).strip() for r in pages}
        statements = repair(bad, set(by_slug) - set(bad), parent_slug)
        path = out / "catalog_slug_conflicts.sql"
        path.write_text(
            "-- One slug, several pages that do not say the same thing. A key cannot serve both:\n"
            "-- whichever row is written last would rename the others. Review these, run them, then\n"
            "-- re-run the extractor.\n\n" + "\n".join(statements) + "\n",
            encoding="utf-8",
        )

        verb = "REPAIRING" if args.repair else "REFUSED"
        print(f"\n{verb}: {len(bad)} slug(s) unusable as keys, {len(statements)} rows affected\n")
        for slug, (affected, why) in sorted(bad.items(), key=lambda kv: -len(kv[1][0]))[:10]:
            print(f"  {slug:<22} {len(affected):>2} pages, {why}")
        if len(bad) > 10:
            print(f"  ...and {len(bad) - 10} more")
        print(f"\nrepair written to {path.resolve()}")

        if not args.repair:
            sys.exit(2)

        # Replay it over the rows we hold, so the texts file and the repair describe one state.
        renames = dict(re.findall(r"localization = '([^']*)' WHERE id = (\d+);", "\n".join(statements)))
        renames = {page_id: slug for slug, page_id in renames.items()}
        for row in pages:
            if row["id"] in renames:
                row["localization"] = renames[row["id"]]

        by_slug, still_bad = conflicts(pages)

        if still_bad:
            print(f"\nthe repair did not settle {len(still_bad)} slug(s) -- refusing", file=sys.stderr)
            sys.exit(2)

        print(f"repair applied in memory: {len(by_slug)} slugs, all usable\n")

    texts, statements = extract(by_slug)

    (out / "external_catalog_text.json").write_text(
        json.dumps(texts, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    (out / "catalog_texts.sql").write_text(
        "-- Generated by tools/catalog_converter/extract_catalog_texts.py\n"
        "-- Points every catalogue caption and blurb at its key in external_catalog_text.json.\n"
        "-- Re-runnable: absolute values, so a second run leaves the same rows.\n\n"
        + "\n".join(statements) + "\n",
        encoding="utf-8",
    )

    titles = sum(1 for k in texts if ".text_" not in k)
    # Only the caption token goes into a varchar(50); the blurb tokens live in longtext.
    longest = max((len("${" + k + "}") for k in texts if ".text_" not in k), default=0)
    print(f"keys       : {len(texts)} ({titles} titles, {len(texts) - titles} blurbs)")
    print(f"statements : {len(statements)}")
    print(f"longest caption token: {longest} chars against varchar({MAX_TOKEN})")

    known = Path(args.known) if args.known else None
    if known and known.is_file():
        existing = json.loads(known.read_text(encoding="utf-8"))
        shared = sorted(set(texts) & set(existing))
        agree = [k for k in shared if existing[k] == texts[k]]
        print(f"already in {known.name}: {len(shared)} of our keys "
              f"({len(agree)} say the same thing, {len(shared) - len(agree)} differ)")
        for k in [k for k in shared if existing[k] != texts[k]][:5]:
            print(f"   {k}\n     ours     {texts[k]!r}\n     existing {existing[k]!r}")

    if longest > MAX_TOKEN:
        over = [k for k in texts if len("${" + k + "}") > MAX_TOKEN]
        print(f"\nREFUSED: {len(over)} token(s) do not fit the column: {over[:3]}", file=sys.stderr)
        sys.exit(2)

    print(f"\nwritten to {out.resolve()}")


if __name__ == "__main__":
    main()
