using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace LootUp.Core.Characters
{
    public sealed class PlayerCharacterRuntime : MonoBehaviour
    {
        [SerializeField]
        private CharacterDefinition characterDefinition;

        [SerializeField]
        [FormerlySerializedAs("boosterGauge")]
        private float feverGauge;

        [SerializeField]
        private float feverDurationSeconds = 8f;

        private float feverRemainingSeconds;
        private bool isFeverActive;
        private CharacterUpgradeModifiers upgradeModifiers;

        public event Action<float> FeverGaugeChanged;
        public event Action<float> BoosterGaugeChanged;
        public event Action<float> FeverStarted;
        public event Action FeverEnded;

        public CharacterDefinition CharacterDefinition => characterDefinition;
        public float MoveSpeedColumnsPerSecond => characterDefinition != null
            ? characterDefinition.MoveSpeedColumnsPerSecond * (1f + upgradeModifiers.MoveSpeedBonusPercent * 0.01f)
            : 4f;
        public float PivotCooldownSeconds => characterDefinition != null ? characterDefinition.PivotCooldownSeconds : 0f;
        public int MaxLife => characterDefinition != null ? characterDefinition.MaxLife + upgradeModifiers.MaxLifeBonus : 3;
        public float InstantItemAcquireChance => characterDefinition != null
            ? Mathf.Clamp01(characterDefinition.InstantItemAcquireChance + upgradeModifiers.InstantItemAcquireChanceBonusPercent * 0.01f)
            : 0f;
        public float CollectionItemChanceBonusPercent => characterDefinition != null
            ? characterDefinition.CollectionItemChanceBonusPercent + upgradeModifiers.CollectionItemChanceBonusPercent
            : 0f;
        public float ItemChance => CharacterProgressionState.GetItemChance(characterDefinition);
        public CharacterProgressionSnapshot Progression => CharacterProgressionState.GetSnapshot(characterDefinition);
        public bool IsLevelSkillUnlocked => CharacterProgressionState.IsSkillUnlocked(characterDefinition);
        public float SkillItemPageSpawnChance => ItemChance;
        public float FeverGauge => feverGauge;
        public float FeverGaugeMax => characterDefinition != null ? characterDefinition.FeverGaugeMax : 100f;
        public float FeverGaugeNormalized => FeverGaugeMax <= 0f ? 0f : Mathf.Clamp01(feverGauge / FeverGaugeMax);
        public bool IsFeverActive => isFeverActive;
        public float FeverDurationSeconds => Mathf.Max(0.1f, feverDurationSeconds);
        public float FeverRemainingSeconds => Mathf.Max(0f, feverRemainingSeconds);
        public float FeverRemainingNormalized => !isFeverActive
            ? 0f
            : Mathf.Clamp01(feverRemainingSeconds / FeverDurationSeconds);
        public float BoosterGauge => FeverGauge;
        public float BoosterGaugeMax => FeverGaugeMax;
        public float BoosterGaugeNormalized => FeverGaugeNormalized;

        private void Update()
        {
            if (!isFeverActive)
            {
                return;
            }

            feverRemainingSeconds = Mathf.Max(0f, feverRemainingSeconds - Time.deltaTime);
            if (feverRemainingSeconds <= 0f)
            {
                EndFever();
            }
        }

        public void Configure(CharacterDefinition definition)
        {
            characterDefinition = definition;
            upgradeModifiers = CharacterUpgradeResolver.Resolve(definition);
            feverGauge = 0f;
            feverRemainingSeconds = 0f;
            isFeverActive = false;
            NotifyFeverGaugeChanged();
        }

        public void AddMoveDistanceColumns(float movedColumns)
        {
            float gainPerColumn = characterDefinition != null ? characterDefinition.FeverGainPerColumn : 0f;
            AddFeverGauge(CharacterLevelGrowthPolicy.ApplyFeverGain(
                Mathf.Max(0f, movedColumns) * gainPerColumn,
                Progression.Level));
        }

        public void AddPivotCharge()
        {
            float gain = characterDefinition != null ? characterDefinition.FeverGainPerPivot : 0f;
            AddFeverGauge(CharacterLevelGrowthPolicy.ApplyFeverGain(gain, Progression.Level));
        }

        public void FillFeverGaugeForTest()
        {
            if (isFeverActive)
            {
                return;
            }

            AddFeverGauge(FeverGaugeMax);
        }

        public float AddFeverGaugeFromItem(float amount)
        {
            if (amount <= 0f || isFeverActive)
            {
                return 0f;
            }

            float adjustedAmount = CharacterLevelGrowthPolicy.ApplySupportItemEffect(
                amount,
                Progression.Level);
            float addedAmount = Mathf.Min(adjustedAmount, Mathf.Max(0f, FeverGaugeMax - feverGauge));
            AddFeverGauge(adjustedAmount);
            return addedAmount;
        }

        public bool RollInstantItemAcquire()
        {
            float chance = Mathf.Clamp01(InstantItemAcquireChance);
            return chance > 0f && UnityEngine.Random.value <= chance;
        }

        private void AddFeverGauge(float amount)
        {
            if (amount <= 0f || isFeverActive)
            {
                return;
            }

            feverGauge = Mathf.Min(FeverGaugeMax, feverGauge + amount);
            NotifyFeverGaugeChanged();

            if (feverGauge >= FeverGaugeMax)
            {
                StartFever();
            }
        }

        private void StartFever()
        {
            if (isFeverActive)
            {
                return;
            }

            isFeverActive = true;
            feverRemainingSeconds = FeverDurationSeconds;
            feverGauge = 0f;
            NotifyFeverGaugeChanged();
            FeverStarted?.Invoke(FeverDurationSeconds);
            Debug.Log($"Fever Started: {(characterDefinition != null ? characterDefinition.FeverBuffKey : "Undefined")}", this);
        }

        private void EndFever()
        {
            if (!isFeverActive)
            {
                return;
            }

            isFeverActive = false;
            feverRemainingSeconds = 0f;
            FeverEnded?.Invoke();
            Debug.Log("Fever Ended", this);
        }

        private void NotifyFeverGaugeChanged()
        {
            FeverGaugeChanged?.Invoke(FeverGaugeNormalized);
            BoosterGaugeChanged?.Invoke(FeverGaugeNormalized);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            feverDurationSeconds = Mathf.Max(0.1f, feverDurationSeconds);
        }
#endif
    }
}
