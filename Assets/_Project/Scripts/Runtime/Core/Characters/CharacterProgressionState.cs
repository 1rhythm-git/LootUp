using System;
using UnityEngine;

namespace LootUp.Core.Characters
{
    public readonly struct CharacterProgressionSnapshot
    {
        public CharacterProgressionSnapshot(string characterId, int level, int currentExperience, int requiredExperience)
        {
            CharacterId = characterId;
            Level = Mathf.Max(1, level);
            CurrentExperience = Mathf.Max(0, currentExperience);
            RequiredExperience = Mathf.Max(0, requiredExperience);
        }

        public string CharacterId { get; }
        public int Level { get; }
        public int CurrentExperience { get; }
        public int RequiredExperience { get; }
        public bool IsMaxLevel => RequiredExperience <= 0;
        public float NormalizedExperience => IsMaxLevel
            ? 1f
            : Mathf.Clamp01((float)CurrentExperience / RequiredExperience);
    }

    public static class CharacterProgressionState
    {
        private static ICharacterProgressionService service;

        public static event Action<string> ProgressChanged;
        public static event Action<string> OwnershipChanged;
        public static event Action<string> SelectionChanged;

        public static ICharacterProgressionService Service => service ??= new LocalCharacterProgressionService();
        public static string SelectedCharacterId => Service.SelectedCharacterId;
        public static string EquippedCharacterId => Service.EquippedCharacterId;

        public static void Configure(ICharacterProgressionService progressionService)
        {
            service = progressionService ?? throw new ArgumentNullException(nameof(progressionService));
        }

        public static CharacterProgressionSnapshot GetSnapshot(CharacterDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.CharacterId))
            {
                return new CharacterProgressionSnapshot(string.Empty, 1, 0, 0);
            }

            CharacterProgressionRecord record = GetNormalizedRecord(definition);
            int requiredExperience = definition.GetRequiredExperienceForLevel(record.Level);
            return new CharacterProgressionSnapshot(definition.CharacterId, record.Level, record.CurrentExperience, requiredExperience);
        }

        // (추가) 결과식이 확정되면 런 결과 계층에서 호출할 캐릭터 XP 지급 진입점이다.
        public static CharacterProgressionSnapshot AddExperience(CharacterDefinition definition, int amount)
        {
            if (definition == null || amount <= 0 || string.IsNullOrWhiteSpace(definition.CharacterId))
            {
                return GetSnapshot(definition);
            }

            CharacterProgressionSnapshot preview =
                PreviewExperience(definition, amount);
            Service.SetProgress(
                definition.CharacterId,
                preview.Level,
                preview.CurrentExperience,
                definition.InitiallyOwned);
            ProgressChanged?.Invoke(definition.CharacterId);
            return GetSnapshot(definition);
        }

        public static CharacterProgressionSnapshot PreviewExperience(
            CharacterDefinition definition,
            int amount)
        {
            if (definition == null
                || amount <= 0
                || string.IsNullOrWhiteSpace(definition.CharacterId))
            {
                return GetSnapshot(definition);
            }

            CharacterProgressionRecord record =
                GetNormalizedRecord(definition);
            int level = record.Level;
            int currentExperience = (int)Math.Min(
                int.MaxValue,
                (long)record.CurrentExperience + Mathf.Max(0, amount));
            NormalizeProgress(
                definition,
                ref level,
                ref currentExperience);
            int requiredExperience =
                definition.GetRequiredExperienceForLevel(level);
            return new CharacterProgressionSnapshot(
                definition.CharacterId,
                level,
                currentExperience,
                requiredExperience);
        }

        // 저장 데이터 복원과 디버그 설정에서 공통으로 사용하는 진행도 진입점이다.
        public static void SetProgress(CharacterDefinition definition, int level, int currentExperience)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.CharacterId))
            {
                return;
            }

            int normalizedLevel = Mathf.Clamp(level, 1, definition.MaxCharacterLevel);
            int normalizedExperience = Mathf.Max(0, currentExperience);
            NormalizeProgress(definition, ref normalizedLevel, ref normalizedExperience);
            Service.SetProgress(definition.CharacterId, normalizedLevel, normalizedExperience, definition.InitiallyOwned);
            ProgressChanged?.Invoke(definition.CharacterId);
        }

        public static bool IsOwned(CharacterDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.CharacterId))
            {
                return false;
            }

            return Service.GetOrCreate(definition.CharacterId, definition.InitiallyOwned).IsOwned;
        }

        public static void SetOwned(CharacterDefinition definition, bool isOwned)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.CharacterId))
            {
                return;
            }

            Service.GetOrCreate(definition.CharacterId, definition.InitiallyOwned);
            if (CharacterProgressionManager.TrySetOwnership(
                definition.CharacterId,
                isOwned))
            {
                OwnershipChanged?.Invoke(definition.CharacterId);
            }
        }

        public static bool TrySelectAndEquip(CharacterDefinition definition)
        {
            if (!IsOwned(definition)
                || !CharacterProgressionManager.TrySelectAndEquip(
                    definition.CharacterId))
            {
                return false;
            }

            SelectionChanged?.Invoke(definition.CharacterId);
            return true;
        }

        public static bool IsSkillUnlocked(CharacterDefinition definition)
        {
            return definition != null
                && definition.IsSkillConfigured
                && GetSnapshot(definition).Level >= definition.SkillUnlockLevel;
        }

        public static float GetItemChance(CharacterDefinition definition)
        {
            return definition != null ? definition.ItemChance : 0f;
        }

        // 이전 API 호출부 호환용이며 Item Chance는 더 이상 스킬 해금에 종속되지 않는다.
        public static float GetActiveSkillItemPageSpawnChance(CharacterDefinition definition)
        {
            return GetItemChance(definition);
        }

        private static CharacterProgressionRecord GetNormalizedRecord(CharacterDefinition definition)
        {
            CharacterProgressionRecord record = Service.GetOrCreate(definition.CharacterId, definition.InitiallyOwned);
            int level = record.Level;
            int currentExperience = record.CurrentExperience;
            NormalizeProgress(definition, ref level, ref currentExperience);
            if (level != record.Level || currentExperience != record.CurrentExperience)
            {
                Service.SetProgress(definition.CharacterId, level, currentExperience, definition.InitiallyOwned);
                return new CharacterProgressionRecord(
                    record.CharacterId,
                    level,
                    currentExperience,
                    record.IsOwned,
                    record.IsEquipped);
            }

            return record;
        }

        private static void NormalizeProgress(CharacterDefinition definition, ref int level, ref int currentExperience)
        {
            level = Mathf.Clamp(level, 1, definition.MaxCharacterLevel);
            currentExperience = Mathf.Max(0, currentExperience);

            while (level < definition.MaxCharacterLevel)
            {
                int requiredExperience = definition.GetRequiredExperienceForLevel(level);
                if (requiredExperience <= 0 || currentExperience < requiredExperience)
                {
                    break;
                }

                currentExperience -= requiredExperience;
                level++;
            }

            if (level >= definition.MaxCharacterLevel)
            {
                currentExperience = 0;
            }
        }
    }
}
