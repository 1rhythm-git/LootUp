using System;
using System.Collections.Generic;

namespace LootUp.Core.Characters
{
    public enum CharacterProgressionSynchronizationState
    {
        NotConfigured,
        LocalOnly,
        Synchronizing,
        Synchronized,
        Failed
    }

    public enum CharacterProgressionOperationState
    {
        Applied,
        Duplicate,
        Queued,
        InvalidRequest,
        Error
    }

    public readonly struct CharacterProgressionServerSnapshot
    {
        public CharacterProgressionServerSnapshot(
            IReadOnlyList<CharacterProgressionRecord> records,
            string selectedCharacterId,
            string equippedCharacterId)
        {
            Records = records ?? Array.Empty<CharacterProgressionRecord>();
            SelectedCharacterId = selectedCharacterId?.Trim() ?? string.Empty;
            EquippedCharacterId = equippedCharacterId?.Trim() ?? string.Empty;
        }

        public IReadOnlyList<CharacterProgressionRecord> Records { get; }
        public string SelectedCharacterId { get; }
        public string EquippedCharacterId { get; }
    }

    public readonly struct CharacterProgressionSynchronizationResult
    {
        public CharacterProgressionSynchronizationResult(
            bool succeeded,
            bool migrated,
            CharacterProgressionServerSnapshot snapshot,
            string message)
        {
            Succeeded = succeeded;
            Migrated = migrated;
            Snapshot = snapshot;
            Message = message ?? string.Empty;
        }

        public bool Succeeded { get; }
        public bool Migrated { get; }
        public CharacterProgressionServerSnapshot Snapshot { get; }
        public string Message { get; }
    }

    public readonly struct CharacterExperienceRequest
    {
        public CharacterExperienceRequest(
            string requestId,
            string characterId,
            int experienceDelta,
            int levelAfter,
            int experienceAfter,
            string reason,
            string runId,
            string createdAt)
        {
            RequestId = requestId?.Trim() ?? string.Empty;
            CharacterId = CharacterIdMigration.Normalize(characterId);
            ExperienceDelta = Math.Max(0, experienceDelta);
            LevelAfter = Math.Max(1, levelAfter);
            ExperienceAfter = Math.Max(0, experienceAfter);
            Reason = reason?.Trim() ?? string.Empty;
            RunId = runId?.Trim() ?? string.Empty;
            CreatedAt = createdAt?.Trim() ?? string.Empty;
        }

        public string RequestId { get; }
        public string CharacterId { get; }
        public int ExperienceDelta { get; }
        public int LevelAfter { get; }
        public int ExperienceAfter { get; }
        public string Reason { get; }
        public string RunId { get; }
        public string CreatedAt { get; }
        public bool IsValid =>
            !string.IsNullOrWhiteSpace(RequestId)
            && !string.IsNullOrWhiteSpace(CharacterId)
            && ExperienceDelta > 0;
    }

    public readonly struct CharacterProgressionOperationResult
    {
        public CharacterProgressionOperationResult(
            CharacterProgressionOperationState state,
            CharacterProgressionRecord record,
            bool hasAuthoritativeRecord,
            string message)
        {
            State = state;
            Record = record;
            HasAuthoritativeRecord = hasAuthoritativeRecord;
            Message = message ?? string.Empty;
        }

        public CharacterProgressionOperationState State { get; }
        public CharacterProgressionRecord Record { get; }
        public bool HasAuthoritativeRecord { get; }
        public string Message { get; }
        public bool Completed =>
            State == CharacterProgressionOperationState.Applied
            || State == CharacterProgressionOperationState.Duplicate;
    }

    public readonly struct CharacterProgressionMutationResult
    {
        public CharacterProgressionMutationResult(
            bool succeeded,
            string message)
        {
            Succeeded = succeeded;
            Message = message ?? string.Empty;
        }

        public bool Succeeded { get; }
        public string Message { get; }
    }

    public readonly struct CharacterOwnershipUpdate
    {
        public CharacterOwnershipUpdate(
            string characterId,
            bool isOwned)
        {
            CharacterId = CharacterIdMigration.Normalize(characterId);
            IsOwned = isOwned;
        }

        public string CharacterId { get; }
        public bool IsOwned { get; }
    }
}
