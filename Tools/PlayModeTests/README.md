# P0 Play Mode 검사

`Unity/ProjectTP0PlayProbe.cs`는 FeatureSample 씬의 샘플 버튼 경로(Actor·Pool, 입력, UI, Scene)를 실행 중인 Unity 에디터의 Play Mode에서 순서대로 실행한다. 샘플 버튼 함수는 private이라 같은 함수를 리플렉션으로 호출하고, 입력은 가상 Gamepad 상태 이벤트로 넣는다. 마지막 단계에서 `Tools/UITests/Unity/ProjectTUIPlayProbe.Verify`도 같은 세션에서 실행한다.

UIEventHandler를 삭제하면서 `onscreen-diag` 단계와 ui-probe의 OnScreenControl sender 검사(`Fresh sender after real OnDisable/OnEnable` 포함)도 삭제했다. 아래 결과 기록의 pass 수와 해당 실패는 삭제 전 구성 기준이다. 삭제 후 구성의 pass 수는 아직 기록하지 않았다.

## 실행

1. `Client/Assets/ProjectTP0ProbeHarness/`를 만들고 `Unity/ProjectTP0PlayProbe.cs`와 `../UITests/Unity/ProjectTUIPlayProbe.cs`를 복사한다.
2. 컴파일이 끝나면 `Assets/Scenes/Sample/FeatureSample.unity`를 열고 Play Mode에 진입한다.
3. Global이 Ready가 되면 `ProjectTP0PlayProbe.Run()`을 호출한다(MCP RunCommand 또는 에디터 스크립트).
4. 콘솔의 `[P0] PASS/FAIL/NOTE` 줄과 `PROJECTT_P0_PLAYMODE_RESULT pass=N fail=M`을 확인한다. `ProjectTP0PlayProbe.Results`로도 읽을 수 있다.
5. Play Mode를 종료하고 임시 폴더와 `.meta`를 삭제한다.

프로브는 실행 중에만 `backgroundBehavior=IgnoreFocus`, `editorInputBehaviorInPlayMode=AllDeviceInputAlwaysGoesToGameView`인 설정 복제본을 쓰고 끝나면 원래 설정으로 되돌린다. 에셋은 수정하지 않는다. 게임 뷰에 포커스가 없으면 원래 설정에서는 두 가지 문제가 생긴다.
- `ResetAndDisableNonBackgroundDevices`가 가상 장치를 끈다.
- 동기 `InputSystem.Update()`가 Editor 업데이트로 실행되어 Gamepad 이벤트를 처리하지 않는다. Input System은 `IgnoreFocus`와 `AllDeviceInputAlwaysGoesToGameView`가 함께 설정될 때만 포커스와 관계없이 Player 업데이트를 실행한다.

포커스는 실행 도중에도 바뀌므로 시작 시점의 포커스와 관계없이 항상 복제본을 쓴다.

## 결과 (2026-10-17, 불안정 실패 원인 확인과 수정, HEAD 84c4a124 + 작업 트리)

**수정 후 15회 모두 pass=107 fail=0이고, 결과 117줄이 모두 같다.** 실행 동안 Input System의 포커스 상태(`m_HasFocus`)를 매 에디터 틱 기록했다.
- 포커스가 계속 있음: 8회
- 포커스가 계속 없음: 3회
- 포커스를 강제로 끈 상태(매 틱 `InputManager.OnFocusChanged(false)`): 4회

`+ InputSystem.Update()` 진단값 45개는 모두 (0.47, 0.00)이었다.

**원인 확인.** `Fresh sender after real OnDisable/OnEnable` 불안정 실패의 원인은 다음과 같다. 게임 뷰에 포커스가 없으면 동기 `InputSystem.Update()`가 Editor 업데이트로 실행되어 Gamepad 이벤트를 처리하지 않는다. 그래서 직후에 읽은 값이 0이다. 다음 프레임의 Player 업데이트가 이벤트를 처리하므로 1프레임 뒤에는 값이 들어온다. 근거는 다음과 같다.
- Input System 1.19.0 `InputManager.defaultUpdateType`은 Play Mode라도 `gameHasFocus`가 false면 `Editor`를 돌려준다.
- 수정 전 프로브를 5회 실행했다. 4회 실패했고, 실패한 4회 모두 동기 Update 시점에 `m_HasFocus=False, defaultUpdateType=Editor`였다. 통과한 1회는 `True, Dynamic`이었다. 실패 중 2회는 시작 때 포커스가 있다가 실행 도중 잃은 경우였다.
- 같은 프레임에서 `m_HasFocus`만 바꿔 읽으면 true일 때 0.47, false일 때 0이었다. `AllDeviceInputAlwaysGoesToGameView`를 함께 설정하면 false에서도 0.47이었다.

## 결과 (2026-10-17, UI API 변경 반영 HEAD 29ead2f7)

**수정 전에는 컴파일되지 않았다.** 커밋 1ea407c9에서 `UIManager.GetCurrentStackUI()`가 `TopWidget`으로 바뀌었는데 프로브는 옛 이름을 썼다. 이 8곳을 `TopWidget`으로 바꿨다.

**ui-probe의 낡은 기대를 고쳤다.** 이름만 바꾼 상태에서는 결과가 pass=106 fail=1이었다. 실패는 ui-probe의 `expected show error`였다.
- 원인: 3abb5835 이후 가려진 UI는 복원될 때 OnShow를 다시 받는다. 그런데 `ProjectTUIPlayProbe`의 d는 중첩 Show 실패용 핸들러를 계속 달고 있었다. 그래서 c를 숨겨 d가 복원될 때 이 핸들러가 다시 실행되어, try/catch 밖에서 예외를 던졌다.
- 조치: 핸들러를 그 시나리오에서만 쓰도록 했다.

**수정 후 결과.** HEAD 코드에서 4회 실행했다.
- 3회: pass=107 fail=0
- 1회: `Fresh sender after real OnDisable/OnEnable`에서 fail=1. 같은 코드로 다시 실행했을 때는 재현되지 않았다.

**불안정한 실패.** 이 1회 실패는 `onscreen-diag`의 `+ InputSystem.Update()` 진단값이 (0.00, 0.00)일 때 함께 나타난다(확인됨). 이 진단값은 동기 `InputSystem.Update()` 직후에 읽은 값이다. 실패한 실행에서는 이 값이 0이고, 1프레임 뒤 값만 (0.47, 0.00)이다. 통과한 실행에서는 두 값이 모두 (0.47, 0.00)이다.

원인은 위 "원인 확인"에 있다. 당시 프로브는 시작 시점에 포커스가 없을 때만 `backgroundBehavior`만 바꾼 복제본을 썼다. 그래서 시작 때 포커스가 있다가 실행 도중 잃은 실행도 실패했다.

수정 전에는 이 실패만으로 코드 변경의 회귀를 판단하지 않았다. 전후 비교는 포커스 안내 줄을 뺀 전체 결과로 했다. 2026-10-17 ResourceBase 변경과 ErrorReporter 취소 보고 변경을 검증할 때 이렇게 비교했고, 변경 전 코드와 변경 후 코드의 결과 117줄이 같았다. 3abb5835 직전 UIContainer로 실행했을 때도 pass=107 fail=0이었다.

## 결과 (2026-10-14, Scene·Resource·Pool 매니저 리뷰 리팩토링 HEAD 1ba983d9)

**pass=107 fail=0.** 오류 로그는 의도된 2건(Double Request 거부, Enter Fail)과 iOS 플러그인 참조 오류 2건으로 이전 결과와 같다. Play Mode 종료 시 오류·경고는 없다. 대상 변경은 SceneManager 전환 단계 분리·`HasScene`·`eSceneTransitionResult`·SceneBase 훅 정리·`Transition` 콜백 필수화, `IResource`→`ResourceBase`·씬 언로드 함수 이름 구분, 제네릭 풀 제거·`Release`를 `Return`/`Destroy`로 분리·`GameObjectPool` 생성자 초기화다. Double Request·Enter Fail 콜백은 변경 후에도 `Failure`로 전달된다. 같은 세션에서 턴제 전투 프로브도 실행했다(아래).

## 결과 (2026-10-13, ResourceManager 리뷰 리팩토링 HEAD 54dc8aec)

**pass=107 fail=0.** 오류 로그는 의도된 2건(Double Request 거부, Enter Fail)과 iOS 플러그인 참조 오류 2건으로 이전 결과와 같다. Play Mode 종료 시 오류·경고는 없다. 대상 변경은 LoadAsset 제거, dontDestroy를 AppScope 지정으로 통일, 씬 로드·언로드 API internal 축소, GetResource 분리다. 프로브는 변경된 API를 직접 호출하지 않으며 씬 전환·System UI·Scope 풀 경로로 확인했다.

## 결과 (2026-10-12, Manager Lifecycle 리팩토링 HEAD 70ee163e)

**pass=107 fail=0.** 오류 로그는 의도된 2건과 iOS 플러그인 참조 오류 2건으로 3차와 같다. Play Mode 종료 시 `OnApplicationQuit` → Shutdown에서 오류는 없고, 종료 후 `Global.State`는 Created다. 종료 중 ResourceManager가 마지막 씬을 해제할 때 Unity 경고 "Unloading the last loaded scene ... is not supported" 2건이 나왔다. 앱 종료(ApplicationExit)에서는 씬 언로드를 건너뛰도록 고친 뒤, SampleSceneA Single 전환 후 종료하는 재현에서 경고가 사라졌다.

## 결과 (2026-10-11 3차, 씬 배치 Actor 계약 Check 추가)

**pass=107 fail=0.** scene 단계에 배치 Actor 검사 12개를 추가했다. SampleSceneA·SampleSceneB Unity 씬에 Actor를 옮겨 놓고 `Spawn()`한 뒤 확인했다. 정지 뒤 언로드까지 갱신 계속(Additive 1프레임, Single 2프레임), 그 동안 입력 차단, 언로드 파괴 시 `enter,enable,disable-after-stop,release`, Additive 언로드가 A의 배치 Actor에 영향 없음. 계약은 [SceneManager.md](../../reports/tests/LifecycleTests/SceneManager.md) "씬 배치 Actor". 이번 실행은 에디터 포커스가 있어 기존 UI 검사도 통과했다. 오류 로그는 의도된 2건(Double Request 거부, Enter Fail)과 에디터 임포트의 iOS 플러그인 참조 오류 2건이다.

## 결과 (2026-10-11 2차, 진행 중 입력 계약 Check 추가)

**pass=95 fail=1.** input 단계에 "스틱 유지 중 재활성화는 다음 값 변화부터 전달"(0,0)과 "다음 값 변화가 재활성화된 Actor에 도달"(0,0.59) 두 Check를 추가해 통과했다. 실패 1건은 포커스 없는 실행의 기존 UI 검사다.

## 결과 (2026-10-11, HEAD ca39853d + 작업 트리, Skill 단계 추가)

**pass=93 fail=1.** skill 단계 13/13 통과: 스폰 Hp 100/100·Atk 10, 스킬 201 발동·완료, 쿨다운 중 무시·3초 뒤 발동, 버프 1 Atk 12, 버프 5 스턴 태그·발동 차단·만료 해제, 재스폰 시 Atk·Hp·태그 초기화와 스킬 재등록. 실패 1건은 에디터 포커스 없는 실행의 `Fresh sender after real OnDisable/OnEnable`로 아래 10/9 1차 실행과 같다. Skill 단계와 무관하다.

## 결과 (2026-10-09, Unity 6000.3.14f1, HEAD c56035db + 작업 트리)

2차 실행: **pass=81 fail=0**. 의도된 오류 로그 2건(Double Request 거부, Enter Fail 테스트 예외) 외에 Error/Exception 로그 없음.

1차 실행은 69/5였다. 5건 중 3건은 프로브 결함(Respawn 판정식, `DoubleRequestInternal` 소유 클래스, cleanup 순회 중 목록 수정)이었고 수정 후 통과했다. 1건(`Close B restores A`)은 UI 포커스 중 누른 Gamepad South가 UI/Submit으로 A의 버튼을 눌러 A가 먼저 닫혔기 때문이며, 프로브 순서를 고친 뒤 통과했다. 남은 1건이 아래 UI 검사다.

### `Fresh sender after real OnDisable/OnEnable` (9/26 배치 실패)

- 1차 실행(에디터 포커스 없음)에서 같은 지점에서 실패가 재현됐다.
- 같은 상태에서 MCP 명령으로 확인했을 때, Hide/Show 전 첫 송신은 물론 가상 Gamepad에 상태를 직접 넣고 `InputSystem.Update()`로 읽어도 값이 0이었다. 실패가 OnDisable/OnEnable 이전 단계에서도 나타나므로 재활성화 경로만의 결함으로 볼 근거는 없다.
- 2차 실행(프로브 시작 시 에디터 포커스 있음)에서 `onscreen-diag`가 직접 장치, 첫 송신, 재활성화 뒤 새 sender 모두 값을 쓰는 것을 확인했고, `ProjectTUIPlayProbe.Verify`도 통과했다.
- 결론: 재활성화 뒤 새 sender는 정상 동작한다. 포커스 없는 상태에서 직접 장치 읽기까지 0이 되는 원인은 2026-10-17에 확인했다(위 "원인 확인").

### 관찰 사항 (판정 없음)

- 스틱을 누른 채 Actor를 비활성화했다가 다시 활성화하면, 스틱 값이 바뀔 때까지 Move가 다시 전달되지 않는다(`MoveDirection` 0 유지). 2026-10-11 현재 계약으로 확정해 input 단계의 Check로 고정했다([ControllerContextReport](../../reports/tests/LifecycleTests/ControllerContextReport.md) "진행 중 입력").
- Single B(Enter Fail) 실패 뒤 `CurrentScene`은 None, Unity 활성 씬은 SampleSceneB, 전환 중 아님, 입력 허용 상태였다.
- 포커스된 Static UI에서 Gamepad South는 게임 Jump로 전달되지 않고 UI/Submit으로 처리됐다.

# 턴제 전투 Skill System 검사

결과: **pass=61 fail=0** (2026-10-14, HEAD 1ba983d9, PoolManager `Release` 제거·`GameObjectPool` 생성자 전환 후 재확인). 이전: **pass=61 fail=0** (2026-10-11, `TurnBattleSample` 씬). AI 전투는 콘솔(`Tools/TurnGameTests`)과 같은 8라운드·유닛별 발동 수로 끝났다.

`Unity/ProjectTTurnBattleProbe.cs`는 `TurnBattleRunner` 공개 API로 콘솔 테스트와 같은 시나리오를 실제 `ComBaseActor`(`TurnUnit`)와 풀 경로로 실행한다. 리플렉션은 쓰지 않는다.

## 실행

1. `Client/Assets/ProjectTTurnProbeHarness/`에 `Unity/ProjectTTurnBattleProbe.cs`를 복사하고 컴파일을 기다린다.
2. `Assets/Scenes/Sample/TurnBattleSample.unity`로 Play Mode에 진입한 뒤 `ProjectTTurnBattleProbe.Run()`을 호출한다.
3. `[Turn] PASS/FAIL` 줄과 `PROJECTT_TURN_BATTLE_RESULT`를 확인한다. 끝나면 새 전투가 시작되어 직접 플레이할 수 있다.
4. Play Mode를 종료하고 임시 폴더와 `.meta`를 삭제한다.

## 콘솔 테스트에 없는 Unity 전용 항목

| 항목 | 확인 내용 |
|---|---|
| `AutoTickSkills => false` | 30프레임이 지나도 스턴·쿨다운 유지, `TickSkills`(EndTurn)로만 진행 |
| 비활성화 | `SetActive(false)`가 활성 채널링을 취소하고, 비활성 중 `TickSkills`는 진행하지 않음 |
| 풀 사망·재대여 | 사망 = `Global.Pool.Return` → `SpawnSerial` 변경·`Released` 정리, 재대여 시 같은 인스턴스가 깨끗한 상태로 스킬 재등록 |

IMGUI 버튼 클릭(직접 플레이 경로)은 프로브가 누르지 않는다. 버튼은 `pendingAction`으로 같은 공개 API를 호출한다.
