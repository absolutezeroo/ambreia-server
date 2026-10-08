# Vortex MMORPG — Master Design Specification

> **Remplacée** par `2026-10-06-vortex-rpg-design.md` (spec unifiée). Ce document reste comme source ; en cas de conflit, la spec unifiée prévaut.

> **Type**: Superpowers-style design specification
> **Version**: 0.1.0
> **Date**: 2026-10-06
> **Status**: Draft for design validation
> **Project**: Vortex Cloud / MMORPG conversion
> **Primary stack**: C# / .NET, Orleans, SuperSocket, existing Habbo-style client and emulator
> **Purpose**: Source of truth for product behavior and technical boundaries before implementation plans are written.

---

## 0. How to use this specification

This document is a **design specification**, not an implementation plan.

It defines:

- what the MMORPG must do;
- the system boundaries;
- the authoritative server rules;
- the expected UI behavior;
- the runtime and persistence model;
- the important edge cases;
- the acceptance criteria;
- the unresolved design questions.

It deliberately does **not** define exact source file paths, class names, migrations, commits, or task-by-task implementation steps. Those belong in separate implementation plans after each subsystem design is approved.

### 0.1 Normative language

The words below are intentional:

- **MUST**: required for correctness.
- **MUST NOT**: prohibited.
- **SHOULD**: recommended unless a documented reason exists.
- **SHOULD NOT**: avoid unless justified.
- **MAY**: optional.

### 0.2 Decision states

Every major design choice should be treated as one of:

- **APPROVED** — already decided.
- **PROPOSED** — recommended design, still changeable.
- **OPEN** — explicit decision still required.

### 0.3 Scope rule

This is the **master specification**. Before implementing a large subsystem, create a child design spec for that subsystem if the work cannot be reviewed comfortably as one isolated feature.

Recommended child specs:

```text
docs/superpowers/specs/
├── 2026-10-06-world-aoi-design.md
├── 2026-10-06-combat-design.md
├── 2026-10-06-items-inventory-equipment-design.md
├── 2026-10-06-quests-achievements-design.md
├── 2026-10-06-guilds-parties-social-design.md
├── 2026-10-06-economy-market-design.md
├── 2026-10-06-professions-crafting-design.md
├── 2026-10-06-dungeons-pvp-design.md
└── 2026-10-06-admin-observability-design.md
```

The master spec remains the cross-system source of truth.

---

# 1. Product vision

## 1.1 Core concept — APPROVED

Vortex MMORPG reuses the existing Habbo-style client and emulator as a technical foundation, while removing systems that do not serve the MMORPG.

The project is **not** intended to remain a standard Habbo hotel with RPG commands added on top.

The target is a real social MMORPG built around:

- isometric rooms/world rendering;
- avatars and social presence;
- persistent characters;
- combat;
- monsters;
- classes and skills;
- equipment and loot;
- quests;
- professions;
- crafting;
- groups;
- guilds;
- dungeons;
- economy;
- achievements;
- reputation;
- PvP;
- exploration;
- world events.

## 1.2 Product pillars

### P1 — Social MMORPG

The social layer is a first-class game system, not a lobby around combat.

Players SHOULD naturally meet in:

- city hubs;
- marketplaces;
- guild spaces;
- profession areas;
- event zones;
- world-boss locations;
- public exploration zones.

### P2 — Continuous world feel

The main world SHOULD feel like one connected world rather than a menu of disconnected rooms.

### P3 — Readable isometric combat

Combat MUST remain readable with a fixed/isometric camera and MUST NOT rely on FPS-style precision.

### P4 — Server authority

The server MUST be authoritative for all gameplay-relevant state.

### P5 — Scalable simulation

Inactive parts of the world MUST NOT consume the same CPU/network budget as active player areas.

### P6 — Reusable content pipeline

Monsters, items, quests, professions and zones MUST be data-driven enough to produce content without adding bespoke server code for every asset.

---

# 2. Existing foundation and constraints

## 2.1 Existing technology — APPROVED

The project currently builds on:

- C# / .NET;
- Orleans for distributed state/services;
- SuperSocket for networking;
- an existing Habbo-style client;
- an existing emulator/server foundation.

The project SHOULD evolve the existing architecture instead of rewriting the complete backend in another language without a measured technical need.

## 2.2 Existing client capabilities

The old Habbo-like system has already demonstrated high avatar density in a single room.

This is a useful baseline, but MMORPG performance MUST be benchmarked separately with:

- movement;
- combat;
- monsters;
- AI;
- VFX;
- equipment;
- damage numbers;
- AOI updates;
- projectiles;
- status effects;
- chat;
- loot;
- pathfinding.

## 2.3 Rendering style

The visual direction is:

- current Habbo-inspired isometric presentation;
- readable low-poly / pixel-friendly assets;
- lightweight GLB where 3D assets are used;
- compact, consistent HUD;
- restrained VFX designed for isometric readability.

The game MUST NOT require cinematic 3D effects to communicate gameplay.

---

# 3. High-level architecture

## 3.1 Architectural split — APPROVED

Use a hybrid architecture:

```text
Persistent / business domain
        |
        |  Orleans / services / repositories
        v
Character, Account, Guild, Economy, Quests, Progression
        |
        | runtime projection
        v
Real-time simulation
        |
        | ECS per active simulation partition
        v
Movement, Combat, AI, AOI, Projectiles, Buffs, Spawns
```

### Persistent/business layer

Good candidates:

- account;
- character profile;
- inventory persistence;
- quest progression;
- achievements;
- reputation;
- professions;
- guild membership;
- party metadata;
- marketplace;
- mail;
- economy;
- unlocks;
- account-wide collections.

### ECS/runtime layer

Good candidates:

- active players;
- monsters;
- NPC runtime entities;
- projectiles;
- ground effects;
- temporary loot;
- resource nodes;
- active buffs/debuffs;
- combat state;
- threat;
- movement;
- local AI;
- AOI membership.

## 3.2 ECS boundary — APPROVED

The entire application MUST NOT be converted to ECS.

ECS is for high-frequency runtime simulation.

A marketplace listing, guild rank or mail message MUST NOT exist as an ECS entity merely for architectural consistency.

## 3.3 Orleans boundary — APPROVED

Fine-grained high-frequency monsters SHOULD NOT each be independent Orleans grains.

Preferred ownership:

```text
World / Region / Zone owner
└── owns runtime ECS world or simulation partition
    ├── PlayerEntity
    ├── MonsterEntity
    ├── ProjectileEntity
    ├── ResourceEntity
    └── GroundEffectEntity
```

Cross-zone/business state remains outside the high-frequency tick.

---

# 4. Main world model

## 4.1 Large continuous map — APPROVED

The main world uses a **large map partitioned into logical regions, zones and AOI cells**.

It SHOULD NOT require a visible room loading transition for every small area.

Concept:

```text
World
├── Region: Ambre
│   ├── Zone: Capital
│   ├── Zone: Forest
│   ├── Zone: Mines
│   ├── Zone: Ruins
│   └── Zone: Marsh
└── Region: ...
```

Each zone is internally subdivided into spatial cells.

## 4.2 Private content — APPROVED

The following SHOULD normally use separate instances:

- guild halls;
- housing/private spaces;
- dungeons;
- raids;
- PvP arenas;
- tutorial instances where isolation is useful.

## 4.3 Cell states

Each simulation cell SHOULD support:

### Active

At least one relevant player is present or nearby.

Full simulation allowed.

### Warm / Sleeping

No player currently requires full simulation, but the cell remains resident for a configurable grace period.

Expensive work is reduced or paused.

### Unloaded

No relevant player.

No real-time entity simulation.

Only static definitions/persistent timers that explicitly require wall-clock progression remain relevant.

## 4.4 Hysteresis

AOI activation/deactivation MUST use hysteresis/grace periods to prevent constant spawn/despawn churn at cell boundaries.

Example policy:

```text
activate radius     = R
deactivate radius   = R + margin
sleep delay         = configurable
unload delay        = configurable
```

Exact values are OPEN and require benchmark data.

---

# 5. Area of Interest (AOI)

## 5.1 Purpose

AOI determines which world state is relevant to each connected client and which runtime areas need active simulation.

## 5.2 Visibility

A client MUST receive only entities/events relevant to its AOI except explicitly global events.

Examples of AOI-relevant data:

- nearby players;
- nearby monsters;
- nearby NPCs;
- resource nodes;
- projectiles;
- ground effects;
- nearby loot;
- relevant local emotes;
- local combat events.

## 5.3 AOI updates

Movement across AOI boundaries MUST produce differential updates:

```text
entered set
stayed set
left set
```

The server SHOULD avoid resending the complete visible world every update.

## 5.4 Global exceptions

Some data MAY bypass AOI:

- world event announcement;
- guild message;
- party status;
- matchmaking notification;
- system notification;
- global chat if enabled.

## 5.5 Acceptance criteria

- A player must not receive remote monster updates from an unrelated far-away zone.
- Crossing a cell boundary must not duplicate entities.
- Leaving and re-entering quickly must not create duplicate monster instances.
- AOI membership must remain correct after teleportation.
- AOI must handle disconnect/reconnect safely.

---

# 6. Character system

## 6.1 Character identity

Each character MUST have:

```text
CharacterId
AccountId
Name
Appearance
Level
Experience
ClassId
SpecializationId?
EquippedTitleId?
GuildId?
CreationDate
LastLogin
PlayTime
CurrentWorldLocation
```

## 6.2 Character lifecycle

Required states:

```text
Offline
Loading
Online
ChangingZone
InInstance
DisconnectedGrace
Saving
```

The server MUST prevent two simultaneous authoritative sessions for the same character unless explicitly supported.

## 6.3 Character creation

OPEN decisions:

- number of characters per account;
- name rules;
- class chosen at creation vs later;
- appearance restrictions;
- starting region;
- starting items.

## 6.4 Character UI tabs — APPROVED

Character screen includes:

```text
Equipment
Stats
Titles
Reputation
```

---

# 7. Stats system

## 7.1 Stat categories

### Primary stats — PROPOSED

Candidate primary attributes:

- Strength;
- Dexterity;
- Intelligence;
- Constitution;
- Spirit.

Exact list remains OPEN.

### Derived combat stats

Potential derived stats:

- MaxHealth;
- MaxResource;
- PhysicalPower;
- MagicalPower;
- Armor;
- MagicResistance;
- CriticalChance;
- CriticalDamage;
- Accuracy;
- Dodge;
- Block;
- AttackSpeed;
- CastSpeed;
- Cooldown modifiers;
- MovementSpeed;
- ArmorPenetration;
- ResistancePenetration;
- HealingPower;
- DamageReduction.

## 7.2 Stat composition

Stats MUST be computed from explicit sources.

Example:

```text
FinalStrength =
    BaseStrength
  + LevelGrowth
  + Equipment
  + PassiveSkills
  + Specialization
  + Buffs
  + TemporaryEffects
```

The system SHOULD allow the UI/debug tools to expose a breakdown by source.

## 7.3 Rules

- The client MUST NOT submit authoritative final stats.
- Stat calculations MUST be deterministic for the same authoritative inputs.
- Temporary effects MUST be distinguishable from persistent progression.
- Negative or invalid values MUST be clamped/validated according to stat rules.

---

# 8. Class and specialization system

## 8.1 Class

A class defines:

- resource type;
- allowed equipment;
- core active skills;
- base stat profile;
- progression unlocks;
- available specializations.

## 8.2 Specialization tab — APPROVED

The Skills UI includes a **Specialization** tab.

A specialization MAY:

- unlock new skills;
- modify existing skills;
- grant passives;
- change resource interactions;
- define combat role;
- unlock an ultimate.

## 8.3 Open decisions

- number of classes at launch;
- number of specializations per class;
- respec cost;
- respec location requirements;
- saved loadouts;
- whether specialization can change in combat/instance.

---

# 9. Skill system

## 9.1 Skill UI tabs — APPROVED

```text
Active
Passive
Specialization
Shortcuts
```

## 9.2 Skill definition

Each active skill SHOULD define:

```text
SkillId
Name
Icon
ClassRestrictions
SpecializationRestrictions
ResourceCost
Cooldown
GlobalCooldownBehavior
CastTime
ChannelTime?
Range
TargetingMode
AreaShape?
AreaSize?
ProjectileDefinition?
Effects[]
AnimationId
VfxId
SfxId
InterruptRules
MovementRules
```

## 9.3 Targeting modes

Supported modes SHOULD include:

- self;
- selected unit;
- friendly unit;
- hostile unit;
- ground position;
- direction;
- cone;
- line;
- radius around caster;
- radius around target.

## 9.4 Server validation

Every skill activation MUST validate:

- actor alive;
- correct skill ownership;
- cooldown;
- resource;
- target legality;
- range;
- combat restrictions;
- control effects;
- line/path rule if required;
- instance/PvP rule;
- item/equipment requirement.

The client MUST NOT determine success.

## 9.5 Passive skills

Passive skills MAY modify:

- stats;
- skill coefficients;
- proc behavior;
- resource generation;
- cooldowns;
- targeting;
- status effects;
- conditional combat rules.

Passives MUST NOT require fake polling if an event-driven rule is sufficient.

---

# 10. Shortcut / action bar system

## 10.1 Purpose

Allows players to bind actions to quick slots.

Bindable categories MAY include:

- active skills;
- consumables;
- mounts;
- emotes;
- utility items.

## 10.2 Persistence

Bindings SHOULD be stored per character.

Possible future extension:

- per specialization loadout.

## 10.3 Rules

A shortcut references an action identifier; it MUST NOT duplicate authoritative item or skill state.

Deleting/replacing an item MUST safely invalidate broken bindings.

---

# 11. Combat model

## 11.1 Combat style — PROPOSED

Recommended model:

- real-time;
- targetable units;
- movement during combat;
- limited active ability bar;
- ground telegraphs;
- positional avoidance;
- isometric readability;
- no FPS-style precision requirement.

Final combat feel remains a design validation item.

## 11.2 Combat state

Runtime state MAY contain:

```text
CurrentTarget
InCombat
CombatStartTime
LastHostileAction
ResourceState
Cooldowns
CastState
ChannelState
ThreatState
StatusEffects
```

## 11.3 Core outcomes

Combat engine MUST support at least:

- damage;
- healing;
- critical hit;
- miss if used;
- dodge;
- block if used;
- immunity;
- absorb/shield;
- interrupt;
- death.

Optional mechanics:

- parry;
- stagger;
- knockback;
- knock-up.

## 11.4 Floating combat text

The client MAY display:

```text
-42
-95 CRIT
+31
BLOCK
DODGE
IMMUNE
```

The combat text is presentation only.

## 11.5 Combat exit

Combat MUST have explicit exit rules.

Examples:

- no hostile activity for N seconds;
- all hostile targets dead;
- encounter reset.

Exact timeout is OPEN.

---

# 12. Casts, channels and interrupts

## 12.1 Cast types

Skills MAY be:

- instant;
- cast-time;
- channelled;
- movement-compatible;
- stationary-required.

## 12.2 Interrupt

The server MUST validate interrupts.

Interruptible casts SHOULD expose:

- skill;
- remaining duration;
- interrupt immunity if applicable.

## 12.3 Cancellation conditions

Potential conditions:

- movement;
- stun;
- death;
- invalid target;
- range loss;
- explicit cancel;
- zone transition.

Each skill definition MUST define applicable rules rather than relying on global assumptions.

---

# 13. Status effects

## 13.1 Types

Examples:

- buff;
- debuff;
- damage over time;
- heal over time;
- stun;
- silence;
- root;
- slow;
- haste;
- shield;
- immunity;
- vulnerability.

## 13.2 Data model

Each runtime effect SHOULD know:

```text
EffectId
SourceEntityId
TargetEntityId
StartTime
EndTime
Stacks
Magnitude
Dispellable
PersistencePolicy
```

## 13.3 Stacking policies

Definition MUST specify one:

- no stacking;
- refresh duration;
- add stack;
- replace if stronger;
- independent instances.

## 13.4 Cross-zone policy

Effects MUST define whether they:

- disappear on zone transfer;
- persist through transfer;
- persist through logout;
- persist through death.

---

# 14. Threat and aggro

## 14.1 Purpose

Group PvE requires deterministic enemy target selection.

## 14.2 Threat sources

Potential sources:

- damage;
- healing;
- explicit taunt;
- buffs;
- scripted encounter rules.

## 14.3 Requirements

- Threat is server authoritative.
- Threat SHOULD be encounter-local.
- Dead or invalid actors MUST be removed/ignored.
- Boss scripts MAY override normal target selection.
- Taunt MUST define precise behavior.

---

# 15. Monster system

## 15.1 Static definition

A monster template SHOULD include:

```text
MonsterDefinitionId
Name
ModelAsset
Level
Faction
Tags[]
BaseStats
MovementProfile
AggroProfile
LeashRules
SkillLoadout
AiProfile
LootTableId
ExperienceReward
SpawnRules
VfxProfile?
SfxProfile?
```

## 15.2 Runtime entity

Runtime ECS entity SHOULD contain only active state needed for simulation.

Typical components:

```text
Position
Movement
Health
CombatStats
Target
Aggro
Threat
AIState
Cooldowns
StatusEffects
SpawnOrigin
MonsterTag
```

## 15.3 AI states

Baseline:

```text
Idle
Patrol
Alert
Chase
Attack
Cast
Evade
ReturnHome
Dead
```

## 15.4 Monster archetypes

Content SHOULD support reusable AI archetypes:

- melee;
- ranged;
- caster;
- tank;
- support;
- assassin/mobile;
- elite;
- boss.

---

# 16. Spawn system

## 16.1 Spawn definitions

Spawns SHOULD be declarative.

Example fields:

```text
SpawnId
MonsterDefinitionId
Position/Area
MaxAlive
RespawnPolicy
ActivationPolicy
PatrolProfile?
Conditions?
```

## 16.2 AOI integration

If no player is relevant to a spawn region:

- monsters MAY remain uninstantiated;
- AI MUST NOT tick unnecessarily;
- pathfinding MUST NOT run;
- network updates MUST NOT be generated.

## 16.3 Respawn

Respawn policy MAY be:

- fixed delay;
- randomized range;
- wave;
- event-driven;
- encounter reset;
- daily/event schedule.

## 16.4 Edge cases

Must handle:

- player camping spawn;
- zone sleeping during respawn timer;
- server restart;
- duplicated spawn activation;
- boss spawn exclusivity.

---

# 17. Boss encounters

## 17.1 Encounter controller

Bosses SHOULD use encounter logic separate from generic monster AI when needed.

## 17.2 Phases

Phases MAY trigger by:

- health threshold;
- elapsed time;
- add death;
- player action;
- scripted objective.

## 17.3 Telegraphs

Dangerous attacks SHOULD use clear isometric ground telegraphs.

Telegraph definition SHOULD separate:

- gameplay area;
- warning duration;
- VFX;
- actual hit resolution.

## 17.4 Reset

Encounter reset MUST clean:

- boss state;
- adds;
- temporary hazards;
- threat;
- encounter-only buffs;
- loot eligibility if encounter not completed.

---

# 18. Death and resurrection

## 18.1 Player death

Required behavior:

- mark player dead;
- disable illegal actions;
- preserve party visibility;
- expose resurrection options.

## 18.2 Resurrection sources

Possible:

- checkpoint;
- healer skill;
- consumable;
- scripted encounter resurrection.

## 18.3 Death penalty

OPEN.

Options include:

- none;
- durability damage;
- temporary debuff;
- currency fee.

Heavy loss SHOULD be avoided unless intentionally part of the game identity.

## 18.4 Monster death

On monster death:

- stop AI;
- resolve kill credit;
- resolve quest credit;
- resolve XP;
- resolve loot;
- schedule despawn/respawn;
- publish achievement/progression events.

This pipeline MUST be idempotent.

---

# 19. Items

## 19.1 Item definition

All RPG items SHOULD derive from data definitions rather than class-per-item.

Common fields:

```text
ItemDefinitionId
Name
Description
Icon
Category
Rarity
MaxStack
VendorBuyPrice?
VendorSellPrice?
Tradable
BindPolicy
LevelRequirement?
ClassRestrictions?
UniqueRules?
Tags[]
```

## 19.2 Item categories

Baseline:

- equipment;
- consumable;
- material;
- quest;
- currency/token;
- utility;
- cosmetic;
- miscellaneous.

---

# 20. Inventory

## 20.1 Requirements

Inventory MUST support:

- slots;
- stackable items;
- split stack;
- merge stack;
- move;
- sort;
- filter;
- search;
- use;
- equip;
- sell;
- trade;
- destroy if allowed;
- lock/favorite if implemented.

## 20.2 Authoritative operations

Every inventory mutation MUST be atomic and server validated.

The client MUST NOT directly mutate quantities.

## 20.3 Full inventory policy

OPEN.

Potential fallback order:

1. merge existing stack;
2. free inventory slot;
3. mailbox/recovery storage for protected rewards;
4. ground loot remains available;
5. reject operation if appropriate.

Quest/paid/system rewards MUST NOT silently disappear.

## 20.4 Unique item rules

Unique constraints MAY apply:

- per stack;
- per character;
- per account;
- equipped-only.

---

# 21. Equipment

## 21.1 Proposed slots

```text
Head
Chest
Hands
Legs
Feet
MainHand
OffHand
Neck
Ring1
Ring2
Cape?
Relic?
```

Final slot list is OPEN.

## 21.2 Equipment item data

Equipment MAY define:

- armor/weapon type;
- base stats;
- bonus stats;
- requirements;
- set;
- sockets;
- upgrade level;
- durability;
- cosmetic appearance;
- bind state.

## 21.3 Validation

Equip operation MUST validate:

- item ownership;
- item still exists;
- level;
- class;
- slot;
- two-hand/off-hand conflicts;
- combat restrictions if any.

## 21.4 Comparison UI

UI SHOULD show differences against currently equipped item.

---

# 22. Item rarity

## 22.1 Proposed ladder

```text
Common
Uncommon
Rare
Epic
Legendary
Mythic
```

Final naming is OPEN.

Rarity MUST NOT be the only determinant of power.

---

# 23. Loot

## 23.1 Sources

- monster;
- boss;
- chest;
- quest;
- event;
- profession;
- world resource.

## 23.2 Loot tables

Loot tables MUST be data-driven.

Entries MAY define:

```text
Item
Weight/Chance
MinQuantity
MaxQuantity
Conditions
LevelScaling?
DifficultyScaling?
```

## 23.3 Group loot

OPEN modes:

- personal loot;
- shared loot;
- need/greed/pass;
- master/leader loot is not recommended unless intentionally required.

## 23.4 Eligibility

Loot eligibility SHOULD derive from contribution/encounter participation rules, not from client claims.

---

# 24. Quests

## 24.1 Quest UI tabs — APPROVED

```text
Main
Side
Daily
Completed
```

## 24.2 Quest definition

A quest SHOULD define:

```text
QuestId
Title
Description
Category
LevelRange
Prerequisites
StartConditions
Objectives[]
Rewards[]
NextQuestId?
RepeatPolicy
ResetPolicy?
AbandonPolicy
SharePolicy
```

## 24.3 Objective types

Baseline:

- kill;
- collect;
- talk;
- explore;
- interact;
- craft;
- use item;
- complete dungeon;
- kill boss;
- defend;
- escort if supported.

## 24.4 Progress

Quest progression MUST be event-driven where practical.

Examples:

```text
MonsterKilled
ItemAcquired
NpcInteracted
ZoneEntered
CraftCompleted
DungeonCompleted
```

## 24.5 Daily quests

Daily quests MUST use server time and a clearly defined reset boundary.

Reset timezone/policy is OPEN.

---

# 25. Dialogue system

## 25.1 Requirements

Dialogues SHOULD support:

- speaker;
- text;
- multiple choices;
- conditional choices;
- quest hooks;
- reputation checks;
- item checks;
- branches;
- exit;
- service opening.

## 25.2 Server authority

Dialogue choices that cause gameplay state changes MUST be validated server-side.

---

# 26. NPC services

One NPC MAY expose multiple services:

- dialogue;
- quest;
- vendor;
- reputation vendor;
- bank;
- profession trainer;
- travel;
- guild service.

Services SHOULD be composable rather than implemented through unique NPC subclasses.

---

# 27. Reputation

## 27.1 Character UI tab — APPROVED

Character screen includes Reputation.

## 27.2 Reputation model

Each faction SHOULD define thresholds.

Proposed ranks:

```text
Hostile
Unfriendly
Neutral
Friendly
Honored
Revered
Exalted
```

Names remain OPEN.

## 27.3 Reputation sources

- quests;
- bosses;
- faction activities;
- world events;
- contracts;
- dungeons.

## 27.4 Rewards

May unlock:

- vendors;
- items;
- recipes;
- titles;
- cosmetics;
- travel;
- discounts;
- quests.

---

# 28. Titles

## 28.1 Character Titles tab — APPROVED

Players can browse locked/unlocked titles and equip one active title.

## 28.2 Title sources

- achievements;
- quests;
- PvP;
- professions;
- reputation;
- exploration;
- events;
- secret discoveries.

## 28.3 Data

```text
TitleId
Name
Description
Source
Rarity?
HiddenUntilUnlocked
```

Only unlocked titles MAY be equipped.

---

# 29. Achievements

## 29.1 Achievement structure

An achievement MAY have:

- binary completion;
- cumulative counter;
- tiered milestones;
- hidden state;
- account-wide or character-wide scope.

## 29.2 Categories

Recommended:

- combat;
- exploration;
- quests;
- dungeons;
- professions;
- collection;
- social;
- guild;
- economy;
- PvP;
- events;
- secrets.

## 29.3 Rewards

Potential rewards:

- achievement points;
- title;
- badge;
- cosmetic;
- item;
- currency;
- account unlock.

## 29.4 Idempotency

Completion MUST be idempotent.

A duplicated event MUST NOT grant rewards twice.

---

# 30. Collections

## 30.1 Achievements UI tab — APPROVED

Achievements area contains a **Collections** tab.

## 30.2 Collection types

Possible:

- discovered monsters;
- equipment appearances;
- pets;
- mounts;
- relics;
- recipes;
- books/lore;
- cosmetics;
- world discoveries.

## 30.3 Completion rewards

A collection MAY reward milestones:

```text
25%
50%
75%
100%
```

---

# 31. Badges

## 31.1 Achievements UI tab — APPROVED

Achievements area contains a **Badges** tab.

## 31.2 Behavior

Players MAY:

- unlock badges;
- inspect source;
- favorite badges;
- display selected badges on profile.

Max visible badge count is OPEN.

---

# 32. Achievement Titles

## 32.1 Achievements UI tab — APPROVED

Achievements area contains a **Titles** tab.

This is a filtered acquisition view of titles related to achievement/progression systems.

The project SHOULD avoid maintaining two independent title ownership systems.

---

# 33. Professions

## 33.1 Profession model

Possible professions:

- mining;
- woodcutting;
- fishing;
- blacksmithing;
- alchemy;
- cooking;
- tailoring;
- enchanting.

Launch list is OPEN.

## 33.2 Profession progression

Each profession MAY have:

```text
ProfessionLevel
ProfessionExperience
KnownRecipes
Unlocks
ToolRequirements
```

## 33.3 Profession limits

OPEN:

- all professions learnable;
- limited active professions;
- gathering/crafting split;
- respec behavior.

---

# 34. Resource gathering

## 34.1 Resource nodes

Resource nodes SHOULD use the same AOI/runtime activation principles as monsters.

Definition:

```text
ResourceNodeId
ResourceType
SpawnArea
RequiredProfession
RequiredLevel
GatherTime?
LootTable
RespawnPolicy
```

## 34.2 Contention model

OPEN:

- globally consumed node;
- personal node;
- short shared cooldown.

---

# 35. Crafting

## 35.1 Craft UI

Crafting SHOULD expose:

- recipe list;
- filters;
- locked recipes;
- required materials;
- owned materials;
- output preview;
- craft amount;
- station requirement.

## 35.2 Recipe definition

```text
RecipeId
Profession
RequiredLevel
Ingredients[]
Outputs[]
StationType?
CraftDuration?
Conditions?
```

## 35.3 Atomicity

Craft completion MUST atomically:

1. validate recipe;
2. validate materials;
3. consume materials;
4. create outputs;
5. award progression.

Failure MUST NOT consume only part of the ingredients.

---

# 36. NPC shop

## 36.1 Main tabs — APPROVED

```text
Buy
Sell
Buyback
```

## 36.2 Buy categories — APPROVED

```text
Equipment
Consumables
Materials
Miscellaneous
```

Additional contextual categories MAY exist.

## 36.3 Vendor inventory

Vendor offers MAY be gated by:

- faction;
- reputation;
- level;
- quest;
- event;
- profession;
- dungeon progress.

## 36.4 Buyback

Buyback SHOULD retain a limited number of recent vendor sales.

OPEN:

- number retained;
- session-only vs persistent;
- expiry.

---

# 37. Marketplace / Auction House

## 37.1 Tabs — APPROVED

```text
Buy
Sell
My Sales
History
```

## 37.2 Buy

Filters SHOULD include:

- search text;
- category;
- rarity;
- level;
- price;
- sort.

## 37.3 Sell

Selling MUST validate:

- ownership;
- tradability;
- bind state;
- stack quantity;
- allowed price range if enforced;
- listing fee.

## 37.4 My Sales

States:

```text
Active
Sold
Expired
Cancelled
```

## 37.5 History

History SHOULD show completed purchases/sales relevant to the player.

## 37.6 Economy protection

Marketplace operations MUST be transactional.

The server MUST prevent:

- double sale;
- duplicate item;
- double payout;
- stale listing purchase;
- race condition on last quantity.

---

# 38. Direct player trade

## 38.1 Trade flow

Suggested flow:

```text
Invite
→ Accept trade
→ Both add items/currency
→ Both lock offer
→ Final confirmation
→ Atomic exchange
```

## 38.2 Safety invariant

If either side changes:

- item;
- quantity;
- currency;

after confirmation, both confirmations MUST reset.

## 38.3 Atomicity

No partial trade is allowed.

Either the entire exchange commits or nothing changes.

---

# 39. Bank / storage

## 39.1 Storage types

Potential:

- character bank;
- account/shared bank;
- material storage;
- guild bank.

## 39.2 Features

- deposit;
- withdraw;
- stacking;
- sorting;
- filtering;
- searching.

Access MUST respect permissions and ownership.

---

# 40. Mail

## 40.1 Mail types

- player mail;
- system mail;
- marketplace delivery;
- reward mail;
- support/admin mail.

## 40.2 Attachments

Mail MAY contain:

- items;
- currency.

All attachment claims MUST be transactional/idempotent.

## 40.3 Expiration

Expiration policy is OPEN.

Protected system rewards SHOULD NOT disappear silently.

---

# 41. Party system

## 41.1 Core features

- invite;
- accept/decline;
- leave;
- kick;
- leader;
- promote leader;
- role;
- ready check;
- party chat.

## 41.2 Proposed roles

```text
Tank
Healer
Damage
```

## 41.3 Party UI

UI SHOULD show:

- character;
- level;
- role;
- health;
- primary combat resource if useful;
- dead/offline/out-of-range state;
- important dispellable debuffs where appropriate.

Party state is global to members and is not limited by local AOI.

---

# 42. Group Finder

## 42.1 Tabs — APPROVED

```text
Group Finder
My Registrations
```

## 42.2 Listing fields

Potential:

- activity;
- difficulty;
- roles needed;
- level requirement;
- item power requirement if used;
- language;
- note;
- leader;
- current members.

## 42.3 My Registrations

Must show:

- activity;
- requested role;
- status;
- elapsed wait;
- cancellation.

## 42.4 Constraints

A player MUST NOT be simultaneously in incompatible queued states.

Exact queue/listing model is OPEN:

- automated matchmaking;
- manual listings;
- both.

---

# 43. Dungeons

## 43.1 Instance model

Dungeons SHOULD run in isolated instances.

## 43.2 Definition

```text
DungeonId
Name
LevelRange
PartySize
DifficultyModes
Map/InstanceTemplate
Encounters[]
Objectives[]
LootTables
EntryRules
```

## 43.3 Proposed difficulties

```text
Normal
Heroic
Mythic
```

Names/number are OPEN.

## 43.4 Required edge cases

- party member disconnect;
- reconnect;
- leader leaves;
- player kicked;
- wipe;
- instance reset;
- dungeon completed while a member is temporarily offline;
- replacement member if supported.

---

# 44. Guilds

## 44.1 Guild features

- create;
- name;
- emblem;
- description;
- announcement;
- invite;
- application;
- ranks;
- permissions;
- member notes if desired;
- guild chat;
- guild progression;
- guild activities.

## 44.2 Rank permissions

Permission model SHOULD be explicit capabilities:

```text
InviteMembers
RemoveMembers
PromoteMembers
DemoteMembers
EditGuildInfo
ManageRanks
WithdrawGuildBank
DepositGuildBank
StartGuildEvent
ManageGuildHall
```

Avoid hard-coding privileges by rank name.

## 44.3 Audit log

Guild-sensitive actions SHOULD be logged.

---

# 45. Guild hall

## 45.1 Instance

Each guild MAY own a private instanced hall.

## 45.2 Potential rooms/services

- main hall;
- bank;
- trophies;
- crafting area;
- planning room;
- member area.

## 45.3 Progression

Guild progression MAY unlock:

- visual upgrades;
- rooms;
- services;
- convenience features.

Decorating rights MUST use guild permissions.

---

# 46. Social graph

## 46.1 Friends

Required:

- request;
- accept;
- decline;
- remove;
- online status according to privacy rules.

## 46.2 Block

Blocking SHOULD affect:

- whispers;
- friend requests;
- trade invites;
- party invites;
- other direct interactions as appropriate.

## 46.3 Inspect player

Possible actions:

- inspect;
- whisper;
- invite party;
- invite guild;
- trade;
- duel;
- add friend;
- block.

Each action MUST be independently permission-validated.

---

# 47. Chat

## 47.1 Channels

Potential:

- local;
- whisper;
- party;
- guild;
- trade;
- system;
- global if desired.

## 47.2 Moderation

Support:

- mute;
- block;
- rate limit;
- moderation logs;
- server announcements.

Global/trade chat SHOULD have stronger spam controls than local chat.

---

# 48. PvP

## 48.1 Possible modes

- duel;
- arena;
- battleground later.

## 48.2 PvP rules

PvP SHOULD have explicit rules for:

- skill scaling;
- consumables;
- gear normalization if any;
- death;
- resurrection;
- leaving;
- disconnect;
- rewards.

## 48.3 Matchmaking

If ranked PvP exists:

- MMR/rating;
- season;
- anti-AFK;
- abandonment penalties;
- reward integrity.

Final PvP scope is OPEN.

---

# 49. World events

## 49.1 Event examples

- invasion;
- defend location;
- collect resources;
- public boss;
- escort;
- timed event.

## 49.2 Participation

Rewards MUST use server-observed participation.

The client cannot claim contribution.

## 49.3 Scaling

Events MAY scale by:

- active player count;
- average level;
- predefined difficulty bands.

Scaling rules MUST avoid exploits where players intentionally manipulate presence.

---

# 50. World bosses

World bosses are public encounter controllers in the main world.

They require:

- spawn schedule/policy;
- public participation tracking;
- contribution;
- personal or controlled shared rewards;
- anti-tagging abuse;
- reset;
- high-density performance rules.

VFX limits SHOULD be more aggressive near large public encounters.

---

# 51. Map and exploration

## 51.1 Map UI

May display:

- player;
- party;
- known settlements;
- quests;
- dungeon entrances;
- travel points;
- world events;
- discovered points of interest.

## 51.2 Discovery

Areas MAY be undiscovered until:

- entered;
- interacted with;
- unlocked by quest.

Discovery can feed:

- exploration XP;
- achievements;
- collections.

---

# 52. Travel

Potential systems:

- waypoints;
- portals;
- transport NPCs;
- hearth/return item;
- dungeon teleport.

Travel restrictions MAY include:

- discovery;
- cost;
- cooldown;
- quest;
- reputation;
- combat state.

---

# 53. VFX

## 53.1 Direction

VFX MUST prioritize isometric readability.

Recommended:

- 2D/2.5D effects;
- small particle counts;
- ground ellipses aligned to the isometric plane;
- short effects;
- clear telegraphs;
- restrained glow.

## 53.2 Player settings

Players SHOULD be able to reduce:

- other-player skill VFX;
- ambient particles;
- damage text;
- non-critical effects.

Critical enemy telegraphs MUST remain visible even on low VFX settings.

---

# 54. SFX and positional audio

## 54.1 UI

UI SFX are non-spatial.

## 54.2 World

World SFX MAY use lightweight 2D/2.5D spatialization:

- left/right pan;
- distance attenuation;
- zone ambience.

## 54.3 Priority

Critical combat audio cues SHOULD not be drowned by ambient/social SFX.

---

# 55. 3D / GLB asset budget

## 55.1 Direction

GLB is acceptable for lightweight low-poly content.

Suggested starting targets, subject to measurement:

```text
normal monster : ~2k–5k triangles
elite          : ~5k–8k
boss           : ~8k–15k
textures       : 256 or 512 max in most cases
materials      : as few as practical
```

These are budgets, not hard limits.

## 55.2 Required measurement

Build tooling SHOULD report per asset:

- triangles;
- material count;
- texture memory;
- GLB size;
- bone count;
- animation count.

---

# 56. Asset/content pipeline

Content SHOULD separate:

```text
definition data
visual asset
animation set
VFX set
SFX set
localization
```

A monster balance tweak SHOULD NOT require editing its GLB.

A texture/model change SHOULD NOT require rewriting AI logic.

---

# 57. Notifications

Central notification service SHOULD support categories:

- informational;
- reward;
- social;
- warning;
- critical.

Examples:

- quest complete;
- achievement;
- level up;
- party invite;
- guild invite;
- item sold;
- mail;
- matchmaking ready.

Duplicate notifications SHOULD be coalesced where useful.

---

# 58. Tutorial / onboarding

The opening experience SHOULD teach systems progressively.

Recommended order:

```text
movement
→ interaction
→ first combat
→ skill
→ loot
→ equipment
→ quest
→ inventory
→ social/group later
```

The first session SHOULD NOT expose every major UI at once.

---

# 59. Settings

The game SHOULD expose:

### Audio
- master;
- music;
- effects;
- ambience.

### Graphics/presentation
- UI scale;
- VFX quality;
- other-player VFX;
- floating combat text;
- nameplates;
- shadows/optional rendering choices.

### Controls
- shortcut keys;
- interaction;
- targeting.

### Chat/social
- channel visibility;
- timestamps;
- filters.

---

# 60. Server authority and anti-cheat

## 60.1 Client trust model — APPROVED

The client is untrusted.

The client MAY request:

```text
Move toward position
Cast Skill X
Use Item Y
Buy Listing Z
Craft Recipe R
```

The server determines whether each request succeeds.

## 60.2 Server-owned decisions

The server MUST own:

- damage;
- healing;
- cooldown;
- resource costs;
- loot;
- XP;
- currency;
- inventory;
- equipment;
- quest credit;
- achievement credit;
- marketplace operations;
- crafting;
- reputation;
- position legality;
- PvP result.

## 60.3 Rate limiting

Sensitive requests MUST be rate-limited where appropriate.

---

# 61. Networking

## 61.1 Message design

Prefer domain messages over repurposing unrelated legacy packet semantics.

Examples:

```text
SkillCastRequest
SkillCastStarted
SkillCastResolved
CombatDamageEvent
StatusEffectApplied
QuestProgressUpdated
LootAvailable
InventoryChanged
EquipmentChanged
PartyUpdated
```

Actual naming is implementation-level.

## 61.2 Snapshot vs delta

High-frequency systems SHOULD favor deltas/events over complete state snapshots where safe.

Snapshots remain useful for:

- initial zone entry;
- resync;
- recovery;
- reconnect.

## 61.3 Ordering/idempotency

Financial/inventory operations MUST tolerate duplicate network delivery or retries without duplicating value.

---

# 62. Persistence

## 62.1 Persistent state examples

- character;
- level/XP;
- inventory;
- equipment;
- currencies;
- quest progress;
- achievements;
- reputation;
- professions;
- unlocked titles/badges;
- guild;
- mail;
- marketplace ownership.

## 62.2 Runtime-only examples

- current target;
- short cast state;
- projectile;
- short ground effect;
- active local AI path;
- temporary threat.

## 62.3 Save strategy

The design SHOULD avoid writing the entire character on every small state change.

Use:

- domain persistence;
- batching where safe;
- explicit transactional boundaries;
- recovery strategy.

Exact storage technology is outside this master spec.

---

# 63. Transactions and economic integrity

The following MUST be atomic or equivalently protected:

- direct trade;
- marketplace purchase;
- marketplace payout;
- vendor purchase;
- vendor sale;
- buyback;
- crafting material consumption/output;
- mail attachment claim;
- guild bank withdrawal;
- currency conversion.

Invariant:

> A retry, timeout or duplicated request must never create extra items or currency.

---

# 64. Concurrency rules

Every mutable resource MUST have an explicit owner/concurrency strategy.

Examples:

```text
Character inventory     → character domain owner
Marketplace listing     → marketplace/listing owner
Guild bank              → guild bank owner
Zone runtime entity     → zone simulation owner
Dungeon instance        → instance owner
```

Avoid multiple authoritative writers to the same state.

---

# 65. Performance targets

Exact numbers require benchmarking, but budgets MUST exist.

Measure at minimum:

- active players per zone;
- entities per zone;
- ECS tick duration;
- AI duration;
- pathfinding duration;
- AOI update duration;
- packets/sec;
- bytes/sec;
- GC allocations/sec;
- DB persistence latency;
- marketplace transaction latency.

## 65.1 Tick budget

OPEN.

The simulation SHOULD support configurable tick rates by subsystem if useful.

Not every system requires the same frequency.

Example concept:

```text
movement        high frequency
combat          medium/high
AI decision     medium
spawn checks    low
economy         event-driven
```

---

# 66. Pathfinding

Pathfinding is expected to become a major cost center.

Requirements:

- path requests SHOULD be bounded;
- monsters SHOULD NOT recompute full paths every frame;
- sleeping cells MUST NOT pathfind;
- repeated destinations SHOULD reuse/cache when safe;
- stuck detection required;
- return-home behavior required.

Benchmark pathfinding independently.

---

# 67. Observability

## 67.1 Metrics

At minimum expose:

```text
online_players
players_per_zone
entities_per_zone
active_monsters
sleeping_cells
active_cells
ecs_tick_ms
ai_tick_ms
pathfinding_ms
aoi_update_ms
packets_in_per_sec
packets_out_per_sec
bytes_out_per_sec
gc_alloc_rate
marketplace_transactions
trade_failures
```

## 67.2 Structured logs

Important business logs:

- rare loot creation;
- item deletion;
- trade;
- marketplace sale;
- currency grant/removal;
- admin grant;
- guild bank action;
- moderation action.

## 67.3 Correlation

Operations that cross services SHOULD carry correlation IDs.

---

# 68. Administration

Admin tooling SHOULD support:

- find player;
- inspect character;
- teleport;
- mute;
- kick;
- ban;
- modify quest state;
- grant/remove item with audit;
- grant/remove currency with audit;
- modify reputation with audit;
- spawn/despawn monster in controlled environments;
- reset encounter;
- inspect zone metrics.

Every privileged mutation MUST be audited.

---

# 69. Moderation

Required moderation concepts:

- warning;
- mute;
- temporary ban;
- permanent ban;
- chat report;
- player report;
- evidence/log references;
- moderator action history.

Moderation permissions SHOULD be capability-based.

---

# 70. Localization

Player-facing definitions SHOULD use localization keys instead of hardcoded server strings.

Includes:

- item names;
- descriptions;
- quests;
- NPC dialogue;
- skills;
- achievements;
- titles;
- notifications.

---

# 71. Time model

Server MUST have a canonical time source.

Systems depending on time:

- cooldowns;
- daily resets;
- marketplace expiration;
- mail expiration;
- world events;
- buffs;
- respawns;
- PvP seasons.

Wall-clock time and simulation elapsed time MUST NOT be confused.

---

# 72. Error handling

Player-visible failures SHOULD return stable domain error codes.

Example concepts:

```text
InventoryFull
NotEnoughCurrency
TargetOutOfRange
SkillOnCooldown
InvalidTarget
ListingNoLongerAvailable
TradeOfferChanged
MissingMaterials
PermissionDenied
```

The client maps error codes to localized presentation.

---

# 73. Reconnect and session recovery

The system MUST define reconnect behavior for:

- open world;
- party;
- dungeon;
- PvP;
- trade;
- marketplace transaction;
- dead character;
- loading transition.

A network disconnect MUST NOT leave the same character active indefinitely.

Sensitive transactions MUST complete or roll back independent of client connectivity.

---

# 74. UI system inventory

The current UI design set includes or expects:

## Character
- Equipment
- Stats
- Titles
- Reputation

## Skills
- Active
- Passive
- Specialization
- Shortcuts

## Quests
- Main
- Side
- Daily
- Completed

## Achievements
- Achievements overview
- Collections
- Badges
- Titles

## Group
- Current group
- Group Finder
- My Registrations

## Marketplace
- Buy
- Sell
- My Sales
- History

## Professions/Crafting
- professions overview;
- recipe/craft interface;
- relevant profession tabs.

## Shop
- Buy
- Sell
- Buyback
- categories:
  - Equipment
  - Consumables
  - Materials
  - Miscellaneous

## Guild
Expected:
- overview;
- members;
- activities;
- bank;
- management;
- permissions/logs as required.

## Other major screens still requiring final UI validation
- mail;
- bank;
- direct trade;
- map;
- dungeon summary;
- PvP;
- social/friends;
- settings;
- notifications;
- world event tracker;
- loot roll;
- death/resurrection;
- NPC dialogue.

---

# 75. Data-driven definition registry

The server SHOULD maintain versionable definitions for:

```text
Classes
Specializations
Skills
StatusEffects
Items
Equipment
Monsters
AIProfiles
LootTables
Spawns
Quests
Achievements
Collections
Reputations
Factions
Professions
Recipes
Vendors
Dungeons
WorldEvents
```

Definitions SHOULD be validated at startup/build time.

Invalid content SHOULD fail fast with actionable diagnostics rather than producing undefined runtime behavior.

---

# 76. Content IDs

Content MUST use stable identifiers independent of display names.

Bad:

```text
"Épée rouillée"
```

as a primary lookup identity.

Preferred:

```text
item.weapon.rusted_sword
monster.forest.bramble_wolf
quest.ambre.main.001
```

Exact convention is OPEN, but IDs MUST be stable.

---

# 77. Versioning and migration

Changing definitions MUST consider existing player state.

Examples:

- removed item;
- renamed skill;
- changed quest objectives;
- deleted reputation faction;
- changed profession recipe;
- equipment slot removed.

Content migrations MUST be explicit for destructive changes.

---

# 78. Testing strategy

## 78.1 Unit tests

Best targets:

- stat formulas;
- loot rolls;
- quest transitions;
- inventory operations;
- equipment validation;
- crafting;
- reputation thresholds;
- achievement progression.

## 78.2 Integration tests

Must cover:

- trade atomicity;
- marketplace race;
- mail claim retry;
- reconnect;
- dungeon lifecycle;
- AOI enter/leave;
- spawn sleep/wake;
- cross-zone character transition.

## 78.3 Simulation tests

Create headless load scenarios for:

- thousands of moving players;
- high mob density;
- mass combat;
- world boss;
- pathfinding pressure;
- AOI boundary churn.

## 78.4 Property/invariant tests

Strong candidates:

```text
currency never becomes duplicated by retry
item count preserved across trade
one listing cannot be bought twice
loot reward granted at most once
quest reward granted at most once
```

---

# 79. Minimum vertical slice

Before scaling content, prove the complete loop.

Recommended slice:

```text
1 city hub
1 outdoor zone
3 monster archetypes
1 elite
1 boss
1 dungeon
1–3 classes
small skill set per class
equipment + inventory
loot
10–15 quests
1 gathering profession
1 crafting profession
vendor
party
basic group finder
basic achievements
basic reputation
```

The vertical slice MUST exercise the real production architecture:

- AOI;
- ECS runtime;
- persistence;
- networking;
- authoritative combat.

Avoid building a throwaway prototype that cannot validate the final architecture.

---

# 80. Explicit non-goals for first playable

Unless separately approved, the first playable SHOULD NOT require:

- massive raid system;
- dozens of classes;
- hundreds of skills;
- extremely complex housing;
- real-time physics combat;
- advanced battleground PvP;
- cross-server matchmaking;
- player-created scripting;
- every profession;
- every economy feature at final scale.

---

# 81. Open design decisions

The following MUST be resolved before their dependent implementation plan is considered complete:

1. Launch class count.
2. Primary stat list.
3. Combat resource model per class.
4. Active skill bar size.
5. Auto-attack behavior.
6. Exact targeting rules.
7. PvP launch scope.
8. Death penalty.
9. Equipment slot list.
10. Rarity names.
11. Bind-on-pickup / bind-on-equip policy.
12. Loot distribution model.
13. Inventory size/expansion model.
14. Profession count and profession limits.
15. Craft duration model.
16. Daily reset time.
17. Marketplace listing duration/tax model.
18. Group finder: listings, matchmaking, or both.
19. Party max size.
20. Dungeon difficulty model.
21. Guild progression model.
22. Achievement scope: character vs account.
23. Collection scope: character vs account.
24. Fast travel rules.
25. Main-world PvP rules if any.
26. AOI cell size.
27. AOI radii.
28. Cell sleep/unload delays.
29. ECS tick rates.
30. Pathfinding implementation and budget.
31. Reconnect grace duration.
32. Character count per account.
33. Max level.
34. Post-level-cap progression.
35. Premium/cash-shop policy, if any.

---

# 82. Architecture invariants

These are hard constraints unless this spec is revised.

### INV-001
The client is never authoritative for value-producing gameplay results.

### INV-002
High-frequency world simulation is isolated from long-lived business state.

### INV-003
Inactive world areas do not run full monster simulation.

### INV-004
One mutable game resource has one authoritative owner at a time.

### INV-005
Economic operations are atomic/idempotent.

### INV-006
Content definitions are stable-ID, data-driven where practical.

### INV-007
The main world is spatially partitioned even if visually continuous.

### INV-008
Private dungeons/guild halls may use isolated instances.

### INV-009
AOI controls network relevance and contributes to simulation activation.

### INV-010
Every critical subsystem must have measurable performance/health metrics.

---

# 83. Definition of design-complete for a child system

A subsystem is ready for implementation planning only when its child spec contains:

- purpose;
- player flow;
- in-scope behavior;
- non-goals;
- domain model;
- state ownership;
- persistence rules;
- server commands;
- emitted events;
- client/UI states;
- permissions;
- validation;
- concurrency model;
- failure modes;
- reconnect behavior where relevant;
- observability;
- security/anti-cheat;
- tests;
- acceptance criteria;
- resolved open questions.

---

# 84. Implementation-plan handoff template

After a child design spec is approved, create a separate plan.

Suggested structure:

```markdown
# <Feature> Implementation Plan

## Goal
## Preconditions
## File structure
## Data migrations
## Tasks
### Task 1
- files
- failing test
- implementation
- verification

### Task 2
...

## Integration tests
## Load/performance checks
## Rollout
## Rollback
```

The plan MUST conform to the approved design rather than silently changing it.

---

# 85. Acceptance criteria for the master architecture

The architecture is considered validated when a vertical slice can demonstrate all of the following:

- A player enters the large world.
- AOI loads only nearby runtime entities.
- Distant monster areas remain inactive.
- Monsters wake and spawn correctly as players approach.
- Multiple players can fight the same monster.
- Combat is server authoritative.
- Skills, cooldowns and status effects resolve correctly.
- Loot is awarded exactly once.
- Inventory/equipment changes persist.
- Quest progress is event-driven and persists.
- A party can enter an isolated dungeon instance.
- Disconnect/reconnect does not duplicate the player or rewards.
- Marketplace/trade operations cannot duplicate items/currency.
- Metrics reveal zone, AI, pathfinding and networking cost.
- Admin tools can inspect and diagnose the runtime.
- The same content definitions can create additional monsters/items/quests without bespoke server architecture changes.

---

# 86. Next specification order

Recommended order before large-scale implementation:

```text
1. World + AOI + zone ownership
2. ECS runtime model
3. Character projection + persistence boundaries
4. Combat + skills + status effects
5. Monsters + AI + spawns
6. Items + inventory + equipment + loot
7. Quests + achievements + reputation
8. Party + group finder + dungeons
9. Professions + resources + crafting
10. Economy + vendors + marketplace + trade + mail
11. Guilds + guild hall
12. PvP
13. World events + bosses
14. Admin + moderation + observability
```

This order prioritizes the systems that define the technical foundation of everything else.

---

# 87. Review checklist

Before approving this master spec, verify:

- [ ] The world model matches the intended player experience.
- [ ] ECS boundaries are accepted.
- [ ] Orleans responsibilities are accepted.
- [ ] Server-authoritative rules are accepted.
- [ ] AOI activation/sleep design is accepted.
- [ ] Instanced-content boundaries are accepted.
- [ ] UI system inventory matches the mockups.
- [ ] No major RPG subsystem is missing.
- [ ] Open decisions are explicitly visible rather than guessed.
- [ ] Economic invariants are sufficient.
- [ ] Performance/observability are treated as product requirements.
- [ ] The vertical slice is small enough to build but complete enough to validate the architecture.

---

## End of master design specification
