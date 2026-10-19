using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BackEnd;
using LitJson;
using LootUp.Core.Backend;
using LootUp.Core.Currency;
using LootUp.Core.Profile;
using UnityEngine;
using BackndApi = BackEnd.Backend;

namespace LootUp.Core.Characters
{
    public sealed class BackndCharacterAcquisitionService :
        ICharacterAcquisitionService
    {
        private const int SchemaVersion = 1;
        private const int MigrationVersion = 1;

        private readonly string userId;
        private readonly SemaphoreSlim operationGate =
            new SemaphoreSlim(1, 1);

        public BackndCharacterAcquisitionService(string userId)
        {
            this.userId = userId?.Trim() ?? string.Empty;
        }

        public bool IsOnline =>
            !string.IsNullOrWhiteSpace(userId)
            && BackndSdkManager.State
            == BackndInitializationState.Initialized;

        public async Task<CharacterAcquisitionResult> AcquireAsync(
            CharacterAcquisitionRequest request)
        {
            if (!request.IsValid
                || !CharacterProgressionCatalog.Contains(
                    request.CharacterId))
            {
                return CreateResult(
                    CharacterAcquisitionState.InvalidRequest,
                    default,
                    false,
                    "Character acquisition request is invalid.");
            }

            await operationGate.WaitAsync();
            try
            {
                if (!IsOnline)
                {
                    return CreateResult(
                        CharacterAcquisitionState.Unavailable,
                        default,
                        false,
                        "BackND character acquisition service is offline.");
                }

                CurrencyProfileQuery profile = await LoadProfileAsync();
                if (!profile.Response.IsSuccess() || !profile.Exists)
                {
                    return CreateError(
                        profile.Response,
                        "Currency profile does not exist.");
                }

                CharacterProgressQuery character =
                    await LoadCharacterAsync(request.CharacterId);
                if (!character.Response.IsSuccess())
                {
                    return CreateError(character.Response);
                }

                if (character.Exists && character.IsOwned)
                {
                    return CreateResult(
                        CharacterAcquisitionState.AlreadyOwned,
                        profile.Balances,
                        true,
                        string.Empty);
                }

                LedgerQuery ledger =
                    await FindLedgerAsync(request.RequestId);
                if (!ledger.Response.IsSuccess())
                {
                    return CreateError(ledger.Response);
                }

                if (ledger.Exists)
                {
                    BackendReturnObject recoveryResponse = await RunRequest(
                        callback => BackndApi.GameData.TransactionWriteV2(
                            new List<TransactionValue>
                            {
                                CreateCharacterTransaction(
                                    character,
                                    request.CharacterId)
                            },
                            callback));
                    return recoveryResponse.IsSuccess()
                        ? CreateResult(
                            CharacterAcquisitionState.Duplicate,
                            profile.Balances,
                            true,
                            string.Empty)
                        : CreateError(recoveryResponse);
                }

                UserCurrencyType currencyType =
                    request.AcquisitionType
                    == CharacterAcquisitionType.Ruby
                        ? UserCurrencyType.Ruby
                        : UserCurrencyType.GameMoney;
                int previousAmount =
                    profile.Balances.GetAmount(currencyType);
                if (previousAmount < request.Amount)
                {
                    return CreateResult(
                        CharacterAcquisitionState.InsufficientFunds,
                        profile.Balances,
                        true,
                        "Currency balance is insufficient.");
                }

                int nextAmount = previousAmount - request.Amount;
                CurrencyBalanceSnapshot nextBalances =
                    currencyType == UserCurrencyType.Ruby
                        ? new CurrencyBalanceSnapshot(
                            profile.Balances.GameMoney,
                            nextAmount)
                        : new CurrencyBalanceSnapshot(
                            nextAmount,
                            profile.Balances.Ruby);
                string timestamp = DateTimeOffset.UtcNow.ToString(
                    "O",
                    CultureInfo.InvariantCulture);
                List<TransactionValue> transactions =
                    new List<TransactionValue>
                    {
                        TransactionValue.SetUpdateV2(
                            BackndCurrencyLedgerService.ProfileTableName,
                            profile.RowInDate,
                            userId,
                            CreateProfileParam(
                                nextBalances,
                                request.RequestId)),
                        TransactionValue.SetInsert(
                            BackndCurrencyLedgerService.LedgerTableName,
                            CreateLedgerParam(
                                request,
                                currencyType,
                                nextAmount,
                                timestamp)),
                        CreateCharacterTransaction(character, request.CharacterId)
                    };
                BackendReturnObject response = await RunRequest(
                    callback => BackndApi.GameData.TransactionWriteV2(
                        transactions,
                        callback));
                return response.IsSuccess()
                    ? CreateResult(
                        CharacterAcquisitionState.Acquired,
                        nextBalances,
                        true,
                        string.Empty)
                    : CreateError(response);
            }
            catch (Exception exception)
            {
                return CreateResult(
                    CharacterAcquisitionState.Error,
                    default,
                    false,
                    exception.Message);
            }
            finally
            {
                operationGate.Release();
            }
        }

        private async Task<CurrencyProfileQuery> LoadProfileAsync()
        {
            BackendReturnObject response = await RunRequest(
                callback => BackndApi.GameData.GetMyData(
                    BackndCurrencyLedgerService.ProfileTableName,
                    new Where(),
                    1,
                    callback));
            if (!response.IsSuccess())
            {
                return new CurrencyProfileQuery(
                    response,
                    string.Empty,
                    default,
                    false);
            }

            JsonData rows = response.FlattenRows();
            if (rows == null || rows.Count == 0)
            {
                return new CurrencyProfileQuery(
                    response,
                    string.Empty,
                    default,
                    false);
            }

            JsonData row = rows[0];
            return new CurrencyProfileQuery(
                response,
                GetString(row, "inDate"),
                new CurrencyBalanceSnapshot(
                    ParseInt(GetString(row, "gameMoney")),
                    ParseInt(GetString(row, "ruby"))),
                true);
        }

        private async Task<CharacterProgressQuery> LoadCharacterAsync(
            string characterId)
        {
            Where where = new Where();
            where.Equal("characterId", characterId);
            BackendReturnObject response = await RunRequest(
                callback => BackndApi.GameData.GetMyData(
                    BackndCharacterProgressionService.ProgressTableName,
                    where,
                    1,
                    callback));
            if (!response.IsSuccess())
            {
                return new CharacterProgressQuery(
                    response,
                    string.Empty,
                    1,
                    0,
                    false,
                    false);
            }

            JsonData rows = response.FlattenRows();
            if (rows == null || rows.Count == 0)
            {
                return new CharacterProgressQuery(
                    response,
                    string.Empty,
                    1,
                    0,
                    false,
                    false);
            }

            JsonData row = rows[0];
            return new CharacterProgressQuery(
                response,
                GetString(row, "inDate"),
                Mathf.Clamp(
                    ParseInt(GetString(row, "level")),
                    1,
                    CharacterDefinition.MaximumCharacterLevel),
                ParseInt(GetString(row, "currentExperience")),
                ParseBool(GetString(row, "isOwned")),
                true);
        }

        private async Task<LedgerQuery> FindLedgerAsync(
            string requestId)
        {
            Where where = new Where();
            where.Equal("requestId", requestId);
            BackendReturnObject response = await RunRequest(
                callback => BackndApi.GameData.GetMyData(
                    BackndCurrencyLedgerService.LedgerTableName,
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

        private TransactionValue CreateCharacterTransaction(
            CharacterProgressQuery character,
            string characterId)
        {
            if (character.Exists)
            {
                Param update = new Param();
                update.Add("isOwned", true);
                return TransactionValue.SetUpdateV2(
                    BackndCharacterProgressionService.ProgressTableName,
                    character.RowInDate,
                    userId,
                    update);
            }

            Param insert = new Param();
            insert.Add("schemaVersion", SchemaVersion);
            insert.Add("migrationVersion", MigrationVersion);
            insert.Add("characterId", characterId);
            insert.Add("level", character.Level);
            insert.Add("currentExperience", character.Experience);
            insert.Add("isOwned", true);
            return TransactionValue.SetInsert(
                BackndCharacterProgressionService.ProgressTableName,
                insert);
        }

        private static Param CreateProfileParam(
            CurrencyBalanceSnapshot balances,
            string requestId)
        {
            Param param = new Param();
            param.Add("schemaVersion", SchemaVersion);
            param.Add("migrationVersion", MigrationVersion);
            param.Add("gameMoney", balances.GameMoney);
            param.Add("ruby", balances.Ruby);
            param.Add("lastRequestId", requestId ?? string.Empty);
            return param;
        }

        private static Param CreateLedgerParam(
            CharacterAcquisitionRequest request,
            UserCurrencyType currencyType,
            int balanceAfter,
            string timestamp)
        {
            Param param = new Param();
            param.Add("schemaVersion", SchemaVersion);
            param.Add("requestId", request.RequestId);
            param.Add("currencyType", currencyType.ToString());
            param.Add("deltaAmount", -request.Amount);
            param.Add("balanceAfter", Math.Max(0, balanceAfter));
            param.Add("reason", "character_purchase");
            param.Add("runId", string.Empty);
            param.Add("characterId", request.CharacterId);
            param.Add("createdAt", timestamp);
            return param;
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

        private static CharacterAcquisitionResult CreateError(
            BackendReturnObject response,
            string fallback = "BackND character acquisition failed.")
        {
            string message = response != null && !response.IsSuccess()
                ? response.ToString()
                : fallback;
            return CreateResult(
                CharacterAcquisitionState.Error,
                default,
                false,
                message);
        }

        private static CharacterAcquisitionResult CreateResult(
            CharacterAcquisitionState state,
            CurrencyBalanceSnapshot balances,
            bool hasAuthoritativeBalances,
            string message)
        {
            return new CharacterAcquisitionResult(
                state,
                balances,
                hasAuthoritativeBalances,
                message);
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

        private static int ParseInt(string value)
        {
            return int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int result)
                ? Math.Max(0, result)
                : 0;
        }

        private static bool ParseBool(string value)
        {
            return bool.TryParse(value, out bool result)
                ? result
                : string.Equals(value, "1", StringComparison.Ordinal);
        }

        private readonly struct CurrencyProfileQuery
        {
            public CurrencyProfileQuery(
                BackendReturnObject response,
                string rowInDate,
                CurrencyBalanceSnapshot balances,
                bool exists)
            {
                Response = response;
                RowInDate = rowInDate ?? string.Empty;
                Balances = balances;
                Exists = exists;
            }

            public BackendReturnObject Response { get; }
            public string RowInDate { get; }
            public CurrencyBalanceSnapshot Balances { get; }
            public bool Exists { get; }
        }

        private readonly struct CharacterProgressQuery
        {
            public CharacterProgressQuery(
                BackendReturnObject response,
                string rowInDate,
                int level,
                int experience,
                bool isOwned,
                bool exists)
            {
                Response = response;
                RowInDate = rowInDate ?? string.Empty;
                Level = level;
                Experience = experience;
                IsOwned = isOwned;
                Exists = exists;
            }

            public BackendReturnObject Response { get; }
            public string RowInDate { get; }
            public int Level { get; }
            public int Experience { get; }
            public bool IsOwned { get; }
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
