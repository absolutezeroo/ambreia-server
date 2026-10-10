# Bilan — Z1 « Règles et déclencheurs des zones »

> Plan : `docs/superpowers/plans/2026-10-10-zone-rules-z1.md` · Spec :
> `docs/superpowers/specs/2026-10-10-zone-rules-z1-design.md` · 2026-10-10

## Avant / après

| Élément de l'éditeur | Avant | Après |
|---|---|---|
| Conditions `PlayerLevel`, `HasItem` | toujours fausses | répondues par le module personnages, depuis les faits de la zone |
| Action `SpawnGroup` | ignorée | réveille un groupe en sommeil : une vague, sans réapparition |
| Déclencheurs `MonsterKilled`, `PlayerLevel`, `ItemOwned` | jamais lancés | lancés (point ou monstre ; niveau franchi ; objet en hausse) |
| Point d'apparition : `min`, `group`, `conditions`, `aoiActivation` | ignorés | effectif tiré dans min..max, sommeil, conditions, éveil par proximité |
| Niveau conseillé (zone, aire) | ignoré | bulle `zone.level` sous le niveau, une fois par lieu et par visite |
| Validateur « sans effet » | liste écrite à la main | d'après ce que les modules déclarent ; seuls quêtes et factions restent sans effet |

**Extension :** une condition, une action, une partie des faits d'un joueur ou un type de
déclencheur se déclarent par leur module (`AddZoneCondition`, `AddZoneAction`,
`AddZoneFactsSource`, `AddZoneTrigger`). Un test branche une partie « quête » inconnue des rooms,
qui fait partir son `QuestState` et ferme une aire : le futur module de quêtes n'aura pas à
toucher aux rooms ni au validateur.

## Tâches et relectures

| | Commits |
|---|---|
| T1 règles déclarées, registre | 05ec35748 |
| T2 faits du joueur | 90c3c9634 |
| T3 conditions niveau / objet | d742c06fa |
| Relecture T1–2 | f28b0a0a4 — les faits deviennent des parties déclarées par leur module (fermés : critique de modularité) ; envoi sans lecture ni exception ; lus avant le pas d'arrivée ; séquence contre l'arrivée en retard |
| T4 déclencheurs niveau / objet | a8032ab53 |
| Relecture T3–4 | 19680563e — niveau transmis après une réactivation du personnage ; extension prouvée par une partie « quête » ; séquence monotone. Spec et plan des tâches 8 et 9 révisés |
| T5 monstre tué | d7ca18489 |
| T6 min..max, sommeil, vagues | a783169db |
| Relecture T5–6 | 36862bcf9 — vague due tant que la place manque ; renforts dans la vague ; file des signaux |
| T7 conditions, proximité | f93921cb7 |
| T8 niveau conseillé | e7da87b4b |
| T9 validateur | 4748a5bc1 |
| Relecture T7–8 | 0bb0e8a08 — combattants gardés relâchés ; réveil refusé sous conditions fausses ; poursuivant gardé ; joueur mort ignoré |
| T10 preuve, docs | ce commit |

Chaque paire a été relue par `grain-rules-reviewer` et `modularity-reviewer`.

## Preuve en direct

`driver.mjs rules` sur une seconde instance (base `vortex_schema_check`) : vault refusé
(`zone.restricted`), loup tué → embuscade de deux loups en (5, 10)/(6, 10), niveau suivant →
« rules.level », éclat d'ambre → « rules.shard », vault ouvert (`zone.area`) ; relancé avec le mage
déjà monté : vert. `driver.mjs combat` vert (non-régression des monstres).

## Mineures reportées

- Pièces des rooms (`IRoomConditionEvaluator`, `RoomRules`) publiques sans être un point d'extension.
- Un import qui renomme un objet ne pousse pas les faits (HasItem périmé jusqu'au prochain changement).
- Pas de test du relais par la présence ; pas de test d'attache qui échoue.
- Relais des faits non `[AlwaysInterleave]` : un changement pendant une téléportation entre zones,
  avant la lecture de la zone d'arrivée, perd son déclencheur (la spec veut « franchir sur place »).
- Une chaîne « objet obtenu → donner un objet » échappe aux limites D15 à travers le personnage
  (latente : aucune action ne donne d'objet aujourd'hui).
- Vérifications du validateur par règle (bornes de niveau, objets, cibles) écrites dans le validateur ;
  un crochet par règle quand un deuxième module en voudra.
- Vocabulaire `ZoneTriggerType` / `ZoneAction` fermé dans Primitives (format du document).
- Coût de la proximité (points × joueurs × cases, une fois par seconde) ; monstres gardés au-dessus du
  nouveau tirage ; une condition qui lève journalise chaque seconde ; une étape peut montrer plusieurs
  bulles.
