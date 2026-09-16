using System;
using System.Collections.Generic;
using LootUp.Core.Characters;
using LootUp.Core.Currency;
using LootUp.Core.Items;
using LootUp.Core.Profile;

namespace LootUp.Core.Game
{
    public readonly struct RunResultContext
    {
        public RunResultContext(
            GameOverReason gameOverReason,
            int highestFloor,
            int startFloor,
            int gameplayScore,
            float remainingSeconds,
            int remainingHearts,
            int acquiredGameMoney,
            CharacterDefinition characterDefinition,
            int characterLevel,
            IReadOnlyList<ItemRunEvent> acquiredItemEvents)
        {
            GameOverReason = gameOverReason;
            HighestFloor = highestFloor;
            StartFloor = startFloor;
            GameplayScore = gameplayScore;
            RemainingSeconds = remainingSeconds;
            RemainingHearts = remainingHearts;
            AcquiredGameMoney = acquiredGameMoney;
            CharacterDefinition = characterDefinition;
            CharacterLevel = characterLevel;
            AcquiredItemEvents = acquiredItemEvents;
        }

        public GameOverReason GameOverReason { get; }
        public int HighestFloor { get; }
        public int StartFloor { get; }
        public int GameplayScore { get; }
        public float RemainingSeconds { get; }
        public int RemainingHearts { get; }
        public int AcquiredGameMoney { get; }
        public CharacterDefinition CharacterDefinition { get; }
        public int CharacterLevel { get; }
        public IReadOnlyList<ItemRunEvent> AcquiredItemEvents { get; }
    }

    public sealed class RunResultService
    {
        private readonly RunRewardSettings rewardSettings;
        private readonly string runId = Guid.NewGuid().ToString("N");
        private bool rewardsSettled;

        public RunResultService(RunRewardSettings rewardSettings)
        {
            this.rewardSettings = rewardSettings ?? new RunRewardSettings();
        }

        // (추가) 런타임 상태 수집과 결과 계산의 경계를 명확히 분리한다.
        public RunResultData CreateResult(RunResultContext context)
        {
            RunRewardBreakdown rewards = RunRewardCalculator.Calculate(
                rewardSettings,
                context.CharacterDefinition,
                context.CharacterLevel,
                context.StartFloor,
                context.HighestFloor,
                context.GameplayScore,
                context.RemainingHearts,
                context.AcquiredGameMoney);

            return new RunResultData(
                context.GameOverReason,
                context.HighestFloor,
                rewards,
                context.CharacterDefinition != null ? context.CharacterDefinition.CharacterId : string.Empty,
                context.CharacterLevel,
                context.RemainingSeconds,
                context.RemainingHearts,
                context.AcquiredItemEvents);
        }

        // (추가) 동일한 런 결과의 보상은 여러 번 요청돼도 한 번만 반영한다.
        public bool TrySettleRewards(RunResultData resultData, CharacterDefinition characterDefinition)
        {
            if (rewardsSettled || resultData == null)
            {
                return false;
            }

            rewardsSettled = true;

            UserProfileManager.TrySetBestRun(
                resultData.HighestFloor,
                resultData.Score,
                resultData.CharacterId,
                resultData.CharacterLevel);

            if (resultData.TotalGameMoney > 0)
            {
                _ = CurrencyLedgerManager.AddCurrencyAsync(
                    UserCurrencyType.GameMoney,
                    resultData.TotalGameMoney,
                    $"run:{runId}:game-money",
                    "run_reward",
                    runId);
            }

            if (characterDefinition != null && resultData.TotalExperience > 0)
            {
                _ = CharacterProgressionManager.AddExperienceAsync(
                    characterDefinition,
                    resultData.TotalExperience,
                    $"run:{runId}:character-xp:{characterDefinition.CharacterId}",
                    "run_reward",
                    runId);
            }

            return true;
        }
    }
}
