using System.Collections;
using LootUp.Core.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LootUp.Core.World
{
    public sealed class ElevatorController : MonoBehaviour
    {
        [SerializeField]
        private BuildingGridUI buildingGridUI;

        [SerializeField]
        private InfiniteFloorManager floorManager;

        [SerializeField]
        private PlayerSpawner playerSpawner;

        [SerializeField]
        private RectTransform elevatorLayer;

        [SerializeField]
        private int leftElevatorColumn;

        [SerializeField]
        private int rightElevatorColumn = BuildingGridUI.DefaultColumns - 1;

        [SerializeField]
        private float activationCenterTolerancePixels = 12f;

        [SerializeField]
        private bool requireSubmitInput;

        [SerializeField]
        private float ascentDuration = 0.45f;

        [SerializeField]
        private Vector2 elevatorSize = new Vector2(104f, 18f);

        [SerializeField]
        private float cableThickness = 4f;

        [SerializeField]
        private Color elevatorColor = new Color(0.18f, 0.72f, 0.95f, 0.92f);

        [SerializeField]
        private Color elevatorOutlineColor = new Color(1f, 1f, 1f, 0.55f);

        private RectTransform[] elevatorRectTransforms;
        private Image[] elevatorImages;
        private RectTransform activeCableRectTransform;
        private Image activeCableImage;
        private RectTransform lastPageArrivalElevatorRectTransform;
        private PlayerMotor playerMotor;
        private PlayerController playerController;
        private InputAction submitAction;
        private bool isAscending;

        public int CurrentFloorStartColumn { get; private set; }
        public int CurrentAbsoluteFloor => floorManager != null ? floorManager.CurrentAbsoluteFloor : 1;
        public float PlatformHeight => Mathf.Max(1f, elevatorSize.y);
        public int ActiveElevatorColumn => GetCurrentElevatorColumn();
        public int InactiveElevatorColumn
        {
            get
            {
                int activeColumn = GetCurrentElevatorColumn();
                int leftColumn = GetElevatorColumnForAbsoluteFloor(2);
                int rightColumn = GetElevatorColumnForAbsoluteFloor(1);
                return activeColumn == leftColumn ? rightColumn : leftColumn;
            }
        }

        private void Awake()
        {
            EnsureReferences();
            CreateElevatorVisuals();
            CreateSubmitAction();
            CurrentFloorStartColumn = GetCurrentElevatorColumn();
        }

        private void OnEnable()
        {
            submitAction?.Enable();

            if (floorManager != null)
            {
                floorManager.CurrentFloorChanged += HandleCurrentFloorChanged;
            }

            ApplyElevatorPosition();
        }

        private void Start()
        {
            CurrentFloorStartColumn = GetCurrentElevatorColumn();
            ApplyElevatorPosition();
        }

        private void Update()
        {
            TryResolvePlayerMotor();

            if (playerMotor == null || floorManager == null)
            {
                return;
            }

            if (isAscending)
            {
                return;
            }

            if (!IsPlayerCenteredOnCurrentElevator())
            {
                return;
            }

            if (requireSubmitInput && (submitAction == null || !submitAction.WasPressedThisFrame()))
            {
                return;
            }

            StartCoroutine(AscendToNextFloor());
        }

        private void OnDisable()
        {
            submitAction?.Disable();

            if (floorManager != null)
            {
                floorManager.CurrentFloorChanged -= HandleCurrentFloorChanged;
            }
        }

        private void OnDestroy()
        {
            submitAction?.Dispose();
        }

        [ContextMenu("Debug/Move Player To Elevator")]
        private void DebugMovePlayerToElevator()
        {
            TryResolvePlayerMotor();
            playerMotor?.WarpToColumn(GetCurrentElevatorColumn());
        }

        [ContextMenu("Debug/Move To Next Floor")]
        private void DebugMoveToNextFloor()
        {
            if (!Application.isPlaying || isAscending)
            {
                return;
            }

            StartCoroutine(AscendToNextFloor());
        }

        public void ClearTravelGuideLines()
        {
            DestroyAllCableVisuals();
        }

        private IEnumerator AscendToNextFloor()
        {
            TryResolvePlayerMotor();

            RectTransform activeElevator = GetActiveElevatorRectTransform();
            if (floorManager == null || buildingGridUI == null || activeElevator == null || playerMotor == null)
            {
                yield break;
            }

            isAscending = true;
            playerMotor.SetMovementLocked(true);
            playerMotor.SnapCenterToColumn(GetCurrentElevatorColumn());
            playerController?.StopAndFace(GetNextTravelDirection());
            yield return null;

            int startAbsoluteFloor = floorManager.CurrentAbsoluteFloor;
            int startPageIndex = floorManager.CurrentPageIndex;
            int startElevatorColumn = GetCurrentElevatorColumn();
            int startRow = floorManager.CurrentPageFloorIndex;
            int targetBoundary = Mathf.Min(startRow + 1, buildingGridUI.Rows);
            Vector2 elevatorStart = GetElevatorAnchoredPositionForBoundary(startRow);
            Vector2 elevatorEnd = GetElevatorAnchoredPositionForBoundary(targetBoundary);
            Vector2 playerStart = playerMotor.GetAnchoredPositionForFloorIndex(startRow);
            Vector2 playerEnd = GetPlayerAnchoredPositionForBoundary(targetBoundary);
            float startFloorLineY = GetFloorLineY(startRow);
            float elapsed = 0f;

            CreateCableVisual(startAbsoluteFloor);
            SetCableFromPlatformBottomToFloorLine(elevatorStart, startFloorLineY);
            activeElevator.gameObject.SetActive(true);

            while (elapsed < ascentDuration)
            {
                elapsed += Time.deltaTime;
                float t = ascentDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / ascentDuration);
                float easedT = Mathf.SmoothStep(0f, 1f, t);
                Vector2 elevatorPosition = Vector2.Lerp(elevatorStart, elevatorEnd, easedT);
                Vector2 playerPosition = Vector2.Lerp(playerStart, playerEnd, easedT);

                activeElevator.anchoredPosition = elevatorPosition;
                playerMotor.SetManualAnchoredPosition(playerPosition);
                SetCableFromPlatformBottomToFloorLine(elevatorPosition, startFloorLineY);

                yield return null;
            }

            floorManager.MoveToNextFloor();
            CurrentFloorStartColumn = startElevatorColumn;
            bool pageChanged = floorManager.CurrentPageIndex != startPageIndex;
            if (pageChanged)
            {
                DestroyAllCableVisuals();
                ApplyElevatorPosition();
                CreatePageArrivalElevatorVisual(startAbsoluteFloor, startElevatorColumn);
            }

            playerMotor.SnapCenterToColumn(startElevatorColumn);
            playerController?.StopAndFace(GetNextTravelDirection());
            playerMotor.SetMovementLocked(false);

            isAscending = false;
        }

        private void HandleCurrentFloorChanged(int currentAbsoluteFloor)
        {
            if (isAscending)
            {
                return;
            }

            ApplyElevatorPosition();
        }

        private void CreateElevatorVisuals()
        {
            if (elevatorLayer == null || buildingGridUI == null)
            {
                return;
            }

            int rowCount = Mathf.Max(1, buildingGridUI.Rows);
            if (elevatorRectTransforms != null && elevatorRectTransforms.Length == rowCount)
            {
                return;
            }

            elevatorRectTransforms = new RectTransform[rowCount];
            elevatorImages = new Image[rowCount];

            for (int row = 0; row < rowCount; row++)
            {
                GameObject elevatorObject = new GameObject($"Elevator_{row}", typeof(RectTransform), typeof(Image), typeof(Outline));
                elevatorObject.layer = elevatorLayer.gameObject.layer;
                elevatorObject.transform.SetParent(elevatorLayer, false);

                Image image = elevatorObject.GetComponent<Image>();
                image.color = elevatorColor;
                image.raycastTarget = false;

                Outline outline = elevatorObject.GetComponent<Outline>();
                outline.effectColor = elevatorOutlineColor;
                outline.effectDistance = new Vector2(2f, -2f);
                outline.useGraphicAlpha = true;

                RectTransform elevatorRect = elevatorObject.GetComponent<RectTransform>();
                elevatorRect.sizeDelta = elevatorSize;

                elevatorRectTransforms[row] = elevatorRect;
                elevatorImages[row] = image;
            }
        }

        private void CreateCableVisual(int startAbsoluteFloor)
        {
            if (elevatorLayer == null)
            {
                return;
            }

            GameObject cableObject = new GameObject($"ElevatorGuideLine_{startAbsoluteFloor}", typeof(RectTransform), typeof(Image));
            cableObject.layer = elevatorLayer.gameObject.layer;
            cableObject.transform.SetParent(elevatorLayer, false);
            cableObject.transform.SetSiblingIndex(0);

            activeCableImage = cableObject.GetComponent<Image>();
            activeCableImage.color = elevatorColor;
            activeCableImage.raycastTarget = false;

            activeCableRectTransform = cableObject.GetComponent<RectTransform>();
        }

        private void CreatePageArrivalElevatorVisual(int startAbsoluteFloor, int elevatorColumn)
        {
            if (elevatorLayer == null || buildingGridUI == null || floorManager == null)
            {
                return;
            }

            DestroyLastPageArrivalElevatorVisual();

            GameObject elevatorObject = new GameObject($"PageArrivalElevator_{startAbsoluteFloor}_To_{floorManager.CurrentAbsoluteFloor}", typeof(RectTransform), typeof(Image), typeof(Outline));
            elevatorObject.layer = elevatorLayer.gameObject.layer;
            elevatorObject.transform.SetParent(elevatorLayer, false);

            Image image = elevatorObject.GetComponent<Image>();
            image.color = elevatorColor;
            image.raycastTarget = false;

            Outline outline = elevatorObject.GetComponent<Outline>();
            outline.effectColor = elevatorOutlineColor;
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = true;

            lastPageArrivalElevatorRectTransform = elevatorObject.GetComponent<RectTransform>();
            lastPageArrivalElevatorRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            lastPageArrivalElevatorRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            lastPageArrivalElevatorRectTransform.pivot = new Vector2(0.5f, 0.5f);
            lastPageArrivalElevatorRectTransform.sizeDelta = elevatorSize;
            lastPageArrivalElevatorRectTransform.anchoredPosition = GetElevatorAnchoredPositionForBoundary(floorManager.CurrentPageFloorIndex, elevatorColumn);
            lastPageArrivalElevatorRectTransform.SetAsLastSibling();
        }

        private void DestroyAllCableVisuals()
        {
            if (elevatorLayer == null)
            {
                activeCableRectTransform = null;
                activeCableImage = null;
                return;
            }

            for (int i = elevatorLayer.childCount - 1; i >= 0; i--)
            {
                Transform child = elevatorLayer.GetChild(i);
                if (child != null && child.name.StartsWith("ElevatorGuideLine_"))
                {
                    child.gameObject.SetActive(false);
                    Destroy(child.gameObject);
                }
            }

            activeCableRectTransform = null;
            activeCableImage = null;
        }

        private void DestroyLastPageArrivalElevatorVisual()
        {
            if (lastPageArrivalElevatorRectTransform == null)
            {
                return;
            }

            Destroy(lastPageArrivalElevatorRectTransform.gameObject);
            lastPageArrivalElevatorRectTransform = null;
        }

        private void ApplyElevatorPosition()
        {
            if (elevatorRectTransforms == null || elevatorLayer == null || buildingGridUI == null)
            {
                return;
            }

            for (int row = 0; row < elevatorRectTransforms.Length; row++)
            {
                RectTransform elevatorRect = elevatorRectTransforms[row];
                if (elevatorRect == null)
                {
                    continue;
                }

                Vector2 elevatorPosition = GetElevatorAnchoredPositionForRow(row);
                elevatorRect.anchorMin = new Vector2(0.5f, 0.5f);
                elevatorRect.anchorMax = new Vector2(0.5f, 0.5f);
                elevatorRect.pivot = new Vector2(0.5f, 0.5f);
                elevatorRect.anchoredPosition = elevatorPosition;
                elevatorRect.sizeDelta = elevatorSize;
                elevatorRect.gameObject.SetActive(true);
            }

        }

        private int GetCurrentElevatorColumn()
        {
            int currentAbsoluteFloor = floorManager != null ? floorManager.CurrentAbsoluteFloor : 1;
            return GetElevatorColumnForAbsoluteFloor(currentAbsoluteFloor);
        }

        private int GetElevatorColumnForAbsoluteFloor(int absoluteFloor)
        {
            int columnCount = buildingGridUI != null ? buildingGridUI.Columns : BuildingGridUI.DefaultColumns;
            int maxColumn = Mathf.Max(0, columnCount - 1);
            bool useRightElevator = absoluteFloor % 2 == 1;
            int targetColumn = useRightElevator ? rightElevatorColumn : leftElevatorColumn;

            return Mathf.Clamp(targetColumn, 0, maxColumn);
        }

        private Vector2 GetElevatorAnchoredPositionForRow(int row)
        {
            FloorPageData pageData = buildingGridUI.CurrentPageData;
            int absoluteFloor = pageData != null ? pageData.GetAddressByRow(row).AbsoluteFloor : row + 1;
            return GetElevatorAnchoredPositionForBoundary(row, GetElevatorColumnForAbsoluteFloor(absoluteFloor));
        }

        private Vector2 GetElevatorAnchoredPositionForBoundary(int boundaryIndex)
        {
            return GetElevatorAnchoredPositionForBoundary(boundaryIndex, GetCurrentElevatorColumn());
        }

        private Vector2 GetElevatorAnchoredPositionForBoundary(int boundaryIndex, int column)
        {
            Rect layerRect = elevatorLayer.rect;
            int clampedColumn = Mathf.Clamp(column, 0, Mathf.Max(0, buildingGridUI.Columns - 1));
            int clampedBoundary = Mathf.Clamp(boundaryIndex, 0, Mathf.Max(0, buildingGridUI.Rows));

            float normalizedX = (clampedColumn + 0.5f) / Mathf.Max(1, buildingGridUI.Columns);
            float x = Mathf.Lerp(layerRect.xMin, layerRect.xMax, normalizedX);
            float floorLineY = GetFloorLineY(clampedBoundary);
            float y = floorLineY - elevatorSize.y * 0.5f;

            return new Vector2(x, y);
        }

        private Vector2 GetPlayerAnchoredPositionForBoundary(int boundaryIndex)
        {
            RectTransform playerRect = playerMotor.RectTransform;
            int clampedBoundary = Mathf.Clamp(boundaryIndex, 0, Mathf.Max(0, buildingGridUI.Rows));
            float floorLineY = GetFloorLineY(clampedBoundary);
            float playerY = floorLineY + playerRect.rect.height * 0.5f;
            float playerX = playerRect.anchoredPosition.x;

            return new Vector2(playerX, playerY);
        }

        private float GetFloorLineY(int boundaryIndex)
        {
            Rect layerRect = elevatorLayer.rect;
            int clampedBoundary = Mathf.Clamp(boundaryIndex, 0, Mathf.Max(0, buildingGridUI.Rows));
            float rowHeight = layerRect.height / Mathf.Max(1, buildingGridUI.Rows);

            return layerRect.yMin + rowHeight * clampedBoundary;
        }

        private void SetCableFromPlatformBottomToFloorLine(Vector2 platformPosition, float floorLineY)
        {
            if (activeCableRectTransform == null)
            {
                return;
            }

            float platformBottomY = platformPosition.y - elevatorSize.y * 0.5f;
            float minY = Mathf.Min(floorLineY, platformBottomY);
            float maxY = Mathf.Max(floorLineY, platformBottomY);
            float height = Mathf.Max(cableThickness, maxY - minY);
            float centerY = minY + height * 0.5f;

            activeCableRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            activeCableRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            activeCableRectTransform.pivot = new Vector2(0.5f, 0.5f);
            activeCableRectTransform.anchoredPosition = new Vector2(platformPosition.x, centerY);
            activeCableRectTransform.sizeDelta = new Vector2(cableThickness, height);
            activeCableRectTransform.gameObject.SetActive(true);

            if (activeCableImage != null)
            {
                activeCableImage.color = elevatorColor;
            }
        }

        private bool IsPlayerCenteredOnCurrentElevator()
        {
            RectTransform activeElevator = GetActiveElevatorRectTransform();
            if (activeElevator == null || playerMotor == null)
            {
                return false;
            }

            float centerDistance = Mathf.Abs(playerMotor.RectTransform.anchoredPosition.x - activeElevator.anchoredPosition.x);
            return centerDistance <= activationCenterTolerancePixels;
        }

        private RectTransform GetActiveElevatorRectTransform()
        {
            if (elevatorRectTransforms == null || floorManager == null)
            {
                return null;
            }

            int row = Mathf.Clamp(floorManager.CurrentPageFloorIndex, 0, elevatorRectTransforms.Length - 1);
            return elevatorRectTransforms[row];
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
                playerController = playerSpawner.SpawnedPlayer.GetComponent<PlayerController>();
            }
        }

        private int GetNextTravelDirection()
        {
            int currentColumn = GetElevatorColumnForAbsoluteFloor(floorManager.CurrentAbsoluteFloor - 1);
            int nextColumn = GetElevatorColumnForAbsoluteFloor(floorManager.CurrentAbsoluteFloor);

            return nextColumn < currentColumn ? -1 : 1;
        }

        private void CreateSubmitAction()
        {
            submitAction = new InputAction("UseElevator", InputActionType.Button);
            submitAction.AddBinding("<Keyboard>/space");
            submitAction.AddBinding("<Keyboard>/e");
            submitAction.AddBinding("<Gamepad>/buttonSouth");
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
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            leftElevatorColumn = Mathf.Max(0, leftElevatorColumn);
            rightElevatorColumn = Mathf.Max(0, rightElevatorColumn);
            activationCenterTolerancePixels = Mathf.Max(0f, activationCenterTolerancePixels);
            ascentDuration = Mathf.Max(0f, ascentDuration);
            cableThickness = Mathf.Max(1f, cableThickness);
            elevatorSize.x = Mathf.Max(1f, elevatorSize.x);
            elevatorSize.y = Mathf.Max(1f, elevatorSize.y);
        }
#endif
    }
}
