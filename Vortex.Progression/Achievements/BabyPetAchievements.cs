using System.Collections.Frozen;
using System.Collections.Generic;

namespace Vortex.Progression.Achievements;

/// <summary>
/// Which baby pet type each breeding achievement counts the colours of.
/// </summary>
/// <remarks>
/// The ids are the client's own, read from <c>pet.type.&lt;id&gt;</c> in external_flash_texts:
/// 24 Baby Bear, 25 Baby Terrier, 28 Kitten Baby, 29 Puppy Baby, 30 Piglet Baby. They are the babies,
/// not the adults — <c>pet.type.4</c> is a Bear and no achievement counts it, which is the whole
/// point of "for owning %limit% different coloured <em>baby</em> bears".
/// </remarks>
public static class BabyPetAchievements
{
    public static readonly FrozenDictionary<int, string> ByPetType = new Dictionary<int, string>
    {
        [24] = AchievementNames.BearBreeder,
        [25] = AchievementNames.TerrierBreeder,
        [28] = AchievementNames.CatBreeder,
        [29] = AchievementNames.DogBreeder,
        [30] = AchievementNames.PigBreeder,
    }.ToFrozenDictionary();
}
