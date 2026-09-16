using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BackEnd;
using LitJson;
using LootUp.Core.Backend;
using UnityEngine;
using BackndApi = BackEnd.Backend;

namespace LootUp.Core.Characters
{
    public sealed class BackndCharacterProgressionService :
        ICharacterProgressionSyncService
    {
        public const string ProgressTableName =
            "LootUpCharacterProgress";
        public const string LoadoutTableName =
            "LootUpPlayerLoadout";
        public const string LedgerTableName =
            "LootUpCharacterProgressLedger";

        private const int SchemaVersion = 1;
        private const int MigrationVersion = 1;

        private readonly string userId;
        private readonly SemaphoreSlim operationGate =
            new SemaphoreSlim(1, 1);

        public BackndCharacterProgressionService(string userId)
        {
            this.userId = userId?.Trim() ?? string.Empty;
        }

        public bool IsOnline =>
            !string.IsNullOrWhiteSpace(userId)
            && BackndSdkManager.State
            == BackndInitializationState.Initialized;

        public async Task<CharacterProgressionSynchronizationResult>
            SynchronizeAsync(
                CharacterProgressionServerSnapshot localSnapshot)
        {
            await operationGate.WaitAsync();
            try
            {
                if (!IsOnline)
                {
                    return CreateSynchronizationError(
                        localSnapshot,
                        "BackND character progression service is offline.");
                }

                ProgressRowsQuery progressQuery =
                    await LoadProgressRowsAsync();
                if (!progressQuery.Response.IsSuccess())
                {
                    return CreateSynchronizationError(
                        localSnapshot,
                        progressQuery.Response);
                }

                LoadoutQuery loadoutQuery = await LoadLoadoutAsync();
                if (!loadoutQuery.Response.IsSuccess())
                {
                    return CreateSynchronizationError(
                        localSnapshot,
                        loadoutQuery.Response);
                }

                bool migrated = progressQuery.Rows.Count == 0;
                List<CharacterProgressionRecord> mergedRecords =
                    MergeRecords(progressQuery.Rows, localSnapshot.Records);
                string selectedCharacterId = loadoutQuery.Exists
                    ? loadoutQuery.SelectedCharacterId
                    : localSnapshot.SelectedCharacterId;
                string equippedCharacterId = loadoutQuery.Exists
                    ? loadoutQuery.EquippedCharacterId
                    : localSnapshot.EquippedCharacterId;
                NormalizeLoadout(
                    mergedRecords,
                    ref selectedCharacterId,
                    ref equippedCharacterId);

                List<TransactionValue> transactions =
                    CreateSynchronizationTransactions(
                        progressQuery.Rows,
                        loadoutQuery,
                        mergedRecords,
                        selectedCharacterId,
                        equippedCharacterId);
                if (transactions.Count > 0)
                {
                    BackendReturnObject response = await RunRequest(
                        callback => BackndApi.GameData.TransactionWriteV2(
                            transactions,
                            callback));
                    if (!response.IsSuccess())
                    {
                        return CreateSynchronizationError(
                            localSnapshot,
                            response);
                    }
                }

                return new CharacterProgressionSynchronizationResult(
                    true,
                    migrated,
                    new CharacterProgressionServerSnapshot(
                        mergedRecords,
                        selectedCharacterId,
                        equippedCharacterId),
                    string.Empty);
            }
            catch (Exception exception)
            {
                return CreateSynchronizationError(
                    localSnapshot,
                    exception.Message);
            }
            finally
            {
                operationGate.Release();
            }
        }

        public async Task<CharacterProgressionOperationResult>
            ApplyExperienceAsync(CharacterExperienceRequest request)
        {
            if (!request.IsValid
                || !CharacterProgressionCatalog.Contains(
                    request.CharacterId))
            {
                return CreateOperationResult(
                    CharacterProgressionOperationState.InvalidRequest,
                    default,
                    false,
                    "Character experience request is invalid.");
            }

            await operationGate.WaitAsync();
            try
            {
                if (!IsOnline)
                {
                    return CreateOperationError(
                        "BackND character progression service is offline.");
                }

                LedgerQuery ledgerQuery =
                    await FindLedgerAsync(request.RequestId);
                if (!ledgerQuery.Response.IsSuccess())
                {
                    return CreateOperationError(ledgerQuery.Response);
                }

                ProgressRowQuery progressQuery =
                    await FindProgressRowAsync(request.CharacterId);
                if (!progressQuery.Response.IsSuccess())
                {
                    return CreateOperationError(progressQuery.Response);
                }

                if (!progressQuery.Exists)
                {
                    return CreateOperationError(
                        "Character progression row does not exist.");
                }

                if (ledgerQuery.Exists)
                {
                    return CreateOperationResult(
                        CharacterProgressionOperationState.Duplicate,
                        progressQuery.Row.Record,
                        true,
                        string.Empty);
                }

                if (IsProgressBehind(
                    request.LevelAfter,
                    request.ExperienceAfter,
                    progressQuery.Row.Record))
                {
                    return CreateOperationResult(
                        CharacterProgressionOperationState.InvalidRequest,
                        progressQuery.Row.Record,
                        true,
                        "Character experience result is older than the server record.");
                }

                CharacterProgressionRecord nextRecord =
                    new CharacterProgressionRecord(
                        request.CharacterId,
                        Mathf.Clamp(
                            request.LevelAfter,
                            1,
                            CharacterDefinition.MaximumCharacterLevel),
                        request.ExperienceAfter,
                        progressQuery.Row.Record.IsOwned,
                        progressQuery.Row.Record.IsEquipped);
                List<TransactionValue> transactions =
                    new List<TransactionValue>
                    {
                        TransactionValue.SetUpdateV2(
                            ProgressTableName,
                            progressQuery.Row.RowInDate,
                            userId,
                            CreateProgressParam(nextRecord)),
                        TransactionValue.SetInsert(
                            LedgerTableName,
                            CreateLedgerParam(request, nextRecord))
                    };
                BackendReturnObject response = await RunRequest(
                    callback => BackndApi.GameData.TransactionWriteV2(
                        transactions,
                        callback));
                if (!response.IsSuccess())
                {
                    return CreateOperationError(response);
                }

                return CreateOperationResult(
                    CharacterProgressionOperationState.Applied,
                    nextRecord,
                    true,
                    string.Empty);
            }
            catch (Exception exception)
            {
                return CreateOperationError(exception.Message);
            }
            finally
            {
                operationGate.Release();
            }
        }

        public async Task<CharacterProgressionMutationResult>
            SetOwnershipAsync(
                string characterId,
                bool isOwned)
        {
            characterId = CharacterIdMigration.Normalize(characterId);
            if (!CharacterProgressionCatalog.Contains(characterId))
            {
                return new CharacterProgressionMutationResult(
                    false,
                    "Character progression record is invalid.");
            }

            await operationGate.WaitAsync();
            try
            {
                if (!IsOnline)
                {
                    return CreateMutationError(
                        "BackND character progression service is offline.");
                }

                ProgressRowQuery query =
                    await FindProgressRowAsync(characterId);
                if (!query.Response.IsSuccess())
                {
                    return CreateMutationError(query.Response);
                }

                TransactionValue transaction;
                if (query.Exists)
                {
                    Param ownershipParam = new Param();
                    ownershipParam.Add("isOwned", isOwned);
                    transaction = TransactionValue.SetUpdateV2(
                        ProgressTableName,
                        query.Row.RowInDate,
                        userId,
                        ownershipParam);
                }
                else
                {
                    transaction = TransactionValue.SetInsert(
                        ProgressTableName,
                        CreateProgressParam(
                            new CharacterProgressionRecord(
                                characterId,
                                1,
                                0,
                                isOwned,
                                false)));
                }

                BackendReturnObject response = await RunRequest(
                    callback => BackndApi.GameData.TransactionWriteV2(
                        new List<TransactionValue> { transaction },
                        callback));
                return response.IsSuccess()
                    ? new CharacterProgressionMutationResult(
                        true,
                        string.Empty)
                    : CreateMutationError(response);
            }
            catch (Exception exception)
            {
                return CreateMutationError(exception.Message);
            }
            finally
            {
                operationGate.Release();
            }
        }

        public async Task<CharacterProgressionMutationResult>
            SetLoadoutAsync(
                string selectedCharacterId,
                string equippedCharacterId)
        {
            selectedCharacterId =
                CharacterIdMigration.Normalize(selectedCharacterId);
            equippedCharacterId =
                CharacterIdMigration.Normalize(equippedCharacterId);
            await operationGate.WaitAsync();
            try
            {
                if (!IsOnline)
                {
                    return CreateMutationError(
                        "BackND character progression service is offline.");
                }

                CharacterProgressionMutationResult validation =
                    await ValidateOwnedLoadoutAsync(
                        selectedCharacterId,
                        equippedCharacterId);
                if (!validation.Succeeded)
                {
                    return validation;
                }

                LoadoutQuery query = await LoadLoadoutAsync();
                if (!query.Response.IsSuccess())
                {
                    return CreateMutationError(query.Response);
                }

                Param param = CreateLoadoutParam(
                    selectedCharacterId,
                    equippedCharacterId);
                TransactionValue transaction = query.Exists
                    ? TransactionValue.SetUpdateV2(
                        LoadoutTableName,
                        query.RowInDate,
                        userId,
                        param)
                    : TransactionValue.SetInsert(
                        LoadoutTableName,
                        param);
                BackendReturnObject response = await RunRequest(
                    callback => BackndApi.GameData.TransactionWriteV2(
                        new List<TransactionValue> { transaction },
                        callback));
                return response.IsSuccess()
                    ? new CharacterProgressionMutationResult(
                        true,
                        string.Empty)
                    : CreateMutationError(response);
            }
            catch (Exception exception)
            {
                return CreateMutationError(exception.Message);
            }
            finally
            {
                operationGate.Release();
            }
        }

        private async Task<CharacterProgressionMutationResult>
            ValidateOwnedLoadoutAsync(
                string selectedCharacterId,
                string equippedCharacterId)
        {
            if (!string.IsNullOrEmpty(selectedCharacterId))
            {
                ProgressRowQuery selected =
                    await FindProgressRowAsync(selectedCharacterId);
                if (!selected.Response.IsSuccess())
                {
                    return CreateMutationError(selected.Response);
                }

                if (!selected.Exists || !selected.Row.Record.IsOwned)
                {
                    return CreateMutationError(
                        "Selected character is not owned.");
                }
            }

            if (!string.IsNullOrEmpty(equippedCharacterId)
                && !string.Equals(
                    selectedCharacterId,
                    equippedCharacterId,
                    StringComparison.Ordinal))
            {
                ProgressRowQuery equipped =
                    await FindProgressRowAsync(equippedCharacterId);
                if (!equipped.Response.IsSuccess())
                {
                    return CreateMutationError(equipped.Response);
                }

                if (!equipped.Exists || !equipped.Row.Record.IsOwned)
                {
                    return CreateMutationError(
                        "Equipped character is not owned.");
                }
            }

            return new CharacterProgressionMutationResult(
                true,
                string.Empty);
        }

        private async Task<ProgressRowsQuery> LoadProgressRowsAsync()
        {
            BackendReturnObject response = await RunRequest(
                callback => BackndApi.GameData.GetMyData(
                    ProgressTableName,
                    new Where(),
                    100,
                    callback));
            List<ProgressRow> rows = new List<ProgressRow>();
            if (!response.IsSuccess())
            {
                return new ProgressRowsQuery(response, rows);
            }

            JsonData flattenedRows = response.FlattenRows();
            if (flattenedRows != null)
            {
                for (int i = 0; i < flattenedRows.Count; i++)
                {
                    ProgressRow row = ParseProgressRow(flattenedRows[i]);
                    if (!string.IsNullOrWhiteSpace(row.Record.CharacterId)
                        && CharacterProgressionCatalog.Contains(
                            row.Record.CharacterId))
                    {
                        rows.Add(row);
                    }
                }
            }

            return new ProgressRowsQuery(response, rows);
        }

        private async Task<ProgressRowQuery> FindProgressRowAsync(
            string characterId)
        {
            Where where = new Where();
            where.Equal("characterId", characterId);
            BackendReturnObject response = await RunRequest(
                callback => BackndApi.GameData.GetMyData(
                    ProgressTableName,
                    where,
                    1,
                    callback));
            if (!response.IsSuccess())
            {
                return new ProgressRowQuery(
                    response,
                    default,
                    false);
            }

            JsonData rows = response.FlattenRows();
            return rows != null && rows.Count > 0
                ? new ProgressRowQuery(
                    response,
                    ParseProgressRow(rows[0]),
                    true)
                : new ProgressRowQuery(response, default, false);
        }

        private async Task<LoadoutQuery> LoadLoadoutAsync()
        {
            BackendReturnObject response = await RunRequest(
                callback => BackndApi.GameData.GetMyData(
                    LoadoutTableName,
                    new Where(),
                    1,
                    callback));
            if (!response.IsSuccess())
            {
                return new LoadoutQuery(
                    response,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    false);
            }

            JsonData rows = response.FlattenRows();
            if (rows == null || rows.Count == 0)
            {
                return new LoadoutQuery(
                    response,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    false);
            }

            JsonData row = rows[0];
            return new LoadoutQuery(
                response,
                GetString(row, "inDate"),
                CharacterIdMigration.Normalize(
                    GetString(row, "selectedCharacterId")),
                CharacterIdMigration.Normalize(
                    GetString(row, "equippedCharacterId")),
                true);
        }

        private async Task<LedgerQuery> FindLedgerAsync(string requestId)
        {
            Where where = new Where();
            where.Equal("requestId", requestId);
            BackendReturnObject response = await RunRequest(
                callback => BackndApi.GameData.GetMyData(
                    LedgerTableName,
                    where,
                    1,
                    callback));
            if (!response.IsSuccess())
            {
                return new LedgerQuery(response, false);
            }

            JsonData rows = response.FlattenRows();
            return new LedgerQuery(
                response,
                rows != null && rows.Count > 0);
        }

        private static List<CharacterProgressionRecord> MergeRecords(
            IReadOnlyList<ProgressRow> serverRows,
            IReadOnlyList<CharacterProgressionRecord> localRecords)
        {
            Dictionary<string, CharacterProgressionRecord> merged =
                new Dictionary<string, CharacterProgressionRecord>(
                    StringComparer.Ordinal);
            if (serverRows != null)
            {
                for (int i = 0; i < serverRows.Count; i++)
                {
                    CharacterProgressionRecord record =
                        serverRows[i].Record;
                    merged[record.CharacterId] = record;
                }
            }

            if (localRecords != null)
            {
                for (int i = 0; i < localRecords.Count; i++)
                {
                    CharacterProgressionRecord record = localRecords[i];
                    if (CharacterProgressionCatalog.Contains(
                        record.CharacterId)
                        && !merged.ContainsKey(record.CharacterId))
                    {
                        merged.Add(record.CharacterId, record);
                    }
                }
            }

            List<CharacterProgressionRecord> result =
                new List<CharacterProgressionRecord>();
            IReadOnlyList<CharacterProgressionRecord> catalog =
                CharacterProgressionCatalog.Records;
            for (int i = 0; i < catalog.Count; i++)
            {
                CharacterProgressionRecord fallback = catalog[i];
                result.Add(merged.TryGetValue(
                    fallback.CharacterId,
                    out CharacterProgressionRecord record)
                    ? record
                    : fallback);
            }

            return result;
        }

        private List<TransactionValue>
            CreateSynchronizationTransactions(
                IReadOnlyList<ProgressRow> serverRows,
                LoadoutQuery loadoutQuery,
                IReadOnlyList<CharacterProgressionRecord> mergedRecords,
                string selectedCharacterId,
                string equippedCharacterId)
        {
            HashSet<string> existingIds = new HashSet<string>(
                StringComparer.Ordinal);
            for (int i = 0; i < serverRows.Count; i++)
            {
                existingIds.Add(serverRows[i].Record.CharacterId);
            }

            List<TransactionValue> transactions =
                new List<TransactionValue>();
            for (int i = 0; i < mergedRecords.Count; i++)
            {
                CharacterProgressionRecord record = mergedRecords[i];
                if (!existingIds.Contains(record.CharacterId))
                {
                    transactions.Add(TransactionValue.SetInsert(
                        ProgressTableName,
                        CreateProgressParam(record)));
                }
            }

            if (!loadoutQuery.Exists)
            {
                transactions.Add(TransactionValue.SetInsert(
                    LoadoutTableName,
                    CreateLoadoutParam(
                        selectedCharacterId,
                        equippedCharacterId)));
            }
            else if (!string.Equals(
                loadoutQuery.SelectedCharacterId,
                selectedCharacterId,
                StringComparison.Ordinal)
                || !string.Equals(
                    loadoutQuery.EquippedCharacterId,
                    equippedCharacterId,
                    StringComparison.Ordinal))
            {
                transactions.Add(TransactionValue.SetUpdateV2(
                    LoadoutTableName,
                    loadoutQuery.RowInDate,
                    userId,
                    CreateLoadoutParam(
                        selectedCharacterId,
                        equippedCharacterId)));
            }

            return transactions;
        }

        private static void NormalizeLoadout(
            IReadOnlyList<CharacterProgressionRecord> records,
            ref string selectedCharacterId,
            ref string equippedCharacterId)
        {
            selectedCharacterId =
                CharacterIdMigration.Normalize(selectedCharacterId);
            equippedCharacterId =
                CharacterIdMigration.Normalize(equippedCharacterId);
            if (!IsOwned(records, equippedCharacterId))
            {
                equippedCharacterId = FindFirstOwned(records);
            }

            if (!IsOwned(records, selectedCharacterId))
            {
                selectedCharacterId = equippedCharacterId;
            }
        }

        private static bool IsOwned(
            IReadOnlyList<CharacterProgressionRecord> records,
            string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
            {
                return false;
            }

            for (int i = 0; i < records.Count; i++)
            {
                if (string.Equals(
                    records[i].CharacterId,
                    characterId,
                    StringComparison.Ordinal))
                {
                    return records[i].IsOwned;
                }
            }

            return false;
        }

        private static string FindFirstOwned(
            IReadOnlyList<CharacterProgressionRecord> records)
        {
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].IsOwned)
                {
                    return records[i].CharacterId;
                }
            }

            return string.Empty;
        }

        private static ProgressRow ParseProgressRow(JsonData row)
        {
            string characterId = CharacterIdMigration.Normalize(
                GetString(row, "characterId"));
            return new ProgressRow(
                GetString(row, "inDate"),
                new CharacterProgressionRecord(
                    characterId,
                    Mathf.Clamp(
                        ParseInt(GetString(row, "level"), 1),
                        1,
                        CharacterDefinition.MaximumCharacterLevel),
                    ParseInt(
                        GetString(row, "currentExperience"),
                        0),
                    ParseBool(GetString(row, "isOwned")),
                    false));
        }

        private static Param CreateProgressParam(
            CharacterProgressionRecord record)
        {
            Param param = new Param();
            param.Add("schemaVersion", SchemaVersion);
            param.Add("migrationVersion", MigrationVersion);
            param.Add("characterId", record.CharacterId);
            param.Add("level", record.Level);
            param.Add("currentExperience", record.CurrentExperience);
            param.Add("isOwned", record.IsOwned);
            return param;
        }

        private static Param CreateLoadoutParam(
            string selectedCharacterId,
            string equippedCharacterId)
        {
            Param param = new Param();
            param.Add("schemaVersion", SchemaVersion);
            param.Add("migrationVersion", MigrationVersion);
            param.Add(
                "selectedCharacterId",
                selectedCharacterId ?? string.Empty);
            param.Add(
                "equippedCharacterId",
                equippedCharacterId ?? string.Empty);
            return param;
        }

        private static Param CreateLedgerParam(
            CharacterExperienceRequest request,
            CharacterProgressionRecord record)
        {
            Param param = new Param();
            param.Add("schemaVersion", SchemaVersion);
            param.Add("requestId", request.RequestId);
            param.Add("characterId", request.CharacterId);
            param.Add("experienceDelta", request.ExperienceDelta);
            param.Add("levelAfter", record.Level);
            param.Add("experienceAfter", record.CurrentExperience);
            param.Add("reason", request.Reason);
            param.Add("runId", request.RunId);
            param.Add(
                "createdAt",
                string.IsNullOrWhiteSpace(request.CreatedAt)
                    ? CreateTimestamp()
                    : request.CreatedAt);
            return param;
        }

        private static bool IsProgressBehind(
            int level,
            int experience,
            CharacterProgressionRecord serverRecord)
        {
            return level < serverRecord.Level
                || (level == serverRecord.Level
                    && experience < serverRecord.CurrentExperience);
        }

        private static Task<BackendReturnObject> RunRequest(
            Action<BackndApi.BackendCallback> request)
        {
            TaskCompletionSource<BackendReturnObject> source = new();
            try
            {
                request(response =>
                    BackndSdkManager.PostToMainThread(
                        () => source.TrySetResult(response)));
            }
            catch (Exception exception)
            {
                source.TrySetException(exception);
            }

            return source.Task;
        }

        private static string GetString(JsonData data, string key)
        {
            if (data == null
                || !data.IsObject
                || !data.Keys.Contains(key)
                || data[key] == null)
            {
                return string.Empty;
            }

            return data[key].ToString();
        }

        private static int ParseInt(string value, int fallback)
        {
            return int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int result)
                ? Math.Max(0, result)
                : fallback;
        }

        private static bool ParseBool(string value)
        {
            return bool.TryParse(value, out bool result)
                ? result
                : string.Equals(value, "1", StringComparison.Ordinal);
        }

        private static string CreateTimestamp()
        {
            return DateTimeOffset.UtcNow.ToString(
                "O",
                CultureInfo.InvariantCulture);
        }

        private static CharacterProgressionSynchronizationResult
            CreateSynchronizationError(
                CharacterProgressionServerSnapshot snapshot,
                BackendReturnObject response)
        {
            return CreateSynchronizationError(
                snapshot,
                response?.ToString()
                ?? "BackND character progression request failed.");
        }

        private static CharacterProgressionSynchronizationResult
            CreateSynchronizationError(
                CharacterProgressionServerSnapshot snapshot,
                string message)
        {
            return new CharacterProgressionSynchronizationResult(
                false,
                false,
                snapshot,
                message);
        }

        private static CharacterProgressionOperationResult
            CreateOperationError(BackendReturnObject response)
        {
            return CreateOperationError(
                response?.ToString()
                ?? "BackND character progression request failed.");
        }

        private static CharacterProgressionOperationResult
            CreateOperationError(string message)
        {
            return CreateOperationResult(
                CharacterProgressionOperationState.Error,
                default,
                false,
                message);
        }

        private static CharacterProgressionOperationResult
            CreateOperationResult(
                CharacterProgressionOperationState state,
                CharacterProgressionRecord record,
                bool hasAuthoritativeRecord,
                string message)
        {
            return new CharacterProgressionOperationResult(
                state,
                record,
                hasAuthoritativeRecord,
                message);
        }

        private static CharacterProgressionMutationResult
            CreateMutationError(BackendReturnObject response)
        {
            return CreateMutationError(
                response?.ToString()
                ?? "BackND character progression request failed.");
        }

        private static CharacterProgressionMutationResult
            CreateMutationError(string message)
        {
            return new CharacterProgressionMutationResult(false, message);
        }

        private readonly struct ProgressRow
        {
            public ProgressRow(
                string rowInDate,
                CharacterProgressionRecord record)
            {
                RowInDate = rowInDate ?? string.Empty;
                Record = record;
            }

            public string RowInDate { get; }
            public CharacterProgressionRecord Record { get; }
        }

        private readonly struct ProgressRowsQuery
        {
            public ProgressRowsQuery(
                BackendReturnObject response,
                IReadOnlyList<ProgressRow> rows)
            {
                Response = response;
                Rows = rows;
            }

            public BackendReturnObject Response { get; }
            public IReadOnlyList<ProgressRow> Rows { get; }
        }

        private readonly struct ProgressRowQuery
        {
            public ProgressRowQuery(
                BackendReturnObject response,
                ProgressRow row,
                bool exists)
            {
                Response = response;
                Row = row;
                Exists = exists;
            }

            public BackendReturnObject Response { get; }
            public ProgressRow Row { get; }
            public bool Exists { get; }
        }

        private readonly struct LoadoutQuery
        {
            public LoadoutQuery(
                BackendReturnObject response,
                string rowInDate,
                string selectedCharacterId,
                string equippedCharacterId,
                bool exists)
            {
                Response = response;
                RowInDate = rowInDate ?? string.Empty;
                SelectedCharacterId = selectedCharacterId ?? string.Empty;
                EquippedCharacterId = equippedCharacterId ?? string.Empty;
                Exists = exists;
            }

            public BackendReturnObject Response { get; }
            public string RowInDate { get; }
            public string SelectedCharacterId { get; }
            public string EquippedCharacterId { get; }
            public bool Exists { get; }
        }

        private readonly struct LedgerQuery
        {
            public LedgerQuery(
                BackendReturnObject response,
                bool exists)
            {
                Response = response;
                Exists = exists;
            }

            public BackendReturnObject Response { get; }
            public bool Exists { get; }
        }
    }
}
