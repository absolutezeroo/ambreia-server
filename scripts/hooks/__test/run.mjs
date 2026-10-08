// Self-contained smoke test for the hook scripts. Creates its own fixtures, removes them, and
// asserts the exit codes Claude Code reads: 0 = allow, 2 = block and hand stderr back.
//   node scripts/hooks/__test/run.mjs
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';

const root = process.cwd();

// Sonde csharpier : du C# valide mais mal formate. Le hook doit le reformater ET le dire, parce que
// le fichier sur disque ne correspond alors plus a ce qui vient d'etre ecrit. La seconde sonde est
// deja propre : elle prouve que le cas courant reste silencieux.
const csProbe = path.join(root, 'Vortex.Primitives', '__HookProbeFormat.cs');
const csProbeClean = path.join(root, 'Vortex.Primitives', '__HookProbeClean.cs');
fs.writeFileSync(csProbe, 'namespace Vortex.Primitives;public class HookProbeFormat{public int X{get;set;}}\n');
fs.writeFileSync(
  csProbeClean,
  'namespace Vortex.Primitives;\n\npublic class HookProbeClean\n{\n    public int X { get; set; }\n}\n',
);

const run = (script, payload) =>
  spawnSync(process.execPath, [path.join('scripts', 'hooks', script)], {
    input: JSON.stringify(payload),
    encoding: 'utf8',
  });

const edit = (rel) => ({ tool_input: { file_path: path.join(root, rel) } });
const cmd = (command) => ({ tool_name: 'Bash', tool_input: { command } });

const cases = [
  ['post-edit.mjs', edit('README.md'), 0, 'fichier hors perimetre'],
  ['guard-emulator.mjs', cmd('dotnet build Vortex.Main/Vortex.Main.csproj'), 0, 'build normal'],
  ['guard-emulator.mjs', cmd('taskkill /F /IM Vortex.Main.exe'), 2, 'taskkill emulateur'],
  ['guard-emulator.mjs', cmd('Stop-Process -Name Vortex.Main -Force'), 2, 'Stop-Process emulateur'],
  ['guard-emulator.mjs', cmd('taskkill /F /IM chrome.exe'), 0, 'taskkill sans rapport'],
  // Le verbe doit etre en position de commande. Ces trois cas ne tuent rien : ils en PARLENT.
  // Le troisieme a reellement bloque le commit qui introduisait ce garde.
  ['guard-emulator.mjs', cmd('grep -rn "taskkill Vortex.Main" scripts/'), 0, 'grep qui mentionne le verbe'],
  ['guard-emulator.mjs', cmd('echo taskkill /F /IM Vortex.Main.exe'), 0, 'echo qui mentionne le verbe'],
  [
    'guard-emulator.mjs',
    cmd("git commit -F - <<'EOF'\nrefuses a command that would kill the running Vortex.Main\nEOF"),
    0,
    'corps de heredoc qui mentionne le verbe',
  ],
  // ... mais un vrai kill enchaine derriere une autre commande doit toujours passer a la trappe.
  ['guard-emulator.mjs', cmd('dotnet build || taskkill /F /IM Vortex.Main.exe'), 2, 'kill enchaine apres ||'],
  // csharpier par fichier : sans ca, l'ecart de format n'apparait qu'au bout des ~2 min du pre-commit.
  ['post-edit.mjs', edit(path.join('Vortex.Primitives', '__HookProbeFormat.cs')), 2, 'C# mal formate : reformate et signale'],
  ['post-edit.mjs', edit(path.join('Vortex.Primitives', '__HookProbeClean.cs')), 0, 'C# deja propre : silencieux'],
  // Garde commit : l'index est partage avec l'user et avec toute autre session sur le meme tree.
  ['guard-commit.mjs', cmd('git commit -o Vortex.Main/Program.cs -m "x"'), 0, 'commit scope par -o'],
  ['guard-commit.mjs', cmd('git commit --only Vortex.Main/Program.cs -m "x"'), 0, 'commit scope par --only'],
  ['guard-commit.mjs', cmd('git commit -- Vortex.Main/Program.cs'), 0, 'commit scope par -- pathspec'],
  ['guard-commit.mjs', cmd('git commit -m "x"'), 2, 'commit nu'],
  ['guard-commit.mjs', cmd('git commit -am "x"'), 2, 'commit -am'],
  ['guard-commit.mjs', cmd('git add -A'), 2, 'add qui ratisse tout'],
  ['guard-commit.mjs', cmd('git add .'), 2, 'add . qui ratisse tout'],
  ['guard-commit.mjs', cmd('git add Vortex.Main/Program.cs'), 0, 'add scope'],
  ['guard-commit.mjs', cmd('git add -A -- Vortex.Main'), 0, 'add -A limite a un pathspec'],
  ['guard-commit.mjs', cmd('git status --short'), 0, 'status sans rapport'],
  // Meme piege que guard-emulator : le verbe doit etre en position de commande, pas cite.
  ['guard-commit.mjs', cmd('grep -rn "git commit -m" scripts/'), 0, 'grep qui mentionne le verbe'],
  [
    'guard-commit.mjs',
    cmd("git commit -o scripts/hooks/guard-commit.mjs -F - <<'EOF'\nrefuses a bare `git add -A`\nEOF"),
    0,
    'corps de heredoc qui mentionne le verbe',
  ],
  ['guard-commit.mjs', cmd('dotnet build && git commit -m "x"'), 2, 'commit nu enchaine apres &&'],
];

// Scripts autonomes : pas de payload sur stdin.

// Le mur 3 est a zero depuis que le hub est protocole-libre, donc pointer une baseline vide ne
// prouve plus rien : il faut une vraie violation. La sonde est un fichier de contrats qui importe le
// protocole -- exactement ce que le mur existe pour refuser. Elle est retiree avant le cas passant.
const wallProbe = path.join(root, 'Vortex.Primitives', 'Rooms', '__WallProbe.cs');
fs.writeFileSync(wallProbe, 'using Vortex.Protocol.Messages.Incoming.Handshake;\n');

// Le mur 4 garde un gain de build mesure (74s -> 17s), que rendre est invisible : la compilation
// redevient simplement plus lente. La sonde rend vraiment la reference a un projet protege.
const freeProject = path.join(root, 'Vortex.Furniture', 'Vortex.Furniture.csproj');
const freeProjectOriginal = fs.readFileSync(freeProject, 'utf8');
fs.writeFileSync(
  freeProject,
  freeProjectOriginal.replace(
    '</ItemGroup>\n</Project>',
    '  <ProjectReference Include="..\\Vortex.Protocol\\Vortex.Protocol.csproj" />\n</ItemGroup>\n</Project>'
  )
);

// Le mur 8 (couches) : une reference Coeur -> Plateforme, la seule sonde en place pour ce cas.
const layerProject = path.join(root, 'Vortex.Crypto', 'Vortex.Crypto.csproj');
const layerProjectOriginal = fs.readFileSync(layerProject, 'utf8');
// Le mur 9 : un module de room qui ouvre un contexte de base, ce que le store de Persistence existe
// pour remplacer.
const roomPartProbe = path.join(root, 'Vortex.Rooms', 'Grains', 'Modules', '__WallProbe9.cs');
const probeRoomPart = () => {
  restoreLayers();
  fs.writeFileSync(roomPartProbe, '// probe\nclass P { VortexDbContext Db; }\n');
};
const restoreWallProbes = () => {
  fs.rmSync(wallProbe, { force: true });
  fs.rmSync(roomPartProbe, { force: true });
  fs.writeFileSync(freeProject, freeProjectOriginal);
};

// La mise en page : deux types dans un fichier, et un namespace qui ne nomme pas son dossier.
const layoutProbe = path.join(root, 'Vortex.Primitives', 'Rooms', '__LayoutProbe.cs');
const probeLayout = () =>
  fs.writeFileSync(
    layoutProbe,
    'namespace Vortex.Primitives;\n\npublic class LayoutProbeA { }\n\npublic class LayoutProbeB { }\n'
  );
const restoreLayout = () => fs.rmSync(layoutProbe, { force: true });
const probeLayers = () => {
  restoreWallProbes();
  fs.writeFileSync(
    layerProject,
    layerProjectOriginal.replace(
      /<\/Project>\s*$/,
      '<ItemGroup>\n<ProjectReference Include="..\\Vortex.Rooms\\Vortex.Rooms.csproj" />\n</ItemGroup>\n</Project>\n'
    )
  );
};
const restoreLayers = () => fs.writeFileSync(layerProject, layerProjectOriginal);

const direct = [
  ['check-architecture-walls.mjs', [], 2, 'fuite protocole + reference rendue a un projet protege'],
  ['check-architecture-walls.mjs', [], 2, 'reference Coeur -> Plateforme bloquee', {}, probeLayers],
  ['check-architecture-walls.mjs', [], 2, 'module de room qui ouvre la base bloque', {}, probeRoomPart],
  ['check-architecture-walls.mjs', [], 0, 'murs archi : tous tiennent', {}, restoreLayers],
  ['check-code-layout.mjs', [], 2, 'deux types et un namespace hors dossier bloques', {}, probeLayout],
  ['check-code-layout.mjs', [], 0, 'mise en page : un type par fichier tient', {}, restoreLayout],
];

let failed = 0;
for (const [script, payload, want, label] of cases) {
  const r = run(script, payload);
  const ok = r.status === want;
  if (!ok) failed++;
  console.log(`${ok ? 'OK  ' : 'FAIL'} ${label} (attendu=${want} obtenu=${r.status})`);
  if (!ok && r.stderr) console.log('     ' + r.stderr.trim().split('\n').slice(0, 4).join('\n     '));
}

for (const [script, argv, want, label, env, setup] of direct) {
  if (setup) setup();
  if (script === 'check-architecture-walls.mjs' && want === 0) restoreWallProbes();
  const r = spawnSync(process.execPath, [path.join('scripts', 'hooks', script), ...argv], {
    encoding: 'utf8',
    env: { ...process.env, ...env },
  });
  const ok = r.status === want;
  if (!ok) failed++;
  console.log(`${ok ? 'OK  ' : 'FAIL'} ${label} (attendu=${want} obtenu=${r.status})`);
  if (!ok && r.stderr) console.log('     ' + r.stderr.trim().split('\n').slice(0, 4).join('\n     '));
}

fs.rmSync(csProbe, { force: true });
fs.rmSync(csProbeClean, { force: true });
fs.rmSync(wallProbe, { force: true });
fs.rmSync(roomPartProbe, { force: true });
restoreLayout();
fs.writeFileSync(freeProject, freeProjectOriginal);
restoreLayers();
console.log(failed ? `\n${failed} test(s) en echec.` : '\nTous les hooks se comportent comme attendu.');
process.exit(failed ? 1 : 0);
