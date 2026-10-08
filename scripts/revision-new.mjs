#!/usr/bin/env node
// Draws a production revision: every id of Vortex.Revisions/Revision20260701/Headers.cs re-drawn at
// random, written to Vortex.Revisions/Releases/AMBREIA-<UTC yyyyMMddHHmm>.json, which the server
// embeds and loads in production (ReleaseRevision) and the client's production build inlines.
// Spec: vortex-rpg-client docs/superpowers/specs/2026-10-08-random-header-revisions-design.md.
import {existsSync, mkdirSync, readFileSync, writeFileSync} from 'node:fs';
import {randomInt} from 'node:crypto';
import {dirname, join, resolve} from 'node:path';
import {fileURLToPath} from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const HEADERS = join(ROOT, 'Vortex.Revisions', 'Revision20260701', 'Headers.cs');
const RELEASES = join(ROOT, 'Vortex.Revisions', 'Releases');

// Read before the server knows the client's table / must reach a client on any table.
const FIXED = {events: 'ClientHelloMessageEvent', composers: 'DisconnectReasonComposer'};
const FIXED_ID = 4000;
const MAX_ID = 3999;

const CLASS_RE = /^\s*(?:internal|public)\s+static\s+class\s+(MessageEvent|MessageComposer)\b/;
const CONST_RE = /^\s*public\s+const\s+int\s+(\w+)\s*=/;

export function readHeaderNames(text)
{
    const names = {events: [], composers: []};
    let side = null;

    for(const line of text.split(/\r?\n/))
    {
        const cls = CLASS_RE.exec(line);

        if(cls) side = cls[1] === 'MessageEvent' ? 'events' : 'composers';

        const constant = CONST_RE.exec(line);

        if(constant && side) names[side].push(constant[1]);
    }

    return names;
}

export function revisionId(date)
{
    const pad = (value) => String(value).padStart(2, '0');

    return `AMBREIA-${date.getUTCFullYear()}${pad(date.getUTCMonth() + 1)}${pad(date.getUTCDate())}${pad(date.getUTCHours())}${pad(date.getUTCMinutes())}`;
}

/** `draw(min, max)` returns an integer in [min, max): crypto.randomInt by default. */
export function drawRelease(names, date = new Date(), draw = randomInt)
{
    const release = {revision: revisionId(date), events: {}, composers: {}};

    for(const side of ['events', 'composers'])
    {
        // Fisher–Yates over 1..3999, then one id per name: distinct by construction.
        const pool = Array.from({length: MAX_ID}, (_, i) => i + 1);

        for(let i = pool.length - 1; i > 0; i--)
        {
            const j = draw(0, i + 1);

            [pool[i], pool[j]] = [pool[j], pool[i]];
        }

        let next = 0;

        for(const name of names[side])
        {
            release[side][name] = name === FIXED[side] ? FIXED_ID : pool[next++];
        }
    }

    return release;
}

/** Writes `<dir>/<revision>.json` and returns its path; never replaces an existing revision. */
export function writeRelease(release, dir)
{
    const target = join(dir, `${release.revision}.json`);

    // Two draws in one minute would share a name; the first may already be deployed.
    if(existsSync(target)) throw new Error(`${target} already exists. Wait a minute and draw again.`);

    mkdirSync(dir, {recursive: true});
    writeFileSync(target, `${JSON.stringify(release, null, 2)}\n`);

    return target;
}

if(process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url))
{
    const release = drawRelease(readHeaderNames(readFileSync(HEADERS, 'utf8')));

    try
    {
        const target = writeRelease(release, RELEASES);

        console.log(`revision-new: wrote ${target} (${Object.keys(release.events).length} events, ${Object.keys(release.composers).length} composers).`);
    }
    catch(error)
    {
        console.error(`revision-new: ${error.message}`);
        process.exit(1);
    }
}
