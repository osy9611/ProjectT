# InputManager 설계

작성일: 2026-10-03

## 1. 목적과 범위

PS5·Xbox·Switch에 대응하는 입력 구조를 만든다. 입력을 Actor별 Controller가 아니라 `Global.Input` 매니저가 전체적으로 관리한다.

| 항목 | 결정 |
| --- | --- |
| 대상 플랫폼 | PS5, Xbox, Switch (PC는 개발·검증용으로 함께 지원) |
| 유저 수 | 로컬 멀티플레이 미지원. 주 유저 1명 |
| 검증 범위 | 로직 테스트 + PC Play Mode(Xbox/DualSense 패드). 콘솔 개발 키트·플랫폼 패키지 연동은 이후 단계 |
| 기준 커밋 | `4ccef66e` InputContext 입력 구조 및 Actor 생명주기 정리 |

## 2. 왜 매니저가 필요한가

**해결할 문제:** 콘솔 입력은 유저 단위다. 유저-패드 페어링, 패드 연결 해제·복구, UI 입력 모듈 연결, 플랫폼별 확인/취소 배치는 게임 전체에서 하나의 상태로 유지되어야 한다.

**기존 구조로 어려운 이유:**
- `BaseActor.CreateController`는 Actor마다 에셋을 복제한다. Actor는 풀 반환·씬 종료로 해제되므로 페어링과 연결 해제 상태가 씬마다 사라진다.
- UI(EventSystem의 `DefaultInputActions`)와 게임(Actor 복제 에셋)의 입력 경로가 분리되어 있다. 패드 A 하나로 `UI/Submit`과 `Player/Jump`가 함께 실행된다.
- InputContext 소비는 같은 액션 안에서만 동작한다. 서로 다른 액션이 같은 버튼을 쓰는 UI/게임 충돌은 막지 못한다.
- UIManager에 두면 UI 외 책임(페어링·게임 입력)이 섞인다.

**추가 비용:** 매니저 1개, SceneManager·UIManager와의 연동 2곳, Global 인스펙터 필드 1개, 씬별 EventSystem을 매니저 하나로 통합.

## 3. 구조

```
Global.Input (InputManager)
 ├─ 복제 InputActionAsset (주 유저 1개)
 ├─ InputUser 페어링 / 연결 해제·복구 / 컨트롤 스킴 변경
 ├─ 활성 맵 결정: UI 포커스 → "UI", 그 외 → "Player"
 ├─ 입력 허용: 씬 전환 잠금 · 패드 연결 해제
 ├─ EventSystem + InputSystemUIInputModule (actionsAsset = 복제 에셋)
 └─ Controller (InputContext 우선순위 전달)
        ▲ AddContext / RemoveContext
     BaseActor (활성화 생명주기에서 자동 등록·제거)
```

| 클래스 | 책임 | 변경 |
| --- | --- | --- |
| InputManager (신규) | 에셋 복제·소유, 페어링, 활성 맵, 입력 허용, UI 입력 모듈, 플랫폼 규약 적용 | 신규 |
| Controller | 활성 맵 하나만 켜고 InputContext 우선순위로 전달 | 모든 맵 Enable → 지정 맵만 Enable. 생성·Init은 InputManager만 호출 |
| InputContext | 행동 묶음, 우선순위, 소비, onReset | 유지 |
| BaseActor | 자신의 InputContext 목록 보유. 활성화 시 등록, 비활성화·해제 시 제거 | `CreateController`, 에셋 복제, 씬 입력 허용 구독 제거 |
| ComBaseActor | Unity 생명주기 → Actor 훅 | 유지 |
| SceneManager | 전환 잠금을 `Global.Input`에 전달 | 씬·Actor 단위 전달 → InputManager 한 곳 |
| SceneBase | 씬 상태·종료 통지 | `InputAllowedChanged`, `IsInputAllowed` 제거 검토 (사용처가 Actor 입력뿐) |
| UIManager | UI 포커스 상태 소유, 변경 시 `Global.Input`에 통지. 패드 선택 포커스 관리 | 포커스 변경 통지·선택 대상 복원 추가 |

### 3.1 InputManager 공개 API 초안

```csharp
public sealed class InputManager : ManagerBase
{
    public InputManager(InputActionAsset asset);

    public Controller Controller { get; }        // 주 유저 Controller
    public InputDevice CurrentDevice { get; }     // 버튼 아이콘 선택용
    public bool IsDeviceLost { get; }

    public void SetInputAllowed(bool allowed);    // SceneManager 전환 잠금
    internal void SetUIFocused(bool focused);     // UIManager 포커스 통지
}
```

- 에셋은 Global 인스펙터 필드로 받는다. `new PatchManager(UseRemoteResource)`처럼 생성자로 전달한다.
- 연결 해제·복구·컨트롤 변경은 새 이벤트 대신 기존 `Global.Notify`로 알린다. `NotificationId`에 `InputDeviceLost`, `InputDeviceRegained`, `InputControlsChanged`를 추가한다.
- Actor가 사용하는 API는 `Global.Input.Controller`의 `AddContext`/`RemoveContext`뿐이다. SceneBase·BaseActor에 전달용 API를 두지 않는다.

### 3.2 입력 활성 조건

Controller는 아래 조건을 모두 만족할 때만 활성 맵을 켠다. 조건 계산은 InputManager 한 곳에서 한다.

| 조건 | 출처 |
| --- | --- |
| 매니저 Ready, 종료 토큰 미취소 | ManagerBase |
| 씬 전환 잠금 해제 | SceneManager → `SetInputAllowed` |
| 주 유저에 페어링된 장치 존재 | InputUser `DeviceLost`/`DeviceRegained` |

활성 맵은 UI 포커스로만 정한다. 맵이 바뀌거나 비활성화되면 기존 `onReset`으로 저장값(이동 벡터 등)을 정리한다.

### 3.3 Global 등록 순서

`Notify` 다음, `Pool` 앞에 등록한다.

- 종료는 역순이다. Pool·Scene·UI가 먼저 정리되므로 풀·씬의 Actor 해제 시점에 `Global.Input`은 아직 Ready다.
- Actor 해제 시 컨텍스트가 이미 분리되어 있으면(`Controller.Release` 이후) `Global.Input`을 조회하지 않는다.
- SceneManager·UIManager는 Input보다 뒤에 초기화되므로 호출 시점에 `Global.Input`이 Ready다.

## 4. 콘솔 대응 항목

| 항목 | 설계 | 단계 |
| --- | --- | --- |
| UI/게임 버튼 충돌 | UI 포커스 시 `UI` 맵만, 그 외 `Player` 맵만 활성. EventSystem도 같은 복제 에셋 사용 | 2 |
| 패드 UI 조작 | UIManager가 스택 최상단 UI의 첫 선택 대상을 지정하고, 닫힐 때 이전 선택 복원 | 2 |
| 유저-장치 페어링 | `InputUser.PerformPairingWithDevice` + `AssociateActionsWithUser`. PC는 미페어링 장치 사용 시 해당 장치로 전환(키보드·마우스 ↔ 패드) | 3 |
| 연결 해제·복구 | `InputUser.onChange`의 DeviceLost → 입력 비활성 + `InputDeviceLost` 통지. DeviceRegained → 복구. 일시정지 UI 표시는 게임/UI 측이 통지를 받아 처리 | 3 |
| 확인/취소 배치 | 초기화 때 플랫폼별로 `UI/Submit`, `UI/Cancel`에 바인딩 오버라이드 한 번 적용. Switch는 오른쪽 버튼(A)이 확인, PS5·Xbox는 아래 버튼이 확인 | 4 |
| 버튼 아이콘 | `CurrentDevice`와 `InputControlsChanged` 통지로 UI가 아이콘 세트 선택 | 4 |
| 플랫폼 유저 연동 | PS5·Xbox 초기 유저, Switch 컨트롤러 설정 화면은 플랫폼 패키지 API로 연결. 3단계의 페어링 지점에 연결만 추가 | 5 (개발 키트 필요) |

세부 인증 요구사항은 각 플랫폼 개발자 문서(NDA)로 확인한다. 이 문서는 공개적으로 알려진 일반 요구만 반영한다.

## 5. 이전 기능 처리

| 기능 | 처리 |
| --- | --- |
| `SetRebind` | 미구현(본문 주석)이었다. 삭제 유지. 게임 내 리바인드는 접근성 범위 결정 후 별도 설계 |
| `InputUser` | 저장만 하던 코드는 복원하지 않는다. InputManager가 페어링 책임으로 새로 구현 |
| `SwitchActionMap` | 공개 API로 복원하지 않는다. UI 포커스에 따른 맵 전환을 InputManager 공통 진입점에서 처리 |

## 6. 단계별 작업

각 단계마다 LifecycleTests와 Unity 참조 컴파일을 통과시키고 따로 커밋한다.

1. **InputManager 골격과 소유권 이전**
   - InputManager 추가, Global 등록, 에셋 복제·해제, 주 유저 Controller 생성
   - Controller: 지정 맵만 Enable, Init은 InputManager에서 호출
   - BaseActor: `CreateController`·에셋 복제 제거, InputContext 등록을 활성화 생명주기로 이동
   - SceneManager 전환 잠금 → `Global.Input.SetInputAllowed`. SceneBase 입력 허용 이벤트 제거 검토
   - 테스트: Actor 활성/비활성/풀 재사용/씬 종료 시 컨텍스트 등록 상태, 전환 잠금
2. **UI 포커스와 맵 전환**
   - UIContainer 스택 최상단 변경 → UIManager → `Global.Input.SetUIFocused`
   - EventSystem을 InputManager 루트로 통합, 씬별 EventSystem 제거(Title, Download, Test)
   - 패드 선택 포커스 지정·복원
   - Play Mode: 팝업에서 A로 닫을 때 `Player/Jump`가 같은 프레임에 실행되지 않는지 확인
3. **페어링과 연결 해제**
   - InputUser 페어링, PC 장치 자동 전환, DeviceLost/Regained 처리와 통지
   - Play Mode: 패드 USB/블루투스 해제·재연결
4. **플랫폼 규약**
   - 확인/취소 오버라이드, `CurrentDevice`·컨트롤 변경 통지
5. **콘솔 플랫폼 패키지 연동** (개발 키트 확보 후)

## 7. 결정이 필요한 것

| 항목 | 기본안 |
| --- | --- |
| 어떤 UI가 패드 포커스를 가져가는가 | `Static` 타입(스택 관리 UI)이 열려 있으면 UI 포커스. `HUD`·`Dynamic`은 게임 입력 유지 |
| 연결 해제 시 일시정지 UI 담당 | UIManager가 `InputDeviceLost` 통지를 받아 System UI로 표시 |
| PC 키보드·마우스 지원 | 개발·검증용으로 지원. 마지막에 사용한 장치로 자동 전환 |

## 8. 위험과 미검증

- 맵 전환 프레임의 입력 누수: Button 타입은 Enable 시 이미 눌린 상태로 재발동하지 않는 것으로 알려져 있으나 Play Mode로 확인한다.
- 테스트 스텁 확장: LifecycleTests의 `UnityStubs.cs`에 InputUser·EventSystem 관련 스텁이 추가로 필요하다. 실제 Input System 동작은 스텁으로 대체할 수 없으므로 Play Mode 확인 항목으로 남긴다.
- 실제 콘솔 장치, IL2CPP, 플랫폼 인증 요구는 5단계 전까지 미검증이다.
