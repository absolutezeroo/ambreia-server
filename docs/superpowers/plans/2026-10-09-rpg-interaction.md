# RPG F3b — Interaction : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** dans une zone, le décor sans action ne réagit plus à rien, le client apprend à l'entrée quels objets sont cliquables, et un joueur peut choisir une cible que la room garde et vide quand elle part.

**Architecture:**
- **Décor figé :** le refus se fait dans `RoomActionModule.UseItemByIdAsync`, juste après la tentative d'interaction de zone.
- **Objets cliquables :** `CompiledZone` calcule la liste une fois. La rafale d'entrée l'envoie en `RpgZoneInteractives`.
- **Cible :** elle vit dans l'état de la room (`RoomLiveState.Targets`), sous un module `RoomTargetModule`. On l'atteint par une nouvelle facette de grain, `IRoomTargets`. Le module écoute `PlayerLeftEvent` pour vider les cibles de qui part. Il lit niveau et classe au grain du personnage ciblé, qui ne rappelle jamais la room.

**Tech Stack:** C# .NET 10, Orleans (silo unique), xUnit + FluentAssertions, `RoomHarness` (Vortex.Rooms.Tests).

**Spec:** `docs/superpowers/specs/2026-10-09-rpg-interaction-design.md` (approuvée le 2026-10-09).

## Global Constraints

- **Ids réseau :**
  - entrant `RpgTargetSelect` = 9010 ;
  - sortants `RpgZoneInteractives` = 9008 et `RpgTarget` = 9009 ;
  - dans les régions `Vortex-specific` de `Headers.cs`, avec le commentaire `// RPG (spec §3.7)`.
- **Décor figé :** dans une room qui a un `ZoneRuntime`, un objet sans interaction de zone ne réagit à rien, quels que soient les droits. Hors zone, rien ne change.
- **Cible :** privée, sans portée, jamais écrite en base. Un id invalide vide la cible. Pendant la grâce, l'avatar reste, donc la cible aussi.
- **Grains :** la room n'appelle que le grain du personnage ciblé (`GetSummaryAsync`), qui ne rappelle jamais la room. Pas d'`await` de grains en boucle.
- **Conventions du dépôt :**
  - un type par fichier ;
  - le namespace suit le dossier ;
  - les `partial` de `RoomGrain` restent des façades (mur 6) ;
  - le code se modifie par Edit/Write ;
  - commits en français, avec des chemins explicites, sur `main`.

## Décisions de ce plan (écarts assumés à la spec)

1. **Un objet sans interaction mais visé par un déclencheur `Interact` est cliquable.** Il entre dans `RpgZoneInteractives` avec la sorte `interact`.
   - **Pourquoi :** l'usage d'un décor publie déjà `ZoneDecorUsedEvent`, qui fait jouer ces déclencheurs. Sans cela, le client cesserait d'envoyer le clic et le déclencheur mourrait.
   - **Ce qui ne change pas :** le décor reste figé, sans logique Habbo ni changement d'état ; seul le déclencheur joue.
2. **`ZoneDecorObject.Locked` et `ZoneDocument.Interactives` restent déclarés.** Ils sont commentés « ignoré à l'exécution » : la lecture des zones refuse tout champ inconnu (`ZoneJson`, `UnmappedMemberHandling.Disallow`), et des versions stockées peuvent les contenir. C'est le cas prévu par la spec §4.
3. **La sortie de B, dans la preuve `target`, passe par `Quit`.** Une coupure de socket laisserait l'avatar 60 s en grâce, et la cible avec lui.

## Review Focus

1. **Une cible qui part pendant que le joueur la vise encore**, puis revient dans la même room : elle n'est pas re-ciblée d'elle-même, et `RpgTarget(0)` est bien arrivé avant. Test : Tâche 3, `ATargetThatLeaves_IsClearedForWhoeverAimedAtIt`.
2. **Un id d'objet de room qui désigne un meuble ou rien** (0, négatif, l'id d'un décor) : la cible est vidée et `RpgTarget(0)` répond, jamais d'exception. Test : Tâche 3, `AnIdThatIsNotAPlayer_ClearsTheTarget`.
3. **Le propriétaire de la room clique un meuble à plusieurs états de la zone** : rien ne bouge, alors qu'il a les droits. Test : Tâche 1, `TheOwner_CannotToggleFrozenDecor`.
4. **Un grain de personnage ciblé qui lève** : la cible reste choisie, avec niveau 0 et classe vide, et un avertissement est journalisé. Test : Tâche 3, `ACharacterThatDoesNotAnswer_LeavesTheTargetChosen`.
5. **Une zone republiée pendant qu'on vise quelqu'un** : la room est rechargée et les joueurs y rentrent ; la cible est perdue, et le client la vide en entrant (contrat §7.3). Test : Tâche 3, `ATargetDoesNotSurviveTheRoomBeingRebuilt` (un `RoomLiveState` neuf n'a pas de cible).

---

### Task 1: Le décor figé et la liste des objets cliquables

**Files:**
- Modify: `Vortex.Rooms/Grains/Modules/RoomActionModule.cs` (`UseItemByIdAsync`)
- Modify: `Vortex.Rooms/Grains/Zones/Compilation/CompiledZone.cs` (propriété `Interactives`)
- Create: `Vortex.Rooms/Grains/Zones/Compilation/ZoneClickable.cs`
- Modify: `Vortex.Primitives/World/Layers/ZoneDecorObject.cs` (`Locked` : commentaire « ignoré »), `Vortex.Primitives/World/ZoneDocument.cs` (`Interactives` : idem)
- Test: `Vortex.Rooms.Tests/Zones/ZoneFrozenDecorTests.cs`, `Vortex.Rooms.Tests/Zones/ZoneCompilerTests.cs` (ajouts)

**Interfaces:**
- Produces:
  - `public sealed record ZoneClickable(int ObjectId, string Interaction)` (`Vortex.Rooms.Grains.Zones.Compilation`) ;
  - `public IReadOnlyList<ZoneClickable> Interactives { get; }` sur `CompiledZone`. Elle contient, dans l'ordre des objets, chaque décor au sol avec une `Interaction` non vide (sa sorte), ou sans interaction mais visé par un déclencheur `Interact` actif (sorte `"interact"`).

- [ ] **Step 1: Tests qui échouent.**

  `ZoneFrozenDecorTests` reprend la mise en place de `ZoneInteractionTests` : `RoomHarness.CreateZoneAsync`, logique `SolidLogic()` qui enregistre les états, et `Furni(...)` sans interaction.
  - `TheOwner_CannotToggleFrozenDecor` :
    - un décor au sol sans interaction, dont la logique a 2 états, avec `GetUsagePolicy` → `Controller` ;
    - `RoomHarness.Owner` l'utilise (`UseItemByIdAsync`) ;
    - `_states` reste vide, et la méthode répond `false`.
  - `AStranger_CannotToggleFrozenDecorEither` : le même objet utilisé par `RoomHarness.Stranger` ne bouge pas non plus.
  - `ADoor_StillOpens` : `Furni("door")` utilisé, son état passe à 1, comme dans `ZoneInteractionTests`.
  - `AnInteractTrigger_StillFiresOnFrozenDecor` :
    - un décor sans interaction, plus un déclencheur `Interact` sur son id avec une action `ShowDialogue` ;
    - on l'utilise : la bulle part (`harness.ComposersSent` contient un `NotificationDialogMessageComposer`), et `_states` reste vide.
  - `OutsideAZone_TheHabboPathStays` : dans une room ordinaire (`RoomHarness.CreateAsync`), le même meuble utilisé par le propriétaire change d'état. C'est le comportement d'aujourd'hui, à garder.

  Dans `ZoneCompilerTests`, ajouter :
  - `TheClickables_AreTheDecorWithAnInteractionOrAnInteractTrigger` : une zone de 4 décors (`door`, aucune, aucune visée par un déclencheur `Interact`, `sign`) donne `[(1,"door"), (3,"interact"), (4,"sign")]` ;
  - `AZoneWithoutInteractions_HasNoClickables`.

- [ ] **Step 2: Les lancer.** `dotnet test Vortex.Rooms.Tests --filter "FullyQualifiedName~ZoneFrozenDecor|FullyQualifiedName~ZoneCompiler"`. Attendu :
  - le propriétaire et l'inconnu font bouger l'objet (échec) ;
  - `Interactives` n'existe pas (compilation).

- [ ] **Step 3: Implémenter.**

```csharp
// RoomActionModule.UseItemByIdAsync, juste après TryUseAsync
if (await _roomGrain.ZoneInteractions.TryUseAsync(ctx, item, param, ct))
{
    return true;
}

// A zone's decor is frozen (spec F3b §4): without an interaction of its own, a click is no
// furniture use -- whatever the player's rights. Its Interact triggers have already fired.
if (_roomGrain._state.ZoneRuntime is not null)
{
    _roomGrain._logger.LogDebug(
        "Frozen decor {ObjectId} in zone room {RoomId}: player {PlayerId}'s click does nothing.",
        itemId.Value,
        _roomGrain.RoomId.Value,
        ctx.PlayerId.Value
    );

    return false;
}
```

```csharp
// CompiledZone, dans le constructeur, après _triggersByTarget
Interactives =
[
    .. document.Decor.Select((decor, index) => (decor, ObjectId: index + 1))
        .Select(x =>
            x.decor.Interaction is { Length: > 0 } kind ? new ZoneClickable(x.ObjectId, kind)
            : TriggersFor(ZoneTriggerType.Interact, x.decor.Id).Any()
                ? new ZoneClickable(x.ObjectId, "interact")
            : null)
        .OfType<ZoneClickable>(),
];

/// <summary>What a player can click in this zone (spec F3b §5): every floor decor with an
/// interaction, or aimed at by an Interact trigger, by room object id.</summary>
public IReadOnlyList<ZoneClickable> Interactives { get; }
```

  Sur `ZoneDecorObject.Locked` et `ZoneDocument.Interactives`, le résumé devient : `/// <summary>Ignored at run time (spec F3b §4): decor is always frozen; kept so stored zone versions that carry it still parse (ZoneJson refuses unknown fields).</summary>`.

- [ ] **Step 4: Les lancer.** `dotnet test Vortex.Rooms.Tests` : tout vert, `ZoneInteractionTests` et `ZoneTriggerTests` compris.

- [ ] **Step 5: Commit** (`interaction : le décor d'une zone est figé, la zone sait ce qui est cliquable`).

---

### Task 2: `RpgZoneInteractives` à l'entrée en zone

Suivre le skill `adding-a-packet`.

**Files:**
- Create: `Vortex.Protocol/Messages/Outgoing/Rpg/Interaction/RpgZoneInteractivesMessageComposer.cs`, `RpgZoneInteractive.cs`
- Create: `Vortex.Revisions/Revision20260701/Serializers/Rpg/Interaction/RpgZoneInteractivesMessageComposerSerializer.cs`, `Maps/RpgInteractionMap.cs` ; Modify: `Revision20260701.cs`, `Headers.cs`
- Modify: `Vortex.Rooms/Grains/RoomGrain.Entry.cs` (`BuildEntryBurstAsync`)
- Test: `Vortex.Revisions.Tests/Rpg/RpgInteractionWireTests.cs`, `Vortex.Rooms.Tests/Zones/ZoneEntryClickablesTests.cs`, et `Vortex.Rooms.Tests/Session/RoomEntryPayloadTests.cs` s'il épingle la liste des composers de la rafale

**Interfaces:**
- Consumes: `CompiledZone.Interactives` (Tâche 1).
- Produces:
  - `[GenerateSerializer, Immutable] sealed record RpgZoneInteractive { [Id(0)] required int ObjectId; [Id(1)] required string Interaction; }` ;
  - `[GenerateSerializer, Immutable] sealed record RpgZoneInteractivesMessageComposer : IComposer { [Id(0)] required ImmutableArray<RpgZoneInteractive> Objects; }` ;
  - le serializer écrit `count`, puis `objectId` et `interaction` pour chaque objet ;
  - `MessageComposer.RpgZoneInteractivesMessageComposer = 9008`. Les en-têtes 9009 et 9010 sont ajoutés à la Tâche 3, avec leurs messages.

- [ ] **Step 1: Tests qui échouent.**
  - **Fil :** `TheClickables_AreReadInTheClientsOrder`, avec 2 objets ; le corps lu vaut `2, 1, "door", 4, "sign"`, et il ne reste rien.
  - **Entrée :** `AZoneEntry_SendsItsClickables` dans `ZoneEntryClickablesTests`. Une zone avec une porte (objet 1) : la rafale d'`AdmitAsync` contient un `RpgZoneInteractivesMessageComposer` avec `[(1, "door")]`. Le placer après `ItemsMessageComposer`, dans la même famille que les composers de la room.
  - **Hors zone :** `ARoomThatIsNotAZone_SendsNoClickables`, la rafale n'en contient aucun.

- [ ] **Step 2: Les lancer — échec attendu** (compilation).

- [ ] **Step 3: Implémenter** messages, serializer, carte et en-têtes, selon le skill. Dans `BuildEntryBurstAsync`, après `ItemsMessageComposer` :

```csharp
// What a player can click here (spec F3b §5): the client stops showing Habbo's furni info
// for everything else.
if (_state.ZoneRuntime is { } zone)
{
    burst.Add(
        new RpgZoneInteractivesMessageComposer
        {
            Objects =
            [
                .. zone.Compiled.Interactives.Select(c => new RpgZoneInteractive
                {
                    ObjectId = c.ObjectId,
                    Interaction = c.Interaction,
                }),
            ],
        }
    );
}
```

  Seule la constante 9008 est ajoutée ici, pour que `check-inert-declarations` reste à 0 trou.

- [ ] **Step 4: Vérifications.**
  - `dotnet test Vortex.Revisions.Tests` et `dotnet test Vortex.Rooms.Tests` sont verts. Mettre à jour `RoomEntryPayloadTests` si une liste de la rafale y est épinglée : c'est un changement voulu.
  - `node scripts/hooks/check-inert-declarations.mjs` : 0 trou.

- [ ] **Step 5: Commit** (`interaction : RpgZoneInteractives dans la rafale d'entrée d'une zone`).

- [ ] **Step 6: Relecture des tâches 1–2** par l'agent `grain-rules-reviewer` (le diff touche `Vortex.Rooms/Grains`), puis corrections, chacune avec un test qui échoue d'abord, et registre `.superpowers/sdd/2026-10-09-rpg-interaction/progress.md`.

---

### Task 3: La cible

**Files:**
- Create: `Vortex.Primitives/Rooms/Grains/IRoomTargets.cs` ; Modify: `IRoomGrain.cs` (agrégat), `Vortex.Primitives/Orleans/GrainFactoryExtensions.cs` (`GetRoomTargets`)
- Create: `Vortex.Rooms/Grains/State/RoomTarget.cs` ; Modify: `Vortex.Rooms/Grains/State/RoomLiveState.cs` (`Targets`)
- Create: `Vortex.Rooms/Grains/Modules/RoomTargetModule.cs`, `Vortex.Rooms/Grains/RoomGrain.Targets.cs` ; Modify: `RoomGrain.cs` (création et `EventModule.Register`)
- Create: `Vortex.Protocol/Messages/Incoming/Rpg/Interaction/RpgTargetSelectMessage.cs`, `Vortex.Protocol/Messages/Outgoing/Rpg/Interaction/RpgTargetMessageComposer.cs`
- Create: `Vortex.Revisions/Revision20260701/Parsers/Rpg/Interaction/RpgTargetSelectMessageParser.cs`, `Serializers/Rpg/Interaction/RpgTargetMessageComposerSerializer.cs` ; Modify: `Maps/RpgInteractionMap.cs`, `Headers.cs`
- Create: `Vortex.PacketHandlers/Rpg/Interaction/RpgTargetSelectMessageHandler.cs`
- Modify: `Vortex.Rooms.Tests/Support/RoomHarness.cs` (faux `IPlayerCharacterGrain`, résumés par joueur)
- Modify: `scripts/hooks/authorization-surface-baseline.json` (`--update`)
- Test: `Vortex.Rooms.Tests/Avatars/RoomTargetTests.cs`, `Vortex.Revisions.Tests/Rpg/RpgInteractionWireTests.cs` (ajouts), `Vortex.PacketHandlers.Tests/Rpg/TargetSelectHandlerTests.cs`

**Interfaces:**
- Produces:
  - **La facette de grain :**
    - `[Alias("Vortex.Primitives.Rooms.Grains.IRoomTargets")] public interface IRoomTargets : IGrainWithIntegerKey { public Task SelectTargetAsync(ActionContext ctx, int objectId, CancellationToken ct); }` ;
    - `IRoomGrain` l'agrège ;
    - `public static IRoomTargets GetRoomTargets(this IGrainFactory factory, RoomId roomId)`.
  - **L'état :**
    - `internal readonly record struct RoomTarget(RoomObjectId ObjectId, PlayerId PlayerId)` ;
    - `public Dictionary<PlayerId, RoomTarget> Targets { get; } = [];` sur `RoomLiveState`.
  - **Le module :** `RoomTargetModule(RoomGrain roomGrain) : IRoomEventListener`, avec `SelectAsync(ActionContext ctx, int objectId, CancellationToken ct)` et `OnRoomEventAsync`, qui traite `PlayerLeftEvent`.
  - **Le paquet entrant :** `RpgTargetSelectMessage : IMessageEvent { required int ObjectId; }`, sous `MessageEvent.RpgTargetSelectMessageEvent = 9010`.
  - **Le composer :** `[GenerateSerializer, Immutable] RpgTargetMessageComposer : IComposer`, avec :
    - `[Id(0)] int ObjectId`, `[Id(1)] string Kind`, `[Id(2)] string Name`, `[Id(3)] int Level`, `[Id(4)] string ClassId` ;
    - `public const string KindPlayer = "player";` ;
    - `public static readonly RpgTargetMessageComposer None` (0, `""`, `""`, 0, `""`) ;
    - le serializer écrit les champs dans l'ordre §7.2, sous `MessageComposer.RpgTargetMessageComposer = 9009`.

- [ ] **Step 1: Le faux personnage du harnais.** `RoomHarness` gagne `public Dictionary<PlayerId, CharacterSummary> Characters { get; } = [];`, et un booléen `CharacterFails` qui fait lever `GetSummaryAsync`. Le `BuildGrainFactory` répond pour `IPlayerCharacterGrain` :

```csharp
if (call.Method.GetGenericArguments()[0] == typeof(IPlayerCharacterGrain))
{
    PlayerId player = new((int)(long)call.Args![0]!);

    return FakeProxy.Create<IPlayerCharacterGrain>(inner =>
        inner.Method.Name == nameof(IPlayerCharacterGrain.GetSummaryAsync)
            ? CharacterFails
                ? Task.FromException<CharacterSummary?>(new InvalidOperationException("down"))
                : Task.FromResult(Characters.GetValueOrDefault(player))
            : null
    );
}
```

- [ ] **Step 2: Tests qui échouent** (`RoomTargetTests`). Deux joueurs, `Owner` et `Stranger`, entrent dans la room par les aides du harnais ; `ComposersSent` filtre les `RpgTargetMessageComposer`.
  - `ChoosingAPlayer_AnswersHisNameLevelAndClass` :
    - `Characters[Stranger]` est guerrier niveau 3 ;
    - Owner sélectionne l'objet de Stranger ;
    - Owner seul reçoit `RpgTarget(objStranger, "player", <nom>, 3, "class.warrior")`.
  - `ChoosingOneself_IsAllowed`
  - `ZeroClearsTheTarget` : `RpgTarget.None`, et `Targets` ne contient plus Owner.
  - `AnIdThatIsNotAPlayer_ClearsTheTarget` : 0, −5 et l'id d'un meuble répondent tous `None`, sans exception.
  - `ATargetThatLeaves_IsClearedForWhoeverAimedAtIt` :
    - Owner vise Stranger, puis Stranger quitte la room (`RemoveAvatarFromPlayerAsync`) ;
    - Owner reçoit `None`.
  - `TheOneWhoLeaves_IsForgotten` : Owner vise Stranger puis part. `Targets` ne contient plus Owner, et Stranger ne reçoit rien.
  - `ACharacterThatDoesNotAnswer_LeavesTheTargetChosen` : `CharacterFails = true`. La réponse a le nom, le niveau 0 et la classe vide, et `Targets[Owner]` est posé.
  - `ATargetDoesNotSurviveTheRoomBeingRebuilt` : un `RoomLiveState` neuf a `Targets` vide.
  - `WithoutAPlayer_NothingHappens` : `ctx.PlayerId` = 0, rien n'est envoyé.

  Fil (`RpgInteractionWireTests`) : `ATargetSelect_ReadsTheObjectId` (9010), et `TheTarget_IsReadInTheClientsOrder` (9009).

  Handler (`TargetSelectHandlerTests`, forme de `ItemHandlersTests`) : avec joueur et room, `SelectTargetAsync(ctx, objectId)` est appelé sur la room du contexte ; sans joueur ou sans room, rien.

- [ ] **Step 3: Les lancer — échec attendu** (compilation).

- [ ] **Step 4: Implémenter.**

```csharp
// RoomGrain.Targets.cs — façade (mur 6)
public Task SelectTargetAsync(ActionContext ctx, int objectId, CancellationToken ct) =>
    ctx.PlayerId <= 0 ? Task.CompletedTask : TargetModule.SelectAsync(ctx, objectId, ct);
```

```csharp
// RoomTargetModule
public async Task SelectAsync(ActionContext ctx, int objectId, CancellationToken ct)
{
    if (
        objectId <= 0
        || !roomGrain._state.AvatarsByObjectId.TryGetValue(new RoomObjectId(objectId), out IRoomAvatar? avatar)
        || avatar is not IRoomPlayer target
    )
    {
        roomGrain._state.Targets.Remove(ctx.PlayerId);
        await SendAsync(ctx.PlayerId, RpgTargetMessageComposer.None);

        return;
    }

    roomGrain._state.Targets[ctx.PlayerId] = new RoomTarget(target.ObjectId, target.PlayerId);
    CharacterSummary? summary = await SummaryOrNullAsync(target.PlayerId);

    await SendAsync(
        ctx.PlayerId,
        new RpgTargetMessageComposer
        {
            ObjectId = objectId,
            Kind = RpgTargetMessageComposer.KindPlayer,
            Name = target.GetSnapshot().Name,
            Level = summary?.Level ?? 0,
            ClassId = summary?.ClassId ?? string.Empty,
        }
    );
}

public async Task OnRoomEventAsync(RoomEvent evt, CancellationToken ct)
{
    if (evt is not PlayerLeftEvent left)
    {
        return;
    }

    roomGrain._state.Targets.Remove(left.PlayerId);

    PlayerId[] aimers =
    [
        .. roomGrain._state.Targets.Where(t => t.Value.PlayerId == left.PlayerId).Select(t => t.Key),
    ];

    foreach (PlayerId aimer in aimers)
    {
        roomGrain._state.Targets.Remove(aimer);
    }

    // Different players' presences: sent together, not one after the other.
    await Task.WhenAll(aimers.Select(a => SendAsync(a, RpgTargetMessageComposer.None)));
}
```

Les deux aides du module :
- **`SummaryOrNullAsync`** appelle `roomGrain._grainFactory.GetPlayerCharacterGrain(player).GetSummaryAsync()`. Une exception est journalisée en avertissement (joueur ciblé), et l'aide répond `null`.
- **`SendAsync(PlayerId to, IComposer c)`** envoie par `roomGrain._grainFactory.GetPlayerPresenceGrain(to).SendComposerAsync(c)`, dans un try/catch qui journalise.

Le nom vient de l'instantané de l'avatar : vérifier le nom exact de la propriété sur `RoomPlayerAvatarSnapshot`, qui porte le nom envoyé par `Users`.

Le reste du câblage :
- dans `RoomGrain.cs` : `TargetModule = new RoomTargetModule(this);` puis `EventModule.Register(TargetModule);` ;
- **le handler** fait `if (ctx.PlayerId <= 0 || ctx.RoomId <= 0) return;`, puis appelle `grainFactory.GetRoomTargets(ctx.RoomId).SelectTargetAsync(ctx.AsActionContext(), message.ObjectId, ct)`.

- [ ] **Step 5: Vérifications.**
  - `dotnet test Vortex.Rooms.Tests`, `Vortex.Revisions.Tests` et `Vortex.PacketHandlers.Tests` sont verts.
  - `node scripts/hooks/check-inert-declarations.mjs` : 0 trou.
  - `node scripts/hooks/check-authorization-surface.mjs --update` : `IRoomTargets::SelectTargetAsync` y entre avec la garde `actor-guard`, celle de `ClickTileAsync`. Si elle sort `NONE`, la garde de la façade n'est pas reconnue : corriger la façade, pas la baseline.
  - `node scripts/hooks/check-architecture-walls.mjs` : OK.

- [ ] **Step 6: Commit** (`interaction : la cible, gardée par la room et vidée quand elle part`).

---

### Task 4: Les preuves, la doc, la relecture

**Files:**
- Modify: `.claude/skills/run-vortex-emulator/driver.mjs`. Les en-têtes `rpgZoneInteractives: 9008`, `rpgTarget: 9009`, `rpgTargetSelect: 9010` et `quit` (lu dans `Headers.cs`) ; `travel` étendu ; la commande `target` ; un second compte jetable.
- Modify: `.claude/skills/run-vortex-emulator/SKILL.md`, `docs/architecture.md` (« Flow 9 — interaction »)
- Modify: `docs/superpowers/specs/2026-10-09-rpg-interaction-design.md` (statut, décision 1 du plan : la sorte `interact`)

**Interfaces:**
- Consumes: les paquets des Tâches 2 et 3.

- [ ] **Step 1: `travel`.** À l'entrée de `proof_a` :
  - attendre `RpgZoneInteractives` et vérifier qu'il liste la porte, le panneau et la porte d'enclos de la zone de preuve, avec leurs sortes ;
  - ajouter au document de `proof_a` un décor sans interaction (un second trône, à une case libre) ;
  - envoyer `UseFurniture` sur son id d'objet ; vérifier qu'aucun paquet d'état de meuble ne répond en 1,5 s ;
  - journaliser « frozen decor: no answer ».

- [ ] **Step 2: `target`.**
  - **Les comptes :** un second compte jetable, `run-skill-b@vortex.local`. `webSession()` et `enterWorld()` prennent le compte en paramètre, le premier par défaut.
  - **Mise en place :** A entre dans le monde et ouvre sa room ; B entre, puis envoie `OpenFlatConnection` sur la room de A.
  - **Repérer B :** A lit l'id d'objet de B dans le `Users` qui annonce B (après les trois chaînes, l'entier suivant).
  - **Cibler :** A envoie `RpgTargetSelect(objB)` et attend `RpgTarget` avec l'id de B, `player`, son nom, son niveau et sa classe. Le journaliser.
  - **B part :** B envoie `Quit`, et A attend `RpgTarget(0)`.
  - **Contre-épreuve :** A envoie `RpgTargetSelect(<id d'un meuble>)` et attend `RpgTarget(0)`.
  - Ajouter une ligne d'usage et une ligne au SKILL.

- [ ] **Step 3: Documentation.**
  - **`docs/architecture.md`, « Flow 9 — interaction » :** décor figé, `CompiledZone.Interactives` et `RpgZoneInteractives`, `RoomTargetModule` et ses effacements, preuves `travel` et `target`.
  - **Spec F3b :** statut « livrée par le plan 2026-10-09-rpg-interaction », plus la décision 1 du plan (sorte `interact`) dans la liste §7.2 des sortes.

- [ ] **Step 4: Porte et preuve.**
  - La porte de qualité, ou ses étapes une à une si un autre émulateur tient `Vortex.Main/bin`, en le disant.
  - Le plugin publié ; `travel`, `target`, `smoke` et `lobby` sur une instance du driver (la seconde instance de `SKILL.md` si besoin).
  - Commit (`interaction : preuves travel et target, documentation`).

- [ ] **Step 5: Relecture des tâches 3–4** par `grain-rules-reviewer`, puis relecture finale de toute la branche (agent neuf, modèle le plus capable). Corrections avec tests, registre, et mise à jour de la mémoire `rpg-progress.md` : F3b livré, la suite est F3c (chat).
