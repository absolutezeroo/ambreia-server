# Bilan — M1c « Monstres avancés »

Plan `2026-10-10-rpg-monsters-m1c.md`, spec `2026-10-10-rpg-monsters-m1c-design.md`. Exécuté le
2026-10-10, sur `main`, de 0f00b0b97 à 972f2cc58 : 10 tâches, 5 commits de relecture (tâches 1–2,
3–4, 5–6, 7–8, 9–10) et la relecture finale.

## Avant / après

| | Avant | Après |
|---|---|---|
| Comportements de monstre | `melee` | `melee`, `ranged`, `caster` sur la base `MonsterBehavior` |
| Lanceurs de compétences | joueurs | joueurs et monstres (une seule machine, `CastRequest` par id d'objet) |
| Contenu | 12 compétences, 7 statuts, 2 monstres | 18 compétences, 9 statuts, 6 monstres (dont un élite et un boss à 3 phases) |
| Paquets sortants RPG | jusqu'à 9024 | + 9025 `RpgTelegraphs`, 9026 `RpgMonsterYell`, `rank` en fin de 9009, `immune`, `RemainingMs` −1 |
| Tests Monsters / Skills / Combat | 21 / 147 / 74 | 65 / 167 / 92 |
| Preuve en direct | `combat`, `skills` | + `boss` (compte `run-skill-d@`, arène `proof_boss`) |

Aucune table, aucune colonne : pas de SQL à appliquer.

## Preuves

- Porte complète pas à pas (au premier plan, `Vortex.Main` et Hosting.Tests en `bin/alt`) : toutes les
  étapes vertes après la relecture finale.
- Instance alt (:40010/:8090, base `vortex_schema_check`) : `boss` (deux passes de suite, puis après la
  relecture finale), `skills`, `combat`, `smoke` OK.
- Client (`vortex-rpg-client-dd`, branche `rpg-character`, non commité) : télégraphes, cris, rang,
  `Immunisé`, vus en direct contre `main`.

## Ce que les relectures ont corrigé

- Un comportement déclaré normalement était refusé à l'import (et donc au démarrage) ; un cri sans
  texte n'était pas signalé.
- La zone d'un sort de monstre touchait un joueur sur case sûre ; une ligne diagonale large était
  trouée ; le « sans mana » n'était fixé par aucun test.
- Un statut de phase réappliqué par une compétence devenait minuté ; une forme de télégraphe inconnue
  devenait un cône.
- Le tireur ne visait pas en arrivant par sa propre marche ; un lanceur posté hors de portée restait
  inactif.
- La preuve `boss` dérivait de niveau à chaque passe ; le gardien perdait sa rage en phase 3.
- Un télégraphe commencé et annulé dans le même tick arrivait au client après sa fin ; un monstre
  qui incantait ne rentrait pas quand tous fuyaient au camp.

## Points mineurs reportés

Tous dans le registre `.superpowers/sdd/2026-10-10-rpg-monsters-m1c/progress.md` (lignes
`minor (deferred)`). Les principaux : renforts orphelins sans plafond de zone, renforts imbriqués non
renvoyés, rayons hérités du point d'apparition du boss ; phase et remise à zéro dans le même tick ;
`BossReset` compté à chaque retour ; pas de journal pour une phase ; compétence de monstre sans
recharge lancée à chaque tick ; immunités de boss configurées qui s'ajoutent aux défauts ; aucun test
automatique qui branche l'IA des monstres sur le vrai `CastSystem`.

## Suite

Jalon M1 complet. Prochain : M2 (loot) ou le choix du propriétaire.
