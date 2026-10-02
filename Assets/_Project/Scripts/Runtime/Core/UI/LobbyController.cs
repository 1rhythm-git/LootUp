using System.Collections;
using LootUp.Core.Authentication;
using LootUp.Core.Characters;
using LootUp.Core.Characters.Skills;
using LootUp.Core.Items;
using LootUp.Core.Profile;
using LootUp.Core.SceneFlow;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LootUp.Core.UI
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class LobbyController : MonoBehaviour
    {
        private const string RuntimeRootName = "LobbyRuntimeRoot";
        private const float AgentXBaseMoveSpeed = 2f;
        private const float AgentXBasePivotCooldown = 0.3f;
        private const float AgentXBaseFeverGainPerColumn = 0.15f;

        private static readonly string[] FooterMenuLabels = { "MISSION", "MAIL BOX", "UPGRADE", "ARTIFACT", "SHOP", "LANK" };
        private static readonly string[] FooterMenuIconKeys = { "mission", "mailbox", "upgrade", "artifact", "shop", "rank" };
        private static readonly Vector2 HeaderBandMin = new Vector2(0f, 0.87f);
        private static readonly Vector2 HeaderBandMax = Vector2.one;
        private static readonly Vector2 ContentBandMin = new Vector2(0f, 0.18f);
        private static readonly Vector2 ContentBandMax = new Vector2(1f, 0.87f);
        private static readonly Vector2 FooterBandMin = Vector2.zero;
        private static readonly Vector2 FooterBandMax = new Vector2(1f, 0.18f);

        [SerializeField]
        private RectTransform headerRoot;

        [SerializeField]
        private RectTransform contentRoot;

        [SerializeField]
        private RectTransform footerRoot;

        [SerializeField]
        private CharacterDefinition[] availableCharacters;

        [SerializeField]
        private string playerNickname = "Player";

        [SerializeField]
        private int bestHighestFloor;

        [SerializeField]
        private int bestScore;

        [SerializeField]
        private Color primaryTextColor = Color.white;

        [SerializeField]
        private Color secondaryTextColor = new Color(1f, 1f, 1f, 0.72f);

        [SerializeField]
        private Color lockedSkillTextColor = new Color(0.58f, 0.6f, 0.64f, 1f);

        [SerializeField]
        private Color accentTextColor = new Color(1f, 0.92f, 0f, 1f);

        [SerializeField]
        private Color panelColor = new Color(0.05f, 0.07f, 0.1f, 0.78f);

        [SerializeField]
        private Color startButtonColor = new Color(0.96f, 0.82f, 0.22f, 1f);

        [SerializeField]
        private Color disabledButtonColor = new Color(0.18f, 0.2f, 0.24f, 0.92f);

        [SerializeField]
        private Color experienceColor = new Color(0.2f, 0.78f, 0.72f, 1f);

        [SerializeField, Min(0f)]
        private float startButtonActivationDelay = 0.75f;

        private Font lobbyFont;
        private CharacterDefinition selectedCharacter;
        private Button startButton;
        private Coroutine startButtonActivationCoroutine;
        private Text profileText;
        private Text currencyText;
        private Text rubyCurrencyText;
        private Text loginStateText;
        private Text bestFloorText;
        private Text bestScoreText;
        private Image bestCharacterPortraitImage;
        private Text selectedCharacterLevelText;
        private Text selectedCharacterPositionText;
        private Text selectedCharacterNameText;
        private Text selectedCharacterStatNamesText;
        private Text selectedCharacterStatValuesText;
        private Text selectedCharacterSkillDescriptionText;
        private Text selectedCharacterExperienceText;
        private Image selectedCharacterPortraitImage;
        private Image selectedCharacterExperienceFill;
        private RectTransform selectedCharacterExperienceFillRect;
        private Rect lastSafeArea;
        private Vector2Int lastScreenSize;
        private string bestCharacterId = string.Empty;
        private Sprite generatedBestCharacterPortraitSprite;

        private static Sprite leftArrowSprite;
        private static Sprite rightArrowSprite;
        private static Sprite settingsSprite;
        private static Sprite playSprite;

        private readonly Color sampleNavy = new Color(0.025f, 0.105f, 0.22f, 0.97f);
        private readonly Color samplePurple = new Color(0.55f, 0.12f, 0.88f, 0.98f);
        private readonly Color sampleYellow = new Color(1f, 0.78f, 0.04f, 1f);
        private readonly Color sampleOrange = new Color(1f, 0.48f, 0.02f, 1f);
        private readonly Color sampleOutline = new Color(0.015f, 0.08f, 0.2f, 0.96f);

        private void Awake()
        {
            EnsureReferences();
            ApplySafeAreaLayout();
            EnsureSelectedCharacter();
            BuildLobby();
        }

        private void OnEnable()
        {
            CharacterProgressionState.ProgressChanged += HandleCharacterProgressChanged;
            UserProfileManager.ProfileChanged += HandleUserProfileChanged;
            AuthenticationManager.AuthenticationStateChanged += HandleAuthenticationStateChanged;
            ItemCollectionManager.CollectionChanged += HandleCollectionChanged;

            if (startButton != null && startButtonActivationCoroutine == null)
            {
                ScheduleStartButtonActivation();
            }
        }

        private void OnDisable()
        {
            CharacterProgressionState.ProgressChanged -= HandleCharacterProgressChanged;
            UserProfileManager.ProfileChanged -= HandleUserProfileChanged;
            AuthenticationManager.AuthenticationStateChanged -= HandleAuthenticationStateChanged;
            ItemCollectionManager.CollectionChanged -= HandleCollectionChanged;

            if (startButtonActivationCoroutine != null)
            {
                StopCoroutine(startButtonActivationCoroutine);
                startButtonActivationCoroutine = null;
            }

            if (startButton != null)
            {
                startButton.interactable = false;
            }
        }

        private void OnDestroy()
        {
            DestroyGeneratedBestCharacterPortraitSprite();
        }

        private void Update()
        {
            if (lastSafeArea != Screen.safeArea || lastScreenSize.x != Screen.width || lastScreenSize.y != Screen.height)
            {
                ApplySafeAreaLayout();
            }
        }

        [ContextMenu("Debug/Rebuild Lobby")]
        private void DebugRebuildLobby()
        {
            EnsureReferences();
            ApplySafeAreaLayout();
            BuildLobby();
        }

        [ContextMenu("Debug/Add 100 XP To Selected Character")]
        private void DebugAddSelectedCharacterExperience()
        {
            _ = CharacterProgressionManager.AddExperienceAsync(
                selectedCharacter,
                100,
                $"debug:{System.Guid.NewGuid():N}:character-xp",
                "debug_grant");
        }

        public void StartGame()
        {
            if (startButton == null || !startButton.interactable)
            {
                return;
            }

            EnsureSelectedCharacter();
            CharacterSelectionState.Select(selectedCharacter);

            if (SceneFlowManager.Instance != null)
            {
                SceneFlowManager.Instance.LoadInGame();
                return;
            }

            SceneManager.LoadScene(SceneFlowManager.InGameSceneName, LoadSceneMode.Single);
        }

        private void BuildLobby()
        {
            lobbyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureSelectedCharacter();

            ClearRuntimeRoot(headerRoot);
            ClearRuntimeRoot(contentRoot);
            ClearRuntimeRoot(footerRoot);

            BuildHeader();
            BuildContent();
            BuildFooter();
            RefreshLobbyData();
            ScheduleStartButtonActivation();
        }

        private void BuildHeader()
        {
            RectTransform root = CreateRuntimeRoot(headerRoot);
            if (root == null)
            {
                return;
            }

            Text titleText = CreateText(root, "TitleText", "LOOTUP", new Vector2(0.055f, 0.52f), new Vector2(0.78f, 0.95f), TextAnchor.MiddleLeft, 48, accentTextColor);
            titleText.fontStyle = FontStyle.Bold;
            CreateIconButton(root, "SettingsButton", GetSettingsSprite(), new Vector2(0.869f, 0.633f), new Vector2(0.941f, 0.897f), null, true, "Settings");

            profileText = CreateText(root, "ProfileText", string.Empty, new Vector2(0.055f, 0.08f), new Vector2(0.36f, 0.43f), TextAnchor.MiddleLeft, 27, accentTextColor);
            profileText.fontStyle = FontStyle.Bold;
            CreateResourceImage(root, "GameMoneyIcon", "Items/Icons/score_coin", new Vector2(0.37f, 0.14f), new Vector2(0.405f, 0.37f));
            currencyText = CreateText(root, "CurrencyText", string.Empty, new Vector2(0.415f, 0.08f), new Vector2(0.515f, 0.43f), TextAnchor.MiddleLeft, 25, accentTextColor);
            CreateResourceImage(root, "RubyCurrencyIcon", "Items/Icons/ruby", new Vector2(0.545f, 0.14f), new Vector2(0.58f, 0.37f));
            rubyCurrencyText = CreateText(root, "RubyCurrencyText", string.Empty, new Vector2(0.59f, 0.08f), new Vector2(0.69f, 0.43f), TextAnchor.MiddleLeft, 25, accentTextColor);
            loginStateText = CreateText(root, "LoginStateText", string.Empty, new Vector2(0.74f, 0.08f), new Vector2(0.945f, 0.43f), TextAnchor.MiddleRight, 27, accentTextColor);
            loginStateText.fontStyle = FontStyle.Bold;
        }

        private void BuildContent()
        {
            RectTransform root = CreateRuntimeRoot(contentRoot);
            if (root == null)
            {
                return;
            }

            BuildRecordStrip(root);
            BuildCharacterStage(root);
            startButton = CreateButton(root, "StartButton", "START", new Vector2(0.055f, 0.025f), new Vector2(0.945f, 0.137f), sampleYellow, primaryTextColor, StartGame, false);
            RectTransform startRect = startButton.transform as RectTransform;
            Transform labelTransform = startRect != null ? startRect.Find("StartButtonText") : null;
            if (labelTransform is RectTransform labelRect)
            {
                ConfigureRect(labelRect, new Vector2(0.24f, 0f), new Vector2(0.95f, 1f));
            }

            Image playIcon = CreateImage(startRect, "PlayIcon", new Vector2(0.08f, 0.2f), new Vector2(0.23f, 0.8f), Color.white);
            playIcon.sprite = GetPlaySprite();
            playIcon.preserveAspect = true;
        }

        private void ScheduleStartButtonActivation()
        {
            if (startButtonActivationCoroutine != null)
            {
                StopCoroutine(startButtonActivationCoroutine);
            }

            if (startButton == null)
            {
                startButtonActivationCoroutine = null;
                return;
            }

            startButton.interactable = false;
            startButtonActivationCoroutine = StartCoroutine(ActivateStartButtonWhenReady());
        }

        private IEnumerator ActivateStartButtonWhenReady()
        {
            float activationTime = Time.unscaledTime + startButtonActivationDelay;
            while (Time.unscaledTime < activationTime || IsPrimaryPointerPressed())
            {
                yield return null;
            }

            if (startButton != null)
            {
                startButton.interactable = true;
            }

            startButtonActivationCoroutine = null;
        }

        private static bool IsPrimaryPointerPressed()
        {
            if (Touchscreen.current != null
                && Touchscreen.current.primaryTouch.press.isPressed)
            {
                return true;
            }

            return Mouse.current != null && Mouse.current.leftButton.isPressed;
        }

        private void BuildRecordStrip(RectTransform parent)
        {
            RectTransform panel = CreatePanel(parent, "RecordStrip", new Vector2(0.055f, 0.875f), new Vector2(0.945f, 0.985f), panelColor, false);
            Text title = CreateText(panel, "RecordTitleText", "BEST", new Vector2(0.03f, 0.52f), new Vector2(0.3f, 0.94f), TextAnchor.MiddleLeft, 32, primaryTextColor);
            title.fontStyle = FontStyle.Bold;
            bestFloorText = CreateText(panel, "BestFloorText", string.Empty, new Vector2(0.03f, 0.05f), new Vector2(0.48f, 0.54f), TextAnchor.MiddleLeft, 29, primaryTextColor);
            bestScoreText = CreateText(panel, "BestScoreText", string.Empty, new Vector2(0.36f, 0.05f), new Vector2(0.64f, 0.54f), TextAnchor.MiddleLeft, 29, primaryTextColor);

            RectTransform portraitArea = CreatePanel(
                panel,
                "BestCharacterPortraitArea",
                new Vector2(0.815f, 0.08f),
                new Vector2(0.97f, 0.92f),
                new Color(0f, 0f, 0f, 0.35f),
                true);
            bestCharacterPortraitImage = CreateImage(
                portraitArea,
                "BestCharacterPortrait",
                new Vector2(0.15f, 0f),
                new Vector2(0.85f, 0.7f),
                Color.white);
            bestCharacterPortraitImage.preserveAspect = true;
            bestCharacterPortraitImage.enabled = false;
        }

        private void BuildCharacterStage(RectTransform parent)
        {
            RectTransform stage = CreatePanel(parent, "CharacterStage", new Vector2(0.055f, 0.157f), new Vector2(0.945f, 0.858f), panelColor, false);

            Text sectionTitle = CreateText(stage, "CharacterTitleText", "CHARACTER  |", new Vector2(0.045f, 0.916f), new Vector2(0.31f, 0.986f), TextAnchor.MiddleLeft, 27, accentTextColor);
            sectionTitle.fontStyle = FontStyle.Bold;
            selectedCharacterNameText = CreateText(stage, "SelectedCharacterNameText", string.Empty, new Vector2(0.32f, 0.916f), new Vector2(0.73f, 0.986f), TextAnchor.MiddleLeft, 27, accentTextColor);
            selectedCharacterNameText.fontStyle = FontStyle.Bold;
            selectedCharacterPositionText = CreateText(stage, "CharacterPositionText", string.Empty, new Vector2(0.75f, 0.916f), new Vector2(0.95f, 0.986f), TextAnchor.MiddleRight, 27, accentTextColor);
            selectedCharacterPositionText.fontStyle = FontStyle.Bold;

            selectedCharacterPortraitImage = CreateImage(stage, "SelectedCharacterPortrait", new Vector2(0.31f, 0.646f), new Vector2(0.69f, 0.916f), Color.white);
            selectedCharacterPortraitImage.preserveAspect = true;

            bool canNavigate = GetAvailableCharacterCount() > 1;
            CreateIconButton(stage, "PreviousCharacterButton", GetArrowSprite(false), new Vector2(0.12f, 0.711f), new Vector2(0.24f, 0.851f), SelectPreviousCharacter, canNavigate, "Previous Character");
            CreateIconButton(stage, "NextCharacterButton", GetArrowSprite(true), new Vector2(0.76f, 0.711f), new Vector2(0.88f, 0.851f), SelectNextCharacter, canNavigate, "Next Character");

            selectedCharacterLevelText = CreateText(stage, "SelectedCharacterLevelText", string.Empty, new Vector2(0.055f, 0.585f), new Vector2(0.35f, 0.641f), TextAnchor.MiddleLeft, 27, accentTextColor);
            selectedCharacterLevelText.fontStyle = FontStyle.Bold;

            BuildExperienceGauge(stage);
            CreateDivider(stage, "InfoDivider", new Vector2(0.055f, 0.511f), new Vector2(0.945f, 0.514f));

            selectedCharacterStatNamesText = CreateText(stage, "CharacterStatNamesText", "SPEED\nREFLEX\nVITALITY\nFEVER DRIVE\nITEM LUCK\nAWAKENING", new Vector2(0.07f, 0.213f), new Vector2(0.48f, 0.502f), TextAnchor.MiddleLeft, 28, accentTextColor);
            selectedCharacterStatValuesText = CreateText(stage, "CharacterStatValuesText", string.Empty, new Vector2(0.48f, 0.213f), new Vector2(0.93f, 0.502f), TextAnchor.MiddleLeft, 28, accentTextColor);
            selectedCharacterStatNamesText.fontStyle = FontStyle.Bold;
            selectedCharacterStatValuesText.fontStyle = FontStyle.Bold;
            selectedCharacterStatNamesText.lineSpacing = 1.05f;
            selectedCharacterStatValuesText.lineSpacing = 1.05f;

            RectTransform skillPanel = CreatePanel(stage, "SkillDescriptionPanel", new Vector2(0.055f, 0.033f), new Vector2(0.945f, 0.204f), new Color(0f, 0f, 0f, 0.64f), true);
            Text skillTitleText = CreateText(skillPanel, "SkillTitleText", "SKILL DESCRIPTION", new Vector2(0.035f, 0.62f), new Vector2(0.965f, 0.92f), TextAnchor.MiddleLeft, 28, primaryTextColor);
            skillTitleText.fontStyle = FontStyle.Bold;
            selectedCharacterSkillDescriptionText = CreateText(skillPanel, "SkillDescriptionText", string.Empty, new Vector2(0.035f, 0.08f), new Vector2(0.965f, 0.64f), TextAnchor.UpperLeft, 28, primaryTextColor);
        }

        private void BuildExperienceGauge(RectTransform parent)
        {
            RectTransform background = CreatePanel(parent, "ExperienceGauge", new Vector2(0.055f, 0.539f), new Vector2(0.945f, 0.581f), new Color(0f, 0f, 0f, 0.82f), false);
            selectedCharacterExperienceFill = CreateImage(background, "Fill", new Vector2(0.006f, 0.12f), new Vector2(0.994f, 0.88f), experienceColor);
            selectedCharacterExperienceFill.type = Image.Type.Simple;
            selectedCharacterExperienceFillRect = selectedCharacterExperienceFill.rectTransform;
            selectedCharacterExperienceFillRect.pivot = new Vector2(0f, 0.5f);
            selectedCharacterExperienceText = CreateText(background, "ExperienceText", string.Empty, new Vector2(0.02f, 0f), new Vector2(0.98f, 1f), TextAnchor.MiddleLeft, 20, primaryTextColor);
            selectedCharacterExperienceText.fontStyle = FontStyle.Bold;
        }

        private void BuildFooter()
        {
            RectTransform root = CreateRuntimeRoot(footerRoot);
            if (root == null)
            {
                return;
            }

            const float leftMargin = 0.035f;
            const float gap = 0.012f;
            float buttonWidth = (0.93f - gap * (FooterMenuLabels.Length - 1)) / FooterMenuLabels.Length;
            for (int i = 0; i < FooterMenuLabels.Length; i++)
            {
                float minX = leftMargin + i * (buttonWidth + gap);
                bool isArtifactButton = i == 3;
                bool isLankButton = i == 5;
                bool interactable =
                    (isArtifactButton && ArtifactCatalog.Instance.IsSystemUnlocked())
                    || isLankButton;
                UnityEngine.Events.UnityAction onClick =
                    isArtifactButton
                        ? OpenArtifactPanel
                        : isLankButton
                            ? OpenLankPanel
                            : null;
                CreateFooterMenuButton(
                    root,
                    FooterMenuLabels[i],
                    FooterMenuIconKeys[i],
                    minX,
                    minX + buttonWidth,
                    interactable,
                    onClick);
            }

            RectTransform adArea = CreatePanel(root, "BannerAdArea", new Vector2(0f, 0f), new Vector2(1f, 0.31f), disabledButtonColor, false);
            Text adLabel = CreateText(adArea, "AdLabel", "AD", Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, 28, primaryTextColor);
            adLabel.fontStyle = FontStyle.Bold;
        }

        private void RefreshLobbyData()
        {
            ApplyUserProfileData();

            if (profileText != null)
            {
                profileText.text = $"NAME : {playerNickname}";
            }

            if (currencyText != null)
            {
                currencyText.text = UserProfileManager.GameMoney.ToString("N0");
            }

            if (rubyCurrencyText != null)
            {
                rubyCurrencyText.text = UserProfileManager.Ruby.ToString("N0");
            }

            if (loginStateText != null)
            {
                loginStateText.text = GetAuthenticationStateLabel();
            }

            if (bestFloorText != null)
            {
                bestFloorText.text = $"Floor :  {Mathf.Max(0, bestHighestFloor)}F";
            }

            if (bestScoreText != null)
            {
                bestScoreText.text = $"Score :  {Mathf.Max(0, bestScore):N0}";
            }

            RefreshBestCharacterPortrait();
            RefreshSelectedCharacterInfo();
        }

        private void ApplyUserProfileData()
        {
            string profileNickname = UserProfileManager.Nickname;
            if (!string.IsNullOrWhiteSpace(profileNickname))
            {
                playerNickname = profileNickname;
            }

            bestHighestFloor = UserProfileManager.BestHighestFloor;
            bestScore = UserProfileManager.BestScore;
            bestCharacterId = UserProfileManager.BestCharacterId;
        }

        private void RefreshSelectedCharacterInfo()
        {
            CharacterProgressionSnapshot progression = CharacterProgressionState.GetSnapshot(selectedCharacter);

            if (selectedCharacterLevelText != null)
            {
                selectedCharacterLevelText.text = selectedCharacter != null ? $"Lv. {progression.Level}" : "Lv. -";
            }

            if (selectedCharacterNameText != null)
            {
                selectedCharacterNameText.text = selectedCharacter != null ? selectedCharacter.DisplayName : "No Character";
            }

            if (selectedCharacterPositionText != null)
            {
                selectedCharacterPositionText.text = GetSelectedCharacterPositionLabel();
            }

            if (selectedCharacterStatValuesText != null)
            {
                selectedCharacterStatValuesText.text = GetSelectedCharacterStatValuesLabel();
            }

            if (selectedCharacterSkillDescriptionText != null)
            {
                selectedCharacterSkillDescriptionText.text = GetSelectedCharacterSkillDescription();
                selectedCharacterSkillDescriptionText.color = CharacterProgressionState.IsSkillUnlocked(selectedCharacter)
                    ? primaryTextColor
                    : lockedSkillTextColor;
            }

            if (selectedCharacterExperienceText != null)
            {
                selectedCharacterExperienceText.text = progression.IsMaxLevel
                    ? "MAX LEVEL"
                    : $"XP  {progression.CurrentExperience:N0} / {progression.RequiredExperience:N0}";
            }

            if (selectedCharacterExperienceFillRect != null)
            {
                // (변경) Sprite 유무와 관계없이 현재 XP 비율만큼 게이지 폭을 직접 조절한다.
                float normalizedExperience = Mathf.Clamp01(progression.NormalizedExperience);
                selectedCharacterExperienceFillRect.anchorMax = new Vector2(
                    Mathf.Lerp(0.006f, 0.994f, normalizedExperience),
                    0.88f);
            }

            RefreshSelectedCharacterPortrait();
        }

        private string GetSelectedCharacterPositionLabel()
        {
            int total = GetAvailableCharacterCount();
            if (selectedCharacter == null || total <= 0)
            {
                return "0 / 0";
            }

            int visibleIndex = 0;
            for (int i = 0; i < availableCharacters.Length; i++)
            {
                if (availableCharacters[i] == null || !CharacterProgressionState.IsOwned(availableCharacters[i]))
                {
                    continue;
                }

                visibleIndex++;
                if (availableCharacters[i] == selectedCharacter)
                {
                    return $"{visibleIndex} / {total}";
                }
            }

            return $"1 / {total}";
        }

        private string GetSelectedCharacterStatValuesLabel()
        {
            if (selectedCharacter == null)
            {
                return ":  -\n:  -\n:  -\n:  -\n:  -\n:  -";
            }

            int speedIndex = GetRelativeStatIndex(selectedCharacter.MoveSpeedColumnsPerSecond, AgentXBaseMoveSpeed);
            int reflexIndex = GetInverseRelativeStatIndex(selectedCharacter.PivotCooldownSeconds, AgentXBasePivotCooldown);
            int feverDriveIndex = GetRelativeStatIndex(selectedCharacter.FeverGainPerColumn, AgentXBaseFeverGainPerColumn);
            float itemLuckPercent = selectedCharacter.ItemChance * 100f;
            return $":  {speedIndex}\n:  {reflexIndex}\n:  {selectedCharacter.MaxLife}\n:  {feverDriveIndex}\n:  {itemLuckPercent:0.#}%\n:  LV.{selectedCharacter.SkillUnlockLevel}";
        }

        private string GetSelectedCharacterSkillDescription()
        {
            CharacterSkillDefinition skill = selectedCharacter != null ? selectedCharacter.CharacterSkill : null;
            if (skill == null || string.IsNullOrWhiteSpace(skill.Description))
            {
                return "No skill description available.";
            }

            return skill.Description
                .Replace("P1", skill.P1.ToString("0.#"))
                .Replace("P2", skill.P2.ToString("0.#"))
                .Replace("P3", skill.P3.ToString("0.#"))
                .Replace("P4", skill.P4.ToString("0.#"))
                .Replace("P5", skill.P5.ToString("0.#"));
        }

        private int GetRelativeStatIndex(float value, float baseline)
        {
            return baseline > 0f ? Mathf.Max(0, Mathf.RoundToInt(value / baseline * 100f)) : 0;
        }

        private int GetInverseRelativeStatIndex(float value, float baseline)
        {
            return value > 0f ? Mathf.Max(0, Mathf.RoundToInt(baseline / value * 100f)) : 0;
        }

        private void RefreshSelectedCharacterPortrait()
        {
            if (selectedCharacterPortraitImage == null)
            {
                return;
            }

            Sprite portrait = selectedCharacter != null ? selectedCharacter.PortraitSprite : null;
            selectedCharacterPortraitImage.sprite = portrait;
            selectedCharacterPortraitImage.enabled = portrait != null;
        }

        private void RefreshBestCharacterPortrait()
        {
            if (bestCharacterPortraitImage == null)
            {
                return;
            }

            DestroyGeneratedBestCharacterPortraitSprite();
            CharacterDefinition bestCharacter =
                FindCharacterDefinitionById(bestCharacterId);
            generatedBestCharacterPortraitSprite =
                CreateFacePortraitSprite(bestCharacter);
            bestCharacterPortraitImage.sprite =
                generatedBestCharacterPortraitSprite;
            bestCharacterPortraitImage.enabled =
                generatedBestCharacterPortraitSprite != null;
        }

        private CharacterDefinition FindCharacterDefinitionById(
            string characterId)
        {
            if (availableCharacters == null
                || string.IsNullOrWhiteSpace(characterId))
            {
                return null;
            }

            for (int i = 0; i < availableCharacters.Length; i++)
            {
                CharacterDefinition candidate = availableCharacters[i];
                if (candidate != null
                    && string.Equals(
                        candidate.CharacterId,
                        characterId,
                        System.StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static Sprite CreateFacePortraitSprite(
            CharacterDefinition definition)
        {
            Sprite source =
                definition != null ? definition.PortraitSprite : null;
            if (source == null)
            {
                return null;
            }

            Rect normalizedRect =
                AddPortraitTopPadding(
                    ClampNormalizedRect(definition.IngamePortraitFaceRect),
                    0.05f);
            Rect sourceRect = source.textureRect;
            Rect faceRect = new Rect(
                sourceRect.x + sourceRect.width * normalizedRect.x,
                sourceRect.y + sourceRect.height * normalizedRect.y,
                Mathf.Max(1f, sourceRect.width * normalizedRect.width),
                Mathf.Max(1f, sourceRect.height * normalizedRect.height));
            Sprite portrait = Sprite.Create(
                source.texture,
                PixelSnapRect(faceRect),
                new Vector2(0.5f, 0.5f),
                source.pixelsPerUnit,
                0,
                SpriteMeshType.FullRect,
                Vector4.zero,
                false);
            portrait.name = $"{source.name}_BestFace";
            portrait.hideFlags = HideFlags.HideAndDontSave;
            return portrait;
        }

        private void DestroyGeneratedBestCharacterPortraitSprite()
        {
            if (generatedBestCharacterPortraitSprite == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(generatedBestCharacterPortraitSprite);
            }
            else
            {
                DestroyImmediate(generatedBestCharacterPortraitSprite);
            }

            generatedBestCharacterPortraitSprite = null;
        }

        private static Rect ClampNormalizedRect(Rect rect)
        {
            float width = Mathf.Clamp(rect.width, 0.01f, 1f);
            float height = Mathf.Clamp(rect.height, 0.01f, 1f);
            float x = Mathf.Clamp(rect.x, 0f, 1f - width);
            float y = Mathf.Clamp(rect.y, 0f, 1f - height);
            return new Rect(x, y, width, height);
        }

        private static Rect AddPortraitTopPadding(
            Rect rect,
            float normalizedPadding)
        {
            // (수정) BEST 초상화는 슬롯 여백을 활용해 머리 위쪽 크롭 여유를 확보한다.
            float top = Mathf.Min(
                1f,
                rect.yMax + Mathf.Max(0f, normalizedPadding));
            return new Rect(rect.x, rect.y, rect.width, top - rect.y);
        }

        private static Rect PixelSnapRect(Rect rect)
        {
            return new Rect(
                Mathf.Round(rect.x),
                Mathf.Round(rect.y),
                Mathf.Max(1f, Mathf.Round(rect.width)),
                Mathf.Max(1f, Mathf.Round(rect.height)));
        }

        private void SelectPreviousCharacter()
        {
            SelectAdjacentCharacter(-1);
        }

        private void SelectNextCharacter()
        {
            SelectAdjacentCharacter(1);
        }

        private void SelectAdjacentCharacter(int direction)
        {
            if (availableCharacters == null || availableCharacters.Length == 0 || direction == 0)
            {
                return;
            }

            int currentIndex = System.Array.IndexOf(availableCharacters, selectedCharacter);
            int startIndex = currentIndex >= 0 ? currentIndex : 0;

            for (int step = 1; step <= availableCharacters.Length; step++)
            {
                int index = (startIndex + direction * step) % availableCharacters.Length;
                if (index < 0)
                {
                    index += availableCharacters.Length;
                }

                CharacterDefinition candidate = availableCharacters[index];
                if (candidate != null
                    && candidate != selectedCharacter
                    && CharacterProgressionState.IsOwned(candidate))
                {
                    SelectCharacter(candidate);
                    return;
                }
            }
        }

        private void SelectCharacter(CharacterDefinition characterDefinition)
        {
            if (characterDefinition == null || !CharacterProgressionState.IsOwned(characterDefinition))
            {
                return;
            }

            selectedCharacter = characterDefinition;
            CharacterSelectionState.Select(selectedCharacter);
            RefreshSelectedCharacterInfo();
            Debug.Log($"Lobby character selected: {selectedCharacter.DisplayName} ({selectedCharacter.CharacterId})", this);
        }

        private void EnsureSelectedCharacter()
        {
            if (selectedCharacter != null && CharacterProgressionState.IsOwned(selectedCharacter))
            {
                CharacterSelectionState.Select(selectedCharacter);
                return;
            }

            selectedCharacter = null;
            selectedCharacter = CharacterSelectionState.Resolve(GetFirstAvailableCharacter(), availableCharacters);
            if (selectedCharacter != null)
            {
                CharacterSelectionState.Select(selectedCharacter);
            }
        }

        private CharacterDefinition GetFirstAvailableCharacter()
        {
            if (availableCharacters == null)
            {
                return null;
            }

            for (int i = 0; i < availableCharacters.Length; i++)
            {
                if (availableCharacters[i] != null && CharacterProgressionState.IsOwned(availableCharacters[i]))
                {
                    return availableCharacters[i];
                }
            }

            return null;
        }

        private int GetAvailableCharacterCount()
        {
            if (availableCharacters == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < availableCharacters.Length; i++)
            {
                if (availableCharacters[i] != null && CharacterProgressionState.IsOwned(availableCharacters[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private void HandleCharacterProgressChanged(string characterId)
        {
            if (selectedCharacter != null && selectedCharacter.CharacterId == characterId)
            {
                RefreshSelectedCharacterInfo();
            }
        }

        private void HandleUserProfileChanged()
        {
            RefreshLobbyData();
        }

        private void HandleAuthenticationStateChanged(AuthenticationState state)
        {
            RefreshLobbyData();
        }

        private void HandleCollectionChanged(CollectionChangeResult result)
        {
            ClearRuntimeRoot(footerRoot);
            BuildFooter();
        }

        private static string GetAuthenticationStateLabel()
        {
            if (AuthenticationManager.IsAuthenticated)
            {
                return AuthenticationManager.CurrentSession.IsGuest ? "GUEST" : "ONLINE";
            }

            return AuthenticationManager.State == AuthenticationState.Authenticating
                ? "CONNECTING"
                : "OFFLINE";
        }

        private void ApplySafeAreaLayout()
        {
            if (Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }

            Rect safeArea = Screen.safeArea;
            Vector2 safeMin = new Vector2(safeArea.xMin / Screen.width, safeArea.yMin / Screen.height);
            Vector2 safeMax = new Vector2(safeArea.xMax / Screen.width, safeArea.yMax / Screen.height);

            ApplyBand(headerRoot, safeMin, safeMax, HeaderBandMin, HeaderBandMax);
            ApplyBand(contentRoot, safeMin, safeMax, ContentBandMin, ContentBandMax);
            ApplyBand(footerRoot, safeMin, safeMax, FooterBandMin, FooterBandMax);

            lastSafeArea = safeArea;
            lastScreenSize = new Vector2Int(Screen.width, Screen.height);
        }

        private static void ApplyBand(RectTransform target, Vector2 safeMin, Vector2 safeMax, Vector2 bandMin, Vector2 bandMax)
        {
            if (target == null)
            {
                return;
            }

            Vector2 safeSize = safeMax - safeMin;
            target.anchorMin = safeMin + Vector2.Scale(safeSize, bandMin);
            target.anchorMax = safeMin + Vector2.Scale(safeSize, bandMax);
            target.offsetMin = Vector2.zero;
            target.offsetMax = Vector2.zero;
        }

        private RectTransform CreateRuntimeRoot(RectTransform parent)
        {
            if (parent == null)
            {
                return null;
            }

            Transform existing = parent.Find(RuntimeRootName);
            RectTransform root = existing as RectTransform;
            if (root == null)
            {
                GameObject rootObject = new GameObject(RuntimeRootName, typeof(RectTransform));
                rootObject.layer = parent.gameObject.layer;
                rootObject.transform.SetParent(parent, false);
                root = rootObject.GetComponent<RectTransform>();
            }

            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            root.pivot = new Vector2(0.5f, 0.5f);
            return root;
        }

        private RectTransform CreatePanel(RectTransform parent, string objectName, Vector2 anchorMin, Vector2 anchorMax, Color color, bool useOutline)
        {
            GameObject panelObject = useOutline
                ? new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Outline))
                : new GameObject(objectName, typeof(RectTransform), typeof(Image));
            panelObject.layer = parent.gameObject.layer;
            panelObject.transform.SetParent(parent, false);

            RectTransform rectTransform = ConfigureRect(panelObject.GetComponent<RectTransform>(), anchorMin, anchorMax);
            Image image = panelObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;

            if (useOutline)
            {
                Outline outline = panelObject.GetComponent<Outline>();
                outline.effectColor = new Color(1f, 1f, 1f, 0.18f);
                outline.effectDistance = new Vector2(2f, -2f);
                outline.useGraphicAlpha = true;
            }

            return rectTransform;
        }

        private Text CreateText(RectTransform parent, string objectName, string message, Vector2 anchorMin, Vector2 anchorMax, TextAnchor alignment, int fontSize, Color color)
        {
            GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
            textObject.layer = parent.gameObject.layer;
            textObject.transform.SetParent(parent, false);
            ConfigureRect(textObject.GetComponent<RectTransform>(), anchorMin, anchorMax);

            Text text = textObject.GetComponent<Text>();
            text.text = message;
            text.alignment = alignment;
            text.color = color;
            text.font = lobbyFont;
            text.fontSize = Mathf.Max(1, fontSize);
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Mathf.Max(12, fontSize / 2);
            text.resizeTextMaxSize = Mathf.Max(1, fontSize);
            return text;
        }

        private Image CreateImage(RectTransform parent, string objectName, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            imageObject.layer = parent.gameObject.layer;
            imageObject.transform.SetParent(parent, false);
            ConfigureRect(imageObject.GetComponent<RectTransform>(), anchorMin, anchorMax);

            Image image = imageObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Image CreateResourceImage(RectTransform parent, string objectName, string resourcePath, Vector2 anchorMin, Vector2 anchorMax)
        {
            Image image = CreateImage(parent, objectName, anchorMin, anchorMax, Color.white);
            image.sprite = Resources.Load<Sprite>(resourcePath);
            image.color = image.sprite != null ? Color.white : secondaryTextColor;
            image.preserveAspect = true;
            return image;
        }

        private void CreateDivider(RectTransform parent, string objectName, Vector2 anchorMin, Vector2 anchorMax)
        {
            CreateImage(parent, objectName, anchorMin, anchorMax, new Color(1f, 1f, 1f, 0.14f));
        }

        private Button CreateIconButton(RectTransform parent, string objectName, Sprite icon, Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction onClick, bool interactable, string accessibleName)
        {
            GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Shadow), typeof(Outline));
            buttonObject.layer = parent.gameObject.layer;
            buttonObject.transform.SetParent(parent, false);
            RectTransform rectTransform = ConfigureRect(buttonObject.GetComponent<RectTransform>(), anchorMin, anchorMax);

            Image background = buttonObject.GetComponent<Image>();
            background.color = interactable ? sampleNavy : new Color(0.08f, 0.1f, 0.14f, 0.88f);
            ConfigureButtonDepth(buttonObject, interactable);

            Image iconImage = CreateImage(rectTransform, "Icon", new Vector2(0.2f, 0.2f), new Vector2(0.8f, 0.8f), interactable ? primaryTextColor : secondaryTextColor * 0.45f);
            iconImage.sprite = icon;
            iconImage.preserveAspect = true;

            Button button = buttonObject.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = background;
            button.interactable = interactable;
            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            // 별도 접근성 패키지 도입 전까지 오브젝트 이름에 버튼 목적을 유지한다.
            buttonObject.name = string.IsNullOrWhiteSpace(accessibleName) ? objectName : $"{objectName}_{accessibleName}";
            return button;
        }

        private Button CreateButton(RectTransform parent, string objectName, string label, Vector2 anchorMin, Vector2 anchorMax, Color backgroundColor, Color textColor, UnityEngine.Events.UnityAction onClick, bool interactable)
        {
            GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Shadow), typeof(Outline));
            buttonObject.layer = parent.gameObject.layer;
            buttonObject.transform.SetParent(parent, false);
            RectTransform rectTransform = ConfigureRect(buttonObject.GetComponent<RectTransform>(), anchorMin, anchorMax);

            Image image = buttonObject.GetComponent<Image>();
            image.color = backgroundColor;
            ConfigureButtonDepth(buttonObject, interactable);

            Button button = buttonObject.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = image;
            button.interactable = interactable;
            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            Color edgeColor = objectName == "StartButton"
                ? sampleOrange
                : new Color(backgroundColor.r * 0.58f, backgroundColor.g * 0.58f, backgroundColor.b * 0.58f, backgroundColor.a);
            CreateImage(rectTransform, "BottomEdge", new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.12f), edgeColor);
            Text buttonText = CreateText(rectTransform, $"{objectName}Text", label, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, 38, textColor);
            buttonText.fontStyle = FontStyle.Bold;
            buttonText.verticalOverflow = VerticalWrapMode.Overflow;
            AddStrongTextOutline(buttonText);
            return button;
        }

        private Button CreateTemporaryMenuButton(RectTransform parent, string label, float minX, float maxX)
        {
            string objectName = label.Replace(" ", string.Empty) + "Button";
            GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
            buttonObject.layer = parent.gameObject.layer;
            buttonObject.transform.SetParent(parent, false);
            RectTransform rectTransform = ConfigureRect(
                buttonObject.GetComponent<RectTransform>(),
                new Vector2(minX, 0.39f),
                new Vector2(maxX, 0.95f));

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(disabledButtonColor.r, disabledButtonColor.g, disabledButtonColor.b, 0.82f);

            Outline outline = buttonObject.GetComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.72f);
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = true;

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            Text labelText = CreateText(rectTransform, "Label", label, new Vector2(0.06f, 0.08f), new Vector2(0.94f, 0.34f), TextAnchor.MiddleCenter, 16, primaryTextColor);
            labelText.fontStyle = FontStyle.Bold;
            return button;
        }

        private Button CreateFooterMenuButton(
            RectTransform parent,
            string label,
            string iconKey,
            float minX,
            float maxX,
            bool interactable,
            UnityEngine.Events.UnityAction onClick)
        {
            string objectName = label.Replace(" ", string.Empty) + "Button";
            GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Shadow), typeof(Outline));
            buttonObject.layer = parent.gameObject.layer;
            buttonObject.transform.SetParent(parent, false);
            RectTransform rectTransform = ConfigureRect(
                buttonObject.GetComponent<RectTransform>(),
                new Vector2(minX, 0.39f),
                new Vector2(maxX, 0.95f));

            Image image = buttonObject.GetComponent<Image>();
            image.color = interactable
                ? samplePurple
                : sampleNavy;
            ConfigureButtonDepth(buttonObject, interactable);

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.interactable = interactable;
            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            Image icon = CreateResourceImage(
                rectTransform,
                "Icon",
                $"UI/Lobby/Menu/{iconKey}",
                new Vector2(0.2f, 0.33f),
                new Vector2(0.8f, 0.92f));
            if (icon != null)
            {
                icon.preserveAspect = true;
                icon.color = interactable ? Color.white : new Color(0.72f, 0.78f, 0.86f, 0.78f);
            }

            CreateImage(rectTransform, "TopHighlight", new Vector2(0.06f, 0.9f), new Vector2(0.94f, 0.95f), new Color(1f, 1f, 1f, interactable ? 0.34f : 0.14f));
            Text labelText = CreateText(rectTransform, "Label", label, new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.3f), TextAnchor.MiddleCenter, 15, primaryTextColor);
            labelText.fontStyle = FontStyle.Bold;
            labelText.resizeTextForBestFit = true;
            labelText.resizeTextMinSize = 10;
            labelText.resizeTextMaxSize = 15;
            AddStrongTextOutline(labelText);
            return button;
        }

        private void ConfigureButtonDepth(GameObject buttonObject, bool interactable)
        {
            Shadow shadow = buttonObject.GetComponent<Shadow>();
            shadow.effectColor = new Color(sampleOutline.r, sampleOutline.g, sampleOutline.b, interactable ? 0.9f : 0.68f);
            shadow.effectDistance = new Vector2(0f, -7f);
            shadow.useGraphicAlpha = true;

            Outline outline = buttonObject.GetComponent<Outline>();
            outline.effectColor = sampleOutline;
            outline.effectDistance = new Vector2(4f, -4f);
            outline.useGraphicAlpha = true;
        }

        private void AddStrongTextOutline(Text text)
        {
            if (text == null)
            {
                return;
            }

            Outline outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = sampleOutline;
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = true;
        }

        private void OpenArtifactPanel()
        {
            CloseContentOverlay("LankLobbyPanel");
            ArtifactLobbyPanel.Show(contentRoot, lobbyFont, accentTextColor, primaryTextColor, panelColor);
        }

        private void OpenLankPanel()
        {
            CloseContentOverlay("ArtifactLobbyPanel");
            LankLobbyPanel.Show(
                contentRoot,
                lobbyFont,
                accentTextColor,
                primaryTextColor,
                panelColor,
                availableCharacters);
        }

        private void CloseContentOverlay(string objectName)
        {
            Transform existing =
                contentRoot != null ? contentRoot.Find(objectName) : null;
            if (existing != null)
            {
                Destroy(existing.gameObject);
            }
        }

        private static RectTransform ConfigureRect(RectTransform rectTransform, Vector2 anchorMin, Vector2 anchorMax)
        {
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            return rectTransform;
        }

        private static Sprite GetArrowSprite(bool pointsRight)
        {
            Sprite cachedSprite = pointsRight ? rightArrowSprite : leftArrowSprite;
            if (cachedSprite != null)
            {
                return cachedSprite;
            }

            const int textureSize = 32;
            Texture2D texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
            {
                name = pointsRight ? "LobbyArrowRightTexture" : "LobbyArrowLeftTexture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] pixels = new Color32[textureSize * textureSize];
            Color32 arrowColor = new Color32(255, 255, 255, 255);
            for (int y = 5; y <= 27; y++)
            {
                int headStart = 5 + Mathf.Abs(y - 16);
                for (int x = headStart; x <= 16; x++)
                {
                    SetArrowPixel(pixels, textureSize, x, y, pointsRight, arrowColor);
                }
            }

            for (int y = 12; y <= 20; y++)
            {
                for (int x = 14; x <= 27; x++)
                {
                    SetArrowPixel(pixels, textureSize, x, y, pointsRight, arrowColor);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, textureSize, textureSize), new Vector2(0.5f, 0.5f), textureSize);
            sprite.name = pointsRight ? "LobbyArrowRight" : "LobbyArrowLeft";
            sprite.hideFlags = HideFlags.HideAndDontSave;

            if (pointsRight)
            {
                rightArrowSprite = sprite;
            }
            else
            {
                leftArrowSprite = sprite;
            }

            return sprite;
        }

        private static Sprite GetSettingsSprite()
        {
            if (settingsSprite != null)
            {
                return settingsSprite;
            }

            const int textureSize = 64;
            Texture2D texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
            {
                name = "LobbySettingsTexture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] pixels = new Color32[textureSize * textureSize];
            Color32 iconColor = new Color32(255, 255, 255, 255);
            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    float normalizedX = ((x + 0.5f) / textureSize - 0.5f) * 2f;
                    float normalizedY = ((y + 0.5f) / textureSize - 0.5f) * 2f;
                    float radius = Mathf.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY);
                    float angle = Mathf.Atan2(normalizedY, normalizedX);
                    float outerRadius = Mathf.Cos(angle * 8f) > 0.35f ? 0.94f : 0.78f;
                    if (radius >= 0.34f && radius <= outerRadius)
                    {
                        pixels[y * textureSize + x] = iconColor;
                    }
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            settingsSprite = Sprite.Create(texture, new Rect(0f, 0f, textureSize, textureSize), new Vector2(0.5f, 0.5f), textureSize);
            settingsSprite.name = "LobbySettings";
            settingsSprite.hideFlags = HideFlags.HideAndDontSave;
            return settingsSprite;
        }

        private static Sprite GetPlaySprite()
        {
            if (playSprite != null)
            {
                return playSprite;
            }

            const int textureSize = 64;
            Texture2D texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
            {
                name = "LobbyPlayTexture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] pixels = new Color32[textureSize * textureSize];
            Color32 iconColor = new Color32(255, 255, 255, 255);
            for (int x = 14; x <= 50; x++)
            {
                float progress = (x - 14f) / 36f;
                int halfHeight = Mathf.RoundToInt((1f - progress) * 22f + 2f);
                for (int y = 32 - halfHeight; y <= 32 + halfHeight; y++)
                {
                    pixels[y * textureSize + x] = iconColor;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            playSprite = Sprite.Create(texture, new Rect(0f, 0f, textureSize, textureSize), new Vector2(0.5f, 0.5f), textureSize);
            playSprite.name = "LobbyPlay";
            playSprite.hideFlags = HideFlags.HideAndDontSave;
            return playSprite;
        }

        private static void SetArrowPixel(Color32[] pixels, int textureSize, int x, int y, bool mirrorHorizontally, Color32 color)
        {
            int targetX = mirrorHorizontally ? textureSize - 1 - x : x;
            pixels[y * textureSize + targetX] = color;
        }

        private void ClearRuntimeRoot(RectTransform parent)
        {
            if (parent == null)
            {
                return;
            }

            Transform existing = parent.Find(RuntimeRootName);
            if (existing == null)
            {
                return;
            }

            for (int i = existing.childCount - 1; i >= 0; i--)
            {
                Transform child = existing.GetChild(i);
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

        private void EnsureReferences()
        {
            if (headerRoot == null)
            {
                headerRoot = transform.Find("HeaderUI") as RectTransform;
            }

            if (contentRoot == null)
            {
                contentRoot = transform.Find("ContentUI") as RectTransform;
            }

            if (footerRoot == null)
            {
                footerRoot = transform.Find("FooterUI") as RectTransform;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            bestHighestFloor = Mathf.Max(0, bestHighestFloor);
            bestScore = Mathf.Max(0, bestScore);
        }
#endif
    }
}
