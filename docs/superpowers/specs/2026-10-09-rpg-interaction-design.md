# RPG — spec fille F3b « Interaction » : décor figé, objets cliquables, cible

> **Statut :** design approuvé par le propriétaire le 2026-10-09 (conversation), à relire écrit ·
> **Date :** 2026-10-09 · **Langue :** français
> **Dépôt :** `vortex-rpg-emulator`. Jalon F3 « Réseau RPG et interaction » de la spec unifiée
> `docs/superpowers/specs/2026-10-06-vortex-rpg-design.md` (§3 bis « Interaction », §3.2 bis, §3.5,
> §6.1, §16.3). Suit F3a (`2026-10-08-rpg-network-characters-design.md`) et Objets
> (`2026-10-08-rpg-items-design.md`). Le chat est F3c ; les compétences lancées sur la cible, M1.

---

## 1. But

Poser les fondations du clic avant le combat, pour que M1 n'écrive plus que du gameplay (spec
unifiée §3 bis). Trois choses :

1. **Le décor d'une zone est figé** : un objet sans action RPG ne réagit à rien, quels que soient
   les droits de celui qui clique (§3.2 bis).
2. **Le client sait ce qui est cliquable** : le serveur lui envoie la liste des objets qui ont une
   action, et le client cesse d'ouvrir l'infobulle Habbo.
3. **Un joueur a une cible** : cliquer une entité la sélectionne côté serveur ; le HUD montre qui
   elle est. M1 y lancera ses compétences.

**Critère de réussite.** Dans une zone, un meuble à plusieurs états sans interaction ne change pas
d'état au clic, même pour le propriétaire de la room ; une porte s'ouvre toujours ; le client reçoit
la liste des objets cliquables en entrant ; deux joueurs dans la même zone : A cible B et voit son
nom, son niveau, sa classe ; B s'en va, la cible de A se vide.

## 2. Décisions — APPROUVÉ (conversation du 2026-10-09)

1. **Périmètre** : décor figé, objets cliquables, cible. Pas de PNJ (M3), pas de compétence (M1).
2. **Objets cliquables par un paquet RPG dédié** (`RpgZoneInteractives`), sans toucher aux paquets
   de meubles Habbo.
3. **La cible vit dans le runtime de la zone** (la room), comme le veut §3.5 : c'est là que M1
   vérifiera portée et ligne de vue sans aller-retour entre grains.
4. **Cible privée et sans portée** : les autres ne voient pas qui les cible ; la portée se vérifie
   en agissant (M1, §6.3).

## 3. Périmètre

**Dans F3b.** Le refus du décor sans interaction dans une zone ; le paquet des objets cliquables ;
la cible (choix, réponse, effacement) ; le contrat réseau pour la session client ; les preuves du
driver.

**Hors F3b.** Les interactions nouvelles (coffre, atelier, nœud de récolte : leurs jalons), les PNJ
et le dialogue (M3), les compétences et la portée d'action (M1), les maisons où le joueur pose ses
meubles (chantier Maisons), le cercle de cible et l'infobulle (rendu client, session client), les
barres de vie (M1).

## 4. Décor figé

- Dans une room qui est une zone (elle a un runtime de zone), `UseFurniture` et `UseWallItem` sur un
  objet du décor :
  - avec une interaction (`door`, `lever`, `sign`, `teleport`, `sit`, `use`) : elle s'exécute,
    portée et approche comprises, comme aujourd'hui (`ZoneInteractionModule`) ;
  - sans interaction : **rien**. Ni logique Habbo, ni changement d'état, ni passe-droit pour le
    propriétaire, les droits ou les modérateurs. Le refus est silencieux, journalisé en debug.
- Les objets muraux du décor n'ont pas d'interaction : ils sont toujours figés.
- Hors zone (une room Habbo qui n'est pas une zone), le chemin actuel ne change pas ; il servira
  aux maisons.
- `ZoneDecorObject.Locked` et `ZoneDocument.Interactives` ne sont lus par aucun code. Ils sont
  retirés si la lecture des versions de zone déjà stockées tolère leur absence (un champ inconnu
  ignoré) ; sinon ils restent déclarés, commentés « ignoré », comme les champs retirés du contenu.

## 5. Objets cliquables

- À l'entrée en zone, avec la rafale d'entrée, le serveur envoie `RpgZoneInteractives` : chaque
  objet du décor qui a une interaction, par son id d'objet de room et sa sorte.
- Une republication de la zone renvoie déjà les joueurs dans la room (`RoomForward`, prouvé par
  `driver.mjs zone`) : la liste suit l'entrée suivante.
- L'état d'un objet (porte ouverte ou fermée) continue de passer par les paquets de meubles Habbo
  existants.

## 6. La cible

- Le client envoie `RpgTargetSelect` avec l'id d'objet de room de l'entité cliquée — celui que
  donne `Users` —, ou 0 pour vider la cible.
- La room (un module `RoomTargetModule`) valide : l'entité est un avatar de joueur présent dans la
  même room, soi-même compris. Les monstres et les PNJ entreront par la même porte (M1, M3). Un id
  invalide vide la cible.
- La room répond **à ce joueur seul** `RpgTarget` : l'id d'objet (0 = aucune cible), la sorte
  (`player`), le nom, le niveau et la classe. Niveau et classe viennent du grain du personnage ciblé
  (`IPlayerCharacterGrain.GetSummaryAsync`) ; un personnage qui ne répond pas laisse niveau 0 et
  classe vide, la cible reste choisie.
- **Effacement** :
  - la cible quitte la room : chaque joueur qui la ciblait reçoit `RpgTarget(0)` ;
  - pendant la grâce de 60 s l'avatar reste dans la room, donc la cible aussi ;
  - le joueur qui cible quitte la room : sa cible est oubliée (rien n'est envoyé).
- **Ce qui n'y est pas** : pas de portée, pas de visibilité pour les autres, pas de rafraîchissement
  quand la cible monte de niveau (M1 apportera les barres de vie et leurs mises à jour).
- La cible est de l'état runtime de la room : jamais écrite en base, perdue au déchargement de la
  room comme le reste de la présence dans la zone.

## 7. Réseau (contrat pour les deux dépôts)

Types sur le fil comme la spec F3a §6. Ids dans les régions `Vortex-specific` de `Headers.cs`
(`// RPG (spec §3.7)`). Paquets de **phase monde** : sans joueur en jeu, ignorés.

### 7.1 Client → serveur (`MessageEvent`)

| Id | Message | Champs, dans l'ordre |
|---|---|---|
| 9010 | `RpgTargetSelect` | objectId (entier, 0 = vider) |

### 7.2 Serveur → client (`MessageComposer`)

| Id | Message | Champs, dans l'ordre |
|---|---|---|
| 9008 | `RpgZoneInteractives` | count ; par objet : objectId, interaction (chaîne : `door`, `lever`, `sign`, `teleport`, `sit`, `use`) |
| 9009 | `RpgTarget` | objectId (0 = aucune cible) ; kind (chaîne : `player`, vide si aucune) ; name ; level ; classId |

### 7.3 Côté client (pour la session client)

- Un clic sur un objet de `RpgZoneInteractives` envoie `UseFurniture` (meuble au sol) comme
  aujourd'hui ; un clic sur tout autre objet du décor n'envoie rien et n'ouvre plus l'infobulle
  Habbo. Le curseur peut signaler un objet cliquable.
- Un clic sur un avatar envoie `RpgTargetSelect(objectId)` ; un clic dans le vide ou Échap envoie
  `RpgTargetSelect(0)`. Le HUD montre la cible de `RpgTarget` (nom, niveau, classe traduite) ; le
  cercle de cible suit l'objet tant que `RpgTarget` ne l'a pas vidé.

## 8. Erreurs et concurrence

- La room est la seule à écrire les cibles, dans son tour : un choix et un départ simultanés sont
  sérialisés.
- Le grain du personnage ciblé lu pendant le choix ne rappelle jamais la room : pas de cycle
  room → personnage → room.
- Un id d'objet hors room, d'un objet de décor ou d'un avatar parti vide la cible et le dit
  (`RpgTarget(0)`).

## 9. Observabilité

Refus du décor figé : log debug (joueur, objet). Choix de cible : rien de plus que le paquet ; un
grain de personnage qui ne répond pas : avertissement journalisé.

## 10. Tests

- Décor figé : un meuble à plusieurs états sans interaction ne bouge pas, même cliqué par le
  propriétaire ; une porte s'ouvre ; un objet mural ne bouge pas ; hors zone, le chemin Habbo reste.
- Objets cliquables : la liste part avec l'entrée, avec les bons ids et les bonnes sortes ; une zone
  sans interaction envoie une liste vide.
- Cible : choisir un joueur répond son nom, son niveau, sa classe ; soi-même ; 0 vide ; un id
  invalide vide ; la cible qui part vide la cible de ceux qui la visaient ; le joueur qui part est
  oublié ; un personnage qui ne répond pas laisse la cible choisie.
- Fil : parser de `RpgTargetSelect`, serializers des deux sortants ; handler sans joueur ne fait rien.

## 11. Critères d'acceptation

- `driver.mjs travel` vérifie la liste des objets cliquables à l'entrée de `proof_a` et qu'un objet
  sans interaction ne change pas d'état au clic.
- `driver.mjs target` : deux personnages dans la même zone ; A cible B (nom, niveau, classe) ; B
  quitte la zone ; A reçoit `RpgTarget(0)`.
- La porte de qualité est verte.
