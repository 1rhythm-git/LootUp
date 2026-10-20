using System;
using System.Collections.Generic;
using System.Linq;
using LootUp.Core.Characters;
using LootUp.Core.Enemies;
using UnityEditor;
using UnityEngine;

namespace LootUp.Editor.Balance
{
    public sealed class LevelBalanceSimulatorWindow : EditorWindow
    {
        private const string MenuPath = "Tools/LootUp/Level Balance Simulator";
        private static readonly int[] IterationOptions = { 1000, 5000, 10000 };
        private static readonly string[] IterationLabels = { "1,000", "5,000", "10,000" };
        private static readonly string[] TabLabels =
        {
            "대시보드",
            "구간 분석",
            "캐릭터 비교",
            "가정·경고"
        };

        [SerializeField]
        private DifficultyBandDefinition difficultyBands;

        [SerializeField]
        private TextAsset itemTableCsv;

        [SerializeField]
        private CharacterDefinition selectedCharacter;

        [SerializeField]
        private SimulationSkillProfile skillProfile = SimulationSkillProfile.Standard;

        [SerializeField]
        private int characterLevel = 1;

        [SerializeField]
        private int iterationIndex = 1;

        [SerializeField]
        private int maximumFloor = 130;

        [SerializeField]
        private int seed = 20261020;

        [SerializeField]
        private float initialTimeSeconds = 90f;

        [SerializeField]
        private float columnsPerFloor = 7f;

        [SerializeField]
        private float elevatorSecondsPerFloor = 1.1f;

        [SerializeField]
        private float itemsPerFloor = 0.6f;

        [SerializeField]
        private float baseEnemyMinSpeed = 130f;

        [SerializeField]
        private float baseEnemyMaxSpeed = 360f;

        [SerializeField]
        private float speedStepPerEnemy = 22f;

        [SerializeField]
        private int risingAdvanceFloors = 10;

        [SerializeField]
        private bool showAdvancedSettings;

        [SerializeField]
        private int selectedTab;

        private readonly List<CharacterDefinition> characters = new List<CharacterDefinition>();
        private readonly List<LevelBalanceSimulationResult> results = new List<LevelBalanceSimulationResult>();
        private Vector2 scrollPosition;
        private string statusMessage = "실제 프로젝트 데이터를 불러온 뒤 시뮬레이션을 실행하세요.";
        private string lastExportPath = string.Empty;
        private GUIStyle headerTitleStyle;
        private GUIStyle headerSubtitleStyle;
        private GUIStyle cardValueStyle;
        private GUIStyle cardLabelStyle;
        private GUIStyle sectionTitleStyle;
        private GUIStyle mutedStyle;
        private GUIStyle tableHeaderStyle;

        [MenuItem(MenuPath)]
        public static void Open()
        {
            LevelBalanceSimulatorWindow window = GetWindow<LevelBalanceSimulatorWindow>();
            window.titleContent = new GUIContent("Balance Simulator");
            window.minSize = new Vector2(980f, 680f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Balance Simulator");
            minSize = new Vector2(980f, 680f);
            AutoLoadProjectData();
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawHeader();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DrawConfiguration();
            DrawActionBar();
            DrawStatus();

            if (results.Count > 0)
            {
                selectedTab = GUILayout.Toolbar(selectedTab, TabLabels, GUILayout.Height(28f));
                EditorGUILayout.Space(8f);
                switch (selectedTab)
                {
                    case 1:
                        DrawBandAnalysis();
                        break;
                    case 2:
                        DrawCharacterComparison();
                        break;
                    case 3:
                        DrawAssumptionsAndWarnings();
                        break;
                    default:
                        DrawDashboard();
                        break;
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "시뮬레이션을 실행하면 요약 카드, 난도·생존 곡선, 구간별 통과율과 자동 경고가 이 영역에 표시됩니다.",
                    MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            Rect headerRect = GUILayoutUtility.GetRect(0f, 86f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(
                headerRect,
                EditorGUIUtility.isProSkin
                    ? new Color(0.08f, 0.16f, 0.18f)
                    : new Color(0.11f, 0.25f, 0.27f));
            EditorGUI.DrawRect(
                new Rect(headerRect.x, headerRect.yMax - 4f, headerRect.width, 4f),
                new Color(0.95f, 0.72f, 0.14f));
            GUI.Label(
                new Rect(headerRect.x + 24f, headerRect.y + 16f, headerRect.width - 48f, 32f),
                "LOOTUP LEVEL BALANCE SIMULATOR",
                headerTitleStyle);
            GUI.Label(
                new Rect(headerRect.x + 24f, headerRect.y + 51f, headerRect.width - 48f, 22f),
                "실제 난도·캐릭터·아이템 데이터 + 명시된 플레이 숙련도 가정",
                headerSubtitleStyle);
        }

        private void DrawConfiguration()
        {
            EditorGUILayout.Space(10f);
            BeginPanel("입력 데이터", "에셋 변경 사항은 다음 실행부터 즉시 반영됩니다.");
            EditorGUI.BeginChangeCheck();
            difficultyBands = (DifficultyBandDefinition)EditorGUILayout.ObjectField(
                "난도 밴드",
                difficultyBands,
                typeof(DifficultyBandDefinition),
                false);
            itemTableCsv = (TextAsset)EditorGUILayout.ObjectField(
                "아이템 테이블",
                itemTableCsv,
                typeof(TextAsset),
                false);
            RefreshCharacterListIfNeeded();
            int characterIndex = Mathf.Max(0, characters.IndexOf(selectedCharacter));
            string[] characterLabels = characters.Count > 0
                ? characters.Select(character =>
                    $"{character.DisplayName} ({character.CharacterId})").ToArray()
                : new[] { "캐릭터 없음" };
            int nextCharacterIndex = EditorGUILayout.Popup(
                "기준 캐릭터",
                characterIndex,
                characterLabels);
            if (characters.Count > 0)
            {
                selectedCharacter = characters[Mathf.Clamp(
                    nextCharacterIndex,
                    0,
                    characters.Count - 1)];
            }

            skillProfile = (SimulationSkillProfile)EditorGUILayout.EnumPopup(
                "플레이 숙련도",
                skillProfile);
            characterLevel = EditorGUILayout.IntSlider(
                "캐릭터 레벨",
                characterLevel,
                1,
                selectedCharacter != null ? selectedCharacter.MaxCharacterLevel : 99);
            iterationIndex = EditorGUILayout.Popup(
                "반복 횟수",
                Mathf.Clamp(iterationIndex, 0, IterationOptions.Length - 1),
                IterationLabels);
            maximumFloor = EditorGUILayout.IntSlider(
                "최대 시뮬레이션 층",
                maximumFloor,
                50,
                300);
            seed = EditorGUILayout.IntField("Seed", seed);
            if (EditorGUI.EndChangeCheck())
            {
                statusMessage = "입력값이 변경되었습니다. 다시 실행하면 결과에 반영됩니다.";
            }

            showAdvancedSettings = EditorGUILayout.Foldout(
                showAdvancedSettings,
                "모델 가정값",
                true);
            if (showAdvancedSettings)
            {
                EditorGUI.indentLevel++;
                initialTimeSeconds = EditorGUILayout.FloatField(
                    "초기 시간(초)",
                    initialTimeSeconds);
                columnsPerFloor = EditorGUILayout.FloatField(
                    "층당 이동 칸",
                    columnsPerFloor);
                elevatorSecondsPerFloor = EditorGUILayout.FloatField(
                    "층당 엘리베이터 시간",
                    elevatorSecondsPerFloor);
                itemsPerFloor = EditorGUILayout.Slider(
                    "층당 평균 아이템",
                    itemsPerFloor,
                    0f,
                    2f);
                baseEnemyMinSpeed = EditorGUILayout.FloatField(
                    "Enemy 최소 속도",
                    baseEnemyMinSpeed);
                baseEnemyMaxSpeed = EditorGUILayout.FloatField(
                    "Enemy 최대 속도",
                    baseEnemyMaxSpeed);
                speedStepPerEnemy = EditorGUILayout.FloatField(
                    "Enemy 순번별 속도 증가",
                    speedStepPerEnemy);
                risingAdvanceFloors = EditorGUILayout.IntSlider(
                    "RISING 이동 층",
                    risingAdvanceFloors,
                    1,
                    30);
                EditorGUI.indentLevel--;
            }

            EndPanel();
        }

        private void DrawActionBar()
        {
            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = CanRun();
                Color previous = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.2f, 0.68f, 0.48f);
                if (GUILayout.Button("선택 캐릭터 실행", GUILayout.Height(34f)))
                {
                    RunSelectedCharacter();
                }

                GUI.backgroundColor = new Color(0.23f, 0.5f, 0.82f);
                if (GUILayout.Button("전체 캐릭터 비교", GUILayout.Height(34f)))
                {
                    RunAllCharacters();
                }

                GUI.enabled = results.Count > 0;
                GUI.backgroundColor = new Color(0.92f, 0.67f, 0.18f);
                if (GUILayout.Button("Codex 분석 리포트 내보내기", GUILayout.Height(34f)))
                {
                    ExportReport();
                }

                GUI.backgroundColor = previous;
                GUI.enabled = true;
            }
        }

        private void DrawStatus()
        {
            MessageType type = results.Count > 0
                ? MessageType.None
                : MessageType.Info;
            EditorGUILayout.HelpBox(statusMessage, type);
            if (!string.IsNullOrWhiteSpace(lastExportPath))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel("최근 리포트");
                    EditorGUILayout.SelectableLabel(
                        lastExportPath,
                        EditorStyles.textField,
                        GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    if (GUILayout.Button("열기", GUILayout.Width(54f)))
                    {
                        EditorUtility.RevealInFinder(lastExportPath);
                    }
                }
            }

            EditorGUILayout.Space(8f);
        }

        private void DrawDashboard()
        {
            LevelBalanceSimulationResult primary = results[0];
            DrawSectionTitle("핵심 결과");
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawMetricCard(
                    "P50 최고 층",
                    $"{primary.MedianHighestFloor}F",
                    $"P10 {primary.P10HighestFloor}F · P90 {primary.P90HighestFloor}F",
                    new Color(0.16f, 0.47f, 0.82f));
                DrawMetricCard(
                    "HP 종료",
                    $"{primary.LifeDepletedPercent:0.0}%",
                    $"평균 피격 {primary.AverageHits:0.00}회",
                    new Color(0.82f, 0.28f, 0.24f));
                DrawMetricCard(
                    "시간 종료",
                    $"{primary.TimeOverPercent:0.0}%",
                    $"완주 {primary.CompletedPercent:0.0}% · 초기 {initialTimeSeconds:0.#}초",
                    new Color(0.9f, 0.62f, 0.12f));
                DrawMetricCard(
                    "런 보상",
                    $"{primary.AverageGameMoney:0.00}",
                    $"지원 {primary.AverageSupportItems:0.00} · RISING {primary.AverageRisingActivations:0.00}",
                    new Color(0.15f, 0.63f, 0.46f));
            }

            EditorGUILayout.Space(12f);
            DrawSectionTitle("난도 압박과 조건부 통과율");
            Rect difficultyRect = GUILayoutUtility.GetRect(
                100f,
                310f,
                GUILayout.ExpandWidth(true));
            DrawBandChart(difficultyRect, primary);

            if (results.Count > 1)
            {
                EditorGUILayout.Space(12f);
                DrawSectionTitle("캐릭터별 구간 진입률");
                Rect comparisonRect = GUILayoutUtility.GetRect(
                    100f,
                    310f,
                    GUILayout.ExpandWidth(true));
                DrawCharacterSurvivalChart(comparisonRect);
            }
        }

        private void DrawBandAnalysis()
        {
            LevelBalanceSimulationResult result = results[0];
            DrawSectionTitle(
                $"{result.CharacterName} Lv.{result.CharacterLevel} · {result.SkillProfile}");
            DrawBandTableHeader();
            for (int i = 0; i < result.Bands.Count; i++)
            {
                DrawBandRow(result.Bands[i], i);
            }
        }

        private void DrawCharacterComparison()
        {
            DrawSectionTitle("캐릭터 비교 요약");
            if (results.Count <= 1)
            {
                EditorGUILayout.HelpBox(
                    "전체 캐릭터 비교 버튼을 실행하면 동일 Seed와 숙련도 조건에서 캐릭터 편차를 확인할 수 있습니다.",
                    MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                DrawTableLabel("캐릭터", 150f, true);
                DrawTableLabel("P10", 55f, true);
                DrawTableLabel("P50", 55f, true);
                DrawTableLabel("P90", 55f, true);
                DrawTableLabel("평균 층", 75f, true);
                DrawTableLabel("HP 종료", 80f, true);
                DrawTableLabel("시간 종료", 80f, true);
                DrawTableLabel("지원", 70f, true);
                DrawTableLabel("머니", 70f, true);
                DrawTableLabel("RISING", 70f, true);
            }

            for (int i = 0; i < results.Count; i++)
            {
                LevelBalanceSimulationResult result = results[i];
                Rect rowRect = GUILayoutUtility.GetRect(0f, 28f, GUILayout.ExpandWidth(true));
                if (i % 2 == 0)
                {
                    EditorGUI.DrawRect(
                        rowRect,
                        EditorGUIUtility.isProSkin
                            ? new Color(1f, 1f, 1f, 0.035f)
                            : new Color(0f, 0f, 0f, 0.035f));
                }

                GUILayout.BeginArea(rowRect);
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawTableLabel(result.CharacterName, 150f);
                    DrawTableLabel($"{result.P10HighestFloor}F", 55f);
                    DrawTableLabel($"{result.MedianHighestFloor}F", 55f);
                    DrawTableLabel($"{result.P90HighestFloor}F", 55f);
                    DrawTableLabel($"{result.AverageHighestFloor:0.0}", 75f);
                    DrawTableLabel($"{result.LifeDepletedPercent:0.0}%", 80f);
                    DrawTableLabel($"{result.TimeOverPercent:0.0}%", 80f);
                    DrawTableLabel($"{result.AverageSupportItems:0.00}", 70f);
                    DrawTableLabel($"{result.AverageGameMoney:0.00}", 70f);
                    DrawTableLabel($"{result.AverageRisingActivations:0.00}", 70f);
                }
                GUILayout.EndArea();
            }

            EditorGUILayout.Space(12f);
            Rect chartRect = GUILayoutUtility.GetRect(
                100f,
                330f,
                GUILayout.ExpandWidth(true));
            DrawCharacterSurvivalChart(chartRect);
        }

        private void DrawAssumptionsAndWarnings()
        {
            DrawSectionTitle("모델 가정");
            EditorGUILayout.HelpBox(
                "난도 밴드, 캐릭터 스탯과 아이템 가중치는 실제 프로젝트 데이터입니다. 회피율, 아이템 획득률, 층 이동시간은 플레이 숙련도를 수치화한 추정값이며 실제 런으로 보정해야 합니다.",
                MessageType.Info);
            SimulationProfileValues profile = ResolveDisplayedProfile();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawKeyValue("숙련도", skillProfile.ToString());
                DrawKeyValue("위험 노출 배율", profile.HazardMultiplier.ToString("0.00"));
                DrawKeyValue("아이템 획득률", $"{profile.ItemPickupChance * 100f:0}%");
                DrawKeyValue("RISING 획득률", $"{profile.RisingPickupChance * 100f:0}%");
                DrawKeyValue("층 이동시간 배율", profile.FloorTimeMultiplier.ToString("0.00"));
                DrawKeyValue("반복·Seed", $"{IterationOptions[Mathf.Clamp(iterationIndex, 0, IterationOptions.Length - 1)]:N0}회 · {seed}");
            }

            EditorGUILayout.Space(10f);
            DrawSectionTitle("자동 경고");
            bool hasWarning = false;
            for (int i = 0; i < results.Count; i++)
            {
                LevelBalanceSimulationResult result = results[i];
                for (int j = 0; j < result.Warnings.Count; j++)
                {
                    hasWarning = true;
                    EditorGUILayout.HelpBox(
                        $"[{result.CharacterName}] {result.Warnings[j]}",
                        MessageType.Warning);
                }
            }

            if (!hasWarning)
            {
                EditorGUILayout.HelpBox(
                    "현재 자동 판정 기준에서 경고가 없습니다. 실제 플레이 체감 검증은 별도로 필요합니다.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(10f);
            DrawSectionTitle("Codex 분석 자료");
            EditorGUILayout.LabelField("configuration.json", "입력 에셋과 모델 가정", mutedStyle);
            EditorGUILayout.LabelField("run-results.csv", "개별 가상 런 결과", mutedStyle);
            EditorGUILayout.LabelField("band-summary.csv", "10층 구간별 난도·통과율", mutedStyle);
            EditorGUILayout.LabelField("summary.md", "요약·자동 경고·분석 지침", mutedStyle);
            EditorGUILayout.LabelField("survival-curve.svg", "캐릭터별 구간 진입률 그래프", mutedStyle);
        }

        private void DrawBandChart(Rect rect, LevelBalanceSimulationResult result)
        {
            DrawChartBackground(rect, "압박 지수", "조건부 통과율");
            if (Event.current.type != EventType.Repaint || result.Bands.Count == 0)
            {
                return;
            }

            Rect plot = new Rect(rect.x + 58f, rect.y + 38f, rect.width - 82f, rect.height - 78f);
            Vector3[] pressurePoints = BuildBandSeries(
                plot,
                result.Bands,
                band => band.PressureIndex);
            Vector3[] passPoints = BuildBandSeries(
                plot,
                result.Bands,
                band => band.ConditionalPassRatePercent);
            Handles.BeginGUI();
            Handles.color = new Color(0.86f, 0.32f, 0.25f);
            Handles.DrawAAPolyLine(3f, pressurePoints);
            Handles.color = new Color(0.15f, 0.65f, 0.46f);
            Handles.DrawAAPolyLine(3f, passPoints);
            Handles.EndGUI();
            DrawLegend(rect, new[]
            {
                ("압박 지수", new Color(0.86f, 0.32f, 0.25f)),
                ("조건부 통과율", new Color(0.15f, 0.65f, 0.46f))
            });
        }

        private void DrawCharacterSurvivalChart(Rect rect)
        {
            DrawChartBackground(rect, "구간 진입률", string.Empty);
            if (Event.current.type != EventType.Repaint || results.Count == 0)
            {
                return;
            }

            Color[] colors =
            {
                new Color(0.16f, 0.47f, 0.82f),
                new Color(0.15f, 0.65f, 0.46f),
                new Color(0.9f, 0.62f, 0.12f),
                new Color(0.79f, 0.33f, 0.24f)
            };
            Rect plot = new Rect(rect.x + 58f, rect.y + 38f, rect.width - 82f, rect.height - 78f);
            List<(string, Color)> legends = new List<(string, Color)>();
            Handles.BeginGUI();
            for (int i = 0; i < results.Count; i++)
            {
                LevelBalanceSimulationResult result = results[i];
                Handles.color = colors[i % colors.Length];
                Handles.DrawAAPolyLine(
                    3f,
                    BuildBandSeries(
                        plot,
                        result.Bands,
                        band => band.EnterRatePercent));
                legends.Add(($"{result.CharacterName} · P50 {result.MedianHighestFloor}F", Handles.color));
            }
            Handles.EndGUI();
            DrawLegend(rect, legends.ToArray());
        }

        private static void DrawChartBackground(
            Rect rect,
            string primaryLabel,
            string secondaryLabel)
        {
            EditorGUI.DrawRect(
                rect,
                EditorGUIUtility.isProSkin
                    ? new Color(0.13f, 0.14f, 0.16f)
                    : Color.white);
            GUI.Box(rect, GUIContent.none);
            Rect plot = new Rect(rect.x + 58f, rect.y + 38f, rect.width - 82f, rect.height - 78f);
            for (int i = 0; i <= 4; i++)
            {
                float y = plot.yMax - plot.height * i / 4f;
                EditorGUI.DrawRect(
                    new Rect(plot.x, y, plot.width, 1f),
                    EditorGUIUtility.isProSkin
                        ? new Color(1f, 1f, 1f, 0.08f)
                        : new Color(0f, 0f, 0f, 0.09f));
                GUI.Label(
                    new Rect(rect.x + 8f, y - 9f, 44f, 18f),
                    $"{i * 25}%",
                    EditorStyles.miniLabel);
            }

            GUI.Label(
                new Rect(rect.x + 12f, rect.y + 9f, 180f, 20f),
                string.IsNullOrWhiteSpace(secondaryLabel)
                    ? primaryLabel
                    : $"{primaryLabel} / {secondaryLabel}",
                EditorStyles.boldLabel);
        }

        private static Vector3[] BuildBandSeries(
            Rect plot,
            IReadOnlyList<LevelBalanceBandResult> bands,
            Func<LevelBalanceBandResult, float> selector)
        {
            Vector3[] points = new Vector3[bands.Count];
            for (int i = 0; i < bands.Count; i++)
            {
                float x = bands.Count <= 1
                    ? plot.center.x
                    : Mathf.Lerp(plot.x, plot.xMax, i / (float)(bands.Count - 1));
                float y = Mathf.Lerp(
                    plot.yMax,
                    plot.y,
                    Mathf.Clamp01(selector(bands[i]) / 100f));
                points[i] = new Vector3(x, y);
                GUI.Label(
                    new Rect(x - 20f, plot.yMax + 8f, 45f, 18f),
                    $"{bands[i].StartFloor}F",
                    EditorStyles.miniLabel);
            }

            return points;
        }

        private static void DrawLegend(
            Rect rect,
            IReadOnlyList<(string Label, Color Color)> entries)
        {
            float x = rect.x + 210f;
            for (int i = 0; i < entries.Count; i++)
            {
                EditorGUI.DrawRect(
                    new Rect(x, rect.y + 14f, 22f, 3f),
                    entries[i].Color);
                GUI.Label(
                    new Rect(x + 30f, rect.y + 5f, 180f, 20f),
                    entries[i].Label,
                    EditorStyles.miniLabel);
                x += 205f;
            }
        }

        private void DrawBandTableHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                DrawTableLabel("층", 70f, true);
                DrawTableLabel("역할", 100f, true);
                DrawTableLabel("Enemy", 62f, true);
                DrawTableLabel("Red", 54f, true);
                DrawTableLabel("속도", 62f, true);
                DrawTableLabel("압박", 68f, true);
                DrawTableLabel("진입률", 72f, true);
                DrawTableLabel("통과율", 72f, true);
                DrawTableLabel("지원 배율", 78f, true);
                DrawTableLabel("지원 비중", 78f, true);
            }
        }

        private void DrawBandRow(LevelBalanceBandResult band, int index)
        {
            Rect rowRect = GUILayoutUtility.GetRect(0f, 28f, GUILayout.ExpandWidth(true));
            bool recovery = band.Role.IndexOf("회복", StringComparison.OrdinalIgnoreCase) >= 0;
            if (recovery)
            {
                EditorGUI.DrawRect(rowRect, new Color(0.15f, 0.65f, 0.46f, 0.12f));
            }
            else if (index % 2 == 0)
            {
                EditorGUI.DrawRect(
                    rowRect,
                    EditorGUIUtility.isProSkin
                        ? new Color(1f, 1f, 1f, 0.035f)
                        : new Color(0f, 0f, 0f, 0.035f));
            }

            GUILayout.BeginArea(rowRect);
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawTableLabel($"{band.StartFloor}~{band.EndFloor}", 70f);
                DrawTableLabel(band.Role, 100f);
                DrawTableLabel(band.EnemyCount.ToString(), 62f);
                DrawTableLabel(band.DangerousLineCount.ToString(), 54f);
                DrawTableLabel($"+{band.EnemySpeedBonus:0}", 62f);
                DrawTableLabel($"{band.PressureIndex:0.0}", 68f);
                DrawTableLabel($"{band.EnterRatePercent:0.0}%", 72f);
                DrawTableLabel($"{band.ConditionalPassRatePercent:0.0}%", 72f);
                DrawTableLabel($"x{band.SupportItemWeightMultiplier:0.00}", 78f);
                DrawTableLabel($"{band.SupportItemSharePercent:0.0}%", 78f);
            }
            GUILayout.EndArea();
        }

        private void RunSelectedCharacter()
        {
            results.Clear();
            try
            {
                LevelBalanceSimulationSettings settings = BuildSettings(selectedCharacter);
                LevelBalanceSimulationResult result =
                    new LevelBalanceSimulationEngine().Run(settings);
                results.Add(result);
                statusMessage =
                    $"{result.CharacterName} {result.Iterations:N0}회 완료 · P50 {result.MedianHighestFloor}F · 경고 {result.Warnings.Count}건";
                selectedTab = 0;
                Repaint();
            }
            catch (Exception exception)
            {
                statusMessage = $"실행 실패: {exception.Message}";
                Debug.LogException(exception);
            }
        }

        private void RunAllCharacters()
        {
            results.Clear();
            try
            {
                LevelBalanceSimulationEngine engine = new LevelBalanceSimulationEngine();
                for (int i = 0; i < characters.Count; i++)
                {
                    bool canceled = EditorUtility.DisplayCancelableProgressBar(
                        "LootUp Balance Simulator",
                        $"{characters[i].DisplayName} 계산 중",
                        i / (float)Mathf.Max(1, characters.Count));
                    if (canceled)
                    {
                        statusMessage = "전체 비교가 취소되었습니다.";
                        break;
                    }

                    results.Add(engine.Run(BuildSettings(characters[i])));
                }

                if (results.Count > 0)
                {
                    float spread = results.Max(result => result.MedianHighestFloor)
                        - results.Min(result => result.MedianHighestFloor);
                    statusMessage =
                        $"캐릭터 {results.Count}종 비교 완료 · P50 최고 층 편차 {spread:0}F";
                    selectedTab = 2;
                }

                Repaint();
            }
            catch (Exception exception)
            {
                statusMessage = $"실행 실패: {exception.Message}";
                Debug.LogException(exception);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void ExportReport()
        {
            try
            {
                lastExportPath = LevelBalanceReportExporter.Export(
                    BuildSettings(selectedCharacter),
                    results);
                statusMessage =
                    "Codex 분석용 CSV·JSON·Markdown·SVG 리포트를 생성했습니다.";
                Repaint();
            }
            catch (Exception exception)
            {
                statusMessage = $"내보내기 실패: {exception.Message}";
                Debug.LogException(exception);
            }
        }

        private LevelBalanceSimulationSettings BuildSettings(
            CharacterDefinition character)
        {
            return new LevelBalanceSimulationSettings
            {
                DifficultyBands = difficultyBands,
                ItemTableCsv = itemTableCsv,
                Character = character,
                SkillProfile = skillProfile,
                CharacterLevel = characterLevel,
                Iterations = IterationOptions[Mathf.Clamp(
                    iterationIndex,
                    0,
                    IterationOptions.Length - 1)],
                MaximumFloor = maximumFloor,
                Seed = seed,
                InitialTimeSeconds = Mathf.Max(1f, initialTimeSeconds),
                ColumnsPerFloor = Mathf.Max(1f, columnsPerFloor),
                ElevatorSecondsPerFloor = Mathf.Max(0f, elevatorSecondsPerFloor),
                ItemsPerFloor = Mathf.Max(0f, itemsPerFloor),
                BaseEnemyMinSpeed = Mathf.Max(0f, baseEnemyMinSpeed),
                BaseEnemyMaxSpeed = Mathf.Max(baseEnemyMinSpeed, baseEnemyMaxSpeed),
                SpeedStepPerEnemy = Mathf.Max(0f, speedStepPerEnemy),
                RisingAdvanceFloors = Mathf.Max(1, risingAdvanceFloors)
            };
        }

        private void AutoLoadProjectData()
        {
            if (difficultyBands == null)
            {
                difficultyBands = LoadFirstAsset<DifficultyBandDefinition>(
                    "t:DifficultyBandDefinition");
            }

            if (itemTableCsv == null)
            {
                itemTableCsv = AssetDatabase.LoadAssetAtPath<TextAsset>(
                    "Assets/_Project/Data/Tables/Items.csv");
            }

            RefreshCharacterList();
            if (selectedCharacter == null && characters.Count > 0)
            {
                selectedCharacter = characters[0];
            }
        }

        private void RefreshCharacterListIfNeeded()
        {
            if (characters.Count == 0 || characters.Any(character => character == null))
            {
                RefreshCharacterList();
            }
        }

        private void RefreshCharacterList()
        {
            characters.Clear();
            string[] guids = AssetDatabase.FindAssets("t:CharacterDefinition");
            for (int i = 0; i < guids.Length; i++)
            {
                CharacterDefinition character = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (character != null)
                {
                    characters.Add(character);
                }
            }

            characters.Sort((left, right) => string.Compare(
                left.CharacterId,
                right.CharacterId,
                StringComparison.OrdinalIgnoreCase));
        }

        private static T LoadFirstAsset<T>(string filter) where T : UnityEngine.Object
        {
            string guid = AssetDatabase.FindAssets(filter).FirstOrDefault();
            return string.IsNullOrWhiteSpace(guid)
                ? null
                : AssetDatabase.LoadAssetAtPath<T>(
                    AssetDatabase.GUIDToAssetPath(guid));
        }

        private bool CanRun()
        {
            return difficultyBands != null
                && itemTableCsv != null
                && selectedCharacter != null;
        }

        private void BeginPanel(string title, string description)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(title, sectionTitleStyle);
            EditorGUILayout.LabelField(description, mutedStyle);
            EditorGUILayout.Space(4f);
        }

        private static void EndPanel()
        {
            EditorGUILayout.EndVertical();
        }

        private void DrawSectionTitle(string title)
        {
            EditorGUILayout.LabelField(title, sectionTitleStyle);
            Rect line = GUILayoutUtility.GetRect(0f, 2f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(
                line,
                EditorGUIUtility.isProSkin
                    ? new Color(1f, 1f, 1f, 0.1f)
                    : new Color(0f, 0f, 0f, 0.1f));
            EditorGUILayout.Space(5f);
        }

        private void DrawMetricCard(
            string label,
            string value,
            string detail,
            Color accent)
        {
            using (new EditorGUILayout.VerticalScope(
                EditorStyles.helpBox,
                GUILayout.Height(88f),
                GUILayout.MinWidth(180f)))
            {
                Rect accentRect = GUILayoutUtility.GetRect(0f, 3f, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(accentRect, accent);
                EditorGUILayout.LabelField(label, cardLabelStyle);
                EditorGUILayout.LabelField(value, cardValueStyle);
                EditorGUILayout.LabelField(detail, mutedStyle);
            }
        }

        private static void DrawTableLabel(
            string value,
            float width,
            bool isHeader = false)
        {
            GUILayout.Label(
                value,
                isHeader ? EditorStyles.miniBoldLabel : EditorStyles.miniLabel,
                GUILayout.Width(width));
        }

        private static void DrawKeyValue(string key, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(key, GUILayout.Width(180f));
                EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
            }
        }

        private SimulationProfileValues ResolveDisplayedProfile()
        {
            switch (skillProfile)
            {
                case SimulationSkillProfile.Beginner:
                    return new SimulationProfileValues(1.22f, 0.58f, 0.62f, 1.18f);
                case SimulationSkillProfile.Expert:
                    return new SimulationProfileValues(0.68f, 0.9f, 0.92f, 0.86f);
                default:
                    return new SimulationProfileValues(0.92f, 0.76f, 0.8f, 1f);
            }
        }

        private void EnsureStyles()
        {
            if (headerTitleStyle != null)
            {
                return;
            }

            headerTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 23,
                normal = { textColor = Color.white }
            };
            headerSubtitleStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.84f, 0.9f, 0.9f) }
            };
            cardValueStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 24,
                alignment = TextAnchor.MiddleLeft
            };
            cardLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 11
            };
            sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15
            };
            mutedStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(0.68f, 0.72f, 0.75f)
                        : new Color(0.34f, 0.38f, 0.42f)
                }
            };
            tableHeaderStyle = new GUIStyle(EditorStyles.miniBoldLabel);
        }
    }
}
