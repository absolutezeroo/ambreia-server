using System.Text.Json.Serialization;

namespace Vortex.Primitives.World.Rules;

/// <summary>One typed condition; <c>kind</c> picks the shape. An unknown kind fails to parse.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PlayerLevelCondition), "PlayerLevel")]
[JsonDerivedType(typeof(QuestStateCondition), "QuestState")]
[JsonDerivedType(typeof(TimeOfDayCondition), "TimeOfDay")]
[JsonDerivedType(typeof(HasItemCondition), "HasItem")]
[JsonDerivedType(typeof(FactionCondition), "Faction")]
[JsonDerivedType(typeof(ZoneActiveCondition), "ZoneActive")]
public abstract record ZoneCondition;
