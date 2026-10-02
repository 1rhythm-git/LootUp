# LootUp BackND 5.18.3 연동 계획

## 1. 검토 범위와 현재 상태

- 검토 대상: 프로젝트 루트의 `Backend-5.18.3.unitypackage`
- 확인된 SDK 어셈블리 버전: `5.18.3.0`
- 현재 상태: SDK 임포트와 인증 설정 및 Unity 6.3 Editor 컴파일 확인 완료
- 연동 대상: 기존 로그인 흐름과 LANK 전역 순위
- 유지 원칙: 게임 플레이 및 UI가 `BackEnd.Backend`를 직접 호출하지 않고 서비스 인터페이스를 경유

패키지에는 `Backend.dll`, Android용 `Backend.aar`, 설정 및 Editor DLL,
`LitJSON.dll`, `WebSocket4Net.dll`, `SendQueueMgr.cs`가 포함되어 있다.
Android AAR의 최소 SDK는 22이며 현재 프로젝트의 최소 SDK 25와 충돌하지 않는다.

현재 단계:

| 단계 | 상태 | 비고 |
| --- | --- | --- |
| P0 콘솔 인증 정보 | 완료 | 인증값은 SDK 설정 자산에만 저장 |
| P1 SDK 임포트/컴파일 | 완료 | Unity 6000.3.17f1 컴파일 성공 |
| P2 SDK 초기화 계층 | 완료 | Play Mode 실제 서버 초기화 성공 확인 |
| P3 로그인 연동 | 완료 | 회원가입·수동 로그인·ID/PW 기억하기 사용자 검증 완료 |
| P4~P5 LANK 연동 | 완료 | Editor 및 Android 실제 계정 검증 완료 |

## 2. 적용 전 수동 선행 작업

### P0. 뒤끝 콘솔 및 인증 정보

다음 항목은 코드 작업 전에 사람이 뒤끝 콘솔에서 생성하거나 확인해야 한다.

- 뒤끝 콘솔에서 LootUp 프로젝트 생성 또는 대상 프로젝트 확정
- Android 패키지 이름을 `com.lafgames.LootUp`과 일치하도록 등록
- `Client App ID`, `Signature Key` 발급 및 보관
- 개발/테스트 환경과 운영 환경 사용 정책 확정
- 테스트 계정 및 접근 허용 정책 확인

인증키는 채팅이나 일반 문서에 기록하지 않는다. SDK 임포트 후 Unity의
`The Backend > Edit Settings`에서 설정하고, 생성되는 설정 파일의 버전 관리
범위는 실제 파일 내용을 확인한 뒤 결정한다.

### P0. 로그인 계정 정책

로그인 ID와 표시 닉네임은 별도 값으로 분리한다.

- 로그인 ID: 변경 불가능한 Custom ID, 영문/숫자/밑줄/하이픈 `4~20자`
- 비밀번호: `6~32자`, 기억하기를 체크한 경우에만 로컬 난독화 저장
- 표시 닉네임: 한글/영문/숫자/밑줄 `2~12자`
- 일반 로그인: Account ID와 비밀번호만 입력
- 신규 가입: Account ID/비밀번호 입력 후 닉네임 설정으로 이어지는 2단계 UI
- `CHECK NAME`: 가입 전 로컬 형식 확인
- 실제 닉네임 중복 확인 및 생성: CustomSignUp 성공 후 인증 세션에서 처리
- 세션 정책: 앱 재실행마다 로그인 화면을 표시하고 로그인 버튼을 직접 눌러야 함
- 로그인 정보 기억: 사용자가 체크한 경우에만 ID와 마스킹된 비밀번호 입력값을 로컬에 복원
- 기억하기와 자동 로그인은 분리하며, 저장값이 있어도 로그인 버튼을 직접 눌러야 함
- 뒤끝 토큰: 재실행 자동 로그인에는 사용하지 않고 앱 시작 시 남은 세션을 로그아웃
- 기존 로컬 Guest 계정: 서버 계정으로 자동 이전하지 않음
- 서버 가입/로그인 후 사용자 식별자는 뒤끝 `gamerInDate`를 사용

Google 로그인은 `Backend-5.18.3.unitypackage`만으로 완료되지 않는다.
Google Play Console, OAuth, Play Games Services 설정과 별도 GPGS 플러그인이
필요하므로 Custom 로그인 안정화 이후 별도 단계로 진행한다.

### P0. 랭킹 테이블 및 정렬 정책

뒤끝 유저 랭킹은 콘솔에서 선택한 하나의 숫자 컬럼을 기준으로 정렬한다.
현재 확정된 `도달 층수 > 스코어 > 플레이 당시 캐릭터 레벨` 규칙을 그대로
보존하려면 아래 중 하나가 필요하다.

- 권장안: 세 값을 범위가 겹치지 않는 하나의 `rankValue`로 합성
- 대안: 층수 랭킹 조회 후 동률 구간을 클라이언트에서 재정렬
- 대안: 뒤끝 Function 등 서버 로직으로 별도 순위 계산

클라이언트 재정렬은 전체 순위 번호와 페이지 경계가 어긋날 수 있으므로
권장하지 않는다. 합성값을 사용할 경우 아래 상한이 먼저 확정되어야 한다.

- `highestFloor` 최대값
- `score` 최대값
- `characterLevel` 최대값
- `rankValue` 저장 타입과 오버플로 방지 범위

기간 랭킹용 게임 데이터 테이블 `LootUpRank`:

| 컬럼 | 용도 |
| --- | --- |
| `rankValue` | 뒤끝 랭킹 정렬용 합성 숫자 |
| `highestFloor` | 표시 및 검증용 최고 도달 층 |
| `score` | 표시 및 검증용 스코어 |
| `characterLevel` | 플레이 당시 캐릭터 레벨 |
| `characterId` | 전신 초상화 선택용 캐릭터 식별자 |
| `recordData` | 랭킹 추가 항목용 표시 데이터 JSON |
| `recordVersion` | 데이터 규칙 변경 대응 |

합성 인코딩 예약 범위는 `층수 0~9,999`, `점수 0~99,999,999`,
`캐릭터 레벨 1~999`로 제한하며 현재 게임의 실제 캐릭터 최대 레벨은
Lv.99이다. 합성식은
`층수 × 100,000,000,000 + 점수 × 1,000 + 레벨`이며 최대값은
뒤끝 정수 랭킹의 안전 범위인 `2^53` 미만이다.

뒤끝 콘솔에 다음 기간 랭킹 항목을 생성하고 활성화했다.

- `개발 > 게임정보 > 테이블`의 Private 테이블 이름: `LootUpRank`
- DOUBLE 컬럼: `rankValue`
- INT 컬럼: `highestFloor`, `score`, `characterLevel`, `recordVersion`
- STRING 컬럼: `characterId`, `recordData`
- 모든 사용자 정의 컬럼: NULL 허용
- `운영 > 리더보드`의 리더보드 이름: `LootUp Global Rank`
- 대상: 유저, 그룹 구분 없음, 매일 00:00(UTC+09:00) 초기화
- 정렬 대상: `LootUpRank.rankValue`
- 정렬 순서: 내림차순
- 추가 항목: `recordData`

게임정보 테이블은 실제 사용자 기록을 저장하고, 운영 리더보드는 해당
테이블의 숫자 컬럼을 기준으로 순위를 계산하는 별도 기능이다.
코드는 `Backend.Leaderboard.User.GetLeaderboards`에서 테이블명, 정렬 컬럼, 내림차순,
추가 항목이 모두 일치하는 랭킹 UUID를 자동 탐색한다. UUID를 소스에
직접 입력하지 않는다.

Lobby `BEST`는 리더보드 기간과 무관한 계정 누적 최고 기록이다. 이를
리더보드 원본 행과 분리하기 위해 다음 Private 테이블을 별도로 사용한다.

- `개발 > 게임정보 > 테이블`의 Private 테이블 이름: `LootUpBest`
- 컬럼과 타입은 `LootUpRank`와 동일
- 모든 사용자 정의 컬럼: NULL 허용
- 운영 리더보드 연결 없음
- 자동 초기화 및 기간 종료 삭제 대상 아님

로그인 시 계정별 로컬 BEST, 기존 `LootUpBest`, 최초 이전 시 기존
`LootUpRank` 기록을 비교해 `도달 층수 > 스코어 > 캐릭터 레벨` 기준의
최고 기록을 양쪽에 동기화한다. 게임 종료 시에는 `LootUpBest`의 누적
최고 기록과 현재 기간의 `LootUpRank` 기록을 각각 갱신한다.

## 3. 자동 작업과 적용 순서

### P1. SDK 임포트 및 컴파일 확인

- `Backend-5.18.3.unitypackage` 임포트
- 기존 플러그인 및 Android 의존성과 충돌 여부 확인
- Unity 6.3에서 Editor 컴파일 확인
- Android Plugin Import Settings 확인
- Player Settings의 IL2CPP 및 API Compatibility 설정 확인

### P2. SDK 초기화 계층

- 앱 생명주기 동안 한 번만 초기화하는 Bootstrap 추가
- 초기화 성공/실패 상태와 재시도 가능 오류 분리
- 비동기 콜백을 Unity 메인 스레드로 전달하는 처리 추가
- 씬 오브젝트 수동 배치 없이 런타임 Bootstrap에서 생성
- SDK를 사용할 수 없을 때 로컬 서비스로 유지하는 개발용 전환점 제공

### P3. 로그인 연동

- 기존 `IAuthenticationService`를 구현하는 BackND 어댑터 추가
- Custom 회원가입, 수동 로그인, 앱 시작 시 기존 세션 로그아웃 연결
- 회원가입 후 닉네임 등록 및 중복 오류 매핑
- SDK 오류 코드를 기존 `AuthenticationFailure`로 변환
- 로그인 성공 시 기존 `UserProfileManager` 갱신
- 로그인 성공 시 `gamerInDate`별 로컬 사용자 프로필 저장소로 전환
- 로그인 성공 시 `gamerInDate`별 로컬 캐릭터 성장 저장소로 전환
- 계정 전환 시 캐릭터 선택 런타임 캐시 초기화
- 기존 로컬 구현은 Editor 및 오프라인 테스트 대체 구현으로 유지

### P4. LANK 서비스 경계

- `ILeaderboardService`와 전송/조회 모델 추가
- 인증 계정 변경 시 랭킹 서비스를 교체하고 이전 계정 캐시 제거
- LANK UI에 로딩, 빈 결과, 오류, 재시도, 페이지 상태 추가
- MY LANK는 얼굴 초상화, 전체 목록은 캐릭터 전신 초상화 규칙 유지
- 서버 설정 전 로컬 표시를 유지하되 BackND 로그인 후에는 서버 결과만 표시

### P5. 뒤끝 랭킹 저장 및 조회

- 게임 종료 시 최고 기록 행 생성 또는 갱신
- 기존 행을 조회해 `inDate`를 복구하고 중복 Insert 방지
- `Backend.Leaderboard.User.UpdateMyDataAndRefreshLeaderboard`로
  기록과 리더보드를 함께 갱신
- 자동 탐색한 랭킹 UUID로 전체 순위와 MY LANK 조회
- 표시용 층수, 스코어, 캐릭터 레벨, 캐릭터 ID 역직렬화
- 초기화 후 MY LANK가 없으면 이전 `LootUpRank` 기록보다 낮아도 현재 주기의 첫 플레이 기록 제출
- 현재 주기에 이미 등록된 계정은 `도달 층수 > 스코어 > 캐릭터 레벨` 기준 최고 기록만 갱신
- 네트워크 및 콘솔 설정 실패 시 명시적 오류와 재시도 상태 사용
- 중복 전송과 연속 버튼 입력 방지
- 현재 SDK `5.18.3`에 포함된 신규 `Backend.Leaderboard.User` API 사용

### P6. 누적 BEST와 기간 랭킹 분리

- Private `LootUpBest`에 계정 전체 기간 최고 기록 저장
- 로그인 시 계정별 로컬 BEST와 서버 누적 BEST를 양방향 최고값 기준 동기화
- 게임 종료 시 누적 BEST와 현재 기간 랭킹을 독립적으로 갱신
- 같은 기기의 계정별 프로필, 재화, 특성을 `gamerInDate`별로 격리
- 기존 공용 로컬 프로필은 저장된 소유자 ID가 로그인 계정과 일치할 때만 이전
- `LootUpBest` 설정이 아직 없거나 일시 실패해도 현재 기간 랭킹 제출은 계속 수행

기간 보상 구현 전 확정할 서버 계약:

- 기간/시즌 식별자와 종료 시각
- 종료 순위 및 보상 스냅샷
- 지급 상태, 지급 완료 시각, 지급 요청 고유 키
- 보상 지급 재시도와 중복 지급 방지
- 지급 완료 확인 후 이전 `LootUpRank` 기록 삭제

운영 리더보드의 초기화 설정을 변경해야 하면 기존 리더보드를 삭제하고
같은 `LootUpRank.rankValue`, 내림차순, `recordData` 추가 항목으로 새로
생성한다. 클라이언트는 UUID를 자동 탐색하므로 동일 테이블 계약을 유지하면
소스 수정은 필요 없다. 교체 시 동일 계약의 리더보드를 동시에 둘 이상
활성화하지 않고, 재로그인 또는 앱 재시작으로 메모리의 UUID 캐시를 갱신한다.

### P7. 재화 서버 원장과 최초 이관

- `ICurrencyLedgerService`와 BackND 구현을 추가해 게임 로직의 SDK 직접 참조 차단
- 로그인 시 서버 프로필이 없으면 계정별 로컬 `GameMoney`, `Ruby`를 최초 1회 이전
- 서버 프로필이 있으면 서버 잔액으로 계정별 로컬 캐시 복구
- `requestId` 조회 후 중복 요청은 잔액을 다시 변경하지 않음
- 프로필 잔액 갱신과 원장 행 추가를 `TransactionWriteV2`로 함께 처리
- 네트워크 실패 요청을 계정별 Pending Queue에 저장하고 다음 로그인에 재처리
- 계정 전환 중 완료된 이전 계정 응답은 현재 계정 캐시에 적용하지 않음
- 런 결과 게임머니 요청 ID는 `run:{runId}:game-money` 사용

뒤끝 콘솔에 다음 Private 테이블을 생성해야 한다.

`LootUpPlayerProfile`:

| 컬럼 | 타입 | NULL | 용도 |
| --- | --- | --- | --- |
| `schemaVersion` | INT | 허용 | 프로필 스키마 버전 |
| `migrationVersion` | INT | 허용 | 최초 로컬 이전 완료 버전 |
| `gameMoney` | INT | 허용 | 서버 권한 게임머니 잔액 |
| `ruby` | INT | 허용 | 서버 권한 Ruby 잔액 |
| `lastRequestId` | STRING | 허용 | 마지막 반영 요청 추적 |

갱신 시각은 BackND가 자동으로 제공하는 시스템 `updatedAt` 컬럼을 사용하며,
동일 이름의 사용자 컬럼은 생성하거나 클라이언트 `Param`에 포함하지 않는다.

`LootUpCurrencyLedger`:

| 컬럼 | 타입 | NULL | 용도 |
| --- | --- | --- | --- |
| `schemaVersion` | INT | 허용 | 원장 스키마 버전 |
| `requestId` | STRING | 허용 | 재시도 중복 확인용 고유 키 |
| `currencyType` | STRING | 허용 | `GameMoney` 또는 `Ruby` |
| `deltaAmount` | INT | 허용 | 양수 지급, 음수 사용 |
| `balanceAfter` | INT | 허용 | 처리 완료 후 잔액 |
| `reason` | STRING | 허용 | 지급·사용 사유 |
| `runId` | STRING | 허용 | 런 보상 연계 ID, 비런 요청은 빈 값 |
| `createdAt` | STRING | 허용 | UTC ISO-8601 요청 시각 |

두 테이블은 운영 리더보드에 연결하지 않는다. 클라이언트 구현은 완료됐지만
실제 서버 검증은 위 테이블 생성 후 진행한다.

### P8. 캐릭터 성장 서버 이관

- `ICharacterProgressionService`는 Lobby/InGame의 동기식 로컬 캐시 계약으로 유지
- `ICharacterProgressionSyncService`와 `BackndCharacterProgressionService`로 SDK 호출 격리
- 로그인 시 서버 데이터가 없으면 네 캐릭터 진행과 Loadout을 최초 1회 이전
- 서버 데이터가 있으면 서버 값을 원본으로 계정별 로컬 캐시 교체
- XP 요청 ID는 `run:{runId}:character-xp:{characterId}` 형식 사용
- 진행 행 갱신과 XP 원장 삽입을 `TransactionWriteV2`로 함께 처리
- XP, 보유 상태, Loadout 실패 요청을 계정별 Pending Queue로 재처리
- XP·보유·Loadout은 Pending 저장에 성공한 뒤 로컬 캐시에 반영
- 보유 상태 변경은 `isOwned` 전용 요청으로 처리해 레벨/XP 원장을 우회하지 않음
- 로그인 또는 계정 전환 중 이전 계정 응답이 현재 캐시에 반영되지 않도록 구성 버전 검사
- BackND 캐릭터 초기 동기화 또는 Pending 재처리 실패 시 인증 완료 차단

뒤끝 콘솔에는 `Docs/07_BACKND_DATA_MIGRATION_PLAN.md` 3.2의 스키마대로
Private `LootUpCharacterProgress`, `LootUpPlayerLoadout`,
`LootUpCharacterProgressLedger`를 스키마 정의 및 활성 상태로 생성했다. 코드 구현과
정적 컴파일도 완료됐으며, 다음 단계는 실제 계정 최초 이관과 재로그인 검증이다.

### P9. 선택 작업

- Google Play Games Services 로그인
- 기존 로컬 계정과 소셜 계정 연결 또는 이전
- 서버 Function을 사용한 기록 위변조 검증
- 운영 로그, 제한 정책, 장애 대응 및 데이터 마이그레이션

## 4. 수동/자동 작업 분류

| 구분 | 작업 | 담당 | 선행 조건 |
| --- | --- | --- | --- |
| 수동 | 뒤끝 프로젝트 및 앱 등록 | 사용자 | 뒤끝 콘솔 접근 |
| 수동 | Client App ID, Signature Key 설정 | 사용자 | 앱 등록 완료 |
| 수동 | `LootUpRank`, `LootUpBest` 테이블과 유저 랭킹 생성 | 사용자 | 정렬 정책 확정 |
| 완료 | `LootUpPlayerProfile`, `LootUpCurrencyLedger` Private 테이블 생성 | 사용자/Codex | 실제 계정 검증 완료 |
| 수동 | 랭킹 상한과 초기화 주기 확정 | 사용자 | 게임 운영 정책 |
| 수동 | Google Play/OAuth/GPGS 설정 | 사용자 | Google 로그인 진행 시 |
| 자동 | SDK 임포트 및 컴파일 수정 | Codex | 인증 정보 준비 |
| 자동 | 초기화 및 인증 서비스 구현 | Codex | 계정 정책 확정 |
| 자동 | LANK 서비스 및 UI 상태 구현 | Codex | 테이블/랭킹 UUID 준비 |
| 자동 | 기록 저장, 전역 순위, MY LANK 연결 | Codex | 랭킹 정책 확정 |
| 자동 | 계정별 누적 BEST와 기간 랭킹 저장 분리 | Codex | `LootUpBest` 테이블 준비 |
| 자동 | 재화 서버 원장, 최초 이전, Pending Queue 구현 | Codex | BackND SDK 5.18.3 |
| 자동 | 캐릭터 성장/보유/선택/장착 서버 이관 구현 | Codex | BackND SDK 5.18.3 |
| 수동 | 캐릭터 진행, Loadout, XP 원장 Private 테이블 생성 | 사용자/Codex | P8 스키마 확정 |
| 공동 | Editor/Android 실제 계정 검증 | 사용자/Codex | 각 구현 단계 완료 |

## 5. 완료 상태와 후속 작업

P0 인증 설정, P1 SDK 임포트, P2 SDK 초기화, P3 Custom 로그인,
P4~P5 LANK 클라이언트 구현, 기간 랭킹 서버 콘솔 설정과 실제 계정 검증을 완료했다.
P6 클라이언트 분리 구현을 완료했으며 `LootUpBest` 콘솔 테이블 생성과
Editor/Android 실기기 검증도 완료했다.
재화 서버 원장 클라이언트와 최초 이관 로직을 구현했으며
`LootUpPlayerProfile`, `LootUpCurrencyLedger` 콘솔 생성과 실제 계정의 최초 이관,
재로그인 중복 방지, Pending 재전송, 동일 `requestId` 멱등성 검증을 완료했다.
캐릭터 서버 이관 클라이언트, 최초 이관, 계정별 Pending Queue와 XP 원장을
구현했다. XP 선반영, 보유 상태의 전체 진행 덮어쓰기, Pending 저장 실패 무시를
수정하고 초기 동기화 실패 시 로그인 완료를 차단했다. 콘솔 테이블 생성과 실제
계정 검증은 사용자 요청으로 유보했다.

- Unity Editor에서 `LEADERBOARD NOT FOUND` 오류가 사라지고 LANK 조회 정상 동작 확인
- 게임 종료 후 `LootUpRank` 행과 `LootUp Global Rank` 반영 확인
- Android 실제 계정에서 로그인, 계정별 기록과 MY LANK 표시 확인
- `LootUpBest` 생성 후 Lobby BEST와 기간 랭킹 분리 동작 확인

향후 서버 이관 대상과 로컬 유지 정보, 충돌 정책 및 테이블 경계는
`Docs/07_BACKND_DATA_MIGRATION_PLAN.md`를 기준으로 진행한다. 다음 우선순위는
캐릭터 성장 콘솔/실계정 검증, 재화 계정 A/B·타 기기/Ruby 후속 검증,
Artifact/Character Coin, 런 정산 원장 순서다.

다음 서버 연동 후보는 기간 랭킹 보상/지급 원장, Google 로그인,
기록 위변조 검증과 운영 장애 대응이다.
