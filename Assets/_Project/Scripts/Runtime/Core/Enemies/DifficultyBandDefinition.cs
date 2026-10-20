using System;
using System.Collections.Generic;
using UnityEngine;

namespace LootUp.Core.Enemies
{
    [Serializable]
    public sealed class DifficultyBandEntry
    {
        [SerializeField, Min(1)]
        private int startFloor = 1;

        [SerializeField]
        private string role = "Learning";

        [SerializeField, Min(1)]
        private int enemyCount = 4;

        [SerializeField, Min(0)]
        private int dangerousLineCount;

        [SerializeField, Min(0f)]
        private float enemySpeedBonus;

        [SerializeField, Min(0.01f)]
        private float supportItemWeightMultiplier = 1f;

        public int StartFloor => Mathf.Max(1, startFloor);
        public string Role => role ?? string.Empty;
        public int EnemyCount => Mathf.Max(1, enemyCount);
        public int DangerousLineCount => Mathf.Max(0, dangerousLineCount);
        public float EnemySpeedBonus => Mathf.Max(0f, enemySpeedBonus);
        public float SupportItemWeightMultiplier => Mathf.Max(0.01f, supportItemWeightMultiplier);

        internal void SetStartFloor(int value)
        {
            startFloor = Mathf.Max(1, value);
        }
    }

    public readonly struct DifficultyBandValues
    {
        public DifficultyBandValues(
            int startFloor,
            string role,
            int enemyCount,
            int dangerousLineCount,
            float enemySpeedBonus,
            float supportItemWeightMultiplier,
            int repeatedCycle)
        {
            StartFloor = Mathf.Max(1, startFloor);
            Role = role ?? string.Empty;
            EnemyCount = Mathf.Max(1, enemyCount);
            DangerousLineCount = Mathf.Max(0, dangerousLineCount);
            EnemySpeedBonus = Mathf.Max(0f, enemySpeedBonus);
            SupportItemWeightMultiplier = Mathf.Max(0.01f, supportItemWeightMultiplier);
            RepeatedCycle = Mathf.Max(0, repeatedCycle);
        }

        public int StartFloor { get; }
        public string Role { get; }
        public int EnemyCount { get; }
        public int DangerousLineCount { get; }
        public float EnemySpeedBonus { get; }
        public float SupportItemWeightMultiplier { get; }
        public int RepeatedCycle { get; }
    }

    [CreateAssetMenu(
        fileName = "DifficultyBandDefinition",
        menuName = "LootUp/Balance/Difficulty Band Definition")]
    public sealed class DifficultyBandDefinition : ScriptableObject
    {
        [SerializeField, Min(1)]
        private int floorsPerBand = 10;

        [SerializeField]
        private List<DifficultyBandEntry> bands = new List<DifficultyBandEntry>();

        [Header("130F 이후 반복")]
        [SerializeField]
        private bool repeatLastCycle = true;

        [SerializeField, Min(0)]
        [Tooltip("반복을 시작할 Band의 0 기반 인덱스입니다.")]
        private int repeatFromBandIndex = 9;

        [SerializeField, Min(0f)]
        [Tooltip("40층 반복 주기마다 추가할 Enemy 속도 보정입니다.")]
        private float repeatSpeedBonusPerCycle = 12f;

        public int FloorsPerBand => Mathf.Max(1, floorsPerBand);
        public IReadOnlyList<DifficultyBandEntry> Bands => bands;

        public bool TryGetValues(
            int absoluteFloor,
            out DifficultyBandValues values)
        {
            values = default;
            if (bands == null || bands.Count == 0)
            {
                return false;
            }

            int floor = Mathf.Max(1, absoluteFloor);
            int requestedIndex = (floor - 1) / FloorsPerBand;
            int sourceIndex = Mathf.Clamp(requestedIndex, 0, bands.Count - 1);
            int repeatedCycle = 0;

            if (requestedIndex >= bands.Count && repeatLastCycle)
            {
                int cycleStart = Mathf.Clamp(
                    repeatFromBandIndex,
                    0,
                    bands.Count - 1);
                int cycleLength = Mathf.Max(1, bands.Count - cycleStart);
                int repeatedOffset = requestedIndex - bands.Count;
                sourceIndex = cycleStart + repeatedOffset % cycleLength;
                repeatedCycle = repeatedOffset / cycleLength + 1;
            }

            DifficultyBandEntry source = bands[sourceIndex];
            if (source == null)
            {
                return false;
            }

            // 반복 구간은 원본 파형을 유지하면서 주기마다 속도 기준선만 높인다.
            float repeatedSpeedBonus = repeatLastCycle
                ? repeatedCycle * Mathf.Max(0f, repeatSpeedBonusPerCycle)
                : 0f;
            values = new DifficultyBandValues(
                requestedIndex * FloorsPerBand + 1,
                source.Role,
                source.EnemyCount,
                source.DangerousLineCount,
                source.EnemySpeedBonus + repeatedSpeedBonus,
                source.SupportItemWeightMultiplier,
                repeatedCycle);
            return true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            floorsPerBand = Mathf.Max(1, floorsPerBand);
            repeatFromBandIndex = Mathf.Max(0, repeatFromBandIndex);
            repeatSpeedBonusPerCycle = Mathf.Max(
                0f,
                repeatSpeedBonusPerCycle);

            if (bands == null)
            {
                return;
            }

            for (int i = 0; i < bands.Count; i++)
            {
                bands[i]?.SetStartFloor(i * floorsPerBand + 1);
            }
        }
#endif
    }
}
