# Cœur du monde — plan d'ensemble (éditeur + monde serveur, hors RPG)

> **Mandat du propriétaire (2026-10-07) :** « finir entièrement le cœur avant d'attaquer le RPG » —
> le système du monde (zones, interactions, déclencheurs…) en totale autonomie, base bien découpée,
> pas de god class, architecture de l'émulateur respectée, entièrement modulaire.
>
> **Spec de référence :** `vortex-rpg-client/docs/superpowers/specs/2026-10-07-ambreia-map-editor-spec.md`
> (le propriétaire ; citée « S§n »). Spec 1 serveur, faite : `docs/superpowers/specs/2026-10-07-rpg-world-map-editor-design.md`.
> **Dépôts :** `vortex-rpg-emulator` (`main`) ; `vortex-rpg-client` via le worktree
> `vortex-rpg-client-editeur`, branche `editeur-cartes` (sur `j3-ui` tant que J3 n'est pas dans `master`).

## Ce qui est hors du cœur (RPG, après)

Combat, monstres vivants, comportement des PNJ, quêtes, butin, classes, niveaux, métiers, AOI/ECS de
jeu. Leurs **données** se saisissent et se valident déjà dans l'éditeur (PNJ, spawns, coffres, boss,
conditions `PlayerLevel`/`QuestState`/`HasItem`/`Faction`, actions `StartQuest`/`SpawnGroup`) ; à
l'exécution elles sont **inertes et signalées** : une condition RPG est fausse, une action RPG ne fait
rien, et le validateur l'annonce en avertissement « nécessite le système RPG ».

## Architecture (où vit chaque chose)

| Couche | Rôle | Emplacement |
|---|---|---|
| Contrats | Document de zone, règles, snapshots du monde, interfaces de services | `Vortex.Primitives/World/**` |
| Édition | Sauvegarde, versions, publication, validation | `Vortex.World` (existe) |
| Annuaire | Clé de zone ↔ room, entrées publiées, pour les voyages | `Vortex.World/Directory/ZoneDirectory` (nouveau), interface `IZoneDirectory` en Primitives |
| Exécution | Ce que fait une zone vivante | `Vortex.Rooms/Grains/Zones/**` : **un module par responsabilité**, branchés sur `RoomGrain` comme les modules existants |
| Interactions joueur | Utiliser un mobi | paquets `UseFurniture`/`UseWallItem` + handlers (skill `adding-a-packet`) |
| Éditeur | Interface sur les layouts Glaze du propriétaire | `packages/ambreia-map-editor/src/ui/**` (client) |

Modules d'exécution (aucun ne dépasse sa responsabilité ; état propre dans `ZoneRuntimeState`, champ de `RoomLiveState`) :

| Module | Fait | Ne fait pas |
|---|---|---|
| `ZoneCompiler` (pur) | Document → `CompiledZone` : index case → aires, case → passage, entrées par id, triggers par type, décor id ↔ objet | rien d'autre |
| `ZoneSettingsApplier` (pur) | Paramètres → `RoomSnapshot` (nom, capacité…) | — |
| `ZoneArrivalModule` | Choix de la case d'arrivée (entrée attendue, sinon `arrival`, sinon porte) | voyage |
| `ZoneAreaModule` | Aires où se tient chaque joueur ; publie `ZoneAreaEnteredEvent`/`LeftEvent` sur le bus de room | règles |
| `ZonePassageModule` | Pas sur un passage → voyage | conditions RPG |
| `ZoneTravel` (service) | Envoyer un joueur vers une zone + entrée (même room : déplacement instantané ; autre : arrivée attendue + `RoomForward`) | — |
| `ZoneInteractionModule` | Utilisation d'un mobi interactif → effet (porte, panneau, levier, téléporteur) + événement | — |
| `ZoneTriggerModule` | Écoute les événements de zone, évalue conditions, exécute actions, cooldowns | logique d'une condition ou action précise |
| `IZoneConditionEvaluator` × n | Une classe par sorte de condition | — |
| `IZoneActionExecutor` × n | Une classe par sorte d'action | — |
| `ZoneClockSystem` | Étape d'horloge : triggers `Timer`, fins de cooldown | — |
| `ZoneRulesQuery` | « Quelles règles sur cette case / pour ce joueur ? » (sûre, PvP, niveau conseillé) — pour le futur combat et pour l'affichage | — |

Base de données : le document reste la source d'autorité (une ligne par version, JSON). L'état vivant
(aires actives, variables, cooldowns, portes ouvertes) est **en mémoire, par room** : il repart du
document à chaque activation (S§75 : le jeu ne modifie pas la carte). Aucune nouvelle table sans un
besoin d'exécution qui la justifie ; si un besoin apparaît (ex. annuaire des entrées pour les liens
inter-zones), il est lu depuis `zones` + `zone_versions` publiées et mis en cache.

## Décisions (points laissés ouverts par la spec ou par le code)

| # | Décision | Pourquoi | Coût si faux |
|---|---|---|---|
| D1 | Capacité d'une zone = `settings.capacity` (défaut 50), écrase `rooms.players_max` **en mémoire** | Le document est l'autorité ; la ligne `rooms` appartient au grain | changer une ligne d'application |
| D2 | Nom de la room = `settings.name`, description = région | Le client affiche le nom de la room | idem |
| D3 | Arrivée : entrée demandée (`ZoneEntry.id`) > première entrée `arrival` > porte | S§10 (spawn par défaut), S§28 (destination spawn) | ordre dans un seul module |
| D4 | Sortes d'entrée connues : `arrival`, `respawn`, `teleport`, `dungeon_entry`, `dungeon_return` ; une sorte inconnue reste valable (avertissement) | S§10 liste « MAY » | ajout d'une constante |
| D5 | Aire : polygone de cases (≥ 3 points, contour inclus) ; priorité = entier, la plus haute gagne ; égalité → la plus récente dans le document | S§20.3 « résolution explicite » | une fonction pure testée |
| D6 | Règles d'une aire par sa `kind` : `Safe` → sûre, `PvP` → PvP permis, autres → héritent de la zone (`settings.pvpAllowed`) | S§20.1/20.2 ; pas de combat encore | table de correspondance |
| D7 | `DisplayOnHUD` : entrer dans une aire nommée affiche une bulle (`NotificationDialog`) « Vous entrez : <nom> » + règles (« Zone sûre » / « PvP ») | Rend le PvP/sûr visible avant le combat | texte côté serveur |
| D8 | Passage : un pas sur une case du passage déclenche le voyage ; cooldown par joueur ; même zone → déplacement instantané | S§28/29 | — |
| D9 | Voyage inter-zones : la room de destination garde une « arrivée attendue » (joueur → entrée, 30 s), puis `RoomForward` ; l'entrée normale lit cette arrivée | Pas d'état d'arrivée aujourd'hui | — |
| D10 | Interaction : nouveaux paquets `UseFurniture`/`UseWallItem` ; sur un mobi de zone, `ZoneDecorObject.interaction` choisit l'effet : `door` (bascule état + marchable), `sign` (bulle du `tag`), `lever` (bascule état), `teleport` (vers `interactive`/passage lié) ; sans `interaction` → logique du mobi | S§30 | une classe d'effet par sorte |
| D11 | Triggers à l'exécution : `EnterZone`, `LeaveZone` (cible = id d'aire), `Interact` (cible = id de décor), `Timer` (cible = période en s) ; `MonsterKilled`/`QuestState`/`PlayerLevel`/`ItemOwned` = RPG, inertes | S§26 | — |
| D12 | Conditions à l'exécution : `TimeOfDay` (heure serveur), `ZoneActive` ; RPG inertes (fausses) | idem | — |
| D13 | Actions à l'exécution : `ShowDialogue` (bulle du texte/clé), `ActivateZone`/`DeactivateZone` (aire désactivée = ignorée par aires, passages, triggers), `Teleport`, `OpenDoor` ; `PlaySound` (journalisé, pas de son côté client aujourd'hui) ; RPG inertes | idem | — |
| D14 | Cooldown d'un trigger : par joueur pour les événements de joueur, global pour `Timer` | Évite qu'un joueur bloque les autres | — |
| D15 | Boucle de triggers : profondeur max 8 par événement d'origine ; au-delà, journalisé et arrêté. Validateur : un trigger qui active l'aire qui le déclenche → avertissement | S§26.4 | — |
| D16 | Type de zone (`World`, `Dungeon`, `Lobby`, `Housing`, `Guild`, `Arena`) : stocké et exposé ; **tranché par le propriétaire (2026-10-07) : un donjon = une copie par groupe** (« sinon aucun intérêt ») ; les autres types sont partagés | S§45 `InstancePolicy` | D19–D23 |
| D19 | Instancié = `settings.type == Dungeon` ; une copie par (donjon, groupe) ; tant qu'il n'y a pas de groupes, le groupe est le joueur seul (`ZoneInstanceGroups.KeyFor`) | Décision du propriétaire | la clé de groupe change avec les parties |
| D20 | Id d'une copie = `1 000 000 000 + gabarit × 1000 + emplacement` (`ZoneInstanceIds`) : la copie lit la ligne `rooms`, la zone, les droits et le journal des visites de son gabarit, sans table ni état | Pas de ligne par copie (clés étrangères, nettoyage) | gabarit ≤ 1 147 482 |
| D21 | Entrée : `RoomService` demande `IRoomZone.InstanceForAsync` ; si c'est une autre room → `RoomForward` vers la copie ; l'arrivée attendue (passage, test) suit le joueur dans la copie | Un seul point couvre passages, téléports, SSO, mode test, entrée directe | un aller-retour client de plus |
| D22 | Vie d'une copie : tant que sa room est active ; à la désactivation, elle est libérée et le groupe repart d'une copie neuve ; republier le gabarit ne touche pas les copies en cours | Un donjon se « réinitialise » à la fermeture | — |
| D23 | Au plus `RoomConfig.MaxInstancesPerZone` (200, ≤ 999) copies par donjon ; au-delà, l'entrée est refusée (salle pleine) | Borne mémoire | une file d'attente si nécessaire |
| D24 | Sortes d'aires (`ZoneAreaKinds`) : Safe, NoCombat, PvP, Hostile règlent le combat (la plus prioritaire décide ; un donjon est hostile par défaut) ; NoMount et Restricted valent partout où elles s'étendent ; Town, Quest, Boss, Dungeon, Gathering, Audio, Event, Custom nomment le lieu. Une sorte inconnue est refusée. `ZoneRuleBook` calcule les règles d'une case (sûre, PvP, hostile, sans monture, restreinte, niveau, musique, type), lues par la bulle et par `IRoomZone.GetRulesAtAsync` (combat et monstres à venir). Restricted est appliqué à la marche : on n'y entre qu'en remplissant ses conditions, personne s'il n'en a aucune (le validateur le signale) ; une aire désactivée ne règle plus rien | S§20 ; demande du propriétaire | les effets de combat attendent le RPG |
| D17 | Musique, météo, ambiance : stockées, validées ; pas d'effet tant que le client n'a pas de son/météo | La coupe au noyau a retiré le son | — |
| D18 | Mode test : `POST /api/editor/zones/{key}/test` → ticket SSO pour l'avatar du compte + entrée directe dans la zone (case choisie possible) ; l'éditeur ouvre le client avec ce ticket | Spec 1 §6.6 reporté ; S§48 | — |

## Phases

Chaque tâche : test d'abord (rouge vu), code, test vert, gate du dépôt touché, preuve en vrai quand un
joueur peut le voir, commit par chemins. Une relecture fraîche à la fin de chaque phase.

### Phase 1 — L'éditeur sur `@ambreia/ui` (client)

1. Démarrage des fenêtres (bundles, polices, skins + skins Ambreia, layouts du propriétaire, textes `mapeditor.fr`), hôte canevas + file de souris du client, boucle d'affichage. Preuve : la fenêtre `vortex_map_editor_xml` s'affiche avec ses textes.
2. Vue isométrique dans `viewport_slot` (`display_object_wrapper` en premier enfant), souris routée vers la vue quand aucune fenêtre n'est touchée ; barre d'outils de vue et encart d'infos par-dessus.
3. Barre de menus (`menu_file/edit/view/test`, `zone_menu`, `zoom_menu`), boutons Tester / Enregistrer / Publier, rail des catégories (lignes du layout).
4. Colonne Sol (`left_floor` : outils, hauteurs, hauteur exacte), inspecteur de case (`inspect_tile`).
5. Colonne Mobis (`left_decor` : catégories, recherche, filtres, grille `furni_tile` avec miniatures, pages), inspecteur de mobi (`inspect_decor`).
6. Bas (`bottom_edit` : mini-carte dans `minimap_slot`, calques, infos, journal) ; mode test (`bottom_test`).
7. PNJ, spawns/zones, triggers : listes (`left_npcs/spawns/triggers`, `entity_row`, `group_header`), inspecteurs (`inspect_npc/spawn/trigger`, `rule_row`), dessin des aires (rectangle, polygone) — commandes du cœur + tests.
8. Fenêtres manquantes dans le style du propriétaire, faites par moi à partir de ses gabarits : paramètres de la salle, inspecteur d'aire, passages/entrées, validation (`issue_row`), nouvelle carte. Signalées pour retouche dans Glaze.
9. Retrait des panneaux HTML ; connexion et liste des zones gardées tant qu'elles n'ont pas de layout.

### Phase 2 — Paramètres et arrivées (serveur)

1. `ZoneCompiler` + `CompiledZone` (pur, testé).
2. `ZoneSettingsApplier` : D1, D2 dans `HydrateRoomStateAsync` après le snapshot. Preuve : capacité refusant le 2ᵉ joueur d'une zone de capacité 1.
3. `ZoneArrivalModule` + arrivée attendue (D3, D9) : `RoomAvatarModule` et `RoomService.CompleteRoomEntryAsync` lisent la case d'arrivée du module. Preuve : entrée en vrai sur l'entrée `arrival`.
4. `IZoneDirectory` (Vortex.World) : clé → room, entrées publiées ; invalidé à la publication.

### Phase 3 — Aires, passages, voyages

1. Hook de pas engagé dans `RoomAvatarModule.ProcessNextAvatarStepAsync` → `ZoneAreaModule` (entrées/sorties d'aires, D5) → événements de room.
2. Bulle d'aire (D7) ; `ZoneRulesQuery` (D6, D16).
3. `ZoneTravel` + `ZonePassageModule` (D8, D9). Preuve : marcher sur un passage de A, arriver sur l'entrée de B.

### Phase 4 — Interactions

1. Paquets `UseFurniture`/`UseWallItem` (parser, handler, test de rafale) — skill `adding-a-packet`.
2. `ZoneInteractionModule` + une classe par effet (D10) ; décor id ↔ objet (compilé). Preuve : porte qui s'ouvre et se traverse, panneau qui parle.

### Phase 5 — Déclencheurs

1. `ZoneTriggerModule` (écoute du bus de room, cooldowns D14, profondeur D15).
2. Évaluateurs de conditions (une classe chacun, D12) ; exécuteurs d'actions (une classe chacun, D13).
3. `ZoneClockSystem` (Timer). Preuve : entrer dans une aire → bulle + porte ouverte + téléport.

### Phase 6 — Validation complète et mode test

1. Validateur : les contrôles S§50–54 hors RPG (spawn joueur obligatoire, entrées sur case marchable, destinations de passages/téléports existantes, aires qui se chevauchent sûr/PvP, triggers inertes RPG signalés, boucles évidentes, interactions sur un décor inexistant).
2. Mode test D18 (route + bouton Tester). Preuve : le bouton ouvre le client directement dans la zone.

### Phase 7 — Bilan

Bilan écrit (`2026-10-07-coeur-du-monde.bilan.md`), questions de jeu restantes pour le propriétaire (D16 et celles apparues), relecture finale.

## Review Focus

1. **Un joueur arrive sur une case bloquée ou hors carte** (entrée mal placée, aire désactivée) → il arrive à la porte, jamais coincé. Pin : Phase 2.3.
2. **Un passage renvoie vers une zone non publiée ou supprimée** → le joueur reste où il est, avec une bulle, sans erreur serveur. Pin : Phase 3.3.
3. **Une boucle de triggers** (téléport vers une aire qui re-téléporte) → arrêtée à la profondeur 8, journalisée. Pin : Phase 5.1.
4. **Republier pendant qu'un joueur est dans une aire** → l'état vivant repart du nouveau document sans fuite d'abonnements. Pin : Phase 2.1 + test de rechargement.
5. **Un mobi interactif utilisé par deux joueurs en même temps** → un seul basculement par utilisation, état diffusé à tous. Pin : Phase 4.2.
