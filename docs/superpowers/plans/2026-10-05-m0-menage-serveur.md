# M0 — Ménage serveur : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** retirer de `vortex-rpg-emulator` tout ce que le RPG n'utilisera pas, en gardant à chaque étape un build vert et des tests verts, puis repartir d'une migration EF unique.

**Architecture :** suppression des feuilles vers le cœur (outillage → modules isolés → wired → jeux → features de room → sous-features des modules gardés → base de données). Chaque tâche supprime une feature *entière* (code, protocole, handlers, base, dashboard, tests, config) pour que le dashboard, les hooks et les tests restent cohérents à chaque commit.

**Tech stack :** .NET 9, Orleans, EF Core 9.0.8 + Pomelo MySQL, Svelte (dashboard), scripts node (`scripts/hooks/`).

**Spec :** `docs/superpowers/specs/2026-10-05-habbo-rpg-design.md` (sections 2 et 9). Lire la spec avant de commencer.

**Plan jumeau :** le ménage du client est dans `vortex-rpg-client/docs/superpowers/plans/2026-10-05-m0-menage-client.md`. Il s'exécute **après** ce plan.

## Global Constraints

- Une tâche = un commit. Avant chaque commit : `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck` vert.
- Commits : `guard-commit.mjs` bloque `git commit -m` seul, `git commit -am` et `git add -A`/`.`. Utiliser `git add -A -- <chemins>` puis `git commit -m "..." -- <chemins>` ou `git commit -o <chemins> -m "..."`.
- `csharpier` reformate un `.cs` après chaque `Edit` (hook `post-edit.mjs`) : relire le fichier avant l'`Edit` suivant.
- **Les numéros de ligne de ce plan sont indicatifs** (relevés au commit `16fdbb72`). Se fier aux noms de symboles ; relire chaque fichier avant de le modifier.
- **Ne jamais toucher aux migrations existantes** (`Vortex.Database/Migrations/2026*.cs`) ni aux seeds `Vortex.Database/Seeds/*.sql` avant la Tâche 11 : des migrations lisent ces seeds via `SeedScripts.Read(...)`.
- **Ne pas lancer `dotnet ef database update` ni `MigrateOnStartup=true` avant la Tâche 11.** Le modèle EF diverge du snapshot pendant tout le ménage (EF 9 refuse `Migrate()` avec des changements de modèle en attente). Les tests de base utilisent SQLite + `EnsureCreated` et ne sont pas concernés.
- **Garder la forme des messages réseau des composers conservés.** Quand un champ concerne une feature retirée (ex. `CanBreed`, `CanRate`, champs wired des préférences), écrire une valeur fixe (`false`/`0`) au lieu de supprimer le champ.
- Avant de supprimer un appel dans du code gardé, lire le corps de la méthode appelée : un nom ne prouve pas qu'un appel est inerte.
- Retirer une capability du dashboard = la retirer des **4 fichiers** : `Vortex.Primitives/Permissions/Capabilities.cs` (constante + `Dashboard.All`), `Vortex.Dashboard.Web/src/lib/dashboardPermissions.ts`, `Vortex.Dashboard.Web/src/lib/routes.ts`, `Vortex.Dashboard.Web/src/lib/locales/{en,fr}.js`. Puis mettre à jour `Vortex.Authentication/Permissions/DefaultRoles.cs`, `Vortex.Dashboard.Web/src/lib/apiTypes.d.ts`, `Vortex.Dashboard.Tests/Hosting/authorization-matrix.txt`, `Vortex.Dashboard.Tests/Hosting/ApiTypeScriptContractTests.cs`, `DashboardResponseSchemaTests.cs`. `check-dashboard-capabilities.mjs` vérifie la parité.
- Les baselines `scripts/hooks/*-baseline.json` (architecture-walls, authorization-surface, inert-declarations, sibling-divergence) : quand un check signale une entrée devenue obsolète, retirer l'entrée ou régénérer selon l'usage du script (`--write` quand il existe).
- Retirer un projet de la solution : `dotnet sln Vortex.Cloud.sln remove <chemin.csproj>` (ne pas éditer le `.sln` à la main), puis supprimer le dossier.

## Décisions prises par défaut (le propriétaire peut les renverser avant exécution)

Les inventaires ont révélé des couplages que la spec ne tranchait pas. Défauts retenus, au plus simple :

1. **Coffre de gains (vault)** : déplacé de `Vortex.Collectibles` vers `Vortex.Players` (les paiements d'achievements et le payday HC passent par lui).
2. **Objectifs communautaires** : on garde les objectifs, on supprime leur paiement en VortexCoin on-chain (`CommunityGoalPayoutService`) et les paliers payés en coin.
3. **Échanges** : `RoomTradingSystem` perd l'échange de NFT ; l'échange de furni reste.
4. **Caméra** : retirée avec les photos web (albums, photos du fil d'actualité, endpoints camera de la WebApi) : sans caméra, plus aucune photo n'est produite.
5. **Pets** : l'énergie, la faim et la soif deviennent constantes (toujours au maximum) ; plus de décroissance ni de nourriture. Donner un objet en main à un pet ne le nourrit plus. Le sommeil (niche) et le jeu (jouets) restent. `CustomizePetWithFurni` est gardé **uniquement pour la selle** (sinon la monture ne peut jamais démarrer).
6. **Crackable** : le système et les boîtes à récompense (`furniture_ecotron_box`, `furniture_nft_reward_box`, `furniture_effectbox`) et le cadeau de bienvenue sont retirés ; le format de stuff data `CrackableStuffData` reste (des items existants le portent).
7. **Ballon de foot** : la logique `furniture_pushable` part avec le football ; les balles retombent sur la logique par défaut.
8. **Word quiz** : retiré avec les sondages (même tuyauterie côté client).
9. **Navigator** : on garde Mes rooms, Rooms des amis, Mes groupes / QG de guilde, la recherche et la création de room ; on retire tout le reste (officiel, populaire, mieux notées, recommandées, staff picks, pubs de room et événements, favoris, historique, droits, recherches sauvegardées).
10. **Habbo Club (HC)** et **Rentable spaces** : hors périmètre de la spec, gardés.

## Review Focus

Les cas que ce plan ne teste pas par construction, et qui casseraient sans bruit :

1. **Interface de grain Orleans sans implémentation.** Ça compile et ça échoue à l'exécution (ex. vault). Couvert par le test de démarrage de la Tâche 12 (connexion d'un joueur, gain d'achievement).
2. **Service DI manquant au démarrage d'un plugin** (dashboard, WebApi `[FromServices]`) : 500 à chaque requête sans échec de build. Couvert par `check-webapi-forwarding.mjs` (FastCheck) et le démarrage réel de la Tâche 12.
3. **Forme réseau d'un composer conservé modifiée** (statut de pet, rating, préférences). Couvert par `Vortex.Revisions.Tests` : ne pas supprimer un test de forme réseau d'un composer gardé, l'adapter.
4. **Base neuve** : `InitialCreate` doit recréer les seeds gardés. Couvert par la Tâche 11, étape « migrer une base vide ».
5. **Items existants dont la clé de logique a disparu** : ils doivent se charger avec la logique par défaut, pas planter le chargement de la room. Couvert par `RoomObjectLogicKeyTests` (à adapter, pas supprimer) et la Tâche 12.

---

### Task 0 : Branche et ligne de base

**Files :** aucun.

- [ ] **Step 1 : créer la branche**

```bash
git switch -c m0-menage
```

- [ ] **Step 2 : ligne de base verte**

Run : `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck`
Expected : succès. Noter le nombre de tests passés par projet (sortie de `dotnet test`) dans la description de la PR finale. Si la ligne de base est rouge, s'arrêter et le signaler : le plan suppose un départ vert.

---

### Task 1 : Outillage de conformité Habbo

Retire Specs et les checks qui imposent la conformité au client officiel.

**Files :**
- Supprimer : `Vortex.Specs/`, `Vortex.Specs.Cli/`, `Vortex.Specs.Tests/`, `docs/habbo-specs/`, `docs/completeness/`, `docs/walkthroughs/use-the-habbo-specs.md`, `docs/codebase/02-network-protocol/habbo-specs.md`
- Supprimer : `scripts/hooks/check-wire-conflicts.mjs`, `check-header-registry.mjs`, `wire-conflicts-baseline.json`, `wire-arity-baseline.json`, `header-registry-baseline.json`
- Supprimer : `.claude/skills/habbo-spec/`, `.claude/skills/vortex-completeness/`, `.claude/agents/wire-truth-auditor.md`
- Modifier : `Directory.Build.targets`, `scripts/hooks/post-edit.mjs`, `scripts/hooks/__test/run.mjs`, `scripts/hooks/architecture-walls-baseline.json`, `.claude/settings.json`, `CLAUDE.md`, `AGENTS.md`, `CONTEXT.md`, `.github/copilot-instructions.md`, `.claude/gen-indexes.py`

`check-variable-capabilities.mjs` part en Tâche 5 avec les wired (il garde les variables wired).

- [ ] **Step 1 : constater ce qui existe (test « rouge »)**

```bash
grep -rIl "Vortex.Specs\|check-wire-conflicts\|check-header-registry" --exclude-dir=bin --exclude-dir=obj --exclude-dir=node_modules . | grep -v "^./docs/superpowers/"
```
Expected : une liste non vide (sln, `Directory.Build.targets`, hooks, docs…).

- [ ] **Step 2 : retirer les projets de la solution et les supprimer**

```bash
dotnet sln Vortex.Cloud.sln remove Vortex.Specs/Vortex.Specs.csproj Vortex.Specs.Cli/Vortex.Specs.Cli.csproj Vortex.Specs.Tests/Vortex.Specs.Tests.csproj
rm -rf Vortex.Specs Vortex.Specs.Cli Vortex.Specs.Tests docs/habbo-specs docs/completeness docs/walkthroughs/use-the-habbo-specs.md docs/codebase/02-network-protocol/habbo-specs.md
rm -f scripts/hooks/check-wire-conflicts.mjs scripts/hooks/check-header-registry.mjs scripts/hooks/wire-conflicts-baseline.json scripts/hooks/wire-arity-baseline.json scripts/hooks/header-registry-baseline.json
rm -rf .claude/skills/habbo-spec .claude/skills/vortex-completeness .claude/agents/wire-truth-auditor.md
```

- [ ] **Step 3 : `Directory.Build.targets`**

Dans `VortexCloudFastCheck` : supprimer l'`Exec` de `check-header-registry.mjs` et l'`Exec` final `dotnet run --project Vortex.Specs.Cli -- validate`, et mettre à jour le commentaire qui compte les checks. Dans `VortexCloudQualityGate` : supprimer l'`Exec` de `check-wire-conflicts.mjs` et son commentaire. Laisser l'`Exec` de `check-variable-capabilities.mjs` (Tâche 5).

- [ ] **Step 4 : hooks**

- `scripts/hooks/post-edit.mjs` : supprimer le bloc qui lance `check-header-registry.mjs` quand `Vortex.Revisions/*/Headers.cs` ou `Maps/*.cs` est modifié (~l. 38-48). Au passage, la regex des fichiers de capability cherche `dashboardPermissions.js` et `routes.js` alors que ces fichiers sont en `.ts` : la corriger en `.ts`.
- `scripts/hooks/__test/run.mjs` : supprimer le cas post-edit sur `Headers.cs` (~l. 54), la variable `emptyBaseline` et son nettoyage (~l. 82-83, ~l. 167), les cas `direct` de header-registry (baseline à jour, baseline vide, `VORTEX_HEADER_NO_CLIENT`, repli plafond : ~l. 118, 121, 124, 126-132). Garder les cas variable-capabilities (Tâche 5).
- `scripts/hooks/architecture-walls-baseline.json` : retirer les 3 entrées `Vortex.Specs*`.
- `.claude/settings.json` : retirer la permission `Bash(dotnet run --project Vortex.Specs.Cli:*)`. Ne rien toucher d'autre.

- [ ] **Step 5 : documents de règles**

- `CLAUDE.md` : retirer le point « Any change to Habbo protocol behaviour starts at the specs », les lignes du tableau des automatisations pour `check-header-registry`, `check-wire-conflicts`, `wire-truth-auditor`, `Vortex.Specs.Cli`, la mention `/habbo-spec`, et toute la section « ## Habbo Specs ». Ajouter en tête une ligne : « Ce dépôt est le fork RPG de Vortex : le protocole diverge volontairement du client Habbo officiel. Spec : `docs/superpowers/specs/2026-10-05-habbo-rpg-design.md`. »
- `AGENTS.md` : retirer la section « ## Habbo protocol behaviour: consult the specs first », la recette « ### Change Habbo protocol behaviour », et les lignes du tableau « Required validation before completion » pour header-registry, Specs validate et wire-conflicts.
- `CONTEXT.md` : retirer les lignes sur les projets Specs et la section « ## Habbo protocol behaviour ».
- `.github/copilot-instructions.md` : retirer les lignes sur les specs (~l. 25-26).
- `.claude/gen-indexes.py` : retirer la référence à `habbo-specs` (~l. 242).
- Les mentions restantes dans `.claude/skills/document-vortex/**` et `.claude/agents/vortex-protocol-researcher.md` : supprimer les phrases qui renvoient aux specs.

- [ ] **Step 6 : vérifier (test « vert »)**

Relancer la commande du Step 1. Expected : vide, sauf des commentaires C# qui citent `docs/habbo-specs` (ex. `GetCurrentTimingCodeMessageHandler.cs`, `IRoomRaidProtection.cs`, `RoomActionModule.Floor.cs`) : les reformuler sans le lien.

Run : `node scripts/hooks/__test/run.mjs` → Expected : tous les cas passent.
Run : `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck` → Expected : succès.

- [ ] **Step 7 : commit**

```bash
git add -A -- Vortex.Cloud.sln Vortex.Specs Vortex.Specs.Cli Vortex.Specs.Tests docs scripts/hooks .claude CLAUDE.md AGENTS.md CONTEXT.md .github Directory.Build.targets Vortex.PacketHandlers Vortex.Primitives Vortex.Rooms
git commit -m "m0: retire l'outillage de conformité Habbo (Specs, wire-conflicts, header-registry)" -- Vortex.Cloud.sln Vortex.Specs Vortex.Specs.Cli Vortex.Specs.Tests docs scripts/hooks .claude CLAUDE.md AGENTS.md CONTEXT.md .github Directory.Build.targets Vortex.PacketHandlers Vortex.Primitives Vortex.Rooms
```

---

### Task 2 : Déplacer le coffre de gains hors de Collectibles

À faire **avant** de supprimer Collectibles : les paiements d'achievements (`PlayerAchievementGrain`) et le payday HC (`PlayerGrain`) passent par le vault.

**Files :**
- Déplacer de `Vortex.Collectibles/` vers `Vortex.Players/Vault/` : `PlayerVaultGrain`, `CurrencyVaultRewardSettler`, `VaultRewardSettlerMap`, `EarningsRoutingPolicy` (et tout fichier dont ils dépendent dans Collectibles et qui n'est pas NFT/mint)
- Modifier : leur enregistrement DI (module Collectibles → `Vortex.Players/PlayerModule.cs`)
- Test : `Vortex.Players.Tests/` (tests vault existants dans `Vortex.Players.Tests/Collectibles/` : déplacer ceux qui testent le vault)

**Interfaces :**
- Produit : `IPlayerVaultGrain` (inchangé, dans `Vortex.Primitives/Vault/`), implémenté par `Vortex.Players.Vault.PlayerVaultGrain`.

- [ ] **Step 1 : repérer les dépendances**

```bash
grep -rIln "PlayerVaultGrain\|CurrencyVaultRewardSettler\|VaultRewardSettlerMap\|EarningsRoutingPolicy" --include=*.cs --exclude-dir=bin --exclude-dir=obj .
```
Lire chaque fichier de `Vortex.Collectibles/` listé et ses `using` : tout ce qui est NFT, mint, relic ou store reste dans Collectibles (supprimé en Tâche 4).

- [ ] **Step 2 : déplacer**

`git mv` chaque fichier vers `Vortex.Players/Vault/`, changer le `namespace` en `Vortex.Players.Vault`, déplacer l'enregistrement DI dans `PlayerModule.cs`. Si `Vortex.Players` n'a pas les références de projet nécessaires, les ajouter (jamais une référence vers `Vortex.Collectibles`).

- [ ] **Step 3 : déplacer les tests du vault**

`git mv` les tests vault de `Vortex.Players.Tests/Collectibles/` vers `Vortex.Players.Tests/Vault/`, namespace ajusté.

- [ ] **Step 4 : vérifier**

Run : `dotnet test Vortex.Players.Tests` → Expected : PASS, même nombre de tests vault qu'avant.
Run : `grep -rIn "Vortex.Collectibles" Vortex.Players Vortex.Progression --include=*.cs` → Expected : vide.
Run : `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck` → Expected : succès.

- [ ] **Step 5 : commit**

```bash
git add -A -- Vortex.Collectibles Vortex.Players Vortex.Players.Tests
git commit -m "m0: le coffre de gains vit dans Players, plus dans Collectibles" -- Vortex.Collectibles Vortex.Players Vortex.Players.Tests
```

---

### Task 3 : Chain, Ethereum et paiements on-chain

**Files :**
- `dotnet sln remove` puis supprimer : `Vortex.Chain/`, `Vortex.Chain.Tests/`
- Supprimer : `chain/` (racine), `tools/chain/`, `Vortex.Crypto/Ethereum/`, `Vortex.Crypto.Tests/Ethereum/`, `Vortex.Primitives/Chain/`, `Vortex.Primitives/Crypto/IHotelWalletService.cs`, `Vortex.Primitives/Crypto/IWalletProof.cs`, `Vortex.Players/ChainLeaderboardService.cs`, `Vortex.Progression/Quests/CommunityGoalPayoutService.cs`
- Supprimer (WebApi) : `Vortex.WebApi/Hosting/WebApiEndpoints.Chain.cs`, `WebApiEndpoints.Wallet.cs`, `WebApiEndpoints.Leaderboard.cs`, `Vortex.WebApi/Services/WalletLinkChallenges.cs`, `Vortex.WebApi.Tests/ChainEndpointTests.cs`, `WalletEndpointTests.cs`
- Supprimer (dashboard) : `Vortex.Dashboard.API/Operations/Catalogue/Contracts/GiveChainCoinRequest.cs`
- Modifier : voir étapes.

**Garder dans `Vortex.Crypto`** : `RsaService`, `DiffieService`, `Rc4Engine`, `Rc4Service`, `TotpCodes`, `Configuration/*` (chiffrement réseau et 2FA).

- [ ] **Step 1 : constater**

```bash
grep -rIl "Vortex.Chain\|IChainRewardService\|IHotelWalletService\|IWalletProof\|ChainLeaderboard\|WalletMasterSeed\|ChainRpc" --exclude-dir=bin --exclude-dir=obj --exclude-dir=Migrations --exclude-dir=node_modules . | grep -v "^./docs/"
```
Expected : non vide.

- [ ] **Step 2 : supprimer projets et dossiers**

```bash
dotnet sln Vortex.Cloud.sln remove Vortex.Chain/Vortex.Chain.csproj Vortex.Chain.Tests/Vortex.Chain.Tests.csproj
rm -rf Vortex.Chain Vortex.Chain.Tests chain tools/chain Vortex.Crypto/Ethereum Vortex.Crypto.Tests/Ethereum Vortex.Primitives/Chain
rm -f Vortex.Primitives/Crypto/IHotelWalletService.cs Vortex.Primitives/Crypto/IWalletProof.cs Vortex.Players/ChainLeaderboardService.cs Vortex.Progression/Quests/CommunityGoalPayoutService.cs
rm -f Vortex.WebApi/Hosting/WebApiEndpoints.Chain.cs Vortex.WebApi/Hosting/WebApiEndpoints.Wallet.cs Vortex.WebApi/Hosting/WebApiEndpoints.Leaderboard.cs Vortex.WebApi/Services/WalletLinkChallenges.cs Vortex.WebApi.Tests/ChainEndpointTests.cs Vortex.WebApi.Tests/WalletEndpointTests.cs
rm -f Vortex.Dashboard.API/Operations/Catalogue/Contracts/GiveChainCoinRequest.cs
```

- [ ] **Step 3 : Crypto, Main, Players**

- `Vortex.Crypto/Extensions/ServiceCollectionExtensions.cs` : retirer les enregistrements `IHotelWalletService`, `IWalletProof` et `using Vortex.Crypto.Ethereum`.
- `CryptoConfig` : retirer `WalletMasterSeed` ; `CryptoConfigValidator.cs` : retirer le bloc qui le valide.
- `Vortex.Main/Program.cs` : retirer `AddVortexChain(...)` et son `using`. `Vortex.Main.csproj` : retirer la `ProjectReference` à `Vortex.Chain`.
- `Vortex.Players/PlayerModule.cs` : retirer l'enregistrement de `ChainLeaderboardService` (~l. 53-60). `Vortex.Players.csproj` : retirer la référence `Vortex.Crypto` si plus rien ne l'utilise (le build le dira).
- `appsettings.json` : retirer `WalletMasterSeed`. `docker-compose.yml` : retirer le service chain, le bloc de seed de wallet et le volume `vortex-chain-data`.

- [ ] **Step 4 : objectifs communautaires sans paiement on-chain**

- `Vortex.Progression/ProgressionModule.cs` : retirer l'enregistrement hosted de `CommunityGoalPayoutService`.
- `Vortex.Progression/Grains/CommunityGoalGrain.cs` : retirer `IHotelWalletService` et tout le code de paiement en coin ; un objectif atteint ne paie plus rien (le suivi de progression reste).
- `CommunityGoalLadder` et les contrats dashboard de quête (`CommunityGoalLevelRow`, création/mise à jour de quête) : retirer les champs de montant en coin. Garder l'entité `CommunityGoalPayoutEntity` jusqu'à la Tâche 11 seulement si la retirer casse la compilation ailleurs ; sinon la supprimer avec son DbSet.
- Ne pas toucher à `ChainCode` des quêtes : c'est le champ « chaîne de quêtes » du protocole, sans rapport.

- [ ] **Step 5 : WebApi**

- `Vortex.WebApi/Hosting/WebApiEndpoints.cs` : retirer les `Map...` des endpoints supprimés (chain, wallet, leaderboard).
- `WebApiConfig.cs` : retirer `ChainRpcRateLimit`. `WebApiAppConfigurator.cs`, `WebApiWebHost.cs` (`ForwardSingletons`), `WebApiPlugin.cs` : retirer les services chain/leaderboard.
- `Vortex.WebApi.Tests/Fakes.cs` : retirer `FakeChainService` et `FakeChainLeaderboard` ; `WebApiTestFactory.cs` : retirer leur enregistrement.
- `openapi.json` : retirer les routes `/api/public/leaderboard/coins`, chain et wallet.

- [ ] **Step 6 : dashboard (pièce de monnaie on-chain)**

- `Vortex.Dashboard.API/Hosting/Catalogue/DashboardEndpoints.Economy.cs` et `Operations/Catalogue/CurrencyOperations.cs` : retirer `IChainRewardService` et `GiveChainCoinAsync`.
- `Vortex.Dashboard.Web/src/components/PlayerOperationsPanel.svelte` : retirer l'action `chainCoin`. `dashboardPermissions.ts` : retirer `chainCoin`. `locales/{en,fr}.js` : retirer `operations.giveChainCoin*` et `chainCoin*`.

- [ ] **Step 7 : vérifier**

Relancer la commande du Step 1 → Expected : vide.
Run : `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck` → Expected : succès.

- [ ] **Step 8 : commit**

```bash
DIRS=$(git status --porcelain | sed -E "s#^.. (.* -> )?##" | cut -d/ -f1 | sort -u | xargs)
echo "$DIRS"   # relire : uniquement des dossiers touchés par cette tâche
git add -A -- $DIRS
git status --short   # vérifier que seuls les fichiers de cette tâche sont indexés
git commit -m "m0: retire la blockchain (Chain, Ethereum, paiements on-chain, leaderboard coin)" -- $DIRS
```

---

### Task 4 : Collectibles, NFT, Habbicons, RewardTracks, LoadGen, Benchmark

**Files :**
- `dotnet sln remove` puis supprimer : `Vortex.Collectibles/`, `Vortex.Habbicons/`, `Vortex.RewardTracks/`, `Vortex.Rewards.Tests/`, `Vortex.LoadGen/`, `Vortex.Benchmark/`
- Garder : `Vortex.Primitives/RewardTracks/RewardTrackFacts.cs` et `RewardTrackActions.cs` (le vocabulaire Signals `Facts`/`SignalActions` s'appuie dessus, et Signals nourrit achievements et quêtes). Supprimer tout le reste de `Vortex.Primitives/RewardTracks/`.
- Garder : `EmeraldBalanceMessageComposer`, `SilverBalanceMessageComposer` et leurs serializers (monnaies du portefeuille, rangées par erreur dans les dossiers `Collectibles/`), `GetNftCredits`, `GetSilver`, les composers de styles de chat.
- Garder : `Vortex.Rooms.Tests/Furniture/RoomObjectHydrationBenchmark.cs` (micro-benchmark indépendant). `WiredEngineBenchmark` part en Tâche 5.

- [ ] **Step 1 : constater**

```bash
grep -rIl "Collectible\|Habbicon\|RewardTrack\|LoadGen\|Vortex.Benchmark\|IBenchmarkService\|Nft\|Relic\|PlayerMint" --include=*.cs --include=*.csproj --include=*.svelte --include=*.ts --include=*.js --include=*.json --exclude-dir=bin --exclude-dir=obj --exclude-dir=Migrations --exclude-dir=node_modules . | wc -l
```
Expected : un nombre élevé. Le garder pour comparer.

- [ ] **Step 2 : projets**

```bash
dotnet sln Vortex.Cloud.sln remove Vortex.Collectibles/Vortex.Collectibles.csproj Vortex.Habbicons/Vortex.Habbicons.csproj Vortex.RewardTracks/Vortex.RewardTracks.csproj Vortex.Rewards.Tests/Vortex.Rewards.Tests.csproj Vortex.LoadGen/Vortex.LoadGen.csproj Vortex.Benchmark/Vortex.Benchmark.csproj
rm -rf Vortex.Collectibles Vortex.Habbicons Vortex.RewardTracks Vortex.Rewards.Tests Vortex.LoadGen Vortex.Benchmark
```
- `Vortex.Main/Program.cs` : retirer `AddHostPlugin<HabbiconsModule>`, `<RewardTracksModule>`, `<CollectiblesModule>`, `<BenchmarkModule>` et leurs `using`.
- `Vortex.Main.csproj` : retirer les `ProjectReference` correspondantes, le bloc LoadGen et le commentaire ARCH-MAIN-026.
- `Vortex.Players.Tests.csproj`, `Vortex.Rooms.Tests.csproj` : retirer les références Collectibles, Benchmark, LoadGen. `Vortex.Signals.csproj` : retirer le commentaire.
- `Vortex.Dashboard.API/AssemblyInfo.cs` : retirer `InternalsVisibleTo("Vortex.Rewards.Tests")`.

- [ ] **Step 3 : Primitives**

- Supprimer : `Vortex.Primitives/Collectibles/`, `Habbicons/`, `Benchmark/`, `Players/Grains/IPlayerNftWardrobeGrain.cs`, `Events/HabbiconEvents.cs`, `Events/RewardTrackEvents.cs`, et `RewardTracks/*` sauf `RewardTrackFacts.cs` et `RewardTrackActions.cs`.
- `Orleans/GrainFactoryExtensions.cs` : retirer `GetPlayerHabbiconGrain`, `GetPlayerRewardTrackGrain`, `GetNftCollectionsGrain`, `GetNftStoreGrain`, `GetPlayerNftClaimsGrain`, `GetNftMintingGrain`, `GetPlayerMintGrain`, `GetPlayerNftWardrobeGrain` et leurs `using`.
- `Events/CollectibleEvents.cs` : ne garder que les événements du vault (`VaultIncome*`) ; s'il ne reste qu'eux, renommer le fichier en `VaultEvents.cs`.
- `Server/ConfigKeyCatalog.cs` : retirer `benchmark.enabled`, `habbicons.recent_limit`, `habbicons.use_cooldown_ms`, `reward_tracks.enabled`.
- `Commerce/CommerceOperation.cs` : retirer `RewardTrackPremium` et `RewardTrackPrize` **sans renuméroter** les autres valeurs (valeurs persistées). `Notifications/NotificationKind.cs` : retirer `RewardTrackCompleted`, `HabbiconCollectionCompleted`, même règle. `Signals/Facts.cs` : retirer les clés `Habbicon`, `Collection`, `NftProduct`.
- Capabilities : retirer `HabbiconsRead`, `OpsHabbiconsManage`, `RewardTracksRead`, `OpsRewardTracksManage`, `CollectiblesRead`, `BenchmarkRead`, `OpsBenchmarkRun` (des 4 fichiers, voir Global Constraints).

- [ ] **Step 4 : base (code seulement)**

- Supprimer : `Vortex.Database/Entities/Collectibles/`, `Entities/Habbicons/`, `Entities/RewardTracks/`, `Entities/Players/NftAvatarEntity.cs`, `PlayerNftAvatarEntity.cs`, `PlayerNftOutfitEntity.cs`, `PlayerWalletEntity.cs` (vérifier d'abord avec grep que `PlayerWalletEntity` n'est utilisé que par la partie chain/NFT ; s'il sert au portefeuille de monnaies, le garder).
- `Context/VortexDbContext.cs` : retirer les DbSets et `mb.Entity` correspondants et les `using`.
- Supprimer : `Vortex.Database.Tests/RewardTracks/RewardTrackClaimResumeTests.cs`.
- **Ne pas** toucher aux migrations ni aux seeds `habbicons.sql`, `reward_track_introduction.sql`, `nft_mintable_clothing.sql` (Tâche 11).

- [ ] **Step 5 : protocole et handlers**

- `Vortex.Protocol/Messages/` : supprimer `Incoming/Habbicons`, `Outgoing/Habbicons`, `Incoming/RewardTracks`, `Outgoing/RewardTracks`, `Incoming/Collectibles`, `Outgoing/Collectibles` sauf `EmeraldBalanceMessageComposer` et `SilverBalanceMessageComposer`. Dans `Nft/` : supprimer les messages de garde-robe NFT (`GetSelectedNftWardrobeOutfit`, `GetUserNftWardrobe`, `SaveUserNftWardrobe`, `UserNftWardrobe*Composer`).
- `Vortex.Revisions/Revision20260701/` : supprimer `Maps/HabbiconMap.cs`, `Maps/RewardTrackMap.cs`, `Parsers/Habbicons`, `Serializers/Habbicons`, `Parsers/RewardTracks`, `Serializers/RewardTracks`, `Parsers/Collectibles`, `Serializers/Collectibles` sauf Emerald/Silver. `Maps/CollectiblesMap.cs` : ne garder que les mappings Emerald/Silver. `Maps/NftMap.cs` : retirer les parsers/serializers de garde-robe. `Revision20260701.cs` : retirer `new HabbiconMap()` et `new RewardTrackMap()`. Les constantes de `Headers.cs` peuvent rester.
- `Vortex.PacketHandlers/` : supprimer `Habbicons/`, `RewardTracks/`, `Collectibles/`, et les 3 handlers de garde-robe dans `Nft/`. `Handshake/SSOTicketMessageHandler.cs` : retirer l'envoi d'état Habbicon/RewardTrack à la connexion. `Register/UpdateFigureDataMessageHandler.cs` : retirer l'appel à la garde-robe NFT.
- Tests : supprimer `Vortex.Revisions.Tests/Collectibles/*` sauf `ChatStyleWireTests.cs` et les tests Emerald/Silver ; garder `Notifications/IncomeRewardClaimWireTests.cs` (vault).

- [ ] **Step 6 : échanges sans NFT, chat sans Habbicon**

- `Vortex.Rooms/Grains/Systems/RoomTradingSystem.cs` : retirer tout ce qui concerne les NFT (`GetPlayerMintGrain`, `NftAssets`, `NftAssetLedger`, composers `TradeNft*`). L'échange de furni et de monnaies reste. Lire chaque méthode en entier avant de couper : les NFT sont mêlés à la validation et au commit de l'échange.
- `Vortex.Rooms/Grains/Systems/RoomChatSystem.cs` et `RoomGrain.Avatar.cs` : retirer l'usage de Habbicon ; `IRoomAvatars.UseHabbiconAsync` : retirer.
- Tests : supprimer `Vortex.Rooms.Tests/Trading/RemoveNftFromTradeTests.cs`, `Players/NftAvatarWardrobeTests.cs`, `Players/BenchmarkModuleRegistrationTests.cs`, `Players/BenchmarkPacketFramingTests.cs`, `Players/LoadGeneratorWireTests.cs`, `Vortex.Players.Tests/Collectibles/*` (les tests vault ont été déplacés en Tâche 2). `Vortex.Rooms.Tests/Support/RoomHarness.cs` : retirer `IPlayerMintGrain`.

- [ ] **Step 7 : Signals, Observability, WebApi**

- `Vortex.Signals/Translators/Habbicons/` : supprimer. `Translators/Collectibles/` : supprimer `MintTokensTranslator`, `RelicMintedTranslator`, `NftClaimsTranslator`, `NftStoreTranslator`, `NftAvatarTranslator` ; garder `VaultIncomeTranslator`. Les traducteurs `Items/*` qui utilisent `RewardTrackFacts` restent tels quels (fichier gardé).
- Tests Signals : adapter `TranslatorCaseTests.cs`, `TranslatorInvariantTests.cs`, `VocabularyGovernanceTests.cs`.
- `Vortex.Observability/Events/CollectibleAuditHandlers.cs` : supprimer les handlers relic/mint, garder les handlers vault.
- WebApi : supprimer `Hosting/WebApiEndpoints.Collectibles.cs`, `Vortex.WebApi.Tests/CollectibleMetadataEndpointTests.cs`. `WebApiEndpoints.cs` : retirer le mapping. `Notifications/RewardNotificationHandlers.cs` : supprimer `RewardTrackNotificationHandler`. `Notifications/ProgressionNotificationHandlers.cs` : supprimer `HabbiconCollectionNotificationHandler`. `openapi.json` : retirer les routes.

- [ ] **Step 8 : dashboard**

- `Vortex.Dashboard.API` : supprimer `Admin/Progression/HabbiconAdminService.cs`, `RewardTrackAdminService.cs`, `Admin/Rules/RewardTrackContentValidator.cs`, `RewardTrackSequenceRules.cs`, `Admin/Hotel/ContentAdminService.NftAvatars.cs`, `Api/Progression/HabbiconReads.cs`, `RewardTrackReads.cs`, `CollectibleReads.cs`, `Api/Platform/BenchmarkReads.cs`, tous les `Contracts/` `Habbicon*`, `RewardTrack*`, `PlayerRewardTrack*`, `PlayerHabbicon*`, `Nft*`, `MintableTypeRow`, `Collectibles*`, `Benchmark*`, les `Operations/` correspondants (`*Habbicon*`, `*RewardTrack*`, `*NftAvatar*`, `BenchmarkOperations.cs`), `Infrastructure/HabbiconArtwork.cs`, `Hosting/Platform/DashboardEndpoints.Benchmark.cs`, `Hosting/Progression/DashboardEndpoints.Rewards.cs`. `Operations/Progression/RewardOperations.cs` : supprimer s'il ne reste rien.
- **`BenchmarkReads` et `BenchmarkOperations` partent ensemble** : `IBenchmarkService` est injecté dans les deux ; en laisser un fait échouer le démarrage du plugin.
- Modifier : `DashboardPlugin.cs`, `Hosting/DashboardEndpoints.cs`, `DashboardEndpoints.Insights.cs` (route collectibles), `DashboardEndpoints.Directory.cs` + `Api/Platform/SignalDirectoryReads.cs` (`HabbiconsDirectoryAsync`, `HabbiconCollectionsDirectoryAsync`, `NftStoreDirectoryAsync`), `DashboardEndpoints.Content.cs` (avatars NFT), `ContentAdminService*.cs`, `ContentOperations.cs`, `Primitives/Content/IContentAdminService.cs`, `ContentAdminContracts.cs` (membres NFT).
- `Vortex.Dashboard.Web` : supprimer `src/pages/HabbiconsPage.svelte`, `RewardTracksPage.svelte`, `CollectiblesPage.svelte`, `BenchmarkPage.svelte`, `src/pages/collectibles/`, `src/components/HabbiconSprite.svelte`, `src/components/sequence/`, `src/lib/sequence/tree.ts`, `tools/sequenceSteps.check.mjs`, `tools/sequenceTree.check.mjs`, `tools/pickerWiring.check.mjs` ; retirer ces checks du script `test` de `package.json`. Modifier `components/AppShell.svelte`, `lib/apiTypes.d.ts`, `lib/api.ts`, `lib/pickers/directories.ts`, `styles.css` (bloc `sequence`), les 4 fichiers de capabilities.
- Tests dashboard : supprimer `RewardTrackAdminServiceTests.cs`, `RewardTrackSequenceRulesTests.cs`, `Hosting/HabbiconArtworkTests.cs` ; adapter `ApiTypeScriptContractTests.cs`, `DashboardResponseSchemaTests.cs`, `authorization-matrix.txt`.

- [ ] **Step 9 : scripts et outils**

```bash
rm -f scripts/sql/import_habbo_collectibles_capture.sql scripts/sql/import_habbo_nft_collections.sql scripts/sql/recover_half_applied_habbicon_migration.sql scripts/sql/resync_habbicon_migration_history.sql scripts/sql/reward_track_sequence_showcase.sql tools/catalog_converter/scan_mintable_nft_clothing.py
```
`scripts/hooks/architecture-walls-baseline.json` : retirer les entrées Benchmark, Collectibles, LoadGen. `authorization-surface-baseline.json` : retirer l'entrée Habbicon.

- [ ] **Step 10 : vérifier**

Relancer la commande du Step 1 → Expected : ne restent que les fichiers gardés volontairement (`RewardTrackFacts.cs`, `RewardTrackActions.cs`, les translators `Items/*`, Emerald/Silver, vault, `GetNftCredits`/`GetSilver`, la colonne `HabbiconId` de la messagerie). Tout autre résultat est à traiter.
Run : `cd Vortex.Dashboard.Web && npm run lint && npm run test && npm run build` → Expected : succès.
Run : `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck` → Expected : succès.

- [ ] **Step 11 : commit**

```bash
DIRS=$(git status --porcelain | sed -E "s#^.. (.* -> )?##" | cut -d/ -f1 | sort -u | xargs)
echo "$DIRS"   # relire : uniquement des dossiers touchés par cette tâche
git add -A -- $DIRS
git status --short   # rien d.autre que cette tâche
git commit -m "m0: retire Collectibles, NFT, Habbicons, RewardTracks, LoadGen et Benchmark" -- $DIRS
```

---

### Task 5 : Wired

**Files :** voir étapes. Le principe : avatars, déplacements et furni publient des `RoomEvent` génériques ; le système wired n'est qu'un écouteur enregistré. Couper l'enregistrement suffit côté émetteurs.

**À déplacer avant de supprimer** (utilisés par les jeux gardés) :
- `IWiredCounter` et `WiredCountdownClock` → `Vortex.Rooms/Games/Runtime/` (renommer en `IGameCounter` et `GameCountdownClock`). Utilisés par `FurnitureGameTimerLogic` (`game_timer`) et `RoomGameChrome.cs`.
- `WiredBotChatType` : utilisé par `RoomBotSystem.Chatter.cs` `SayAsync`. Retirer le paramètre de `SayAsync` (son seul autre appelant est wired) ou déplacer l'enum dans `Vortex.Primitives/Rooms/Bots/` sous le nom `BotChatType`.

**À garder** : `WiredGameStartedEvent`, `WiredGameEndedEvent`, `WiredTeamScoreChangedEvent` (les jeux les publient), `ActionContext.Wired`/`CreateForWired` (la modération l'utilise pour expulser tout le monde), les valeurs d'enum persistées `CommerceOperation.WiredChestContract`, `VaultRewardCategoryType.WiredChest`, `WordFilterSource.Wired`, la colonne `FurnitureEntity.WiredChestEntityId` (filtrée dans 7 requêtes).

- [ ] **Step 1 : constater**

```bash
grep -rIl "Wired" --include=*.cs --exclude-dir=bin --exclude-dir=obj --exclude-dir=Migrations . | wc -l
```

- [ ] **Step 2 : déplacements**

Faire les déplacements ci-dessus, mettre à jour `FurnitureGameTimerLogic.cs` et `RoomGameChrome.cs`.
Run : `dotnet build Vortex.Main/Vortex.Main.csproj` → Expected : succès (rien n'est encore supprimé).

- [ ] **Step 3 : supprimer les dossiers**

```bash
rm -rf Vortex.Rooms/Wired Vortex.Rooms/Object/Logic/Furniture/Floor/Wired Vortex.Rooms/Grains/Systems/WiredTrading Vortex.Rooms/Grains/Storage
rm -rf Vortex.Primitives/Rooms/Wired Vortex.Primitives/Rooms/Snapshots/Wired Vortex.Primitives/Rooms/Enums/Wired
rm -rf Vortex.Protocol/Messages/Incoming/Userdefinedroomevents Vortex.Protocol/Messages/Outgoing/Userdefinedroomevents
rm -rf Vortex.Revisions/Revision20260701/Parsers/UserDefinedRoomEvents Vortex.Revisions/Revision20260701/Serializers/UserDefinedRoomEvents
rm -rf Vortex.PacketHandlers/UserDefinedRoomEvents Vortex.Database/Entities/Wired
```
Vérifier d'abord que `Vortex.Rooms/Grains/Storage/` ne contient que des stores de variables wired (`ls` + grep des usages).

- [ ] **Step 4 : supprimer les fichiers isolés**

- Rooms : `Grains/RoomGrain.Wired.*.cs`, `Grains/RoomGrain.WiredTrading.cs`, `Grains/Systems/RoomWiredSystem*.cs`, `WiredErrorLogCounter.cs`, `WiredSharedVariableGrain.cs`, `Providers/RoomWiredVariablesProvider.cs`, `Object/Logic/Furniture/Floor/FurnitureWiredChestLogic.cs`, `WiredChestStuffData.cs`, `FurnitureContractLogic.cs`.
- Primitives : `Rooms/Grains/IRoomWired*.cs`, `IWiredSharedVariableGrain.cs`, `Rooms/Object/IWiredLimits.cs`, `IRoomFurniAccess.cs`, `IRoomChestAccess.cs`, `IRoomTransactionAccess.cs`, `Rooms/Providers/IRoomWiredVariablesProvider.cs`, `Rooms/Enums/WiredDepositStart.cs`, `Observability/WiredStopReason.cs`, `Orleans/Snapshots/Players/PlayerWiredPreferencesSnapshot.cs`, les événements `RoomWiredStackChangedEvent`, `WiredStackCalledEvent`, `WiredVariableBoxChangedEvent`, `WiredVariableChangedEvent`, `WiredTransactionCompletedEvent`, `WiredTransactionFailedEvent`.
- Protocole : `Protocol/.../Room/Engine/WiredMovementsMessageComposer.cs`, `Revisions/.../Maps/UserDefinedRoomEventsMap.cs`, `Serializers/Room/Engine/WiredMovementsMessageComposerSerializer.cs`, `Serializers/Room/Engine/Data/WiredMovementSerializer.cs`.
- Players : `Grains/PlayerGrain.WiredPreferences.cs`. Base : `Entities/Room/WiredSharedVariableEntity.cs`, `Entities/Players/PlayerWiredPreferencesEntity.cs`.
- Dashboard : `Api/Hotel/WiredReads.cs`, `Contracts/Wired*.cs`, `Dashboard.Web/src/pages/WiredStatsPage.svelte`.
- Scripts : `scripts/hooks/check-wired-param-counts.mjs`, `wired-param-counts-baseline.json`, `check-variable-capabilities.mjs`, `scripts/sql/wired*.sql`. `.claude/agents/vortex-rooms-wireds-researcher.md` : retirer les parties wired (ou supprimer s'il ne reste rien d'utile).

- [ ] **Step 5 : `RoomGrain` et l'état de room**

- `RoomGrain.cs` : retirer les champs `_wiredLogChannel`, `_wiredVariablesProvider`, `WiredSystem`, `WiredTradingSystem`, les paramètres de constructeur `wiredVariablesProvider` et `wiredLogChannel` et leurs affectations, leur construction, `EventModule.Register(WiredSystem)`, `NextWiredBoundaryMs`, l'étape de tick `"wired"`.
- `RoomLiveState.cs` : retirer `AllVariablesHash`, `WiredErrorLogCounters`, `NextWiredBoundaryMs`.
- `RoomGrain.Capabilities.cs` : retirer `IRoomChestAccess`, `IRoomTransactionAccess`, `IRoomFurniAccess` de la liste de base et leurs implémentations ; **garder le bloc `IRoomGameAccess`**.
- `RoomGrain.Furni.Floor.cs` + `Primitives/Rooms/Grains/IRoomFurni.Floor.cs` : retirer `ApplyWiredUpdateAsync` et les méthodes de données/variables wired. `Modules/RoomActionModule.Floor.cs` : retirer `ApplyWiredUpdateAsync`.
- `RoomGrain.Facets.cs` et `Primitives/Rooms/Grains/IRoomGrain.cs` : retirer `IRoomWired`. `GrainFactoryExtensions.cs` : retirer `GetRoomWired`.
- `Primitives/Rooms/Object/IRoomObjectContext.cs` : retirer `Transactions`, `Chests`, `Furni`, `WiredLimits` ; `Rooms/Object/RoomObjectContext.cs` : idem.
- Modération : `RoomGrain.Moderation.cs` et `RoomModerationSystem.cs` : retirer `KickUserFromWiredAsync` et `MuteUserFromWiredAsync`.
- `Configuration/RoomConfig.cs` : retirer `: IWiredLimits` et les propriétés `Wired*`. `RoomModule.cs` : retirer `WiredVariableFeatureProcessor`, le log channel et `RoomWiredLogWriterService`.
- `RoomService.cs` : retirer `GetClickUserStateAsync`/`GetEnabledAchievementsAsync` wired, l'envoi de `WiredEnvironmentMessageComposer` et `WiredPermissionsEventMessageComposer` à l'entrée en room.

- [ ] **Step 6 : le reste du serveur**

- `Players/Grains/PlayerPresenceGrain.Room.cs` et `IPlayerGrain.cs` : retirer les membres wired.
- `PacketHandlers/Preferences/AccountPreferences.cs` : écrire `false` dans les champs wired au lieu de lire les préférences supprimées (forme réseau inchangée).
- Bots : appliquer la décision sur `WiredBotChatType` (Step 2).
- Métriques : `IVortexMetrics.cs` et `Observability/Metrics/VortexMetrics.cs` : retirer les métriques wired.
- Revisions : `Revision20260701.cs` : retirer `new UserDefinedRoomEventsMap()` ; `Maps/RoomMap.cs` : retirer le mapping `WiredMovements`.
- Base : `VortexDbContext.cs` : retirer les DbSets wired et le `using`. `RoomEntity.cs` : retirer les colonnes de permission wired et de fuseau horaire wired si plus rien ne les lit.
- Les achievements et quêtes ne reçoivent plus de progression « directe » de wired : les membres concernés (`IPlayerAchievementGrain` set-progress, `SignalActions.Wired`) peuvent rester ; les retirer seulement si c'est trivial.

- [ ] **Step 7 : dashboard**

- Capability `WiredRead` : retirer des 4 fichiers. `DashboardPlugin.cs` : retirer `WiredReads`. `Hosting/Platform/DashboardEndpoints.Stats.cs` : retirer les routes wired.
- Enquête « déplacements de coffre » : `Api/Platform/DirectoryReads.cs` (`ChestMoves`), `ChestMoveRow.cs`, `IdSearch.ChestMoves`, `apiTypes.d.ts`, `InvestigationPage.svelte` : retirer.

- [ ] **Step 8 : hooks et build**

- `Directory.Build.targets` : retirer l'`Exec` de `check-variable-capabilities.mjs`.
- `scripts/hooks/__test/run.mjs` : retirer les cas variable-capabilities (bloque / passe), la sonde `Vortex.Rooms/Wired/Variables/__HookProbeVariable.cs` (création et nettoyage), `fs.rmSync(capabilityProbe)`.
- `check-inert-declarations.mjs` : les sections wired deviennent vides ; vérifier que le script passe toujours (il teste `existsSync`).
- `node scripts/hooks/check-logic-groups.mjs --write` : régénère les groupes de logique (`furnitureEnums.ts`) sans les `wf_*`. Retirer aussi les lignes du groupe `Wired:` dans le script.
- Régénérer ou nettoyer `authorization-surface-baseline.json`, `sibling-divergence-baseline.json`, `inert-declarations-baseline.json`.
- `CLAUDE.md`/`AGENTS.md` : retirer les lignes sur `check-variable-capabilities`, `check-wired-param-counts`, et toute règle propre aux wired.

- [ ] **Step 9 : tests**

- Supprimer : `Vortex.Rooms.Tests/Wired/`, `Bots/WiredBotTalkConfigurationTests.cs`, `Furniture/WiredChestPreviewTests.cs`, `Vortex.Database.Tests/Wired/WiredPersistenceInvariantsTests.cs`, `Vortex.PacketHandlers.Tests/WordFilterWiredHandlerTests.cs`, `Vortex.Revisions.Tests/UserDefinedRoomEvents/`, `Vortex.Revisions.Tests/Wired/VariableFxWireTests.cs`.
- Adapter (paramètres de constructeur `IRoomWiredVariablesProvider` et `RoomWiredLogChannel` retirés) : `Vortex.Rooms.Tests/Support/RoomHarness.cs`, `Grains/TickBoundaryTests.cs`, `Chat/ChatFloodTests.cs`, `Chat/HotelWordFilterTests.cs`, `Chat/RoomFilterWordsTests.cs`, `Moderation/HotelMuteEnforcementTests.cs`, `Pets/PetCareTests.cs`.
- Adapter : `Bots/RoomBotSystemTests.cs` (tests wired ~l. 682-740 : supprimer), `Furniture/RoomObjectHydrationBenchmark.cs` (utilise `WiredTestBoxes` : le remplacer par des furni ordinaires), `Vortex.Hosting.Tests/Architecture/RoomEntryPayloadTests.cs` (plus de composers wired à l'entrée), `Architecture/ConfiguredBudgetTests.cs` (retirer les budgets wired), `Vortex.Dashboard.Tests` (`ApiTypeScriptContractTests`, `DashboardResponseSchemaTests`, `DashboardControlPlaneMetricsTests` : faux de métriques), `Vortex.Revisions.Tests/Room/EmptySerializerBatchWireTests.cs`.
- Garder `Vortex.Revisions.Tests/Preferences/PreferencesWireLayoutTests.cs` : il doit passer sans changement (la forme réseau n'a pas bougé).

- [ ] **Step 10 : vérifier**

```bash
grep -rIl "Wired" --include=*.cs --exclude-dir=bin --exclude-dir=obj --exclude-dir=Migrations .
```
Expected : seulement les fichiers gardés volontairement (événements `WiredGame*`/`WiredTeamScoreChanged`, `ActionContext`, enums persistés, `FurnitureEntity.WiredChestEntityId`, `AccountPreferences` et son composer). Tout autre fichier est à traiter.
Run : `node scripts/hooks/__test/run.mjs` → PASS. `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck` → succès.

- [ ] **Step 11 : commit**

```bash
DIRS=$(git status --porcelain | sed -E "s#^.. (.* -> )?##" | cut -d/ -f1 | sort -u | xargs)
echo "$DIRS"   # relire : uniquement des dossiers touchés par cette tâche
git add -A -- $DIRS
git status --short   # rien d.autre que cette tâche
git commit -m "m0: retire les wired (moteur, variables, coffres, protocole, dashboard)" -- $DIRS
```

---

### Task 6 : Jeux Freeze, BattleBanzai, Football

**Files :**
- Supprimer : `Vortex.Rooms/Games/Freeze/`, `Games/BattleBanzai/`, `Games/Football/`, `Vortex.Rooms.Tests/Freeze/`, `Banzai/`, `Football/`, `scripts/sql/football_ball_logic_binding.sql`
- Garder : `Games/Abstractions`, `Arena`, `Teams`, `Runtime`, `Scoring`, `Events`, `Presentation`, `Progression`, `RoomGameAttribute`, `RoomGameFeatureProcessor`, `Object/Logic/Furniture/Floor/Games/*`, `Primitives/Rooms/Games/*` (génériques, ils serviront aux arènes PvP)
- Test : `Vortex.Rooms.Tests/Games/TestOnlyGame.cs` (nouveau)

**Interfaces :**
- Produit : `TestOnlyGame`, un `[RoomGame]` minimal visible seulement dans l'assembly de tests, pour que les tests du runtime de jeux aient encore un jeu à héberger.

- [ ] **Step 1 : constater**

```bash
grep -rIl "Freeze\|Banzai\|Football" --include=*.cs --exclude-dir=bin --exclude-dir=obj --exclude-dir=Migrations .
```

- [ ] **Step 2 : supprimer**

```bash
rm -rf Vortex.Rooms/Games/Freeze Vortex.Rooms/Games/BattleBanzai Vortex.Rooms/Games/Football Vortex.Rooms.Tests/Freeze Vortex.Rooms.Tests/Banzai Vortex.Rooms.Tests/Football
rm -f scripts/sql/football_ball_logic_binding.sql
```
`FootballBallComponent` portait `[RoomObjectLogic("furniture_pushable")]` : les balles retombent sur la logique par défaut (décision 7).

- [ ] **Step 3 : configuration, signals, achievements**

- `Primitives/Server/ConfigKeyCatalog.cs` : retirer les clés Freeze, Banzai, Football.
- `Floor/Games/FurnitureScoreboardLogic.cs` : retirer les clés `freeze_counter_*`.
- `Signals/Translators/Games/GamePointsTranslator.cs`, `SignalActions.cs`, `RewardTrackActions.cs` : retirer les actions propres à ces jeux. `Progression/Achievements/Events/AchievementSignalConsumer.cs` et `AchievementNames` : retirer les achievements de ces jeux.
- `Navigator/NavigatorPromotedCategoryCodes.cs` : retirer les codes de ces jeux.
- `GameAuraSet.Freeze` : garder (valeur d'enum inoffensive).
- `node scripts/hooks/check-logic-groups.mjs --write` après avoir retiré les lignes Freeze/Banzai/Football du script.

- [ ] **Step 4 : écrire le jeu de test (test rouge)**

`Vortex.Rooms.Tests/Games/RoomGameRuntimeTests.cs` et `MultiArenaRoomTests.cs` construisaient des rooms qui hébergent Freeze ou Banzai. Créer `Vortex.Rooms.Tests/Games/TestOnlyGame.cs` sur le modèle exact de la plus petite des classes `[RoomGame]` supprimées (lire sa version dans `git show HEAD:Vortex.Rooms/Games/Football/<fichier>.cs`) : même interface `IRoomGame`, même constructeur avec `IRoomGameContext`, aucune règle de jeu, un score par équipe. Faire pointer les deux fichiers de test vers `TestOnlyGame` à la place des jeux supprimés.

Run : `dotnet test Vortex.Rooms.Tests --filter "FullyQualifiedName~Games"` → Expected : échec de compilation ou de test tant que `TestOnlyGame` n'est pas complet ; corriger jusqu'au vert.

- [ ] **Step 5 : autres tests**

- `Games/RoomItemIndexTests.cs` : remplacer `FreezeGateComponent` par une logique de furni gardée.
- `Games/RoomGameChromeTests.cs` (aura Freeze : garder la valeur d'enum), `Signals.Tests/ConsumerTableTests.cs`, `TranslatorCaseTests.cs`, `Database.Tests/Achievements/AchievementSeedLevelTests.cs` : retirer les cas des jeux supprimés.

- [ ] **Step 6 : vérifier**

Relancer la commande du Step 1 → Expected : seulement `GameAuraSet.Freeze` et des commentaires.
Run : `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck` → succès.

- [ ] **Step 7 : commit**

```bash
git add -A -- Vortex.Rooms Vortex.Rooms.Tests Vortex.Primitives Vortex.Signals Vortex.Signals.Tests Vortex.Progression Vortex.Navigator Vortex.Database.Tests Vortex.Dashboard.Web scripts
git commit -m "m0: retire Freeze, BattleBanzai et Football ; le runtime de jeux reste pour les arènes" -- Vortex.Rooms Vortex.Rooms.Tests Vortex.Primitives Vortex.Signals Vortex.Signals.Tests Vortex.Progression Vortex.Navigator Vortex.Database.Tests Vortex.Dashboard.Web scripts
```

---

### Task 7 : Jukebox, Mystery box, Crackable, Rating, Caméra

Cinq features indépendantes : faire un sous-commit par feature à l'intérieur de la tâche (build vert entre chaque) si c'est plus confortable. Le grep de contrôle de chaque feature doit être vide avant de passer à la suivante.

**7a — Jukebox**

- Supprimer : `Vortex.Rooms/Grains/RoomGrain.Jukebox.cs`, `Systems/RoomJukeboxSystem.cs`, `Systems/JukeboxDiskStore.cs`, `Primitives/Rooms/Grains/IRoomJukebox.cs`, `Primitives/Sound/Snapshots/{JukeboxLoadResult,JukeboxPlaylistSnapshot,NowPlayingSnapshot}.cs`, `PacketHandlers/Sound/{AddJukeboxDisk,GetJukeboxPlayList,RemoveJukeboxDisk,GetNowPlaying}MessageHandler.cs` avec leurs messages, parsers, composers (`JukeboxPlayListFull`, `JukeboxSongDisks`, `NowPlaying`, `PlayList*`) et serializers, `Vortex.Rooms.Tests/Sound/JukeboxDiskMoveTests.cs`.
- Modifier : `RoomGrain.cs` (`_songProvider`, `JukeboxSystem`, étape de tick `"jukebox"`), `RoomConfig.JukeboxCapacity`, `IRoomGrain.cs`, `GrainFactoryExtensions` (`GetRoomJukebox`), `Revisions/.../Maps/SoundMap.cs`, les faux `ISongProvider` des fichiers de test qui construisent `RoomGrain`, `Revisions.Tests/Sound/SoundWireLayoutTests.cs`.
- Garder : `FurnitureEntity.JukeboxEntityId` (filtré dans 6 requêtes), `ISongProvider`, `SongProvider`, `SongsPage` et les messages d'info de chanson (indépendants du jukebox).
- Contrôle : `grep -rIl "Jukebox" --include=*.cs --exclude-dir=bin --exclude-dir=obj --exclude-dir=Migrations .` → seulement `FurnitureEntity` et ses requêtes.

**7b — Mystery box**

- Supprimer : `Vortex.Rooms/Grains/RoomGrain.MysteryBox.cs`, `RoomMysteryBoxSession.cs`, `Systems/RoomMysteryBoxSystem.cs`, `RoomService.MysteryBox.cs`, `Object/Logic/Furniture/Floor/FurnitureMysteryBoxLogic.cs`, `FurnitureMysteryTrophyLogic.cs`, `Vortex.Players/Grains/{MysteryBoxManagerGrain,PlayerMysteryBoxGrain}.cs`, `Vortex.Primitives/MysteryBox/`, `Primitives/Rooms/Grains/IRoomMysteryBox.cs`, `Primitives/Events/MysteryBoxEvents.cs`, `Protocol/.../Mysterybox`, `Revisions/.../{Parsers,Serializers}/MysteryBox`, `Maps/MysteryBoxMap.cs`, `PacketHandlers/MysteryBox/`, `PacketHandlers/Room/Furniture/OpenMysteryTrophyMessageHandler.cs` (+ message, parser), `Signals/Translators/Items/{MysteryBoxTranslator,MysteryTrophyTranslator}.cs`, `Observability/Events/MysteryBoxAuditHandlers.cs`, tous les fichiers `*MysteryBox*` de `Vortex.Dashboard.API`, `Dashboard.Web/src/pages/MysteryBoxPage.svelte`, `Database/Entities/MysteryBox/PlayerMysteryBoxKeyEntity.cs`, `Rooms.Tests/MysteryBox/`, `Players.Tests/MysteryBox/`.
- Modifier : `RoomGrain.cs` (champ, construction, étape de tick `"mystery-box"`), `RoomLiveState.cs`, `RoomGrain.Avatar.cs` et `Modules/RoomObjectModule.cs` (annulation de session), `RoomConfig`, `IRoomService.cs`, `IRoomGrain.cs`, `RoomGrain.Facets.cs`, `GrainFactoryExtensions`, `Maps/RoomMap.cs`, `Revision20260701.cs` (`MysteryBoxMap`), `Inventory/Grains/InventoryGrain.Furni.cs` (`RefreshMysteryBoxTrackerAsync` et ses appelants), `SSOTicketMessageHandler.cs` (clés à la connexion), `Vortex.Main/Program.cs` (rechargement), `Vortex.Main/Console/ConsoleCommandDispatcher.cs` (commandes `mystery-key`/`mystery-box`), `PlayerModule.cs`, `Facts.cs`, `SignalActions`, `RewardTrackActions`, `ItemEvents.cs`, dashboard (`DashboardPlugin`, `DashboardEndpoints.cs`, 4 fichiers de capabilities), `VortexDbContext`.
- Tests à adapter : `Database.Tests/Commerce/InventoryGrainFixture.cs`, `Hosting.Tests/ConsoleCommandDispatcherTests.cs`, `Dashboard.Tests` (`ApiTypeScriptContractTests`, `DashboardResponseSchemaTests`). Les tests de prix (`PrizePickerTests`, `PrizeVariantsTests`, `PlayerPrizeGrainTests`) utilisent `MysteryBoxColors` : déplacer cette énumération dans `Vortex.Primitives/Prizes/` si les pools de prix restent, sinon adapter.
- Garder le seed `mystery_box.sql` (Tâche 11).
- Contrôle : `grep -rIl "MysteryBox\|MysteryTrophy" --include=*.cs --exclude-dir=bin --exclude-dir=obj --exclude-dir=Migrations .` → vide (ou seulement l'énumération déplacée, renommée).

**7c — Crackable**

- Supprimer : `RoomGrain.Crackable.cs`, `Systems/RoomCrackableSystem.cs`, `Primitives/Rooms/Grains/IRoomCrackable.cs`, `Floor/FurnitureCrackableLogic.cs`, `FurnitureRewardBoxLogic` (`furniture_ecotron_box`, `furniture_nft_reward_box`, `furniture_effectbox`), `FurnitureWelcomeGiftLogic`, `Rooms.Tests/.../CrackableStuffDataTests` seulement si son sujet est le système (garder s'il teste le format de stuff data).
- Modifier : `RoomGrain.cs`, `IRoomGrain`, `RoomGrain.Facets.cs`, `GrainFactoryExtensions` (`GetRoomCrackable`), et `ClaimWelcomeGiftAsync` (retirer).
- **Garder** `ICrackableStuffData`, `CrackableStuffData`, `StuffDataType.CrackableKey`, `CrackableStuffSnapshot` et leur sérialisation : des items existants portent ce format.
- Tests à adapter : `PersistStuffDataTests`, `RoomObjectLogicKeyTests`, `FurnitureLogicStuffDataTypeTests`, `StuffDataRoundTripTests` (retirer les clés de logique supprimées, garder les cas de format).
- Garder les seeds `crackable.sql`, `crackable_and_highscore_families.sql` (Tâche 11).
- Contrôle : `grep -rIl "RoomCrackable\|RewardBoxLogic\|WelcomeGift" --include=*.cs --exclude-dir=bin --exclude-dir=obj --exclude-dir=Migrations .` → vide.

**7d — Rating**

- Supprimer : `Systems/RoomRatingSystem.cs`, `Database/Entities/Room/RoomRatingEntity.cs` (+ DbSet), `Signals/Translators/Rooms/RoomRatedTranslator.cs`, `Rooms.Tests/Rooms/RoomRatingTests.cs`, `RateFlatMessage` + parser + `PacketHandlers/Navigator/RateFlatMessageHandler.cs`.
- Modifier : `RoomGrain.cs`, `RoomGrain.Settings.cs`, `IRoomSettings.cs`. `RoomService.cs` : **continuer d'envoyer** `RoomRatingMessageComposer` à l'entrée en room avec `CanRate = false` (`RoomEntryPayloadTests` l'attend). `RoomRatedEvent` : retirer avec ses consommateurs (`RoomLifecycleAuditHandlers.cs`, `WebApi/Notifications/RoomNotificationHandlers.cs`, `WebApi.Tests/NotificationTests.cs`).
- Garder `RoomEntity.Score` (le navigator l'utilise).
- Contrôle : `grep -rIl "RoomRating\|RoomRated\|RateFlat" --include=*.cs --exclude-dir=bin --exclude-dir=obj --exclude-dir=Migrations .` → seulement `RoomRatingMessageComposer` et son serializer.

**7e — Caméra et photos web** (décision 4)

- Supprimer : `Vortex.Rooms/Camera/`, `Vortex.Primitives/Camera/`, `Vortex.Players/Grains/PlayerCameraGrain.cs`, `PacketHandlers/Camera/`, les messages et composers Camera de `Protocol` (dont `Room/Camera/class_1476`), parsers/serializers Camera et `Maps/CameraMap.cs`, `Rooms.Tests/Camera/`, `Revisions.Tests/Camera/`.
- Photos web : `CameraPhotoEntity`, `ICameraPhotoStore`, `CameraPhotoStore`, `WebApiPhotoService`, `WebApiAlbumService`, la partie photo de `WebApiFeedService`, `WebApiEndpoints.Camera.cs`, les clés étrangères photo de `WebAlbumPhotoEntity`/`WebFeedPostPhotoEntity` (supprimer les entités d'album photo et de photo de post), `CameraPhotoEndpointTests` et les tests photo/album/fil. Le fil d'actualité garde ses posts texte et ses sondages de post (`WebFeedPoll*`).
- Modifier : `RoomModule.cs`, `RoomConfig`, `ConfigKeyCatalog` (clés camera), `GrainFactoryExtensions`, `Revision20260701.cs` (`CameraMap`), `Vortex.Rooms.csproj` (retirer SkiaSharp), `Directory.Packages.props` (versions SkiaSharp si plus utilisées), `SSOTicketMessageHandler` (perk caméra à `false`), `WebApiPlugin`, `WebApiWebHost` (`ForwardSingletons`), `WebApiEndpoints.cs`, `openapi.json`, `WebApi.Tests` (Fakes, factory).
- **Garder** : `SetRoomCameraPreferences` (préférence de suivi de la caméra de room), `CallForHelpFromPhoto`, les champs `CameraInit*` du mapping de room, l'objet en main nommé « Camera ».
- Contrôle : `grep -rIl "CameraPhoto\|PlayerCamera\|Vortex.Rooms.Camera\|SkiaSharp" --include=*.cs --include=*.csproj --include=*.props --exclude-dir=bin --exclude-dir=obj --exclude-dir=Migrations .` → vide.

- [ ] **Step 1 : 7a**, contrôle vide, `dotnet build Vortex.Main/Vortex.Main.csproj && dotnet test Vortex.Cloud.sln` vert.
- [ ] **Step 2 : 7b**, idem.
- [ ] **Step 3 : 7c**, idem.
- [ ] **Step 4 : 7d**, idem.
- [ ] **Step 5 : 7e**, idem, puis `node scripts/hooks/check-webapi-forwarding.mjs` vert.
- [ ] **Step 6 : vérifier le tout**

Run : `node scripts/hooks/check-logic-groups.mjs --write`, puis `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck` → succès.

- [ ] **Step 7 : commit**

```bash
DIRS=$(git status --porcelain | sed -E "s#^.. (.* -> )?##" | cut -d/ -f1 | sort -u | xargs)
echo "$DIRS"   # relire : uniquement des dossiers touchés par cette tâche
git add -A -- $DIRS
git status --short   # rien d.autre que cette tâche
git commit -m "m0: retire jukebox, mystery box, crackable, notes de room et caméra" -- $DIRS
```

---

### Task 8 : Pets — élevage, plantes, besoins

Le cœur des pets reste (placement, mouvement, commandes, animations, caresses, XP de commande, jouets, niche) ainsi que la monture. Lire `RoomPetSystem.Care.cs` en entier : malgré son nom, il contient surtout le cœur (respect, approche, vocalises, `IssueCommandAsync`, `GrantPetCommandXpAsync`, `GrantXpAndLevelUpAsync`) ; seul `GiveSupplementToPetAsync` et la branche plante sont à retirer.

**Files :**
- Supprimer : `Vortex.Rooms/Grains/Systems/RoomPetSystem.Breeding.cs`, `RoomPetSystem.Plants.cs`, `Object/Logic/Furniture/Floor/FurnitureMonsterplantSeedLogic.cs`, `FurniturePetProductLogic.cs` (`pet_food`), `FurniturePetDrinkLogic.cs`
- Supprimer (Primitives) : `Pets/Snapshots/PetBreedingOutcomeSnapshot.cs`, `PetBreedingParentSnapshot.cs`, `PetFeedResult.cs`, `Rooms/Enums/PetBreedingActionType.cs`
- Supprimer (protocole, avec parser et handler de chacun) : `Incoming/Room/Pets/{BreedPets,CompostPlant,HarvestPet}Message`, `Incoming/Inventory/Pets/{CancelPetBreeding,ConfirmPetBreeding}Message`, `Incoming/Room/Engine/TogglePetBreedingPermissionMessage`, `Incoming/Room/Engine/GiveSupplementToPetMessage` ; composers `Inventory/Pets/{ConfirmBreedingRequest,ConfirmBreedingResult,GoToBreedingNestFailure,NestBreedingSuccess,PetBreeding}EventMessageComposer`, `Room/Pets/PetBreedingResultEventMessageComposer` et leurs serializers
- Supprimer : `Database/Entities/Pets/PetFoodEntity.cs`, `Signals/Translators/Pets/PetFedTranslator.cs`, `Progression/Achievements/Events/PetBredAchievementHandler.cs`, `Achievements/BabyPetAchievements.cs`
- **Garder** : `CustomizePetWithFurniMessage` + handler, réduits à la selle (décision 5) ; `FurniturePetNestLogic`, `FurniturePetToyLogic`, `RoomPetSystem.Riding.cs`, `respect.pet_daily_limit`, `PetRespectedTranslator`
- Garder les seeds `pet_food.sql` et ceux des migrations `ReseedPetFoodFromFurnidata`, `AddPetThirst` (Tâche 11)

- [ ] **Step 1 : test de la selle (rouge)**

Aujourd'hui aucun code ne met `HasSaddle = true` ; la selle passe par `CustomizePetWithFurni`, que le handler n'oriente que vers la nourriture et les potions de plante. Ajouter dans `Vortex.Rooms.Tests/Pets/` (fichier `PetSaddleTests.cs`, en suivant la structure de `PetCareTests.cs` et `RoomHarness`) un test : un propriétaire applique un furni de selle sur son cheval via le même chemin que `CustomizePetWithFurni` → le snapshot du pet a `HasSaddle == true` et le furni est consommé. Un second test : un joueur non propriétaire → refus, `HasSaddle` reste `false`.

Pour identifier un furni de selle, lire comment le client l'envoie (`FurniturePetCustomizationLogic` côté client, ou le type de produit du furni en base) et réutiliser l'identification existante des produits de pet (`PetSupplementType` ou équivalent) ; ne pas inventer de clé.

Run : `dotnet test Vortex.Rooms.Tests --filter "FullyQualifiedName~PetSaddle"` → Expected : FAIL.

- [ ] **Step 2 : implémenter la selle**

Dans le handler `CustomizePetWithFurniMessageHandler` et la méthode de room qu'il appelle : remplacer les branches nourriture et potion de plante par une seule branche selle qui vérifie la propriété du pet, consomme le furni et met `HasSaddle = true` (persisté comme les autres champs du pet).

Run : le même filtre → Expected : PASS.

- [ ] **Step 3 : besoins constants**

- `RoomPetSystem.Motion.cs` : supprimer `FeedPetAsync`, `ApplyNeedDecay`, `ApplyOfflineDecay`, `TryDirectPetToFood`, `AutoFeedPetAtBowlAsync`, `UpdateFoodItemInLiveStateAsync`.
- `RoomPetRuntime.cs` et `PetSnapshot`/`PetEntity` : `Nutrition`, `Thirst`, `Energy` valent toujours leur maximum (garder les champs : ils sont sur le réseau). Le coût d'énergie des commandes, le sommeil à `Energy == 0`, le seuil de jeu `PlayEnergyThreshold` et la recherche de niche par `IsTired` deviennent sans effet ; retirer seulement les branches devenues mortes, garder la niche accessible par la commande 13.
- La commande 43 (manger) : retirer son effet. `SelectVocalForState` : retirer les branches faim/soif/fatigue.
- `RoomPetSystem.HandItem.cs` : `ConsumeHandItemAsync` ne nourrit plus ; retirer l'apport de Nutrition/Thirst et `PetFedEvent`. Donner un objet en main à un pet ne fait plus rien d'autre que l'animation existante, s'il y en a une.
- `Vortex.Rooms/Configuration/PetConfig.cs` et `PetTuning.cs` : retirer les clés `pets.plant_*` et les clés nutrition/énergie/soif/faim/supplément.

- [ ] **Step 4 : élevage et plantes**

- `RoomPetSystem.cs` : retirer `MonsterplantPetType` et la branche plante de `ProcessPetsAsync`, `_breedingByPetOneId`, `PendingBreedingSession`, `PetMotionState.LastPlantTickAtMs`, les horloges de besoins, `FeedTargetId`.
- `RoomPetRuntime.cs` : `CanBreed`, `CanHarvest`, `CanRevive`, `HasBreedingPermission` renvoient `false` ; retirer `IsPlant`, `PlantWellBeing`, `PlantLevelFor`, `FeedAsync`, `HasServingsLeft`, `TakeWholeNeedPoints`, la branche plante de `ToFigureString`.
- `RoomPetSystem.Care.cs` : retirer `TreatPlantAsync` et `GiveSupplementToPetAsync`. `RoomPetSystem.Placement.cs` : retirer les lignes plante/élevage. `RoomPetSystem.Riding.cs` : `SendPetStatusAsync` écrit `false` pour `CanBreed`, `CanHarvest`, `CanRevive`, `HasBreedingPermission` (forme réseau gardée).
- `Vortex.Rooms/Grains/RoomGrain.Pets.cs` et `Primitives/Rooms/Grains/IRoomPets.cs` : retirer Feed, GiveSupplement, toutes les méthodes Breeding, PlantSeed, UsePetProduct, TreatPlant, Harvest, Compost.
- `RoomGrain.Avatar.cs` (objet en main) et `RoomPersistenceGrain.cs` (champs de besoins) : adapter.
- `PacketHandlers/Room/Pets/GetPetInfoMessageHandler.cs` : champs plante, bien-être, élevage à leur valeur neutre ; forme réseau gardée.
- `Primitives/Events/PetEvents.cs` : retirer `PetBredEvent` et `PetFedEvent`. `SignalActions.cs`, `RewardTrackActions.cs` : retirer `FeedPet`. `AchievementSignalConsumer.cs`, `AchievementNames.cs`, `PlayerAchievementGrain.cs`, `IPlayerAchievementGrain.ProgressBabyPetColoursAsync` : retirer les achievements d'élevage.
- `FurnitureCategory.Monsterplant*`, `ItemDeletionReason.PetFoodUsedUp` : retirer sans renuméroter.
- `Revision/Maps/RoomMap.cs`, `InventoryMap.cs` : retirer les mappings supprimés. `VortexDbContext.cs` : retirer `PetFood`.
- `PetSnapshot`/`PetEntity` : retirer `ParentOneId`, `ParentTwoId`, `CanBreed`, `LastWateredAt`, `RarityLevel`. `PetCreateRequest`, `InventoryGrain.Pets.cs` : adapter (plus de Nutrition à la création).
- Dashboard : `Api/Hotel/PetReads.cs`, `Contracts/PetTotals.cs` (retirer moyennes énergie/nutrition et compte d'élevables), `PetsStatsPage.svelte`, bloc `petsStats` des locales.

- [ ] **Step 5 : tests**

- Supprimer : `Vortex.Rooms.Tests/Pets/MonsterplantLifecycleTests.cs`, `PetFeedingTests.cs`, `PetBowlAvailabilityTests.cs`, `PetNeedDecayTests.cs`, `PetNeedsAreSeparateTests.cs`, `PetAgeAndHappinessTests.cs` (s'il ne teste que les besoins ; sinon garder la partie âge), `Vortex.Database.Tests/Pets/PetFoodSeedTests.cs`.
- Adapter : `PetHandItemTests` (plus de nourriture), `PetNestSeekingTests` (la niche par commande reste, plus par fatigue), `PetCareTests`, `PetActionFlagsTests`, `PetToyAndTuningTests`, `PetScratchApproachTests`, `Furniture/RoomObjectLogicFamilyTests`, `Support/RoomHarness.cs`, `Grains/RoomPersistenceLossWindowTests`, `Vortex.Database.Tests/Pets/PetModelTests`, `Vortex.Revisions.Tests/Room/ShortReadWireTests.cs`, `Vortex.Signals.Tests` (`ConsumerTableTests`, `VocabularyGovernanceTests`), `Vortex.Database.Tests/Achievements/AchievementSeedLevelTests`.
- Les tests de forme réseau du statut et des infos de pet doivent passer **sans modifier les octets attendus**.

- [ ] **Step 6 : vérifier**

```bash
grep -rIl "Breed\|Monsterplant\|PetFood\|PetFed\|GiveSupplement\|Compost\|HarvestPet" --include=*.cs --exclude-dir=bin --exclude-dir=obj --exclude-dir=Migrations .
```
Expected : seulement les champs réseau neutres (`CanBreed`, `HasBreedingPermission` dans les composers/serializers) et la ligne `PetBreedingResult` éventuelle dans `Headers.cs`.
Run : `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck` → succès.

- [ ] **Step 7 : commit**

```bash
DIRS=$(git status --porcelain | sed -E "s#^.. (.* -> )?##" | cut -d/ -f1 | sort -u | xargs)
echo "$DIRS"   # relire : uniquement des dossiers touchés par cette tâche
git add -A -- $DIRS
git status --short   # rien d.autre que cette tâche
git commit -m "m0: pets sans élevage, plantes ni besoins ; la selle passe par CustomizePetWithFurni" -- $DIRS
```

---

### Task 9 : Builders Club, classification, LTD, vouchers, offres ciblées, sondages

**Rappel** : `Vortex.Players/Club/` est le Habbo Club (HC), **pas** le Builders Club : ne pas y toucher.

**9a — Builders Club**

- Supprimer : `Vortex.Players/BuildersClubService.cs`, `BuildersClubTierSeederService.cs`, `Events/BuildersClubGrantedPushHandler.cs`, `Primitives/Players/IBuildersClubService.cs`, `Players/BuildersClubSubscriptionSnapshot.cs`, `Catalog/Tags/BuildersClubCatalog.cs`, `Database/Entities/Players/BuildersClubTierEntity.cs` (+ DbSet `BuildersClubTiers`), `Vortex.Inventory/Fulfillment/BuildersClubGrant.cs`, `Strategies/BuildersClubFulfillmentStrategy.cs`, `Progression/Achievements/Events/BuildersClubAchievementHandler.cs`, `PacketHandlers/Catalog/BuildersClub{PlaceRoomItem,PlaceWallItem,QueryFurniCount}MessageHandler.cs`, `BuildersClubPlacementHelper.cs`, leurs messages/parsers, `Outgoing/Catalog/BuildersClub{FurniCount,SubscriptionStatus}MessageComposer`, `Outgoing/Room/Engine/BuildersClubPlacementWarningMessageComposer` et serializers, dashboard `Contracts/BuildersClubTierRow.cs`, `Operations/Hotel/Contracts/{BuildersClubTierRequest,DeleteBuildersClubTierRequest}.cs`, tests `Database.Tests/Commerce/BuildersClubGrantTests.cs`, `Rooms.Tests/Rooms/BuildersClubRoomSealTests.cs`, `Players.Tests/Achievements/BuildersClubMembershipDaysTests.cs`.
- Modifier :
  - `PlayerModule.cs` ; catalogue : `CatalogModule.cs`, `CatalogService.cs`, `CatalogType.BuildersClub`, entrées `BuildersClub*` de `CatalogPageLayouts`, `CatalogProductSnapshot`, `CatalogProductEntity.BuildersClubEligible`, `CatalogSnapshotProvider`, `CatalogClubGiftProvider`, `ProductType.BuildersClubDays/Furni`.
  - `PurchaseFromCatalogMessageHandler.cs` (routage d'achat BC), `SSOTicketMessageHandler.cs` (statut BC à la connexion), `RoomService.cs` (scellé de room BC), `NavigatorProvider.cs` (filtre des rooms scellées).
  - Inventaire : `InventoryGrain.Furni.cs` (dont `GrantSingleFurnitureIfUnderLimitAsync`, propre au BC), `IInventoryGrain.Furni.cs`, `FulfillmentPlan.cs`, `IFulfillmentStrategy.cs`, `CatalogFulfillmentPlanner.cs`.
  - `FurnitureEntity.IsBuildersClub`, `SubscriptionType.BuildersClub`, `PlayerEvents.BuildersClubMembershipGrantedEvent`, `AchievementNames.BuildersClub` (enums : sans renuméroter).
  - WebApi : `WebApiPlayerService.cs`, `IWebApiPlayerService` (`BuildersClubDays`), `openapi.json`.
  - Revision : `CatalogMap.cs`, `RoomMap.cs`.
  - Dashboard API : `DashboardPlugin.cs`, `CatalogAdminService.cs`, `ContentAdminService.Hotel.cs`, `IContentAdminService`, `ContentAdminContracts.BuildersClubTierSpec`, `ContentOperations.cs`, `DashboardEndpoints.Content.cs`, `EconomyReads.Extras.cs`, `EconomyExtras*.cs`, `CatalogReads.cs`, `CatalogOperations.cs`, `CatalogAdminContracts`. Dashboard Web : onglet builders de `EconomyExtrasPage.svelte`, sélecteur de type et layouts BC de `CatalogPage.svelte`, locales.
  - `Vortex.Main/ReferenceDataReloader.cs`.
  - Tests : `CatalogPurchaseHarness`, `CatalogTypeIsolationTests`, `CommerceFaultHarness`, `CatalogFulfillmentPlannerTests`, `RoomCreationGuardsTests`, `CatalogAdminServiceTests`, `FurnitureAdminServiceTests`.
- Ne pas toucher à l'éditeur de plan de sol (`UpdateFloorPropertiesMessageHandler`, `FloorPlanUpdate`) : seuls ses commentaires citent le BC.
- Contrôle : `grep -rIl "BuildersClub" --include=*.cs --include=*.svelte --include=*.ts --include=*.js --exclude-dir=bin --exclude-dir=obj --exclude-dir=Migrations --exclude-dir=node_modules .` → vide.

**9b — Classification des utilisateurs**

- Supprimer : `Vortex.Players/UserClassificationService.cs`, `Primitives/Moderation/{IUserClassificationService,UserClassificationEntry,UserClassifications}.cs`, `Vortex.PacketHandlers/UserClassification/`, `Protocol/Messages/Incoming/Userclassification/*`, `Outgoing/Userclassification/UserClassificationMessageComposer.cs`, `Revision/Parsers/UserClassification/*`, `Revision/Serializers/UserClassification/*`, `Revision/Maps/UserClassificationMap.cs`.
- Modifier : `Revision20260701.cs`, `PlayerModule.cs`, `Vortex.Revisions.Tests/Moderation/ModerationWireAdditionsTests.cs`.
- Contrôle : `grep -rIl "UserClassification" ...` → vide.

**9c — LTD**

- Supprimer : `Vortex.Catalog/LtdScheduleService.cs`, `Grains/LtdRaffleGrain.cs`, `Grains/LtdRaffleEntryResults.cs`, `Primitives/Catalog/ILtdScheduleService.cs`, `Catalog/Grains/ILtdRaffleGrain.cs`, `Snapshots/{LtdRaffleEntryResult,LtdSeriesSnapshot,UpcomingLtdSnapshot}.cs`, `LimitedOfferAppearanceSnapshot.cs`, `CatalogPageExpirySnapshot.cs`, `Database/Entities/Catalog/{LtdSeriesEntity,LtdRaffleEntryEntity}.cs` (+ DbSets), `GetCatalogPageWithEarliestExpiryMessageHandler.cs`, `GetLimitedOfferAppearingNextMessageHandler.cs` avec messages, composers, parsers, serializers, `Signals/Translators/Economy/{RaffleEntered,RaffleWon}Translator.cs`, dashboard `Contracts/{LtdSeriesRow,LtdRaffleResultCount}.cs`, `Database.Tests/Catalog/LtdRaffleGrainTests.cs`.
- Modifier : `CatalogModule.cs`, `InventoryGrain.Furni.GrantLtdFurnitureAsync` et son interface, `GrainFactoryExtensions.cs`, `EconomyEvents` (`LtdRaffle*Event`), `EconomyHandlers.cs`, `RewardNotificationHandlers.RaffleWon`, `NotificationKind.RaffleWon`, `CommerceOperation.LtdRaffleEntry`, `SignalActions`/`RewardTrackActions` (`EnterRaffle`, `WinRaffle`), `EconomyReads.Extras.cs`, `InventoryReads.cs`, onglet LTD d'`EconomyExtrasPage`, `CatalogMap.cs`.
- Garder les champs de stock unique `UniqueSize`/`UniqueRemaining` du catalogue (autre sujet).
- Contrôle : `grep -rIl "Ltd\|Raffle" --include=*.cs ...` → vide (hors enums persistés gardés sans renumérotation).

**9d — Vouchers**

- Supprimer : `Vortex.Catalog/Grains/VoucherGrain.cs`, `Vouchers/VoucherAttemptLimiter.cs`, `Primitives/Catalog/{IVoucherAttemptLimiter,VoucherCode}.cs`, `Grains/IVoucherGrain.cs`, `Snapshots/Voucher*.cs`, `Database/Entities/Catalog/Voucher{,Redemption}Entity.cs` (+ DbSets), `RedeemVoucherMessageHandler.cs` avec message, parser, composers `VoucherRedeem{Ok,Error}` et serializers, `Signals/Translators/Economy/VoucherTranslator.cs`, dashboard `Operations/Catalogue/VouchersOperations.cs`, `Contracts/{Create,Deactivate}VoucherRequest.cs`, `VouchersPage.svelte`, `Database.Tests/Catalog/VoucherRedemptionTests.cs`.
- Modifier (Shop et WebApi sont gardés) : `Vortex.Shop/ShopService.cs` (`RedeemVoucherAsync`), `IShopService.cs`, `WebApiEndpoints.cs` (`/api/user/shop/voucher`), `WebApiRequests.RedeemVoucherRequest`, `openapi.json`, `ShopEndpointTests`, `WebApiPurseTests`, `Fakes`.
- Modifier ailleurs : `CatalogModule.cs`, `GrainFactoryExtensions.cs`, `EconomyHandlers.VoucherRedeemedAuditHandler`, `RewardNotificationHandlers.VoucherNotificationHandler`, `NotificationKind.VoucherRedeemed`, `SignalActions`/`RewardTrackActions.RedeemVoucher`, `Facts.Voucher`, `RewardTrackFacts.Voucher`, `DashboardEndpoints.Economy.cs`, `DashboardEndpoints.cs`, `DashboardPlugin.cs`, `SignalDirectoryReads.VouchersDirectoryAsync`, `DashboardEndpoints.Directory.cs`, `DashboardStepUp.cs`, `DefaultRoles.cs`, capability `OpsManageVouchers` (4 fichiers).
- Contrôle : `grep -rIl "Voucher" ...` → vide.

**9e — Offres ciblées**

- Supprimer : `Vortex.Catalog/Grains/{PlayerTargetedOfferGrain,TargetedOfferManagerGrain}.cs`, `TargetedOffers/TargetedOfferMapper.cs`, `Primitives/Catalog/Admin/TargetedOfferAdminContracts.cs`, `ITargetedOfferAdminService.cs`, `Grains/I{Player,}TargetedOffer*Grain.cs`, `Snapshots/TargetedOffer*.cs`, `Database/Entities/Catalog/{TargetedOffer,TargetedOfferProduct,PlayerTargetedOffer}Entity.cs` (+ DbSets et configuration), les handlers `{GetNext,Get,Purchase,SetState,ShopViewed}TargetedOffer*Handler.cs`, `TargetedOfferResponse.cs`, leurs 6 messages, 6 parsers, 2 composers, 2 serializers, `Signals/Translators/Economy/TargetedOfferTranslator.cs`, dashboard `Admin/Catalogue/TargetedOfferAdminService.cs`, `Api/Catalogue/TargetedOfferReads.cs`, `Contracts/TargetedOffer*.cs`, `Hosting/Catalogue/DashboardEndpoints.TargetedOffers.cs`, `Operations/Catalogue/TargetedOfferOperations.cs` et ses contrats, `TargetedOffersPage.svelte`, `TargetedOffersStatsPage.svelte`, tests `Dashboard.Tests/TargetedOfferAdminServiceTests.cs`, `Database.Tests/Catalog/TargetedOfferPurchaseTests.cs`, `Commerce/TargetedOfferWindowTests.cs`, `TargetedOffers/TargetedOfferMapperTests.cs`.
- Modifier : `GrainFactoryExtensions.cs`, `EconomyEvents.TargetedOfferPurchasedEvent`, `EconomyHandlers.cs`, `CommerceOperation.TargetedOffer`, `BuyTargetedOffer`, `Facts.TargetedOffer`, `DashboardPlugin.cs`, `DashboardEndpoints.cs`, `SignalDirectoryReads.cs`, `Directory.cs`, `DashboardAssetUrls.cs`, `ObservabilityConfig.TargetedOfferImageUrlTemplate`, `appsettings.json`, capabilities `TargetedOffersRead` et `OpsTargetedOffersManage` (4 fichiers), `CatalogMap.cs`.
- Contrôle : `grep -rIl "TargetedOffer" ...` → vide.

**9f — Sondages et word quiz** (décision 8)

- Supprimer : `Vortex.Progression/Polls/`, `Grains/PlayerPollGrain.cs`, `Grains/PollManagerGrain.cs`, `Vortex.Primitives/Polls/`, `Vortex.Database/Entities/Polls/` (+ DbSets et configuration), `Vortex.PacketHandlers/Poll/`, `Protocol/Messages/{Incoming,Outgoing}/Poll/`, `Revision/Parsers/Poll/`, `Revision/Serializers/Poll/`, `Revision/Maps/PollMap.cs`, `Signals/Translators/Players/PollCompletedTranslator.cs`, dashboard `Admin/Progression/PollAdminService.cs`, `Admin/Rules/PollAuthoringRules.cs`, `Api/Progression/PollReads.cs`, `Contracts/Poll*.cs`, `Hosting/Progression/DashboardEndpoints.Polls.cs`, `Operations/Progression/PollOperations.cs` et contrats, `PollsPage.svelte`, tests `Dashboard.Tests/PollContractTests.cs`, `Players.Tests/Polls/*`, `Revisions.Tests/Polls/PollWireLayoutTests.cs`.
- Word quiz : chercher `WordQuiz` côté serveur (`grep -rIl "WordQuiz" --include=*.cs`) et supprimer handlers, messages, parsers, composers, serializers et état de room associés.
- Modifier : `Revision20260701.cs`, `GrainFactoryExtensions.cs`, `PlayerEvents.cs`, `ProgressionAuditHandlers.cs`, `SignalActions`/`RewardTrackActions.CompletePoll`, `Facts.Poll`, `DashboardPlugin.cs`, `DashboardEndpoints.cs`, `SignalDirectoryReads.PollsDirectoryAsync`, `Directory.cs`, capabilities `PollsRead` et `OpsPollsManage` (4 fichiers), `VocabularyGovernanceTests`.
- **Garder** `WebFeedPoll*` (sondages des posts du fil web, autre feature).
- Garder le seed `polls.sql` (Tâche 11).
- Contrôle : `grep -rIl "Poll\b\|PollManager\|PlayerPoll\|WordQuiz" --include=*.cs ...` → seulement `WebFeedPoll*`.

- [ ] **Step 1 : 9a**, contrôle vide, `dotnet build Vortex.Main/Vortex.Main.csproj && dotnet test Vortex.Cloud.sln` vert.
- [ ] **Step 2 : 9b**, idem.
- [ ] **Step 3 : 9c**, idem.
- [ ] **Step 4 : 9d**, idem.
- [ ] **Step 5 : 9e**, idem.
- [ ] **Step 6 : 9f**, idem.
- [ ] **Step 7 : vérifier le tout**

Run : `cd Vortex.Dashboard.Web && npm run lint && npm run test && npm run build`, puis `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck` → succès.

- [ ] **Step 8 : commit**

```bash
DIRS=$(git status --porcelain | sed -E "s#^.. (.* -> )?##" | cut -d/ -f1 | sort -u | xargs)
echo "$DIRS"   # relire : uniquement des dossiers touchés par cette tâche
git add -A -- $DIRS
git status --short   # rien d.autre que cette tâche
git commit -m "m0: retire Builders Club, classification, LTD, vouchers, offres ciblées, sondages et word quiz" -- $DIRS
```

---

### Task 10 : Navigator simplifié

Cible (décision 9) : Mes rooms, Rooms des amis, Mes groupes / QG de guilde, recherche texte, création de room, room d'accueil. `RentableSpaceGrain` n'est **pas** le navigator : ne pas y toucher.

**Files :**
- Modifier : `Vortex.Navigator/NavigatorService.cs`, `NavigatorProvider.cs`, `Vortex.Primitives/Navigator/Enums/NavigatorQueryType.cs`, `NavigatorSearchCodes.cs`, `Vortex.Players/Grains/PlayerNavigatorGrain.cs`, `PacketHandlers/NewNavigator/NewNavigatorInitMessageHandler.cs`, `Revision/Maps/NavigatorMap.cs`, `NewNavigatorMap.cs`
- Supprimer : les handlers listés au Step 3 avec messages, parsers, composers, serializers
- Test : `Vortex.Navigator.Tests/NavigatorSearchRoutingTests.cs`, `NavigatorQueryTypeResolutionTests.cs` (adapter), `NavigatorPromotedRoomsTests.cs` (supprimer)

- [ ] **Step 1 : tests de la cible (rouge)**

Dans `Vortex.Navigator.Tests/NavigatorSearchRoutingTests.cs` (suivre la structure existante), écrire un test par code conservé : `my`, `friends_rooms`, `my_groups`, `groups`, recherche texte → chacun résout vers son `NavigatorQueryType` (`MyRooms`, `FriendsRooms`, `MyGroups`, `GuildBases`, `TextSearch`) et renvoie les rooms attendues. Un test : un code retiré (`popular`, `official`, `history`) renvoie un résultat vide sans exception.

Run : `dotnet test Vortex.Navigator.Tests` → Expected : le test « code retiré → vide » FAIL (aujourd'hui il renvoie des rooms).

- [ ] **Step 2 : réduire les types de requête**

`NavigatorQueryType` : ne garder que `MyRooms`, `FriendsRooms`, `MyGroups`, `GuildBases`, `TextSearch` (et `ByFlatCategory` uniquement si la création de room en a besoin pour lister les catégories ; vérifier avec grep). `NavigatorService.ResolveQueryType` : tout code inconnu → résultat vide. `NavigatorProvider` : supprimer les méthodes `Get*` des types retirés et le filtre des rooms scellées BC s'il reste.

- [ ] **Step 3 : supprimer les handlers inutiles**

Dans `Vortex.PacketHandlers/Navigator/` : `CompetitionRoomsSearch`, `GetOfficialRooms`, `GetPopularRoomTags`, les recherches `Popular`, `HighestScore`, `RoomText` (si doublon de la recherche du nouveau navigator : vérifier), `RoomAd`, `RoomsWhereMyFriendsAre`, `MyRecommended`, `MyFavourite`, `MyRoomHistory`, `MyFrequentRoomHistory`, `MyRoomRights`, `ForwardToARandomPromotedRoom`, `ToggleStaffPick`, `RoomAdEventTab*`, `GetUserEventCats`, `EditEvent`, `CancelEvent`, `AddFavouriteRoom`, `DeleteFavouriteRoom`. Supprimer leurs messages, parsers, composers, serializers et mappings. Les événements de room sont liés à `CatalogPurchaseGrain.RoomAd.cs` : retirer l'achat de pub de room.

**Garder** : `CreateFlat`, `CanCreateRoom`, `GetGuestRoom`, `UpdateHomeRoom`, `GetUserFlatCats`, `ConvertGlobalRoomId`, `ForwardToSomeRoom`, `MyRooms`, `MyFriendsRooms`, `MyGuildBases`, `GuildBaseSearch`, `NewNavigatorInit`, `NewNavigatorSearch` (le client les envoie).

- [ ] **Step 4 : état par joueur**

`PlayerNavigatorGrain` : retirer recherches sauvegardées, catégories repliées, modes d'affichage, favoris ; garder les préférences de fenêtre et la room d'accueil. Retirer les entités et DbSets `SavedSearches`, `CollapsedCategories`, `ViewModes`. `NewNavigatorInitMessageHandler` : envoyer des listes vides pour les recherches sauvegardées et catégories repliées (le client attend ces messages), garder contextes et préférences.

- [ ] **Step 5 : configuration**

- `Vortex.Database/Seeds/navigator_configuration.sql` : **ne pas modifier** (une migration le lit). Les onglets en trop seront retirés dans le seed de la Tâche 11.
- `ConfigKeyCatalog` : retirer `navigator.history_limit` et `navigator.favourite_limit`.
- Dashboard `NavigatorConfigPage.svelte` et `NavigatorAdminService` : retirer la gestion des catégories promues, des catégories d'événement, des staff picks ; garder contextes et liens rapides. Capability `Navigator.StaffPick` : retirer (4 fichiers + `DefaultRoles`).
- Supprimer `Vortex.Navigator.Tests/NavigatorPromotedRoomsTests.cs`.

- [ ] **Step 6 : vérifier**

Run : `dotnet test Vortex.Navigator.Tests` → Expected : PASS, y compris le test « code retiré → vide ».
Run : `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck` → succès.

- [ ] **Step 7 : commit**

```bash
DIRS=$(git status --porcelain | sed -E "s#^.. (.* -> )?##" | cut -d/ -f1 | sort -u | xargs)
echo "$DIRS"   # relire : uniquement des dossiers touchés par cette tâche
git add -A -- $DIRS
git status --short   # rien d.autre que cette tâche
git commit -m "m0: navigator réduit aux maisons, rooms d'amis et QG de guilde" -- $DIRS
```

---

### Task 11 : Migration initiale unique et seeds

**Files :**
- Supprimer : `Vortex.Database/Migrations/2026*.cs`, `Vortex.Database/Migrations/VortexDbContextModelSnapshot.cs`
- **Garder** : `Vortex.Database/Migrations/MigrationHelper.cs` (utilisé par `Program.cs`)
- Créer : `Vortex.Database/Migrations/<horodatage>_InitialCreate.cs` (+ `.Designer.cs`, nouveau snapshot) par `dotnet ef`
- Modifier : `Vortex.Database/Seeds/` (supprimer les seeds des features retirées), `scripts/sql/` (supprimer les scripts obsolètes)
- Test : `Vortex.Database.Tests/Migrations/SeedCatalogTests.cs` (nouveau)

**Prérequis :** un MySQL local jetable (le service `mysql` de `docker-compose.yml`). La base `vortex` sera **détruite** : c'est une base de développement du fork.

- [ ] **Step 1 : inventaire des seeds avant suppression**

```bash
grep -rhoE 'SeedScripts\.Read\("[^"]+"\)' Vortex.Database/Migrations | sort -u > /tmp/seeds-read.txt
grep -rlE "InsertData|migrationBuilder\.Sql\(" Vortex.Database/Migrations > /tmp/migrations-with-data.txt
```
Pour chaque migration de `/tmp/migrations-with-data.txt`, noter les données insérées (`InsertData`) ou le SQL inline (`migrationBuilder.Sql("...")`) qui concernent une feature **gardée**. Inventaire de départ (à vérifier) : `SeedDashboardStaffActor`, `AddMarketplaceSettings`, `AddAchievements`, `SeedMoreAchievementsFixCategories`, `AddQuests`, `AddDailyQuests`, `AddServerConfig`, `AddShop`, `AddGroupBadgePartId`, `AddGroupColorId`, `FixGuildRoomUniqueness`, `AddPlayerNuxCompletedAt`, `CategorizeRoomDecorations`, `AddCatalogGiftWrappings`. Le SQL inline qui corrige des données existantes (UPDATE sur des lignes historiques) n'a pas lieu d'être dans une base neuve.

- [ ] **Step 2 : convertir les données gardées en seeds**

Pour chaque `InsertData` et SQL inline gardé du Step 1 : l'écrire dans un fichier `Vortex.Database/Seeds/<nom>.sql` (un fichier par migration d'origine, même nom en snake_case). Ne pas réécrire à la main de longues listes : générer le SQL avec `dotnet ef migrations script <précédente> <celle-ci> --idempotent` restreint à la migration et en extraire les `INSERT`.

- [ ] **Step 3 : supprimer les seeds des features retirées**

```bash
cd Vortex.Database/Seeds
rm -f habbicons.sql polls.sql mystery_box.sql collectibles_currencies.sql reward_track_introduction.sql crackable.sql crackable_and_highscore_families.sql pet_food.sql nft_*.sql
```
Vérifier chacun avant de le supprimer : `collectibles_currencies.sql` peut contenir les monnaies Emerald/Silver gardées ; dans ce cas, garder les lignes de monnaie dans un fichier `currencies.sql`. `crackable_and_highscore_families.sql` contient aussi les familles de logique highscore, gardées : en extraire les lignes highscore dans `highscore_families.sql` avant de le supprimer. `navigator_configuration.sql` : retirer les onglets et liens rapides des types de requête supprimés en Tâche 10.

- [ ] **Step 4 : écrire le test des seeds (rouge)**

`Vortex.Database.Tests/Migrations/SeedCatalogTests.cs` : un test qui liste les ressources embarquées `Seeds/*.sql` de l'assembly `Vortex.Database` et vérifie que (a) chaque seed est lu par `InitialCreate` (chercher le nom dans le source de la migration via la ressource ou via `SeedScripts`), (b) aucun seed ne mentionne une table supprimée (`habbicon`, `reward_track`, `poll`, `mystery_box`, `nft`, `wired`, `builders_club`, `voucher`, `targeted_offer`, `ltd`). Suivre la structure des tests existants qui lisent des seeds (`AchievementSeedLevelTests`, `PetCommand*`).

Run : `dotnet test Vortex.Database.Tests --filter "FullyQualifiedName~SeedCatalog"` → Expected : FAIL (les anciennes migrations lisent encore les seeds supprimés, et `InitialCreate` n'existe pas).

- [ ] **Step 5 : régénérer les migrations**

```bash
rm -f Vortex.Database/Migrations/2026*.cs Vortex.Database/Migrations/VortexDbContextModelSnapshot.cs
ls Vortex.Database/Migrations   # doit rester : MigrationHelper.cs
dotnet tool restore
pwsh scripts/ef.ps1 migrations add InitialCreate --context VortexDbContext
```
Dans la méthode `Up` de `InitialCreate`, après la création des tables, ajouter un `migrationBuilder.Sql(SeedScripts.Read("<fichier>.sql"));` par seed gardé, dans l'ordre des dépendances (tables référencées d'abord). Utiliser exactement la même façon de lire les seeds que les anciennes migrations (`git show HEAD~1:Vortex.Database/Migrations/<une ancienne migration>.cs`).

- [ ] **Step 6 : migrer une base vide**

```bash
docker compose up -d mysql
docker compose exec mysql mysql -uroot -p"$MYSQL_ROOT_PASSWORD" -e "DROP DATABASE IF EXISTS vortex; CREATE DATABASE vortex;"
pwsh scripts/ef.ps1 database update --context VortexDbContext
```
Expected : succès, aucune erreur SQL. Adapter le nom du service et les identifiants à `docker-compose.yml`.

- [ ] **Step 7 : vérifier**

Run : `dotnet test Vortex.Database.Tests` → PASS (y compris `SeedCatalogTests`). Retirer ou adapter les tests qui lisaient des seeds supprimés (`PetFoodSeedTests` déjà parti en Tâche 8, `PetSeedReRunnability` si concerné).
Run : `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck` → succès.

- [ ] **Step 8 : scripts SQL**

Supprimer de `scripts/sql/` tout script restant qui vise une feature retirée ou l'historique de migrations (`builders_club_membership_pages.sql`, `wired*`, `import_habbo_*` restants…). Ceux qui gardent un sens sur une base neuve restent. Au passage, `scripts/sql/catalog_groups_pages.sql` existe dans `vortex-emulator` (non suivi) et n'a pas été copié dans le fork : rien à faire ici.

- [ ] **Step 9 : commit**

```bash
git add -A -- Vortex.Database Vortex.Database.Tests scripts/sql
git commit -m "m0: une seule migration InitialCreate, seeds des features gardées seulement" -- Vortex.Database Vortex.Database.Tests scripts/sql
```

---

### Task 12 : Validation finale et démarrage réel

**Files :** aucun nouveau, sauf corrections.

- [ ] **Step 1 : gate complet**

Run : `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate`
Expected : succès sans nouvel avertissement par rapport à la Tâche 0.

- [ ] **Step 2 : démarrer l'émulateur sur la base neuve**

Utiliser le skill `run-vortex-emulator` du dépôt. Expected : l'hôte démarre, aucun plugin (dashboard, WebApi) ne lève d'exception au démarrage, le dashboard répond.

- [ ] **Step 3 : parcours de fumée (Review Focus 1, 2, 5)**

Avec le client `vortex-modern-client` (le client du fork n'est pas encore nettoyé, il parle encore le même protocole) ou le harness de socket du skill :
1. connexion d'un joueur (SSO) → pas d'erreur serveur ;
2. création d'une room, entrée, déplacement, chat ;
3. pose et ramassage d'un furni ; un furni dont la clé de logique a été supprimée (une balle de foot, une boîte crackable) se charge avec la logique par défaut ;
4. un pet : placement, commande, caresse, selle puis monture ;
5. échange de furni entre deux joueurs ;
6. gain d'un achievement (vérifie que le vault déplacé en Tâche 2 répond) ;
7. ouverture du navigator : Mes rooms, Rooms des amis, Mes groupes ;
8. dashboard : chaque entrée du menu s'ouvre sans erreur 500.
Noter tout échec, le corriger dans la tâche concernée (nouveau commit), relancer le Step 1.

- [ ] **Step 4 : bilan**

Dans la description de PR (ou un fichier `docs/superpowers/plans/2026-10-05-m0-menage-serveur.bilan.md`) : nombre de projets, de lignes et de tests avant et après (Tâche 0), décisions par défaut appliquées, écarts rencontrés.

```bash
git add -A -- docs/superpowers/plans
git commit -m "m0: bilan du ménage serveur" -- docs/superpowers/plans
```
