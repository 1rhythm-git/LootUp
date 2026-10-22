using LootUp.Core.Characters;
using LootUp.Core.Items;
using UnityEngine;

namespace LootUp.Core.Game
{
    [System.Serializable]
    public sealed class RunRewardSettings
    {
        [SerializeField, Min(0)]
        private int floorScorePerFloor = 100;

        [SerializeField, Min(0)]
        private int lifeScorePerHeart = 100;

        [SerializeField, Min(0f)]
        private float scoreExperienceMultiplier = 0.01f;

        [SerializeField, Min(0)]
        private int floorExperiencePerFloor = 1;

        [SerializeField, Min(0)]
        private int maximumPerformanceExperiencePerRun = 100;

        [SerializeField, Min(0f)]
        private float bonusGameMoneyMultiplier = 0.01f;

        public int FloorScorePerFloor => Mathf.Max(0, floorScorePerFloor);
        public int LifeScorePerHeart => Mathf.Max(0, lifeScorePerHeart);
        public float ScoreExperienceMultiplier => Mathf.Max(0f, scoreExperienceMultiplier);
        public int FloorExperiencePerFloor => Mathf.Max(0, floorExperiencePerFloor);
        public int MaximumPerformanceExperiencePerRun => Mathf.Max(0, maximumPerformanceExperiencePerRun);
        public float BonusGameMoneyMultiplier => Mathf.Max(0f, bonusGameMoneyMultiplier);
    }

    public readonly struct RunRewardBreakdown
    {
        public RunRewardBreakdown(
            int gameplayScore,
            int floorScore,
            int lifeScore,
            int artifactBonusScore,
            int totalScore,
            int levelExperience,
            int floorExperience,
            int scoreExperience,
            int artifactBonusExperience,
            int totalExperience,
            int acquiredGameMoney,
            int bonusGameMoney)
        {
            GameplayScore = Mathf.Max(0, gameplayScore);
            FloorScore = Mathf.Max(0, floorScore);
            LifeScore = Mathf.Max(0, lifeScore);
            ArtifactBonusScore = Mathf.Max(0, artifactBonusScore);
            TotalScore = Mathf.Max(0, totalScore);
            LevelExperience = Mathf.Max(0, levelExperience);
            FloorExperience = Mathf.Max(0, floorExperience);
            ScoreExperience = Mathf.Max(0, scoreExperience);
            ArtifactBonusExperience = Mathf.Max(0, artifactBonusExperience);
            TotalExperience = Mathf.Max(0, totalExperience);
            AcquiredGameMoney = Mathf.Max(0, acquiredGameMoney);
            BonusGameMoney = Mathf.Max(0, bonusGameMoney);
        }

        public int GameplayScore { get; }
        public int FloorScore { get; }
        public int LifeScore { get; }
        public int ArtifactBonusScore { get; }
        public int TotalScore { get; }
        public int LevelExperience { get; }
        public int FloorExperience { get; }
        public int ScoreExperience { get; }
        public int ArtifactBonusExperience { get; }
        public int BonusExperience => ScoreExperience + ArtifactBonusExperience;
        public int TotalExperience { get; }
        public int AcquiredGameMoney { get; }
        public int BonusGameMoney { get; }
        public int TotalGameMoney => AcquiredGameMoney + BonusGameMoney;
    }

    public static class RunRewardCalculator
    {
        public static RunRewardBreakdown Calculate(
            RunRewardSettings settings,
            CharacterDefinition characterDefinition,
            int characterLevel,
            int startFloor,
            int highestFloor,
            int gameplayScore,
            int remainingHearts,
            int acquiredGameMoney)
        {
            settings ??= new RunRewardSettings();

            int floorMoveCount = Mathf.Max(0, highestFloor - Mathf.Max(1, startFloor));
            int floorScore = floorMoveCount * settings.FloorScorePerFloor;
            int lifeScore = Mathf.Max(0, remainingHearts) * settings.LifeScorePerHeart;
            int baseTotalScore = Mathf.Max(0, gameplayScore) + floorScore + lifeScore;
            ArtifactModifiers artifactModifiers = ArtifactEffectResolver.Resolve();
            int artifactBonusScore = Mathf.FloorToInt(baseTotalScore * artifactModifiers.ResultScoreBonusPercent * 0.01f);
            int totalScore = baseTotalScore + artifactBonusScore;

            int levelExperience = characterDefinition != null
                ? characterDefinition.GetRunExperienceRewardForLevel(characterLevel)
                : 0;
            int floorExperience = Mathf.Min(
                floorMoveCount * settings.FloorExperiencePerFloor,
                settings.MaximumPerformanceExperiencePerRun);
            int scoreExperience = Mathf.Min(
                Mathf.FloorToInt(baseTotalScore * settings.ScoreExperienceMultiplier),
                Mathf.Max(0, settings.MaximumPerformanceExperiencePerRun - floorExperience));
            int baseTotalExperience = levelExperience + floorExperience + scoreExperience;
            int artifactBonusExperience = Mathf.FloorToInt(baseTotalExperience * artifactModifiers.ResultExperienceBonusPercent * 0.01f);
            int totalExperience = baseTotalExperience + artifactBonusExperience;
            int bonusGameMoney = Mathf.FloorToInt(baseTotalScore * settings.BonusGameMoneyMultiplier);

            return new RunRewardBreakdown(
                gameplayScore,
                floorScore,
                lifeScore,
                artifactBonusScore,
                totalScore,
                levelExperience,
                floorExperience,
                scoreExperience,
                artifactBonusExperience,
                totalExperience,
                acquiredGameMoney,
                bonusGameMoney);
        }
    }
}
