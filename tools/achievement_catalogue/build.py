"""Generate Vortex.Database/Seeds/achievement_catalogue.sql.

Two sources, because neither is enough on its own:

  * the client's `external_flash_texts.txt` says which achievements exist and how many levels
    each one can NAME. `badge_name_ACH_<Name><Level>` is per level; `badge_name_ACH_<Name>`
    carries `%roman%` and covers every level. A level past what the client can name renders in
    the achievement window as the raw key, under a badge image that does not exist either.
  * the Habbox wiki states the progress requirement of each level, which the client never sees:
    it prints `%limit%`, filled from this table.

The wiki names its badge images after the badge code -- `[[Image:ACH RoomEntry1.gif]]` -- so the
join between the two is exact and needs no name matching.

Nothing here is invented. A level whose requirement no source states is not seeded, and the family
stops at the last level that has one.

    python tools/achievement_catalogue/build.py --fetch
    python tools/achievement_catalogue/build.py --texts <path to external_flash_texts.txt>

`--fetch` downloads the wiki pages as raw wikitext into tools/achievement_catalogue/wiki/. Without
it the cached copies are used, so a regeneration is reproducible offline.
"""

import argparse
import collections
import os
import re
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))

DEFAULT_TEXTS = os.path.join(
    ROOT, "..", "vortex-modern-client", "sources", "external_flash_texts.txt"
)
DEFAULT_OUT = os.path.join(ROOT, "Vortex.Database", "Seeds", "achievement_catalogue.sql")

# Wiki page -> the category string to seed. Only values the client has a `quests.<x>.name` key
# for: the category is the tab caption, and one outside that vocabulary shows the raw key. The
# earlier seeds used `avatar` and `build`, neither of which is in it.
PAGES = {
    "Explore_Habbo": "explore",
    "Your_Profile": "identity",
    "Make_Friends": "social",
    "Pets_(Achievements)": "pets",
    "Games_(Achievements)": "games",
    "Build_Your_Room": "room_builder",
    "Music_(Achievements)": "music",
    # Last: a family listed both here and on a live page belongs to the live page.
    "Archive": "archive",
}

BADGE = re.compile(
    r"(?:Image|File):ACH[ _]([A-Za-z][A-Za-z_]*?)_?(\d+)\.(?:gif|png)", re.IGNORECASE
)
BOLD = re.compile(r"'''([^']+)'''")
NUMBER = re.compile(r"(\d[\d,]*)")
WORTH = re.compile(r"worth\s+\d[\d,]*\s+(?:pixels?|credits?|duckets?)", re.IGNORECASE)
UNAVAILABLE = re.compile(r"not yet available", re.IGNORECASE)


def fetch(wiki_dir):
    os.makedirs(wiki_dir, exist_ok=True)
    for page in PAGES:
        url = f"https://habboxwiki.com/{page}?action=raw"
        with urllib.request.urlopen(url, timeout=60) as response:
            body = response.read().decode("utf-8", "replace")
        with open(os.path.join(wiki_dir, page + ".txt"), "w", encoding="utf-8") as handle:
            handle.write(body)
        print(f"  {page:24} {len(body):>7} octets")


def read_client(path):
    """What the client can name: per-level keys, the level-less key, and which show a number."""
    per_level = collections.defaultdict(set)
    generic = set()
    limited = set()
    with open(path, encoding="utf-8", errors="replace") as handle:
        for raw in handle:
            if "=" not in raw:
                continue
            key, value = raw.split("=", 1)
            key, value = key.strip(), value.rstrip("\n")
            found = re.fullmatch(r"badge_name_(?:al_)?ACH_([A-Za-z]+?)(\d+)", key)
            if found:
                per_level[found.group(1)].add(int(found.group(2)))
                continue
            found = re.fullmatch(r"badge_name_(?:al_)?ACH_([A-Za-z]+)", key)
            if found:
                generic.add(found.group(1))
                continue
            found = re.fullmatch(r"badge_desc_(?:al_)?ACH_([A-Za-z]+?)\d*", key)
            if found and "%limit%" in value:
                limited.add(found.group(1))
    return per_level, generic, limited


def requirement(line, family, level, limited):
    """The level's progress requirement, or None when no source states it."""
    if UNAVAILABLE.search(line):
        return None

    # The pixel reward is not a requirement, and on many lines it is the only other number.
    text = WORTH.sub("", line)

    for bold in BOLD.findall(text):
        found = NUMBER.search(bold)
        if found:
            return int(found.group(1).replace(",", ""))

    # Unbolded ("For adding 5 tags to your Habbo"): trust it only when there is one number.
    description = text.split("||")[-1]
    numbers = NUMBER.findall(description)
    if len(numbers) == 1:
        return int(numbers[0].replace(",", ""))

    # No quantity at all, and the client's description shows none either: a "do it once" badge.
    if not numbers and level == 1 and family not in limited:
        return 1

    return None


def read_wiki(wiki_dir, limited):
    levels = collections.defaultdict(dict)
    category = {}
    for page, cat in PAGES.items():
        path = os.path.join(wiki_dir, page + ".txt")
        if not os.path.exists(path):
            continue
        with open(path, encoding="utf-8", errors="replace") as handle:
            for line in handle:
                found = BADGE.search(line)
                if not found:
                    continue
                family, level = found.group(1), int(found.group(2))
                # A page lists some badges twice (current wording and archived); keep the first
                # statement that carries a number.
                if levels[family].get(level) is None:
                    levels[family][level] = requirement(line, family, level, limited)
                category.setdefault(family, cat)
    return levels, category


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--texts", default=DEFAULT_TEXTS)
    parser.add_argument("--wiki", default=os.path.join(HERE, "wiki"))
    parser.add_argument("--out", default=DEFAULT_OUT)
    parser.add_argument("--fetch", action="store_true", help="re-download the wiki pages")
    args = parser.parse_args()

    if args.fetch:
        print("telechargement du wikitext :")
        fetch(args.wiki)

    client_levels, client_generic, client_limited = read_client(args.texts)
    wiki_levels, wiki_category = read_wiki(args.wiki, client_limited)

    # The wiki spells a few image names in the wrong case. The badge code is both a localization
    # key and an image name, so the client's spelling is the one that resolves.
    canonical = {family.lower(): family for family in set(client_levels) | client_generic}
    known = set(canonical.values())
    renamed, unknown = {}, []
    for family in list(wiki_levels):
        if family in known:
            continue
        fixed = canonical.get(family.lower())
        if fixed is None:
            unknown.append(family)
            del wiki_levels[family]
            wiki_category.pop(family, None)
            continue
        renamed[family] = fixed
        wiki_levels[fixed] = {**wiki_levels.pop(family), **wiki_levels.get(fixed, {})}
        wiki_category.setdefault(fixed, wiki_category.pop(family))

    if renamed:
        print("casse reprise du client :", renamed)
    if unknown:
        print("familles du wiki inconnues du client, ecartees :", unknown)

    rows, dropped = [], []
    for family in sorted(wiki_levels):
        # None = the level-less key exists, so any level renders.
        nameable = (
            None
            if family in client_generic
            else (max(client_levels[family]) if client_levels.get(family) else 0)
        )
        run, previous = [], 0
        for level in range(1, max(wiki_levels[family]) + 1):
            value = wiki_levels[family].get(level)
            # A gap, or a requirement that does not grow, ends the family: the calculator walks
            # these in order and a flat step would complete two levels at once.
            if value is None or value <= previous:
                break
            if nameable is not None and level > nameable:
                break
            run.append((level, value))
            previous = value

        if not run:
            dropped.append((family, max(wiki_levels[family])))
            continue

        rows.extend(
            (family, wiki_category[family], level, value, 10 * level, 0, level)
            for level, value in run
        )

    families = sorted({row[0] for row in rows})
    no_page = len((set(client_levels) | client_generic) - set(wiki_levels))

    print(f"familles retenues : {len(families)}   niveaux : {len(rows)}")
    for family, wiki_max in dropped:
        print(f"   ecartee (aucun palier connu) : {family} (wiki_max={wiki_max})")
    print(f"familles du client sans page wiki : {no_page}")

    values = ",\n".join(
        f"  ('{f}', '{c}', {lv}, 'ACH_{f}{lv}', {req}, {ra}, {rt}, {sp})"
        for f, c, lv, req, ra, rt, sp in rows
    )

    with open(args.out, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(HEADER.format(
            dropped=len(dropped),
            no_page=no_page,
            families=len(families),
            levels=len(rows),
            values=values,
        ))
        handle.write(BODY)

    print(f"-> {args.out}")


HEADER = """-- The achievement catalogue. GENERATED by tools/achievement_catalogue/build.py -- regenerate
-- it rather than editing here.
--
-- Two sources, because neither is enough on its own:
--
--   * the client's external_flash_texts says which achievements exist and how many levels each
--     one can NAME. `badge_name_ACH_<Name><Level>` is per level; `badge_name_ACH_<Name>` carries
--     `%roman%` and covers every level. A level past what the client can name renders as the raw
--     key -- which is what the two earlier seeds shipped, having given all 35 of their families
--     five levels each, a figure taken from nothing.
--   * the Habbox wiki states the progress requirement of each level, which the client never sees:
--     it prints `%limit%`, filled from this table. Its badge images are named after the badge
--     code ([[Image:ACH RoomEntry1.gif]]), so the join is exact.
--
-- Nothing here is invented. A level whose requirement no source states is not seeded and the
-- family stops there; {dropped} families are left out entirely for that reason, and {no_page} more exist in
-- the client with no wiki page at all -- among them GamePlayed, which keeps whatever it has.
-- Requirements are cumulative and strictly increasing, which is what
-- AchievementProgressCalculator.ApplyProgress walks.
--
-- Rewards follow the convention the wiki confirms wherever it states one ("Worth 10 pixels" on a
-- level 1): 10 pixels per level, one score point per level. Both are DB-editable.
--
-- The category is the tab caption: the client resolves `quests.<category>.name`, so a value
-- outside that vocabulary shows the raw key -- `avatar` and `build`, from the earlier seeds, are
-- not in it and are corrected here.
--
-- {families} achievements, {levels} levels.

CREATE TEMPORARY TABLE `vortex_achievement_seed` (
  `name` VARCHAR(64) NOT NULL,
  `category` VARCHAR(32) NOT NULL,
  `level` INT NOT NULL,
  `badge_code` VARCHAR(64) NOT NULL,
  `progress_requirement` INT NOT NULL,
  `reward_amount` INT NOT NULL,
  `reward_type` INT NOT NULL,
  `score_points` INT NOT NULL,
  PRIMARY KEY (`name`, `level`)
);

INSERT INTO `vortex_achievement_seed`
  (`name`, `category`, `level`, `badge_code`, `progress_requirement`, `reward_amount`, `reward_type`, `score_points`)
VALUES
{values};
"""

BODY = """
-- 1. The achievements themselves, and the category of the ones already seeded.
INSERT IGNORE INTO `achievements` (`name`, `category`, `display_method`)
SELECT DISTINCT `name`, `category`, 0 FROM `vortex_achievement_seed`;

UPDATE `achievements` `a`
  JOIN (SELECT DISTINCT `name`, `category` FROM `vortex_achievement_seed`) `s`
    ON `s`.`name` = `a`.`name`
   SET `a`.`category` = `s`.`category`
 WHERE `a`.`category` <> `s`.`category`;

-- 2. Take back the badges of levels this catalogue does not have. The badge goes before the level
--    row does, because it is matched through it.
DELETE `b`
  FROM `player_badges` `b`
  JOIN `achievement_levels` `l` ON `l`.`badge_code` = `b`.`badge_code`
  JOIN `achievements` `a` ON `a`.`id` = `l`.`achievement_id`
  JOIN (SELECT `name`, MAX(`level`) AS `top` FROM `vortex_achievement_seed` GROUP BY `name`) `s`
    ON `s`.`name` = `a`.`name`
 WHERE `l`.`level` > `s`.`top`;

-- 3. Cap what each player holds at what still exists. Never demotes: the thresholds change here,
--    and taking back a level somebody cleared under the old ones is not this seed's business.
UPDATE `player_achievements` `pa`
  JOIN `achievements` `a` ON `a`.`id` = `pa`.`achievement_id`
  JOIN (SELECT `name`, MAX(`level`) AS `top` FROM `vortex_achievement_seed` GROUP BY `name`) `s`
    ON `s`.`name` = `a`.`name`
   SET `pa`.`level` = LEAST(`pa`.`level`, `s`.`top`);

-- 4. Replace the levels. player_achievements keys on the achievement and a level NUMBER, never on
--    a level row, so these can be rebuilt wholesale.
DELETE `l`
  FROM `achievement_levels` `l`
  JOIN `achievements` `a` ON `a`.`id` = `l`.`achievement_id`
  JOIN (SELECT DISTINCT `name` FROM `vortex_achievement_seed`) `s` ON `s`.`name` = `a`.`name`;

INSERT INTO `achievement_levels`
  (`achievement_id`, `level`, `badge_code`, `progress_requirement`, `reward_amount`, `reward_type`, `score_points`)
SELECT `a`.`id`, `s`.`level`, `s`.`badge_code`, `s`.`progress_requirement`,
       `s`.`reward_amount`, `s`.`reward_type`, `s`.`score_points`
  FROM `vortex_achievement_seed` `s`
  JOIN `achievements` `a` ON `a`.`name` = `s`.`name`;

-- 5. Pull each bar up to the level its owner holds. A bar behind its own level reads as broken,
--    and the real requirements are mostly higher than the invented ones they replace.
UPDATE `player_achievements` `pa`
  JOIN `achievement_levels` `l`
    ON `l`.`achievement_id` = `pa`.`achievement_id`
   AND `l`.`level` = `pa`.`level`
   SET `pa`.`progress` = GREATEST(`pa`.`progress`, `l`.`progress_requirement`);

-- 6. Hand back the badge of the level each player now stands at. A level-up swaps the badge
--    rather than adding one, so the one taken back in step 2 was the only one they had.
INSERT IGNORE INTO `player_badges` (`player_id`, `badge_code`, `slot_id`)
SELECT `pa`.`player_id`, `l`.`badge_code`, 0
  FROM `player_achievements` `pa`
  JOIN `achievement_levels` `l`
    ON `l`.`achievement_id` = `pa`.`achievement_id`
   AND `l`.`level` = `pa`.`level`;

-- 7. Recompute the achievement score: the level rows it was accumulated from are gone.
UPDATE `players` `p`
   SET `p`.`achievement_score` = COALESCE((
           SELECT SUM(`l`.`score_points`)
             FROM `player_achievements` `pa`
             JOIN `achievement_levels` `l`
               ON `l`.`achievement_id` = `pa`.`achievement_id`
              AND `l`.`level` <= `pa`.`level`
            WHERE `pa`.`player_id` = `p`.`id`
       ), 0);

DROP TEMPORARY TABLE `vortex_achievement_seed`;
"""


if __name__ == "__main__":
    main()
