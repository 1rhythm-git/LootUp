using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LootUp.Core.Characters;
using LootUp.Core.Enemies;
using LootUp.Core.Items;
using UnityEditor;
using UnityEngine;

namespace LootUp.Editor.Balance
{
    public enum SimulationSkillProfile
    {
        Beginner,
        Standard,
        Expert
    }

    [Serializable]
    public sealed class LevelBalanceSimulationSettings
    {
        public DifficultyBandDefinition DifficultyBands;
        public TextAsset ItemTableCsv;
        public CharacterDefinition Character;
        public SimulationSkillProfile SkillProfile = SimulationSkillProfile.Standard;
        public int CharacterLevel = 1;
        public int Iterations = 5000;
        public int MaximumFloor = 130;
        public int Seed = 20261020;
        public float InitialTimeSeconds = 90f;
        public float ColumnsPerFloor = 7f;
        public float ElevatorSecondsPerFloor = 1.1f;
        public float ItemsPerFloor = 0.6f;
        public float BaseEnemyMinSpeed = 130f;
        public float BaseEnemyMaxSpeed = 360f;
        public float SpeedStepPerEnemy = 22f;
        public int RisingAdvanceFloors = 10;
    }

    [Serializable]
    public sealed class LevelBalanceRunResult
    {
        public int RunIndex;
        public int HighestFloor;
        public string EndReason;
        public int Hits;
        public int SupportItems;
        public int GameMoney;
        public int RisingActivations;
        public float RemainingTime;
    }

    [Serializable]
    public sealed class LevelBalanceBandResult
    {
        public int StartFloor;
        public int EndFloor;
        public string Role;
        public int EnemyCount;
        public int DangerousLineCount;
        public float EnemySpeedBonus;
        public float SupportItemWeightMultiplier;
        public float PressureIndex;
        public int EnteredRuns;
        public int CompletedRuns;
        public float EnterRatePercent;
        public float ConditionalPassRatePercent;
        public float SupportItemSharePercent;
    }

    [Serializable]
    public sealed class LevelBalanceSimulationResult
    {
        public string CharacterId;
        public string CharacterName;
        public int CharacterLevel;
        public string SkillProfile;
        public int Iterations;
        public int Seed;
        public float AverageHighestFloor;
        public int P10HighestFloor;
        public int MedianHighestFloor;
        public int P90HighestFloor;
        public float LifeDepletedPercent;
        public float TimeOverPercent;
        public float CompletedPercent;
        public float AverageHits;
        public float AverageSupportItems;
        public float AverageGameMoney;
        public float AverageRisingActivations;
        public List<LevelBalanceBandResult> Bands = new List<LevelBalanceBandResult>();
        public List<LevelBalanceRunResult> Runs = new List<LevelBalanceRunResult>();
        public List<string> Warnings = new List<string>();
    }

    internal readonly struct SimulationProfileValues
    {
        public SimulationProfileValues(
            float hazardMultiplier,
            float itemPickupChance,
            float risingPickupChance,
            float floorTimeMultiplier)
        {
            HazardMultiplier = hazardMultiplier;
            ItemPickupChance = itemPickupChance;
            RisingPickupChance = risingPickupChance;
            FloorTimeMultiplier = floorTimeMultiplier;
        }

        public float HazardMultiplier { get; }
        public float ItemPickupChance { get; }
        public float RisingPickupChance { get; }
        public float FloorTimeMultiplier { get; }
    }

    internal sealed class LevelBalanceSimulationEngine
    {
        private const int RisingLetterCount = 6;
        private const float RisingBaseSpawnChance = 0.12f;
        private const float RisingSpawnChancePerMiss = 0.03f;
        private const float RisingMaximumSpawnChance = 0.45f;
        private const float RisingMaximumBoostedSpawnChance = 0.7f;
        private const int RisingGuaranteedAttempt = 8;
        private const float RisingDuplicateLetterWeight = 0.45f;
        private const float RisingDuplicateSpawnBonus = 0.05f;
        private const float RisingMaximumDuplicateSpawnBonus = 0.25f;
        private const float RisingDuplicateTimeBonus = 3f;
        private const float RisingDuplicateFeverBonus = 10f;
        private const float FeverDurationSeconds = 8f;

        public LevelBalanceSimulationResult Run(LevelBalanceSimulationSettings settings)
        {
            Validate(settings);

            ItemTable itemTable = ItemTable.Load(settings.ItemTableCsv);
            SimulationProfileValues profile = ResolveProfile(settings.SkillProfile);
            LevelBalanceSimulationResult result = new LevelBalanceSimulationResult
            {
                CharacterId = settings.Character.CharacterId,
                CharacterName = settings.Character.DisplayName,
                CharacterLevel = Mathf.Clamp(
                    settings.CharacterLevel,
                    1,
                    settings.Character.MaxCharacterLevel),
                SkillProfile = settings.SkillProfile.ToString(),
                Iterations = Mathf.Max(100, settings.Iterations),
                Seed = settings.Seed
            };

            int lifeDepleted = 0;
            int timeOver = 0;
            for (int i = 0; i < result.Iterations; i++)
            {
                System.Random random = new System.Random(
                    unchecked(settings.Seed + i * 486187739));
                LevelBalanceRunResult run = SimulateRun(
                    i,
                    settings,
                    itemTable,
                    profile,
                    random);
                result.Runs.Add(run);
                lifeDepleted += run.EndReason == "LifeDepleted" ? 1 : 0;
                timeOver += run.EndReason == "TimeOver" ? 1 : 0;
            }

            PopulateSummary(result, lifeDepleted, timeOver);
            PopulateBands(result, settings, itemTable);
            PopulateWarnings(result);
            return result;
        }

        private static LevelBalanceRunResult SimulateRun(
            int runIndex,
            LevelBalanceSimulationSettings settings,
            ItemTable itemTable,
            SimulationProfileValues profile,
            System.Random random)
        {
            CharacterDefinition character = settings.Character;
            int maximumFloor = Mathf.Max(10, settings.MaximumFloor);
            int currentFloor = 1;
            int highestFloor = 0;
            int life = Mathf.Max(1, character.MaxLife);
            float remainingTime = Mathf.Max(1f, settings.InitialTimeSeconds);
            float feverGauge = 0f;
            float feverRemaining = 0f;
            int hits = 0;
            int supportItems = 0;
            int gameMoney = 0;
            int risingActivations = 0;
            int risingMisses = 0;
            float risingSpawnBonus = 0f;
            bool[] risingLetters = new bool[RisingLetterCount];

            while (currentFloor <= maximumFloor && life > 0 && remainingTime > 0f)
            {
                if (!settings.DifficultyBands.TryGetValues(
                        currentFloor,
                        out DifficultyBandValues band))
                {
                    break;
                }

                float moveSpeed = Mathf.Max(0.1f, character.MoveSpeedColumnsPerSecond);
                float floorSeconds = (
                    Mathf.Max(1f, settings.ColumnsPerFloor) / moveSpeed
                    + Mathf.Max(0f, settings.ElevatorSecondsPerFloor)
                    + character.PivotCooldownSeconds * 2f)
                    * profile.FloorTimeMultiplier;
                bool feverActive = feverRemaining > 0f;
                if (feverActive)
                {
                    floorSeconds *= 0.82f;
                    feverRemaining = Mathf.Max(0f, feverRemaining - floorSeconds);
                }

                remainingTime -= floorSeconds;
                if (remainingTime <= 0f)
                {
                    break;
                }

                float hitChance = ResolveHitChance(
                    band,
                    character,
                    settings,
                    profile,
                    feverActive);
                if (random.NextDouble() <= hitChance)
                {
                    hits++;
                    life--;
                    feverGauge = 0f;
                    feverRemaining = 0f;
                    if (life <= 0)
                    {
                        highestFloor = Mathf.Max(highestFloor, currentFloor);
                        break;
                    }
                }

                SimulateStandardItems(
                    currentFloor,
                    settings,
                    itemTable,
                    profile,
                    random,
                    ref life,
                    ref remainingTime,
                    ref feverGauge,
                    ref supportItems,
                    ref gameMoney);

                if ((currentFloor - 1) % 10 == 0
                    && random.NextDouble() <= character.ItemChance)
                {
                    SimulateItemChanceReward(
                        currentFloor,
                        itemTable,
                        profile,
                        random,
                        ref remainingTime,
                        ref supportItems);
                }

                feverGauge += Mathf.Max(0f, character.FeverGainPerColumn)
                    * Mathf.Max(1f, settings.ColumnsPerFloor);
                feverGauge += Mathf.Max(0f, character.FeverGainPerPivot) * 2f;
                if (feverGauge >= Mathf.Max(1f, character.FeverGaugeMax))
                {
                    feverGauge = 0f;
                    feverRemaining = FeverDurationSeconds;
                }

                highestFloor = Mathf.Max(highestFloor, currentFloor);

                if (currentFloor >= 2)
                {
                    int attempt = risingMisses + 1;
                    float spawnChance = Mathf.Min(
                        RisingMaximumBoostedSpawnChance,
                        Mathf.Min(
                            RisingMaximumSpawnChance,
                            RisingBaseSpawnChance
                            + risingMisses * RisingSpawnChancePerMiss)
                        + risingSpawnBonus);
                    bool spawned = attempt >= RisingGuaranteedAttempt
                        || random.NextDouble() <= spawnChance;
                    if (spawned)
                    {
                        risingMisses = 0;
                        if (random.NextDouble() <= profile.RisingPickupChance)
                        {
                            bool duplicate = AcquireRisingLetter(
                                risingLetters,
                                random);
                            if (duplicate)
                            {
                                remainingTime += RisingDuplicateTimeBonus;
                                feverGauge += RisingDuplicateFeverBonus;
                                risingSpawnBonus = Mathf.Min(
                                    RisingMaximumDuplicateSpawnBonus,
                                    risingSpawnBonus + RisingDuplicateSpawnBonus);
                            }

                            if (AreAllRisingLettersCollected(risingLetters))
                            {
                                risingActivations++;
                                Array.Clear(risingLetters, 0, risingLetters.Length);
                                risingSpawnBonus = 0f;
                                int advance = Mathf.Max(1, settings.RisingAdvanceFloors);
                                highestFloor = Mathf.Min(
                                    maximumFloor,
                                    currentFloor + advance);
                                currentFloor += advance - 1;
                            }
                        }
                    }
                    else
                    {
                        risingMisses++;
                    }
                }

                currentFloor++;
            }

            return new LevelBalanceRunResult
            {
                RunIndex = runIndex,
                HighestFloor = Mathf.Clamp(highestFloor, 0, maximumFloor),
                EndReason = life <= 0
                    ? "LifeDepleted"
                    : highestFloor >= maximumFloor && remainingTime > 0f
                        ? "Completed"
                        : "TimeOver",
                Hits = hits,
                SupportItems = supportItems,
                GameMoney = gameMoney,
                RisingActivations = risingActivations,
                RemainingTime = Mathf.Max(0f, remainingTime)
            };
        }

        private static void SimulateStandardItems(
            int floor,
            LevelBalanceSimulationSettings settings,
            ItemTable itemTable,
            SimulationProfileValues profile,
            System.Random random,
            ref int life,
            ref float remainingTime,
            ref float feverGauge,
            ref int supportItems,
            ref int gameMoney)
        {
            float expectedAttempts = Mathf.Max(0f, settings.ItemsPerFloor);
            int attempts = Mathf.FloorToInt(expectedAttempts);
            if (random.NextDouble() < expectedAttempts - attempts)
            {
                attempts++;
            }

            for (int i = 0; i < attempts; i++)
            {
                if (random.NextDouble() > profile.ItemPickupChance)
                {
                    continue;
                }

                ItemDefinition item = PickStandardItem(
                    floor,
                    settings.DifficultyBands,
                    itemTable,
                    random);
                ApplyItem(
                    item,
                    settings.Character.MaxLife,
                    random,
                    ref life,
                    ref remainingTime,
                    ref feverGauge,
                    ref supportItems,
                    ref gameMoney);
            }
        }

        private static void SimulateItemChanceReward(
            int floor,
            ItemTable itemTable,
            SimulationProfileValues profile,
            System.Random random,
            ref float remainingTime,
            ref int supportItems)
        {
            if (random.NextDouble() > profile.ItemPickupChance)
            {
                return;
            }

            List<ItemDefinition> candidates = itemTable.GetSpawnCandidates(floor)
                .Where(item => item != null
                    && item.Enabled
                    && item.ItemType != ItemType.Collection
                    && (item.ItemType == ItemType.Time
                        || item.ItemType == ItemType.Skill))
                .ToList();
            ItemDefinition selected = PickWeighted(candidates, random, null);
            if (selected != null && selected.ItemType == ItemType.Time)
            {
                remainingTime += Mathf.Max(0, selected.EffectValue);
                supportItems++;
            }
        }

        private static ItemDefinition PickStandardItem(
            int floor,
            DifficultyBandDefinition difficultyBands,
            ItemTable itemTable,
            System.Random random)
        {
            List<ItemDefinition> candidates = itemTable.GetSpawnCandidates(floor)
                .Where(item => item != null
                    && item.Enabled
                    && item.ItemType != ItemType.Collection)
                .ToList();
            float supportMultiplier = 1f;
            if (difficultyBands.TryGetValues(
                    floor,
                    out DifficultyBandValues values))
            {
                supportMultiplier = values.SupportItemWeightMultiplier;
            }

            return PickWeighted(candidates, random, item =>
                IsSupportItem(item) ? supportMultiplier : 1f);
        }

        private static ItemDefinition PickWeighted(
            List<ItemDefinition> candidates,
            System.Random random,
            Func<ItemDefinition, float> multiplierSelector)
        {
            int totalWeight = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                float multiplier = multiplierSelector != null
                    ? Mathf.Max(0f, multiplierSelector(candidates[i]))
                    : 1f;
                totalWeight += Mathf.Max(
                    0,
                    Mathf.RoundToInt(candidates[i].SpawnWeight * multiplier));
            }

            if (totalWeight <= 0)
            {
                return null;
            }

            int roll = random.Next(0, totalWeight);
            int cursor = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                float multiplier = multiplierSelector != null
                    ? Mathf.Max(0f, multiplierSelector(candidates[i]))
                    : 1f;
                cursor += Mathf.Max(
                    0,
                    Mathf.RoundToInt(candidates[i].SpawnWeight * multiplier));
                if (roll < cursor)
                {
                    return candidates[i];
                }
            }

            return null;
        }

        private static void ApplyItem(
            ItemDefinition item,
            int maximumLife,
            System.Random random,
            ref int life,
            ref float remainingTime,
            ref float feverGauge,
            ref int supportItems,
            ref int gameMoney)
        {
            if (item == null)
            {
                return;
            }

            int passCount = item.ItemType == ItemType.Time
                ? random.Next(1, 6)
                : 1;
            switch (item.ItemType)
            {
                case ItemType.Time:
                    remainingTime += Mathf.Max(0, item.EffectValue) * passCount;
                    supportItems++;
                    break;
                case ItemType.Fever:
                    feverGauge += Mathf.Max(0, item.EffectValue);
                    supportItems++;
                    break;
                case ItemType.Heal:
                    life = Mathf.Min(
                        Mathf.Max(1, maximumLife),
                        life + Mathf.Max(0, item.EffectValue));
                    break;
                case ItemType.Currency:
                    gameMoney += Mathf.Max(0, item.EffectValue);
                    break;
            }
        }

        private static float ResolveHitChance(
            DifficultyBandValues band,
            CharacterDefinition character,
            LevelBalanceSimulationSettings settings,
            SimulationProfileValues profile,
            bool feverActive)
        {
            float averageEnemySequence = Mathf.Max(0, band.EnemyCount - 1) * 0.5f;
            float averageEnemySpeed = (
                settings.BaseEnemyMinSpeed + settings.BaseEnemyMaxSpeed) * 0.5f
                + settings.SpeedStepPerEnemy * averageEnemySequence
                + band.EnemySpeedBonus;
            float speedPressure = Mathf.Max(0f, averageEnemySpeed - 245f) / 245f;
            float baseChance = 0.012f
                + band.EnemyCount * 0.0065f
                + band.DangerousLineCount * 0.014f
                + speedPressure * 0.035f;
            float mobilityMultiplier = Mathf.Clamp(
                2.5f / Mathf.Max(0.1f, character.MoveSpeedColumnsPerSecond),
                0.68f,
                1.3f);
            float feverMultiplier = feverActive ? 0.72f : 1f;
            return Mathf.Clamp01(
                baseChance
                * profile.HazardMultiplier
                * mobilityMultiplier
                * feverMultiplier);
        }

        private static bool AcquireRisingLetter(
            bool[] letters,
            System.Random random)
        {
            float totalWeight = 0f;
            for (int i = 0; i < letters.Length; i++)
            {
                totalWeight += letters[i] ? RisingDuplicateLetterWeight : 1f;
            }

            double roll = random.NextDouble() * totalWeight;
            float cursor = 0f;
            for (int i = 0; i < letters.Length; i++)
            {
                cursor += letters[i] ? RisingDuplicateLetterWeight : 1f;
                if (roll < cursor)
                {
                    bool duplicate = letters[i];
                    letters[i] = true;
                    return duplicate;
                }
            }

            return false;
        }

        private static bool AreAllRisingLettersCollected(bool[] letters)
        {
            for (int i = 0; i < letters.Length; i++)
            {
                if (!letters[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static void PopulateSummary(
            LevelBalanceSimulationResult result,
            int lifeDepleted,
            int timeOver)
        {
            List<int> orderedFloors = result.Runs
                .Select(run => run.HighestFloor)
                .OrderBy(value => value)
                .ToList();
            result.AverageHighestFloor = (float)result.Runs.Average(
                run => run.HighestFloor);
            result.P10HighestFloor = Percentile(orderedFloors, 0.1f);
            result.MedianHighestFloor = Percentile(orderedFloors, 0.5f);
            result.P90HighestFloor = Percentile(orderedFloors, 0.9f);
            result.LifeDepletedPercent = lifeDepleted * 100f / result.Iterations;
            result.TimeOverPercent = timeOver * 100f / result.Iterations;
            int completed = result.Runs.Count(run => run.EndReason == "Completed");
            result.CompletedPercent = completed * 100f / result.Iterations;
            result.AverageHits = (float)result.Runs.Average(run => run.Hits);
            result.AverageSupportItems = (float)result.Runs.Average(
                run => run.SupportItems);
            result.AverageGameMoney = (float)result.Runs.Average(
                run => run.GameMoney);
            result.AverageRisingActivations = (float)result.Runs.Average(
                run => run.RisingActivations);
        }

        private static void PopulateBands(
            LevelBalanceSimulationResult result,
            LevelBalanceSimulationSettings settings,
            ItemTable itemTable)
        {
            int floorsPerBand = settings.DifficultyBands.FloorsPerBand;
            int bandCount = Mathf.CeilToInt(
                Mathf.Max(10, settings.MaximumFloor) / (float)floorsPerBand);
            for (int i = 0; i < bandCount; i++)
            {
                int startFloor = i * floorsPerBand + 1;
                int endFloor = Mathf.Min(
                    settings.MaximumFloor,
                    startFloor + floorsPerBand - 1);
                if (!settings.DifficultyBands.TryGetValues(
                        startFloor,
                        out DifficultyBandValues values))
                {
                    continue;
                }

                int entered = result.Runs.Count(run => run.HighestFloor >= startFloor);
                int completed = result.Runs.Count(run => run.HighestFloor >= endFloor);
                result.Bands.Add(new LevelBalanceBandResult
                {
                    StartFloor = startFloor,
                    EndFloor = endFloor,
                    Role = values.Role,
                    EnemyCount = values.EnemyCount,
                    DangerousLineCount = values.DangerousLineCount,
                    EnemySpeedBonus = values.EnemySpeedBonus,
                    SupportItemWeightMultiplier = values.SupportItemWeightMultiplier,
                    PressureIndex = ResolvePressureIndex(values),
                    EnteredRuns = entered,
                    CompletedRuns = completed,
                    EnterRatePercent = entered * 100f / result.Iterations,
                    ConditionalPassRatePercent = entered > 0
                        ? completed * 100f / entered
                        : 0f,
                    SupportItemSharePercent = ResolveSupportItemShare(
                        startFloor,
                        values.SupportItemWeightMultiplier,
                        itemTable)
                });
            }
        }

        private static void PopulateWarnings(LevelBalanceSimulationResult result)
        {
            int minimumReliableEntries = Mathf.Max(
                30,
                Mathf.CeilToInt(result.Iterations * 0.01f));
            for (int i = 1; i < result.Bands.Count; i++)
            {
                LevelBalanceBandResult previous = result.Bands[i - 1];
                LevelBalanceBandResult current = result.Bands[i];
                if (previous.EnteredRuns < minimumReliableEntries
                    || current.EnteredRuns < minimumReliableEntries)
                {
                    continue;
                }

                float drop = previous.ConditionalPassRatePercent
                    - current.ConditionalPassRatePercent;
                if (drop >= 30f)
                {
                    result.Warnings.Add(
                        $"{current.StartFloor}~{current.EndFloor}F 구간 통과율이 직전 구간보다 {drop:0.0}%p 급락했습니다.");
                }

                if (IsRecoveryRole(current.Role)
                    && current.ConditionalPassRatePercent
                    <= previous.ConditionalPassRatePercent)
                {
                    result.Warnings.Add(
                        $"{current.StartFloor}~{current.EndFloor}F 회복 구간의 조건부 통과율이 직전 피크보다 높지 않습니다.");
                }
            }

            if (result.MedianHighestFloor < 50)
            {
                result.Warnings.Add(
                    "P50 최고 층이 50F 미만입니다. Lv.1 무강화 기준 하드월 여부를 실제 플레이로 확인해야 합니다.");
            }
        }

        private static bool IsRecoveryRole(string role)
        {
            return !string.IsNullOrWhiteSpace(role)
                && (role.IndexOf("Recovery", StringComparison.OrdinalIgnoreCase) >= 0
                    || role.IndexOf("회복", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static float ResolvePressureIndex(DifficultyBandValues values)
        {
            float enemy = Mathf.Clamp01(values.EnemyCount / 7f);
            float dangerous = Mathf.Clamp01(values.DangerousLineCount / 4f);
            float speed = Mathf.Clamp01(values.EnemySpeedBonus / 96f);
            return (enemy * 0.4f + dangerous * 0.3f + speed * 0.3f) * 100f;
        }

        private static float ResolveSupportItemShare(
            int floor,
            float supportMultiplier,
            ItemTable itemTable)
        {
            List<ItemDefinition> candidates = itemTable.GetSpawnCandidates(floor);
            int supportWeight = 0;
            int totalWeight = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                ItemDefinition item = candidates[i];
                if (item == null || !item.Enabled || item.ItemType == ItemType.Collection)
                {
                    continue;
                }

                int weight = Mathf.Max(
                    0,
                    Mathf.RoundToInt(
                        item.SpawnWeight
                        * (IsSupportItem(item) ? supportMultiplier : 1f)));
                totalWeight += weight;
                if (IsSupportItem(item))
                {
                    supportWeight += weight;
                }
            }

            return totalWeight > 0 ? supportWeight * 100f / totalWeight : 0f;
        }

        private static bool IsSupportItem(ItemDefinition item)
        {
            return item != null
                && (item.ItemType == ItemType.Time
                    || item.ItemType == ItemType.Fever);
        }

        private static int Percentile(List<int> sorted, float normalized)
        {
            if (sorted == null || sorted.Count == 0)
            {
                return 0;
            }

            int index = Mathf.Clamp(
                Mathf.RoundToInt((sorted.Count - 1) * Mathf.Clamp01(normalized)),
                0,
                sorted.Count - 1);
            return sorted[index];
        }

        private static SimulationProfileValues ResolveProfile(
            SimulationSkillProfile profile)
        {
            switch (profile)
            {
                case SimulationSkillProfile.Beginner:
                    return new SimulationProfileValues(1.22f, 0.58f, 0.62f, 1.18f);
                case SimulationSkillProfile.Expert:
                    return new SimulationProfileValues(0.68f, 0.9f, 0.92f, 0.86f);
                default:
                    return new SimulationProfileValues(0.92f, 0.76f, 0.8f, 1f);
            }
        }

        private static void Validate(LevelBalanceSimulationSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (settings.DifficultyBands == null)
            {
                throw new InvalidOperationException("DifficultyBandDefinition이 필요합니다.");
            }

            if (settings.ItemTableCsv == null)
            {
                throw new InvalidOperationException("Items.csv TextAsset이 필요합니다.");
            }

            if (settings.Character == null)
            {
                throw new InvalidOperationException("CharacterDefinition이 필요합니다.");
            }
        }
    }

    internal static class LevelBalanceReportExporter
    {
        [Serializable]
        private sealed class ExportConfiguration
        {
            public string generatedAt;
            public string difficultyAsset;
            public string itemTableAsset;
            public string skillProfile;
            public int characterLevel;
            public int iterations;
            public int maximumFloor;
            public int seed;
            public float initialTimeSeconds;
            public float columnsPerFloor;
            public float elevatorSecondsPerFloor;
            public float itemsPerFloor;
            public float baseEnemyMinSpeed;
            public float baseEnemyMaxSpeed;
            public float speedStepPerEnemy;
            public int risingAdvanceFloors;
        }

        public static string Export(
            LevelBalanceSimulationSettings settings,
            IReadOnlyList<LevelBalanceSimulationResult> results)
        {
            if (settings == null || results == null || results.Count == 0)
            {
                throw new InvalidOperationException("내보낼 시뮬레이션 결과가 없습니다.");
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? Application.dataPath;
            string folderName = DateTime.Now.ToString(
                "yyyy-MM-dd_HHmmss",
                CultureInfo.InvariantCulture);
            string outputDirectory = Path.Combine(
                projectRoot,
                "Docs",
                "BalanceReports",
                folderName);
            Directory.CreateDirectory(outputDirectory);

            WriteConfiguration(settings, outputDirectory);
            WriteRunResults(results, outputDirectory);
            WriteBandSummary(results, outputDirectory);
            WriteSummary(settings, results, outputDirectory);
            WriteSvg(results, outputDirectory);
            AssetDatabase.Refresh();
            return outputDirectory;
        }

        private static void WriteConfiguration(
            LevelBalanceSimulationSettings settings,
            string outputDirectory)
        {
            ExportConfiguration configuration = new ExportConfiguration
            {
                generatedAt = DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
                difficultyAsset = AssetDatabase.GetAssetPath(settings.DifficultyBands),
                itemTableAsset = AssetDatabase.GetAssetPath(settings.ItemTableCsv),
                skillProfile = settings.SkillProfile.ToString(),
                characterLevel = settings.CharacterLevel,
                iterations = settings.Iterations,
                maximumFloor = settings.MaximumFloor,
                seed = settings.Seed,
                initialTimeSeconds = settings.InitialTimeSeconds,
                columnsPerFloor = settings.ColumnsPerFloor,
                elevatorSecondsPerFloor = settings.ElevatorSecondsPerFloor,
                itemsPerFloor = settings.ItemsPerFloor,
                baseEnemyMinSpeed = settings.BaseEnemyMinSpeed,
                baseEnemyMaxSpeed = settings.BaseEnemyMaxSpeed,
                speedStepPerEnemy = settings.SpeedStepPerEnemy,
                risingAdvanceFloors = settings.RisingAdvanceFloors
            };
            File.WriteAllText(
                Path.Combine(outputDirectory, "configuration.json"),
                JsonUtility.ToJson(configuration, true),
                new UTF8Encoding(false));
        }

        private static void WriteRunResults(
            IReadOnlyList<LevelBalanceSimulationResult> results,
            string outputDirectory)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(
                "CharacterId,CharacterName,Level,SkillProfile,RunIndex,HighestFloor,EndReason,Hits,SupportItems,GameMoney,RisingActivations,RemainingTime");
            for (int i = 0; i < results.Count; i++)
            {
                LevelBalanceSimulationResult result = results[i];
                for (int j = 0; j < result.Runs.Count; j++)
                {
                    LevelBalanceRunResult run = result.Runs[j];
                    builder.Append(Csv(result.CharacterId)).Append(',')
                        .Append(Csv(result.CharacterName)).Append(',')
                        .Append(result.CharacterLevel).Append(',')
                        .Append(Csv(result.SkillProfile)).Append(',')
                        .Append(run.RunIndex).Append(',')
                        .Append(run.HighestFloor).Append(',')
                        .Append(run.EndReason).Append(',')
                        .Append(run.Hits).Append(',')
                        .Append(run.SupportItems).Append(',')
                        .Append(run.GameMoney).Append(',')
                        .Append(run.RisingActivations).Append(',')
                        .AppendLine(run.RemainingTime.ToString("0.###", CultureInfo.InvariantCulture));
                }
            }

            File.WriteAllText(
                Path.Combine(outputDirectory, "run-results.csv"),
                builder.ToString(),
                new UTF8Encoding(false));
        }

        private static void WriteBandSummary(
            IReadOnlyList<LevelBalanceSimulationResult> results,
            string outputDirectory)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(
                "CharacterId,Level,SkillProfile,StartFloor,EndFloor,Role,EnemyCount,DangerousLineCount,EnemySpeedBonus,SupportMultiplier,PressureIndex,EnteredRuns,CompletedRuns,EnterRatePercent,ConditionalPassRatePercent,SupportItemSharePercent");
            for (int i = 0; i < results.Count; i++)
            {
                LevelBalanceSimulationResult result = results[i];
                for (int j = 0; j < result.Bands.Count; j++)
                {
                    LevelBalanceBandResult band = result.Bands[j];
                    builder.Append(Csv(result.CharacterId)).Append(',')
                        .Append(result.CharacterLevel).Append(',')
                        .Append(Csv(result.SkillProfile)).Append(',')
                        .Append(band.StartFloor).Append(',')
                        .Append(band.EndFloor).Append(',')
                        .Append(Csv(band.Role)).Append(',')
                        .Append(band.EnemyCount).Append(',')
                        .Append(band.DangerousLineCount).Append(',')
                        .Append(Number(band.EnemySpeedBonus)).Append(',')
                        .Append(Number(band.SupportItemWeightMultiplier)).Append(',')
                        .Append(Number(band.PressureIndex)).Append(',')
                        .Append(band.EnteredRuns).Append(',')
                        .Append(band.CompletedRuns).Append(',')
                        .Append(Number(band.EnterRatePercent)).Append(',')
                        .Append(Number(band.ConditionalPassRatePercent)).Append(',')
                        .AppendLine(Number(band.SupportItemSharePercent));
                }
            }

            File.WriteAllText(
                Path.Combine(outputDirectory, "band-summary.csv"),
                builder.ToString(),
                new UTF8Encoding(false));
        }

        private static void WriteSummary(
            LevelBalanceSimulationSettings settings,
            IReadOnlyList<LevelBalanceSimulationResult> results,
            string outputDirectory)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# LootUp 레벨 밸런스 시뮬레이션");
            builder.AppendLine();
            builder.AppendLine($"- 생성 시각: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"- 반복 횟수: 캐릭터당 {settings.Iterations:N0}회");
            builder.AppendLine($"- 최대 층: {settings.MaximumFloor}F");
            builder.AppendLine($"- Seed: {settings.Seed}");
            builder.AppendLine($"- 숙련도 가정: {settings.SkillProfile}");
            builder.AppendLine();
            builder.AppendLine("> 이 결과는 실제 데이터와 명시된 숙련도 가정을 결합한 확률 모델입니다. 충돌 체감과 기기 성능을 대체하지 않습니다.");
            builder.AppendLine();
            builder.AppendLine("## 요약");
            builder.AppendLine();
            builder.AppendLine("| 캐릭터 | 레벨 | 평균 최고 층 | P10 | P50 | P90 | HP 종료 | 시간 종료 | 완주 | 평균 지원 아이템 | 평균 GameMoney | 평균 RISING |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            for (int i = 0; i < results.Count; i++)
            {
                LevelBalanceSimulationResult result = results[i];
                builder.AppendLine(
                    $"| {result.CharacterName} | {result.CharacterLevel} | {result.AverageHighestFloor:0.0} | {result.P10HighestFloor} | {result.MedianHighestFloor} | {result.P90HighestFloor} | {result.LifeDepletedPercent:0.0}% | {result.TimeOverPercent:0.0}% | {result.CompletedPercent:0.0}% | {result.AverageSupportItems:0.00} | {result.AverageGameMoney:0.00} | {result.AverageRisingActivations:0.00} |");
            }

            builder.AppendLine();
            builder.AppendLine("## 자동 경고");
            builder.AppendLine();
            bool hasWarning = false;
            for (int i = 0; i < results.Count; i++)
            {
                LevelBalanceSimulationResult result = results[i];
                for (int j = 0; j < result.Warnings.Count; j++)
                {
                    hasWarning = true;
                    builder.AppendLine(
                        $"- [{result.CharacterName}] {result.Warnings[j]}");
                }
            }

            if (!hasWarning)
            {
                builder.AppendLine("- 자동 경고 없음");
            }

            builder.AppendLine();
            builder.AppendLine("## Codex 분석 요청 기준");
            builder.AppendLine();
            builder.AppendLine("- `configuration.json`으로 사용 데이터와 가정을 확인한다.");
            builder.AppendLine("- `band-summary.csv`에서 하드월, 회복 구간과 캐릭터 격차를 비교한다.");
            builder.AppendLine("- `run-results.csv`에서 사망 원인과 보상 분포의 P10·P50·P90을 재검산한다.");
            builder.AppendLine("- 수정안은 Enemy, Red Line, 속도, 지원 아이템 배율 순으로 제안하고 사용자 승인 전 데이터 에셋을 변경하지 않는다.");
            File.WriteAllText(
                Path.Combine(outputDirectory, "summary.md"),
                builder.ToString(),
                new UTF8Encoding(false));
        }

        private static void WriteSvg(
            IReadOnlyList<LevelBalanceSimulationResult> results,
            string outputDirectory)
        {
            const int width = 1200;
            const int height = 620;
            const float left = 90f;
            const float top = 100f;
            const float chartWidth = 1040f;
            const float chartHeight = 400f;
            string[] colors = { "#2878D0", "#138A72", "#E2A21A", "#C85B3C" };
            int maximumFloor = results.Max(result =>
                result.Bands.Count > 0 ? result.Bands[result.Bands.Count - 1].EndFloor : 130);

            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">");
            builder.AppendLine("<rect width=\"1200\" height=\"620\" fill=\"#F5F7FA\"/>");
            builder.AppendLine("<text x=\"60\" y=\"52\" font-family=\"Arial, sans-serif\" font-size=\"26\" font-weight=\"700\" fill=\"#17202A\">LOOTUP SURVIVAL CURVE</text>");
            builder.AppendLine("<text x=\"60\" y=\"78\" font-family=\"Arial, sans-serif\" font-size=\"13\" fill=\"#5B6573\">10층 구간 진입률 · 높을수록 해당 구간까지 생존</text>");
            builder.AppendLine($"<rect x=\"{left}\" y=\"{top}\" width=\"{chartWidth}\" height=\"{chartHeight}\" rx=\"8\" fill=\"#FFFFFF\" stroke=\"#D8DEE7\"/>");
            for (int tick = 0; tick <= 4; tick++)
            {
                float y = top + chartHeight - chartHeight * tick / 4f;
                builder.AppendLine($"<line x1=\"{left}\" y1=\"{y:0.##}\" x2=\"{left + chartWidth}\" y2=\"{y:0.##}\" stroke=\"#E7EBF0\"/>");
                builder.AppendLine($"<text x=\"{left - 14}\" y=\"{y + 4:0.##}\" text-anchor=\"end\" font-family=\"Arial, sans-serif\" font-size=\"11\" fill=\"#5B6573\">{tick * 25}%</text>");
            }

            for (int i = 0; i < results.Count; i++)
            {
                LevelBalanceSimulationResult result = results[i];
                StringBuilder points = new StringBuilder();
                for (int j = 0; j < result.Bands.Count; j++)
                {
                    LevelBalanceBandResult band = result.Bands[j];
                    float x = left + chartWidth * (band.StartFloor - 1f) / Mathf.Max(1, maximumFloor - 1f);
                    float y = top + chartHeight * (1f - Mathf.Clamp01(band.EnterRatePercent / 100f));
                    points.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "{0:0.##},{1:0.##} ",
                        x,
                        y);
                }

                string color = colors[i % colors.Length];
                builder.AppendLine($"<polyline points=\"{points}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"4\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
                float legendX = 90f + i * 250f;
                builder.AppendLine($"<line x1=\"{legendX}\" y1=\"550\" x2=\"{legendX + 30}\" y2=\"550\" stroke=\"{color}\" stroke-width=\"4\"/>");
                builder.AppendLine($"<text x=\"{legendX + 40}\" y=\"555\" font-family=\"Arial, sans-serif\" font-size=\"13\" fill=\"#17202A\">{Escape(result.CharacterName)} · P50 {result.MedianHighestFloor}F</text>");
            }

            builder.AppendLine("</svg>");
            File.WriteAllText(
                Path.Combine(outputDirectory, "survival-curve.svg"),
                builder.ToString(),
                new UTF8Encoding(false));
        }

        private static string Csv(string value)
        {
            string normalized = value ?? string.Empty;
            return $"\"{normalized.Replace("\"", "\"\"")}\"";
        }

        private static string Number(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }
    }
}
