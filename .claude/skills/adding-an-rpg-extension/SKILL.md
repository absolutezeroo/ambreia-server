---
name: adding-an-rpg-extension
description: Adds a pluggable piece to a Vortex RPG system — a skill effect type (ISkillEffect), a monster behaviour (IMonsterBehavior), a hit-chain step (IHitStep), a combat system ticked by every zone (ICombatSystem), a stat modifier source (IStatModifierSource), or a chat channel, rule or link kind — declared by DI in its own module so the core never names it, with its test and live proof. Use whenever the user wants a new kind of skill effect ("push", "drain", "teleport"), a new monster AI ("ranged", "caster", a boss), a new damage rule (resistance, vulnerability, block), a new periodic combat mechanic, a new chat channel, or asks where such a piece goes.
---

# A pluggable RPG piece

The owner's rule: **the core runs a pipeline, each variant is a piece declared by the module that
owns it, and adding one never touches the core**. If your change edits `CombatZone`, `HitResolver`,
the chat pipeline or a `Kinds/` class to name your piece, it is in the wrong place.

## Pick the extension point

| You want… | Implement | Key / order | Declared in | Copy |
|---|---|---|---|---|
| A skill effect type (`"kind"` of a skill's effect) | `ISkillEffect` (it is also the import's `ISkillEffectRule`) | `Kind` string | `SkillsModule.AddEffect<T>(services)` | `Vortex.Rpg.Skills/Effects/DamageEffect.cs` |
| A monster AI (`"behavior"` of a monster) | `IMonsterBehavior` | `Key` string; the last declared wins | `services.AddSingleton<IMonsterBehavior, T>()` in `MonstersModule` | `Vortex.Rpg.Monsters/Behaviors/MeleeBehavior.cs` |
| A step of every hit (dodge, armor, shield…) | `IHitStep` | `Priority`: dodge 100, base 200, crit 300, modifiers 350, armor 400, minimum 900, shield 950; equal ones run by type name | `services.AddSingleton<IHitStep, T>()` in the module that owns the rule | `Vortex.Rpg.Combat/Hits/ArmorStep.cs` |
| A mechanic ticked in every zone (statuses, casts) | `ICombatSystem` | `Order`: statuses 100, casts 200; all tick before the automatic hits | its module, as one singleton seen under both its type and `ICombatSystem` | `Vortex.Rpg.Skills/Statuses/StatusSystem.cs` |
| Stats that change while something lasts | `IStatModifierSource` | — | same, often the same singleton as its system | `StatusSystem` |
| A chat channel / rule / link kind | `IChatChannel` / `IChatRule` / `IChatLinkKind` | `Key`; the last declared wins, a shipped one (`IsDefault`) always yields | the owning module's `ConfigureServices` | `Vortex.Players/Chat/Channels/WorldChatChannel.cs` |

None fits? Do not bend one: ask the owner. A new extension point is a design decision of a spec, and
the RPG specs (chat, M1a, M1b) carry a "Modularité et extensibilité" section listing the planned ones.

## Recipe

1. **Read the interface** and its neighbours first: the XML docs say what the core guarantees
   (an effect's target is alive; a hit step returning false stops the chain; a combat system keeps
   its state in the fighters' parts, `Combatant.Get<T>()`, because one instance serves every zone).
2. **The class** goes in its module's folder for that kind of piece (`Effects/`, `Behaviors/`,
   `Hits/`, `Statuses/`…): one type per file, the namespace is the folder. No state in fields:
   pieces are singletons shared by every zone.
3. **Declare it** in the module's `ConfigureServices`, next to its siblings, with a comment naming the
   spec section. A piece that is two things (a system and a modifier source) is one singleton exposed
   twice (`sp => sp.GetRequiredService<T>()`), never two instances.
4. **Content that uses it**: an effect's fields or a behaviour's key are validated at import. Add
   your piece to the lists `ContentPackInRepoTests` builds by hand ("a new shipped effect is added here
   too"), and to `SkillKindTests` / `DeclaredBehaviorsTests` when they enumerate. Then write the content
   with the `adding-rpg-content` skill.
5. **Test it plugged in, not just alone**: through the test world of its project
   (`Vortex.Rpg.Skills.Tests/Support/SkillWorld.cs`, `Vortex.Rpg.Monsters.Tests/Support/MonsterWorld.cs`,
   `Vortex.Rpg.Combat.Tests/CombatExtensionTests.cs`), so the zone runs it as it will in game. Copy
   `EffectTests`, `MeleeBehaviorTests` or `StatusFromModuleTests`. A test that declares a throwaway
   piece is also how you prove a new extension point works.
6. **Prove it live**: push the content, then the driver scenario that exercises it
   (`driver.mjs combat`, `driver.mjs skills`; see the `run-vortex-emulator` skill). A piece the client
   must draw (a new status icon, a telegraph) also needs its packet: `adding-a-packet`.

## Traps

- **A piece left undeclared compiles and does nothing**: content naming its key is refused at import
  (effect, behaviour), or the step silently never runs (hit step, system). Check the module line.
- **Order and priority are part of the behaviour**: a damage modifier after armor or a shield before
  the minimum gives other numbers. Put the chosen value next to the siblings in the table above, and
  test the order when it matters.
- **Never send from a piece**: a combat system queues its messages in `BeforeFlush` / `Entry`; the
  zone sends them grouped. A chat channel delivers through `IChatRouting`.
- **Randomness** uses the `Random` the core hands in (`HitContext.Random`, the zone's), never
  `Random.Shared` or a `new Random()`: the tests swap in `FixedRandom` to pin a crit or a dodge.
- A new `Vortex.Rpg.*` project for a piece is almost never needed; it requires the owner's approval and
  an entry in `PLATFORM` of `scripts/hooks/check-architecture-walls.mjs`.

## Done

The project's tests green with the piece running through its test world, `VortexCloudQualityGate`
green, the live scenario showing the effect, and the evidence (commands and output) in the report.
Under `Grains/` or `Vortex.Rooms`, have `grain-rules-reviewer` review the diff.
