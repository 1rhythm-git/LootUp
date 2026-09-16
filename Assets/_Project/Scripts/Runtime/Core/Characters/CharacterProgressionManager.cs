using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace LootUp.Core.Characters
{
    public static class CharacterProgressionManager
    {
        private static readonly SemaphoreSlim OperationGate =
            new SemaphoreSlim(1, 1);

        private static ICharacterProgressionSyncService syncService;
        private static ICharacterProgressionService localService;
        private static PendingCharacterProgressionStore pendingStore;
        private static int configurationVersion;

        public static bool UsesServerAuthority => syncService != null;
        public static CharacterProgressionSynchronizationState State
        {
            get;
            private set;
        } = CharacterProgressionSynchronizationState.NotConfigured;
        public static string LastSynchronizationMessage { get; private set; } =
            string.Empty;
        public static bool IsGameplayReady =>
            State == CharacterProgressionSynchronizationState.LocalOnly
            || State == CharacterProgressionSynchronizationState.Synchronized;

        public static void Configure(
            ICharacterProgressionSyncService progressionSyncService,
            ICharacterProgressionService progressionLocalService,
            string userId)
        {
            Interlocked.Increment(ref configurationVersion);
            syncService = progressionSyncService;
            localService = progressionLocalService;
            pendingStore = syncService != null
                ? new PendingCharacterProgressionStore(userId)
                : null;
            LastSynchronizationMessage = string.Empty;
            State = syncService == null
                ? CharacterProgressionSynchronizationState.LocalOnly
                : CharacterProgressionSynchronizationState.NotConfigured;
        }

        public static void MarkSynchronizationFailed(string message)
        {
            State = CharacterProgressionSynchronizationState.Failed;
            LastSynchronizationMessage = message ?? string.Empty;
        }

        public static async Task<CharacterProgressionSynchronizationResult>
            InitializeAsync()
        {
            EnsureCatalogRecords();
            CharacterProgressionServerSnapshot localSnapshot =
                CreateLocalSnapshot();
            ICharacterProgressionSyncService activeService = syncService;
            PendingCharacterProgressionStore activeStore = pendingStore;
            int activeVersion = configurationVersion;
            if (activeService == null)
            {
                State = CharacterProgressionSynchronizationState.LocalOnly;
                return new CharacterProgressionSynchronizationResult(
                    true,
                    false,
                    localSnapshot,
                    string.Empty);
            }

            await OperationGate.WaitAsync();
            try
            {
                State = CharacterProgressionSynchronizationState.Synchronizing;
                if (activeVersion != configurationVersion)
                {
                    return FailSynchronization(
                        CreateChangedResult(localSnapshot));
                }

                CharacterProgressionSynchronizationResult result =
                    await activeService.SynchronizeAsync(localSnapshot);
                if (!result.Succeeded)
                {
                    Debug.LogWarning(
                        $"Character progression synchronization failed: {result.Message}");
                    return FailSynchronization(result);
                }

                if (activeVersion != configurationVersion)
                {
                    return FailSynchronization(
                        CreateChangedResult(localSnapshot));
                }

                if (!ApplySnapshot(result.Snapshot))
                {
                    return FailSynchronization(
                        new CharacterProgressionSynchronizationResult(
                            false,
                            result.Migrated,
                            CreateLocalSnapshot(),
                            "Character progression cache could not be saved."));
                }

                CharacterProgressionMutationResult flushResult =
                    await FlushPendingAsync(
                    activeService,
                    activeStore,
                    activeVersion);
                if (!flushResult.Succeeded)
                {
                    return FailSynchronization(
                        new CharacterProgressionSynchronizationResult(
                            false,
                            result.Migrated,
                            CreateLocalSnapshot(),
                            flushResult.Message));
                }

                State = CharacterProgressionSynchronizationState.Synchronized;
                LastSynchronizationMessage = string.Empty;
                return new CharacterProgressionSynchronizationResult(
                    true,
                    result.Migrated,
                    CreateLocalSnapshot(),
                    result.Message);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Character progression synchronization failed: {exception.Message}");
                return FailSynchronization(
                    new CharacterProgressionSynchronizationResult(
                        false,
                        false,
                        CreateLocalSnapshot(),
                        exception.Message));
            }
            finally
            {
                OperationGate.Release();
            }
        }

        public static async Task<CharacterProgressionOperationResult>
            AddExperienceAsync(
                CharacterDefinition definition,
                int amount,
                string requestId,
                string reason,
                string runId = "")
        {
            if (definition == null
                || amount <= 0
                || string.IsNullOrWhiteSpace(requestId))
            {
                return CreateInvalidOperationResult();
            }

            CharacterProgressionSnapshot snapshot =
                CharacterProgressionState.PreviewExperience(
                    definition,
                    amount);
            CharacterExperienceRequest request =
                new CharacterExperienceRequest(
                    requestId,
                    definition.CharacterId,
                    amount,
                    snapshot.Level,
                    snapshot.CurrentExperience,
                    reason,
                    runId,
                    DateTimeOffset.UtcNow.ToString(
                        "O",
                        CultureInfo.InvariantCulture));
            if (!request.IsValid)
            {
                return CreateInvalidOperationResult();
            }

            ICharacterProgressionSyncService activeService = syncService;
            PendingCharacterProgressionStore activeStore = pendingStore;
            int activeVersion = configurationVersion;
            if (activeService == null)
            {
                CharacterProgressionState.AddExperience(
                    definition,
                    amount);
                return new CharacterProgressionOperationResult(
                    CharacterProgressionOperationState.Applied,
                    FindLocalRecord(definition.CharacterId),
                    true,
                    string.Empty);
            }

            if (activeStore == null
                || !activeStore.TryEnqueueExperience(
                    request,
                    out CharacterExperienceRequest storedRequest,
                    out bool alreadyPending))
            {
                return new CharacterProgressionOperationResult(
                    CharacterProgressionOperationState.Error,
                    FindLocalRecord(definition.CharacterId),
                    false,
                    "Character experience request could not be persisted.");
            }

            if (!alreadyPending)
            {
                CharacterProgressionState.SetProgress(
                    definition,
                    snapshot.Level,
                    snapshot.CurrentExperience);
            }

            return await ApplyExperienceRequestAsync(
                activeService,
                activeStore,
                storedRequest,
                activeVersion);
        }

        public static bool TrySetOwnership(
            string characterId,
            bool isOwned)
        {
            ICharacterProgressionSyncService activeService = syncService;
            PendingCharacterProgressionStore activeStore = pendingStore;
            int activeVersion = configurationVersion;
            ICharacterProgressionService activeLocal =
                localService ?? CharacterProgressionState.Service;
            if (activeService == null)
            {
                return activeLocal.SetOwned(characterId, isOwned);
            }

            if (activeStore == null
                || !activeStore.TrySetPendingOwnership(
                    characterId,
                    isOwned))
            {
                return false;
            }

            if (!activeLocal.SetOwned(characterId, isOwned))
            {
                activeStore.RemoveOwnership(characterId);
                return false;
            }

            _ = SendOwnershipAsync(
                activeService,
                activeStore,
                characterId,
                isOwned,
                activeVersion);
            return true;
        }

        public static bool TrySelectAndEquip(string characterId)
        {
            ICharacterProgressionSyncService activeService = syncService;
            PendingCharacterProgressionStore activeStore = pendingStore;
            int activeVersion = configurationVersion;
            ICharacterProgressionService activeLocal =
                localService ?? CharacterProgressionState.Service;
            if (activeService == null)
            {
                return activeLocal.SetSelectedAndEquipped(characterId);
            }

            if (activeStore == null
                || !activeStore.TrySetPendingLoadout(
                    characterId,
                    characterId))
            {
                return false;
            }

            if (!activeLocal.SetSelectedAndEquipped(characterId))
            {
                activeStore.ClearPendingLoadout();
                return false;
            }

            _ = SendLoadoutAsync(
                activeService,
                activeStore,
                characterId,
                characterId,
                activeVersion);
            return true;
        }

        private static async Task<CharacterProgressionMutationResult>
            SendOwnershipAsync(
                ICharacterProgressionSyncService activeService,
                PendingCharacterProgressionStore activeStore,
                string characterId,
                bool isOwned,
                int activeVersion)
        {
            await OperationGate.WaitAsync();
            try
            {
                if (activeVersion != configurationVersion
                    || !activeService.IsOnline)
                {
                    return new CharacterProgressionMutationResult(
                        false,
                        "Character ownership update is queued.");
                }

                CharacterProgressionMutationResult result =
                    await activeService.SetOwnershipAsync(
                        characterId,
                        isOwned);
                if (result.Succeeded
                    && activeVersion == configurationVersion)
                {
                    activeStore.RemoveOwnership(characterId);
                }

                return result;
            }
            finally
            {
                OperationGate.Release();
            }
        }

        private static async Task<CharacterProgressionMutationResult>
            SendLoadoutAsync(
                ICharacterProgressionSyncService activeService,
                PendingCharacterProgressionStore activeStore,
                string selectedCharacterId,
                string equippedCharacterId,
                int activeVersion)
        {
            await OperationGate.WaitAsync();
            try
            {
                if (activeVersion != configurationVersion
                    || !activeService.IsOnline)
                {
                    return new CharacterProgressionMutationResult(
                        false,
                        "Character loadout update is queued.");
                }

                CharacterProgressionMutationResult result =
                    await activeService.SetLoadoutAsync(
                        selectedCharacterId,
                        equippedCharacterId);
                if (result.Succeeded
                    && activeVersion == configurationVersion)
                {
                    activeStore.ClearPendingLoadout();
                }

                return result;
            }
            finally
            {
                OperationGate.Release();
            }
        }

        private static async Task<CharacterProgressionOperationResult>
            ApplyExperienceRequestAsync(
                ICharacterProgressionSyncService activeService,
                PendingCharacterProgressionStore activeStore,
                CharacterExperienceRequest request,
                int activeVersion)
        {
            await OperationGate.WaitAsync();
            try
            {
                if (activeVersion != configurationVersion
                    || !activeService.IsOnline)
                {
                    return CreateQueuedOperationResult(request.CharacterId);
                }

                CharacterProgressionOperationResult result =
                    await activeService.ApplyExperienceAsync(request);
                if (activeVersion != configurationVersion)
                {
                    return CreateQueuedOperationResult(request.CharacterId);
                }

                HandleExperienceResult(activeStore, request, result);
                return result.State == CharacterProgressionOperationState.Error
                    ? CreateQueuedOperationResult(request.CharacterId, result.Message)
                    : result;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Character experience request failed: {exception.Message}");
                return CreateQueuedOperationResult(
                    request.CharacterId,
                    exception.Message);
            }
            finally
            {
                OperationGate.Release();
            }
        }

        private static async Task<CharacterProgressionMutationResult>
            FlushPendingAsync(
            ICharacterProgressionSyncService activeService,
            PendingCharacterProgressionStore activeStore,
            int activeVersion)
        {
            if (activeService == null || activeStore == null)
            {
                return new CharacterProgressionMutationResult(
                    true,
                    string.Empty);
            }

            IReadOnlyList<CharacterOwnershipUpdate> ownershipUpdates =
                activeStore.GetOwnershipSnapshot();
            for (int i = 0; i < ownershipUpdates.Count; i++)
            {
                if (!CanContinue(activeService, activeVersion))
                {
                    return new CharacterProgressionMutationResult(
                        false,
                        "Character account changed while flushing pending ownership updates.");
                }

                CharacterOwnershipUpdate update = ownershipUpdates[i];
                CharacterProgressionMutationResult result =
                    await activeService.SetOwnershipAsync(
                        update.CharacterId,
                        update.IsOwned);
                if (!result.Succeeded)
                {
                    return result;
                }

                activeStore.RemoveOwnership(update.CharacterId);
            }

            IReadOnlyList<CharacterExperienceRequest> experienceRequests =
                activeStore.GetExperienceSnapshot();
            for (int i = 0; i < experienceRequests.Count; i++)
            {
                if (!CanContinue(activeService, activeVersion))
                {
                    return new CharacterProgressionMutationResult(
                        false,
                        "Character account changed while flushing pending experience requests.");
                }

                CharacterExperienceRequest request = experienceRequests[i];
                CharacterProgressionOperationResult result =
                    await activeService.ApplyExperienceAsync(request);
                HandleExperienceResult(activeStore, request, result);
                if (result.State == CharacterProgressionOperationState.Error)
                {
                    return new CharacterProgressionMutationResult(
                        false,
                        result.Message);
                }
            }

            if (activeStore.TryGetPendingLoadout(
                out string selectedCharacterId,
                out string equippedCharacterId))
            {
                if (!CanContinue(activeService, activeVersion))
                {
                    return new CharacterProgressionMutationResult(
                        false,
                        "Character account changed while flushing the pending loadout.");
                }

                CharacterProgressionMutationResult result =
                    await activeService.SetLoadoutAsync(
                        selectedCharacterId,
                        equippedCharacterId);
                if (result.Succeeded)
                {
                    activeStore.ClearPendingLoadout();
                }
                else
                {
                    return result;
                }
            }

            return new CharacterProgressionMutationResult(
                true,
                string.Empty);
        }

        private static void HandleExperienceResult(
            PendingCharacterProgressionStore activeStore,
            CharacterExperienceRequest request,
            CharacterProgressionOperationResult result)
        {
            if (result.HasAuthoritativeRecord)
            {
                ApplyRecord(result.Record);
            }

            if (result.Completed
                || result.State
                == CharacterProgressionOperationState.InvalidRequest)
            {
                activeStore?.RemoveExperience(request.RequestId);
            }
        }

        private static void EnsureCatalogRecords()
        {
            if (localService == null)
            {
                return;
            }

            IReadOnlyList<CharacterProgressionRecord> defaults =
                CharacterProgressionCatalog.Records;
            for (int i = 0; i < defaults.Count; i++)
            {
                CharacterProgressionRecord record = defaults[i];
                localService.GetOrCreate(
                    record.CharacterId,
                    record.IsOwned);
            }
        }

        private static CharacterProgressionServerSnapshot
            CreateLocalSnapshot()
        {
            return localService == null
                ? new CharacterProgressionServerSnapshot(
                    Array.Empty<CharacterProgressionRecord>(),
                    string.Empty,
                    string.Empty)
                : new CharacterProgressionServerSnapshot(
                    localService.GetAllRecords(),
                    localService.SelectedCharacterId,
                    localService.EquippedCharacterId);
        }

        private static bool ApplySnapshot(
            CharacterProgressionServerSnapshot snapshot)
        {
            return localService != null
                && localService.ReplaceAll(
                snapshot.Records,
                snapshot.SelectedCharacterId,
                snapshot.EquippedCharacterId);
        }

        private static void ApplyRecord(CharacterProgressionRecord record)
        {
            if (localService == null
                || string.IsNullOrWhiteSpace(record.CharacterId))
            {
                return;
            }

            localService.SetProgress(
                record.CharacterId,
                record.Level,
                record.CurrentExperience,
                record.IsOwned);
            localService.SetOwned(record.CharacterId, record.IsOwned);
        }

        private static CharacterProgressionRecord FindLocalRecord(
            string characterId)
        {
            IReadOnlyList<CharacterProgressionRecord> records =
                localService?.GetAllRecords();
            if (records != null)
            {
                for (int i = 0; i < records.Count; i++)
                {
                    if (string.Equals(
                        records[i].CharacterId,
                        characterId,
                        StringComparison.Ordinal))
                    {
                        return records[i];
                    }
                }
            }

            return new CharacterProgressionRecord(
                characterId,
                1,
                0,
                false,
                false);
        }

        private static bool CanContinue(
            ICharacterProgressionSyncService activeService,
            int activeVersion)
        {
            return activeVersion == configurationVersion
                && activeService.IsOnline;
        }

        private static CharacterProgressionOperationResult
            CreateQueuedOperationResult(
                string characterId,
                string message = "Character experience request is queued.")
        {
            return new CharacterProgressionOperationResult(
                CharacterProgressionOperationState.Queued,
                FindLocalRecord(characterId),
                false,
                message);
        }

        private static CharacterProgressionOperationResult
            CreateInvalidOperationResult()
        {
            return new CharacterProgressionOperationResult(
                CharacterProgressionOperationState.InvalidRequest,
                default,
                false,
                "Character experience request is invalid.");
        }

        private static CharacterProgressionSynchronizationResult
            CreateChangedResult(
                CharacterProgressionServerSnapshot snapshot)
        {
            return new CharacterProgressionSynchronizationResult(
                false,
                false,
                snapshot,
                "Character progression account changed while synchronizing.");
        }

        private static CharacterProgressionSynchronizationResult
            FailSynchronization(
                CharacterProgressionSynchronizationResult result)
        {
            State = CharacterProgressionSynchronizationState.Failed;
            LastSynchronizationMessage = result.Message;
            return result;
        }
    }
}
