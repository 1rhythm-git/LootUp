using System.Collections;
using System.Collections.Generic;
using LootUp.Core.Characters;
using LootUp.Core.Game;
using LootUp.Core.Items;
using LootUp.Core.Player;
using LootUp.Core.UI;
using LootUp.Core.World;
using UnityEngine;
using UnityEngine.UI;

namespace LootUp.Core.Rising
{
    public sealed class RisingSystem : MonoBehaviour, IGameplayPausable
    {
        [Header("Spawn Balance")]
        [SerializeField, Min(1)]
        private int minimumSpawnFloor = 2;

        [SerializeField, Range(0f, 1f)]
        private float baseSpawnChance = 0.12f;

        [SerializeField, Range(0f, 1f)]
        private float spawnChancePerMiss = 0.03f;

        [SerializeField, Range(0f, 1f)]
        private float maximumSpawnChance = 0.45f;

        [SerializeField, Range(0f, 1f)]
        private float maximumBoostedSpawnChance = 0.7f;

        [SerializeField, Min(1)]
        private int guaranteedAttempt = 8;

        [SerializeField, Min(0f)]
        private float spawnDelaySeconds = 0.45f;

        [SerializeField, Min(0f)]
        private float startElevatorClearanceColumns = 1.15f;

        [Header("Duplicate Reward")]
        [SerializeField, Range(0f, 1f)]
        private float completedLetterWeightMultiplier = 0.45f;

        [SerializeField, Range(0f, 1f)]
        private float duplicateRepeatWeightMultiplier = 0.5f;

        [SerializeField, Range(0f, 1f)]
        private float duplicateSpawnChanceBonus = 0.05f;

        [SerializeField, Range(0f, 1f)]
        private float maximumDuplicateSpawnChanceBonus = 0.25f;

        [SerializeField, Min(0f)]
        private float duplicateTimeBonusSeconds = 3f;

        [SerializeField, Min(0f)]
        private float duplicateFeverGaugeBonus = 10f;

        [Header("Rising Effect")]
        [SerializeField, Min(1)]
        private int stageAdvanceCount = 1;

        [SerializeField, Min(0.01f)]
        private float floorStepDuration = 0.065f;

        [SerializeField, Min(0f)]
        private float activationHoldSeconds = 0.35f;

        [SerializeField]
        private Vector2 pickupSize = new Vector2(82f, 82f);

        private readonly List<RectTransform> speedLines = new List<RectTransform>();
        private InfiniteFloorManager floorManager;
        private BuildingGridUI buildingGridUI;
        private ElevatorController elevatorController;
        private PlayerSpawner playerSpawner;
        private ItemSpawner itemSpawner;
        private TopHUDController topHUDController;
        private Canvas targetCanvas;
        private RectTransform pickupLayer;
        private RisingLetterPickup currentPickup;
        private Coroutine pendingSpawnCoroutine;
        private Coroutine activationCoroutine;
        private GameObject activationOverlay;
        private int missedEligibleFloors;
        private int lastEvaluatedFloor = int.MinValue;
        private bool subscribed;
        private bool gameplayPaused;
        private bool isActivating;
        private bool elevatorWasEnabled;
        private bool itemSpawnerWasEnabled;
        private readonly int[] duplicateCountsByLetterType = new int[RisingConstants.LetterTypeCount];
        private int collectedSlotMask;
        private float accumulatedDuplicateSpawnChanceBonus;

        public int CollectedLetterCount => CountCollectedSlots();
        public bool IsActivating => isActivating;

        private void Start()
        {
            EnsureReferences();
            Subscribe();
            topHUDController?.SetRisingProgressMask(collectedSlotMask, false);

            if (floorManager != null)
            {
                EvaluateFloor(floorManager.CurrentAbsoluteFloor);
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        public void Configure(
            InfiniteFloorManager manager,
            BuildingGridUI gridUI,
            ElevatorController elevator,
            PlayerSpawner spawner,
            ItemSpawner regularItemSpawner,
            TopHUDController hud,
            Canvas canvas)
        {
            if (floorManager != manager)
            {
                Unsubscribe();
            }

            floorManager = manager;
            buildingGridUI = gridUI;
            elevatorController = elevator;
            playerSpawner = spawner;
            itemSpawner = regularItemSpawner;
            topHUDController = hud;
            targetCanvas = canvas;
            EnsureReferences();
            Subscribe();
            topHUDController?.SetRisingProgressMask(collectedSlotMask, isActivating);
        }

        public void SetGameplayPaused(bool isPaused)
        {
            gameplayPaused = isPaused;
            if (!isPaused)
            {
                if (!isActivating && floorManager != null)
                {
                    lastEvaluatedFloor = int.MinValue;
                    EvaluateFloor(floorManager.CurrentAbsoluteFloor);
                }

                return;
            }

            CancelPendingSpawn();
            DestroyCurrentPickup();
            if (activationCoroutine != null)
            {
                StopCoroutine(activationCoroutine);
                activationCoroutine = null;
            }

            ClearActivationOverlay();
            isActivating = false;
        }

        [ContextMenu("Debug/Spawn Next RISING Letter")]
        private void DebugSpawnNextLetter()
        {
            if (!Application.isPlaying || gameplayPaused || isActivating || currentPickup != null)
            {
                return;
            }

            CancelPendingSpawn();
            pendingSpawnCoroutine = StartCoroutine(SpawnLetterWhenReady(
                floorManager != null ? floorManager.CurrentAbsoluteFloor : 1,
                true));
        }

        [ContextMenu("Debug/Prepare Final RISING Letter")]
        private void DebugPrepareFinalLetter()
        {
            if (!Application.isPlaying || gameplayPaused || isActivating)
            {
                return;
            }

            collectedSlotMask = RisingConstants.FullCollectionMask & ~(1 << (RisingConstants.LetterCount - 1));
            topHUDController?.SetRisingProgressMask(collectedSlotMask, false);
            DebugSpawnNextLetter();
        }

        public void AcquireRandomLetterForDebug()
        {
            if (!Application.isPlaying || gameplayPaused || isActivating)
            {
                return;
            }

            int letterTypeIndex = DrawLetterTypeIndex();
            AcquireLetter(letterTypeIndex, true);
        }

        private void HandleCurrentFloorChanged(int currentAbsoluteFloor)
        {
            DestroyCurrentPickup();
            CancelPendingSpawn();

            if (!isActivating)
            {
                EvaluateFloor(currentAbsoluteFloor);
            }
        }

        private void EvaluateFloor(int absoluteFloor)
        {
            if (gameplayPaused
                || isActivating
                || currentPickup != null
                || floorManager == null
                || absoluteFloor < minimumSpawnFloor
                || absoluteFloor == lastEvaluatedFloor)
            {
                return;
            }

            lastEvaluatedFloor = absoluteFloor;
            int attemptNumber = missedEligibleFloors + 1;
            float spawnChance = Mathf.Min(
                maximumBoostedSpawnChance,
                Mathf.Min(
                    maximumSpawnChance,
                    baseSpawnChance + missedEligibleFloors * spawnChancePerMiss)
                + accumulatedDuplicateSpawnChanceBonus);
            bool shouldSpawn = attemptNumber >= guaranteedAttempt || Random.value <= spawnChance;
            if (!shouldSpawn)
            {
                missedEligibleFloors++;
                return;
            }

            missedEligibleFloors = 0;
            pendingSpawnCoroutine = StartCoroutine(SpawnLetterWhenReady(absoluteFloor, false));
        }

        private IEnumerator SpawnLetterWhenReady(int expectedFloor, bool ignoreClearance)
        {
            float elapsed = 0f;
            while (elapsed < spawnDelaySeconds)
            {
                if (!CanContinueSpawn(expectedFloor))
                {
                    pendingSpawnCoroutine = null;
                    yield break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            PlayerMotor playerMotor = ResolvePlayerMotor();
            while (playerMotor == null
                || (!ignoreClearance
                    && elevatorController != null
                    && playerMotor.IsWithinColumnRange(
                        elevatorController.CurrentFloorStartColumn,
                        startElevatorClearanceColumns)))
            {
                if (!CanContinueSpawn(expectedFloor))
                {
                    pendingSpawnCoroutine = null;
                    yield break;
                }

                playerMotor = ResolvePlayerMotor();
                yield return null;
            }

            CreatePickup(expectedFloor, playerMotor);
            pendingSpawnCoroutine = null;
        }

        private bool CanContinueSpawn(int expectedFloor)
        {
            return !gameplayPaused
                && !isActivating
                && currentPickup == null
                && floorManager != null
                && floorManager.CurrentAbsoluteFloor == expectedFloor;
        }

        private void CreatePickup(int absoluteFloor, PlayerMotor playerMotor)
        {
            EnsurePickupLayer();
            if (pickupLayer == null || buildingGridUI == null || elevatorController == null)
            {
                return;
            }

            int letterTypeIndex = DrawLetterTypeIndex();
            int displaySlotIndex = GetDisplaySlotIndex(letterTypeIndex);
            int column = elevatorController.InactiveElevatorColumn;
            int row = floorManager != null ? floorManager.CurrentPageFloorIndex : 0;
            Color letterColor = RisingConstants.GetColor(displaySlotIndex);

            GameObject pickupObject = new GameObject(
                $"RisingLetter_{RisingConstants.GetLetterByType(letterTypeIndex)}_{absoluteFloor}",
                typeof(RectTransform),
                typeof(Image),
                typeof(Outline),
                typeof(RisingLetterPickup));
            pickupObject.layer = pickupLayer.gameObject.layer;
            pickupObject.transform.SetParent(pickupLayer, false);

            RectTransform pickupRect = pickupObject.GetComponent<RectTransform>();
            pickupRect.anchorMin = new Vector2(0.5f, 0.5f);
            pickupRect.anchorMax = new Vector2(0.5f, 0.5f);
            pickupRect.pivot = new Vector2(0.5f, 0.5f);
            pickupRect.sizeDelta = pickupSize;
            pickupRect.anchoredPosition = GetPickupPosition(column, row);

            Image background = pickupObject.GetComponent<Image>();
            background.color = new Color(letterColor.r, letterColor.g, letterColor.b, 0.88f);
            background.raycastTarget = false;

            Outline outline = pickupObject.GetComponent<Outline>();
            outline.effectColor = Color.white;
            outline.effectDistance = new Vector2(3f, -3f);
            outline.useGraphicAlpha = true;

            GameObject textObject = new GameObject("Letter", typeof(RectTransform), typeof(Text), typeof(Outline));
            textObject.layer = pickupObject.layer;
            textObject.transform.SetParent(pickupRect, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            Text letterText = textObject.GetComponent<Text>();
            letterText.text = RisingConstants.GetLetterByType(letterTypeIndex).ToString();
            letterText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            letterText.fontSize = 55;
            letterText.fontStyle = FontStyle.Bold;
            letterText.alignment = TextAnchor.MiddleCenter;
            letterText.color = Color.white;
            letterText.raycastTarget = false;

            Outline textOutline = textObject.GetComponent<Outline>();
            textOutline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            textOutline.effectDistance = new Vector2(3f, -3f);

            currentPickup = pickupObject.GetComponent<RisingLetterPickup>();
            currentPickup.Configure(letterTypeIndex, playerMotor, HandleLetterCollected);
        }

        private Vector2 GetPickupPosition(int column, int row)
        {
            RectTransform cellRect = buildingGridUI.GetCellRectTransform(column, row);
            if (cellRect == null)
            {
                return Vector2.zero;
            }

            Vector3[] corners = new Vector3[4];
            cellRect.GetWorldCorners(corners);
            Vector3 bottomCenterWorld = Vector3.Lerp(corners[0], corners[3], 0.5f);
            Vector2 bottomCenterLocal = pickupLayer.InverseTransformPoint(bottomCenterWorld);
            return new Vector2(bottomCenterLocal.x, bottomCenterLocal.y + pickupSize.y * 0.5f);
        }

        private void HandleLetterCollected(RisingLetterPickup pickup)
        {
            if (pickup == null || pickup != currentPickup || isActivating || gameplayPaused)
            {
                return;
            }

            currentPickup = null;
            Destroy(pickup.gameObject);
            AcquireLetter(pickup.LetterTypeIndex, false);
        }

        private void AcquireLetter(int letterTypeIndex, bool fromDebugInput)
        {
            int safeLetterTypeIndex = Mathf.Clamp(
                letterTypeIndex,
                0,
                RisingConstants.LetterTypeCount - 1);
            char letter = RisingConstants.GetLetterByType(safeLetterTypeIndex);
            int availableSlotIndex = FindFirstUncollectedSlot(safeLetterTypeIndex);

            if (availableSlotIndex >= 0)
            {
                collectedSlotMask |= 1 << availableSlotIndex;
                topHUDController?.SetItemStatus(
                    collectedSlotMask == RisingConstants.FullCollectionMask
                        ? "RISING READY"
                        : $"RISING {CountCollectedSlots()}/{RisingConstants.LetterCount}");
            }
            else
            {
                ApplyDuplicateReward(safeLetterTypeIndex, letter);
            }

            topHUDController?.SetRisingProgressMask(collectedSlotMask, false);
            if (fromDebugInput)
            {
                Debug.Log($"RISING test draw: {letter}", this);
            }

            if (collectedSlotMask == RisingConstants.FullCollectionMask
                && activationCoroutine == null)
            {
                if (topHUDController != null
                    && !topHUDController.TryBeginRisingTransition())
                {
                    return;
                }

                activationCoroutine = StartCoroutine(ActivateRising());
            }
        }

        private void ApplyDuplicateReward(int letterTypeIndex, char letter)
        {
            duplicateCountsByLetterType[letterTypeIndex]++;
            accumulatedDuplicateSpawnChanceBonus = Mathf.Min(
                maximumDuplicateSpawnChanceBonus,
                accumulatedDuplicateSpawnChanceBonus + duplicateSpawnChanceBonus);

            topHUDController?.AddTime(duplicateTimeBonusSeconds);
            PlayerCharacterRuntime characterRuntime = ResolvePlayerCharacterRuntime();
            float addedFever = characterRuntime != null
                ? characterRuntime.AddFeverGaugeFromItem(duplicateFeverGaugeBonus)
                : 0f;
            topHUDController?.SetItemStatus(
                $"DUPLICATE {letter}  +{duplicateTimeBonusSeconds:0.#}s  +{addedFever:0.#} FEVER");
        }

        private int DrawLetterTypeIndex()
        {
            float totalWeight = 0f;
            float[] weights = new float[RisingConstants.LetterTypeCount];
            for (int i = 0; i < weights.Length; i++)
            {
                int remainingSlotCount = CountUncollectedSlots(i);
                float weight = remainingSlotCount;
                if (remainingSlotCount <= 0)
                {
                    weight = completedLetterWeightMultiplier;
                }

                weight *= Mathf.Pow(
                    duplicateRepeatWeightMultiplier,
                    duplicateCountsByLetterType[i]);
                weights[i] = Mathf.Max(0.0001f, weight);
                totalWeight += weights[i];
            }

            float roll = Random.value * totalWeight;
            for (int i = 0; i < weights.Length; i++)
            {
                roll -= weights[i];
                if (roll <= 0f)
                {
                    return i;
                }
            }

            return weights.Length - 1;
        }

        private int GetDisplaySlotIndex(int letterTypeIndex)
        {
            int uncollectedSlotIndex = FindFirstUncollectedSlot(letterTypeIndex);
            return uncollectedSlotIndex >= 0
                ? uncollectedSlotIndex
                : RisingConstants.GetFirstSlotIndex(letterTypeIndex);
        }

        private int FindFirstUncollectedSlot(int letterTypeIndex)
        {
            for (int i = 0; i < RisingConstants.LetterCount; i++)
            {
                if (RisingConstants.SlotMatchesLetterType(i, letterTypeIndex)
                    && (collectedSlotMask & (1 << i)) == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private int CountUncollectedSlots(int letterTypeIndex)
        {
            int count = 0;
            for (int i = 0; i < RisingConstants.LetterCount; i++)
            {
                if (RisingConstants.SlotMatchesLetterType(i, letterTypeIndex)
                    && (collectedSlotMask & (1 << i)) == 0)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountCollectedSlots()
        {
            int count = 0;
            for (int i = 0; i < RisingConstants.LetterCount; i++)
            {
                if ((collectedSlotMask & (1 << i)) != 0)
                {
                    count++;
                }
            }

            return count;
        }

        private IEnumerator ActivateRising()
        {
            isActivating = true;
            CancelPendingSpawn();
            DestroyCurrentPickup();
            PlayerMotor playerMotor = ResolvePlayerMotor();
            PlayerController playerController = ResolvePlayerController();
            if (floorManager == null || buildingGridUI == null || playerMotor == null)
            {
                ResetRisingProgress();
                topHUDController?.SetTimerPaused(false);
                isActivating = false;
                activationCoroutine = null;
                yield break;
            }

            elevatorWasEnabled = elevatorController != null && elevatorController.enabled;
            itemSpawnerWasEnabled = itemSpawner != null && itemSpawner.enabled;
            if (elevatorController != null)
            {
                elevatorController.ClearTravelGuideLines();
                elevatorController.enabled = false;
            }

            if (itemSpawner != null)
            {
                itemSpawner.enabled = false;
                itemSpawner.ClearSpawnedItems();
            }

            topHUDController?.SetTimerPaused(true);
            topHUDController?.SetRisingProgressMask(RisingConstants.FullCollectionMask, true);
            playerController?.SetControlEnabled(false);
            playerMotor.SetMovementLocked(true);

            int currentFloor = floorManager.CurrentAbsoluteFloor;
            int rowsPerStage = Mathf.Max(1, buildingGridUI.Rows);
            int targetStageIndex = floorManager.CurrentPageIndex + Mathf.Max(1, stageAdvanceCount);
            int targetFloor = targetStageIndex * rowsPerStage + 1;
            int passedFloors = Mathf.Max(1, targetFloor - currentFloor);
            CreateActivationOverlay(passedFloors);

            for (int floor = currentFloor + 1; floor <= targetFloor; floor++)
            {
                floorManager.SetCurrentFloor(floor);
                AnimateSpeedLines(floor - currentFloor);
                yield return new WaitForSecondsRealtime(floorStepDuration);
            }

            playerMotor.SnapCenterToColumn(elevatorController != null
                ? elevatorController.InactiveElevatorColumn
                : 0);
            topHUDController?.SetItemStatus($"RISING +{passedFloors} FLOORS");
            yield return new WaitForSecondsRealtime(activationHoldSeconds);

            ClearActivationOverlay();
            ResetRisingProgress();
            playerMotor.SetMovementLocked(false);
            playerController?.SetControlEnabled(true);
            topHUDController?.SetTimerPaused(false);

            if (elevatorController != null)
            {
                elevatorController.ClearTravelGuideLines();
                elevatorController.enabled = elevatorWasEnabled;
            }

            if (itemSpawner != null)
            {
                itemSpawner.enabled = itemSpawnerWasEnabled;
                if (itemSpawnerWasEnabled)
                {
                    itemSpawner.SpawnCurrentPageItems();
                }
            }

            isActivating = false;
            activationCoroutine = null;
            lastEvaluatedFloor = int.MinValue;
            EvaluateFloor(floorManager.CurrentAbsoluteFloor);
        }

        private void CreateActivationOverlay(int passedFloors)
        {
            ClearActivationOverlay();
            if (targetCanvas == null)
            {
                return;
            }

            activationOverlay = new GameObject("RisingActivationOverlay", typeof(RectTransform), typeof(Image));
            activationOverlay.layer = targetCanvas.gameObject.layer;
            activationOverlay.transform.SetParent(targetCanvas.transform, false);
            RectTransform overlayRect = activationOverlay.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            Image overlayImage = activationOverlay.GetComponent<Image>();
            overlayImage.color = new Color(0.02f, 0.04f, 0.08f, 0.5f);
            overlayImage.raycastTarget = false;

            speedLines.Clear();
            for (int i = 0; i < 12; i++)
            {
                GameObject lineObject = new GameObject($"SpeedLine_{i}", typeof(RectTransform), typeof(Image));
                lineObject.layer = activationOverlay.layer;
                lineObject.transform.SetParent(overlayRect, false);
                RectTransform lineRect = lineObject.GetComponent<RectTransform>();
                float x = (i + 0.5f) / 12f;
                lineRect.anchorMin = new Vector2(x, 0.5f);
                lineRect.anchorMax = new Vector2(x, 0.5f);
                lineRect.sizeDelta = new Vector2(i % 3 == 0 ? 8f : 4f, 180f + i % 4 * 70f);
                lineRect.anchoredPosition = new Vector2(0f, (i % 5 - 2) * 180f);
                Image lineImage = lineObject.GetComponent<Image>();
                lineImage.color = RisingConstants.GetColor(i % RisingConstants.LetterCount);
                lineImage.raycastTarget = false;
                speedLines.Add(lineRect);
            }

            GameObject textObject = new GameObject("RisingText", typeof(RectTransform), typeof(Text), typeof(Outline));
            textObject.layer = activationOverlay.layer;
            textObject.transform.SetParent(overlayRect, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.1f, 0.4f);
            textRect.anchorMax = new Vector2(0.9f, 0.6f);
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            Text text = textObject.GetComponent<Text>();
            text.text = $"RISING\n+{passedFloors} FLOORS";
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 72;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            Outline outline = textObject.GetComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(5f, -5f);
        }

        private void AnimateSpeedLines(int step)
        {
            for (int i = 0; i < speedLines.Count; i++)
            {
                RectTransform line = speedLines[i];
                if (line == null)
                {
                    continue;
                }

                Vector2 position = line.anchoredPosition;
                position.y = Mathf.Repeat(position.y + 155f + step * 17f + 600f, 1200f) - 600f;
                line.anchoredPosition = position;
            }
        }

        private void ResetRisingProgress()
        {
            collectedSlotMask = 0;
            accumulatedDuplicateSpawnChanceBonus = 0f;
            for (int i = 0; i < duplicateCountsByLetterType.Length; i++)
            {
                duplicateCountsByLetterType[i] = 0;
            }

            topHUDController?.SetRisingProgressMask(0, false);
        }

        private void ClearActivationOverlay()
        {
            speedLines.Clear();
            if (activationOverlay != null)
            {
                Destroy(activationOverlay);
                activationOverlay = null;
            }
        }

        private void EnsurePickupLayer()
        {
            if (pickupLayer != null || buildingGridUI == null)
            {
                return;
            }

            RectTransform parent = buildingGridUI.transform.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            Transform existing = parent.Find("RisingItemLayer");
            if (existing != null)
            {
                pickupLayer = existing as RectTransform;
                return;
            }

            GameObject layerObject = new GameObject("RisingItemLayer", typeof(RectTransform));
            layerObject.layer = parent.gameObject.layer;
            layerObject.transform.SetParent(parent, false);
            pickupLayer = layerObject.GetComponent<RectTransform>();
            pickupLayer.anchorMin = Vector2.zero;
            pickupLayer.anchorMax = Vector2.one;
            pickupLayer.offsetMin = Vector2.zero;
            pickupLayer.offsetMax = Vector2.zero;
            pickupLayer.SetAsLastSibling();
        }

        private PlayerMotor ResolvePlayerMotor()
        {
            if (playerSpawner != null && playerSpawner.SpawnedPlayer != null)
            {
                return playerSpawner.SpawnedPlayer.GetComponent<PlayerMotor>();
            }

            return FindFirstObjectByType<PlayerMotor>();
        }

        private PlayerController ResolvePlayerController()
        {
            if (playerSpawner != null && playerSpawner.SpawnedPlayer != null)
            {
                return playerSpawner.SpawnedPlayer.GetComponent<PlayerController>();
            }

            return FindFirstObjectByType<PlayerController>();
        }

        private PlayerCharacterRuntime ResolvePlayerCharacterRuntime()
        {
            if (playerSpawner != null && playerSpawner.SpawnedPlayer != null)
            {
                return playerSpawner.SpawnedPlayer.GetComponent<PlayerCharacterRuntime>();
            }

            return FindFirstObjectByType<PlayerCharacterRuntime>();
        }

        private void EnsureReferences()
        {
            floorManager ??= FindFirstObjectByType<InfiniteFloorManager>();
            buildingGridUI ??= FindFirstObjectByType<BuildingGridUI>();
            elevatorController ??= FindFirstObjectByType<ElevatorController>();
            playerSpawner ??= FindFirstObjectByType<PlayerSpawner>();
            itemSpawner ??= FindFirstObjectByType<ItemSpawner>();
            topHUDController ??= FindFirstObjectByType<TopHUDController>();
            targetCanvas ??= FindFirstObjectByType<Canvas>();
        }

        private void Subscribe()
        {
            if (subscribed || floorManager == null)
            {
                return;
            }

            floorManager.CurrentFloorChanged += HandleCurrentFloorChanged;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || floorManager == null)
            {
                subscribed = false;
                return;
            }

            floorManager.CurrentFloorChanged -= HandleCurrentFloorChanged;
            subscribed = false;
        }

        private void CancelPendingSpawn()
        {
            if (pendingSpawnCoroutine == null)
            {
                return;
            }

            StopCoroutine(pendingSpawnCoroutine);
            pendingSpawnCoroutine = null;
        }

        private void DestroyCurrentPickup()
        {
            if (currentPickup == null)
            {
                return;
            }

            Destroy(currentPickup.gameObject);
            currentPickup = null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            minimumSpawnFloor = Mathf.Max(1, minimumSpawnFloor);
            baseSpawnChance = Mathf.Clamp01(baseSpawnChance);
            spawnChancePerMiss = Mathf.Clamp01(spawnChancePerMiss);
            maximumSpawnChance = Mathf.Clamp(maximumSpawnChance, baseSpawnChance, 1f);
            maximumBoostedSpawnChance = Mathf.Clamp(
                maximumBoostedSpawnChance,
                maximumSpawnChance,
                1f);
            guaranteedAttempt = Mathf.Max(1, guaranteedAttempt);
            spawnDelaySeconds = Mathf.Max(0f, spawnDelaySeconds);
            startElevatorClearanceColumns = Mathf.Max(0f, startElevatorClearanceColumns);
            completedLetterWeightMultiplier = Mathf.Clamp01(completedLetterWeightMultiplier);
            duplicateRepeatWeightMultiplier = Mathf.Clamp01(duplicateRepeatWeightMultiplier);
            duplicateSpawnChanceBonus = Mathf.Clamp01(duplicateSpawnChanceBonus);
            maximumDuplicateSpawnChanceBonus = Mathf.Clamp01(maximumDuplicateSpawnChanceBonus);
            duplicateTimeBonusSeconds = Mathf.Max(0f, duplicateTimeBonusSeconds);
            duplicateFeverGaugeBonus = Mathf.Max(0f, duplicateFeverGaugeBonus);
            stageAdvanceCount = Mathf.Max(1, stageAdvanceCount);
            floorStepDuration = Mathf.Max(0.01f, floorStepDuration);
            activationHoldSeconds = Mathf.Max(0f, activationHoldSeconds);
            pickupSize.x = Mathf.Max(1f, pickupSize.x);
            pickupSize.y = Mathf.Max(1f, pickupSize.y);
        }
#endif
    }
}
