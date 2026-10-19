using System;
using System.Threading;
using System.Threading.Tasks;
using LootUp.Core.Currency;
using LootUp.Core.Profile;

namespace LootUp.Core.Characters
{
    public static class CharacterAcquisitionManager
    {
        private static readonly SemaphoreSlim OperationGate =
            new SemaphoreSlim(1, 1);

        private static ICharacterAcquisitionService service;
        private static int configurationVersion;

        public static void Configure(
            ICharacterAcquisitionService acquisitionService)
        {
            Interlocked.Increment(ref configurationVersion);
            service = acquisitionService;
        }

        public static bool CanPurchase(CharacterDefinition definition)
        {
            if (definition == null
                || CharacterProgressionState.IsOwned(definition))
            {
                return false;
            }

            CharacterAcquisitionOption option =
                definition.GetPrimaryAcquisitionOption();
            if (option == null
                || !option.Enabled
                || !option.UsesCurrency)
            {
                return false;
            }

            if (service != null && !service.IsOnline)
            {
                return false;
            }

            int balance = option.CurrencyType == UserCurrencyType.Ruby
                ? UserProfileManager.Ruby
                : UserProfileManager.GameMoney;
            return balance >= option.Amount;
        }

        public static async Task<CharacterAcquisitionResult> PurchaseAsync(
            CharacterDefinition definition)
        {
            CharacterAcquisitionOption option =
                definition != null
                    ? definition.GetPrimaryAcquisitionOption()
                    : null;
            if (definition == null
                || string.IsNullOrWhiteSpace(definition.CharacterId)
                || option == null)
            {
                return CreateResult(
                    CharacterAcquisitionState.InvalidRequest,
                    "Character acquisition data is invalid.");
            }

            if (CharacterProgressionState.IsOwned(definition))
            {
                return CreateResult(
                    CharacterAcquisitionState.AlreadyOwned,
                    string.Empty);
            }

            if (!option.Enabled)
            {
                return CreateResult(
                    CharacterAcquisitionState.Disabled,
                    "Character acquisition option is disabled.");
            }

            if (option.AcquisitionType
                == CharacterAcquisitionType.RewardedAd)
            {
                return CreateResult(
                    CharacterAcquisitionState.RewardedAdRequired,
                    "Rewarded ad integration is required.");
            }

            CharacterAcquisitionRequest request =
                new CharacterAcquisitionRequest(
                    $"character-purchase:v1:{definition.CharacterId}",
                    definition.CharacterId,
                    option.AcquisitionType,
                    option.Amount);
            if (!request.IsValid)
            {
                return CreateResult(
                    CharacterAcquisitionState.InvalidRequest,
                    "Character acquisition request is invalid.");
            }

            await OperationGate.WaitAsync();
            try
            {
                if (CharacterProgressionState.IsOwned(definition))
                {
                    return CreateResult(
                        CharacterAcquisitionState.AlreadyOwned,
                        string.Empty);
                }

                ICharacterAcquisitionService activeService = service;
                int activeVersion = configurationVersion;
                if (activeService == null)
                {
                    return PurchaseLocally(definition, option);
                }

                if (!activeService.IsOnline)
                {
                    return CreateResult(
                        CharacterAcquisitionState.Unavailable,
                        "Character purchase requires an online connection.");
                }

                CharacterAcquisitionResult result =
                    await activeService.AcquireAsync(request);
                if (activeVersion != configurationVersion)
                {
                    return CreateResult(
                        CharacterAcquisitionState.Error,
                        "Character account changed while purchasing.");
                }

                if (!result.Completed)
                {
                    if (result.HasAuthoritativeBalances)
                    {
                        ApplyBalances(result.Balances);
                    }

                    return result;
                }

                if (result.HasAuthoritativeBalances)
                {
                    ApplyBalances(result.Balances);
                }

                if (!CharacterProgressionState
                    .ApplyAuthoritativeOwnership(definition, true))
                {
                    return new CharacterAcquisitionResult(
                        CharacterAcquisitionState.Error,
                        result.Balances,
                        result.HasAuthoritativeBalances,
                        "Purchased character could not be cached locally.");
                }

                return result;
            }
            catch (Exception exception)
            {
                return CreateResult(
                    CharacterAcquisitionState.Error,
                    exception.Message);
            }
            finally
            {
                OperationGate.Release();
            }
        }

        private static CharacterAcquisitionResult PurchaseLocally(
            CharacterDefinition definition,
            CharacterAcquisitionOption option)
        {
            UserCurrencyChangeResult spend =
                UserProfileManager.TrySpendCurrency(
                    option.CurrencyType,
                    option.Amount);
            if (!spend.Applied)
            {
                return CreateResult(
                    CharacterAcquisitionState.InsufficientFunds,
                    "Currency balance is insufficient.");
            }

            if (!CharacterProgressionState.TrySetOwned(definition, true))
            {
                UserProfileManager.AddCurrency(
                    option.CurrencyType,
                    option.Amount);
                return CreateResult(
                    CharacterAcquisitionState.Error,
                    "Character ownership could not be saved.");
            }

            return new CharacterAcquisitionResult(
                CharacterAcquisitionState.Acquired,
                GetBalances(),
                true,
                string.Empty);
        }

        private static void ApplyBalances(
            CurrencyBalanceSnapshot balances)
        {
            UserProfileManager.ApplyAuthoritativeCurrencyBalances(
                balances.GameMoney,
                balances.Ruby);
        }

        private static CharacterAcquisitionResult CreateResult(
            CharacterAcquisitionState state,
            string message)
        {
            return new CharacterAcquisitionResult(
                state,
                GetBalances(),
                true,
                message);
        }

        private static CurrencyBalanceSnapshot GetBalances()
        {
            return new CurrencyBalanceSnapshot(
                UserProfileManager.GameMoney,
                UserProfileManager.Ruby);
        }
    }
}
