LootUp Codex Execution Plan
________________________________________
공통 작업 규칙
모든 파트에서 다음 규칙을 지킨다.
1.	Unity 6.3 LTS 기준으로 작성한다.
2.	Input System New만 사용한다.
3.	기존 기능을 임의로 삭제하지 않는다.
4.	아직 구현하지 않은 기능을 임시로 과도하게 연결하지 않는다.
5.	각 파트 완료 후 컴파일 에러를 확인한다.
6.	Inspector 연결 항목을 보고한다.
7.	새로 생성하거나 수정한 파일 목록을 보고한다.
8.	대규모 리팩터링을 하지 않는다.
9.	클래스 책임을 분리한다.
10.	코드 주석은 한국어로 작성한다.
11.	작업 완료 후 `05_WORK_LOG.md`에 완료 내용, 고려 요소, 리팩터링 타이밍을 기록한다.
12.	전체 공정률 100%는 Google Play 실제 출시 완료를 기준으로 하며 기능 구현률과 구분한다.

출시 공정률 기준
•	2026-07-21 현재 Google Play 출시 기준 공정률은 약 45%이다.
•	핵심 게임 루프와 주요 UI 흐름 이후에도 영구 저장, 온라인 기능, 수익화, Android 실기기 QA, 배포 및 스토어 심사가 완료되어야 100%로 판단한다.
•	캐릭터별 XP/레벨과 선택/보유/장착 상태의 로컬 저장 통합은 완료되었다.
•	재시작 최우선: `LootUpBest` 콘솔 생성 후 계정별 BEST와 일일 리더보드 초기화 회귀를 검증한다.
•	1차 범위는 `GameStateController`의 런 정산/결과 UI 분리, `PlayerSpawner`의 치트 입력/생성/런타임 연결 분리, `ItemSpawner`의 Page 배치/수집형 드랍/UI 생성 분리이다.
•	리팩터링은 동작을 변경하지 않는 단계별 작업으로 진행하고 각 단계마다 Unity 컴파일과 기존 게임 흐름을 회귀 검증한다.
________________________________________
PART 1
프로젝트 베이스 구축
목표
빈 프로젝트에 기본 폴더, 씬, 공통 구조를 만든다.
작업
•	Assets/_Project 폴더 구조 생성
•	Loading 씬 생성
•	Title 씬 생성
•	Lobby 씬 생성
•	InGame 씬 생성
•	기본 Canvas 구조
•	GameRoot
•	Managers
•	EventSystem
•	Main Camera
•	SceneFlowManager
•	RuntimeBootstrapper
•	TitleSceneController
아직 구현하지 않을 것
•	플레이어 이동
•	적
•	아이템
•	서버 SDK
•	랭킹
•	상세 UI 연출
완료 조건
•	Loading, Title, Lobby, InGame 네 씬이 정상적으로 열림
•	씬 전환 가능
•	컴파일 에러 없음
구현 상태
•	완료
현행 메모
•	시작 흐름은 `Loading → Title → Lobby → InGame` 순서이다.
•	Loading은 로딩바 없이 기존 대기와 LAF 로고 강조 기능을 유지한다.
•	Loading 대기 시간은 1.5초이며 LAF 로고 하이라이트 유지 시간은 기존 0.8초를 유지한다.
•	Title은 `Title.png`를 표시하고 Lobby 비동기 로딩 진행률이 100%가 되면 점멸하는 `TOUCH` 입력을 활성화한다.
•	Lobby 전체 화면 배경은 `Lobby.png`를 사용한다.
________________________________________
PART 2
그리드와 무한 층 기반
목표
8×10 그리드와 절대 층수 구조를 구현한다.
작업
•	BuildingGridUI
•	GridCell
•	FloorPageGenerator
•	InfiniteFloorManager
•	수평선 11개
•	좌하단 원점
•	절대 층수
•	내부 페이지 인덱스
•	10층 단위 페이지 전환 기반
완료 조건
•	8×10 셀이 정확히 생성됨
•	Cell_0_0이 왼쪽 아래
•	1~10층이 정상 표시됨
•	다음 페이지 데이터 계산 가능
•	Stage Clear 개념 없음
________________________________________
PART 3
가시성 시스템
목표
현재 층, 지난 층, 미래 층의 밝기를 구분한다.
작업
•	FloorVisibilityController
•	Current Floor 상태
•	Past Floor 상태
•	Future Floor 상태
•	GridPanel과 정확히 정렬되는 가시성 레이어
완료 조건
•	1층 시작 시 1층이 밝음
•	위층은 검게 표시
•	층 상승 시 이전 층은 약간 어두워짐
________________________________________
PART 4
플레이어 생성과 좌우 이동
목표
Lobby 선택값에 따라 플레이어를 런타임 생성하고 좌우 이동시킨다.
작업
•	PlayerSpawner
•	PlayerController
•	PlayerMotor
•	Input System
•	터치 입력 확장 가능 구조
•	시작 셀 배치
•	좌우 이동
•	층 경계 제한
완료 조건
•	씬에 Player가 미리 배치되지 않음
•	런타임 생성
•	좌우 이동 가능
•	화면 밖으로 나가지 않음
현행 및 전환 메모
•	UI에는 이동/방향전환 피버 획득량을 분리하지 않고 `피버 충전` 단일 스테이터스로 표시한다.
•	`피버 충전` UI 값은 Agent X 100 기준 상대 지수이며 이동속도는 포함하지 않는다.
•	`FeverBalanceSettings`에 공통 이동 기본 획득량 0.15와 방향전환 배율 1.5를 둔다.
•	`CharacterDefinition`에는 `FeverGainMultiplier`만 두고 실제 이동/전환 획득량을 공통 설정에서 계산한다.
•	기존 결과값은 Agent X 1.0, Alice 1.5, Landy 1.2, Ninja 2.0 배율로 동일하게 유지한다.
•	Lobby 기본 능력치는 `SPEED`, `REFLEX`, `VITALITY`, `FEVER DRIVE`, `ITEM LUCK`, `AWAKENING`의 6개 항목을 이름/값 정렬 목록으로 표시한다.
•	`SPEED`, `REFLEX`, `FEVER DRIVE`는 Agent X를 100으로 보는 상대 지수이며, `REFLEX`는 방향전환 대기시간에 역비례한다.
•	`VITALITY`는 최대 생명력, `ITEM LUCK`은 실제 확률(%), `AWAKENING`은 스킬 해금 레벨을 표시한다.
•	기본 캐릭터 내부 ID `default`는 유지하고 Lobby 표시명은 `Agent X`를 사용한다.
•	공통 피버 설정과 캐릭터별 배율 데이터 구조 전환은 캐릭터 강화 능력치 확정 시 진행한다.
피버타임 현행 메모
•	게이지 100% 도달 즉시 방향전환이나 추가 입력 없이 자동 발동한다.
•	기본 지속시간은 8초이며 활성 중 추가 충전을 중지하고 종료 후 0%부터 다시 충전한다.
•	현재 활성 상태인 일반 드롭 아이템 셀을 제외한 모든 빈 셀에 Pass 미적용 피버 골드바를 배치한다.
•	피버 중 일반 아이템이 획득 또는 만료되면 새로 빈 셀이 된 위치에 피버 골드바를 배치한다.
•	페이지 전환 중에도 효과를 유지하고 종료 시 미획득 피버 골드바를 모두 제거한다.
•	피버 골드바는 층별 가림 효과 밖의 게임 필드 최상위 레이어에 표시한다.
캐릭터 스킬 현행 메모
•	`CharacterSkillDefinition` 에셋에서 해금 레벨, 발동 조건, 효과 종류, 설명과 P1~P5를 관리한다.
•	`CharacterSkillRuntime`은 런타임 생성 Player에 자동 부착하고 아이템 기본 효과 이후 스킬을 한 번 판정한다.
•	스킬 효과는 `ICharacterSkillEffect` 구현체로 분리한다.
•	스킬 발동 텍스트는 `PlayerItemPickupFeedback`의 공용 상승·페이드 연출을 재사용한다. 아이템 연계 발동은 아이템 텍스트 위쪽, 비아이템 연계 발동은 기본 위치에 표시한다.
•	모든 캐릭터 최대 레벨은 Lv.99로 통일하고 향후 성장 및 능력치 밸런스는 Max Lv.99를 기준으로 조율한다.
•	Agent X Lv.5, Landy/Alice Lv.15, Ninja Lv.20 해금 구간은 현재 개별 성장 테이블 값을 유지하며, Lv.20 이후 미설정 구간은 마지막 값을 사용한다.
•	Lobby 스킬 설명은 P1~P5를 현재 에셋 값으로 치환해 표시하며 강화/상세 조작은 후속 작업으로 남긴다.
Lobby 디자인 현행 메모
•	`concept/Lobby/Lobby_Design.png`의 세로 구성을 기준으로 상단 프로필/재화, BEST, 캐릭터, START, 메뉴, 광고 순서로 배치한다.
•	하단 메뉴는 `MISSION`, `MAIL BOX`, `UPGRADE`, `ARTIFACT`, `SHOP`, `LANK` 6개이다.
•	Lobby `BEST`는 계정 전체 기간 누적 최고 기록이며 `gamerInDate`별 로컬 프로필과 Private `LootUpBest` 테이블에 유지한다.
•	`LANK`는 현재 리더보드 기간의 BackND 계정별 기록 기반 `FLOOR`, `SCORE` 탭과 MY LANK 및 전체 목록을 제공한다.
•	MY LANK에는 기록 캐릭터 얼굴 초상화와 레벨, 전체 목록에는 기록 캐릭터 전신 초상화와 레벨을 표시한다.
•	LANK 순위는 최고 도달 층, 스코어, 플레이 당시 캐릭터 레벨 순으로 비교한다.
•	`ILeaderboardService`로 MY LANK와 전역 상위 5개 기록을 조회하며 로딩, 빈 결과, 오류, 재시도 상태를 표시한다.
•	설정 버튼도 아이콘과 입력 상태만 구성하고 기능은 후속 작업으로 연결한다.
________________________________________
PART 5
엘리베이터와 층 상승
목표
플레이어가 다음 층으로 이동할 수 있도록 한다.
작업
•	ElevatorController
•	층 이동 상태
•	현재 층 증가
•	페이지 경계 전환
•	현재 층 UI 갱신
•	최고 도달 층 갱신
완료 조건
•	1층에서 2층 이동
•	10층에서 11층 이동
•	페이지 인덱스 갱신
•	게임이 종료되지 않고 계속 진행
________________________________________
PART 6
하트, 리스폰, 게임 오버
목표
하트 3개와 사망 구조를 구현한다.
작업
•	CharacterDefinition
•	캐릭터별 MaxLife 적용
•	PlayerHealth
•	PlayerRespawnController
•	Normal 모드
•	Hard 모드
•	GameStateController
•	GameOverUI
•	결과창 광고보기 버튼 확장 지점
•	Google AdMob 보상형 광고 부활 정책 준비
•	시간 제한
완료 조건
•	피격 시 하트 감소
•	하트가 남으면 리스폰
•	하트 0이면 게임 종료
•	시간 0이면 게임 종료
•	게임 오버 시 최고 층 확정
•	광고 부활은 아직 구현하지 않더라도 결과창/상태 전환 구조에서 확장 가능
________________________________________
PART 7
점수 시스템
목표
층 기록과 점수를 분리하고, Game Over 기준 런 종료 점수 계산 규칙을 준비한다.
작업
•	ScoreManager
•	Current Run Score
•	Best Score
•	Run Highest Floor
•	Best Highest Floor
•	점수 이벤트 API
•	GameOver 결과 데이터
•	광고 부활 사용 여부 필드 확장 준비
•	Game Over 시 runHighestFloor 확정
•	RunScoreResult에 runHighestFloor 포함
•	Gameplay Score 반영
•	Floor Score 계산: floorMoveCount × floorScoreValue
•	Life Score 계산: remainingHearts × lifeScorePerHeart
•	Game Over 총점 계산: Gameplay Score + Floor Score + Life Score
•	캐릭터 레벨 기본 XP, 층 XP, Total Score 보너스 XP 계산
•	런 획득 게임머니와 Total Score 보너스 게임머니 계산
•	ScoreBalanceData 또는 Inspector 설정으로 점수 계수 관리
•	RunScoreResult 데이터 구조
완료 조건
•	층수와 점수가 별도로 표시됨
•	점수 추가 API가 준비됨
•	런 종료 결과 생성 가능
•	Game Over 결과 데이터에 runHighestFloor가 반드시 포함됨
•	Game Over 기준 총점이 생성됨
•	추후 AdMob 보상형 광고 부활 사용 여부를 저장/랭킹 데이터에 포함 가능
•	보너스별 점수 breakdown을 UI와 저장 데이터에 전달 가능
•	게임머니와 캐릭터 XP를 결과 확정 시 한 번만 지급
•	결과창에 점수, XP, 게임머니 breakdown을 순서대로 표시
•	결과창 글꼴은 `GAME OVER` 111, 결과 상세 48, `CONFIRM` 51 적용
검토 메모
•	Time Bonus는 기본 점수 공식에서 제외하고 Life Score는 포함한다.
•	캐릭터별 이동속도, 방향전환 쿨타임, 아이템 즉시 획득 확률은 점수/랭킹 검증에 영향을 줄 수 있으므로 결과 데이터에 캐릭터 ID를 포함할 수 있어야 한다.
•	무한 상승 구조라도 Game Over 시 해당 런의 최고 도달 층인 runHighestFloor를 확정한다.
•	Line Bonus와 세로 경계 통과 기반 점수는 사용하지 않는다.
구현 상태
•	완료
현행 메모
•	`RunRewardSettings`에서 층 점수, 생명력 점수, 층 XP, 점수 XP 배율, 보너스 게임머니 배율을 조정한다.
•	TopUI는 보유 게임머니, 보유 Ruby, 현재 런 획득 게임머니를 분리해 표시한다.
•	Lobby XP 게이지는 현재 XP 비율을 RectTransform 폭으로 반영하며 XP 0에서는 비어 있다.
•	최고 기록에는 캐릭터 ID와 플레이 당시 레벨을 함께 저장한다.
•	최고 기록 갱신은 도달 층수, 스코어, 캐릭터 레벨 순으로 비교한다.
________________________________________
PART 8
적 기본 시스템
목표
페이지당 적 7개를 생성하고 수직 이동시킨다.
작업
•	EnemyStageSpawner
•	EnemyPatrol
•	EnemyCollisionHandler
•	EnemyRoot
•	상단 생성
•	위아래 이동
•	페이지 전환 시 정리
완료 조건
•	적 7개 생성
•	수직 이동
•	플레이어 충돌 시 피격
•	UI 요소에 가려지지 않음
•	Game Over 결과창 표시 시 Enemy 이동/충돌/생성 정지
현행 메모
•	현재 구현은 정식 Enemy 클래스가 아니라 `TestEnemySpawner`, `TestEnemyHazard` 기반이다.
•	Enemy 이미지는 경찰 단일 PNG를 사용한다.
•	히트박스 디버그 표시는 테스트용이며 일반 플레이에서는 숨긴다.
•	일부 Enemy Line은 생성 시 흰색으로 시작하고 Enemy가 천장에 도달할 때마다 흰색 비공격과 붉은 공격 상태를 반복한다.
•	붉은 상태에서 플레이어가 접촉하면 기존 Enemy 피격 흐름으로 생명력을 감소시키며, 바닥 충돌에서는 색상과 공격 상태를 변경하지 않는다.
•	`TestEnemySpawner`에서 위험 라인 순환 대상의 시작 Page, 첫 적용 수, 증가 주기, 주기당 증가 수와 최대 적용 수를 설정한다.
•	기본 난이도는 Page 2부터 1개, 2 Page마다 1개 증가, Page당 최대 4개이다.
________________________________________
PART 9
EnemyTrail
목표
적의 이동에 따라 수직 라인을 생성하고 삭제한다.
작업
•	EnemyTrailLineController
•	내려갈 때 라인 증가
•	올라갈 때 라인 감소
•	상하단 Clamp
•	수평선 접합
완료 조건
•	적 이동과 라인이 동기화됨
•	상단과 하단을 넘지 않음
•	라인 단절 없음
구현 상태
•	완료
•	현재 `TestEnemyHazard`가 Enemy 머리부터 상단까지 가이드라인을 생성하고 이동에 맞춰 길이를 갱신한다.
•	일반 라인과 위험 대상의 흰색 상태는 비공격 판정이며 붉은 상태의 실제 표시 Rect 접촉만 피해를 준다.
•	전용 `EnemyTrailLineController` 분리는 정식 Enemy 구조 전환 시 진행할 리팩터링 항목이다.
________________________________________
PART 10
아이템 데이터 기반
목표
아이템 데이터를 테이블 또는 ScriptableObject 기반으로 정의한다.
작업
•	ItemDefinition
•	ItemCategory
•	ItemPassDirection
•	ItemRarity
•	ItemInstance
•	Item ID 검증
•	Required Pass Count
•	EffectKey
•	EffectValue
•	EffectDurationSeconds
완료 조건
•	CSV 또는 Inspector에서 아이템 데이터 관리 가능
•	Score, Skill, Collection 타입 선택 가능
•	통과 횟수 설정 가능
•	아이템이 필드에 남아있는 시간과 획득 후 버프 지속시간을 분리 가능
현행 메모
•	현재 구현은 `Items.csv`, `ItemIcons.csv` 기반이다.
•	`LifetimeSeconds`는 필드 수명, `EffectDurationSeconds`는 효과 지속시간으로 사용한다.
________________________________________
PART 11
아이템 통과 판정
목표
플레이어가 아이템을 여러 번 통과해 획득하도록 한다.
작업
•	ItemPassDetector
•	Enter/Exit 기반 판정
•	내부 체류 중 중복 방지
•	좌우 양방향 인정
•	Current Pass Count
•	Required Pass Count
•	ItemProgressView
완료 조건
•	1회 아이템 정상 획득
•	3회 아이템은 정확히 3번째 통과에 획득
•	콜라이더 내부 체류 중 횟수 증가하지 않음
•	통과 카운트 시 SFX와 남은 횟수 UI가 갱신됨
________________________________________
PART 12
스코어형 아이템
목표
스코어형 아이템 효과를 구현한다.
작업
•	IItemEffect
•	AddScoreEffect
•	ScoreMultiplierEffect
•	Combo 연동 가능 구조
•	획득 연출 이벤트
완료 조건
•	획득 즉시 점수 반영
•	중복 획득 방지
•	효과가 ItemInstance와 분리됨
현행 메모
•	Score Coin과 Pass Orb가 점수형 아이템으로 동작한다.
•	Pass Orb는 보석 아이콘을 사용하고 3회 통과 후 획득한다.
•	Score 계열 아이템은 현재 스폰 시 1~5회 랜덤 통과 카운트를 받고, 추가 통과 1회당 +25% 점수 보정을 받는다.
•	Time 계열 아이템은 현재 스폰 시 1~5회 랜덤 통과 카운트를 받고, `EffectValue x 통과 카운트`만큼 시간을 증가시킨다.
________________________________________
PART 13
스킬형 아이템
목표
플레이어에게 영향을 주는 스킬형 아이템 효과 기반을 구현한다.
현재 범위
•	하트 회복
•	Max Life 증가
•	시간 증가
•	플레이어 이동 속도 증가
•	피버 게이지 즉시 충전
작업
•	IItemEffect / ItemEffectResolver
•	지속 시간
•	중첩 정책
•	효과 종료
•	버프 지속 중 플레이어 시각 효과
완료 조건
•	효과 시작과 종료가 정확함
•	중복 효과 정책이 작동함
•	게임 오버 시 효과 정리
구현 상태
•	완료
현행 메모
•	현재 구현된 스킬형 아이템은 `Red Sneaker`, `Winged Shoe`, `Winged Heart`, `Fever Battery`이다.
•	이동속도 증가는 `AddMoveSpeedItemEffect`로 처리한다.
•	이동속도 증가는 영구 적용하지 않으며, 기본 5초에 스폰 시 부여된 1~3 카운트를 곱해 5초, 10초, 15초 동안 적용한다.
•	생명력이 실제로 차감되면 활성 이동속도 효과를 즉시 제거하고 캐릭터 기본 이동속도로 복원한다.
•	효과 지속 중 `PlayerBuffVisualFeedback`으로 캐릭터 점멸을 표시한다.
•	활성 이동속도 버프는 현재 퍼센트 합산 방식으로 계산한다.
•	Max Life 증가는 `AddMaxLifeItemEffect`로 처리하며, 현재 런에서 최대 +1까지만 허용한다.
•	Heart Pack은 3회, Winged Heart는 5회 통과 후 획득한다.
•	Fever Battery는 스폰 시 카운트 1/3/5 중 하나를 부여하고 각각 피버 게이지를 5%/15%/30% 충전하며, 100% 도달 시 기존 피버 자동 발동 경로를 사용한다.
•	Enemy 일시 정지 아이템은 필요성이 낮아 기획 범위에서 제외한다.
•	무적, Enemy 감속 등 추가 효과와 세부 중첩 제한은 추후 레벨 디자인 및 밸런스 단계에서 검토한다.
________________________________________
PART 14
수집형 아이템
목표
장기 저장되는 수집 데이터를 구현한다.
작업
•	ItemCollectionManager
•	CollectionData
•	고유 수집 아이템
•	수량형 수집 아이템
•	업적 진행도 API
•	패시브 강화 연동 포인트
완료 조건
•	획득 데이터가 런 종료 후에도 유지됨
•	중복 아이템 정책 작동
•	저장 서비스와 분리됨
구현 상태
•	Artifact 16종과 8종 조합 효과의 데이터 기반 구현 완료
•	모든 Artifact Pass Count 10, 미보유 Golden Cup 3번째 Page 확정 배치 완료
•	Artifact 전용 최상위 레이어, 최초 획득 후 Lobby 메뉴 해금과 Archive UI 완료
•	Archive는 `EFFECTS`를 기본 화면으로 표시하고 `CHAMPION RECORD`를 첫 효과로 노출
•	CharacterCoin 실제 콘텐츠와 강화 밸런스는 후속 작업
________________________________________
PART 15
아이템 스폰
목표
셀 내부에 아이템을 안전하게 배치한다.
작업
•	ItemSpawnManager
•	셀 기반 스폰
•	시작 셀 제외
•	엘리베이터 셀 제외
•	적 중복 위치 방지
•	층별 등장 조건
•	등장 가중치
•	실행별 랜덤 시드
완료 조건
•	도달 가능한 셀에만 아이템 생성
•	한 셀에 기본 1개
•	페이지 전환 시 아이템 정리
•	실행마다 동일한 배치가 반복되지 않음
현행 메모
•	현재 `ItemSpawner`는 `randomizeSeedOnStart`가 켜져 있으면 실행마다 `runtimeSeed`를 새로 만든다.
•	페이지별 난수는 `runtimeSeed`와 page index를 섞어 생성한다.
•	재현 테스트가 필요하면 `randomizeSeedOnStart`를 끄고 고정 `randomSeed`를 사용한다.
________________________________________
PART 16
로컬 저장 서비스
목표
뒤끝 연결 전 로컬 저장 구조를 만든다.
작업
•	IDataSaveService
•	ILeaderboardService
•	IAchievementService
•	IInventoryService
•	LocalDataSaveService
•	LocalLeaderboardService
•	LocalAchievementService
•	LocalInventoryService
•	데이터 버전
완료 조건
•	최고 층 저장
•	최고 점수 저장
•	수집형 아이템 저장
•	선택 캐릭터와 게임 모드 저장
•	캐릭터별 레벨, 경험치, 보유 및 장착 상태 저장
•	게임 코드가 구체 저장 클래스를 직접 호출하지 않음
구현 상태
•	프로필 재화와 수집/강화 데이터의 로컬 저장 기반은 구현됨
•	캐릭터별 레벨/경험치와 선택/보유/장착 상태를 `ICharacterProgressionService` 뒤에 분리해 로컬 저장 완료
•	캐릭터 진행 저장 키를 `LootUp.CharacterProgression.v2`와 `LootUp.CharacterProgression.Account.v2.{gamerInDate}`로 분리
•	수집/강화 저장 키를 `LootUp.CollectionProgress.v1`과 `LootUp.CollectionProgress.Account.v2.{gamerInDate}`로 분리
•	기존 공용 수집 데이터는 최초 로그인 계정 한 곳만 승계하고 MigrationOwner로 중복 이전 차단
•	Ninja의 과거 ID `triangle_low_spec`는 로드 시 `ninja`로 변환하고 진행/선택/장착/강화 데이터를 병합
•	프로필/인증/캐릭터 진행/수집 저장 키는 `LootUp.*` 형식을 사용
•	테스트 초기화를 위해 캐릭터 진행 `v1`은 자동 이전하지 않으며 다른 구 키는 각 저장 서비스 정책에 따라 처리
•	최초 캐릭터 데이터는 캐릭터 에셋의 `InitiallyOwned` 기준으로 생성
•	레벨별 필요 XP와 기본 런 XP는 `CharacterDefinition` 에셋에서 계속 관리하여 추후 레벨 디자인 변경 가능
•	최고 층, 최고 점수, 기록 캐릭터 ID와 플레이 당시 레벨의 로컬 저장 완료
•	`ICurrencyLedgerService`와 `CurrencyLedgerManager`를 추가해 재화 서버 권한, Pending Queue, 최초 이관 경계 구현
•	런 결과 게임머니 지급을 고유 RunId 기반 서버 원장 요청으로 전환
•	선택 게임 모드와 직전 런 기록의 통합 저장은 후속 작업
________________________________________
PART 17
뒤끝 서버 연결
목표
BackND SDK를 서비스 경계 뒤에 연결하고 계정별 인증과 LANK를 제공한다.
작업
•	BackND 서비스 어댑터
•	서버 호출 인터페이스
•	실패 처리
•	계정별 캐릭터 성장 저장소
•	게임 종료 기록 제출과 순위 조회
구현 상태
•	BackND 5.18.3 SDK 초기화와 Android 패키지명 및 Google Hash 설정 완료
•	`IAuthenticationService`에 Custom 회원가입, 수동 로그인, 로그아웃 계약 구현
•	`AuthenticationManager`에 `SignedOut`, `Authenticating`, `Authenticated`, `Failed` 상태와 변경 이벤트 구현
•	Title에서 Lobby를 사전 로드하되 앱 시작마다 로그인 UI를 표시하고 자동 로그인하지 않음
•	Title 로그인 UI는 Account ID/Password만 요구하고, 신규 가입은 Account ID/Password 1단계와 Nickname 확인 2단계로 분리
•	`REMEMBER ID / PW` 선택 시 ID와 마스킹된 Password 입력값만 복원하고 사용자가 직접 로그인
•	로그인 성공 시 `gamerInDate`별 캐릭터 진행·수집/강화 저장소와 LANK 서비스를 구성
•	로그인 성공 시 `gamerInDate`별 사용자 프로필을 구성하고 `LootUpBest`의 계정 누적 최고 기록과 동기화
•	Private `LootUpRank` 테이블과 `LootUp Global Rank` 유저 리더보드 연동
•	Private `LootUpBest` 누적 최고 기록과 기간용 `LootUpRank`를 분리
•	Private `LootUpPlayerProfile`, `LootUpCurrencyLedger` 생성 및 실제 계정의 최초 이관, Pending 재전송, 중복 요청 방지 검증 완료
•	게임 종료 기록 제출, MY LANK, 전역 상위 5개 기록 조회 및 오류/재시도 구현
•	리더보드 초기화 후 MY LANK가 없으면 이전 테이블 기록 비교를 건너뛰고 첫 플레이 기록을 다시 등록
•	현재 초기화 주기에 등록된 이후에는 도달 층수, 스코어, 캐릭터 레벨 순으로 최고 기록 유지
•	향후 기간 종료 시 순위 스냅샷, 보상 지급 및 중복 지급 방지 완료 후 이전 기간 기록 삭제
후속 작업
•	Google 로그인
•	오프라인 동기화 큐와 서버 기록 위변조 검증
•	기간 랭킹 보상 테이블과 지급 Function 구현
완료 조건
•	로컬 구현체를 뒤끝 구현체로 교체할 수 있는 구조
•	Gameplay 코드 수정 없이 서비스 교체 가능
•	Android 실제 계정별 로그인, 성장 데이터 분리, 기록 제출과 순위 조회 확인
________________________________________
PART 18
최종 통합 테스트
테스트 흐름
1.	Loading 진입 및 로딩바 미표시 확인
2.	Title 진입 및 로딩 진행률 확인
3.	Title 로그인 UI가 표시되는지 확인
4.	신규 가입에서 Account ID/Password 입력 후 Nickname 설정 단계로 전환되는지 확인
5.	저장된 ID/PW는 ID 평문과 Password 마스킹 상태로 복원되고 자동 로그인하지 않는지 확인
6.	인증 성공 후 하단 점멸 `TOUCH` 입력으로 Lobby 진입
7.	Lobby 배경 및 캐릭터 UI 확인
8.	캐릭터 선택
9.	모드 선택
10.	InGame 시작
11.	좌우 이동
12.	아이템 여러 번 통과
13.	아이템 획득
14.	적 충돌
15.	리스폰
16.	10층 이상 진행
17.	페이지 전환
18.	스킬 효과
19.	수집 아이템 저장
20.	Game Over
21.	결과창 표시
22.	광고보기 선택 시 보상형 광고 시청 후 부활
23.	확인 선택 시 최고 층과 점수 저장
24.	Lobby 복귀
25.	저장 데이터 확인
26.	`LootUpRank` 게임 종료 기록 제출 확인
27.	LANK의 MY LANK와 전역 순위 및 계정별 데이터 분리 확인
완료 조건
•	컴파일 에러 없음
•	Missing Reference 없음
•	NullReferenceException 없음
•	Android 입력 정상
•	페이지 전환 후 아이템과 적 상태 정상
•	저장 데이터 정상
