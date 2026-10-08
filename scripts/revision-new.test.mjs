import {test} from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync, readFileSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {drawRelease, readHeaderNames, revisionId, writeRelease} from './revision-new.mjs';

const HEADERS = `
namespace X;
internal static class MessageEvent
{
    public const int ClientHelloMessageEvent = 4000;
    public const int PongMessageEvent = 362; // comment
    public const int SSOTicketMessageEvent = 882;
}
internal static class MessageComposer
{
    public const int DisconnectReasonComposer = 4000;
    public const int PingMessageComposer = 1407;
}
`;

test('reads every constant name of both classes', () =>
{
    assert.deepEqual(readHeaderNames(HEADERS), {
        events: ['ClientHelloMessageEvent', 'PongMessageEvent', 'SSOTicketMessageEvent'],
        composers: ['DisconnectReasonComposer', 'PingMessageComposer']
    });
});

test('the revision is named after the UTC minute it was drawn', () =>
{
    assert.equal(revisionId(new Date(Date.UTC(2026, 9, 8, 15, 30, 59))), 'AMBREIA-202610081530');
});

test('draws distinct ids in 1..3999 and keeps the two fixed ones on 4000', () =>
{
    const release = drawRelease(readHeaderNames(HEADERS), new Date(Date.UTC(2026, 9, 8, 15, 30)));

    assert.equal(release.revision, 'AMBREIA-202610081530');
    assert.equal(release.events.ClientHelloMessageEvent, 4000);
    assert.equal(release.composers.DisconnectReasonComposer, 4000);

    for(const [side, fixed] of [['events', 'ClientHelloMessageEvent'], ['composers', 'DisconnectReasonComposer']])
    {
        const drawn = Object.entries(release[side]).filter(([name]) => name !== fixed).map(([, id]) => id);

        assert.equal(new Set(drawn).size, drawn.length);
        assert.ok(drawn.every(id => Number.isInteger(id) && id >= 1 && id <= 3999));
    }
});

test('the draw comes from the given source, so two draws differ', () =>
{
    const names = readHeaderNames(HEADERS);
    const date = new Date(Date.UTC(2026, 9, 8, 15, 30));
    let n = 0;
    const counting = (min, max) => min + (n++ % (max - min));

    assert.notDeepEqual(drawRelease(names, date, counting).events, drawRelease(names, date, () => 1).events);
});

test('a second draw in the same minute does not overwrite the first', () =>
{
    const dir = mkdtempSync(join(tmpdir(), 'releases-'));
    const date = new Date(Date.UTC(2026, 9, 8, 15, 30));
    const names = readHeaderNames(HEADERS);
    const first = writeRelease(drawRelease(names, date), dir);
    const written = readFileSync(first, 'utf8');

    assert.equal(first, join(dir, 'AMBREIA-202610081530.json'));
    assert.throws(() => writeRelease(drawRelease(names, date), dir), /already exists/);
    assert.equal(readFileSync(first, 'utf8'), written);
});
