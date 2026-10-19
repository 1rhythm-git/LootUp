using System;
using LootUp.Core.Currency;
using LootUp.Core.Profile;
using UnityEngine;

namespace LootUp.Core.Characters
{
    public enum CharacterAcquisitionType
    {
        GameMoney,
        RewardedAd,
        Ruby
    }

    [Serializable]
    public sealed class CharacterAcquisitionOption
    {
        [SerializeField]
        private CharacterAcquisitionType acquisitionType;

        [SerializeField]
        private bool enabled;

        [SerializeField, Min(1)]
        [Tooltip("재화 가격 또는 광고 누적 완료 횟수입니다.")]
        private int amount = 1;

        public CharacterAcquisitionType AcquisitionType => acquisitionType;
        public bool Enabled => enabled;
        public int Amount => Mathf.Max(1, amount);
        public bool UsesCurrency =>
            acquisitionType == CharacterAcquisitionType.GameMoney
            || acquisitionType == CharacterAcquisitionType.Ruby;
        public UserCurrencyType CurrencyType =>
            acquisitionType == CharacterAcquisitionType.Ruby
                ? UserCurrencyType.Ruby
                : UserCurrencyType.GameMoney;
    }

    public enum CharacterAcquisitionState
    {
        Acquired,
        Duplicate,
        AlreadyOwned,
        Disabled,
        RewardedAdRequired,
        InsufficientFunds,
        InvalidRequest,
        Unavailable,
        Error
    }

    public readonly struct CharacterAcquisitionRequest
    {
        public CharacterAcquisitionRequest(
            string requestId,
            string characterId,
            CharacterAcquisitionType acquisitionType,
            int amount)
        {
            RequestId = requestId?.Trim() ?? string.Empty;
            CharacterId = CharacterIdMigration.Normalize(characterId);
            AcquisitionType = acquisitionType;
            Amount = Math.Max(0, amount);
        }

        public string RequestId { get; }
        public string CharacterId { get; }
        public CharacterAcquisitionType AcquisitionType { get; }
        public int Amount { get; }
        public bool IsValid =>
            !string.IsNullOrWhiteSpace(RequestId)
            && !string.IsNullOrWhiteSpace(CharacterId)
            && Amount > 0
            && (AcquisitionType == CharacterAcquisitionType.GameMoney
                || AcquisitionType == CharacterAcquisitionType.Ruby);
    }

    public readonly struct CharacterAcquisitionResult
    {
        public CharacterAcquisitionResult(
            CharacterAcquisitionState state,
            CurrencyBalanceSnapshot balances,
            bool hasAuthoritativeBalances,
            string message)
        {
            State = state;
            Balances = balances;
            HasAuthoritativeBalances = hasAuthoritativeBalances;
            Message = message ?? string.Empty;
        }

        public CharacterAcquisitionState State { get; }
        public CurrencyBalanceSnapshot Balances { get; }
        public bool HasAuthoritativeBalances { get; }
        public string Message { get; }
        public bool Completed =>
            State == CharacterAcquisitionState.Acquired
            || State == CharacterAcquisitionState.Duplicate
            || State == CharacterAcquisitionState.AlreadyOwned;
    }
}
