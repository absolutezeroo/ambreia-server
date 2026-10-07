# Bilan — M0 bis, fondation F0 (serveur)

Plan : `docs/superpowers/plans/2026-10-06-m0-bis-fondation-f0.md`. Spec : `docs/superpowers/specs/2026-10-06-vortex-rpg-design.md`
(§22.1 bis pour les 12 décisions, §3.4 bis pour les couches). Branche `m0-bis`, base `fafdfba4` (fin du M0), 100 % local.

## Avant / après

| | Avant (`fafdfba4`) | Après (Tâche 14) |
|---|---|---|
| Projets `.csproj` suivis | 47 | 49 (+ Permissions, Permissions.Tests, WebApi.Authentication(.Tests) ; − Authentication(.Tests) ; Shop → WebApi.Shop) |
| Fichiers `.cs` suivis | 4 712 | 3 712 |
| Lignes `.cs` (hors `Vortex.Database/Migrations`) | 289 691 | 248 002 |
| Attributs `[Fact]`/`[Theory]` | 2 177 | 1 834 |
| Tests exécutés par le QualityGate | 2 995 (fin du M0) | **2 600, 0 échec** |

| Projet de tests | Fin du M0 | Fin du M0 bis |
|---|---|---|
| Vortex.Rooms.Tests | 893 | 836 |
| Vortex.Signals.Tests | 363 | 333 |
| Vortex.Players.Tests | 316 | 216 (dont les tests du ticket SSO, venus d'Authentication) |
| Vortex.Hosting.Tests | 215 | 224 |
| Vortex.Dashboard.Tests | 233 | 213 |
| Vortex.Revisions.Tests | 259 | 199 |
| Vortex.WebApi.Tests | 230 | 155 |
| Vortex.Database.Tests | 161 | 127 |
| Vortex.Crypto.Tests | 64 | 64 |
| Vortex.Navigator.Tests | 61 | 61 |
| Vortex.PacketHandlers.Tests | 60 | 45 |
| Vortex.Authentication.Tests | 68 | → WebApi.Authentication.Tests 51 + Permissions.Tests 4 + Players.Tests (SSO) |
| Vortex.Plugins.Tests | 40 | 40 |
| Vortex.Shop.Tests | 19 | → WebApi.Shop.Tests 19 |
| Vortex.Pipeline.Tests | 13 | 13 |

## Ce qui a été fait (14 commits sur `m0-bis`)

| Tâche | Commit | Contenu |
|---|---|---|
| 1 | `d41b1755` | 74 handlers vides et 148 composers morts retirés |
| 2 | `783fa764` | chansons, résolutions, niveaux de compte, quiz, guides, revue de chat, photo CFH, restes RewardTracks |
| 3 | `82a3d4c7` | styles de chat |
| 4 | `17a39df8` | rollers (décision 6) |
| 5 | `e3cc5bd3` | respects (décision 9 ; statut de relation gardé) |
| 6 | `755f943d` | objectifs communautaires, derby, espaces à louer, anti-raid, bots possédés (décision 11) |
| 7 | `ffa53c1b` | vue d'hôtel et fil social du site (décision 10 ; articles gardés) |
| 8 | `fb127ead` | vie simulée des pets (commandes, paroles, objets en main, jouets, niche, besoins) ; selle et monture gardées ; le compagnon suit son maître |
| 9 | `ae898962` | renommages : Membership (décision 3), RewardVault, LootTable (décision 5), PetSkin (décision 12) |
| 10 | `d5b36bdb` | références périmées retirées, mur de couches (8e mur) |
| 11 | `f7e4ffbd` | Authentication → WebApi.Authentication + Permissions ; ticket SSO dans Players |
| 12 | `45cc36dc` | Shop → WebApi.Shop |
| 13 | `6f6fc118` | documents de règles (AGENTS, CONTEXT, CLAUDE, copilot, docs/codebase) |
| 14 | ce commit | InitialCreate régénérée, corrections révélées par la suite complète, bilan |

Décisions conservées sans travail de code : 1 (comptes multi-avatars = personnages), 2 (moteur de quêtes gardé,
signaux enrichis plus tard), 4 (or + diamants, à câbler en M2), 7 (cadeaux gardés), 8 (maison fixe : aucune
édition de plan par le joueur ajoutée).

## Validation finale (Tâche 14)

- **Migration** : `InitialCreate` régénérée (`20261007012104`), les 27 seeds recopiés dans le même ordre (sans
  `pet_vocals`, `pet_command_names`, `pet_commands`, `hand_items` ; `pet_palettes` → `pet_skins`).
- **Base neuve** `vortex_m0bis_check` : migration sans erreur ; import depuis `turbo` (lecture seule) :
  63 modèles, 55 282 définitions, 3 936 vêtements, 2 168 pages, 79 799 offres, 104 764 produits — identiques au M0 —
  plus 4 offres et 5 cadeaux d'abonnement (`membership_offers`/`membership_gifts` lus depuis `catalog_club_*`) et
  158 offres à `membership_level` > 0 (= les 158 `club_level` de turbo).
- **QualityGate** : vert, 2 600 tests, 0 échec (troisième passage ; voir Écarts).
- **Démarrage réel** (skill `run-vortex-emulator`, plugins publiés comme au M0) : hôte prêt en ~6 s, deux plugins
  chargés, `/health` Healthy, base up, silo actif, socket de jeu (ping 544 → 188), session dashboard.
  Parcours socket : inscription web → avatar → SSO → création de room → entrée → marche → chat → achat de 4 meubles
  (multistate 2×1, roller, trampoline de pet, niche) → pose, déplacement, **empilement** (z = 1.0), ramassage.
  Le roller et le trampoline se chargent en `default_floor` (journal : « Logic type 'roller' not found … falling
  back to default_floor »). La 4e pose a échoué parce que le script la visait en x = 13 sur `model_a` (12 de large) :
  artefact du script, pas un défaut ; la niche est couverte par `APetToyOrNestDefinition_LoadsAsDefaultLogic`.
  Dashboard : les 76 routes GET sans paramètre de la matrice → 73 × 200, 1 × 400 (paramètre requis), **aucun 500**.
  Émulateur arrêté ensuite (`driver.mjs stop`). Les user-secrets pointent sur `vortex_m0bis_check`.

## Écarts rencontrés

- **Renommage trop large, rattrapé par la suite** : les familles d'achievements `BasicClub`/`VipClub` (clés client
  `ACH_BasicClub*`/`ACH_VipClub*`) et la valeur du type de quête `BuyClub` avaient été renommées ; restaurées
  (la constante s'appelle `QuestTypes.BuyMembership`, sa valeur reste `"BuyClub"`). Commentaires « Builders Club »
  restaurés aussi.
- **Tests à SQL brut** (`RoomContentsReleaseTests`, `RoomPersistenceLossWindowTests`) qui inséraient encore
  `race`/`energy`/… dans `pets` : colonnes mises à jour.
- **Test d'achat de bot** (`ABotNotificationThatFails_KeepsTheBotAndTheCharge`) : sans objet depuis le retrait des bots
  possédés (Tâche 6) ; supprimé.
- **Instantanés** `apiTypes.d.ts` et `authorization-matrix.txt` : réordonnés par les renommages, et la matrice gardait
  les routes `community-goals`/`rentable-*` retirées au T6 ; régénérés depuis la sortie réelle.
- **Formatage** de la migration générée (csharpier) : premier passage du gate rouge, corrigé.
- **Course de build du dashboard** : quand le front a changé, le `dotnet test` parallèle du gate peut compiler
  `Vortex.Dashboard.API` pendant que Vite réécrit `Assets/` (`CS1566 … dashboard-*.css introuvable`), et
  Dashboard.Tests/Players.Tests ne tournent pas. Construire `Vortex.Dashboard.API` une fois avant le gate suffit.
- **Mode rapide** (décision du propriétaire, R4/R5) : pas de revue ni de tests par tâche ; compilation seule par lot,
  toute la suite ici. Les étapes « test rouge » des briefs n'ont donc pas été observées rouges avant l'implémentation.

Toutes les décisions prises en route (« Ruling ») et les mineurs différés sont dans le ledger
`.superpowers/sdd/2026-10-06-m0-bis-fondation-f0/progress.md` et reportés dans le message de fin de session.

## Inventaire des champs réseau figés ajoutés au M0 bis (à joindre à celui du M0 pour l'étape protocole du client)

| Message | Champ | Valeur | Fichier |
|---|---|---|---|
| `UserObjectMessageComposer` | `respectTotal`, `respectLeft`, `petRespectLeft` | `0` | `Vortex.Revisions/Revision20260701/Serializers/Handshake/UserObjectMessageSerializer.cs` |
| `UserObjectMessageComposer` | `respectReplenishesLeft`, `maxRespectPerDay` | `0` | idem |
| `ExtendedProfileMessageComposer` | `accountLevel` | `0` | `…/Serializers/Users/ExtendedProfileMessageComposerSerializer.cs` |
| `GuideReportingStatusMessageComposer` | `statusCode` | `0` (rien en attente) | `Vortex.PacketHandlers/Help/GetGuideReportingStatusMessageHandler.cs` |
| `PerkAllowancesMessageComposer` | `USE_GUIDE_TOOL`, `CALL_ON_HELPERS`, `JUDGE_CHAT_REVIEWS` | non autorisés | `Vortex.PacketHandlers/Handshake/SSOTicketMessageHandler.cs` |
| `AccountPreferencesEventMessageComposer` | `preferedChatStyle` | `0` | `…/Serializers/Preferences/AccountPreferencesEventMessageComposerSerializer.cs` |
| `ChatMessageComposer`, `ShoutMessageComposer`, `WhisperMessageComposer` | `styleId` | `0` | `…/Serializers/Room/Chat/*MessageComposerSerializer.cs` |
| `Chat`/`Shout`/`Whisper`/`SetChatStylePreference` (entrants) | style de chat | lu et ignoré | `…/Parsers/Room/Chat/*`, `…/Parsers/Preferences/SetChatStylePreferenceMessageParser.cs` |
| `SlideObjectBundleMessageComposer` | `rollerItemId` | `0` | `…/Serializers/Room/Engine/SlideObjectBundleMessageComposerSerializer.cs` |
| `PetInfoMessageComposer` | `energy`, `maxEnergy` | plafond du niveau (`MaxEnergy`) | `…/Serializers/Room/Pets/PetInfoMessageComposerSerializer.cs` |
| `PetInfoMessageComposer` | `happiness`, `maxHappiness` | plafond du niveau (`MaxHappiness`) | idem |
| `PetInfoMessageComposer` | `respect` | `0` | idem |
| `PetInfoMessageComposer` | `skillThresholds` | liste vide (plus de commandes) | `Vortex.Protocol/Messages/Outgoing/Room/Pets/PetInfoMessageComposer.cs` (défaut) |

Messages que le client envoie encore et que le serveur ne lit plus (avertissement « no parser », pas d'erreur) :
`GetCurrentTimingCode`, `GetBonusRareInfo` (vue d'hôtel), `GetPetCommands`, `PetSelected`, `PassCarryItemToPet`,
plus ceux retirés au T1-T7. Champs JSON de l'API web publique renommés (`clubDays` → `membershipDays`, etc.) :
`Vortex.WebApi/openapi.json` régénéré.

## Checklist du propriétaire avec le client

- [ ] Connexion (ticket SSO) et arrivée en room sans erreur côté client (vue d'hôtel vide, attendu).
- [ ] Création de room, marche, chat (bulle standard : plus de styles).
- [ ] Construction de map : poser, déplacer, tourner un 1×1 et un multi-cases ; empiler ; ramasser.
- [ ] Un roller et un jouet de pet du catalogue se posent comme du décor (aucune animation).
- [ ] Pet : poser, il suit son maître quand on s'éloigne de plus de 3 cases, sinon il erre ; fiche du pet (barres
      pleines) ; mettre la selle, monter, descendre, retirer la selle.
- [ ] Échange de meubles entre deux joueurs.
- [ ] Dashboard : pages Abonnements (Membership), Tables de loot, Pets (colonne Skin), Bots (plus d'onglet objets en main).
