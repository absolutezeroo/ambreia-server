"""Generate docs/codebase/generated/*.md from the document-vortex inventory scan.

Exhaustive reference indexes only. Every explanatory page is hand-written.
"""

import json
import os
import re
import collections

INV = json.load(open('.claude/document-vortex-inventory.json', encoding='utf-8'))
OUT = 'docs/codebase/generated'
SHA = INV['git']['head']

HEADER = (
    "> **Generated reference index.** This file inventories code symbols from a static scan of the\n"
    "> repository at commit `{sha}` and should not be used as the sole source for runtime semantics.\n"
    "> Regenerate with `/document-vortex update`. Explanatory pages live one directory up.\n"
).format(sha=SHA[:12])


def w(name, body):
    with open(os.path.join(OUT, name), 'w', encoding='utf-8', newline='\n') as f:
        f.write(body)
    print('wrote', name, len(body))


def proj_name(p):
    return os.path.basename(p).replace('.csproj', '')


# ---------- project index ----------
projects = INV['projects']
by_name = {proj_name(p['path']): p for p in projects}
exe, web = set(), set()
for p in projects:
    txt = open(p['path'], encoding='utf-8', errors='ignore').read()
    if '<OutputType>Exe' in txt:
        exe.add(proj_name(p['path']))
    if 'Microsoft.NET.Sdk.Web' in txt:
        web.add(proj_name(p['path']))
test = {proj_name(t['path']) for t in INV['test_projects']}
perf = {proj_name(t['path']) for t in INV['performance_projects']}


def kind(n):
    if n in test:
        return 'Test'
    if n in perf:
        return 'Perf/tooling'
    if n in exe and n in web:
        return 'Executable (web SDK)'
    if n in exe:
        return 'Executable'
    if n in web:
        return 'Library (web SDK)'
    return 'Library'


rows = []
for p in sorted(projects, key=lambda x: proj_name(x['path'])):
    n = proj_name(p['path'])
    refs = ', '.join('`%s`' % proj_name(r) for r in sorted(p['project_references'])) or '&mdash;'
    pkgs = ', '.join('`%s`' % pk['name'] for pk in p['package_references'][:6]) or '&mdash;'
    if len(p['package_references']) > 6:
        pkgs += ', &hellip;'
    rows.append('| `%s` | %s | %s | %s |' % (n, kind(n), refs, pkgs))

body = [
    '# Project index', '', HEADER, '',
    '%d projects in `Vortex.Cloud.sln`. `Kind` is derived from `OutputType` and the SDK attribute in '
    'the `.csproj`: a project with no `OutputType` is a **library**, however service-like its name '
    'sounds. Responsibilities are described in '
    '[`../00-overview/solution-map.md`](../00-overview/solution-map.md).' % len(projects), '',
    '| Project | Kind | Direct project references | Key packages |',
    '|---|---|---|---|',
] + rows
w('project-index.md', '\n'.join(body) + '\n')

# ---------- dependency index ----------
indeg = collections.Counter()
for p in projects:
    for r in p['project_references']:
        indeg[proj_name(r)] += 1

lines = [
    '# Project dependency index', '', HEADER, '',
    '## Most-referenced projects', '',
    'In-degree = how many projects in the solution reference this one directly. A high in-degree '
    'marks a contracts hub, not necessarily a layer that owns behaviour.', '',
    '| Project | Referenced by | Kind |', '|---|---|---|',
]
for n, c in indeg.most_common():
    lines.append('| `%s` | %d | %s |' % (n, c, kind(n)))

lines += ['', '## Roots (referenced by no other project)', '']
for n in sorted(n for n in by_name if indeg[n] == 0):
    lines.append('- `%s` &mdash; %s' % (n, kind(n)))

lines += ['', '## Adjacency (outgoing)', '', '| Project | Depends on |', '|---|---|']
for p in sorted(projects, key=lambda x: proj_name(x['path'])):
    n = proj_name(p['path'])
    refs = ', '.join('`%s`' % proj_name(r) for r in sorted(p['project_references'])) or '&mdash;'
    lines.append('| `%s` | %s |' % (n, refs))
w('project-dependencies.md', '\n'.join(lines) + '\n')

# ---------- grain index ----------
ifaces = {g['symbol']: g['path'] for g in INV['symbols']['grain_interfaces']}
impls = {g['symbol']: g['path'] for g in INV['symbols']['grain_implementations']}

KEYMAP = [
    ('IGrainWithGuidCompoundKey', 'Guid + string'),
    ('IGrainWithIntegerCompoundKey', 'long + string'),
    ('IGrainWithGuidKey', 'Guid'),
    ('IGrainWithIntegerKey', 'long'),
    ('IGrainWithStringKey', 'string'),
]

lines = [
    '# Grain index', '', HEADER, '',
    'Grain interfaces found by static scan, including partial-file parts. `Key` is read from the '
    '`IGrainWith*Key` the interface extends. `Persistence` distinguishes an Orleans '
    '`[PersistentState]` store from a grain that merely *names* a `DbContext` type &mdash; the latter '
    'is a lexical hit, not proof of what the grain persists. Responsibilities and **state ownership** '
    'are documented in [`../03-orleans/grain-map.md`](../03-orleans/grain-map.md); a grain named after '
    'an entity does not necessarily own it.', '',
    '| Interface | Declared in | Implementation | Key | Persistence |', '|---|---|---|---|---|',
]
def read_all_parts(path):
    """An interface may be split across partial files (IFoo.cs, IFoo.Bar.cs)."""
    d, base = os.path.dirname(path), os.path.basename(path)
    stem = base.split('.')[0]
    out = []
    for f in sorted(os.listdir(d)):
        if f == stem + '.cs' or f.startswith(stem + '.'):
            out.append(open(os.path.join(d, f), encoding='utf-8', errors='ignore').read())
    return '\n'.join(out) or open(path, encoding='utf-8', errors='ignore').read()


for name in sorted(ifaces):
    ipath = ifaces[name].replace('\\', '/')
    itxt = read_all_parts(ifaces[name])
    key = next((label for tok, label in KEYMAP if tok in itxt), None)
    if key is None:
        # Aggregate interfaces (e.g. IRoomGrain) inherit their key from a base facet.
        d = os.path.dirname(ifaces[name])
        for base in re.findall(r'\bI[A-Za-z0-9_]+', itxt):
            cand = os.path.join(d, base + '.cs')
            if base != name and os.path.exists(cand):
                key = next((l for t, l in KEYMAP if t in read_all_parts(cand)), None)
                if key:
                    key += ' (via `%s`)' % base
                    break
    key = key or 'Unverified'
    impl_name = name[1:] if name.startswith('I') else name
    ipl = impls.get(impl_name)
    pers = '&mdash;'
    if ipl and os.path.exists(ipl):
        t = read_all_parts(ipl)
        m = re.search(r'\[PersistentState\(\s*"([^"]+)"(?:\s*,\s*"([^"]+)")?', t)
        if m:
            pers = '`[PersistentState]` ' + (
                'store `%s`' % m.group(2) if m.group(2) else 'state `%s`' % m.group(1))
        elif 'DbContext' in t:
            pers = 'names a `DbContext`'
    lines.append('| `%s` | `%s` | %s | %s | %s |' % (
        name, ipath,
        '`%s`' % ipl.replace('\\', '/') if ipl else '_not matched by scan_',
        key, pers))
w('grain-index.md', '\n'.join(lines) + '\n')

# ---------- handler index ----------
handlers = [h for h in INV['symbols']['packet_handlers']
            if h['symbol'].endswith('Handler')
            and 'IMessageHandler<' in open(h['path'], encoding='utf-8', errors='ignore').read()]
groups = collections.defaultdict(list)
for h in handlers:
    parts = h['path'].replace('\\', '/').split('/')
    groups[parts[1] if len(parts) > 2 else '(root)'].append(h)

lines = [
    '# Packet handler index', '', HEADER, '',
    '%d handler types under `Vortex.PacketHandlers/**`, grouped by domain folder. The filter is a '
    'declared `IMessageHandler<T>` implementation, not a `*Handler` filename &mdash; registration is '
    'a reflection scan for that closed generic and nothing else. Handlers are orchestration only: '
    'they validate input, call grains, and map snapshots to composers. See '
    '[`../02-network-protocol/packet-pipeline.md`](../02-network-protocol/packet-pipeline.md).'
    % len(handlers), '',
    '| Domain | Handlers |', '|---|---|',
]
for dom in sorted(groups):
    lines.append('| `%s` | %d |' % (dom, len(groups[dom])))
lines.append('')
for dom in sorted(groups):
    lines += ['## %s' % dom, '', '| Handler | File |', '|---|---|']
    for h in sorted(groups[dom], key=lambda x: x['symbol']):
        lines.append('| `%s` | `%s` |' % (h['symbol'], h['path'].replace('\\', '/')))
    lines.append('')
w('packet-handler-index.md', '\n'.join(lines) + '\n')

# ---------- revision index ----------
arts = [a['path'].replace('\\', '/') for a in INV['symbols']['revision_artifacts']]
parsers = [a for a in arts if '/Parsers/' in a]
sers = [a for a in arts if '/Serializers/' in a]
maps = [a for a in arts if '/Maps/' in a]
other = [a for a in arts if a not in parsers and a not in sers and a not in maps]


def group_by_domain(items, marker):
    g = collections.defaultdict(list)
    for p in items:
        seg = p.split(marker + '/')[1].split('/')
        g[seg[0] if len(seg) > 1 else '(root)'].append(p)
    return g


MAPDIR = 'Vortex.Revisions/Revision20260701/Maps'
maptext = '\n'.join(open(os.path.join(MAPDIR, f), encoding='utf-8', errors='ignore').read()
                    for f in sorted(os.listdir(MAPDIR)))
mapped_p = maptext.count('MapParser')
mapped_s = maptext.count('MapSerializer')
header_consts = open('Vortex.Revisions/Revision20260701/Headers.cs',
                     encoding='utf-8', errors='ignore').read().count('const int')


def count_declaring(paths, token):
    n = 0
    for p in paths:
        if token in open(p, encoding='utf-8', errors='ignore').read():
            n += 1
    return n


n_parser_cls = count_declaring(parsers, 'IParser')
n_ser_cls = count_declaring(sers, 'AbstractSerializer<')

lines = [
    '# Revision index', '', HEADER, '',
    'Artefacts of the embedded default revision `Revision20260701`, plus shared revision '
    'infrastructure. **Header ids are per-revision and are never global protocol truth** &mdash; see '
    '[`../02-network-protocol/revisions.md`](../02-network-protocol/revisions.md).', '',
    '## Written vs mapped', '',
    'A parser or serializer class existing is not the same as it being reachable. Only what a '
    '`Maps/*.cs` class registers into the revision tables can ever run; the remainder compiles, '
    'ships, and is dead. Registering a duplicate header or composer type is a startup crash rather '
    'than a silent overwrite (`Vortex.Revisions/RevisionMapBuilder.cs`).', '',
    '| Artefact kind | Classes | Registered in `Maps/` | Unmapped |', '|---|---|---|---|',
    '| Parsers (incoming) | %d | %d | %d |' % (n_parser_cls, mapped_p, n_parser_cls - mapped_p),
    '| Serializers (outgoing) | %d | %d | %d |' % (n_ser_cls, mapped_s, n_ser_cls - mapped_s), '',
    'Counted as files declaring the base type (`IParser` / `AbstractSerializer<>`); a handful of base '
    'and helper classes inflate a raw file count of %d and %d. The unmapped serializers are whole '
    'families &mdash; every `Game2*` (32 files, 0 mapped), all `Talent*` composers, the jukebox/playlist '
    'set. They compile and ship and can never run.' % (len(parsers), len(sers)), '',
    '| Other artefact | Count |', '|---|---|',
    '| Header map classes | %d |' % len(maps),
    '| Header id constants in `Headers.cs` | %d |' % header_consts,
    '| Infrastructure / other | %d |' % len(other), '',
    '## Parsers by domain', '',
    'File counts. See the written-vs-mapped table above before treating a count as coverage.', '',
    '| Domain | Count |', '|---|---|',
]
pg = group_by_domain(parsers, 'Parsers')
for d in sorted(pg):
    lines.append('| `%s` | %d |' % (d, len(pg[d])))
lines += ['', '## Serializers by domain', '', '| Domain | Count |', '|---|---|']
sg = group_by_domain(sers, 'Serializers')
for d in sorted(sg):
    lines.append('| `%s` | %d |' % (d, len(sg[d])))
lines += ['', '## Header maps', '', 'One map class per protocol domain; each contributes its ids to '
          'the revision registry.', '']
for a in sorted(maps):
    lines.append('- `%s`' % a)
lines += ['', '## Revision infrastructure', '']
for a in sorted(other)[:80]:
    lines.append('- `%s`' % a)
if len(other) > 80:
    lines.append('- _&hellip; %d more_' % (len(other) - 80))
w('revision-index.md', '\n'.join(lines) + '\n')

# ---------- entity index ----------
ents = [e for e in INV['symbols']['entities']
        if e['path'].replace('\\', '/').startswith('Vortex.Database/')]
eg = collections.defaultdict(list)
for e in ents:
    parts = e['path'].replace('\\', '/').split('/')
    eg[parts[2] if len(parts) > 3 else '(root)'].append(e)

lines = [
    '# Entity index', '', HEADER, '',
    '%d entity types under `Vortex.Database/Entities/**`, grouped by folder. **145 of them are mapped '
    'as a `DbSet` on `VortexDbContext`**; the remainder are bases or unmapped types. Table names, keys, '
    'indexes and relationships are configured separately &mdash; see '
    '[`../07-database/entities-and-relationships.md`](../07-database/entities-and-relationships.md). '
    'A table listed here is **not** proof that the DB owns that state at runtime; check '
    '[`../07-database/ownership-boundaries.md`](../07-database/ownership-boundaries.md).' % len(ents),
    '', '| Group | Entities |', '|---|---|',
]
for g in sorted(eg):
    lines.append('| `%s` | %d |' % (g, len(eg[g])))
lines.append('')
for g in sorted(eg):
    lines += ['## %s' % g, '', '| Entity | File |', '|---|---|']
    for e in sorted(eg[g], key=lambda x: x['symbol']):
        lines.append('| `%s` | `%s` |' % (e['symbol'], e['path'].replace('\\', '/')))
    lines.append('')

migs = sorted({os.path.basename(m['path']) for m in INV['symbols']['migrations']
               if not m['path'].endswith('.Designer.cs')})
lines += [
    '## Migrations', '',
    '%d migration files under `Vortex.Database/Migrations/` (designer files excluded).' % len(migs),
    '', '- Oldest: `%s`' % migs[0], '- Newest: `%s`' % migs[-1], '',
    'See [`../07-database/migrations.md`](../07-database/migrations.md) for the offline authoring '
    'recipe.', '',
]
w('entity-index.md', '\n'.join(lines) + '\n')

# ---------- endpoint index ----------
ENDPOINT_TOKENS = ('MapGet(', 'MapPost(', 'MapPut(', 'MapDelete(', 'MapPatch(',
                   'MapReadGet(', 'MapGroup(')
ep = set(e['path'].replace('\\', '/') for e in INV['symbols']['endpoint_files'])
for host in ('Vortex.WebApi',):
    for d, _, fs in os.walk(host):
        if os.sep + 'obj' in d or os.sep + 'bin' in d:
            continue
        for f in fs:
            if not f.endswith('.cs'):
                continue
            full = os.path.join(d, f)
            body = open(full, encoding='utf-8', errors='ignore').read()
            if any(t in body for t in ENDPOINT_TOKENS):
                ep.add(full.replace(os.sep, '/'))

lines = [
    '# Endpoint file index', '', HEADER, '',
    'Files that register HTTP endpoints, detected by any of `MapGet` / `MapPost` / `MapPut` / '
    '`MapDelete` / `MapPatch` / `MapReadGet` / `MapGroup` &mdash; the repo-local `MapReadGet` wrapper '
    'is why a plain `MapGet` scan under-reports.', '',
    '| File | Host project |', '|---|---|',
]
for p in sorted(ep):
    lines.append('| `%s` | `%s` |' % (p, p.split('/')[0]))
w('endpoint-index.md', '\n'.join(lines) + '\n')

# ---------- configuration index ----------
lines = [
    '# Configuration index', '', HEADER, '',
    'Configuration key prefixes discovered by static scan of `appsettings*.json` and `IConfiguration` '
    'access in code. This is a prefix inventory, not the full key set. Consumers and effects are '
    'documented in [`../01-runtime/configuration.md`](../01-runtime/configuration.md).', '',
    '| Key / prefix |', '|---|',
]
for k in sorted(INV['configuration_keys']):
    lines.append('| `%s` |' % k)
w('configuration-index.md', '\n'.join(lines) + '\n')

print('done')
