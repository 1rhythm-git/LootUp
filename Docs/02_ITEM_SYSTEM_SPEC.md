LootUp Item System Specification
________________________________________
1. 시스템 목적
아이템 시스템은 플레이어가 각 층의 셀 내부에 배치된 아이템을 통과하여 획득하는 구조이다.
일반적인 접촉 즉시 획득 방식 외에도, 아이템마다 요구되는 통과 횟수가 존재할 수 있다.
플레이어는 좌우 이동만 가능하므로 같은 층에서 아이템을 여러 차례 왕복 통과해 획득 조건을 달성한다.
________________________________________
2. 핵심 용어
Required Pass Count
아이템을 획득하기 위해 필요한 통과 횟수이다.
Current Pass Count
현재까지 유효하게 인정된 통과 횟수이다.
Valid Pass
플레이어가 아이템 판정 영역에 진입하고, 이후 완전히 빠져나온 한 번의 통과이다.
Acquired
필요 통과 횟수를 충족해 아이템 효과가 실행되고 아이템이 제거되거나 수집 처리된 상태이다.
________________________________________
3. 통과 판정 규칙
플레이어가 아이템 콜라이더 내부에 계속 머물러도 통과 횟수는 한 번만 증가해야 한다.
판정 순서:
Outside
→ Enter
→ Count +1
→ Inside
→ Exit
→ Ready For Next Pass
같은 프레임이나 같은 진입 상태에서 중복 카운트하면 안 된다.
________________________________________
4. 통과 방향
기본 아이템은 양방향 통과를 인정한다.
Left To Right = Valid
Right To Left = Valid
확장 가능한 방향 규칙:
Any
LeftToRightOnly
RightToLeftOnly
Alternating
Alternating 아이템은 좌→우와 우→좌를 번갈아 통과해야 유효한 방식으로 확장할 수 있다.
초기 구현에서는 Any만 실제 사용하되 enum 확장 구조를 준비할 수 있다.
________________________________________
5. 획득 처리
다음 조건이 만족되면 아이템을 획득한다.
Current Pass Count >= Required Pass Count
획득 시 처리:
1.	중복 획득 방지 상태 설정
2.	아이템 효과 실행
3.	점수 또는 데이터 반영
4.	획득 연출 실행
5.	아이템 비활성화 또는 풀 반환
6.	필요한 경우 저장 요청
________________________________________
6. 아이템 타입
Score
현재 런의 점수에 즉시 반영된다.
필수 데이터 예시:
•	Base Score
•	Score Multiplier
•	Combo Value
•	Bonus Duration
Skill
획득 시 플레이어 또는 적에게 효과를 적용한다.
필수 데이터 예시:
•	Effect ID
•	Target Type
•	Duration
•	Stack Policy
•	Stack Limit
•	Effect Value
Target Type 예시:
Player
AllEnemies
NearestEnemy
CurrentFloorEnemies
GameState

현재 구현된 Skill 아이템:
•	Red Sneaker
	- EffectKey: add_move_speed_percent
	- EffectValue: 20
	- EffectDurationSeconds: 5
	- RequiredPassCount: 스폰 시 1~3 랜덤
	- 효과: 플레이어 이동속도 20% 증가
•	Winged Shoe
	- EffectKey: add_move_speed_percent
	- EffectValue: 50
	- EffectDurationSeconds: 5
	- RequiredPassCount: 스폰 시 1~3 랜덤
	- 효과: 플레이어 이동속도 50% 증가
•	Winged Heart
	- EffectKey: add_max_life
	- EffectValue: 1
	- 효과: 현재 생명력이 Max이고 추가 슬롯이 없을 때 Max Life +1
	- 차감 상태에서는 Max Life 증가 없이 생명력만 회복
	- 추가된 생명력 슬롯이 피해로 소모되면 Max Life는 캐릭터의 원래 수치로 복귀
		- 추가 슬롯 활성 중 재획득하면 SCORE로 환산하며 Max Life는 중첩 증가하지 않음
		- 추가 슬롯 소모 후 Max 상태에서 재획득하면 다시 Max Life +1 적용
•	Fever Battery
	- ItemType: Fever
	- EffectKey: add_fever_gauge
	- EffectValue: 5
	- RequiredPassCount: 스폰 시 1/3/5 중 랜덤
	- SpawnWeight: 18
	- 효과: 카운트 1은 5%, 3은 15%, 5는 30% 즉시 충전
	- 100% 도달 시 기존 피버 자동 발동
	- 피버 활성 중에는 Pass와 수명 타이머를 정지하고 획득되지 않은 상태로 필드에 보존
	- 피버 종료 후 남은 Pass와 수명부터 다시 진행
	- 피버 중 배터리 접촉 시 `FEVER ACTIVE` 피드백만 표시

이동속도 증가 아이템은 영구 강화가 아니다.
효과 지속시간 동안만 `PlayerMotor`의 이동속도에 반영하고, 시간이 끝나면 자동으로 제거한다.
효과 지속 중에는 `PlayerBuffVisualFeedback` 점멸, 기존 Run 스프라이트 대시 애니메이션, 강화된 대시 먼지를 표시한다.
최종 이동속도 버프 지속시간은 `EffectDurationSeconds × RequiredPassCount × Artifact 지속시간 보정`으로 계산한다.
현재 설정에서는 카운트에 따라 5초, 10초, 15초가 적용된다.

Collection
장기 저장되는 수집 데이터에 반영한다.
수집형은 다음 두 종류로 구분한다.
•	Artifact: `MaxOwnedAmount=1`인 고유 수집품이며, 획득 후 스폰 후보에서 제외한다.
•	CharacterCoin: 중복 보유할 수 있으며 로비의 캐릭터 능력치 강화 비용으로 사용한다.

수집형 필수 데이터:
•	CollectionItemType
•	CollectionId
•	CollectionTargetId
•	AcquireAmount
•	MaxOwnedAmount
•	CollectionBaseSpawnChance
•	CollectionSpawnChancePerFloor

`MaxAcquirePerRun`은 현재 런의 획득 횟수 제한이고, `MaxOwnedAmount`는 모든 런을 합친 영구 보유량 제한이다.
캐릭터 강화 비용은 `CollectionCost[]`로 관리해 한 종류 또는 여러 종류의 캐릭터 코인을 조합할 수 있다.

Artifact 확장 구조:
• `Resources/Data/Artifacts.csv`: 수집품 ID, 표시명, 테마, 아이콘 경로를 등록한다.
• `Resources/Data/ArtifactEffects.csv`: 효과 타입, 효과값, 요구 수량, 후보 Artifact ID와 UI 설명을 등록한다.
• 신규 Artifact는 두 CSV와 `Items.csv`, `ItemIcons.csv`에 행을 추가해 등록한다.
• 기존 효과 타입의 신규 조합은 코드 변경 없이 `ArtifactEffects.csv` 행 추가로 확장한다.
• 완전히 새로운 효과 동작은 `ArtifactEffectType`, `ArtifactEffectResolver`, 실제 적용 지점만 확장하며 저장 구조와 Lobby UI는 유지한다.
• Artifact 효과 활성 상태는 별도 저장하지 않고 현재 보유 수집품으로 매번 계산해 데이터 변경과 저장 마이그레이션을 분리한다.

Artifact 초기 드랍 정책:
• 수집형 드랍은 셀마다가 아니라 `CollectionItemType`별 Page 1회 확률 판정을 수행한다.
• Artifact 기본 Page 확률은 0.1%, 층당 0.0002% 증가, 최대 0.5%이다.
• 당첨 후 미보유 Artifact 중 하나를 선택하며 한 Page에는 수집형 아이템을 최대 1개 배치한다.
• 모든 Artifact의 기본 Required Pass Count는 10이다.
• Golden Cup 미보유 상태에서 3번째 Page에 도달하면 유효한 빈 셀 한 곳에 반드시 배치한다.
• Golden Cup 확정 배치는 최초 Artifact 확인과 Lobby 메뉴 해금을 유도하는 온보딩 장치로 사용한다.
• Artifact는 전용 최상위 레이어에 배치해 층별 시야 가림과 다른 인게임 레이어의 영향을 받지 않는다.
• 최초 Artifact 획득 후 Lobby의 `ARTIFACT` 메뉴를 활성화한다.

Artifact Lobby UI:
• `COLLECTION`은 4×4 목록에서 보유 여부와 관계없이 Artifact 이름을 표시한다.
• 보유 Artifact는 원색 아이콘과 활성 상태색, 미보유 Artifact는 저채도 아이콘과 비활성 상태색으로 구분한다.
• `EFFECTS`는 기본 진입 탭인 세로 스크롤 목록이며 효과명, 상세 설명, 효과값, 활성 상태, 요구 수량을 표시한다.
• 효과 표시 순서는 `ArtifactEffects.csv` 행 순서를 따르며 `CHAMPION RECORD`를 첫 번째로 노출한다.
• 각 효과 카드에는 조합 후보 Artifact의 아이콘과 이름을 모두 표시한다.
• 조합 후보의 획득 여부는 활성/비활성 색상으로 구분하며, 요구 후보가 늘어나면 카드 높이를 자동 확장한다.
________________________________________
7. 효과 구조
아이템 데이터와 아이템 효과 실행을 분리한다.
권장 구조:
ItemDefinition
    ↓
ItemInstance
    ↓
IItemEffect
    ├── AddScoreEffect
    ├── HealHeartEffect
    ├── AddTimeEffect
    ├── PlayerSpeedEffect
    ├── FreezeEnemiesEffect
    ├── RemoveEnemiesEffect
    └── AddCollectionEffect
ItemDefinition은 데이터만 보유한다.
실제 효과는 IItemEffect 구현체가 담당한다.

현재 구현체:
•	AddScoreItemEffect
•	AddTimeItemEffect
•	HealHeartItemEffect
•	AddMaxLifeItemEffect
•	AddMoveSpeedItemEffect

현재 데이터 소스:
•	`Assets/_Project/Data/Tables/Items.csv`
•	`Assets/_Project/Data/Tables/ItemIcons.csv`

현재 `Items.csv` 주요 컬럼:
•	ItemId
•	ServerItemId
•	TableVersion
•	Enabled
•	DisplayName
•	ItemType
•	IconKey
•	MinFloor
•	MaxFloor
•	SpawnWeight
•	RequiredPassCount
•	LifetimeSeconds
•	PassDirection
•	EffectKey
•	EffectValue
•	EffectDurationSeconds
•	AffectsScore
•	AffectsProgression
•	ServerValidated
•	MaxAcquirePerRun
•	Rarity

`LifetimeSeconds`는 아이템이 필드에 남아있는 시간이다.
`EffectDurationSeconds`는 획득 후 적용되는 버프 지속시간이다.
두 값은 서로 다른 목적이므로 같은 값으로 묶지 않는다.

현재 통과 카운트 규칙:
•	Heart Pack은 3회 통과 후 획득한다.
•	Winged Heart는 5회 통과 후 획득한다.
•	Time, Score 계열 아이템은 스폰 시 1~5회 중 랜덤 통과 카운트를 부여한다.
•	Red Sneaker, Winged Shoe는 스폰 시 1~3회 중 랜덤 통과 카운트를 부여한다.
•	Fever Battery는 스폰 시 1/3/5회 중 랜덤 통과 카운트를 부여한다.
•	Fever Battery는 통과 카운트에 따라 피버 게이지를 각각 5%/15%/30% 충전한다.
•	이동속도 아이템은 통과 카운트에 비례해 버프 지속시간이 상승한다.
•	Time 계열 아이템은 통과 카운트에 비례해 시간 증가량이 상승한다.
•	Score 계열 아이템은 랜덤 통과 카운트가 높을수록 점수 보정이 붙는다.
•	현재 보정값은 추가 통과 1회당 +25%이다.
________________________________________
8. 중첩 정책
스킬형 아이템은 다음 중첩 정책을 가질 수 있다.
IgnoreNew
RefreshDuration
AddDuration
StackValue
ReplaceWithStronger
예시:
•	무적 아이템 재획득: 지속 시간 갱신
•	이동 속도 증가: 능력치 중첩 없이 지속 시간 갱신
•	약한 버프 획득 후 강한 버프 획득: 강한 효과로 교체

현재 이동속도 증가 구현은 하나의 활성 능력치와 지속시간만 유지한다.
동일하거나 하위 이동속도 아이템을 연속 획득하면 현재 능력치는 유지하고 지속시간만 새 아이템 기준으로 갱신한다.
상위 이동속도 아이템을 획득하면 능력치와 지속시간을 모두 새 아이템 기준으로 갱신한다.
정식 밸런스 단계에서 Stack Policy와 Stack Limit을 데이터화할 수 있다.
Enemy 일시 정지 아이템은 현재 및 향후 필수 구현 범위에서 제외한다.
추가 스킬 효과와 중첩 세부 정책은 레벨 디자인 단계에서 필요성이 확인된 항목만 확장한다.
________________________________________
9. 아이템 진행 UI
필요 통과 횟수가 2 이상이면 진행 상태를 표시한다.
초기 구현 권장:
1 / 3
2 / 3
추후 확장:
•	아이콘 테두리 게이지
•	색상 변화
•	흔들림
•	발광 강도
•	단계별 스프라이트
진행 UI는 ItemInstance 내부 로직과 분리한다.

현재 구현:
•	아이템 중앙에 남은 통과 횟수를 숫자로 표시한다.
•	숫자는 흰색 Bold 텍스트를 사용한다.
•	검은 Outline과 Shadow를 사용해 시인성을 보강한다.
•	획득 시 `gain.ogg`, 통과 카운트 시 `pass.ogg` 재생 구조를 사용한다.
________________________________________
10. 스폰 규칙
아이템은 셀 내부에 생성한다.
스폰 데이터는 다음 조건을 가질 수 있다.
•	최소 등장 층
•	최대 등장 층
•	등장 가중치
•	아이템 타입
•	희귀도
•	한 페이지 최대 생성 개수
•	동일 아이템 중복 제한
•	적과 같은 셀 배치 가능 여부
•	엘리베이터 셀 배치 가능 여부
초기 안전 규칙:
•	시작 셀에는 생성하지 않음
•	엘리베이터 셀에는 생성하지 않음
•	적과 동일 위치에 생성하지 않음
•	한 셀에 하나의 아이템만 생성
•	플레이어가 도달할 수 없는 위치에는 생성하지 않음

현재 구현:
•	한 페이지 최대 아이템 수는 `maxItemsPerPage`로 제한한다.
•	좌측 끝 셀과 우측 끝 셀에는 생성하지 않는다.
•	실행마다 `runtimeSeed`를 생성하고, 페이지 인덱스를 섞어 배치 난수를 만든다.
•	`randomizeSeedOnStart`가 켜져 있으면 새 실행마다 배치가 달라진다.
•	`randomizeSeedOnStart`가 꺼져 있으면 `randomSeed` 기반으로 재현 가능한 배치를 만든다.
•	캐릭터 기본 스테이터스인 Item Chance 판정에 성공하면 해당 페이지의 아이템 중 1개를 Time 또는 Skill 타입으로 보장한다.
•	Item Chance는 레벨 또는 캐릭터 스킬 해금 여부와 관계없이 페이지 생성 시 항상 판정한다.
•	Item Chance는 기존 아이템 가중치 추첨을 제거하지 않으며, 보장 대상 외 아이템은 기존 규칙으로 선택한다.
•	캐릭터 스킬은 아이템 기본 효과 적용 이후 아이템 획득 1회당 한 번 판정한다.
•	스킬 발동 조건은 원본 `ItemType`을 기준으로 하므로, 하트가 최대 상태에서 스코어로 전환되어도 하트 획득 스킬 조건으로 처리한다.
•	스킬로 발생한 추가 Score 또는 Time은 아이템 획득 이벤트를 다시 발생시키지 않는다.
•	캐릭터 스킬 발동 확률과 효과 수치는 `CharacterSkillDefinition`의 P1~P5에서 관리한다.
•	Collection 아이템은 일반 아이템의 상대 가중치와 분리된 절대 확률을 사용한다.
•	최종 확률은 `(CollectionBaseSpawnChance + CollectionSpawnChancePerFloor × (현재 층 - MinFloor)) × (1 + PlayerChanceBonusPercent / 100)`으로 계산한다.
•	Artifact는 이미 보유했거나 `MaxOwnedAmount`에 도달하면 스폰 후보에서 제외한다.
•	Collection 아이템은 한 페이지에 동일한 `CollectionId`가 중복 생성되지 않는다.

RISING 전용 스폰 규칙:
•	일반 아이템 테이블과 분리된 `RisingSystem`이 문자 수집 상태와 스폰을 관리한다.
•	2층부터 층 진입마다 기본 12% 확률로 판정한다.
•	실패한 층마다 확률이 3%p 증가하고 최대 45%로 제한한다.
•	8번째 연속 판정은 확정 스폰으로 처리한다.
•	`R`, `I`, `S`, `N`, `G`를 가중치 기반으로 무작위 생성하며 이미 채운 알파벳도 중복 등장할 수 있다.
•	초기 가중치는 여섯 슬롯 기준으로 계산해 `I`가 두 슬롯분, 나머지 문자가 한 슬롯분을 가진다.
•	필요 수량을 채운 알파벳의 기본 가중치는 45%로 감소하지만 추첨 후보에는 유지한다.
•	중복 획득 시 다음 RISING 등장 확률을 5%p 올리고, 해당 알파벳의 이후 추첨 가중치를 다시 50% 낮춘다.
•	중복 보정은 RISING 한 사이클 동안 누적되며 등장 확률 보너스는 최대 25%p로 제한한다.
•	중복 보상이 적용된 최종 등장 확률 상한은 70%다.
•	중복 획득 즉시 시간 3초와 피버 게이지 10을 지급한다.
•	현재 층의 활성 엘리베이터 반대편 비활성 엘리베이터에 생성한다.
•	플레이어가 도착 엘리베이터에서 1.15칸 이상 벗어난 뒤 생성해 뒤쪽 팝업 조건을 보장한다.
•	확률, 보정치, 확정 횟수는 `RisingSystem` SerializeField로 노출해 플레이 테스트 후 조정한다.
________________________________________
11. 저장 규칙
Score 아이템:
•	런 종료 결과에 포함
•	개별 아이템 획득 이력은 필수 저장하지 않음
Game Money 아이템:
•	런 중 획득량을 보유 게임머니와 분리해 누적
•	소액, 중액, 고액 항목별 가중치로 배치하며 고액일수록 낮은 확률 적용
•	정상 런 결과 확정 시 점수 기반 보너스 게임머니와 합산해 한 번만 지급
•	비정상 종료 시 미확정 게임머니는 지급하지 않음
Skill 아이템:
•	기본적으로 런 종료 시 효과 소멸
•	영구 스킬인 경우 Collection 또는 Progress 데이터로 처리
•	이동속도 아이템은 현재 런의 지정 지속시간 동안만 적용하고, 저장 대상이 아니다.
•	이동속도 아이템 효과는 생명력이 실제로 차감되는 순간 즉시 제거하고 캐릭터 기본 이동속도로 복원한다.
Collection 아이템:
•	획득 즉시 로컬 저장
•	서버 연결 시 동기화 대상
•	Artifact는 중복 여부를 확인하고 획득 후 재등장하지 않음
•	CharacterCoin은 `MaxOwnedAmount`까지 수량 누적
•	획득 `EventId`를 서버 멱등 키로 사용하고 미전송 이벤트를 로컬에 보관
•	서버 응답은 충돌 및 획득 판정을 대기시키지 않음
•	비정상 종료 시 Collection은 유지하고 완료되지 않은 런의 점수 보상은 정산하지 않음
•	캐릭터 강화는 로비에서 코인 차감과 강화 단계 증가를 하나의 저장 작업으로 처리
•	강화 능력치는 다음 InGame 진입 시 `PlayerCharacterRuntime` 초기값에 반영
________________________________________
12. 예외 상황
다음 상황을 방어한다.
•	아이템 획득 직후 다시 충돌
•	플레이어가 리스폰하며 아이템 내부에서 생성
•	아이템 효과 실행 중 아이템 오브젝트 파괴
•	페이지 전환 중 통과 판정
•	게임 오버 직전 획득
•	같은 프레임에 여러 아이템 획득
•	수집 데이터 저장 실패
•	Required Pass Count가 0 이하로 설정됨
Required Pass Count가 0 이하이면 런타임에서 최소 1로 보정한다.
________________________________________
13. Inspector 기준
ItemDefinition에서 설정할 항목:
Item ID
Display Name
Description
Icon
Prefab
Item Category
Required Pass Count
Pass Direction
Rarity
Minimum Floor
Spawn Weight
Effect Definitions
Is Persistent
ItemInstance에서 설정할 항목:
Definition
Pass Trigger
Progress View
Acquire Effect Root
________________________________________
14. 테스트 항목
1.	한 번 통과 아이템 획득
2.	세 번 통과 아이템이 정확히 세 번째에 획득
3.	콜라이더 내부 체류 중 중복 증가하지 않음
4.	좌→우 통과 인정
5.	우→좌 통과 인정
6.	획득 후 중복 효과 실행 방지
7.	리스폰 후 정상 판정
8.	페이지 전환 시 남은 아이템 정리
9.	수집형 저장
10.	스킬 중첩 정책 확인
11.	RISING 문자가 가중치에 따라 무작위 생성되고 각 슬롯 색상이 서로 다른지 확인
12.	현재 층의 비활성 엘리베이터에만 생성되는지 확인
13.	연속 미등장 시 확률이 증가하고 8번째 판정에서 확정 생성되는지 확인
14.	RISING 완성 시 다음 10층 구간의 첫 층으로 빠르게 이동하는지 확인
15.	이동한 층이 결과의 Highest Floor 및 Floor Bonus에 반영되는지 확인
16.	중복 문자를 획득하면 시간, 피버와 다음 등장 확률 보너스가 적용되는지 확인
17.	중복 획득한 알파벳의 이후 추첨 비중이 감소하는지 확인
18.	숫자키 `4`가 월드 배치 없이 실제 문자 가중치 추첨으로 획득을 처리하는지 확인

________________________________________
15. RISING 시스템
RISING는 한 런 동안 무작위로 등장하는 문자를 모아 여섯 글자를 완성하는 특수 진행 시스템이다.

UI 규칙:
•	TopHUD 중앙에 `RISING` 여섯 칸을 항상 표시한다.
•	미수집 글자는 회색 비활성 상태로 표시한다.
•	수집한 글자는 R=빨강, I=하늘, S=노랑, I=자홍, N=초록, G=주황으로 활성화한다.
•	완성 시 전체 글자가 점멸하고 `RISING +N FLOORS` 연출을 표시한다.

발동 규칙:
•	기본 이동 목적지는 다음 10층 스테이지의 첫 층이다.
•	현재 층에서 목적 층까지의 실제 차이를 N으로 사용한다.
•	층을 한 층씩 빠르게 갱신하며 세로 속도선 오버레이로 스크롤 상승을 표현한다.
•	발동 중 타이머, 플레이어 입력, 엘리베이터와 일반 아이템 스폰을 잠시 정지한다.
•	마지막 문자 획득 즉시 타이머를 정지하며, 같은 프레임의 Time Over 판정보다 문자 획득을 먼저 처리한다.
•	남은 시간이 0초에 가까워도 최소 잔여 시간을 보존하고 목적 층 도착 및 캐릭터 배치 완료 후 타이머를 재개한다.
•	완료 후 캐릭터는 목적 스테이지 첫 층의 도착측 비활성 엘리베이터에 배치한다.
•	RISING 시작과 도착 시 이전 엘리베이터 이동에서 생성된 수직 케이블 선을 즉시 비활성화하고 제거한다.
•	`InfiniteFloorManager.RunHighestFloor`가 목적 층까지 갱신되므로 기존 결과 정산의 층 점수와 층 경험치에 자동 반영한다.
•	발동 후 글자 진행도는 0으로 초기화되어 같은 런에서 다시 수집할 수 있다.

테스트 치트:
•	숫자열 또는 넘버패드 `4`를 누르면 월드 아이템 배치 없이 RISING 문자 1개를 획득한다.
•	문자는 실사용과 동일한 가중치 추첨을 거치며 중복 보상 및 RISING 완성도 동일하게 처리한다.
