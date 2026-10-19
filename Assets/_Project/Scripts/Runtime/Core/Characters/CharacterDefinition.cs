using LootUp.Core.Characters.Skills;
using UnityEngine;
using UnityEngine.Serialization;

namespace LootUp.Core.Characters
{
    [CreateAssetMenu(fileName = "CharacterDefinition", menuName = "LootUp/Characters/Character Definition")]
    public sealed class CharacterDefinition : ScriptableObject
    {
        public const int MaximumCharacterLevel = 99;

        [SerializeField]
        private string characterId = "default";

        [SerializeField]
        private string displayName = "Default";

        [SerializeField]
        private Sprite portraitSprite;

        [SerializeField]
        private Rect ingamePortraitFaceRect = new Rect(0.24f, 0.52f, 0.52f, 0.34f);

        [SerializeField]
        private Color bodyColor = new Color(0.95f, 0.78f, 0.22f, 1f);

        [SerializeField]
        private Color outlineColor = new Color(0.05f, 0.05f, 0.06f, 0.85f);

        [SerializeField]
        private Sprite[] idleSprites;

        [SerializeField]
        private Sprite[] walkSprites;

        [SerializeField]
        private Sprite[] runSprites;

        [SerializeField]
        private float[] idleFrameScales;

        [SerializeField]
        private float[] walkFrameScales;

        [SerializeField]
        private float[] runFrameScales;

        [SerializeField]
        private float animationFramesPerSecond = 6f;

        [SerializeField]
        private Vector2 spriteVisualScale = Vector2.one;

        [Header("Base Gameplay Stats")]
        [SerializeField]
        private float moveSpeedColumnsPerSecond = 4f;

        [SerializeField]
        private float pivotCooldownSeconds;

        [SerializeField]
        [FormerlySerializedAs("boosterGaugeMax")]
        private float feverGaugeMax = 100f;

        [SerializeField]
        [FormerlySerializedAs("boosterGainPerColumn")]
        private float feverGainPerColumn = 8f;

        [SerializeField]
        [FormerlySerializedAs("boosterGainPerPivot")]
        private float feverGainPerPivot = 12f;

        [SerializeField]
        [FormerlySerializedAs("boosterBuffKey")]
        private string feverBuffKey = "Undefined";

        [SerializeField]
        private int maxLife = 3;

        [SerializeField, Range(0f, 1f)]
        private float instantItemAcquireChance;

        [SerializeField, Range(0f, 1f)]
        [FormerlySerializedAs("skillItemPageSpawnChance")]
        [Tooltip("페이지 생성 시 Time 또는 Skill 타입 아이템 1개를 보장할 기본 확률입니다.")]
        private float itemChance = 0.15f;

        [SerializeField, Min(0f)]
        private float collectionItemChanceBonusPercent;

        [SerializeField]
        private CharacterUpgradeDefinition[] collectionUpgrades;

        [Header("Progression And Ownership")]
        [SerializeField]
        [Tooltip("새 저장 데이터에서 이 캐릭터를 기본 보유 상태로 생성할지 결정합니다.")]
        private bool initiallyOwned = true;

        [SerializeField]
        [Tooltip("캐릭터를 획득할 수 있는 경로의 기획 데이터입니다. 실행 로직 적용 전에는 비활성 상태로 유지합니다.")]
        private CharacterAcquisitionOption[] acquisitionOptions;

        [SerializeField]
        [Tooltip("현재 레벨에서 다음 레벨로 올라갈 때 필요한 XP입니다. 미설정된 Lv.99 이전 구간은 마지막 값을 사용합니다.")]
        private int[] requiredExperienceByLevel = { 100, 150, 225, 325, 450, 600, 800, 1050, 1350 };

        [SerializeField]
        [Tooltip("런 종료 시 현재 캐릭터 레벨에 따라 지급하는 기본 XP입니다.")]
        private int[] runExperienceRewardByLevel = { 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 };

        [Header("Character Skill")]
        [SerializeField]
        private CharacterSkillDefinition characterSkill;

        [SerializeField, HideInInspector]
        private string unlockableSkillId = "Undefined";

        [SerializeField, HideInInspector]
        private string unlockableSkillName = "Skill Pending";

        [SerializeField, HideInInspector]
        [TextArea(2, 4)]
        private string unlockableSkillDescription = "Character-specific ability will be configured later.";

        [SerializeField, HideInInspector]
        private int skillUnlockLevel = 3;

        public string CharacterId => characterId;
        public string DisplayName => displayName;
        public Sprite PortraitSprite => portraitSprite;
        public Rect IngamePortraitFaceRect => ingamePortraitFaceRect;
        public Color BodyColor => bodyColor;
        public Color OutlineColor => outlineColor;
        public Sprite[] IdleSprites => idleSprites;
        public Sprite[] WalkSprites => walkSprites;
        public Sprite[] RunSprites => runSprites;
        public float AnimationFramesPerSecond => animationFramesPerSecond;
        public Vector2 SpriteVisualScale => spriteVisualScale;
        public float MoveSpeedColumnsPerSecond => moveSpeedColumnsPerSecond;
        public float PivotCooldownSeconds => pivotCooldownSeconds;
        public float FeverGaugeMax => feverGaugeMax;
        public float FeverGainPerColumn => feverGainPerColumn;
        public float FeverGainPerPivot => feverGainPerPivot;
        public string FeverBuffKey => feverBuffKey;
        public float BoosterGaugeMax => FeverGaugeMax;
        public float BoosterGainPerColumn => FeverGainPerColumn;
        public float BoosterGainPerPivot => FeverGainPerPivot;
        public string BoosterBuffKey => FeverBuffKey;
        public int MaxLife => maxLife;
        public float InstantItemAcquireChance => instantItemAcquireChance;
        public float ItemChance => Mathf.Clamp01(itemChance);
        public float CollectionItemChanceBonusPercent => Mathf.Max(0f, collectionItemChanceBonusPercent);
        public CharacterUpgradeDefinition[] CollectionUpgrades => collectionUpgrades;
        public bool InitiallyOwned => initiallyOwned;
        public CharacterAcquisitionOption[] AcquisitionOptions => acquisitionOptions;
        public int MaxCharacterLevel => MaximumCharacterLevel;
        public CharacterSkillDefinition CharacterSkill => characterSkill;
        public string UnlockableSkillId => characterSkill != null ? characterSkill.SkillId : unlockableSkillId;
        public string UnlockableSkillName => characterSkill != null ? characterSkill.DisplayName : unlockableSkillName;
        public string UnlockableSkillDescription => characterSkill != null ? characterSkill.Description : unlockableSkillDescription;
        public int SkillUnlockLevel => Mathf.Clamp(
            characterSkill != null ? characterSkill.UnlockLevel : skillUnlockLevel,
            1,
            MaxCharacterLevel);
        public bool IsSkillConfigured => characterSkill != null
            || (!string.IsNullOrWhiteSpace(unlockableSkillId)
                && !string.Equals(unlockableSkillId, "Undefined", System.StringComparison.OrdinalIgnoreCase));
        public float SkillItemPageSpawnChance => ItemChance;

        public CharacterAcquisitionOption GetPrimaryAcquisitionOption()
        {
            if (acquisitionOptions == null)
            {
                return null;
            }

            CharacterAcquisitionOption fallback = null;
            for (int i = 0; i < acquisitionOptions.Length; i++)
            {
                CharacterAcquisitionOption option = acquisitionOptions[i];
                if (option == null)
                {
                    continue;
                }

                fallback ??= option;
                if (option.Enabled)
                {
                    return option;
                }
            }

            return fallback;
        }

        public float GetIdleFrameScale(int frameIndex)
        {
            return GetFrameScale(idleFrameScales, frameIndex);
        }

        public float GetWalkFrameScale(int frameIndex)
        {
            return GetFrameScale(walkFrameScales, frameIndex);
        }

        public float GetRunFrameScale(int frameIndex)
        {
            return GetFrameScale(runFrameScales, frameIndex);
        }

        // (추가) 캐릭터 테이블의 현재 레벨 기준 필요 XP를 반환한다. 최대 레벨은 0을 반환한다.
        public int GetRequiredExperienceForLevel(int currentLevel)
        {
            int normalizedLevel = Mathf.Clamp(currentLevel, 1, MaxCharacterLevel);
            if (normalizedLevel >= MaxCharacterLevel)
            {
                return 0;
            }

            if (requiredExperienceByLevel == null || requiredExperienceByLevel.Length == 0)
            {
                return 1;
            }

            int index = Mathf.Clamp(normalizedLevel - 1, 0, requiredExperienceByLevel.Length - 1);
            return Mathf.Max(1, requiredExperienceByLevel[index]);
        }

        // (추가) 런 종료 시 현재 캐릭터 레벨에 대응하는 기본 획득 XP를 반환한다.
        public int GetRunExperienceRewardForLevel(int currentLevel)
        {
            if (runExperienceRewardByLevel == null || runExperienceRewardByLevel.Length == 0)
            {
                return 0;
            }

            int index = Mathf.Clamp(Mathf.Max(1, currentLevel) - 1, 0, runExperienceRewardByLevel.Length - 1);
            return Mathf.Max(0, runExperienceRewardByLevel[index]);
        }

        private float GetFrameScale(float[] frameScales, int frameIndex)
        {
            if (frameScales == null || frameIndex < 0 || frameIndex >= frameScales.Length)
            {
                return 1f;
            }

            return Mathf.Max(0.01f, frameScales[frameIndex]);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            moveSpeedColumnsPerSecond = Mathf.Max(0f, moveSpeedColumnsPerSecond);
            ingamePortraitFaceRect = ClampNormalizedRect(ingamePortraitFaceRect);
            animationFramesPerSecond = Mathf.Max(1f, animationFramesPerSecond);
            spriteVisualScale.x = Mathf.Max(0.01f, spriteVisualScale.x);
            spriteVisualScale.y = Mathf.Max(0.01f, spriteVisualScale.y);
            ClampFrameScales(idleFrameScales);
            ClampFrameScales(walkFrameScales);
            ClampFrameScales(runFrameScales);
            pivotCooldownSeconds = Mathf.Max(0f, pivotCooldownSeconds);
            feverGaugeMax = Mathf.Max(1f, feverGaugeMax);
            feverGainPerColumn = Mathf.Max(0f, feverGainPerColumn);
            feverGainPerPivot = Mathf.Max(0f, feverGainPerPivot);
            maxLife = Mathf.Max(1, maxLife);
            instantItemAcquireChance = Mathf.Clamp01(instantItemAcquireChance);
            itemChance = Mathf.Clamp01(itemChance);
            collectionItemChanceBonusPercent = Mathf.Max(0f, collectionItemChanceBonusPercent);
            skillUnlockLevel = Mathf.Clamp(skillUnlockLevel, 1, MaxCharacterLevel);
            if (requiredExperienceByLevel != null)
            {
                for (int i = 0; i < requiredExperienceByLevel.Length; i++)
                {
                    requiredExperienceByLevel[i] = Mathf.Max(1, requiredExperienceByLevel[i]);
                }
            }

            if (runExperienceRewardByLevel != null)
            {
                for (int i = 0; i < runExperienceRewardByLevel.Length; i++)
                {
                    runExperienceRewardByLevel[i] = Mathf.Max(0, runExperienceRewardByLevel[i]);
                }
            }
        }

        private void ClampFrameScales(float[] frameScales)
        {
            if (frameScales == null)
            {
                return;
            }

            for (int i = 0; i < frameScales.Length; i++)
            {
                frameScales[i] = Mathf.Max(0.01f, frameScales[i]);
            }
        }

        private Rect ClampNormalizedRect(Rect rect)
        {
            float width = Mathf.Clamp(rect.width, 0.01f, 1f);
            float height = Mathf.Clamp(rect.height, 0.01f, 1f);
            float x = Mathf.Clamp(rect.x, 0f, 1f - width);
            float y = Mathf.Clamp(rect.y, 0f, 1f - height);
            return new Rect(x, y, width, height);
        }
#endif
    }
}
