using LootUp.Core.Player;
using LootUp.Core.Game;
using LootUp.Core.World;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LootUp.Core.Enemies
{
    public sealed class TestEnemySpawner : MonoBehaviour, IGameplayPausable
    {
        private const string EnemyLayerName = "EnemyLayer";
        private const string EnemyGuideLineLayerName = "EnemyGuideLineLayer";

        [SerializeField]
        private BuildingGridUI buildingGridUI;

        [SerializeField]
        private InfiniteFloorManager floorManager;

        [SerializeField]
        private PlayerSpawner playerSpawner;

        [SerializeField]
        private RectTransform enemyLayer;

        [SerializeField]
        private RectTransform guideLineLayer;

        [SerializeField]
        private RectTransform playerLayer;

        [SerializeField]
        private bool spawnOnStart = true;

        [Header("Difficulty Balance")]
        [SerializeField]
        [Tooltip("설정하면 10층 단위 난도 데이터를 사용하고, 비어 있으면 기존 증가 공식을 사용합니다.")]
        private DifficultyBandDefinition difficultyBands;

        [SerializeField]
        private int baseEnemiesPerPage = 4;

        [SerializeField]
        private int maxEnemiesPerPage = 7;

        [SerializeField]
        private int floorsPerEnemyIncrease = 10;

        [SerializeField]
        private int enemyLineRandomSeed = 81473;

        [SerializeField]
        private bool randomizeSeedOnStart = true;

        [SerializeField]
        private bool randomizeLinesPerPage = true;

        [SerializeField]
        private int damage = 1;

        [SerializeField]
        private float hitCooldownSeconds = 0.65f;

        [SerializeField]
        private float minVerticalSpeed = 130f;

        [SerializeField]
        private float maxVerticalSpeed = 360f;

        [SerializeField]
        private float speedStepPerLine = 22f;

        [SerializeField]
        private float speedStepPerDifficulty = 18f;

        [SerializeField]
        private float guideLineThickness = 4f;

        [SerializeField]
        private Color guideLineColor = new Color(1f, 1f, 1f, 0.68f);

        [Header("Dangerous Enemy Line Difficulty")]
        [SerializeField, Min(1)]
        [Tooltip("붉은 공격 라인이 처음 등장하는 Page 번호입니다. Page는 1부터 시작합니다.")]
        private int dangerousLineStartPage = 2;

        [SerializeField, Min(0)]
        [Tooltip("첫 등장 Page에 붉은 공격 라인이 적용되는 Enemy 수입니다.")]
        private int baseDangerousLineEnemiesPerPage = 1;

        [SerializeField, Min(0)]
        [Tooltip("공격 라인 수가 증가하는 Page 간격입니다. 0이면 자동 증가하지 않습니다.")]
        private int pagesPerDangerousLineIncrease = 2;

        [SerializeField, Min(0)]
        [Tooltip("증가 주기마다 추가되는 공격 라인 Enemy 수입니다.")]
        private int dangerousLineIncreasePerStep = 1;

        [SerializeField, Min(0)]
        [Tooltip("한 Page에서 공격 라인을 가질 수 있는 최대 Enemy 수입니다.")]
        private int maxDangerousLineEnemiesPerPage = 4;

        [SerializeField]
        private Color dangerousGuideLineColor = new Color(1f, 0.08f, 0.05f, 0.92f);

        [SerializeField, Min(0f)]
        private float dangerousLineCollisionPadding = 2f;

        [SerializeField]
        private Vector2 enemySize = new Vector2(48f, 76f);

        [SerializeField]
        private Sprite enemySprite;

        [SerializeField]
        private Color enemySpriteTint = Color.white;

        [SerializeField]
        private bool preserveEnemySpriteAspect = true;

        [SerializeField]
        private Vector2 enemyVisualScale = new Vector2(1.45f, 1.45f);

        [SerializeField]
        private Vector2 enemyVisualOffset;

        [SerializeField]
        private Color enemyColor = new Color(1f, 0.12f, 0.1f, 0.94f);

        [SerializeField]
        private Color enemyOutlineColor = new Color(1f, 1f, 1f, 0.52f);

        private readonly System.Collections.Generic.List<TestEnemyHazard> spawnedEnemies = new System.Collections.Generic.List<TestEnemyHazard>();
        private readonly List<int> availableLineIndices = new List<int>();
        private readonly HashSet<int> dangerousLineIndices = new HashSet<int>();
        private int lastPageIndex = -1;
        private int runtimeEnemyLineRandomSeed;
        private bool hasRuntimeEnemyLineRandomSeed;

        private void Awake()
        {
            EnsureReferences();
        }

        private void Start()
        {
            if (floorManager != null)
            {
                lastPageIndex = floorManager.CurrentPageIndex;
            }

            InitializeRuntimeSeed();

            if (spawnOnStart)
            {
                SpawnTestEnemy();
            }
        }

        private void OnEnable()
        {
            EnsureReferences();

            if (floorManager != null)
            {
                floorManager.CurrentFloorChanged += HandleCurrentFloorChanged;
            }
        }

        private void OnDisable()
        {
            if (floorManager != null)
            {
                floorManager.CurrentFloorChanged -= HandleCurrentFloorChanged;
            }
        }

        [ContextMenu("Debug/Spawn Test Enemy")]
        public void SpawnTestEnemy()
        {
            EnsureReferences();

            if (enemyLayer == null || buildingGridUI == null)
            {
                Debug.LogWarning("TestEnemySpawner 참조가 부족해 테스트 Enemy를 생성할 수 없습니다.", this);
                return;
            }

            ClearSpawnedEnemies();
            lastPageIndex = floorManager != null ? floorManager.CurrentPageIndex : lastPageIndex;

            int currentFloor = floorManager != null
                ? floorManager.CurrentAbsoluteFloor
                : 1;
            int difficultyStep = GetDifficultyStep();
            int enemyCount = GetEnemyCountForDifficulty(difficultyStep);
            int pageNumber = floorManager != null ? floorManager.CurrentPageIndex + 1 : 1;
            int dangerousLineEnemyCount = GetDangerousLineEnemyCount(pageNumber, enemyCount);
            float difficultySpeedBonus = GetDifficultySpeedBonus(
                difficultyStep);
            if (difficultyBands != null
                && difficultyBands.TryGetValues(
                    currentFloor,
                    out DifficultyBandValues band))
            {
                int internalLineCount = GetInternalLineCount();
                enemyCount = Mathf.Clamp(
                    band.EnemyCount,
                    1,
                    internalLineCount);
                dangerousLineEnemyCount = Mathf.Clamp(
                    band.DangerousLineCount,
                    0,
                    enemyCount);
                difficultySpeedBonus = band.EnemySpeedBonus;
            }

            BuildLineSelection(enemyCount, dangerousLineEnemyCount);

            for (int i = 0; i < availableLineIndices.Count; i++)
            {
                int lineIndex = availableLineIndices[i];
                SpawnEnemyAtLine(
                    lineIndex,
                    i,
                    difficultySpeedBonus,
                    dangerousLineIndices.Contains(lineIndex));
            }
        }

        public void SetGameplayPaused(bool isPaused)
        {
            enabled = !isPaused;
        }

        private void SpawnEnemyAtLine(
            int lineIndex,
            int sequenceIndex,
            float difficultySpeedBonus,
            bool canCycleDangerousLine)
        {
            GameObject enemyObject = new GameObject($"TestEnemy_Line_{lineIndex}", typeof(RectTransform), typeof(TestEnemyHazard));
            enemyObject.layer = enemyLayer.gameObject.layer;
            enemyObject.transform.SetParent(enemyLayer, false);

            RectTransform enemyRect = enemyObject.GetComponent<RectTransform>();
            enemyRect.sizeDelta = enemySize;

            CreateEnemyVisual(enemyObject.transform, enemyObject.layer);

            float clampedDifficultySpeedBonus = Mathf.Max(
                0f,
                difficultySpeedBonus);
            float lineMinSpeed = Mathf.Max(
                0f,
                minVerticalSpeed
                + clampedDifficultySpeedBonus
                + speedStepPerLine * sequenceIndex);
            float lineMaxSpeed = Mathf.Max(
                lineMinSpeed,
                maxVerticalSpeed
                + clampedDifficultySpeedBonus
                + speedStepPerLine * sequenceIndex);
            TestEnemyHazard enemy = enemyObject.GetComponent<TestEnemyHazard>();
            enemy.Configure(
                buildingGridUI,
                playerSpawner,
                lineIndex,
                damage,
                hitCooldownSeconds,
                lineMinSpeed,
                lineMaxSpeed,
                guideLineThickness,
                guideLineColor,
                dangerousGuideLineColor,
                guideLineLayer,
                canCycleDangerousLine,
                dangerousLineCollisionPadding);
            spawnedEnemies.Add(enemy);
        }

        private void CreateEnemyVisual(Transform parent, int layer)
        {
            GameObject visualObject = new GameObject("Visual", typeof(RectTransform), typeof(Image), typeof(Outline));
            visualObject.layer = layer;
            visualObject.transform.SetParent(parent, false);

            RectTransform visualRect = visualObject.GetComponent<RectTransform>();
            visualRect.anchorMin = new Vector2(0.5f, 0.5f);
            visualRect.anchorMax = new Vector2(0.5f, 0.5f);
            visualRect.pivot = new Vector2(0.5f, 0.5f);
            visualRect.anchoredPosition = enemyVisualOffset;
            visualRect.sizeDelta = new Vector2(enemySize.x * Mathf.Max(0.01f, enemyVisualScale.x), enemySize.y * Mathf.Max(0.01f, enemyVisualScale.y));

            Image enemyImage = visualObject.GetComponent<Image>();
            enemyImage.sprite = enemySprite;
            enemyImage.color = enemySprite != null ? enemySpriteTint : enemyColor;
            enemyImage.preserveAspect = preserveEnemySpriteAspect;
            enemyImage.raycastTarget = false;

            Outline outline = visualObject.GetComponent<Outline>();
            outline.effectColor = enemyOutlineColor;
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = true;
        }

        private void ClearSpawnedEnemies()
        {
            for (int i = spawnedEnemies.Count - 1; i >= 0; i--)
            {
                TestEnemyHazard enemy = spawnedEnemies[i];
                if (enemy != null)
                {
                    Destroy(enemy.gameObject);
                }
            }

            spawnedEnemies.Clear();
        }

        private int GetDifficultyStep()
        {
            int currentFloor = floorManager != null ? floorManager.CurrentAbsoluteFloor : 1;
            int interval = Mathf.Max(1, floorsPerEnemyIncrease);

            return Mathf.Max(0, (Mathf.Max(1, currentFloor) - 1) / interval);
        }

        private int GetEnemyCountForDifficulty(int difficultyStep)
        {
            int internalLineCount = GetInternalLineCount();
            int minCount = Mathf.Clamp(baseEnemiesPerPage, 1, internalLineCount);
            int maxCount = Mathf.Clamp(maxEnemiesPerPage, minCount, internalLineCount);

            return Mathf.Clamp(minCount + Mathf.Max(0, difficultyStep), minCount, maxCount);
        }

        private float GetDifficultySpeedBonus(int difficultyStep)
        {
            return Mathf.Max(0f, speedStepPerDifficulty)
                * Mathf.Max(0, difficultyStep);
        }

        private int GetInternalLineCount()
        {
            int columns = buildingGridUI != null ? Mathf.Max(1, buildingGridUI.Columns) : BuildingGridUI.DefaultColumns;

            return Mathf.Max(1, columns - 1);
        }

        private int GetDangerousLineEnemyCount(int pageNumber, int enemyCount)
        {
            int currentPage = Mathf.Max(1, pageNumber);
            int startPage = Mathf.Max(1, dangerousLineStartPage);
            if (currentPage < startPage || enemyCount <= 0)
            {
                return 0;
            }

            int increaseStep = pagesPerDangerousLineIncrease > 0
                ? (currentPage - startPage) / pagesPerDangerousLineIncrease
                : 0;
            int requestedCount = Mathf.Max(0, baseDangerousLineEnemiesPerPage)
                + increaseStep * Mathf.Max(0, dangerousLineIncreasePerStep);
            int maxCount = Mathf.Clamp(maxDangerousLineEnemiesPerPage, 0, enemyCount);

            return Mathf.Clamp(requestedCount, 0, maxCount);
        }

        private void BuildLineSelection(int enemyCount, int dangerousLineEnemyCount)
        {
            availableLineIndices.Clear();
            dangerousLineIndices.Clear();

            int internalLineCount = GetInternalLineCount();
            for (int i = 0; i < internalLineCount; i++)
            {
                availableLineIndices.Add(i + 1);
            }

            if (randomizeLinesPerPage)
            {
                int pageIndex = floorManager != null ? floorManager.CurrentPageIndex : 0;
                System.Random random = new System.Random(unchecked(GetRuntimeEnemyLineRandomSeed() + pageIndex * 73856093));

                for (int i = availableLineIndices.Count - 1; i > 0; i--)
                {
                    int swapIndex = random.Next(0, i + 1);
                    int temp = availableLineIndices[i];
                    availableLineIndices[i] = availableLineIndices[swapIndex];
                    availableLineIndices[swapIndex] = temp;
                }
            }

            int clampedEnemyCount = Mathf.Clamp(enemyCount, 1, availableLineIndices.Count);
            if (availableLineIndices.Count > clampedEnemyCount)
            {
                availableLineIndices.RemoveRange(clampedEnemyCount, availableLineIndices.Count - clampedEnemyCount);
            }

            int clampedDangerousCount = Mathf.Clamp(dangerousLineEnemyCount, 0, availableLineIndices.Count);
            for (int i = 0; i < clampedDangerousCount; i++)
            {
                dangerousLineIndices.Add(availableLineIndices[i]);
            }

            availableLineIndices.Sort();
        }

        private int GetRuntimeEnemyLineRandomSeed()
        {
            if (!hasRuntimeEnemyLineRandomSeed)
            {
                InitializeRuntimeSeed();
            }

            return runtimeEnemyLineRandomSeed;
        }

        private void InitializeRuntimeSeed()
        {
            runtimeEnemyLineRandomSeed = randomizeSeedOnStart
                ? unchecked(enemyLineRandomSeed ^ Random.Range(int.MinValue, int.MaxValue) ^ GetInstanceID() ^ System.Environment.TickCount)
                : enemyLineRandomSeed;
            hasRuntimeEnemyLineRandomSeed = true;
        }

        private void HandleCurrentFloorChanged(int currentAbsoluteFloor)
        {
            if (!spawnOnStart || floorManager == null)
            {
                return;
            }

            int currentPageIndex = floorManager.CurrentPageIndex;
            if (lastPageIndex < 0)
            {
                lastPageIndex = currentPageIndex;
                return;
            }

            if (currentPageIndex == lastPageIndex)
            {
                return;
            }

            SpawnTestEnemy();
        }

        private void EnsureReferences()
        {
            if (buildingGridUI == null)
            {
                buildingGridUI = FindFirstObjectByType<BuildingGridUI>();
            }

            if (floorManager == null)
            {
                floorManager = FindFirstObjectByType<InfiniteFloorManager>();
            }

            if (playerSpawner == null)
            {
                playerSpawner = FindFirstObjectByType<PlayerSpawner>();
            }

            if (enemyLayer == null)
            {
                enemyLayer = FindEnemyLayer();
            }

            if (guideLineLayer == null)
            {
                guideLineLayer = FindGuideLineLayer();
            }

            if (playerLayer == null)
            {
                playerLayer = FindLayer("PlayerLayer");
            }

            ApplyForegroundLayerOrder();
        }

        private RectTransform FindEnemyLayer()
        {
            Transform existing = GameObject.Find(EnemyLayerName)?.transform;
            if (existing is RectTransform existingRect)
            {
                return existingRect;
            }

            RectTransform parent = ResolveMiddleUIRoot();
            if (parent == null)
            {
                return null;
            }

            GameObject layerObject = new GameObject(EnemyLayerName, typeof(RectTransform));
            layerObject.layer = parent.gameObject.layer;
            layerObject.transform.SetParent(parent, false);

            RectTransform layerRect = layerObject.GetComponent<RectTransform>();
            layerRect.anchorMin = Vector2.zero;
            layerRect.anchorMax = Vector2.one;
            layerRect.offsetMin = Vector2.zero;
            layerRect.offsetMax = Vector2.zero;
            layerRect.pivot = new Vector2(0.5f, 0.5f);

            return layerRect;
        }

        private RectTransform FindGuideLineLayer()
        {
            Transform existing = GameObject.Find(EnemyGuideLineLayerName)?.transform;
            if (existing is RectTransform existingRect)
            {
                return existingRect;
            }

            RectTransform parent = ResolveMiddleUIRoot();
            if (parent == null)
            {
                return null;
            }

            GameObject layerObject = new GameObject(EnemyGuideLineLayerName, typeof(RectTransform));
            layerObject.layer = parent.gameObject.layer;
            layerObject.transform.SetParent(parent, false);

            RectTransform layerRect = layerObject.GetComponent<RectTransform>();
            layerRect.anchorMin = Vector2.zero;
            layerRect.anchorMax = Vector2.one;
            layerRect.offsetMin = Vector2.zero;
            layerRect.offsetMax = Vector2.zero;
            layerRect.pivot = new Vector2(0.5f, 0.5f);

            return layerRect;
        }

        private RectTransform FindLayer(string layerName)
        {
            Transform existing = GameObject.Find(layerName)?.transform;
            return existing as RectTransform;
        }

        private void ApplyForegroundLayerOrder()
        {
            guideLineLayer?.SetAsLastSibling();
            enemyLayer?.SetAsLastSibling();
            playerLayer?.SetAsLastSibling();
        }

        private RectTransform ResolveMiddleUIRoot()
        {
            if (buildingGridUI != null && buildingGridUI.transform.parent is RectTransform gridParent)
            {
                return gridParent;
            }

            GameObject middleUI = GameObject.Find("MiddleUI");
            return middleUI != null ? middleUI.transform as RectTransform : null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            baseEnemiesPerPage = Mathf.Max(1, baseEnemiesPerPage);
            maxEnemiesPerPage = Mathf.Max(baseEnemiesPerPage, maxEnemiesPerPage);
            floorsPerEnemyIncrease = Mathf.Max(1, floorsPerEnemyIncrease);
            damage = Mathf.Max(1, damage);
            hitCooldownSeconds = Mathf.Max(0f, hitCooldownSeconds);
            minVerticalSpeed = Mathf.Max(0f, minVerticalSpeed);
            maxVerticalSpeed = Mathf.Max(minVerticalSpeed, maxVerticalSpeed);
            speedStepPerLine = Mathf.Max(0f, speedStepPerLine);
            speedStepPerDifficulty = Mathf.Max(0f, speedStepPerDifficulty);
            guideLineThickness = Mathf.Max(1f, guideLineThickness);
            dangerousLineStartPage = Mathf.Max(1, dangerousLineStartPage);
            baseDangerousLineEnemiesPerPage = Mathf.Max(0, baseDangerousLineEnemiesPerPage);
            pagesPerDangerousLineIncrease = Mathf.Max(0, pagesPerDangerousLineIncrease);
            dangerousLineIncreasePerStep = Mathf.Max(0, dangerousLineIncreasePerStep);
            maxDangerousLineEnemiesPerPage = Mathf.Max(0, maxDangerousLineEnemiesPerPage);
            dangerousLineCollisionPadding = Mathf.Max(0f, dangerousLineCollisionPadding);
            enemySize.x = Mathf.Max(1f, enemySize.x);
            enemySize.y = Mathf.Max(1f, enemySize.y);
            enemyVisualScale.x = Mathf.Max(0.01f, enemyVisualScale.x);
            enemyVisualScale.y = Mathf.Max(0.01f, enemyVisualScale.y);
        }
#endif
    }
}
