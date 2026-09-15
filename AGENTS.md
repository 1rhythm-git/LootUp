# LootUp – Unity Project Agent Instructions

## Codex 필수 작업 지침

이 섹션의 지침은 아래 프로젝트 설명 및 기존 개발 지침보다 우선한다.

### 출력 언어

- 모든 설명, 보고, 요약, 체크리스트는 한국어로 작성한다.
- 코드 블록은 원문 형식을 유지하되 코드 주석(`//`, `/* */`)은 한국어로 작성한다.
- 에러 로그, 경로, 클래스명, 메서드명, Unity API 명칭은 원문을 유지한다.

### 작업 시작 및 브리핑

- 작업 시작 전 `목표 / 현재 상태 / 기대 동작`을 3줄로 정리한다.
- Codex 세션을 시작하거나 현재 상태 및 다음 작업을 브리핑할 때는 반드시 `Docs/05_WORK_LOG.md`의 마지막 작업 기록과 다음 작업 후보를 먼저 확인한다.
- 작업 로그에 `재시작 최우선`으로 표시된 미완료 항목이 있으면 다른 다음 작업 후보보다 먼저 브리핑하고 처리한다.
- 브리핑 전 `git status`와 최근 `git log`를 함께 확인해 작업 로그가 실제 저장소 상태 및 최근 커밋과 일치하는지 검증한다.
- 과거 프로젝트 개요나 `Future Plans`만 근거로 이미 완료된 작업을 다음 작업으로 보고하지 않는다.

### 작업 결과 보고

- 작업 결과는 `변경 요약 -> 영향 범위 -> 테스트 체크리스트` 순서로 보고한다.
- 체크리스트의 완료 항목은 `[O]`, 미완료 또는 확인 필요 항목은 `[ ]`로 표기한다. `[x]` 표기는 사용하지 않는다.
- 사용자가 "통합본", "전체 코드", "전체 스크립트"를 요청하면 해당 파일을 처음부터 끝까지 전체 출력한다.

### Git 관리

- 작업 시작 전과 완료 후 `git status --short`를 확인해 기존 변경과 이번 작업 변경을 구분한다.
- 사용자가 만든 기존 변경이나 현재 작업과 무관한 변경은 되돌리거나 함께 수정하지 않는다.
- 커밋 또는 결과 보고 전에 `git diff --check`와 대상 파일의 diff를 확인한다.
- 줄바꿈(LF/CRLF), 인코딩, 파일 모드 변경만으로 파일 전체가 수정되지 않도록 저장소의 기존 형식을 유지한다.
- 줄바꿈이나 포맷 변경으로 대규모 diff가 감지되면 실제 내용 변경과 분리해 보고하고, 사용자 승인 없이 일괄 정규화하지 않는다.
- 작업 로그에는 완료 내용뿐 아니라 변경된 주요 파일, 검증 결과, 남은 이슈, 관련 커밋 또는 작업 기준을 기록한다.

### 작업 태도

- 사용자가 요청하지 않은 기존 기능이나 로직은 삭제하거나 변경하지 않는다.
- 불확실한 부분은 되묻기를 최소화하고 합리적으로 가정해 진행하되, 마지막 체크리스트에 확인 항목을 남긴다.

### 캐릭터 아트 제작 지침

- 캐릭터 디자인 기반 애니메이션은 `Idle 2 / Walk 2 / Run 2`의 6프레임 구성을 기본으로 한다.
- 디자인 기반 6프레임은 정면 포즈를 사용하지 않고 항상 화면 오른쪽을 향한 측면 기반으로 제작한다.
- 얼굴, 몸통, 골반, 발과 이동 실루엣이 모두 같은 측면 방향을 읽을 수 있어야 한다.
- Walk와 Run 2프레임은 좌우 손발이 확실히 교차되는 반대 포즈로 제작한다. 두 프레임에서 같은 손과 같은 발이 계속 앞에 남아 있으면 걷기/달리기가 아니라 끌고 가는 동작처럼 보이므로 금지한다.
- Walk는 앞발 접지와 뒷발 회수, Run은 더 큰 보폭과 팔 스윙 및 공중감을 명확히 구분한다.
- 화면 왼쪽 이동은 별도 좌측 프레임을 만들지 않고 런타임 Sprite 좌우 반전으로 처리한다.
- 충돌박스는 모든 캐릭터가 공통 규격을 사용한다.
- 캐릭터 이미지/스프라이트의 실제 표시 크기는 충돌박스보다 작아서는 안 되며, 다소 큰 수준은 허용할 수 있다. 충돌박스보다 얼마나 커도 되는지는 추후 테스트로 결정한다.
- 인게임 개별 프레임은 398×435 RGBA, Point 필터, Mipmap Off, Alpha Is Transparency를 기본 규격으로 사용한다.
- 로비 초상화는 1024×1536 RGBA 투명 PNG와 2등신 전신 픽셀아트를 기본 규격으로 사용한다.

## Tech Stack

- Unity 6.3 LTS (6000.3.17f1)
- C# scripts under `Assets/Scripts/**`
- Target platforms: Android, Windows (Editor)

## Core Game Loop

- Player starts at the **left side of 1F**, moves to the right, takes an elevator, and keeps climbing up.
- Building is represented as a **UI grid 8 (columns) x 10 (visible floors)**.
- Player has **3 heart HP**. When HP reaches 0, it is Game Over.
- There is a **timer**; when it reaches 0, it is Game Over (Time Over).
- Reaching higher floors is the main progress metric. We record the highest floor when the game ends.
- Screen rules:
  - Resolution target: 9:20 portrait.
  - Only the current floor is fully bright.
  - Floors not yet reached are blacked out.
  - Floors already passed are slightly dark.

## Current Scene / UI Structure

- Main Game scene structure:
  - `Canvas/TopUI`:
    - Character portrait (later), level, nickname, hearts, time, floor text, options.
  - `Canvas/MiddleUI`:
    - Building background image, 8x10 grid, player, enemies, items.
    - `GridPanel` with `BuildingGridUI`:
      - Manages 8x10 cells and floor brightness (unreached/current/passed).
    - `LinePanel` with `LineManagerUI`:
      - Vertical lines aligned to grid column boundaries. Used later to highlight enemy path.
    - `FloorLinePanel` with `FloorLineUI`:
      - Horizontal floor boundary lines aligned to grid row boundaries.
  - `Canvas/BottomUI`:
    - Item slots.
    - Banner ad area (shares visibility/removal state with Lobby).

## Important Scripts (so far)

- `Assets/Scripts/Core/World/BuildingGridUI.cs`
  - Creates and manages the 8x10 cell grid UI.
  - Controls floor colors: unreached (black), current (bright), passed (dark).
  - Exposes `SetCurrentFloor(int floorIndex)` and `GetCellRectTransform(int column, int row)`.

- `Assets/Scripts/Core/World/LineManagerUI.cs`
  - Creates **vertical lines** aligned with the grid column boundaries.
  - Uses `BuildingGridUI.GetCellRectTransform()` to compute exact positions.
  - There are 9 lines (0..8) for 8 columns (including left/right walls).
  - Provides `SetLineVisible(int columnIndex, bool visible, float alpha = 1f)` for enemy highlighting.

- `Assets/Scripts/Core/World/FloorLineUI.cs`
  - Creates **horizontal lines** aligned with the grid row boundaries.
  - Uses `BuildingGridUI` cell positions to compute positions.
  - There are 11 lines (0..10) for 10 visible floors.

- `Assets/Scripts/Core/World/FloorDebugTester.cs`
  - Temporary debug utility to visually test floor brightness by changing `currentFloorIndex` in the inspector.

## Coding Guidelines

- Do NOT remove or break existing features unless explicitly asked.
- Prefer adding new methods or classes rather than heavily changing existing public APIs.
- Follow Unity’s standard C# style:
  - PascalCase for methods and public fields.
  - camelCase for private fields.
- Apply SOLID principles when designing or extending systems (SRP/LSP/ISP/OCP/DIP) to keep responsibilities clear and dependencies abstracted.
- When adding new scripts:
  - Place them in an appropriate folder (`Core/World`, `Core/UI`, `Systems`, etc.).
  - Add clear comments in English where new or modified logic is introduced.

## Safety / Scope

- Work only inside this workspace; do not touch system files.
- When modifying scripts, keep them **compilable for Unity 6.3 LTS**.
- Assume the project uses **Input System package (new)**, not the old `UnityEngine.Input` API, unless a script is explicitly using the old system.

## Future Plans (for context)

- Add TopUI HUD (hearts, timer, floor label).
- Implement Player controller, enemies, elevator behavior and their interaction with the grid.
- Introduce items and leveling system using ScriptableObject assets.
- Plan to integrate backend services (login/signup/leaderboard) using BackND (https://backnd.com/ko/). When implementing, keep client-side code decoupled via interfaces/service abstractions to allow backend provider swaps (e.g., backend SDK wrappers, async calls with error handling and retries).
