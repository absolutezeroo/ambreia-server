using System.Text.Json.Serialization;

namespace Vortex.Primitives.World.Rules;

/// <summary>One typed action; <c>kind</c> picks the shape. An unknown kind fails to parse.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(StartQuestAction), "StartQuest")]
[JsonDerivedType(typeof(ShowDialogueAction), "ShowDialogue")]
[JsonDerivedType(typeof(ActivateZoneAction), "ActivateZone")]
[JsonDerivedType(typeof(DeactivateZoneAction), "DeactivateZone")]
[JsonDerivedType(typeof(TeleportAction), "Teleport")]
[JsonDerivedType(typeof(SpawnGroupAction), "SpawnGroup")]
[JsonDerivedType(typeof(PlaySoundAction), "PlaySound")]
[JsonDerivedType(typeof(OpenDoorAction), "OpenDoor")]
public abstract record ZoneAction;
