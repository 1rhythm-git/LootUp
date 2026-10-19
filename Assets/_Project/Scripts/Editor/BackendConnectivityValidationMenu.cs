using System;
using LootUp.Core.Authentication;
using LootUp.Core.Characters;
using UnityEditor;
using UnityEngine;

namespace LootUp.Editor.Validation
{
    public static class BackendConnectivityValidationMenu
    {
        private const string MenuRoot =
            "LootUp/Validation/Backend Connectivity/";
        private const string SimulateOfflineMenu =
            MenuRoot + "Simulate Character Offline";
        private const string RestoreOnlineMenu =
            MenuRoot + "Restore Character Online + Flush Pending";
        private const string QueueTestExperienceMenu =
            MenuRoot + "Queue Test Character XP (+1)";

        private static bool isRestoringOnline;

        [MenuItem(SimulateOfflineMenu)]
        private static void SimulateCharacterOffline()
        {
            if (!CanChangeConnectivity())
            {
                return;
            }

            BackndCharacterProgressionService.SetEditorOfflineSimulation(
                true);
            Menu.SetChecked(SimulateOfflineMenu, true);
            Debug.LogWarning(
                "[BackendConnectivityValidation] CharacterOffline=True, "
                + "다음 캐릭터 XP/보유/Loadout 요청은 Pending Queue에 저장됩니다.");
        }

        [MenuItem(SimulateOfflineMenu, true)]
        private static bool ValidateSimulateCharacterOffline()
        {
            bool isOffline =
                BackndCharacterProgressionService
                    .EditorOfflineSimulationEnabled;
            Menu.SetChecked(SimulateOfflineMenu, isOffline);
            return CanUseValidationMenu()
                && !isOffline
                && !isRestoringOnline;
        }

        [MenuItem(RestoreOnlineMenu)]
        private static async void RestoreCharacterOnlineAndFlushPending()
        {
            if (!CanChangeConnectivity())
            {
                return;
            }

            isRestoringOnline = true;
            BackndCharacterProgressionService.SetEditorOfflineSimulation(
                false);
            Menu.SetChecked(SimulateOfflineMenu, false);

            try
            {
                CharacterProgressionSynchronizationResult result =
                    await CharacterProgressionManager.InitializeAsync();
                bool passed = result.Succeeded
                    && CharacterProgressionManager.State
                    == CharacterProgressionSynchronizationState.Synchronized;
                string message =
                    "[BackendConnectivityValidation] "
                    + "CharacterOffline=False, "
                    + $"PendingFlushSucceeded={passed}, "
                    + $"State={CharacterProgressionManager.State}, "
                    + $"Message={result.Message}";
                if (passed)
                {
                    Debug.Log(message);
                }
                else
                {
                    Debug.LogError(message);
                }
            }
            finally
            {
                isRestoringOnline = false;
            }
        }

        [MenuItem(RestoreOnlineMenu, true)]
        private static bool ValidateRestoreCharacterOnlineAndFlushPending()
        {
            return CanUseValidationMenu()
                && BackndCharacterProgressionService
                    .EditorOfflineSimulationEnabled
                && !isRestoringOnline;
        }

        [MenuItem(QueueTestExperienceMenu)]
        private static async void QueueTestCharacterExperience()
        {
            if (!CanChangeConnectivity())
            {
                return;
            }

            CharacterDefinition definition = FindDefinition(
                CharacterProgressionState.SelectedCharacterId);
            if (definition == null)
            {
                Debug.LogError(
                    "[BackendConnectivityValidation] 선택 캐릭터 에셋을 찾을 수 없습니다.");
                return;
            }

            string requestId = $"validation:pending-xp:{Guid.NewGuid():N}";
            CharacterProgressionOperationResult first =
                await CharacterProgressionManager.AddExperienceAsync(
                    definition,
                    1,
                    requestId,
                    "validation_pending",
                    "validation");
            CharacterProgressionSnapshot firstSnapshot =
                CharacterProgressionState.GetSnapshot(definition);
            CharacterProgressionOperationResult second =
                await CharacterProgressionManager.AddExperienceAsync(
                    definition,
                    1,
                    requestId,
                    "validation_pending",
                    "validation");
            CharacterProgressionSnapshot secondSnapshot =
                CharacterProgressionState.GetSnapshot(definition);

            bool passed =
                first.State == CharacterProgressionOperationState.Queued
                && second.State == CharacterProgressionOperationState.Queued
                && firstSnapshot.Level == secondSnapshot.Level
                && firstSnapshot.CurrentExperience
                == secondSnapshot.CurrentExperience;
            string message =
                "[BackendConnectivityValidation] "
                + $"RequestId={requestId}, First={first.State}, "
                + $"Second={second.State}, Level={secondSnapshot.Level}, "
                + $"XP={secondSnapshot.CurrentExperience}, "
                + $"PendingDuplicateGuardPassed={passed}";
            if (passed)
            {
                Debug.Log(message);
            }
            else
            {
                Debug.LogError(message);
            }
        }

        [MenuItem(QueueTestExperienceMenu, true)]
        private static bool ValidateQueueTestCharacterExperience()
        {
            return CanUseValidationMenu()
                && BackndCharacterProgressionService
                    .EditorOfflineSimulationEnabled
                && !isRestoringOnline;
        }

        private static bool CanChangeConnectivity()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning(
                    "[BackendConnectivityValidation] Play Mode에서 실행하세요.");
                return false;
            }

            if (!AuthenticationManager.IsAuthenticated
                || !CharacterProgressionManager.UsesServerAuthority)
            {
                Debug.LogWarning(
                    "[BackendConnectivityValidation] BackND 로그인과 캐릭터 서버 구성을 완료한 뒤 실행하세요.");
                return false;
            }

            return true;
        }

        private static bool CanUseValidationMenu()
        {
            return EditorApplication.isPlaying
                && AuthenticationManager.IsAuthenticated
                && CharacterProgressionManager.UsesServerAuthority;
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
