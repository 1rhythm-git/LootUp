using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LootUp.Core.Authentication;
using LootUp.Core.Characters;
using UnityEditor;
using UnityEngine;

namespace LootUp.Editor.Validation
{
    public static class CharacterProgressionValidationMenu
    {
        private const string MenuRoot =
            "LootUp/Validation/Character Progression/";

        [MenuItem(MenuRoot + "Validate Duplicate XP Request")]
        private static async void ValidateDuplicateExperienceRequest()
        {
            if (!CanRunValidation())
            {
                return;
            }

            CharacterDefinition definition = FindDefinition(
                CharacterProgressionState.SelectedCharacterId);
            if (definition == null)
            {
                Debug.LogError(
                    "[CharacterProgressionValidation] 선택 캐릭터 에셋을 찾을 수 없습니다.");
                return;
            }

            string requestId =
                $"validation:character-xp:{Guid.NewGuid():N}";
            try
            {
                CharacterProgressionOperationResult first =
                    await CharacterProgressionManager.AddExperienceAsync(
                        definition,
                        1,
                        requestId,
                        "validation_duplicate",
                        "validation");
                CharacterProgressionOperationResult second =
                    await CharacterProgressionManager.AddExperienceAsync(
                        definition,
                        1,
                        requestId,
                        "validation_duplicate",
                        "validation");
                CharacterProgressionSnapshot finalSnapshot =
                    CharacterProgressionState.GetSnapshot(definition);

                bool passed =
                    first.State == CharacterProgressionOperationState.Applied
                    && second.State
                    == CharacterProgressionOperationState.Duplicate
                    && second.HasAuthoritativeRecord
                    && finalSnapshot.Level == second.Record.Level
                    && finalSnapshot.CurrentExperience
                    == second.Record.CurrentExperience;
                string message =
                    $"[CharacterProgressionValidation] "
                    + $"First={first.State}, Second={second.State}, "
                    + $"Level={finalSnapshot.Level}, "
                    + $"XP={finalSnapshot.CurrentExperience}, "
                    + $"Passed={passed}";
                if (passed)
                {
                    Debug.Log(message);
                }
                else
                {
                    Debug.LogError(message);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        [MenuItem(MenuRoot + "Validate Loadout Round Trip")]
        private static async void ValidateLoadoutRoundTrip()
        {
            if (!CanRunValidation())
            {
                return;
            }

            string originalCharacterId =
                CharacterProgressionState.SelectedCharacterId;
            CharacterDefinition originalDefinition =
                FindDefinition(originalCharacterId);
            CharacterDefinition targetDefinition =
                FindAnotherOwnedDefinition(originalCharacterId);
            if (originalDefinition == null || targetDefinition == null)
            {
                Debug.LogError(
                    "[CharacterProgressionValidation] 왕복 검증에는 현재 캐릭터와 다른 보유 캐릭터가 필요합니다.");
                return;
            }

            try
            {
                bool targetQueued =
                    CharacterProgressionState.TrySelectAndEquip(
                        targetDefinition);
                CharacterProgressionSynchronizationResult targetSync =
                    targetQueued
                        ? await CharacterProgressionManager.InitializeAsync()
                        : default;
                bool targetPassed = targetQueued
                    && targetSync.Succeeded
                    && IsSelectedAndEquipped(targetDefinition.CharacterId);

                bool restoreQueued =
                    CharacterProgressionState.TrySelectAndEquip(
                        originalDefinition);
                CharacterProgressionSynchronizationResult restoreSync =
                    restoreQueued
                        ? await CharacterProgressionManager.InitializeAsync()
                        : default;
                bool restorePassed = restoreQueued
                    && restoreSync.Succeeded
                    && IsSelectedAndEquipped(originalCharacterId);
                bool passed = targetPassed && restorePassed;
                string message =
                    $"[CharacterProgressionValidation] "
                    + $"Target={targetDefinition.CharacterId}, "
                    + $"TargetApplied={targetPassed}, "
                    + $"Restored={restorePassed}, Passed={passed}";
                if (passed)
                {
                    Debug.Log(message);
                }
                else
                {
                    Debug.LogError(message);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static bool CanRunValidation()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning(
                    "[CharacterProgressionValidation] Play Mode에서 실행하세요.");
                return false;
            }

            if (!AuthenticationManager.IsAuthenticated
                || !CharacterProgressionManager.UsesServerAuthority
                || CharacterProgressionManager.State
                != CharacterProgressionSynchronizationState.Synchronized)
            {
                Debug.LogWarning(
                    "[CharacterProgressionValidation] BackND 로그인과 캐릭터 동기화 완료 후 실행하세요.");
                return false;
            }

            return true;
        }

        private static bool IsSelectedAndEquipped(string characterId)
        {
            return string.Equals(
                    CharacterProgressionState.SelectedCharacterId,
                    characterId,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    CharacterProgressionState.EquippedCharacterId,
                    characterId,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static CharacterDefinition FindAnotherOwnedDefinition(
            string excludedCharacterId)
        {
            IReadOnlyList<CharacterProgressionRecord> records =
                CharacterProgressionState.Service.GetAllRecords();
            for (int i = 0; i < records.Count; i++)
            {
                CharacterProgressionRecord record = records[i];
                if (record.IsOwned
                    && !string.Equals(
                        record.CharacterId,
                        excludedCharacterId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    CharacterDefinition definition =
                        FindDefinition(record.CharacterId);
                    if (definition != null)
                    {
                        return definition;
                    }
                }
            }

            return null;
        }

        private static CharacterDefinition FindDefinition(string characterId)
        {
            string[] assetGuids =
                AssetDatabase.FindAssets("t:CharacterDefinition");
            for (int i = 0; i < assetGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(assetGuids[i]);
                CharacterDefinition definition =
                    AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path);
                if (definition != null
                    && string.Equals(
                        definition.CharacterId,
                        characterId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return definition;
                }
            }

            return null;
        }
    }
}
