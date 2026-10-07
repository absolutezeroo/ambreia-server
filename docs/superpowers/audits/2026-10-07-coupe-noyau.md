# Coupe au noyau (« F0 ter ») — proposition à valider

> **Statut : choix tranchés le 2026-10-07 (voir « Décisions ») — liste à relire avant le plan.**
> Base : `main` @ `371cd7bf` (fin du M0 bis). Spec : `docs/superpowers/specs/2026-10-06-vortex-rpg-design.md`.

## Objectif (mots du propriétaire)

Garder le réseau, le pathfinding, la gestion des heightmaps et le moteur de room ; passer **directement au
système de zone** ; avoir **la connexion classique et rien à côté**. Tout ce qui ne sert pas à ça part ;
`main` d'aujourd'hui (tag à poser avant la coupe : `pre-noyau`) reste la carrière où l'on reprend un système
quand un jalon en a besoin.

## Ce qui reste

| Projet | Ce qu'on garde | Ce qui part à l'intérieur |
|---|---|---|
| `Vortex.Main` | hôte, configuration, enregistrement des modules gardés | modules des projets retirés |
| `Vortex.Networking`, `Pipeline`, `Runtime`, `Crypto`, `Messages`, `Plugins`, `Logging` | tout (socket, cadrage, chiffrement, dispatch, hôte de plugins) | — |
| `Vortex.Observability` | journaux, métriques, santé, audit | journaux de domaines retirés (économie, objets) si plus rien ne les alimente |
| `Vortex.Protocol` + `Vortex.Revisions` | le **cadre** (parsers, serializers, headers, maps) et les seuls messages du flux ci-dessous | les ~1 000 autres messages Habbo et leurs headers |
| `Vortex.PacketHandlers` | `Handshake`, `Room/Session`, `Room/Engine` (déplacement, alias de meubles ; **pas** de pose/déplacement/ramassage de meuble en jeu), `Room/Layout` (tuile d'entrée, cases occupées), `Room/Avatar` (regard) | `Catalog`, `Inventory`, `Marketplace`, `FriendList`, `Users` (groupes, profils, blocages), `Navigator`, `NewNavigator`, `Quest`, `Preferences`, `GroupForums`, `Help`, `Moderator`, `RoomSettings`, `Room/Action` (droits, kick, ban, mute), `Room/Pets`, `Room/Furniture` (dés, roue, post-it, mannequin…), `Room/Chat`, la pose/le déplacement/le ramassage de meubles en jeu (`PlaceObject`, `MoveObject`, `MoveWallItem`, `PickupObject`…), `Fishing`, `Tracking`, `Sound`, `Register`, `Nux`, `Advertisement`, `RewardVault`, `Notifications` |
| `Vortex.Rooms` | `RoomGrain` (cœur), `RoomMapModule` (heightmap, cases, hauteurs), `RoomPathingSystem`, `RoomAvatarModule` + `RoomAvatarTickSystem`, `RoomFurniModule` (chargement et placement des meubles de la zone ; **empilement gardé pour les maisons**, sans paquet joueur jusqu'à elles), le cadre des logiques de meubles (`Object/Logic`) avec les logiques de base, `RoomPersistenceGrain`, `RoomDirectoryGrain` | jeux (`Games/`), bots, pets, échanges, chat (`RoomChatSystem`), modération de room, sonnette, réglages, droits, objets en main, logiques interactives Habbo (dés, roue, feux d'artifice, post-it…), CFH, chatlogs, visites |
| `Vortex.Furniture` | définitions de meubles (fournisseur, données de logique) | — |
| `Vortex.Players` | `PlayerPresenceGrain` (sessions, envoi des composers), `PlayerDirectoryGrain`, le ticket SSO, la fiche minimale du joueur (nom, figure) | portefeuille et devises, abonnement, coffre de récompenses, messagerie, profils étendus, préférences, badges |
| `Vortex.Permissions` | rôles et capacités du jeu (staff ; l'éditeur en aura besoin) | sanctions, catalogue CFH, filtre de mots (reviennent avec le chat), capacités du dashboard |
| `Vortex.Database` | contexte, intercepteurs, migrations ; tables : comptes, joueurs, tickets SSO, rooms/zones, modèles, définitions de meubles, meubles posés, rôles | toutes les autres tables (≈ 80 %), migration `InitialCreate` régénérée |
| `Vortex.Primitives` | contrats des projets gardés | contrats des projets retirés |
| `Vortex.WebApi` + `Vortex.WebApi.Authentication` | inscription, connexion, création d'avatar, ticket SSO (sans eux, pas de « connexion classique ») | articles, profils publics, rooms publiques, notifications, signalements, mot de passe oublié, MFA, e-mail, verrou, questions de sécurité |

## Ce qui part entièrement

`Vortex.Catalog`, `Vortex.Inventory`, `Vortex.Marketplace`, `Vortex.Social`, `Vortex.Progression`,
`Vortex.Signals`, `Vortex.Fishing`, `Vortex.Navigator`, `Vortex.WebApi.Shop`, **`Vortex.Dashboard.API`,
`Vortex.Dashboard.Web`** et leurs projets de tests, plus les seeds, scripts SQL, hooks et skills qui les servent
(`check-dashboard-capabilities`, `check-logic-groups` côté dashboard, `dashboard-render`, `/new-dashboard-page`).

## Flux réseau gardé (la « connexion classique »)

1. **Poignée de main :** `ClientHello`, `InitDiffieHandshake`, `CompleteDiffieHandshake`, `VersionCheck`,
   `UniqueId`, `Pong`/latence, `Disconnect`.
2. **Connexion :** `SSOTicket` → `AuthenticationOK`, `UserRights`, et seulement ce dont le client a besoin pour
   atteindre son écran (à mesurer avec le client) ; `InfoRetrieve` → `UserObject`.
3. **Entrée en zone :** `OpenFlatConnection` → `OpenConnection`, `RoomReady`, `HeightMap`, `FloorHeightMap`,
   `RoomEntryTile`, `RoomVisualizationSettings`, `Users`, `Objects`, `Items`, `RoomEntryInfo`.
4. **Dans la zone :** `MoveAvatar` → `UserUpdate` (pathfinding), `GetFurnitureAliases`, `LookTo`. Pas de chat.

Tout le reste de la rafale SSO actuelle (devises, effets, navigateur, favoris, modération, pêche, perks,
succès, abonnement…) part.

## Décisions (2026-10-07)

1. **Chat de room : retiré.** Il revient avec le réseau RPG (F3).
2. **Construction des cartes : F4 tôt.** Plus de construction en jeu ; l'éditeur de bureau arrive juste après
   le modèle de zone (F1), puisqu'il en a besoin pour son format. Ordre : coupe → F1 → F4 → F2, F3, F5.
3. **Entrer dans une zone sans navigateur :** une zone de développement seedée, et la connexion y envoie le
   joueur (`RoomForward`) — l'amorce du « dernière zone du personnage » de F2. *Par défaut, faute d'objection.*
4. **Dashboard : retiré entièrement**, à recréer plus tard à partir de zéro.
5. **WebApi : le minimum** — inscription, connexion, avatar, ticket SSO.

## Conséquences

- **Spec :** les sections qui décrivent des systèmes gardés « pour les reprendre » (catalogue → marchands,
  quêtes, succès, groupes, échanges, hôtel des ventes) restent des cibles, mais leur code repart de la carrière
  au lieu d'être adapté sur place ; §3.4 bis (couches) et §22.1 bis (décisions) à amender.
- **Client :** le serveur n'enverra plus que le flux ci-dessus ; les fenêtres Habbo du client (catalogue,
  inventaire, navigateur…) resteront vides ou sans réponse jusqu'à leur retrait côté client. À coordonner avec
  la session du client.
- **Taille :** estimation à vérifier une fois la liste validée, de l'ordre d'un tiers des ~190 000 lignes C#
  hors tests reste.
- **Méthode :** même procédé que le M0 (plan écrit, une tâche par bloc, compilation par lot, suite complète et
  démarrage réel à la fin), sur une branche locale.
