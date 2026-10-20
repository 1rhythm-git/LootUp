using System.Collections.Generic;
using LootUp.Core.Enemies;
using LootUp.Core.Player;
using LootUp.Core.World;
using UnityEngine;

namespace LootUp.Core.Items
{
    public sealed class ItemSpawner : MonoBehaviour
    {
        [SerializeField]
        private TextAsset itemTableCsv;

        [SerializeField]
        private TextAsset itemIconTableCsv;

        [SerializeField]
        private BuildingGridUI buildingGridUI;

        [SerializeField]
        private InfiniteFloorManager floorManager;

        [SerializeField]
        private PlayerSpawner playerSpawner;

        [SerializeField]
        private RunItemEventRecorder eventRecorder;

        [SerializeField]
        private DifficultyBandDefinition difficultyBands;

        [SerializeField]
        private RectTransform itemLayer;

        [SerializeField]
        private bool spawnOnStart = true;

        [SerializeField]
        private int maxItemsPerPage = 6;

        [SerializeField]
        private int randomSeed = 30415;

        [SerializeField]
        private bool randomizeSeedOnStart = true;

        [SerializeField]
        private string forcedTestItemId;

        [SerializeField, Min(1)]
        private int guaranteedGoldenCupPageNumber = 3;

        [SerializeField]
        private string guaranteedGoldenCupItemId = "artifact_golden_cup_01";

        [SerializeField]
        private Vector2 itemSize = new Vector2(76.8f, 76.8f);

        [SerializeField]
        private int passCountFontSize = 32;

        [SerializeField]
        private int randomPassCountMin = 1;

        [SerializeField]
        private int randomPassCountMax = 5;

        [SerializeField]
        private int speedItemPassCountMin = 1;

        [SerializeField]
        private int speedItemPassCountMax = 3;

        [SerializeField]
        private int scoreBonusPercentPerExtraPass = 25;

        [SerializeField]
        private Color defaultItemColor = new Color(1f, 0.08f, 0.06f, 0.95f);

        private readonly List<ItemInstance> spawnedItems = new List<ItemInstance>();
        private ItemTable itemTable;
        private ItemIconTable itemIconTable;
        private IItemIconProvider iconProvider;
        private PlayerMotor playerMotor;
        private RectTransform artifactLayer;
        private int lastSpawnedPageIndex = int.MinValue;
        private int runtimeSeed;
        private bool hasRuntimeSeed;

        public ItemIconTable IconTable => itemIconTable;
        public event System.Action<int> CurrentPageItemsSpawned;
        public event System.Action CurrentPageItemOccupancyChanged;

        private void Awake()
        {
            EnsureReferences();
            ReloadTable();
            ReloadIconTable();
            EnsureItemLayer();
            EnsureArtifactLayer();
            EnsureIconProvider();
            EnsureRuntimeSeed();
        }

        private void LateUpdate()
        {
            // 아티팩트는 층별 시야 가림과 다른 인게임 레이어보다 항상 위에 표시한다.
            artifactLayer?.SetAsLastSibling();
        }

        private void OnEnable()
        {
            if (floorManager != null)
            {
                floorManager.CurrentFloorChanged += HandleCurrentFloorChanged;
            }
        }

        private void Start()
        {
            TryResolvePlayerMotor();

            if (spawnOnStart)
            {
                SpawnCurrentPageItems();
            }
        }

        private void OnDisable()
        {
            if (floorManager != null)
            {
                floorManager.CurrentFloorChanged -= HandleCurrentFloorChanged;
            }
        }

        [ContextMenu("Debug/Reload Item Table")]
        public void ReloadTable()
        {
            itemTable = ItemTable.Load(itemTableCsv);
        }

        [ContextMenu("Debug/Reload Item Icon Table")]
        public void ReloadIconTable()
        {
            itemIconTable = ItemIconTable.Load(itemIconTableCsv);
        }

        [ContextMenu("Debug/Spawn Current Page Items")]
        public void SpawnCurrentPageItems()
        {
            EnsureReferences();
            EnsureItemLayer();
            EnsureArtifactLayer();
            EnsureIconProvider();
            TryResolvePlayerMotor();

            if (buildingGridUI == null
                || floorManager == null
                || itemLayer == null
                || itemTable == null)
            {
                return;
            }

            ClearSpawnedItems();

            FloorPageData pageData = buildingGridUI.CurrentPageData;
            if (pageData == null)
            {
                return;
            }

            EnsureRuntimeSeed();

            int pageSeed = unchecked(
                runtimeSeed + floorManager.CurrentPageIndex * 73856093);
            System.Random random = new System.Random(pageSeed);

            // (변경) 드랍 선택과 셀 배치는 Planner가 담당한다.
            ItemPagePlanner planner = CreatePagePlanner();
            IReadOnlyList<ItemSpawnPlan> plans = planner.CreatePlan(
                pageData,
                buildingGridUI.Rows,
                buildingGridUI.Columns,
                maxItemsPerPage,
                random);

            // (변경) 계획된 데이터의 UI 생성은 View Factory에 위임한다.
            ItemViewFactory viewFactory = CreateViewFactory();
            for (int i = 0; i < plans.Count; i++)
            {
                ItemInstance item = viewFactory.Create(plans[i]);
                item.AvailabilityChanged += HandleItemAvailabilityChanged;
                spawnedItems.Add(item);
            }

            lastSpawnedPageIndex = floorManager.CurrentPageIndex;
            CurrentPageItemsSpawned?.Invoke(lastSpawnedPageIndex);
        }

        public bool IsCurrentPageCellOccupied(int column, int row)
        {
            if (buildingGridUI == null
                || column < 0
                || column >= buildingGridUI.Columns
                || row < 0
                || row >= buildingGridUI.Rows)
            {
                return false;
            }

            for (int i = 0; i < spawnedItems.Count; i++)
            {
                ItemInstance item = spawnedItems[i];
                if (item != null
                    && item.IsAvailable
                    && item.ColumnIndex == column
                    && item.PageFloorIndex == row)
                {
                    return true;
                }
            }

            return false;
        }

        [ContextMenu("Debug/Clear Items")]
        public void ClearSpawnedItems()
        {
            for (int i = spawnedItems.Count - 1; i >= 0; i--)
            {
                ItemInstance item = spawnedItems[i];
                if (item == null)
                {
                    continue;
                }

                item.AvailabilityChanged -= HandleItemAvailabilityChanged;

                if (Application.isPlaying)
                {
                    Destroy(item.gameObject);
                }
                else
                {
                    DestroyImmediate(item.gameObject);
                }
            }

            spawnedItems.Clear();
        }

        private void HandleCurrentFloorChanged(int currentAbsoluteFloor)
        {
            if (floorManager == null)
            {
                return;
            }

            if (floorManager.CurrentPageIndex != lastSpawnedPageIndex)
            {
                SpawnCurrentPageItems();
            }
        }

        private void HandleItemAvailabilityChanged(ItemInstance item)
        {
            CurrentPageItemOccupancyChanged?.Invoke();
        }

        private ItemPagePlanner CreatePagePlanner()
        {
            ItemSpawnPolicy spawnPolicy = new ItemSpawnPolicy(
                itemTable,
                eventRecorder,
                playerSpawner,
                forcedTestItemId,
                guaranteedGoldenCupPageNumber,
                guaranteedGoldenCupItemId,
                randomPassCountMin,
                randomPassCountMax,
                speedItemPassCountMin,
                speedItemPassCountMax,
                scoreBonusPercentPerExtraPass,
                difficultyBands);
            return new ItemPagePlanner(spawnPolicy);
        }

        private ItemViewFactory CreateViewFactory()
        {
            return new ItemViewFactory(
                buildingGridUI,
                floorManager,
                playerMotor,
                eventRecorder,
                itemLayer,
                artifactLayer,
                iconProvider,
                itemSize,
                passCountFontSize);
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

            if (eventRecorder == null)
            {
                eventRecorder = FindFirstObjectByType<RunItemEventRecorder>();
            }
        }

        private void EnsureIconProvider()
        {
            if (itemIconTable == null)
            {
                ReloadIconTable();
            }

            if (iconProvider == null)
            {
                iconProvider = new ResourceItemIconProvider(
                    itemIconTable,
                    new FallbackItemIconProvider(defaultItemColor));
            }
        }

        private void EnsureItemLayer()
        {
            if (itemLayer != null || buildingGridUI == null)
            {
                return;
            }

            GameObject layerObject = new GameObject("ItemLayer", typeof(RectTransform));
            layerObject.layer = buildingGridUI.gameObject.layer;
            layerObject.transform.SetParent(buildingGridUI.transform.parent, false);
            itemLayer = layerObject.GetComponent<RectTransform>();
            itemLayer.anchorMin = Vector2.zero;
            itemLayer.anchorMax = Vector2.one;
            itemLayer.offsetMin = Vector2.zero;
            itemLayer.offsetMax = Vector2.zero;
            itemLayer.pivot = new Vector2(0.5f, 0.5f);
            itemLayer.SetSiblingIndex(buildingGridUI.transform.GetSiblingIndex() + 1);
        }

        private void EnsureArtifactLayer()
        {
            if (artifactLayer != null
                || buildingGridUI == null
                || buildingGridUI.transform.parent == null)
            {
                return;
            }

            Transform existing = buildingGridUI.transform.parent.Find("ArtifactLayer");
            if (existing != null)
            {
                artifactLayer = existing as RectTransform;
            }
            else
            {
                GameObject layerObject =
                    new GameObject("ArtifactLayer", typeof(RectTransform));
                layerObject.layer = buildingGridUI.gameObject.layer;
                layerObject.transform.SetParent(buildingGridUI.transform.parent, false);
                artifactLayer = layerObject.GetComponent<RectTransform>();
                artifactLayer.anchorMin = Vector2.zero;
                artifactLayer.anchorMax = Vector2.one;
                artifactLayer.offsetMin = Vector2.zero;
                artifactLayer.offsetMax = Vector2.zero;
                artifactLayer.pivot = new Vector2(0.5f, 0.5f);
            }

            artifactLayer.SetAsLastSibling();
        }

        private void TryResolvePlayerMotor()
        {
            if (playerMotor != null)
            {
                return;
            }

            if (playerSpawner != null && playerSpawner.SpawnedPlayer != null)
            {
                playerMotor = playerSpawner.SpawnedPlayer.GetComponent<PlayerMotor>();
            }
        }

        private void EnsureRuntimeSeed()
        {
            if (hasRuntimeSeed)
            {
                return;
            }

            runtimeSeed = randomizeSeedOnStart
                ? unchecked(
                    randomSeed
                    ^ Random.Range(int.MinValue, int.MaxValue)
                    ^ GetInstanceID()
                    ^ System.Environment.TickCount)
                : randomSeed;
            hasRuntimeSeed = true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            maxItemsPerPage = Mathf.Max(0, maxItemsPerPage);
            passCountFontSize = Mathf.Max(1, passCountFontSize);
            randomPassCountMin = Mathf.Max(1, randomPassCountMin);
            randomPassCountMax = Mathf.Max(randomPassCountMin, randomPassCountMax);
            speedItemPassCountMin = Mathf.Max(1, speedItemPassCountMin);
            speedItemPassCountMax = Mathf.Max(
                speedItemPassCountMin,
                speedItemPassCountMax);
            scoreBonusPercentPerExtraPass = Mathf.Max(
                0,
                scoreBonusPercentPerExtraPass);
            itemSize.x = Mathf.Max(1f, itemSize.x);
            itemSize.y = Mathf.Max(1f, itemSize.y);
        }
#endif
    }
}
