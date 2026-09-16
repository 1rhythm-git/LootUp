using LootUp.Core.Characters;
using LootUp.Core.Items;
using LootUp.Core.Profile;
using UnityEngine;

namespace LootUp.Core.Authentication
{
    public static class EditorGuestDataResetter
    {
        private static readonly string[] PlayerDataKeys =
        {
            "LootUp.UserProfile.v1",
            "PH.UserProfile.v1",
            "LootUp.CharacterProgression.v1",
            "PH.CharacterProgression.v1",
            "LootUp.CollectionProgress.v1",
            "PH.CollectionProgress.v1",
            LocalCollectionInventoryService.SharedMigrationOwnerKey
        };

        public static void ResetPlayerData()
        {
#if UNITY_EDITOR
            for (int i = 0; i < PlayerDataKeys.Length; i++)
            {
                PlayerPrefs.DeleteKey(PlayerDataKeys[i]);
            }

            PlayerPrefs.Save();

            // (추가) 정적 서비스 캐시도 새 저장 데이터 기준으로 즉시 교체한다.
            UserProfileManager.Configure(new LocalUserProfileService());
            LocalCharacterProgressionService characterProgression =
                new LocalCharacterProgressionService();
            CharacterProgressionState.Configure(characterProgression);
            CharacterProgressionManager.Configure(
                null,
                characterProgression,
                string.Empty);
            CharacterSelectionState.Reset();
            ItemCollectionManager.Configure(
                new LocalCollectionInventoryService());
            Debug.Log(
                "Guest registration reset Editor player profile, character progression, and collection data.");
#endif
        }
    }
}
