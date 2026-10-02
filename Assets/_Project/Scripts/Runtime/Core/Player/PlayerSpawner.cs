using LootUp.Core.Characters;
using LootUp.Core.World;
using UnityEngine;

namespace LootUp.Core.Player
{
    [DefaultExecutionOrder(10000)]
    public sealed class PlayerSpawner : MonoBehaviour
    {
        [SerializeField]
        private bool spawnOnStart = true;

        [SerializeField]
        private BuildingGridUI buildingGridUI;

        [SerializeField]
        private InfiniteFloorManager floorManager;

        [SerializeField]
        private RectTransform playerLayer;

        [SerializeField]
        private RectTransform touchArea;

        [SerializeField]
        private GameObject playerPrefab;

        [SerializeField]
        private CharacterDefinition characterDefinition;

        [SerializeField]
        private int startColumn;

        [SerializeField]
        private Vector2 playerSize = new Vector2(61f, 72f);

        [SerializeField]
        private Color playerColor = new Color(0.95f, 0.78f, 0.22f, 1f);

        [SerializeField]
        private Color playerOutlineColor = new Color(0.05f, 0.05f, 0.06f, 0.85f);

        private GameObject spawnedPlayer;
        private CharacterDefinition activeCharacterDefinition;
        private PlayerRuntimeBinder runtimeBinder;

        public GameObject SpawnedPlayer => spawnedPlayer;

        private void Awake()
        {
            EnsureReferences();
            EnsureCollaborators();
        }

        private void Start()
        {
            if (spawnOnStart)
            {
                SpawnPlayer();
            }
        }

        private void LateUpdate()
        {
            // 아티팩트와 다른 런타임 레이어 정렬이 끝난 뒤 PlayerLayer를 최상단에 유지한다.
            playerLayer?.SetAsLastSibling();
        }

        [ContextMenu("Debug/Spawn Player")]
        public void SpawnPlayer()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Player는 Play 모드에서만 런타임 생성합니다.", this);
                return;
            }

            EnsureReferences();
            EnsureCollaborators();

            if (spawnedPlayer != null)
            {
                Destroy(spawnedPlayer);
            }

            if (playerLayer == null || buildingGridUI == null || floorManager == null)
            {
                Debug.LogWarning("PlayerSpawner 참조가 부족해 Player를 생성할 수 없습니다.", this);
                return;
            }

            activeCharacterDefinition = CharacterSelectionState.Resolve(characterDefinition);

            // (변경) 생성과 컴포넌트 조립은 Factory에 위임한다.
            PlayerRuntimeFactory factory = new PlayerRuntimeFactory(
                playerLayer,
                playerPrefab,
                playerSize,
                playerColor,
                playerOutlineColor,
                this);
            PlayerRuntimeComponents components = factory.Create(activeCharacterDefinition);
            spawnedPlayer = components.Root;

            // (변경) HUD, Health, 이동 및 Fever 연결은 Binder에 위임한다.
            runtimeBinder.Bind(
                gameObject,
                components,
                activeCharacterDefinition,
                touchArea,
                buildingGridUI,
                floorManager,
                startColumn);
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

            if (touchArea == null && buildingGridUI != null)
            {
                touchArea = buildingGridUI.transform.parent as RectTransform;
            }
        }

        // (추가) 치트 입력과 런타임 연결 객체를 Scene 변경 없이 구성한다.
        private void EnsureCollaborators()
        {
            runtimeBinder ??= new PlayerRuntimeBinder();

            PlayerDebugInput debugInput = GetComponent<PlayerDebugInput>();
            if (debugInput == null)
            {
                debugInput = gameObject.AddComponent<PlayerDebugInput>();
            }

            debugInput.Configure(() => spawnedPlayer, this);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            startColumn = Mathf.Max(0, startColumn);
            playerSize.x = Mathf.Max(1f, playerSize.x);
            playerSize.y = Mathf.Max(1f, playerSize.y);
        }
#endif
    }
}
