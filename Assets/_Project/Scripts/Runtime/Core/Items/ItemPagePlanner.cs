using System.Collections.Generic;
using LootUp.Core.Characters;
using LootUp.Core.Enemies;
using LootUp.Core.Player;
using LootUp.Core.Profile;
using LootUp.Core.World;
using UnityEngine;

namespace LootUp.Core.Items
{
    public readonly struct ItemSpawnPlan
    {
        public ItemSpawnPlan(
            ItemDefinition definition,
            FloorAddress address,
            int column,
            int runtimePassCount,
            int scoreBonusPercent)
        {
            Definition = definition;
            Address = address;
            Column = column;
            RuntimePassCount = runtimePassCount;
            ScoreBonusPercent = scoreBonusPercent;
        }

        public ItemDefinition Definition { get; }
        public FloorAddress Address { get; }
        public int Column { get; }
        public int RuntimePassCount { get; }
        public int ScoreBonusPercent { get; }
    }

    public sealed class ItemSpawnPolicy
    {
        private readonly ItemTable itemTable;
        private readonly RunItemEventRecorder eventRecorder;
        private readonly PlayerSpawner playerSpawner;
        private readonly string forcedTestItemId;
        private readonly int guaranteedGoldenCupPageNumber;
        private readonly string guaranteedGoldenCupItemId;
        private readonly int randomPassCountMin;
        private readonly int randomPassCountMax;
        private readonly int speedItemPassCountMin;
        private readonly int speedItemPassCountMax;
        private readonly int scoreBonusPercentPerExtraPass;
        private readonly DifficultyBandDefinition difficultyBands;

        public ItemSpawnPolicy(
            ItemTable itemTable,
            RunItemEventRecorder eventRecorder,
            PlayerSpawner playerSpawner,
            string forcedTestItemId,
            int guaranteedGoldenCupPageNumber,
            string guaranteedGoldenCupItemId,
            int randomPassCountMin,
            int randomPassCountMax,
            int speedItemPassCountMin,
            int speedItemPassCountMax,
            int scoreBonusPercentPerExtraPass,
            DifficultyBandDefinition difficultyBands)
        {
            this.itemTable = itemTable;
            this.eventRecorder = eventRecorder;
            this.playerSpawner = playerSpawner;
            this.forcedTestItemId = forcedTestItemId;
            this.guaranteedGoldenCupPageNumber = guaranteedGoldenCupPageNumber;
            this.guaranteedGoldenCupItemId = guaranteedGoldenCupItemId;
            this.randomPassCountMin = randomPassCountMin;
            this.randomPassCountMax = randomPassCountMax;
            this.speedItemPassCountMin = speedItemPassCountMin;
            this.speedItemPassCountMax = speedItemPassCountMax;
            this.scoreBonusPercentPerExtraPass = scoreBonusPercentPerExtraPass;
            this.difficultyBands = difficultyBands;
        }

        public ItemDefinition GetGuaranteedGoldenCup(FloorPageData pageData)
        {
            if (pageData == null
                || pageData.PageIndex + 1 != Mathf.Max(1, guaranteedGoldenCupPageNumber)
                || string.IsNullOrWhiteSpace(guaranteedGoldenCupItemId)
                || !itemTable.TryGet(guaranteedGoldenCupItemId, out ItemDefinition goldenCup)
                || !CanSpawnForPlayer(goldenCup))
            {
                return null;
            }

            return goldenCup;
        }

        public ItemDefinition PickStandardItem(int absoluteFloor, System.Random random)
        {
            if (!string.IsNullOrWhiteSpace(forcedTestItemId)
                && itemTable.TryGet(forcedTestItemId, out ItemDefinition forcedItem))
            {
                return forcedItem.CanSpawnAtFloor(absoluteFloor) && CanSpawnForPlayer(forcedItem)
                    ? forcedItem
                    : null;
            }

            List<ItemDefinition> candidates = itemTable.GetSpawnCandidates(absoluteFloor);
            int totalWeight = 0;

            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].ItemType != ItemType.Collection && CanSpawnForPlayer(candidates[i]))
                {
                    totalWeight += GetStandardSpawnWeight(candidates[i], absoluteFloor);
                }
            }

            if (totalWeight <= 0)
            {
                return null;
            }

            int roll = random.Next(0, totalWeight);
            int cursor = 0;

            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].ItemType == ItemType.Collection || !CanSpawnForPlayer(candidates[i]))
                {
                    continue;
                }

                cursor += GetStandardSpawnWeight(candidates[i], absoluteFloor);
                if (roll < cursor)
                {
                    return candidates[i];
                }
            }

            return null;
        }

        private int GetStandardSpawnWeight(
            ItemDefinition definition,
            int absoluteFloor)
        {
            int baseWeight = definition != null
                ? Mathf.Max(0, definition.SpawnWeight)
                : 0;
            if (baseWeight == 0 || !IsSupportItem(definition))
            {
                return baseWeight;
            }

            float multiplier = 1f;
            if (difficultyBands != null
                && difficultyBands.TryGetValues(
                    absoluteFloor,
                    out DifficultyBandValues values))
            {
                multiplier = values.SupportItemWeightMultiplier;
            }

            return Mathf.Max(0, Mathf.RoundToInt(baseWeight * multiplier));
        }

        private static bool IsSupportItem(ItemDefinition definition)
        {
            return definition != null
                && (definition.ItemType == ItemType.Time
                    || definition.ItemType == ItemType.Fever);
        }

        // (추가) 수집형 아이템의 페이지 단위 출현 정책을 Spawner에서 분리한다.
        public ItemDefinition TryPickCollectionItem(
            int absoluteFloor,
            System.Random random,
            HashSet<string> spawnedCollectionIds)
        {
            if (!string.IsNullOrWhiteSpace(forcedTestItemId))
            {
                return null;
            }

            List<ItemDefinition> candidates = itemTable.GetSpawnCandidates(absoluteFloor);
            Dictionary<CollectionItemType, List<ItemDefinition>> candidatesByType =
                new Dictionary<CollectionItemType, List<ItemDefinition>>();
            Dictionary<CollectionItemType, float> chanceByType =
                new Dictionary<CollectionItemType, float>();
            float totalChance = 0f;

            for (int i = 0; i < candidates.Count; i++)
            {
                ItemDefinition candidate = candidates[i];
                if (candidate.ItemType != ItemType.Collection
                    || !CanSpawnForPlayer(candidate)
                    || spawnedCollectionIds.Contains(candidate.CollectionId))
                {
                    continue;
                }

                float candidateChance = candidate.GetCollectionSpawnChance(
                    absoluteFloor,
                    GetPlayerCollectionChanceBonusPercent(candidate.CollectionItemType));
                if (candidateChance <= 0f)
                {
                    continue;
                }

                if (!candidatesByType.TryGetValue(
                    candidate.CollectionItemType,
                    out List<ItemDefinition> typedCandidates))
                {
                    typedCandidates = new List<ItemDefinition>();
                    candidatesByType.Add(candidate.CollectionItemType, typedCandidates);
                }

                typedCandidates.Add(candidate);
                if (!chanceByType.TryGetValue(candidate.CollectionItemType, out float currentChance)
                    || candidateChance > currentChance)
                {
                    chanceByType[candidate.CollectionItemType] = candidateChance;
                }
            }

            foreach (KeyValuePair<CollectionItemType, float> chanceEntry in chanceByType)
            {
                totalChance += Mathf.Min(
                    chanceEntry.Value,
                    GetCollectionPageChanceCap(chanceEntry.Key));
            }

            if (totalChance <= 0f || random.NextDouble() >= Mathf.Clamp01(totalChance))
            {
                return null;
            }

            double selection = random.NextDouble() * totalChance;
            float cursor = 0f;
            foreach (KeyValuePair<CollectionItemType, float> chanceEntry in chanceByType)
            {
                cursor += Mathf.Min(
                    chanceEntry.Value,
                    GetCollectionPageChanceCap(chanceEntry.Key));
                if (selection >= cursor
                    || !candidatesByType.TryGetValue(
                        chanceEntry.Key,
                        out List<ItemDefinition> typedCandidates)
                    || typedCandidates.Count == 0)
                {
                    continue;
                }

                return typedCandidates[random.Next(0, typedCandidates.Count)];
            }

            return null;
        }

        public ItemDefinition PickItemChanceReward(int absoluteFloor, System.Random random)
        {
            List<ItemDefinition> candidates = itemTable.GetSpawnCandidates(absoluteFloor);
            int totalWeight = 0;

            for (int i = 0; i < candidates.Count; i++)
            {
                if (IsItemChanceReward(candidates[i]) && CanSpawnForPlayer(candidates[i]))
                {
                    totalWeight += Mathf.Max(0, candidates[i].SpawnWeight);
                }
            }

            if (totalWeight <= 0)
            {
                return null;
            }

            int roll = random.Next(0, totalWeight);
            int cursor = 0;

            for (int i = 0; i < candidates.Count; i++)
            {
                ItemDefinition candidate = candidates[i];
                if (!IsItemChanceReward(candidate) || !CanSpawnForPlayer(candidate))
                {
                    continue;
                }

                cursor += Mathf.Max(0, candidate.SpawnWeight);
                if (roll < cursor)
                {
                    return candidate;
                }
            }

            return null;
        }

        public bool RollItemChance(System.Random random)
        {
            if (!string.IsNullOrWhiteSpace(forcedTestItemId))
            {
                return false;
            }

            CharacterDefinition definition = CharacterSelectionState.SelectedCharacter;
            if (definition == null && playerSpawner != null && playerSpawner.SpawnedPlayer != null)
            {
                PlayerCharacterRuntime runtime =
                    playerSpawner.SpawnedPlayer.GetComponent<PlayerCharacterRuntime>();
                definition = runtime != null ? runtime.CharacterDefinition : null;
            }

            float chance = CharacterProgressionState.GetItemChance(definition);
            return chance > 0f && random.NextDouble() <= chance;
        }

        public bool IsItemChanceReward(ItemDefinition definition)
        {
            return definition != null
                && (definition.ItemType == ItemType.Time || definition.ItemType == ItemType.Skill);
        }

        public int ResolveRuntimePassCount(ItemDefinition definition, System.Random random)
        {
            if (definition == null)
            {
                return 1;
            }

            if (IsMoveSpeedItem(definition))
            {
                return RollPassCount(random, speedItemPassCountMin, speedItemPassCountMax);
            }

            if (IsFeverGaugeItem(definition))
            {
                return random.Next(0, 3) * 2 + 1;
            }

            if (ShouldRandomizePassCount(definition))
            {
                return RollPassCount(random, randomPassCountMin, randomPassCountMax);
            }

            return Mathf.Max(1, definition.RequiredPassCount);
        }

        public int ResolveScoreBonusPercent(ItemDefinition definition, int passCount)
        {
            if (definition == null || !IsScoreRelated(definition))
            {
                return 0;
            }

            return Mathf.Max(0, passCount - 1)
                * Mathf.Max(0, scoreBonusPercentPerExtraPass);
        }

        private static float GetCollectionPageChanceCap(CollectionItemType collectionItemType)
        {
            return collectionItemType == CollectionItemType.Artifact ? 0.005f : 1f;
        }

        private bool CanSpawnForPlayer(ItemDefinition definition)
        {
            if (definition == null
                || (eventRecorder != null && eventRecorder.HasReachedAcquireLimit(definition)))
            {
                return false;
            }

            return definition.ItemType != ItemType.Collection
                || !ItemCollectionManager.HasReachedOwnedLimit(definition);
        }

        private float GetPlayerCollectionChanceBonusPercent(
            CollectionItemType collectionItemType)
        {
            float userTraitBonusPercent =
                UserProfileManager.GetCollectionTraitChanceBonusPercent();
            if (collectionItemType == CollectionItemType.Artifact)
            {
                userTraitBonusPercent += UserProfileManager.GetArtifactChanceBonusPercent();
            }
            else if (collectionItemType == CollectionItemType.CharacterCoin)
            {
                userTraitBonusPercent += UserProfileManager.GetCharacterCoinChanceBonusPercent();
                userTraitBonusPercent +=
                    ArtifactEffectResolver.Resolve().CharacterCoinChanceBonusPercent;
            }

            CharacterDefinition definition = CharacterSelectionState.SelectedCharacter;
            if (playerSpawner != null && playerSpawner.SpawnedPlayer != null)
            {
                PlayerCharacterRuntime runtime =
                    playerSpawner.SpawnedPlayer.GetComponent<PlayerCharacterRuntime>();
                if (runtime != null)
                {
                    return runtime.CollectionItemChanceBonusPercent + userTraitBonusPercent;
                }
            }

            if (definition == null)
            {
                return userTraitBonusPercent;
            }

            CharacterUpgradeModifiers modifiers = CharacterUpgradeResolver.Resolve(definition);
            return definition.CollectionItemChanceBonusPercent
                + modifiers.CollectionItemChanceBonusPercent
                + userTraitBonusPercent;
        }

        private static bool ShouldRandomizePassCount(ItemDefinition definition)
        {
            return definition != null
                && (definition.ItemType == ItemType.Time || IsScoreRelated(definition));
        }

        private static bool IsMoveSpeedItem(ItemDefinition definition)
        {
            return definition != null
                && definition.EffectKey == ItemEffectKeys.AddMoveSpeedPercent;
        }

        private static bool IsFeverGaugeItem(ItemDefinition definition)
        {
            return definition != null
                && (definition.ItemType == ItemType.Fever
                    || definition.EffectKey == ItemEffectKeys.AddFeverGauge);
        }

        private static int RollPassCount(System.Random random, int minimum, int maximum)
        {
            int min = Mathf.Max(1, Mathf.Min(minimum, maximum));
            int max = Mathf.Max(min, Mathf.Max(minimum, maximum));
            return random.Next(min, max + 1);
        }

        private static bool IsScoreRelated(ItemDefinition definition)
        {
            return definition != null
                && (definition.ItemType == ItemType.Score
                    || definition.EffectKey == ItemEffectKeys.AddScore
                    || definition.AffectsScore);
        }
    }

    public sealed class ItemPagePlanner
    {
        private readonly ItemSpawnPolicy spawnPolicy;

        public ItemPagePlanner(ItemSpawnPolicy spawnPolicy)
        {
            this.spawnPolicy = spawnPolicy;
        }

        // (추가) 페이지 배치 계획을 View 생성과 분리하고 기존 난수 호출 순서를 유지한다.
        public IReadOnlyList<ItemSpawnPlan> CreatePlan(
            FloorPageData pageData,
            int rows,
            int columns,
            int maxItemsPerPage,
            System.Random random)
        {
            List<ItemSpawnPlan> plans = new List<ItemSpawnPlan>();
            HashSet<int> occupied = new HashSet<int>();
            HashSet<string> spawnedCollectionIds = new HashSet<string>();
            int spawnedCount = 0;
            bool needsItemChanceRewardForPage = spawnPolicy.RollItemChance(random);
            int pageReferenceFloor = pageData.GetAddressByRow(0).AbsoluteFloor;
            ItemDefinition pageCollectionItem =
                spawnPolicy.GetGuaranteedGoldenCup(pageData)
                ?? spawnPolicy.TryPickCollectionItem(
                    pageReferenceFloor,
                    random,
                    spawnedCollectionIds);

            if (pageCollectionItem != null
                && TryAddPageCollectionPlan(
                    plans,
                    pageCollectionItem,
                    pageData,
                    rows,
                    columns,
                    random,
                    occupied))
            {
                spawnedCollectionIds.Add(pageCollectionItem.CollectionId);
                spawnedCount++;
            }

            for (int guard = 0;
                 guard < rows * columns && spawnedCount < maxItemsPerPage;
                 guard++)
            {
                int row = random.Next(0, rows);
                int column = random.Next(0, columns);
                int key = row * columns + column;

                if (occupied.Contains(key) || IsExcludedCell(column, columns))
                {
                    continue;
                }

                FloorAddress address = pageData.GetAddressByRow(row);
                ItemDefinition definition = needsItemChanceRewardForPage
                    ? spawnPolicy.PickItemChanceReward(address.AbsoluteFloor, random)
                    : null;
                definition ??= spawnPolicy.PickStandardItem(address.AbsoluteFloor, random);
                if (definition == null)
                {
                    continue;
                }

                if (needsItemChanceRewardForPage
                    && spawnPolicy.IsItemChanceReward(definition))
                {
                    needsItemChanceRewardForPage = false;
                }

                if (definition.ItemType == ItemType.Collection
                    && !string.IsNullOrWhiteSpace(definition.CollectionId))
                {
                    spawnedCollectionIds.Add(definition.CollectionId);
                }

                plans.Add(CreatePlanEntry(definition, address, column, random));
                occupied.Add(key);
                spawnedCount++;
            }

            return plans;
        }

        private bool TryAddPageCollectionPlan(
            List<ItemSpawnPlan> plans,
            ItemDefinition definition,
            FloorPageData pageData,
            int rows,
            int columns,
            System.Random random,
            HashSet<int> occupied)
        {
            List<int> availableCellKeys = new List<int>();
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int key = row * columns + column;
                    if (!occupied.Contains(key) && !IsExcludedCell(column, columns))
                    {
                        availableCellKeys.Add(key);
                    }
                }
            }

            if (availableCellKeys.Count == 0)
            {
                return false;
            }

            int selectedKey = availableCellKeys[random.Next(0, availableCellKeys.Count)];
            int selectedRow = selectedKey / columns;
            int selectedColumn = selectedKey % columns;
            plans.Add(
                CreatePlanEntry(
                    definition,
                    pageData.GetAddressByRow(selectedRow),
                    selectedColumn,
                    random));
            occupied.Add(selectedKey);
            return true;
        }

        private ItemSpawnPlan CreatePlanEntry(
            ItemDefinition definition,
            FloorAddress address,
            int column,
            System.Random random)
        {
            int runtimePassCount =
                spawnPolicy.ResolveRuntimePassCount(definition, random);
            int scoreBonusPercent =
                spawnPolicy.ResolveScoreBonusPercent(definition, runtimePassCount);
            return new ItemSpawnPlan(
                definition,
                address,
                column,
                runtimePassCount,
                scoreBonusPercent);
        }

        private static bool IsExcludedCell(int column, int columns)
        {
            int maxColumn = Mathf.Max(0, columns - 1);
            return column <= 0 || column >= maxColumn;
        }
    }
}
