# RPG — spec fille F3c « Chat » : canaux, privé, système, liens d'objets

> **Statut :** livrée le 2026-10-09 (plan `docs/superpowers/plans/2026-10-09-rpg-chat.md`, preuve
> `driver.mjs chat`) ; design approuvé par le propriétaire le 2026-10-09 ·
> **Date :** 2026-10-09 · **Langue :** français
> **Dépôt :** `vortex-rpg-emulator`. Jalon F3 « Réseau RPG et interaction » de la spec unifiée
> `docs/superpowers/specs/2026-10-06-vortex-rpg-design.md` (§13 Social, §16.3 HUD, §18 limitation de
> débit). Suit F3a (`2026-10-08-rpg-network-characters-design.md`) et F3b
> (`2026-10-09-rpg-interaction-design.md`). La spec unifiée disait « chat : l'existant » ; la coupe
> au noyau du 2026-10-07 l'a retiré : ce chat-ci le remplace.

---

## 1. But

Un vrai chat de RPG, dans une fenêtre à onglets du HUD, et **plus aucune bulle** au-dessus des
avatars. Les messages vont dans des **canaux** : la zone, le monde, le privé, le système ; le groupe
et la guilde sont prêts mais inactifs jusqu'à leurs systèmes. Une ligne se lit
« [14:25] [Général] Marek : vend [Minerai de Fer] x10 » : heure du serveur, canal, émetteur, texte
avec des liens d'objets.

**Critère de réussite.** Deux joueurs dans la même zone se lisent en Général ; deux joueurs dans
des zones différentes se lisent en Monde mais pas en Général ; un privé atteint un joueur nommé où
qu'il soit, et un nom hors ligne est refusé ; un message Guilde est refusé (`not_in_guild`) ; un
lien vers un objet qui n'existe pas disparaît du texte.

## 2. Décisions — APPROUVÉ (conversation du 2026-10-09)

1. **Plus de bulles** : le chat Habbo de la room (paquets `Chat`, `Shout`, `Whisper`) ne revient pas.
2. **Canaux** :

   | Canal | Qui l'entend | État en F3c |
   |---|---|---|
   | `general` | les joueurs de la même zone (la room de zone, copie de donjon comprise) | actif |
   | `world` | tous les joueurs en jeu | actif |
   | `whisper` | un joueur nommé, et l'émetteur (sa copie) | actif |
   | `system` | un joueur, ou tous ; jamais envoyé par un joueur | actif |
   | `group` | les membres du groupe | inactif : refus `not_in_group` (M4) |
   | `guild` | les membres de la guilde | inactif : refus `not_in_guild` (après V1) |

3. **Onglets côté client** : « Général », « Groupe », « Guilde » par défaut ; « + » ouvre un onglet
   (un privé avec un joueur, le Monde, un filtre). Le serveur ne connaît que les canaux.
4. **Routage par le grain de présence de l'émetteur** (`PlayerPresenceGrain`) : c'est le grain du
   joueur, là où vivront plus tard sa mise en sourdine et son blocage ; aucun grain central de chat.
5. **Liens d'objets** dans le texte, vérifiés par le serveur (§6).
6. **Rien n'est stocké** : ni historique, ni journal de modération en V1.
7. **Modulaire et extensible** (demande du propriétaire, 2026-10-09 : « n'oublie jamais
   modularité, extensibilité ») : chaque canal, chaque sorte de lien et chaque contrôle est une
   pièce **déclarée** par le module qui la possède, comme les sources de stats et les sortes de
   contenu ; le cœur du chat ne les connaît pas par leur nom (§14).

## 3. Périmètre

**Dans F3c.** Les canaux `general`, `world`, `whisper`, `system` ; les refus de `group` et `guild` ;
les liens d'objets ; la limitation de débit par canal ; la recherche d'un joueur par son nom ;
l'envoi d'un message système par le serveur ; le contrat réseau pour la session client ; la preuve
du driver.

**Hors F3c.** Les groupes (M4) et les guildes (après V1), le blocage et la mise en sourdine (avec
le social), la modération et les journaux de chat, les commandes de chat (`/w`, `/1` : c'est le
client qui choisit le canal), les emojis (rendus par le client à partir du texte), l'historique au
retour en jeu.

## 4. Envoi et routage

1. Le client envoie `RpgChatSend` : canal, destinataire (un nom, pour `whisper` seulement), texte.
2. Le handler appelle `PlayerPresenceGrain.SendChatAsync(channel, target, text)` du joueur, et
   rien d'autre.
3. La présence fait passer l'envoi dans le pipeline (§14) : joueur en jeu → canal déclaré et
   envoyable par un joueur (`system` ne l'est pas) → texte nettoyé (§5) → contrôles déclarés, par
   priorité (longueur, débit §7…) → liens (§6) → `ChatSendingEvent` (annulable). Le premier refus
   répond `RpgChatResult` avec son code, à l'émetteur seul, et rien n'est diffusé.
4. Elle construit `RpgChatMessage` (canal, id et nom de l'émetteur, texte nettoyé, heure du serveur)
   et le confie au canal, qui le route. Les canaux livrés en F3c routent ainsi :
   - `general` : au flux de la room où se trouve le joueur (le flux qu'écoutent déjà les
     présences des joueurs de cette room) ; sans room, refus `not_in_zone` ;
   - `world` : à un flux Orleans `world-chat`, auquel chaque présence s'abonne quand son joueur
     entre dans le monde et se désabonne quand il en sort ;
   - `whisper` : la présence cherche le destinataire par son nom (§8) ; inconnu → `player_not_found`,
     hors ligne → `player_offline`, soi-même → `player_not_found` ; sinon le message va à sa présence
     et une copie à l'émetteur (pour qu'il voie ce qu'il a écrit dans l'onglet du privé) ;
   - `group` / `guild` : refus `not_in_group` / `not_in_guild`.
5. Un envoi réussi ne répond pas `RpgChatResult` : la ligne elle-même revient à l'émetteur par son
   canal (il fait partie de la zone, du monde, ou reçoit sa copie du privé).

## 5. Texte

- Retrait des espaces en tête et en fin, et des caractères de contrôle ; vide après cela → `empty`.
- Au plus 200 caractères (`ChatConfig.MaxLength`) après nettoyage → sinon `too_long` (rien n'est
  tronqué en silence).
- Le serveur ne réécrit rien d'autre : pas de filtre de mots en V1.

## 6. Liens d'objets

- Un lien est un jeton `[item:<id de contenu>]` dans le texte (`[item:item.material.amber_shard]`).
- Le serveur garde le jeton si l'id est une définition d'objet connue du contenu (`IRpgContent`,
  retirée ou non) ; il **retire** le jeton sinon, le reste du texte inchangé. On ne peut pas lier un
  objet inventé.
- Au plus 5 liens par message (`ChatConfig.MaxLinks`) ; au-delà, les suivants sont retirés.
- Le client affiche le nom traduit (`<id>.name`) entre crochets, coloré par la rareté s'il veut.

## 7. Débit

- Une fenêtre par joueur et par canal, réglable (`ChatConfig`) : `general` 1 message par seconde,
  `world` 1 toutes les 10 s, `whisper` 1 par seconde. Au-delà → `rate_limited`, rien n'est diffusé.
- C'est un contrôle déclaré (`IChatRule`, §14). Ses fenêtres vivent dans l'état d'émetteur que la
  présence du joueur tient et passe aux contrôles (en mémoire, perdu à la déconnexion), en plus de
  la limite globale de paquets par session. Un canal sans limite dans la table n'est pas limité
  au-delà de la limite globale.

## 8. Retrouver un joueur par son nom

- `IPlayerDirectoryGrain` gagne `GetPlayerIdByNameAsync(string name)` : l'id du joueur de ce nom
  exact (insensible à la casse, comme l'index unique de `players.name`), ou null.
- « Hors ligne » : la présence du destinataire répond `IsOnlineAsync` faux (aucune session de jeu).

## 9. Messages système

- `PlayerPresenceGrain.SendSystemChatAsync(string text)` : une ligne `system` à ce joueur, sans
  émetteur (id 0, nom vide). Un système du serveur l'appelle (une quête, plus tard) ; pas d'envoi à
  tous en F3c (ajouté quand un système en aura besoin : par le flux `world-chat`).
- Le texte d'un message système peut être une clé de traduction : le client la traduit si elle en
  est une (convention §7 de la spec Contenu, `fr.txt`).

## 10. Réseau (contrat pour les deux dépôts)

Types sur le fil comme la spec F3a §6. Ids dans les régions `Vortex-specific` de `Headers.cs`
(`// RPG (spec §3.7)`). Paquets de **phase monde**.

### 10.1 Client → serveur (`MessageEvent`)

| Id | Message | Champs, dans l'ordre |
|---|---|---|
| 9011 | `RpgChatSend` | channel (chaîne : `general`, `world`, `whisper`, `group`, `guild`), target (chaîne : un nom pour `whisper`, vide sinon), text (chaîne) |

### 10.2 Serveur → client (`MessageComposer`)

| Id | Message | Champs, dans l'ordre |
|---|---|---|
| 9010 | `RpgChatMessage` | channel (chaîne, dont `system`) ; senderId (entier, 0 pour `system`) ; senderName (chaîne) ; text (chaîne, liens `[item:…]` compris) ; sentAt (entier : secondes depuis l'époque Unix, UTC) ; target (chaîne : pour `whisper`, le nom de l'autre joueur de la conversation, vide sinon) |
| 9011 | `RpgChatResult` | channel (chaîne) ; code (chaîne : `empty`, `too_long`, `rate_limited`, `not_in_zone`, `player_not_found`, `player_offline`, `not_in_group`, `not_in_guild`, `unknown_channel` ; et `refused` : un `ChatSendingEvent` annulé sans raison — avec une raison, c'est elle le code) |

`target` de `RpgChatMessage` permet au client de ranger un privé dans l'onglet de l'autre joueur,
qu'il en soit l'émetteur (sa copie) ou le destinataire.

### 10.3 Côté client (pour la session client)

- La fenêtre de chat à onglets du HUD (« Général », « Groupe », « Guilde », « + »), la barre de
  saisie et le bouton d'emojis ; plus de bulles au-dessus des avatars.
- Une ligne : `[hh:mm]` (heure locale de `sentAt`), `[Canal]` traduit et coloré, le nom de
  l'émetteur, le texte ; un lien `[item:id]` rendu `[Nom traduit]`.
- Un clic sur un nom ouvre l'onglet privé de ce joueur ; écrire dans cet onglet envoie `whisper`
  avec son nom.
- Un `RpgChatResult` s'affiche dans l'onglet actif, traduit.

## 11. Erreurs et concurrence

- La présence de l'émetteur traite ses envois l'un après l'autre (grain non réentrant) : l'ordre de
  ses messages est préservé dans chaque canal.
- La présence n'appelle jamais sa room pour diffuser : elle publie sur le flux de la room, qui ne
  rappelle pas la présence (pas de cycle). Le privé appelle la présence du destinataire par
  `SendComposerAsync` (`[AlwaysInterleave]`).
- Un échec de diffusion (flux, présence du destinataire) est journalisé ; l'émetteur reçoit
  `RpgChatResult` seulement pour les refus de §4–§7, pas pour une panne.

## 12. Observabilité

Compteur des messages par canal et des refus par code. Aucun texte journalisé (vie privée) ; un
refus `rate_limited` est journalisé en debug avec le joueur et le canal.

## 13. Tests

- Texte : vide, espaces seuls, 200 et 201 caractères, caractères de contrôle.
- Liens : jeton connu gardé, inconnu retiré, plus de 5 retirés au-delà du 5e, jeton mal formé
  laissé comme du texte.
- Débit : deux envois `world` en moins de 10 s → le second `rate_limited` ; après la fenêtre, passe.
- Routage : `general` publié sur le flux de la room ; `world` sur le flux `world-chat` ; `whisper`
  au destinataire et en copie à l'émetteur ; nom inconnu, hors ligne, soi-même ; `group`, `guild`,
  `system` refusés ; joueur hors room en `general` → `not_in_zone`.
- Annuaire : nom exact, casse différente, inconnu.
- Fil : parser de `RpgChatSend`, serializers des deux sortants ; handler sans joueur ne fait rien.
- Extensibilité (§14), avec des pièces déclarées par le test :
  - un canal `group` déclaré remplace le canal inactif, et un envoi `group` le route ;
  - un canal nouveau (`trade`) se route sans toucher au cœur, et un canal non déclaré → `unknown_channel` ;
  - un contrôle déclaré refuse un envoi avec son code, à sa priorité ;
  - une sorte de lien déclarée (`quest`) garde ses jetons valides et retire les autres ;
  - `ChatSendingEvent` annulé → rien n'est diffusé ; `ChatSentEvent` publié après une diffusion.

## 14. Modularité et extensibilité

Le chat est un **pipeline** dont chaque étape est une pièce déclarée par DI (le module qui la
possède ajoute une ligne, comme `IStatSource` ou `IContentKind`). Le cœur — la présence et le
pipeline — ne nomme aucun canal, aucune sorte de lien, aucun contrôle.

### 14.1 Les pièces

| Pièce | Contrat (dans `Vortex.Primitives`) | Livrées en F3c | Ajoutées plus tard par |
|---|---|---|---|
| **Canal** | `IChatChannel` : sa clé (`general`…), si un joueur peut y écrire, et son routage (qui reçoit) ; un refus est un code | `general`, `world`, `whisper`, `system`, `group` et `guild` inactifs | le module Groupe remplace `group` ; le module Guilde, `guild` ; un canal de commerce, de recherche de groupe, d'événement… |
| **Sorte de lien** | `IChatLinkKind` : son préfixe (`item`) et la vérification de son id | `item` (contenu `item`) | `quest`, `skill`, `player`, `zone`… par leur module |
| **Contrôle** | `IChatRule` : vérifie ou refuse un envoi avant la diffusion, avec un code | longueur, débit par canal | blocage et sourdine (social), filtre de mots, niveau minimum pour le Monde, anti-spam |

- Un canal se remplace par sa clé : le module Groupe déclare son `group` et il prend la place du
  canal inactif livré en F3c, quel que soit l'ordre de chargement des modules (tout canal livré
  par le cœur, `IsDefault`, cède sa clé à un canal déclaré par un module), sans rien changer
  d'autre (le registre garde le dernier déclaré pour une clé, et le journalise au démarrage).
- L'ordre des contrôles est déclaré (une priorité), stable et journalisé au démarrage.
- La diffusion passe par un **contexte** donné au canal (`ChatRouting`) : envoyer à un joueur, à
  la room d'un joueur, à un flux nommé. Une présence n'écoute que le flux de sa room et
  `world-chat` : un flux que personne n'a rejoint n'atteint personne. Un canal futur (groupe)
  envoie donc membre par membre (`SendToPlayerAsync`), ce qui ne touche pas au grain de présence ;
  un flux par groupe demandera d'ajouter son abonnement (décision du module Groupe, M4).

### 14.2 Les événements

- `ChatSendingEvent` (annulable, publié par la présence avant la diffusion) : un système peut
  refuser un envoi ou réécrire son texte sans être une `IChatRule` — un plugin, un événement.
- `ChatSentEvent` (après la diffusion, hors du chemin critique) : les objectifs de quête « dire »,
  des statistiques, un journal de modération le jour où il existera.
- `system` est envoyé par le serveur par une seule porte (`SendSystemChatAsync`), qu'un système
  appelle sans connaître le routage.

### 14.3 Le protocole

- Un canal est une **chaîne** sur le fil, pas un nombre : un canal nouveau ne change ni les
  paquets ni leurs ids. Un canal inconnu du serveur → `unknown_channel`.
- `RpgChatMessage` porte de quoi ranger n'importe quel canal futur côté client (canal, émetteur,
  conversation `target`).

### 14.4 La configuration

- `ChatConfig` (section `Vortex:Chat`) : longueur, nombre de liens, et la fenêtre de débit **par
  clé de canal** (`RateLimits: { "general": 1000, "world": 10000, "whisper": 1000 }`, en ms) ; un
  canal ajouté prend sa limite dans la même table, sans code.

### 14.5 Où vit le code

- Les contrats (`IChatChannel`, `IChatLinkKind`, `IChatRule`, les événements, `ChatRouting`) dans
  `Vortex.Primitives/Chat`.
- Le pipeline et les pièces livrées en F3c dans `Vortex.Players/Chat` (`Channels/`, `Links/`,
  `Rules/`), câblés par le module des joueurs ; la sorte de lien `item` dans `Vortex.Rpg` (elle lit
  le contenu), déclarée par `RpgModule`.
- Pas de nouveau projet en F3c ; si le chat grossit (journal, modération), il pourra devenir le
  sien sans changer ses contrats.

## 15. Critères d'acceptation

- `driver.mjs chat`, deux comptes :
  - A et B dans la même zone : A écrit en Général, B le lit ;
  - B change de zone : A écrit en Général, B ne le lit pas ; A écrit en Monde, B le lit ;
  - A chuchote à B par son nom : B le lit, A reçoit sa copie ; A chuchote à un nom hors ligne →
    `player_offline` ;
  - A écrit en Guilde → `not_in_guild` ;
  - A écrit un lien vers un objet inventé : la ligne reçue ne contient plus le jeton.
- La porte de qualité est verte.
