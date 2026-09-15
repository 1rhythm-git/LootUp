using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BackEnd;
using LitJson;
using LootUp.Core.Backend;
using LootUp.Core.Profile;
using UnityEngine;
using BackndApi = BackEnd.Backend;

namespace LootUp.Core.Currency
{
    public sealed class BackndCurrencyLedgerService : ICurrencyLedgerService
    {
        public const string ProfileTableName = "LootUpPlayerProfile";
        public const string LedgerTableName = "LootUpCurrencyLedger";

        private const int SchemaVersion = 1;
        private const int MigrationVersion = 1;

        private readonly string userId;
        private readonly SemaphoreSlim operationGate =
            new SemaphoreSlim(1, 1);

        public BackndCurrencyLedgerService(string userId)
        {
            this.userId = userId?.Trim() ?? string.Empty;
        }

        public bool IsOnline =>
            !string.IsNullOrWhiteSpace(userId)
            && BackndSdkManager.State
            == BackndInitializationState.Initialized;

        public async Task<CurrencySynchronizationResult> SynchronizeAsync(
            CurrencyBalanceSnapshot localBalances)
        {
            await operationGate.WaitAsync();
            try
            {
                if (!IsOnline)
                {
                    return new CurrencySynchronizationResult(
                        false,
                        false,
                        localBalances,
                        "BackND currency service is offline.");
                }

                CurrencyProfileQuery query = await LoadProfileAsync();
                if (!query.Response.IsSuccess())
                {
                    return CreateSynchronizationError(query.Response);
                }

                if (query.Exists)
                {
                    return new CurrencySynchronizationResult(
                        true,
                        false,
                        query.Balances,
                        string.Empty);
                }

                string timestamp = CreateTimestamp();
                List<TransactionValue> transactions =
                    new List<TransactionValue>
                    {
                        TransactionValue.SetInsert(
                            ProfileTableName,
                            CreateProfileParam(
                                localBalances,
                                "migration:v1")),
                        TransactionValue.SetInsert(
                            LedgerTableName,
                            CreateLedgerParam(
                                new CurrencyLedgerRequest(
                                    "migration:v1:game-money",
                                    UserCurrencyType.GameMoney,
                                    localBalances.GameMoney,
                                    "initial_migration",
                                    string.Empty,
                                    timestamp),
                                localBalances.GameMoney,
                                timestamp)),
                        TransactionValue.SetInsert(
                            LedgerTableName,
                            CreateLedgerParam(
                                new CurrencyLedgerRequest(
                                    "migration:v1:ruby",
                                    UserCurrencyType.Ruby,
                                    localBalances.Ruby,
                                    "initial_migration",
                                    string.Empty,
                                    timestamp),
                                localBalances.Ruby,
                                timestamp))
                    };
                BackendReturnObject response = await RunRequest(
                    callback => BackndApi.GameData.TransactionWriteV2(
                        transactions,
                        callback));
                if (!response.IsSuccess())
                {
                    return CreateSynchronizationError(response);
                }

                return new CurrencySynchronizationResult(
                    true,
                    true,
                    localBalances,
                    string.Empty);
            }
            catch (Exception exception)
            {
                return new CurrencySynchronizationResult(
                    false,
                    false,
                    localBalances,
                    exception.Message);
            }
            finally
            {
                operationGate.Release();
            }
        }

        public async Task<CurrencyLedgerOperationResult> ApplyAsync(
            CurrencyLedgerRequest request)
        {
            if (!request.IsValid)
            {
                return CreateOperationResult(
                    CurrencyLedgerOperationState.InvalidRequest,
                    default,
                    false,
                    "Currency request is invalid.");
            }

            await operationGate.WaitAsync();
            try
            {
                if (!IsOnline)
                {
                    return CreateOperationResult(
                        CurrencyLedgerOperationState.Error,
                        default,
                        false,
                        "BackND currency service is offline.");
                }

                LedgerQuery ledgerQuery =
                    await FindLedgerAsync(request.RequestId);
                if (!ledgerQuery.Response.IsSuccess())
                {
                    return CreateOperationError(ledgerQuery.Response);
                }

                CurrencyProfileQuery profileQuery =
                    await LoadProfileAsync();
                if (!profileQuery.Response.IsSuccess())
                {
                    return CreateOperationError(profileQuery.Response);
                }

                if (!profileQuery.Exists)
                {
                    return CreateOperationResult(
                        CurrencyLedgerOperationState.Error,
                        default,
                        false,
                        "Currency profile does not exist.");
                }

                if (ledgerQuery.Exists)
                {
                    return CreateOperationResult(
                        CurrencyLedgerOperationState.Duplicate,
                        profileQuery.Balances,
                        true,
                        string.Empty);
                }

                long previousAmount = profileQuery.Balances.GetAmount(
                    request.CurrencyType);
                long nextAmount = previousAmount + request.DeltaAmount;
                if (nextAmount < 0)
                {
                    return CreateOperationResult(
                        CurrencyLedgerOperationState.InsufficientFunds,
                        profileQuery.Balances,
                        true,
                        "Currency balance is insufficient.");
                }

                if (nextAmount > int.MaxValue)
                {
                    return CreateOperationResult(
                        CurrencyLedgerOperationState.InvalidRequest,
                        profileQuery.Balances,
                        true,
                        "Currency balance exceeds the supported range.");
                }

                CurrencyBalanceSnapshot nextBalances =
                    request.CurrencyType == UserCurrencyType.Ruby
                        ? new CurrencyBalanceSnapshot(
                            profileQuery.Balances.GameMoney,
                            (int)nextAmount)
                        : new CurrencyBalanceSnapshot(
                            (int)nextAmount,
                            profileQuery.Balances.Ruby);
                string timestamp = CreateTimestamp();
                List<TransactionValue> transactions =
                    new List<TransactionValue>
                    {
                        TransactionValue.SetUpdateV2(
                            ProfileTableName,
                            profileQuery.RowInDate,
                            userId,
                            CreateProfileParam(
                                nextBalances,
                                request.RequestId)),
                        TransactionValue.SetInsert(
                            LedgerTableName,
                            CreateLedgerParam(
                                request,
                                (int)nextAmount,
                                timestamp))
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
                    CurrencyLedgerOperationState.Applied,
                    nextBalances,
                    true,
                    string.Empty);
            }
            catch (Exception exception)
            {
                return CreateOperationResult(
                    CurrencyLedgerOperationState.Error,
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
                    ProfileTableName,
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
            if (rows == null || rows.Count <= 0)
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

        private async Task<LedgerQuery> FindLedgerAsync(
            string requestId)
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
            CurrencyLedgerRequest request,
            int balanceAfter,
            string timestamp)
        {
            Param param = new Param();
            param.Add("schemaVersion", SchemaVersion);
            param.Add("requestId", request.RequestId);
            param.Add("currencyType", request.CurrencyType.ToString());
            param.Add("deltaAmount", request.DeltaAmount);
            param.Add("balanceAfter", Math.Max(0, balanceAfter));
            param.Add("reason", request.Reason);
            param.Add("runId", request.RunId);
            param.Add(
                "createdAt",
                string.IsNullOrWhiteSpace(request.CreatedAt)
                    ? timestamp
                    : request.CreatedAt);
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

        private static CurrencySynchronizationResult
            CreateSynchronizationError(BackendReturnObject response)
        {
            return new CurrencySynchronizationResult(
                false,
                false,
                default,
                response?.ToString() ?? "BackND currency request failed.");
        }

        private static CurrencyLedgerOperationResult CreateOperationError(
            BackendReturnObject response)
        {
            return CreateOperationResult(
                CurrencyLedgerOperationState.Error,
                default,
                false,
                response?.ToString() ?? "BackND currency request failed.");
        }

        private static CurrencyLedgerOperationResult CreateOperationResult(
            CurrencyLedgerOperationState state,
            CurrencyBalanceSnapshot balances,
            bool hasAuthoritativeBalances,
            string message)
        {
            return new CurrencyLedgerOperationResult(
                state,
                balances,
                hasAuthoritativeBalances,
                message);
        }

        private static string CreateTimestamp()
        {
            return DateTimeOffset.UtcNow.ToString(
                "O",
                CultureInfo.InvariantCulture);
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
