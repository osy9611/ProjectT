# InputManager 리팩토링 보고서

작성일: 2026-10-03
대상 커밋: `e891bba5`(1단계) · `020a21ec`(2단계) · `4bcdd06c`(3·4단계), 기준 `4ccef66e`
설계 문서: [INPUT_MANAGER_DESIGN.md](INPUT_MANAGER_DESIGN.md)

## 1. 요약

입력을 Actor별 Controller에서 `Global.Input`(InputManager) 중심 구조로 옮겼다. PS5·Xbox·Switch 대응에 필요한 단일 유저 페어링, 연결 해제 처리, UI/게임 입력 분리, Switch 확인/취소 규약을 넣었다.

| 항목 | 결과 |
| --- | --- |
| 변경 규모 | 제품 파일 15개, +484 / −377 |
| LifecycleTests | 174 통과 (작업 전 156) |
| UITests | 37/37, 입력 9/9 |
| NotificationTests | 54/54 |
| Unity 참조 컴파일 | Assembly-CSharp, Assembly-CSharp-Editor 통과 |
| 미검증 | Play Mode, 실제 패드 연결 해제, 콘솔 실기 (8절) |

## 2. 진행 방식

단계마다 아래 순서로 진행했고, 단계가 끝날 때마다 커밋했다.

1. 구현 에이전트: 설계 결정을 명시한 지시서로 코드와 테스트를 작성한다.
2. 코드 품질 리뷰 에이전트(읽기 전용): AGENTS.md 규칙, 가독성, 불필요한 구조를 본다.
3. 버그 가능성 리뷰 에이전트(읽기 전용): 실제 Unity·Input System 소스와 대조해 실패 시나리오를 찾는다.
4. 리드(본인): 두 리뷰를 채택하거나 기각하고, 수정 지시를 내리고, 최종 코드를 직접 읽고 테스트로 확인한다.

| 단계 | 규모 | 구현 | 품질 리뷰 | 버그 리뷰 |
| --- | --- | --- | --- | --- |
| 1. 소유권 이전 | 대 (제품 약 250줄 + 테스트 재작성) | Opus | Sonnet | Opus |
| 2. UI 포커스·EventSystem | 대 (UI·씬 YAML 포함) | Opus | Sonnet | Opus |
| 3·4. 페어링·플랫폼 규약 | 중 (약 150줄) | Opus | Sonnet | Opus |

품질 리뷰는 규칙 대조 위주라 Sonnet에 맡겼다. 구현과 버그 리뷰는 Input System 내부 동작을 추적해야 해서 Opus를 썼다.

## 3. 최종 구조

```
Global.Input (InputManager, 가장 먼저 등록 → Global.Awake 안에서 Ready)
 ├─ InputSystem.actions (프로젝트 전역 에셋, 복제·파괴하지 않음)
 ├─ InputUser 1명: 페어링 / 자동 전환 / 분실·복구 / IsDeviceLost / CurrentDevice
 ├─ EventSystem + InputSystemUIInputModule (같은 에셋의 UI 맵)
 ├─ ApplyInputStateInternal: 모든 입력 게이팅을 한 곳에서 계산
 │    게임 입력 = 씬 허용 && !UI 포커스 && !장치 분실 && !전환 프레임
 │    UI 모듈   = 씬 허용
 │    UI 탐색   = UI 포커스 && 포커스 다음 프레임 && !전환 프레임
 └─ Controller (Player 맵 전용, InputContext 우선순위 전달)
        ▲ 활성화 시 등록 / 비활성화·해제 시 제거 (BaseActor 생명주기)
UIContainer ── 보이는 Static 스택 최상단 변화 → Global.Input.SetUIFocused + 패드 선택
SceneManager ── 전환 잠금 → Global.Input.SetInputAllowed
```

### Actor 사용 예

```csharp
public override void OnInit()
{
    onFoot = new InputContext("OnFoot", 0)
        .Bind("Player/Move", ctx => move = ctx.ReadValue<Vector2>(),
            eInputEvent.Performed | eInputEvent.Cancel,
            onReset: () => move = Vector2.zero)
        .Bind("Player/Jump", ctx => Jump(), eInputEvent.Performed);

    RegisterInputContext(onFoot);
}
```

등록은 한 번만 하면 된다. Actor 활성화·비활성화, 풀 반환, 씬 종료에 맞춰 Controller 등록과 해제가 자동으로 처리된다. 상황 전환(탑승 등)은 `RegisterInputContext` / `UnregisterInputContext`로 한다.

## 4. 단계별 변경 내역

### 1단계: Controller 소유권 이전 (`e891bba5`)

| 파일 | 변경 | 이유 |
| --- | --- | --- |
| Managers/InputManager.cs (신규) | 단일 Controller 소유, 씬 잠금 전달 | 패드 페어링·분실 상태는 Actor나 씬보다 오래 유지되어야 한다 |
| Common/Global.cs | `Global.Input`, **가장 먼저 등록** | 초기화가 동기라 Awake 안에서 Ready가 된다. 첫 씬 Actor가 비동기 초기화(Patch)보다 먼저 활성화되어도 안전하다 |
| Input/Controller.cs | 자기 맵만 Enable, 생명주기 API `internal` | 생성 주체가 InputManager 하나뿐이다 |
| Input/InputContext.cs | `internal BaseActor Actor` | 한 컨텍스트를 두 Actor가 등록하면 한쪽 해제가 다른 쪽 입력까지 지우는 문제를 막는다 |
| Actor/BaseActor.cs | `CreateController`·에셋 복제 제거, `RegisterInputContext`/`UnregisterInputContext` | 등록·해제를 생명주기에서 자동으로 처리해 호출 지점마다 수동으로 넣지 않게 한다 |
| Scene/SceneBase.cs | 씬별 입력 허용 상태·이벤트 제거 | 사용처가 Actor 입력뿐이었고 InputManager로 일원화되었다 |
| Managers/SceneManager.cs | 씬 순회 대신 `Global.Input.SetInputAllowed` | 공통 진입점 한 곳에서 처리한다 |

### 2단계: UI 포커스와 EventSystem 통합 (`020a21ec`)

| 파일 | 변경 | 이유 |
| --- | --- | --- |
| InputManager.cs | EventSystem·UI 모듈 생성·소유, 단일 게이팅 메서드 | 패드 A 하나로 UI Submit과 Player Jump가 함께 실행되던 이중 경로를 없앤다 |
| Controller.cs | `Init(asset, mapName)`, Init 이후 다른 맵은 건드리지 않음, `SetActiveMapInternal` 제거 | UI 모듈이 UI 액션의 활성 상태를 스스로 관리하므로 맵 전체를 켜고 끄면 서로 충돌한다 |
| UGUI/UI/UIContainer.cs | `RefreshFocusInternal` / `UpdateSelectionInternal` | 스택 최상단이 실제로 바뀔 때만 포커스를 통지하고 선택을 옮긴다 |
| UGUI/UI/UIBase.cs | `[SerializeField] firstSelected`, 마지막 선택 기억 | 마우스가 없는 패드 환경에서 UI 조작의 시작점이 필요하다 |
| Scenes/Title·Download·Test | 씬에 배치된 EventSystem 제거 | EventSystem을 InputManager 하나로 둔다. 제거한 쪽은 패키지 기본 에셋을 쓰고 있었다 |

### 3·4단계: 페어링과 플랫폼 규약 (`4bcdd06c`)

| 파일 | 변경 | 이유 |
| --- | --- | --- |
| InputManager.cs | InputUser 1명, 시작 장치(PC는 키보드·마우스, 콘솔은 패드), 버튼 입력 시 자동 전환 | 콘솔은 유저-장치 페어링이 기본이고, PC 개발 중에는 장치 전환이 자연스러워야 한다 |
| InputManager.cs | `IsDeviceLost`, `CurrentDevice`, 분실 시 게임 입력만 차단 | 연결 해제 일시정지 UI는 계속 조작할 수 있어야 한다 |
| Event/NotificationId.cs | `InputDeviceLost` / `InputDeviceRegained` / `InputControlsChanged` | 새 이벤트를 만들지 않고 기존 통지 경로를 쓴다 |
| InputManager.cs | Switch에서 UI Submit/Cancel 패드 버튼 교체 | 닌텐도는 오른쪽(A)이 확인이다 |
| InputSystem_Actions.inputactions | UI Submit/Cancel에 Gamepad 전용 바인딩 추가, 용도(usage) 바인딩에서 Gamepad 그룹 제거 | 기존에는 `*/{Submit}` 용도 바인딩만 있었다. 이를 오버라이드하면 키보드까지 바뀌므로 패드 바인딩만 따로 뺐다 |

## 5. 리뷰 판단과 피드백

리드가 판단한 핵심 항목이다. "채택"은 수정을 지시한 항목, "기각"은 근거를 남기고 그대로 둔 항목이다.

### 1단계

| 출처 | 지적 | 판단 | 근거 |
| --- | --- | --- | --- |
| 버그 | Global이 비동기로 초기화되어, Input이 준비되기 전에 Actor가 활성화되면 예외가 나고 Update도 영구히 멈춤 | **채택** | ManagerHost는 첫 실제 await 전까지 동기로 진행된다. 그래서 동기 초기화인 Input을 가장 먼저 등록하는 것이 가장 단순한 해결이다. 별도 대기 로직이 필요 없다 |
| 버그 | 같은 컨텍스트를 두 Actor가 등록 | 채택 | 조용히 남의 입력을 지우는 버그는 찾기 어렵다. 등록 시점에 예외로 막는다 |
| 버그·품질 | 등록 실패 롤백에 flag+finally를 써서 읽기 어렵고, 조회 위치 때문에 부분 상태가 남음 | 채택 | 실제 롤백이므로 AGENTS가 허용하는 catch로 바꿨다. 이미 Controller에 붙은 경우도 함께 분리한다(리드가 직접 보완) |
| 버그 | 첫 전환 전에 입력이 허용됨 | 채택했다가 2단계에서 **번복** | 부팅 씬이 SceneManager 전환 없이 UI 버튼으로만 나간다는 사실이 확인되었다. 잠금의 소유자인 SceneManager의 기본값(허용)과 맞췄다 |
| 품질 | copy-on-write 리스트를 일반 리스트로 바꾸라 | 기각 | onReset 콜백이 순회 중 목록을 바꿀 수 있다. Controller와 InputContext가 같은 패턴을 쓰므로 일관성을 유지한다 |
| 품질 | `UnregisterInputContext`는 사용처 없는 코드 | 기각 | 내부 코드가 아니라 Actor 작성자용 공개 API다(탑승 등 런타임 교체). 없으면 Controller를 직접 건드려 생명주기 추적이 깨진다 |
| 품질 | `SetInputAllowed`는 전달용 래퍼 | 기각 | 3단계에서 장치 분실·UI 포커스를 함께 계산하는 진입점이 되었다 |

### 2단계

| 출처 | 지적 | 판단 | 근거 |
| --- | --- | --- | --- |
| 구현 | 부팅 시 UI 모듈이 꺼져 Title에서 나갈 수 없음 | **채택** | 1단계 판단의 부작용이다. 씬 잠금 기본값을 허용으로 바꿨다 |
| 구현 | 마우스로 선택된 HUD 버튼이 게임 중 패드 Submit에 눌림 | 채택 | 선택 해제만으로는 막을 수 없다. Unity 내장 `sendNavigationEvents`로 탐색 이벤트 자체를 끈다 |
| 버그 | 게임 콜백(Jump)이 UI를 연 프레임에 같은 Submit이 새 버튼을 누름 | 채택 | 콘솔에서 흔한 버그다. 포커스를 얻은 다음 프레임부터 탐색을 허용한다 |
| 버그 | 빈 곳을 클릭하면 선택이 해제되어 패드 탐색이 멈춤 | 채택 | `deselectOnBackgroundClick = false` |
| 버그 | 포커스 통지 콜백 중 UI가 바뀌면 이전 대상을 선택함 | 채택 | 통지 후 최상단이 바뀌었으면 중단한다 |
| 버그 | onReset 예외 시 UI 상태가 갱신되지 않음 | 채택 | UI 상태를 먼저 반영하도록 순서만 바꿨다. Controller 계약은 유지 |
| 구현 | 포커스 기준을 "보이는 최상단"으로 바꾸면서 DetachWidget의 갱신을 되돌림 | 승인 | 해제 중간 상태에서 포커스가 꺼졌다 켜지며 입력이 리셋된다. 모든 경로가 RestorePrevious로 끝나므로 누락도 없다 |
| 품질 | 선택 API를 InputManager로 옮기라 | 기각 | 무엇을 선택할지는 UI의 책임이다. EventSystem은 공유 인프라라 소유권 분리가 적절하다 |
| 품질 | RefreshFocus 호출 지점이 흩어져 있음 | 기각 | 모두 private 스택을 바꾸는 UIContainer 내부 메서드다. 외부 개발자가 수동으로 넣는 구조가 아니다 |

### 3·4단계

| 출처 | 지적 | 판단 | 근거 |
| --- | --- | --- | --- |
| 구현 | 지시한 `<Gamepad>/buttonSouth` 바인딩이 에셋에 없어 에셋을 수정 | 승인 | 용도 바인딩을 오버라이드하면 키보드까지 바뀐다. 버그 리뷰에서도 키보드 Enter/Esc가 유지됨을 확인했다 |
| 버그 | 장치를 전환하게 만든 입력이 Jump/Submit까지 발동 | 채택 | InputUser가 상태 반영 전에 콜백을 호출한다(소스로 확인). 전환 프레임에는 게임 입력과 탐색을 막는다 |
| 버그 | PC에서 패드가 페어링되면 마우스 포인터가 죽음 | 채택 (시작 장치) | 데스크톱은 키보드·마우스를 먼저 고른다. 포인터 이동으로 전환하는 기능은 PC 개발용 편의라 넣지 않았다 |
| 버그 | 트리거 미세 입력으로 장치가 전환됨 | 채택 | 이벤트 값이 눌림 기준을 넘을 때만 전환한다 |
| 버그 | 마우스만 분실되어도 게임 입력이 잠김 | 채택 | 남은 페어링 장치가 없을 때만 분실로 판단한다(소스상 분실 장치가 페어링 목록에서 빠지는 순서 확인) |
| 버그 | 늦게 구독한 쪽이 분실 상태를 알 수 없음 | 채택 | `IsDeviceLost` 노출(설계 3.1) |
| 버그 | 종료 후 공유 에셋에 빈 장치 목록이 남음 | 채택 | `devices = null`로 복원 |
| 품질 | InputManager 분리(235줄) | 기각 | 상태(분실·포커스)를 공유하는 단일 게이팅이 핵심이다. 지금 나누면 연동 비용만 생긴다. 5단계에서 재검토 |

## 6. 리드 코드 리뷰 소견

- **좋은 점:** 입력 허용 조건이 `ApplyInputStateInternal` 한 곳에 모였다. 새 조건(예: 일시정지)도 이 메서드 한 줄로 확장된다. 디버깅할 때도 이 지점 하나만 보면 된다.
- **좋은 점:** Actor 작성자가 알아야 할 API는 `RegisterInputContext` 하나다. Controller·Global 접근이 콘텐츠 코드에 흩어지지 않는다.
- **주의:** `ApplyInputStateInternal`에 이유를 설명하는 주석이 4줄 붙어 있다. 모두 코드만으로 알 수 없는 제약(실행 순서, 프레임 지연 이유)이라 남겼다. 조건이 더 늘면 메서드 위 요약으로 정리하는 편이 좋다.
- **주의:** UI 모듈용 `InputActionReference` 10개는 앱 실행 동안 유지된다. 패키지의 기본 처리와 같은 방식이고 한 번만 생성되므로 그대로 두었다.
- **주의:** 장치 분실 중에는 에셋의 장치 목록이 비어서, 다른 장치의 버튼을 누르기 전까지 UI도 반응하지 않는다. 버튼 하나로 자동 전환되므로 허용했다. 콘솔 규약상 재연결 안내는 시스템 UI가 맡는 경우가 많다.

## 7. 동작 계약 요약

| 상황 | 동작 |
| --- | --- |
| 씬 전환 중 | 게임 입력과 UI 모듈 모두 차단 |
| Static UI가 보이는 최상단 | 게임 입력 차단, 다음 프레임부터 패드 탐색, 첫 선택 대상 지정 |
| UI 스택이 빔 | 게임 입력 허용, 탐색 이벤트 꺼짐, 선택 해제 |
| 패드 분실(남은 장치 없음) | 게임 입력만 차단, `InputDeviceLost` 통지, `IsDeviceLost = true` |
| 다른 장치의 버튼 입력 | 해당 장치로 전환, 그 프레임은 입력 차단, `InputControlsChanged` 통지 |
| Switch | UI 확인 = 오른쪽 버튼, 취소 = 아래 버튼. Player 맵은 위치 기준 유지 |

## 8. Play Mode·실기 확인 목록

1. 팝업을 패드 A로 닫을 때와 Jump로 UI를 열 때 같은 프레임에 이중 실행이 없는지
2. 런타임에 만든 EventSystem이 씬 Canvas를 정상 레이캐스트하는지, "EventSystem 여러 개" 경고가 없는지
3. USB·블루투스 패드 해제·재연결 시 분실/복구 통지, 게임 입력 차단, UI 조작 가능 여부
4. 장치 전환 프레임 차단으로 인한 1~2프레임 입력 지연 체감
5. 키보드·패드의 UI Submit/Cancel이 바인딩 마스크 아래에서 정상 동작하는지
6. Switch NPad의 `buttonSouth`가 물리적 아래 버튼인지 (플랫폼 패키지 필요)

## 9. 후속 과제

| 항목 | 내용 |
| --- | --- |
| Title·Download 씬 버튼 | 씬에 직접 배치된 버튼은 Static UI가 아니어서 패드로 조작할 수 없다. 콘솔 대응을 위해 UIManager 위젯으로 옮겨야 한다 |
| 5단계 | PS5·Xbox 초기 유저, Switch 컨트롤러 설정 화면 연동 (개발 키트와 플랫폼 패키지 필요) |
| 분실 시 일시정지 UI | `InputDeviceLost` 통지를 받아 System UI로 표시 (UI 콘텐츠 작업) |
| 버튼 아이콘 | `CurrentDevice`와 `InputControlsChanged`로 아이콘 세트 선택 (아이콘 리소스 필요) |
| 테스트 코드 | `Tools/` 폴더는 git 미추적이라 이번 커밋에 포함되지 않았다. bin/obj를 제외하고 추적할지 결정 필요 |
| 과거 보고서 | Tools/LifecycleTests의 ActorControllerReport.md, ControllerContextReport.md에는 아직 `CreateController`와 씬별 입력 허용이 설명되어 있다 |
| 부팅 씬 | Global은 Test.unity에만 있고, Build Settings의 세 씬은 모두 비활성(enabled: 0)이다. 빌드 첫 씬 구성과 Global 배치를 정해야 한다 (이번 작업 이전부터의 상태) |
