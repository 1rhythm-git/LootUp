# LootUp BackND 데이터 분리 및 이관 계획

## 1. 문서 목적

현재 로컬 및 BackND에 저장되는 정보를 데이터 수명과 권한 기준으로 분류한다.
이 문서는 서버 저장 범위를 확장할 때의 구현 기준이다. 재화 서버 이관은
완료되었고, 다음 구현 대상은 캐릭터 성장/보유/선택/장착 정보다.

분류 원칙:

- 경제 가치, 성장, 보유 상태는 서버가 최종 권한을 가진다.
- 여러 기기에서 복구되어야 하는 정보는 서버 이관 대상이다.
- 입력 기억과 기기별 환경 설정은 로컬에 유지한다.
- 런타임 임시 상태는 런 종료 전까지 서버 영구 데이터로 취급하지 않는다.
- 서버에 저장되는 모든 변경은 중복 요청과 재시도에 안전해야 한다.
- 사용자 데이터와 게임 마스터 데이터는 같은 테이블에 혼합하지 않는다.

## 2. 현재 BackND 연동 완료 정보

| 정보 | 현재 저장소 | 서버 권한 | 비고 |
| --- | --- | --- | --- |
| 계정 ID, `gamerInDate`, Nickname | BackND 인증 | 서버 | 별도 게임정보 테이블에 복제하지 않음 |
| 현재 기간 랭킹 | `LootUpRank` + 운영 리더보드 | 서버 | 도달 층수 > 스코어 > 캐릭터 레벨 |
| 계정 누적 BEST | Private `LootUpBest` + 계정별 로컬 캐시 | 서버 최고값 동기화 | 리더보드 초기화와 무관하게 유지 |
| 랭킹 캐릭터 ID와 레벨 | `LootUpRank`, `LootUpBest` | 서버 | 해당 기록을 달성한 시점의 값 |

`LootUpBest`와 `LootUpRank` 분리 및 Android 테스트는 완료됐다.

## 3. P0 우선 이관 대상

출시 계정 데이터의 복구, 계정 간 분리, 위변조 방지를 위해 가장 먼저
BackND로 옮겨야 하는 정보다.

### 3.1 계정 프로필과 재화

현재 로컬 원본:

- `LootUp.UserProfile.Account.v2.{gamerInDate}`
- `GameMoney`
- `Ruby`
- 사용자 Trait 데이터

현재 구현 상태:

- `LootUpPlayerProfile` 서버 잔액과 `LootUpCurrencyLedger` 거래 원장 클라이언트 구현 완료
- 계정별 Pending Queue와 다음 로그인 재전송 구현 완료
- BackND Private 테이블 생성 및 실제 계정의 최초 이관, Pending 재전송, 중복 요청 방지 검증 완료

이관 원칙:

- `GameMoney`, `Ruby`는 서버 권한으로 전환한다.
- 획득과 사용은 최종 잔액만 덮어쓰지 않고 고유 거래 ID를 가진 증감 요청으로 처리한다.
- 동일 거래 ID는 한 번만 반영한다.
- 프로필 잔액 갱신과 원장 추가는 `TransactionWriteV2`로 함께 처리한다.
- 서버 미확정 요청은 로컬 Pending Queue에 저장하되 표시 잔액에는 선반영하지 않는다.
- Trait가 Artifact 조합에서 계산되는 파생값이면 서버에 중복 저장하지 않고
  보유 Artifact에서 다시 계산한다.
- 운영자가 직접 지급한 Trait만 존재한다면 별도 서버 필드로 분리한다.

권장 논리 테이블:

- `LootUpPlayerProfile`: 스키마 버전, GameMoney, Ruby, BackND 시스템 갱신 시각
- `LootUpCurrencyLedger`: 거래 ID, 재화 종류, 증감량, 사유, 런 ID, 처리 시각

콘솔 스키마는 `Docs/06_BACKND_INTEGRATION_PLAN.md`의 P7을 기준으로 생성한다.
클라이언트 `requestId` 사전 조회는 일반 재시도 중복을 차단하지만, 악의적인
금액 조작과 다중 기기 동시 요청의 완전한 원자적 멱등성은 BackND Function
또는 동일 수준의 서버 검증으로 보강해야 한다.

### 3.2 캐릭터 성장과 보유 상태

현재 로컬 원본:

- `LootUp.CharacterProgression.Account.v2.{gamerInDate}`
- 캐릭터 ID별 Level, CurrentExperience, IsOwned, IsEquipped
- SelectedCharacterId, EquippedCharacterId

현재 구현 상태:

- 동기식 로컬 캐시와 비동기 BackND 동기화 계층 분리 완료
- 로그인 최초 이전, 서버 원본 복구, 계정 전환 응답 차단 구현 완료
- XP 원장 멱등 처리와 계정별 XP/보유/Loadout Pending Queue 구현 완료
- 런 보상과 Lobby/TopHUD 디버그 XP 지급을 Manager 경로로 통합 완료
- XP·보유·Loadout의 Pending 선저장 후 로컬 반영 순서 적용 완료
- 보유 상태 전용 서버 갱신으로 레벨/XP 덮어쓰기 경로 제거 완료
- 캐릭터 초기 동기화 실패 시 BackND 인증 완료 차단 적용 완료
- 콘솔 테이블 생성과 실제 계정 검증은 사용자 요청으로 유보

이관 원칙:

- 레벨, 경험치, 보유 상태는 서버 권한으로 전환한다.
- 선택 및 장착 캐릭터는 보유 캐릭터인지 서버에서 검증한다.
- 경험치 지급은 런 결과 ID를 기준으로 중복 반영을 방지한다.
- 최초 한 번의 로컬 이전 이후에는 클라이언트의 더 높은 레벨을 자동 채택하지 않는다.
- 계정 전환 시 서버 데이터를 먼저 받은 후 로컬 캐시를 갱신한다.

권장 논리 테이블:

- `LootUpCharacterProgress`: 캐릭터 ID, Level, Experience, IsOwned, 갱신 시각
- `LootUpPlayerLoadout`: SelectedCharacterId, EquippedCharacterId, 갱신 시각

확정 콘솔 스키마:

| 테이블 | 컬럼 | 타입 | 규칙 |
| --- | --- | --- | --- |
| `LootUpCharacterProgress` | `schemaVersion` | int | 초기값 1 |
| `LootUpCharacterProgress` | `migrationVersion` | int | 최초 로컬 이전 완료 시 1 |
| `LootUpCharacterProgress` | `characterId` | string | 정규화된 캐릭터 ID, 계정 내 캐릭터별 1행 |
| `LootUpCharacterProgress` | `level` | int | 1~캐릭터 정의의 Max Lv. |
| `LootUpCharacterProgress` | `currentExperience` | int | 현재 레벨 내부 XP, 0 이상 |
| `LootUpCharacterProgress` | `isOwned` | bool | 서버 권한 보유 상태 |
| `LootUpPlayerLoadout` | `schemaVersion` | int | 초기값 1 |
| `LootUpPlayerLoadout` | `migrationVersion` | int | 최초 로컬 이전 완료 시 1 |
| `LootUpPlayerLoadout` | `selectedCharacterId` | string | 보유 캐릭터만 허용 |
| `LootUpPlayerLoadout` | `equippedCharacterId` | string | 보유 캐릭터만 허용, 계정당 1행 |
| `LootUpCharacterProgressLedger` | `schemaVersion` | int | 초기값 1 |
| `LootUpCharacterProgressLedger` | `requestId` | string | XP 지급 멱등 키 |
| `LootUpCharacterProgressLedger` | `characterId` | string | 지급 대상 캐릭터 ID |
| `LootUpCharacterProgressLedger` | `experienceDelta` | int | 지급 XP, 0보다 커야 함 |
| `LootUpCharacterProgressLedger` | `levelAfter` | int | 지급 후 레벨 |
| `LootUpCharacterProgressLedger` | `experienceAfter` | int | 지급 후 현재 레벨 내부 XP |
| `LootUpCharacterProgressLedger` | `reason` | string | 예: `run_reward` |
| `LootUpCharacterProgressLedger` | `runId` | string | 런 보상 추적 ID |
| `LootUpCharacterProgressLedger` | `createdAt` | string | 클라이언트 요청 생성 UTC 시각 |

세 테이블은 Private, 스키마 정의, 활성 상태로 생성한다. `updatedAt`은 BackND
시스템 컬럼을 사용하고 사용자 컬럼으로 추가하거나 `Param`에 보내지 않는다.
`LootUpCharacterProgressLedger.requestId` 사전 조회와 진행 행 갱신/원장 추가의
`TransactionWriteV2` 묶음으로 일반 재시도 중복을 막는다. 다중 기기 동시
요청의 강한 멱등성은 운영 전 BackND Function으로 보강한다.

콘솔 반영 상태(2026-10-02): 위 세 테이블을 Private, 스키마 정의, 활성 상태로
생성했다. 실제 계정 최초 이관으로 캐릭터 진행 4행과 Loadout 1행 생성을 확인했다.
재로그인, XP 원장과 중복 지급 방지 검증은 남아 있다.

동기화 순서:

1. 로그인 성공 직후 계정별 로컬 캐시를 열고 서버 진행 행과 Loadout을 조회한다.
2. 서버 행이 전혀 없으면 로컬의 정규화된 캐릭터 목록과 Loadout을 최초 1회 등록한다.
3. 서버 행이 있으면 서버 값을 원본으로 로컬 캐시를 교체하며 로컬 고레벨을 합치지 않는다.
4. 선택/장착 값이 미보유 또는 미등록 ID면 서버 값을 비우고 최초 보유 캐릭터로 복구한다.
5. XP 지급은 `run:{runId}:character-xp:{characterId}` 요청 ID로 처리하고 성공 후 캐시를 갱신한다.
6. 선택/장착 변경은 Pending 저장 성공 후 로컬 캐시에 반영하고 서버 실패 시 다음 로그인에 재전송한다.
7. 로그아웃과 계정 전환 시 진행 캐시, 선택 상태와 동기화 작업을 모두 초기화한다.

실패 및 복구 정책:

- 서버 조회 실패 시 캐시를 서버 원본으로 오인하지 않고 동기화 실패 상태를 유지한다.
- 최초 이관 요청 실패 시 서버 성공 확인 전 `migrationVersion`을 완료 처리하지 않는다.
- XP 서버 미확정 요청은 계정별 Pending Queue에 저장하고 다음 로그인에서 재시도한다.
- 서버에서 모르는 캐릭터 ID와 범위를 벗어난 레벨/XP는 적용하지 않고 오류로 기록한다.
- 캐릭터 에셋의 `InitiallyOwned`는 서버 행이 없는 최초 생성에만 사용한다.

정적 코드 점검 결과와 구현 경계:

- `ICharacterProgressionService`는 Lobby/InGame이 즉시 읽는 동기식 계약이므로 로컬 캐시 역할을 유지한다.
- `CharacterProgressionState` 호출부를 직접 BackND 비동기 API로 바꾸지 않는다.
- 비동기 서버 작업은 신규 `ICharacterProgressionSyncService`, `BackndCharacterProgressionService`,
  `CharacterProgressionManager`에서 초기 동기화, XP, 보유, Loadout 변경을 조정한다.
- `AuthenticationManager`는 현재 로그인 후 로컬 서비스를 즉시 구성하므로 캐릭터 동기화 완료를
  `Authenticated` 전환 조건에 포함해야 한다.
- 로그인 시점에는 `LobbyController.availableCharacters`를 사용할 수 없으므로, 최초 이관에 필요한
  정규 캐릭터 ID와 `InitiallyOwned`를 제공하는 UI 독립 카탈로그가 필요하다.
- `RunResultService`의 현재 XP 직접 지급은 고유 `runId` 기반 Manager 요청으로 교체해야 한다.
- `CharacterSelectionState.Reset()`과 로그아웃 서비스 초기화에 서버 동기화 상태 초기화를 포함해야 한다.

예상 수정 대상:

- 유지/보강: `ICharacterProgressionService.cs`, `LocalCharacterProgressionService.cs`,
  `CharacterProgressionState.cs`, `CharacterProgressionData.cs`
- 신규: `ICharacterProgressionSyncService.cs`, `BackndCharacterProgressionService.cs`,
  `CharacterProgressionManager.cs`, 캐릭터 카탈로그 제공 클래스
- 연결: `AuthenticationManager.cs`, `RunResultService.cs`, `CharacterSelectionState.cs`
- 회귀 확인: `LobbyController.cs`, `TopHUDController.cs`, `PlayerCharacterRuntime.cs`

### 3.3 Artifact, Character Coin, 캐릭터 강화

현재 로컬 원본:

- `LootUp.CollectionProgress.Account.v2.{gamerInDate}`
- 기존 공용 데이터 `LootUp.CollectionProgress.v1`
- CollectionId별 OwnedAmount, LifetimeAcquiredAmount
- CharacterId와 UpgradeId별 강화 Level
- 서버 미전송 `PendingCollectionEventData`

현재 상태:

- 수집 저장 키의 `gamerInDate`별 로컬 격리를 완료했다.
- 기존 공용 데이터는 업데이트 후 처음 로그인한 계정만 1회 승계한다.
- `MigrationOwner`가 기록된 뒤 다른 계정은 공용 데이터를 복제하지 않는다.
- 서버 권한 전환과 다른 기기 복구는 아직 구현되지 않았다.

이관 원칙:

- Artifact 보유, Character Coin 수량, 캐릭터 강화는 서버 권한으로 전환한다.
- `EventId`를 멱등 키로 사용해 같은 획득 이벤트를 한 번만 처리한다.
- `ServerItemId`, `TableVersion`으로 클라이언트 아이템 정의를 검증한다.
- Artifact는 고유 보유 항목이므로 서버 합집합 방식의 최초 이전이 가능하다.
- Character Coin 소비와 강화 레벨 증가는 하나의 서버 처리 단위로 묶는다.
- `PendingEvents`는 영구 보유 정보가 아니라 서버 반영 대기 큐로 사용하고,
  성공 확인 후 로컬에서 제거한다.
- 소유자 정보가 없는 기존 공용 로컬 데이터는 최초 로그인 계정 한 곳에만
  귀속하고 서버 최초 이전에서도 같은 계정 ID를 유지한다.

권장 논리 테이블:

- `LootUpCollection`: CollectionId, 종류, 보유량, 누적 획득량
- `LootUpCharacterUpgrade`: CharacterId, UpgradeId, Level
- `LootUpInventoryEvent`: EventId, ServerItemId, TableVersion, 수량, 처리 상태

### 3.4 광고 제거 및 유료 권리

현재 로컬 원본:

- `LootUp.Advertising.AdsRemoved.v1`

이관 원칙:

- 실제 결제 기능과 연결되는 시점에는 로컬 Boolean을 권한의 원본으로 사용하지 않는다.
- Google Play 영수증 또는 서버 검증 결과를 기준으로 계정 Entitlement를 저장한다.
- 로컬 값은 화면 반응을 위한 캐시로만 사용한다.

권장 논리 테이블:

- `LootUpEntitlement`: 상품 ID, 권리 종류, 상태, 영수증 검증 시각

## 4. P1 후속 이관 대상

### 4.1 런 결과와 보상 정산 원장

현재 `RunResultData`에 존재하는 후보:

- 종료 사유, 최고 층, 총점 및 점수 세부 항목
- 캐릭터 ID와 플레이 당시 레벨
- 경험치 및 게임머니 보상 세부 항목
- 남은 시간과 하트
- 획득 아이템 이벤트 목록

서버 저장 목적:

- 재화와 경험치 지급의 중복 방지
- 랭킹 기록 검증
- 비정상 점수 및 위변조 조사
- 장애 발생 시 보상 정산 재처리

권장 논리 테이블:

- `LootUpRunLedger`: RunId, 시작/종료 시각, 결과 요약, 정산 상태
- `LootUpRunItemEvent`: RunId, EventId, 아이템/위치/효과 결과

모든 런 프레임이나 이동 좌표를 저장하지 않고 검증에 필요한 요약과 획득
이벤트만 저장한다.

### 4.2 기간 랭킹 보상

현재 미구현 정보:

- 기간 또는 시즌 ID
- 종료 순위 스냅샷
- 보상 구성
- 지급 상태와 지급 완료 시각
- 지급 요청 고유 키

처리 순서:

1. 기간 종료 순위를 확정한다.
2. 계정별 보상 스냅샷을 생성한다.
3. 고유 지급 키로 보상을 지급한다.
4. 지급 완료 상태를 저장한다.
5. 모든 재시도 가능 상태를 확인한 후 이전 기간 기록을 삭제한다.

권장 논리 테이블:

- `LootUpRankReward`: PeriodId, 최종 순위, 보상, 지급 상태, 지급 시각

### 4.3 향후 계정 콘텐츠

구현 시 서버 권한으로 설계할 정보:

- Mission 진행도와 수령 상태
- Mail Box 우편과 첨부 보상 수령 상태
- Shop 구매 횟수, 구매 제한, 상품 지급 상태
- 출석, 이벤트, 업적 보상
- 인앱결제 및 광고 보상 지급 이력

## 5. 로컬 유지 정보

다음 정보는 BackND 사용자 데이터로 이관하지 않는다.

| 정보 | 이유 |
| --- | --- |
| `REMEMBER ID / PW`와 입력값 | 현재 기기의 로그인 편의 기능이며 자동 로그인 정책과 분리 |
| BackND SDK 세션 토큰 | SDK가 관리하며 게임정보 테이블에 저장하지 않음 |
| BGM/SFX 볼륨과 음소거 | 기기별 환경 설정 |
| 진동/Haptic 설정 | 기기 기능 및 사용자 기기별 선호 |
| 그래픽 품질, 해상도, 언어 | 기기별 설정 |
| UI 마지막 탭, 안내 확인 상태 | 다른 기기로 복구할 필요가 없는 편의 상태 |
| 서버 데이터 캐시 | 오프라인 표시와 빠른 로딩용이며 서버 원본을 대체하지 않음 |
| 서버 전송 대기 큐 | 전송 완료 후 삭제되는 로컬 복구 데이터 |

비밀번호 기억 기능은 현재 로컬 난독화 수준이므로 민감정보 보관 정책을
별도로 검토하고, 서버 게임정보 테이블로 전송하지 않는다.

## 6. 런타임 전용 정보

기본 정책상 영구 저장하거나 BackND로 매 프레임 전송하지 않는 정보:

- Current Floor, 현재 타이머, 현재 하트
- 플레이어 위치와 이동 방향
- 현재 페이지의 Enemy와 Item 배치
- 아이템 통과 중간 횟수
- 활성 버프, Fever 게이지와 필드 상태
- 결과 확정 전 임시 점수와 임시 재화

향후 중단 후 런 재개 기능을 추가한다면 별도 `RunCheckpoint` 계약으로
분리한다. 체크포인트는 일반 계정 프로필이나 랭킹 행에 혼합하지 않는다.

## 7. 게임 마스터 데이터

캐릭터, 아이템, Artifact, 효과, 강화 비용과 밸런스 정의는 사용자 보유
데이터가 아니다.

현재 원본:

- Character ScriptableObject
- `Items.csv`, `ItemIcons.csv`
- `Artifacts.csv`, `ArtifactEffects.csv`
- Character Skill 및 Upgrade 정의

운영 방식:

- 기본값은 앱 빌드에 포함한다.
- 원격 밸런스가 필요할 때 BackND 차트 또는 별도 마스터 데이터로 배포한다.
- 사용자 저장에는 ID와 적용 당시 `TableVersion`만 기록한다.
- 서버는 지급 및 소비 요청에서 해당 버전과 허용 범위를 검증한다.

## 8. 동기화 및 충돌 정책

| 데이터 | 충돌 정책 |
| --- | --- |
| 누적 BEST | 도달 층수 > 스코어 > 캐릭터 레벨의 최고값 |
| 현재 기간 랭킹 | PeriodId 내부 최고값, 기간 변경 시 독립 기록 |
| GameMoney, Ruby | 서버 거래 원장 기준, 클라이언트 잔액 덮어쓰기 금지 |
| 캐릭터 Level/XP | 최초 이전 후 서버 권한 |
| 캐릭터 보유 | 서버 권한, 최초 이전 시 검증된 보유 항목 합집합 |
| 선택/장착 | 서버 시각 기준 최신값, 보유 여부 검증 |
| Artifact 보유 | 서버 권한, 최초 이전 시 검증된 고유 항목 합집합 |
| Character Coin | 서버 거래 원장 기준 |
| 강화 Level | 서버 권한, 재료 소비와 단일 처리 |
| Pending Event | EventId 멱등 처리 후 성공 항목 삭제 |
| 유료 권리 | 영수증 검증 결과 기준 |

## 9. 향후 구현 우선순위

완료: 수집 저장소를 `gamerInDate`별로 격리해 계정 공유 위험을 제거했다.

완료: 서버 데이터 공통 계약과 재화 원장 클라이언트, 최초 이전, Pending Queue를 구현했다.

1. 유보: 캐릭터 성장/보유/선택/장착 콘솔 테이블 생성과 실제 계정 검증.
2. 유보: 재화 계정 A/B·타 기기 복구와 Ruby 지급/사용/잔액 부족 검증.
3. 출시 전: BackND Function으로 재화/XP 허용량, requestId와 동시 최초 이관을 검증한다.
4. Artifact, Character Coin, 강화 및 Pending Event를 이관한다.
5. 런 결과 정산 원장과 서버 검증을 연결한다.
6. 광고 제거 및 결제 권리를 영수증 검증 방식으로 전환한다.
7. 기간 랭킹 보상과 지급 원장을 구현한다.
8. 신규 캐릭터 추가 전 코드 카탈로그를 ScriptableObject 단일 원본으로 통합한다.
9. 캐릭터 실계정 검증 후 Manager/BackND 저장소의 추가 클래스 분리를 재평가한다.

각 단계는 로컬 데이터 백업, 최초 1회 이전 표시, 서버 저장 성공 확인,
재로그인 복구, 다른 기기 복구, 중복 요청 검증을 완료한 뒤 다음 단계로
진행한다.
