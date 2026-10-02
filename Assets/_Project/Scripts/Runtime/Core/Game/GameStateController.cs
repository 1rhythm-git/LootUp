using System.Collections.Generic;
using LootUp.Core.Characters;
using LootUp.Core.Items;
using LootUp.Core.Leaderboard;
using LootUp.Core.Player;
using LootUp.Core.Rising;
using LootUp.Core.SceneFlow;
using LootUp.Core.UI;
using LootUp.Core.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LootUp.Core.Game
{
    public sealed class GameStateController : MonoBehaviour
    {
        [SerializeField]
        private TopHUDController topHUDController;

        [SerializeField]
        private PlayerSpawner playerSpawner;

        [SerializeField]
        private ElevatorController elevatorController;

        [SerializeField]
        private ItemSpawner itemSpawner;

        [SerializeField]
        private InfiniteFloorManager floorManager;

        [SerializeField]
        private RunItemEventRecorder itemEventRecorder;

        [SerializeField]
        private Canvas targetCanvas;

        [SerializeField]
        private Color overlayColor = new Color(0f, 0f, 0f, 0.58f);

        [SerializeField]
        private Color gameOverTextColor = Color.white;

        [SerializeField]
        private Color resultPanelColor = new Color(0.08f, 0.1f, 0.13f, 0.94f);

        [SerializeField]
        private Color confirmButtonColor = new Color(0.96f, 0.82f, 0.22f, 1f);

        [SerializeField]
        private Color confirmButtonTextColor = Color.black;

        [SerializeField]
        private int gameOverFontSize = 111;

        [SerializeField]
        private int resultFontSize = 48;

        [SerializeField]
        private bool clickToLobby = true;

        [SerializeField]
        private bool logGameOver = true;

        [SerializeField]
        private RunRewardSettings rewardSettings = new RunRewardSettings();

        private bool isGameOver;
        private bool exitRequested;
        private GameOverReason gameOverReason = GameOverReason.None;

        [SerializeField]
        private RunResultData lastRunResultData;

        // (추가) 정산과 결과 화면 책임은 전용 협력 객체에 위임한다.
        private RunResultService runResultService;
        private RunResultPresenter runResultPresenter;
        private RisingSystem risingSystem;

        public bool IsGameOver => isGameOver;
        public GameOverReason GameOverReason => gameOverReason;
        public RunResultData LastRunResultData => lastRunResultData;

        private void Awake()
        {
            EnsureReferences();
            EnsureCollaborators();
        }

        private void OnEnable()
        {
            EnsureReferences();
            EnsureCollaborators();

            if (topHUDController != null)
            {
                topHUDController.GameOverRequested += HandleGameOverRequested;
            }
        }

        private void OnDisable()
        {
            if (topHUDController != null)
            {
                topHUDController.GameOverRequested -= HandleGameOverRequested;
            }
        }

        public void RequestGameOver(GameOverReason reason)
        {
            if (isGameOver)
            {
                return;
            }

            isGameOver = true;
            gameOverReason = reason == GameOverReason.None ? GameOverReason.TimeOver : reason;

            EnsureReferences();
            EnsureCollaborators();
            lastRunResultData = runResultService.CreateResult(CreateRunResultContext(gameOverReason));
            _ = LeaderboardManager.SubmitRunAsync(lastRunResultData);

            StopInGameSystems();
            ShowGameOverOverlay();

            if (logGameOver)
            {
                Debug.Log($"Game Over: {gameOverReason}, highestFloor={lastRunResultData.HighestFloor}, score={lastRunResultData.Score}, items={lastRunResultData.AcquiredItemEvents.Count}", this);
            }
        }

        [ContextMenu("Debug/Game Over - Time Over")]
        private void DebugTimeOver()
        {
            RequestGameOver(GameOverReason.TimeOver);
        }

        [ContextMenu("Debug/Game Over - Life Depleted")]
        private void DebugLifeDepleted()
        {
            RequestGameOver(GameOverReason.LifeDepleted);
        }

        private void HandleGameOverRequested(GameOverReason reason)
        {
            RequestGameOver(reason);
        }

        public void ExitInGame()
        {
            if (exitRequested)
            {
                return;
            }

            exitRequested = true;
            EnsureCollaborators();
            runResultService.TrySettleRewards(lastRunResultData, ResolveActiveCharacterDefinition());

            if (!clickToLobby)
            {
                return;
            }

            if (SceneFlowManager.Instance != null)
            {
                SceneFlowManager.Instance.LoadLobby();
                return;
            }

            SceneManager.LoadScene(SceneFlowManager.LobbySceneName, LoadSceneMode.Single);
        }

        private void StopInGameSystems()
        {
            if (topHUDController != null)
            {
                topHUDController.SetTimerPaused(true);
                topHUDController.SetItemStatus($"GAME OVER - {gameOverReason}");
            }

            PlayerController playerController = ResolvePlayerController();
            if (playerController != null)
            {
                playerController.SetControlEnabled(false);
            }

            PlayerMotor playerMotor = ResolvePlayerMotor();
            if (playerMotor != null)
            {
                playerMotor.SetMovementLocked(true);
            }

            if (elevatorController != null)
            {
                elevatorController.enabled = false;
            }

            if (itemSpawner != null)
            {
                itemSpawner.enabled = false;
            }

            PauseGameplayParticipants();
        }

        private static void PauseGameplayParticipants()
        {
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IGameplayPausable pausable)
                {
                    pausable.SetGameplayPaused(true);
                }
            }
        }

        // (변경) 컨트롤러는 현재 런 상태만 수집하고 결과 계산은 서비스에 맡긴다.
        private RunResultContext CreateRunResultContext(GameOverReason reason)
        {
            int highestFloor = floorManager != null ? floorManager.RunHighestFloor : 1;
            int startFloor = floorManager != null ? floorManager.StartAbsoluteFloor : 1;
            int score = topHUDController != null ? topHUDController.CurrentScore : 0;
            float remainingSeconds = topHUDController != null ? topHUDController.RemainingSeconds : 0f;
            int remainingHearts = topHUDController != null ? topHUDController.CurrentHearts : 0;
            int acquiredGameMoney = topHUDController != null ? topHUDController.CurrentRunGameMoney : 0;
            IReadOnlyList<ItemRunEvent> acquiredItemEvents = itemEventRecorder != null
                ? itemEventRecorder.AcquiredItemEvents
                : null;

            CharacterDefinition characterDefinition = ResolveActiveCharacterDefinition();
            CharacterProgressionSnapshot progression = CharacterProgressionState.GetSnapshot(characterDefinition);

            return new RunResultContext(
                reason,
                highestFloor,
                startFloor,
                score,
                remainingSeconds,
                remainingHearts,
                acquiredGameMoney,
                characterDefinition,
                progression.Level,
                acquiredItemEvents);
        }

        private void ShowGameOverOverlay()
        {
            Canvas canvas = ResolveTargetCanvas();
            if (canvas == null)
            {
                return;
            }

            EnsureCollaborators();
            runResultPresenter.Show(canvas, lastRunResultData, ExitInGame);
        }

        private PlayerController ResolvePlayerController()
        {
            if (playerSpawner != null && playerSpawner.SpawnedPlayer != null)
            {
                return playerSpawner.SpawnedPlayer.GetComponent<PlayerController>();
            }

            return FindFirstObjectByType<PlayerController>();
        }

        private PlayerMotor ResolvePlayerMotor()
        {
            if (playerSpawner != null && playerSpawner.SpawnedPlayer != null)
            {
                return playerSpawner.SpawnedPlayer.GetComponent<PlayerMotor>();
            }

            return FindFirstObjectByType<PlayerMotor>();
        }

        private CharacterDefinition ResolveActiveCharacterDefinition()
        {
            if (topHUDController != null && topHUDController.ActiveCharacterDefinition != null)
            {
                return topHUDController.ActiveCharacterDefinition;
            }

            PlayerController playerController = ResolvePlayerController();
            PlayerCharacterRuntime characterRuntime = playerController != null
                ? playerController.GetComponent<PlayerCharacterRuntime>()
                : FindFirstObjectByType<PlayerCharacterRuntime>();
            return characterRuntime != null ? characterRuntime.CharacterDefinition : null;
        }

        private void EnsureReferences()
        {
            if (topHUDController == null)
            {
                topHUDController = FindFirstObjectByType<TopHUDController>();
            }

            if (playerSpawner == null)
            {
                playerSpawner = FindFirstObjectByType<PlayerSpawner>();
            }

            if (elevatorController == null)
            {
                elevatorController = FindFirstObjectByType<ElevatorController>();
            }

            if (itemSpawner == null)
            {
                itemSpawner = FindFirstObjectByType<ItemSpawner>();
            }

            if (floorManager == null)
            {
                floorManager = FindFirstObjectByType<InfiniteFloorManager>();
            }

            if (itemEventRecorder == null)
            {
                itemEventRecorder = FindFirstObjectByType<RunItemEventRecorder>();
            }

            if (targetCanvas == null)
            {
                targetCanvas = FindFirstObjectByType<Canvas>();
            }
        }

        // (추가) Scene 변경 없이 일반 C# 협력 객체를 런타임에 구성한다.
        private void EnsureCollaborators()
        {
            runResultService ??= new RunResultService(rewardSettings);
            runResultPresenter ??= new RunResultPresenter(
                new RunResultPresenterSettings(
                    overlayColor,
                    gameOverTextColor,
                    resultPanelColor,
                    confirmButtonColor,
                    confirmButtonTextColor,
                    gameOverFontSize,
                    resultFontSize));

            risingSystem ??= GetComponent<RisingSystem>();
            if (risingSystem == null)
            {
                risingSystem = gameObject.AddComponent<RisingSystem>();
            }

            risingSystem.Configure(
                floorManager,
                FindFirstObjectByType<BuildingGridUI>(),
                elevatorController,
                playerSpawner,
                itemSpawner,
                topHUDController,
                targetCanvas);
        }

        private Canvas ResolveTargetCanvas()
        {
            if (targetCanvas == null)
            {
                targetCanvas = FindFirstObjectByType<Canvas>();
            }

            return targetCanvas;
        }
    }
}
