using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LootUp.Core.Characters
{
    internal sealed class PendingCharacterProgressionStore
    {
        private const int MaximumPendingExperienceCount = 128;
        private const string SaveKeyPrefix =
            "LootUp.CharacterProgression.Pending.v1.";

        private readonly string saveKey;
        private readonly PendingCharacterProgressionSaveData saveData;

        public PendingCharacterProgressionStore(string userId)
        {
            saveKey = CreateSaveKey(userId);
            saveData = Load();
        }

        public bool TryEnqueueExperience(
            CharacterExperienceRequest request,
            out CharacterExperienceRequest storedRequest,
            out bool alreadyPending)
        {
            storedRequest = default;
            alreadyPending = false;
            if (!request.IsValid)
            {
                return false;
            }

            PendingExperienceData existing =
                FindExperience(request.RequestId);
            if (existing != null)
            {
                storedRequest = CreateRequest(existing);
                alreadyPending = true;
                return true;
            }

            if (saveData.ExperienceRequests.Count
                >= MaximumPendingExperienceCount)
            {
                Debug.LogError(
                    "Character progression pending queue reached its limit.");
                return false;
            }

            saveData.ExperienceRequests.Add(new PendingExperienceData
            {
                RequestId = request.RequestId,
                CharacterId = request.CharacterId,
                ExperienceDelta = request.ExperienceDelta,
                LevelAfter = request.LevelAfter,
                ExperienceAfter = request.ExperienceAfter,
                Reason = request.Reason,
                RunId = request.RunId,
                CreatedAt = request.CreatedAt
            });
            if (!TrySave())
            {
                saveData.ExperienceRequests.RemoveAt(
                    saveData.ExperienceRequests.Count - 1);
                return false;
            }

            storedRequest = request;
            return true;
        }

        public IReadOnlyList<CharacterExperienceRequest>
            GetExperienceSnapshot()
        {
            List<CharacterExperienceRequest> result =
                new List<CharacterExperienceRequest>(
                    saveData.ExperienceRequests.Count);
            for (int i = 0; i < saveData.ExperienceRequests.Count; i++)
            {
                PendingExperienceData entry =
                    saveData.ExperienceRequests[i];
                if (entry != null)
                {
                    result.Add(CreateRequest(entry));
                }
            }

            return result;
        }

        public void RemoveExperience(string requestId)
        {
            for (int i = saveData.ExperienceRequests.Count - 1; i >= 0; i--)
            {
                PendingExperienceData entry =
                    saveData.ExperienceRequests[i];
                if (entry != null
                    && string.Equals(
                        entry.RequestId,
                        requestId,
                        StringComparison.Ordinal))
                {
                    saveData.ExperienceRequests.RemoveAt(i);
                }
            }

            TrySave();
        }

        public bool TrySetPendingOwnership(
            string characterId,
            bool isOwned)
        {
            characterId = CharacterIdMigration.Normalize(characterId);
            if (string.IsNullOrWhiteSpace(characterId))
            {
                return false;
            }

            PendingOwnershipData existing = FindOwnership(characterId);
            bool created = existing == null;
            bool previousOwned = existing != null && existing.IsOwned;
            if (existing == null)
            {
                existing = new PendingOwnershipData();
                saveData.OwnershipUpdates.Add(existing);
            }

            existing.CharacterId = characterId;
            existing.IsOwned = isOwned;
            if (TrySave())
            {
                return true;
            }

            if (created)
            {
                saveData.OwnershipUpdates.Remove(existing);
            }
            else
            {
                existing.IsOwned = previousOwned;
            }

            return false;
        }

        public IReadOnlyList<CharacterOwnershipUpdate>
            GetOwnershipSnapshot()
        {
            List<CharacterOwnershipUpdate> result =
                new List<CharacterOwnershipUpdate>(
                    saveData.OwnershipUpdates.Count);
            for (int i = 0; i < saveData.OwnershipUpdates.Count; i++)
            {
                PendingOwnershipData entry = saveData.OwnershipUpdates[i];
                if (entry != null)
                {
                    result.Add(new CharacterOwnershipUpdate(
                        entry.CharacterId,
                        entry.IsOwned));
                }
            }

            return result;
        }

        public void RemoveOwnership(string characterId)
        {
            for (int i = saveData.OwnershipUpdates.Count - 1; i >= 0; i--)
            {
                PendingOwnershipData entry = saveData.OwnershipUpdates[i];
                if (entry != null
                    && string.Equals(
                        entry.CharacterId,
                        characterId,
                        StringComparison.Ordinal))
                {
                    saveData.OwnershipUpdates.RemoveAt(i);
                }
            }

            TrySave();
        }

        public bool TrySetPendingLoadout(
            string selectedCharacterId,
            string equippedCharacterId)
        {
            bool previousHasUpdate = saveData.HasLoadoutUpdate;
            string previousSelected = saveData.SelectedCharacterId;
            string previousEquipped = saveData.EquippedCharacterId;
            saveData.HasLoadoutUpdate = true;
            saveData.SelectedCharacterId =
                CharacterIdMigration.Normalize(selectedCharacterId);
            saveData.EquippedCharacterId =
                CharacterIdMigration.Normalize(equippedCharacterId);
            if (TrySave())
            {
                return true;
            }

            saveData.HasLoadoutUpdate = previousHasUpdate;
            saveData.SelectedCharacterId = previousSelected;
            saveData.EquippedCharacterId = previousEquipped;
            return false;
        }

        public bool TryGetPendingLoadout(
            out string selectedCharacterId,
            out string equippedCharacterId)
        {
            selectedCharacterId = saveData.SelectedCharacterId
                ?? string.Empty;
            equippedCharacterId = saveData.EquippedCharacterId
                ?? string.Empty;
            return saveData.HasLoadoutUpdate;
        }

        public void ClearPendingLoadout()
        {
            saveData.HasLoadoutUpdate = false;
            saveData.SelectedCharacterId = string.Empty;
            saveData.EquippedCharacterId = string.Empty;
            TrySave();
        }

        private PendingCharacterProgressionSaveData Load()
        {
            if (string.IsNullOrWhiteSpace(saveKey))
            {
                return new PendingCharacterProgressionSaveData();
            }

            string json = PlayerPrefs.GetString(saveKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new PendingCharacterProgressionSaveData();
            }

            try
            {
                PendingCharacterProgressionSaveData loaded =
                    JsonUtility.FromJson<PendingCharacterProgressionSaveData>(json)
                    ?? new PendingCharacterProgressionSaveData();
                loaded.ExperienceRequests ??= new List<PendingExperienceData>();
                loaded.OwnershipUpdates ??= new List<PendingOwnershipData>();
                return loaded;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Character progression pending queue load failed: {exception.Message}");
                return new PendingCharacterProgressionSaveData();
            }
        }

        private bool TrySave()
        {
            if (string.IsNullOrWhiteSpace(saveKey))
            {
                return false;
            }

            try
            {
                PlayerPrefs.SetString(saveKey, JsonUtility.ToJson(saveData));
                PlayerPrefs.Save();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Character progression pending queue save failed: {exception.Message}");
                return false;
            }
        }

        private PendingExperienceData FindExperience(string requestId)
        {
            for (int i = 0; i < saveData.ExperienceRequests.Count; i++)
            {
                PendingExperienceData entry =
                    saveData.ExperienceRequests[i];
                if (entry != null
                    && string.Equals(
                        entry.RequestId,
                        requestId,
                        StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            return null;
        }

        private PendingOwnershipData FindOwnership(string characterId)
        {
            for (int i = 0; i < saveData.OwnershipUpdates.Count; i++)
            {
                PendingOwnershipData entry = saveData.OwnershipUpdates[i];
                if (entry != null
                    && string.Equals(
                        entry.CharacterId,
                        characterId,
                        StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            return null;
        }

        private static CharacterExperienceRequest CreateRequest(
            PendingExperienceData entry)
        {
            return new CharacterExperienceRequest(
                entry.RequestId,
                entry.CharacterId,
                entry.ExperienceDelta,
                entry.LevelAfter,
                entry.ExperienceAfter,
                entry.Reason,
                entry.RunId,
                entry.CreatedAt);
        }

        private static string CreateSaveKey(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return string.Empty;
            }

            string encodedUserId = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(userId.Trim()))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            return SaveKeyPrefix + encodedUserId;
        }

        [Serializable]
        private sealed class PendingCharacterProgressionSaveData
        {
            public int Version = 1;
            public List<PendingExperienceData> ExperienceRequests =
                new List<PendingExperienceData>();
            public List<PendingOwnershipData> OwnershipUpdates =
                new List<PendingOwnershipData>();
            public bool HasLoadoutUpdate;
            public string SelectedCharacterId;
            public string EquippedCharacterId;
        }

        [Serializable]
        private sealed class PendingExperienceData
        {
            public string RequestId;
            public string CharacterId;
            public int ExperienceDelta;
            public int LevelAfter;
            public int ExperienceAfter;
            public string Reason;
            public string RunId;
            public string CreatedAt;
        }

        [Serializable]
        private sealed class PendingOwnershipData
        {
            public string CharacterId;
            public bool IsOwned;
        }
    }
}
