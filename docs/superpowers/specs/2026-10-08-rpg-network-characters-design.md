# RPG — spec fille F3a : réseau RPG, écran des personnages et état du personnage

> **Statut :** à relire par le propriétaire · **Date :** 2026-10-08 · **Langue :** français
> **Dépôts :** `vortex-rpg-emulator` (serveur, cette spec) et `vortex-rpg-client` (client, par la
> session client, à partir du contrat de §6). Première partie de F3 « Réseau RPG et interaction »
> (spec unifiée §21.2, §3.7, §3 bis). Dépend de F2 livré : `IPlayerCharacterGrain`, classe de départ
> « Sans classe » (spec Personnage §2.4 révisée).

---

## 1. But

Il n'y a pas de site : le client est la seule interface du joueur. Il se connecte, choisit ou crée
son personnage, lui donne sa classe et entre en jeu, puis le HUD montre son niveau, son XP et ses
stats. Tout ce qui se passe après le login passe par la socket de jeu, en paquets RPG (ids ≥ 9000).

**Critère de réussite.** Avec le client seul : se connecter, créer un personnage (il naît « Sans
classe »), lui choisir Guerrier, entrer en jeu dans sa dernière zone, voir niveau, XP et stats du
Guerrier au HUD ; une XP accordée fait bouger la barre et le niveau sans recharger.

## 2. Décisions — APPROUVÉ (conversation du 2026-10-08)

1. **Pas de site.** HTTP reste pour le **login** (mot de passe en HTTPS, avant que la socket existe),
   l'éditeur de cartes, l'import du contenu et l'administration ; **tout le jeu** passe par paquets.
2. **Une socket, deux phases** : phase *compte* (écran des personnages) puis phase *personnage*
   (en jeu), sur la même connexion.
3. **Le choix de la classe** (quitter « Sans classe ») se fait **à l'écran des personnages**.
4. **La session client** implémente le client (écran de login, écran des personnages, HUD) à partir
   du contrat de §6 ; ce dépôt fait le serveur.
5. Pas de suppression de personnage ni de retour à l'écran des personnages sans se déconnecter dans
   F3a.

## 3. Périmètre

**Dans F3a.** Ticket de compte ; phase compte de la socket ; liste, création, choix de classe et
entrée en jeu par paquets ; état du personnage (instantané à l'entrée en zone, deltas ensuite) ;
barrière de phase ; limite de débit par intention ; codes d'erreur stables.

**Hors F3a.** Routage du clic vers une action RPG et décor figé (F3b, « Interaction ») ; chat
(F3c) ; regroupement des événements de combat par tick (M1, spec Combat) ; PV et mana *courants*
(M1) ; suppression de personnage ; retour à l'écran des personnages en jeu.

## 4. Parcours du joueur

1. **Login (HTTP)** : `POST /api/public/registration/new` ou `/api/public/authentication/login`,
   puis `GET /api/ssotoken` **sans** avatar sélectionné → **ticket de compte**.
2. **Socket, phase compte** : le client envoie `SSOTicket` (882) avec ce ticket. Le serveur répond
   `RpgCharacterList` et `RpgClassList` (§6).
3. **Écran des personnages** : `RpgCharacterCreate` (le personnage naît « Sans classe »),
   `RpgCharacterChooseClass` (une fois, depuis « Sans classe »), `RpgCharacterListRequest`. Après
   chaque création ou choix réussi, le serveur renvoie `RpgCharacterList`.
4. **`RpgEnterWorld(playerId)`** : la session devient celle du joueur ; le serveur envoie, comme le
   SSO d'un ticket de joueur aujourd'hui, `AuthenticationOK` (230), `UserRights` (3599) et
   `RoomForward` (3339) vers : l'entrée de test en attente, sinon la room en grâce, sinon la
   dernière zone publiée, sinon `world.start_room_id` (spec Personnage §9, §10).
5. **En jeu** : à l'entrée en zone, `RpgCharacterState` ; ensuite `RpgCharacterProgress` à chaque
   gain d'XP et `RpgCharacterStats` à chaque changement de stats.

Le ticket de **joueur** (avatar sélectionné avant `/api/ssotoken`) reste valable : il ouvre
directement la phase personnage. Le driver et le mode test de l'éditeur s'en servent.

## 5. Serveur

### 5.1 Ticket de compte

- Table **`account_tickets`** (sur le modèle de `security_tickets`) : `account_id` (unique, clé
  étrangère vers `player_accounts`), `ticket` (unique), `ip_address`, `expires_at`, horodatages.
- `GET /api/ssotoken` : avatar sélectionné (ou `uniqueId`) → ticket de joueur comme aujourd'hui ;
  sinon → ticket de compte. Même limite de débit qu'aujourd'hui.
- `IAuthenticationService` résout un ticket en **joueur** ou en **compte** (une seule méthode qui
  rend l'un ou l'autre), avec les mêmes règles que le ticket de joueur : durée de vie, expiration
  glissante plafonnée et usage unique selon `AuthenticationConfig.TicketSingleUse`. Aucun des deux
  ne contrôle l'IP aujourd'hui (elle n'est que hachée dans les events de connexion).

### 5.2 Phase compte

- **`IAccountSessionGrain`** (clé = id du compte, contrat dans `Vortex.Primitives`, grain dans
  `Vortex.Players`) : tient l'observateur de la socket pendant la phase compte et envoie les
  paquets (`SendComposerAsync`), comme `PlayerPresenceGrain` pour un joueur. Une seule session de
  compte à la fois : une deuxième connexion du même compte remplace la première (comme la
  connexion concurrente d'un joueur).
- **`SessionGateway`** gagne le rattachement d'une session à un compte et le passage du compte au
  joueur ; la fermeture de la socket en phase compte libère simplement la session de compte (pas de
  délai de grâce : aucun avatar en zone).
- **`MessageContext`** porte l'id du compte en phase compte (le `PlayerId` reste invalide).

### 5.3 Personnages d'un compte

- **`IAccountCharactersGrain`** (clé = id du compte, contrat dans `Vortex.Primitives`, grain dans
  `Vortex.Rpg.Characters`) :
  - `ListAsync()` : les avatars du compte (non supprimés) avec leur personnage (classe,
    spécialisation, niveau, classe de départ ou non) par `IPlayerCharacterGrain` ;
  - `CreateAsync(name, figure, gender)` : 5 au plus (`CharacterConfig.MaxCharactersPerAccount` ;
    `WebApiConfig.MaxAvatarsPerAccount`, configuration du plugin web, reste pour les routes HTTP),
    nom validé comme aujourd'hui (`NameShape`, interne au web API aujourd'hui, déplacé dans
    `Vortex.Primitives` pour être partagé ; unique), figure de 255 caractères au plus
    (`FigureString.MaxLength`, la seule règle appliquée aujourd'hui) ; crée la ligne `players` puis donne « Sans classe » par
    `IPlayerCharacterGrain.ChooseClassAsync` ;
  - `ChooseClassAsync(playerId, classId)` : vérifie l'appartenance, puis `IPlayerCharacterGrain`.
  - Chaque méthode envoie elle-même le résultat (`RpgCharacterResult`) et, en cas de succès, la
    liste à jour, par `IAccountSessionGrain`.
- **Handlers** (`Vortex.PacketHandlers/Rpg/Characters/`) : un appel à ce grain, avec l'id du compte
  pris dans `MessageContext`, jamais dans le paquet.

### 5.4 Entrée en jeu

- `RpgEnterWorld(playerId)` : appartenance vérifiée par `IAccountCharactersGrain` ; puis la socket
  quitte la phase compte et est rattachée au joueur par **le même chemin que le SSO d'un ticket de
  joueur** (la suite de `SSOTicketMessageHandler` extraite en un service commun : rattachement,
  droits, lieu d'arrivée, `AuthenticationOK`, `UserRights`, `RoomForward`).
- Un personnage d'un autre compte → `RpgCharacterResult(3, "character.not_owned")`, la phase
  compte continue.

### 5.5 Barrière de phase et débit

- **Barrière** : un comportement de pipeline (`IMessageBehavior`, comme `RateLimitBehavior`)
  refuse un paquet d'écran des personnages (`IAccountPhaseMessage`, interface marqueur des quatre
  messages de §6.1) quand la session n'est pas en phase compte. Les paquets de jeu restent filtrés
  par leurs handlers, qui refusent déjà une session sans joueur (`PlayerId <= 0`).
- **Débit par intention** (`RpgIntentLimitConfig`, section `Vortex:RpgIntents`) : création 1
  toutes les 2 s, choix de classe 1/s, entrée en jeu 1/s, liste 1/s, par session. Un paquet en
  trop reçoit `RpgCharacterResult(action, "rate_limited")` (la liste : rien) et n'atteint pas le
  grain. S'ajoute à la limite globale de session existante.

### 5.6 État du personnage en jeu

`PlayerCharacterGrain` envoie, par `PlayerPresenceGrain.SendComposerAsync` (le grain qui change
l'état envoie) :

| Quand | Message |
|---|---|
| Entrée en zone (`PlayerEnteredRoomEvent`, le handler de dernière zone existant) et reprise après la grâce | `RpgCharacterState` |
| Gain d'XP (après le `XpGained`, montées de niveau comprises) | `RpgCharacterProgress` |
| Stats changées (niveau, classe, contenu rechargé) | `RpgCharacterStats` |

Un avatar sans personnage n'envoie rien. Les PV et le mana courants viennent avec M1 ; d'ici là le
client affiche les barres pleines au maximum.

## 6. Contrat des paquets (pour les deux dépôts)

Types sur le fil : entier = int32 gros-boutiste ; chaîne = int16 de longueur + UTF-8 ; booléen = un
octet (0/1) ; liste = int32 du nombre puis les éléments. Ids dans les régions `Vortex-specific` de
`Headers.cs` (`// RPG (spec §3.7)`), `MessageEvent` et `MessageComposer` étant deux espaces
distincts. Le client les obtient par `pnpm gen:headers`.

### 6.1 Client → serveur (`MessageEvent`)

| Id | Message | Champs, dans l'ordre |
|---|---|---|
| 9000 | `RpgCharacterListRequest` | — |
| 9001 | `RpgCharacterCreate` | name (chaîne), figure (chaîne), gender (chaîne, `M` ou `F`) |
| 9002 | `RpgCharacterChooseClass` | playerId (entier), classId (chaîne) |
| 9003 | `RpgEnterWorld` | playerId (entier) |

### 6.2 Serveur → client (`MessageComposer`)

| Id | Message | Champs, dans l'ordre |
|---|---|---|
| 9000 | `RpgCharacterList` | maxCharacters ; count ; par personnage : playerId, name, figure, gender, classId, specializationId, level, starter (booléen) |
| 9001 | `RpgClassList` | count ; par classe : id, role, range, primaryStat, starter (booléen), specializations (liste de chaînes) |
| 9002 | `RpgCharacterResult` | action (1 créer, 2 classe, 3 entrer), code (chaîne), playerId (0 si aucun) |
| 9003 | `RpgCharacterState` | playerId, classId, specializationId, level, maxLevel, xp, xpToNext, puis le bloc de stats |
| 9004 | `RpgCharacterProgress` | level, xp, xpToNext |
| 9005 | `RpgCharacterStats` | le bloc de stats |

**Bloc de stats** (ordre fixe) : strength, intelligence, agility, maxHealth, maxMana, armor, critBp,
dodgeBp, speedPercent, power — tous entiers ; `critBp` et `dodgeBp` sont des pourcentages × 100
(620 = 6,2 %). `xpToNext` vaut 0 au niveau maximal. `role` et `primaryStat` s'écrivent comme dans
le contenu (`tank`, `strength`). Un personnage sans classe connue du contenu a `classId` tel quel et
des stats de classe nulles (spec Personnage §12).

### 6.3 Codes de `RpgCharacterResult`

`ok`, `character.name_invalid`, `character.name_taken`, `character.limit_reached`,
`character.figure_invalid`, `character.not_owned`, `character.unknown_class`,
`character.class_already_chosen`, `rate_limited`. Le client traduit chaque code.

### 6.4 Côté client (pour la session client)

- Un écran de **login** (inscription et connexion par les routes HTTP de §4, puis
  `/api/ssotoken`), qui ouvre la socket avec le ticket de compte au lieu d'exiger `?sso=`.
- Un écran des **personnages** : la liste (`RpgCharacterList`), un bouton « Créer » (nom, figure,
  genre), « Choisir sa classe » pour un personnage `starter` (les classes non `starter` de
  `RpgClassList`), « Entrer en jeu ».
- Les messages de §6 dans le style `MessageMap` (`features/character`, `features/hud`), leurs ids
  listés dans `NewStyleHeaders.ts` ; le HUD rempli par `RpgCharacterState` / `Progress` / `Stats`
  (barres de PV et de mana pleines au maximum jusqu'à M1).

## 7. Erreurs et concurrence

- Toute erreur attendue rend un code de §6.3 ; une exception inattendue est journalisée et rend
  le code générique de l'action en échec sans fermer la socket.
- Deux créations simultanées du même compte : le grain du compte les sérialise ; la limite de 5
  tient.
- Une connexion du même compte pendant une phase compte remplace la précédente ; une connexion
  pendant une phase personnage suit les règles de la présence du joueur (grâce, reprise).
- Un ticket de compte suit les règles d'un ticket de joueur : usage unique si
  `TicketSingleUse` est vrai, sinon réutilisable jusqu'à son expiration glissante (300 s au plus).

## 8. Observabilité

Log structuré à chaque création, choix de classe et entrée en jeu (compte, joueur, classe) ;
compteur des paquets refusés par la barrière de phase et par la limite d'intention (raison bornée).

## 9. Tests

- **Fil** : chaque composer et chaque parser de §6, champs lus dans l'ordre du client
  (`Vortex.Revisions.Tests`), et `check-inert-declarations` à 0.
- **Grain du compte** : liste, création (« Sans classe », limite de 5, nom pris ou invalide, figure
  invalide), choix de classe (appartenance, une fois), résultats envoyés.
- **SSO** : ticket de compte → phase compte et listes envoyées ; ticket de joueur inchangé.
- **Entrée en jeu** : appartenance, mêmes paquets et même lieu d'arrivée que le SSO d'un joueur.
- **Barrière et débit** : paquet d'écran hors phase compte refusé ; paquet en trop → `rate_limited`.
- **État** : `RpgCharacterState` à l'entrée en zone, `Progress` après un gain d'XP, `Stats` après
  une montée de niveau ou un choix de classe ; rien pour un avatar sans personnage.
- **En vrai** (`driver.mjs`) : une commande qui suit le parcours de §4 par la socket seule (ticket
  de compte, création, classe, entrée en jeu, état reçu, XP accordée puis `Progress` reçu).

## 10. Critères d'acceptation

- Le driver, sans route d'avatar HTTP, crée un personnage par paquets, lui choisit Guerrier, entre
  en jeu et reçoit un `RpgCharacterState` aux stats du Guerrier niveau 1.
- Une XP accordée (route d'administration) produit un `RpgCharacterProgress` reçu sur la socket.
- Un paquet d'écran des personnages envoyé en jeu est refusé ; un paquet de jeu en phase compte
  n'a aucun effet.
- La porte de qualité passe ; le client, de son côté, régénère ses ids et sert ces messages.
