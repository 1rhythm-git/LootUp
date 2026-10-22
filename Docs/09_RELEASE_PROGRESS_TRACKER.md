# LootUp 출시 진척 계측 기준

기준 로드맵: `Docs/08_GOOGLE_PLAY_LAUNCH_ROADMAP.md`
측정 기간: 2026-10-19 ~ 2026-12-31
전체 기준 작업량: 104시간

## 1. 작업량 정의

`획득 작업량(Earned Hours)`은 실제로 소비한 시간이 아니라 로드맵에서 해당 결과물에 배정한 표준 시간 중 완료된 분량이다.

예시:

- 2시간 작업의 구현과 검증을 모두 완료: 2시간 획득
- 2시간 작업에서 구현만 끝나고 검증이 남음: 사전에 나눈 하위 작업 비율만 획득
- 예상보다 4시간이 걸린 2시간 작업을 완료: 2시간 획득
- 일정 외 긴급 작업: `OUT_OF_SCOPE`로 기록하고 출시 획득 작업량은 0시간

## 2. 기준 시간

| 구분 | 목표 시간 |
|---|---:|
| 월~금 | 1시간/일 |
| 토~일 | 2.5시간/일 |
| 일반 주차 | 10시간/주 |
| 전체 | 104시간 |

주말 실제 작업 가능 범위는 2~3시간이지만 달성률 계산에는 중앙값 2.5시간을 사용한다.

## 3. 작업 ID와 예산

| 작업 ID | 작업 영역 | 예산 |
|---|---|---:|
| STAB | 선결 안정화 | 6시간 |
| DESIGN | 레벨 디자인 및 BM | 14시간 |
| SETTINGS | 환경설정 | 6시간 |
| MISSION | 미션 | 12시간 |
| MAIL | 우편함 | 10시간 |
| UPGRADE | 업그레이드 | 12시간 |
| SHOP | 상점 | 10시간 |
| RELEASE | 통합 QA 및 출시 | 24시간 |
| BUFFER | 예비 버퍼 | 10시간 |
| OUT_OF_SCOPE | 출시 일정 외 작업 | 출시 진척 미반영 |

## 4. 계산 공식

- 당일 달성률 = 당일 획득 작업량 / 당일 목표 시간 × 100
- 주간 달성률 = 해당 월~일 획득 작업량 / 해당 주 계획 시간 × 100
- 누적 출시 진척률 = 누적 획득 작업량 / 104시간 × 100
- 일정 준수율 = 누적 획득 작업량 / 기준일까지 계획된 누적 시간 × 100
- 일정 대비 차이 = 누적 획득 작업량 - 기준일까지 계획된 누적 시간

판정:

| 달성률 | 판정 |
|---:|---|
| 100% 미만 | 미달 |
| 100% | 달성 |
| 100% 초과 | 초과 달성 |

측정 기간 전은 `일정 시작 전`, 12월 31일 이후는 `일정 종료`로 표시한다.

## 5. 작업 시작 보고 형식

```text
일정 계측
- 기준일: YYYY-MM-DD
- 로드맵 작업: TASK-ID / 작업명
- 예상 작업량: N.N시간
- 당일 목표: N.N시간
- 현재 당일 실적: N.N시간 (NN.N%)
- 완료 시 예상: NN.N% / 미달·달성·초과 달성
```

## 6. 작업 완료 보고 형식

```text
일정 계측
- 획득 작업량: N.N시간
- 당일: N.N / N.N시간, NN.N% / 판정
- 주간: N.N / N.N시간, NN.N% / 판정
- 누적: N.N / 104시간, NN.N%
- 일정 대비: +N.N시간 또는 -N.N시간, 일정 준수율 NN.N%

시각화
- 일정 및 진척 시각화: Docs/08_GOOGLE_PLAY_LAUNCH_ROADMAP.md 4.2절
- 진척률 게이지: Docs/Images/google-play-progress-gauges.svg
- 11주 일정표: Docs/Images/google-play-roadmap-timeline.svg
```

## 7. 기록 규칙

- 작업 시작 전 예상 작업량을 하위 작업 단위로 나눈다.
- 작업 종료 후 완료된 하위 작업만 합산해 CSV의 `EarnedHours`에 기록한다.
- 실적 원장을 변경한 작업은 측정 결과와 `Docs/08_GOOGLE_PLAY_LAUNCH_ROADMAP.md`, `Docs/Images/google-play-progress-gauges.svg`의 표시값을 같은 작업 안에서 동기화한다.
- 일정표 구조가 바뀌지 않더라도 현재 주차의 실적·잔여 작업 설명이 달라지면 `Docs/Images/google-play-roadmap-timeline.svg`의 설명과 막대를 함께 확인한다.
- 원장, 로드맵 본문과 시각화 수치가 다르면 일정 보고를 완료로 처리하지 않는다.
- 사용자에게 일정 및 진척률을 보고할 때는 로드맵 4.2절, 진척률 게이지와 11주 일정표를 클릭 가능한 파일 링크로 함께 제공한다.
- 같은 작업을 이어서 수행하면 날짜별로 행을 추가한다.
- `State`는 `Done`, `Partial`, `Blocked`, `OutOfScope` 중 하나를 사용한다.
- `Reference`에는 커밋 해시, 문서 경로 또는 검증 로그를 기록한다.
- 실적 수정 시 기존 행을 중복 추가하지 말고 해당 행을 정정한다.

## 8. 현재 기준선

- 2026-10-18: 계측 체계 수립일이며 출시 일정 시작 전이므로 누적 실적은 0시간으로 시작한다.
- 첫 계측 대상은 2026-10-19의 `STAB` 작업이다.

## 9. 계산 명령 예시

작업 시작 전 예상치:

```powershell
pwsh -NoProfile -File Tools/Measure-ReleaseProgress.ps1 -TaskId STAB -ProposedHours 1
```

실적 기록 후 결과:

```powershell
pwsh -NoProfile -File Tools/Measure-ReleaseProgress.ps1 -TaskId STAB
```

자동 처리용 JSON:

```powershell
pwsh -NoProfile -File Tools/Measure-ReleaseProgress.ps1 -TaskId STAB -AsJson
```
