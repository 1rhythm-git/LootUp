using System;
using System.Collections.Generic;
using LootUp.Core.Characters;
using LootUp.Core.Game;
using LootUp.Core.Player;
using LootUp.Core.Profile;
using LootUp.Core.World;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace LootUp.Core.UI
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class TopHUDController : MonoBehaviour
    {
        private const string RuntimeRootName = "TopHUDRuntimeRoot";
        private const string BottomUIName = "BottomUI";
        private const string FeverGaugeName = "FeverGauge";
        private const string HeartIconResourcePath = "Items/Icons/heart";

        [SerializeField]
        private InfiniteFloorManager floorManager;

        [SerializeField]
        private Sprite characterPortrait;

        [SerializeField]
        private Color portraitFallbackColor = new Color(0.22f, 0.26f, 0.32f, 1f);

        [SerializeField]
        private string playerNickname = "Player";

        [SerializeField]
        [FormerlySerializedAs("playerLevel")]
        private int characterLevel = 1;

        [SerializeField]
        private int maxHearts = 3;

        [SerializeField]
        private int currentHearts = 3;

        [SerializeField]
        private bool useTimer = true;

        [SerializeField]
        private float runDurationSeconds = 90f;

        [SerializeField]
        private int currentScore;

        [SerializeField]
        private int currentRunGameMoney;

        [SerializeField]
        private int fullHeartScoreBonusPerHeart = 100;

        [SerializeField]
        private string itemStatusText = "ITEM -";

        [SerializeField]
        private Color primaryTextColor = Color.white;

        [SerializeField]
        private Color secondaryTextColor = new Color(1f, 1f, 1f, 0.74f);

        [SerializeField]
        private Color heartColor = new Color(1f, 0.18f, 0.24f, 1f);

        [SerializeField]
        [FormerlySerializedAs("boosterReadyColor")]
        private Color feverReadyColor = new Color(0.96f, 0.82f, 0.22f, 1f);

        [SerializeField]
        [FormerlySerializedAs("boosterGaugeBackgroundColor")]
        private Color feverGaugeBackgroundColor = new Color(0f, 0f, 0f, 0.68f);

        [SerializeField]
        [FormerlySerializedAs("boosterGaugeFillColor")]
        private Color feverGaugeFillColor = new Color(0.18f, 0.78f, 0.32f, 1f);

        [SerializeField]
        [FormerlySerializedAs("boosterGaugeEmptyColor")]
        private Color feverGaugeEmptyColor = new Color(1f, 1f, 1f, 0.08f);

        [SerializeField]
        [FormerlySerializedAs("boosterReadyBlinkInterval")]
        private float feverReadyBlinkInterval = 0.18f;

        [SerializeField]
        private Color experienceGaugeBackgroundColor = new Color(0f, 0f, 0f, 0.68f);

        [SerializeField]
        private Color experienceGaugeFillColor = new Color(0.42f, 0.9f, 0.42f, 1f);

        private RectTransform runtimeRoot;
        private Image portraitImage;
        private Text nicknameText;
        private Text levelText;
        private Text timerText;
        private Text floorText;
        private Text scoreText;
        private Text ownedGameMoneyText;
        private Text ownedRubyText;
        private Text runGameMoneyText;
        private Text itemText;
        private Text feverText;
        private Text speedBuffText;
        private Text experienceText;
        private RectTransform feverGaugeFillRect;
        private RectTransform experienceGaugeFillRect;
        private RectTransform heartsIconRoot;
        private Image feverGaugeFillImage;
        private Font hudFont;
        private Sprite heartIconSprite;
        private Sprite generatedPortraitSprite;
        private readonly List<Image> heartImages = new List<Image>();
        private PlayerCharacterRuntime characterRuntime;
        private CharacterDefinition activeCharacterDefinition;
        private PlayerMotor playerMotor;
        private float remainingSeconds;
        private float currentFeverGaugeNormalized;
        private bool timerPaused;
        private bool gameOverRequested;

        public event Action<GameOverReason> GameOverRequested;

        public int CurrentHearts => currentHearts;
        public int MaxHearts => maxHearts;
        public float RemainingSeconds => remainingSeconds;
        public int CurrentScore => currentScore;
        public int CurrentRunGameMoney => currentRunGameMoney;
        public CharacterDefinition ActiveCharacterDefinition => activeCharacterDefinition;
        public int FullHeartScoreBonusPerHeart => Mathf.Max(0, fullHeartScoreBonusPerHeart);

        private void Awake()
        {
            EnsureReferences();
            remainingSeconds = Mathf.Max(0f, runDurationSeconds);
            BuildHUD();
            RefreshAll();
        }

        private void OnEnable()
        {
            CharacterProgressionState.ProgressChanged += HandleCharacterProgressChanged;
            UserProfileManager.ProfileChanged += HandleUserProfileChanged;

            if (floorManager != null)
            {
                floorManager.CurrentFloorChanged += HandleCurrentFloorChanged;
            }
        }

        private void Start()
        {
            EnsureReferences();
            RefreshAll();
        }

        private void Update()
        {
            UpdateFeverVisual();
            RefreshSpeedBuffStatus();

            if (!useTimer || timerPaused || remainingSeconds <= 0f)
            {
                return;
            }

            remainingSeconds = Mathf.Max(0f, remainingSeconds - Time.deltaTime);
            RefreshTimer();

            if (remainingSeconds <= 0f)
            {
                RequestGameOver(GameOverReason.TimeOver);
            }
        }

        private void OnDisable()
        {
            CharacterProgressionState.ProgressChanged -= HandleCharacterProgressChanged;
            UserProfileManager.ProfileChanged -= HandleUserProfileChanged;

            if (floorManager != null)
            {
                floorManager.CurrentFloorChanged -= HandleCurrentFloorChanged;
            }

            if (characterRuntime != null)
            {
                characterRuntime.FeverGaugeChanged -= HandleFeverGaugeChanged;
                characterRuntime.FeverStarted -= HandleFeverStarted;
                characterRuntime.FeverEnded -= HandleFeverEnded;
            }
        }

        private void OnDestroy()
        {
            DestroyGeneratedPortraitSprite();
        }

        public void AddScore(int amount)
        {
            currentScore = Mathf.Max(0, currentScore + amount);
            RefreshScore();
        }

        // (추가) 런 중 획득한 게임머니는 결과 확정 전까지 보유 재화와 분리한다.
        public void AddRunGameMoney(int amount)
        {
            currentRunGameMoney = Mathf.Max(0, currentRunGameMoney + Mathf.Max(0, amount));
            RefreshCurrencies();
        }

        public void AddTime(float seconds)
        {
            remainingSeconds = Mathf.Max(0f, remainingSeconds + seconds);
            RefreshTimer();
        }

        public void HealHeart(int amount)
        {
            currentHearts = Mathf.Min(maxHearts, currentHearts + Mathf.Max(0, amount));
            RefreshHearts();
        }

        public void SetHearts(int maximum, int current)
        {
            maxHearts = Mathf.Max(1, maximum);
            currentHearts = Mathf.Clamp(current, 0, maxHearts);
            RefreshHearts();
        }

        public void DamageHeart(int amount)
        {
            currentHearts = Mathf.Max(0, currentHearts - Mathf.Max(0, amount));
            RefreshHearts();

            if (currentHearts <= 0)
            {
                RequestGameOver(GameOverReason.LifeDepleted);
            }
        }

        public int ApplyHealOrScoreBonus(int amount)
        {
            int healAmount = Mathf.Max(0, amount);
            if (healAmount <= 0)
            {
                return 0;
            }

            if (currentHearts >= maxHearts)
            {
                int scoreBonus = healAmount * Mathf.Max(0, fullHeartScoreBonusPerHeart);
                AddScore(scoreBonus);
                return scoreBonus;
            }

            HealHeart(healAmount);
            return 0;
        }

        public void SetItemStatus(string message)
        {
            itemStatusText = string.IsNullOrWhiteSpace(message) ? "ITEM -" : message;
            RefreshItemStatus();
        }

        public void SetTimerPaused(bool isPaused)
        {
            timerPaused = isPaused;
        }

        public void BindCharacterRuntime(PlayerCharacterRuntime runtime)
        {
            if (characterRuntime != null)
            {
                characterRuntime.FeverGaugeChanged -= HandleFeverGaugeChanged;
                characterRuntime.FeverStarted -= HandleFeverStarted;
                characterRuntime.FeverEnded -= HandleFeverEnded;
            }

            characterRuntime = runtime;

            if (characterRuntime != null)
            {
                ApplyCharacterDefinition(characterRuntime.CharacterDefinition);
                characterRuntime.FeverGaugeChanged += HandleFeverGaugeChanged;
                characterRuntime.FeverStarted += HandleFeverStarted;
                characterRuntime.FeverEnded += HandleFeverEnded;
                RefreshFeverGauge(characterRuntime.FeverGaugeNormalized);
                return;
            }

            RefreshFeverGauge(0f);
        }

        public void BindPlayerMotor(PlayerMotor motor)
        {
            playerMotor = motor;
            RefreshSpeedBuffStatus();
        }

        public void ApplyCharacterDefinition(CharacterDefinition definition)
        {
            if (definition == null)
            {
                return;
            }

            activeCharacterDefinition = definition;
            CharacterProgressionSnapshot progression = CharacterProgressionState.GetSnapshot(definition);
            characterLevel = progression.Level;
            characterPortrait = CreateInGamePortraitSprite(definition);
            maxHearts = Mathf.Max(1, definition.MaxLife);
            currentHearts = maxHearts;
            RefreshIdentity();
            RefreshPortrait();
            RefreshHearts();
            RefreshCharacterExperience();
        }

        [ContextMenu("Debug/Add 100 Character XP")]
        private void DebugAddCharacterExperience()
        {
            _ = CharacterProgressionManager.AddExperienceAsync(
                activeCharacterDefinition,
                100,
                $"debug:{Guid.NewGuid():N}:character-xp",
                "debug_grant");
        }

        [ContextMenu("Debug/Add 100 Score")]
        private void DebugAddScore()
        {
            AddScore(100);
        }

        [ContextMenu("Debug/Damage Heart")]
        private void DebugDamageHeart()
        {
            DamageHeart(1);
        }

        [ContextMenu("Debug/Heal Heart")]
        private void DebugHealHeart()
        {
            currentHearts = Mathf.Min(maxHearts, currentHearts + 1);
            RefreshHearts();
        }

        [ContextMenu("Debug/Reset Timer")]
        private void DebugResetTimer()
        {
            remainingSeconds = Mathf.Max(0f, runDurationSeconds);
            timerPaused = false;
            gameOverRequested = false;
            RefreshTimer();
        }

        private void RequestGameOver(GameOverReason reason)
        {
            if (gameOverRequested)
            {
                return;
            }

            gameOverRequested = true;
            timerPaused = true;
            GameOverRequested?.Invoke(reason);
        }

        private void HandleCurrentFloorChanged(int currentAbsoluteFloor)
        {
            RefreshFloor(currentAbsoluteFloor);
        }

        private void BuildHUD()
        {
            hudFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            runtimeRoot = FindExistingRuntimeRoot();

            if (runtimeRoot == null)
            {
                GameObject rootObject = new GameObject(RuntimeRootName, typeof(RectTransform));
                rootObject.layer = gameObject.layer;
                rootObject.transform.SetParent(transform, false);
                runtimeRoot = rootObject.GetComponent<RectTransform>();
            }

            ClearRuntimeRootChildren();
            DestroyExistingBottomFeverGauge();
            AlignBottomUIToElevatorPlatformBottom();

            runtimeRoot.anchorMin = Vector2.zero;
            runtimeRoot.anchorMax = Vector2.one;
            runtimeRoot.offsetMin = Vector2.zero;
            runtimeRoot.offsetMax = Vector2.zero;
            runtimeRoot.pivot = new Vector2(0.5f, 0.5f);

            portraitImage = CreateImage("Portrait", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -104f), new Vector2(110f, -12f), portraitFallbackColor);
            AddOutline(portraitImage.gameObject, new Color(1f, 1f, 1f, 0.35f), new Vector2(2f, -2f));

            nicknameText = CreateText("NicknameText", new Vector2(0f, 1f), new Vector2(0.4f, 1f), new Vector2(124f, -58f), new Vector2(-8f, -12f), TextAnchor.UpperLeft, 43, primaryTextColor);
            levelText = CreateText("LevelText", new Vector2(0f, 1f), new Vector2(0.4f, 1f), new Vector2(124f, -104f), new Vector2(-8f, -58f), TextAnchor.UpperLeft, 36, secondaryTextColor);
            CreateCharacterExperienceGauge();

            // (변경) 하트와 층수는 기존 x축 배치를 유지하고 TopUI 바닥에 붙인다.
            heartsIconRoot = CreateHeartIconRoot("HeartsIconRoot", new Vector2(0.5f, 0f), new Vector2(0.82f, 0f), new Vector2(6f, 0f), new Vector2(-6f, 46f));
            floorText = CreateText("FloorText", new Vector2(0.82f, 0f), new Vector2(1f, 0f), new Vector2(6f, 0f), new Vector2(-18f, 46f), TextAnchor.MiddleRight, 38, primaryTextColor);

            timerText = CreateText("TimerText", new Vector2(0.64f, 1f), new Vector2(1f, 1f), new Vector2(8f, -58f), new Vector2(-18f, -12f), TextAnchor.UpperRight, 43, primaryTextColor);
            scoreText = CreateText("ScoreText", new Vector2(0.64f, 1f), new Vector2(1f, 1f), new Vector2(8f, -104f), new Vector2(-18f, -60f), TextAnchor.UpperRight, 36, secondaryTextColor);
            CreateCurrencyDisplays();
            itemText = CreateText("ItemStatusText", new Vector2(0.64f, 1f), new Vector2(1f, 1f), new Vector2(8f, -138f), new Vector2(-18f, -106f), TextAnchor.UpperRight, 29, secondaryTextColor);
            speedBuffText = CreateText("SpeedBuffText", new Vector2(0.64f, 1f), new Vector2(1f, 1f), new Vector2(8f, -172f), new Vector2(-18f, -140f), TextAnchor.UpperRight, 27, feverReadyColor);
            CreateFeverGauge();
            heartsIconRoot.transform.SetAsLastSibling();
            floorText.transform.SetAsLastSibling();
        }

        private RectTransform FindExistingRuntimeRoot()
        {
            Transform existing = transform.Find(RuntimeRootName);
            return existing as RectTransform;
        }

        private void ClearRuntimeRootChildren()
        {
            heartImages.Clear();

            for (int i = runtimeRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = runtimeRoot.GetChild(i);
                if (Application.isPlaying)
                {
                    Destroy(child.gameObject);
                }
                else
                {
                    DestroyImmediate(child.gameObject);
                }
            }
        }

        private void DestroyExistingBottomFeverGauge()
        {
            RectTransform bottomRoot = FindBottomUIRoot();
            if (bottomRoot == null)
            {
                return;
            }

            Transform existing = bottomRoot.Find(FeverGaugeName);
            if (existing == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(existing.gameObject);
            }
            else
            {
                DestroyImmediate(existing.gameObject);
            }
        }

        private void AlignBottomUIToElevatorPlatformBottom()
        {
            RectTransform bottomRoot = FindBottomUIRoot();
            ElevatorController elevatorController = FindFirstObjectByType<ElevatorController>();
            if (bottomRoot == null || elevatorController == null)
            {
                return;
            }

            Vector2 offsetMax = bottomRoot.offsetMax;
            offsetMax.y = -elevatorController.PlatformHeight;
            bottomRoot.offsetMax = offsetMax;
        }

        private Image CreateImage(string objectName, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            imageObject.layer = gameObject.layer;
            imageObject.transform.SetParent(runtimeRoot, false);

            RectTransform rectTransform = imageObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.offsetMin = offsetMin;
            rectTransform.offsetMax = offsetMax;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);

            Image image = imageObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;

            return image;
        }

        private Text CreateText(string objectName, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, TextAnchor alignment, int fontSize, Color color)
        {
            return CreateText(runtimeRoot, objectName, anchorMin, anchorMax, offsetMin, offsetMax, alignment, fontSize, color);
        }

        private Text CreateText(RectTransform parent, string objectName, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, TextAnchor alignment, int fontSize, Color color)
        {
            GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
            textObject.layer = gameObject.layer;
            textObject.transform.SetParent(parent, false);

            RectTransform rectTransform = textObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.offsetMin = offsetMin;
            rectTransform.offsetMax = offsetMax;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);

            Text text = textObject.GetComponent<Text>();
            text.alignment = alignment;
            text.font = hudFont;
            text.fontSize = fontSize;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return text;
        }

        private void CreateCurrencyDisplays()
        {
            CreateCurrencyIcon("OwnedGameMoneyIcon", "Items/Icons/score_coin", new Vector2(0.41f, 1f), new Vector2(0f, -42f), new Vector2(28f, -14f));
            ownedGameMoneyText = CreateText("OwnedGameMoneyText", new Vector2(0.445f, 1f), new Vector2(0.52f, 1f), new Vector2(0f, -46f), new Vector2(0f, -10f), TextAnchor.MiddleLeft, 24, primaryTextColor);

            CreateCurrencyIcon("OwnedRubyIcon", "Items/Icons/ruby", new Vector2(0.525f, 1f), new Vector2(0f, -42f), new Vector2(28f, -14f));
            ownedRubyText = CreateText("OwnedRubyText", new Vector2(0.56f, 1f), new Vector2(0.635f, 1f), new Vector2(0f, -46f), new Vector2(0f, -10f), TextAnchor.MiddleLeft, 24, primaryTextColor);

            CreateCurrencyIcon("RunGameMoneyIcon", "Items/Icons/score_coin", new Vector2(0.41f, 1f), new Vector2(0f, -80f), new Vector2(28f, -52f));
            runGameMoneyText = CreateText("RunGameMoneyText", new Vector2(0.445f, 1f), new Vector2(0.635f, 1f), new Vector2(0f, -84f), new Vector2(0f, -48f), TextAnchor.MiddleLeft, 22, feverReadyColor);
        }

        private void CreateCurrencyIcon(string objectName, string resourcePath, Vector2 anchor, Vector2 offsetMin, Vector2 offsetMax)
        {
            Image image = CreateImage(objectName, anchor, anchor, offsetMin, offsetMax, Color.white);
            image.sprite = Resources.Load<Sprite>(resourcePath);
            image.color = image.sprite != null ? Color.white : secondaryTextColor;
            image.preserveAspect = true;
        }

        private void CreateFeverGauge()
        {
            RectTransform gaugeParent = FindBottomUIRoot() ?? runtimeRoot;

            GameObject gaugeObject = new GameObject(FeverGaugeName, typeof(RectTransform), typeof(Image), typeof(Outline));
            gaugeObject.layer = gameObject.layer;
            gaugeObject.transform.SetParent(gaugeParent, false);

            RectTransform gaugeRect = gaugeObject.GetComponent<RectTransform>();
            gaugeRect.anchorMin = new Vector2(0f, 1f);
            gaugeRect.anchorMax = new Vector2(1f, 1f);
            gaugeRect.offsetMin = new Vector2(0f, -36f);
            gaugeRect.offsetMax = Vector2.zero;
            gaugeRect.pivot = new Vector2(0.5f, 0.5f);

            Image gaugeBackground = gaugeObject.GetComponent<Image>();
            gaugeBackground.color = feverGaugeBackgroundColor;
            gaugeBackground.raycastTarget = false;

            Outline outline = gaugeObject.GetComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.25f);
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = true;

            GameObject emptyObject = new GameObject("FeverGaugeEmpty", typeof(RectTransform), typeof(Image));
            emptyObject.layer = gameObject.layer;
            emptyObject.transform.SetParent(gaugeRect, false);

            RectTransform emptyRect = emptyObject.GetComponent<RectTransform>();
            emptyRect.anchorMin = Vector2.zero;
            emptyRect.anchorMax = Vector2.one;
            emptyRect.offsetMin = new Vector2(4f, 4f);
            emptyRect.offsetMax = new Vector2(-4f, -4f);
            emptyRect.pivot = new Vector2(0.5f, 0.5f);

            Image emptyImage = emptyObject.GetComponent<Image>();
            emptyImage.color = feverGaugeEmptyColor;
            emptyImage.raycastTarget = false;

            GameObject fillObject = new GameObject("FeverGaugeFill", typeof(RectTransform), typeof(Image));
            fillObject.layer = gameObject.layer;
            fillObject.transform.SetParent(gaugeRect, false);

            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.offsetMin = new Vector2(4f, 4f);
            fillRect.offsetMax = new Vector2(4f, -4f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            feverGaugeFillRect = fillRect;

            feverGaugeFillImage = fillObject.GetComponent<Image>();
            feverGaugeFillImage.color = feverGaugeFillColor;
            feverGaugeFillImage.type = Image.Type.Simple;
            feverGaugeFillImage.raycastTarget = false;

            feverText = CreateText(gaugeRect, "FeverText", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter, 24, Color.white);
            feverText.transform.SetAsLastSibling();
        }

        private RectTransform CreateHeartIconRoot(string objectName, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject rootObject = new GameObject(objectName, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            rootObject.layer = gameObject.layer;
            rootObject.transform.SetParent(runtimeRoot, false);

            RectTransform rectTransform = rootObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.offsetMin = offsetMin;
            rectTransform.offsetMax = offsetMax;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);

            HorizontalLayoutGroup layoutGroup = rootObject.GetComponent<HorizontalLayoutGroup>();
            layoutGroup.childAlignment = TextAnchor.MiddleRight;
            layoutGroup.childControlWidth = false;
            layoutGroup.childControlHeight = false;
            layoutGroup.childForceExpandWidth = false;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.spacing = 6f;
            layoutGroup.padding = new RectOffset(0, 0, 0, 0);

            return rectTransform;
        }

        private RectTransform FindBottomUIRoot()
        {
            Transform parent = transform.parent;
            RectTransform bottomRoot = parent != null ? parent.Find(BottomUIName) as RectTransform : null;
            if (bottomRoot != null)
            {
                return bottomRoot;
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            return canvas != null ? canvas.transform.Find(BottomUIName) as RectTransform : null;
        }

        private void CreateCharacterExperienceGauge()
        {
            GameObject gaugeObject = new GameObject("CharacterExperienceGauge", typeof(RectTransform), typeof(Image), typeof(Outline));
            gaugeObject.layer = gameObject.layer;
            gaugeObject.transform.SetParent(runtimeRoot, false);

            RectTransform gaugeRect = gaugeObject.GetComponent<RectTransform>();
            gaugeRect.anchorMin = new Vector2(0f, 1f);
            gaugeRect.anchorMax = new Vector2(0.252f, 1f);
            gaugeRect.offsetMin = new Vector2(18f, -132.2f);
            gaugeRect.offsetMax = new Vector2(0f, -105.8f);
            gaugeRect.pivot = new Vector2(0.5f, 0.5f);

            Image gaugeBackground = gaugeObject.GetComponent<Image>();
            gaugeBackground.color = experienceGaugeBackgroundColor;
            gaugeBackground.raycastTarget = false;

            Outline outline = gaugeObject.GetComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.2f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;

            GameObject fillObject = new GameObject("CharacterExperienceFill", typeof(RectTransform), typeof(Image));
            fillObject.layer = gameObject.layer;
            fillObject.transform.SetParent(gaugeRect, false);

            experienceGaugeFillRect = fillObject.GetComponent<RectTransform>();
            experienceGaugeFillRect.anchorMin = Vector2.zero;
            experienceGaugeFillRect.anchorMax = new Vector2(0f, 1f);
            experienceGaugeFillRect.offsetMin = new Vector2(3f, 3f);
            experienceGaugeFillRect.offsetMax = new Vector2(3f, -3f);
            experienceGaugeFillRect.pivot = new Vector2(0f, 0.5f);

            Image fillImage = fillObject.GetComponent<Image>();
            fillImage.color = experienceGaugeFillColor;
            fillImage.raycastTarget = false;

            experienceText = CreateText("CharacterExperienceText", new Vector2(0f, 1f), new Vector2(0.252f, 1f), new Vector2(18f, -132.2f), new Vector2(0f, -105.8f), TextAnchor.MiddleCenter, 15, Color.white);
            experienceText.transform.SetAsLastSibling();
        }

        private void AddOutline(GameObject target, Color color, Vector2 distance)
        {
            Outline outline = target.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = distance;
            outline.useGraphicAlpha = true;
        }

        private void RefreshAll()
        {
            RefreshIdentity();
            RefreshPortrait();
            RefreshHearts();
            RefreshTimer();
            RefreshFloor(floorManager != null ? floorManager.CurrentAbsoluteFloor : 1);
            RefreshScore();
            RefreshCurrencies();
            RefreshItemStatus();
            RefreshCharacterExperience();
            RefreshFeverGauge(characterRuntime != null ? characterRuntime.FeverGaugeNormalized : 0f);
            RefreshSpeedBuffStatus();
        }

        private void RefreshIdentity()
        {
            if (activeCharacterDefinition != null)
            {
                characterLevel = CharacterProgressionState.GetSnapshot(activeCharacterDefinition).Level;
            }

            string profileNickname = UserProfileManager.Nickname;
            if (!string.IsNullOrWhiteSpace(profileNickname))
            {
                playerNickname = profileNickname;
            }

            characterLevel = Mathf.Max(1, characterLevel);

            if (nicknameText != null)
            {
                nicknameText.text = playerNickname;
            }

            if (levelText != null)
            {
                levelText.text = $"Lv. {characterLevel}";
            }
        }

        private void RefreshPortrait()
        {
            if (portraitImage == null)
            {
                return;
            }

            portraitImage.sprite = characterPortrait;
            portraitImage.color = characterPortrait == null ? portraitFallbackColor : Color.white;
            portraitImage.preserveAspect = true;
        }

        private Sprite CreateInGamePortraitSprite(CharacterDefinition definition)
        {
            DestroyGeneratedPortraitSprite();

            Sprite source = definition != null ? definition.PortraitSprite : null;
            if (source == null)
            {
                return null;
            }

            Rect normalizedRect = ClampNormalizedRect(definition.IngamePortraitFaceRect);
            Rect sourceRect = source.textureRect;
            Rect faceRect = new Rect(
                sourceRect.x + sourceRect.width * normalizedRect.x,
                sourceRect.y + sourceRect.height * normalizedRect.y,
                Mathf.Max(1f, sourceRect.width * normalizedRect.width),
                Mathf.Max(1f, sourceRect.height * normalizedRect.height));

            generatedPortraitSprite = Sprite.Create(
                source.texture,
                PixelSnapRect(faceRect),
                new Vector2(0.5f, 0.5f),
                source.pixelsPerUnit,
                0,
                SpriteMeshType.FullRect,
                Vector4.zero,
                false);
            generatedPortraitSprite.name = $"{source.name}_InGameFace";
            generatedPortraitSprite.hideFlags = HideFlags.HideAndDontSave;
            return generatedPortraitSprite;
        }

        private void DestroyGeneratedPortraitSprite()
        {
            if (generatedPortraitSprite == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(generatedPortraitSprite);
            }
            else
            {
                DestroyImmediate(generatedPortraitSprite);
            }

            generatedPortraitSprite = null;
        }

        private static Rect ClampNormalizedRect(Rect rect)
        {
            float width = Mathf.Clamp(rect.width, 0.01f, 1f);
            float height = Mathf.Clamp(rect.height, 0.01f, 1f);
            float x = Mathf.Clamp(rect.x, 0f, 1f - width);
            float y = Mathf.Clamp(rect.y, 0f, 1f - height);
            return new Rect(x, y, width, height);
        }

        private static Rect PixelSnapRect(Rect rect)
        {
            float x = Mathf.Round(rect.x);
            float y = Mathf.Round(rect.y);
            float width = Mathf.Max(1f, Mathf.Round(rect.width));
            float height = Mathf.Max(1f, Mathf.Round(rect.height));
            return new Rect(x, y, width, height);
        }

        private void RefreshHearts()
        {
            maxHearts = Mathf.Max(1, maxHearts);
            currentHearts = Mathf.Clamp(currentHearts, 0, maxHearts);

            if (heartsIconRoot == null)
            {
                return;
            }

            EnsureHeartIcons(maxHearts);

            for (int i = 0; i < maxHearts; i++)
            {
                Image heartImage = heartImages[i];
                bool isFilled = i < currentHearts;
                heartImage.enabled = true;
                heartImage.color = isFilled
                    ? heartColor
                    : new Color(heartColor.r, heartColor.g, heartColor.b, 0.24f);
            }
        }

        private void EnsureHeartIcons(int requiredCount)
        {
            if (heartIconSprite == null)
            {
                heartIconSprite = Resources.Load<Sprite>(HeartIconResourcePath);
            }

            for (int i = heartImages.Count; i < requiredCount; i++)
            {
                GameObject heartObject = new GameObject($"HeartIcon_{i + 1}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                heartObject.layer = gameObject.layer;
                heartObject.transform.SetParent(heartsIconRoot, false);

                RectTransform rectTransform = heartObject.GetComponent<RectTransform>();
                rectTransform.sizeDelta = new Vector2(34f, 34f);
                rectTransform.pivot = new Vector2(0.5f, 0.5f);

                LayoutElement layoutElement = heartObject.GetComponent<LayoutElement>();
                layoutElement.preferredWidth = 34f;
                layoutElement.preferredHeight = 34f;
                layoutElement.flexibleWidth = 0f;
                layoutElement.flexibleHeight = 0f;

                Image image = heartObject.GetComponent<Image>();
                image.sprite = heartIconSprite;
                image.preserveAspect = true;
                image.raycastTarget = false;
                heartImages.Add(image);
            }

            for (int i = 0; i < heartImages.Count; i++)
            {
                bool isActive = i < requiredCount;
                if (heartImages[i] != null)
                {
                    heartImages[i].gameObject.SetActive(isActive);
                }
            }
        }

        private void RefreshTimer()
        {
            if (timerText == null)
            {
                return;
            }

            int seconds = Mathf.CeilToInt(remainingSeconds);
            int minutes = seconds / 60;
            int remainSeconds = seconds % 60;
            timerText.text = $"{minutes:00}:{remainSeconds:00}";
        }

        private void RefreshFloor(int currentAbsoluteFloor)
        {
            if (floorText != null)
            {
                floorText.text = $"{Mathf.Max(1, currentAbsoluteFloor)}F";
            }
        }

        private void RefreshScore()
        {
            if (scoreText != null)
            {
                scoreText.text = $"SCORE {Mathf.Max(0, currentScore)}";
            }
        }

        private void RefreshCurrencies()
        {
            if (ownedGameMoneyText != null)
            {
                ownedGameMoneyText.text = UserProfileManager.GameMoney.ToString("N0");
            }

            if (ownedRubyText != null)
            {
                ownedRubyText.text = UserProfileManager.Ruby.ToString("N0");
            }

            if (runGameMoneyText != null)
            {
                runGameMoneyText.text = $"RUN +{Mathf.Max(0, currentRunGameMoney):N0}";
            }
        }

        private void RefreshItemStatus()
        {
            if (itemText != null)
            {
                itemText.text = itemStatusText;
            }
        }

        private void RefreshCharacterExperience()
        {
            CharacterProgressionSnapshot progression = CharacterProgressionState.GetSnapshot(activeCharacterDefinition);
            float normalizedExperience = activeCharacterDefinition != null ? progression.NormalizedExperience : 0f;

            if (experienceGaugeFillRect != null)
            {
                experienceGaugeFillRect.anchorMax = new Vector2(normalizedExperience, 1f);
                experienceGaugeFillRect.offsetMax = new Vector2(normalizedExperience <= 0f ? 3f : -3f, -3f);
            }

            if (experienceText != null)
            {
                experienceText.text = activeCharacterDefinition == null
                    ? "XP 0 / -"
                    : progression.IsMaxLevel
                        ? "XP MAX"
                        : $"XP {progression.CurrentExperience} / {progression.RequiredExperience}";
            }
        }

        private void HandleCharacterProgressChanged(string characterId)
        {
            if (activeCharacterDefinition == null || !string.Equals(activeCharacterDefinition.CharacterId, characterId, StringComparison.Ordinal))
            {
                return;
            }

            RefreshIdentity();
            RefreshCharacterExperience();
        }

        private void HandleUserProfileChanged()
        {
            RefreshIdentity();
            RefreshCurrencies();
        }

        private void RefreshSpeedBuffStatus()
        {
            if (speedBuffText == null)
            {
                return;
            }

            if (playerMotor == null || !playerMotor.HasActiveMoveSpeedBuff)
            {
                speedBuffText.text = string.Empty;
                return;
            }

            int bonusPercent = Mathf.RoundToInt(playerMotor.MoveSpeedBonusPercent);
            int remainingSecondsCeil = Mathf.CeilToInt(playerMotor.MoveSpeedBuffRemainingSeconds);
            speedBuffText.text = bonusPercent > 0 && remainingSecondsCeil > 0
                ? $"SPEED +{bonusPercent}%  {remainingSecondsCeil}s"
                : string.Empty;
        }

        private void HandleFeverGaugeChanged(float normalizedGauge)
        {
            RefreshFeverGauge(normalizedGauge);
        }

        private void HandleFeverStarted(float durationSeconds)
        {
            SetItemStatus("FEVER TIME!");
            RefreshFeverActive();
        }

        private void HandleFeverEnded()
        {
            RefreshFeverGauge(characterRuntime != null ? characterRuntime.FeverGaugeNormalized : 0f);
        }

        private void RefreshFeverGauge(float normalizedGauge)
        {
            if (feverText == null)
            {
                return;
            }

            float clampedGauge = Mathf.Clamp01(normalizedGauge);
            currentFeverGaugeNormalized = clampedGauge;
            int percent = Mathf.RoundToInt(clampedGauge * 100f);
            feverText.text = $"FEVER {percent}%";
            feverText.color = Color.white;

            if (feverGaugeFillImage != null)
            {
                feverGaugeFillImage.color = clampedGauge >= 1f ? feverReadyColor : feverGaugeFillColor;
            }

            if (feverGaugeFillRect != null)
            {
                feverGaugeFillRect.anchorMax = new Vector2(clampedGauge, 1f);
                feverGaugeFillRect.offsetMax = new Vector2(clampedGauge <= 0f ? 4f : -4f, -4f);
            }
        }

        private void UpdateFeverVisual()
        {
            if (characterRuntime != null && characterRuntime.IsFeverActive)
            {
                RefreshFeverActive();
                return;
            }

            if (feverGaugeFillImage == null || currentFeverGaugeNormalized < 1f)
            {
                return;
            }

            float interval = Mathf.Max(0.01f, feverReadyBlinkInterval);
            bool showReadyColor = Mathf.FloorToInt(Time.unscaledTime / interval) % 2 == 0;
            feverGaugeFillImage.color = showReadyColor ? feverReadyColor : feverGaugeFillColor;
        }

        private void RefreshFeverActive()
        {
            if (feverText == null || characterRuntime == null)
            {
                return;
            }

            float remainingNormalized = characterRuntime.FeverRemainingNormalized;
            feverText.text = $"FEVER TIME  {characterRuntime.FeverRemainingSeconds:0.0}s";
            feverText.color = feverReadyColor;

            if (feverGaugeFillImage != null)
            {
                feverGaugeFillImage.color = feverReadyColor;
            }

            if (feverGaugeFillRect != null)
            {
                feverGaugeFillRect.anchorMax = new Vector2(remainingNormalized, 1f);
                feverGaugeFillRect.offsetMax = new Vector2(remainingNormalized <= 0f ? 4f : -4f, -4f);
            }
        }

        private void EnsureReferences()
        {
            if (floorManager == null)
            {
                floorManager = FindFirstObjectByType<InfiniteFloorManager>();
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            characterLevel = Mathf.Max(1, characterLevel);
            maxHearts = Mathf.Max(1, maxHearts);
            currentHearts = Mathf.Clamp(currentHearts, 0, maxHearts);
            runDurationSeconds = Mathf.Max(0f, runDurationSeconds);
            currentScore = Mathf.Max(0, currentScore);
            fullHeartScoreBonusPerHeart = Mathf.Max(0, fullHeartScoreBonusPerHeart);
        }
#endif
    }
}
