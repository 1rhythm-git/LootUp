using UnityEngine;

namespace LootUp.Core.Characters
{
    public static class CharacterLevelGrowthPolicy
    {
        public const int GrowthCapLevel = 20;
        public const float StartingTimeBonusPerLevelSeconds = 0.2f;
        public const float FeverGainBonusPerLevel = 0.003f;
        public const float SupportItemEffectBonusPerLevel = 0.002f;

        public static int GetGrowthStepCount(int characterLevel)
        {
            return Mathf.Clamp(characterLevel, 1, GrowthCapLevel) - 1;
        }

        public static float GetStartingTimeBonusSeconds(int characterLevel)
        {
            return GetGrowthStepCount(characterLevel) * StartingTimeBonusPerLevelSeconds;
        }

        public static float GetFeverGainMultiplier(int characterLevel)
        {
            return 1f + GetGrowthStepCount(characterLevel) * FeverGainBonusPerLevel;
        }

        public static float GetSupportItemEffectMultiplier(int characterLevel)
        {
            return 1f + GetGrowthStepCount(characterLevel) * SupportItemEffectBonusPerLevel;
        }

        public static float ApplyFeverGain(float baseAmount, int characterLevel)
        {
            return Mathf.Max(0f, baseAmount) * GetFeverGainMultiplier(characterLevel);
        }

        public static float ApplySupportItemEffect(float baseAmount, int characterLevel)
        {
            return Mathf.Max(0f, baseAmount) * GetSupportItemEffectMultiplier(characterLevel);
        }
    }
}
