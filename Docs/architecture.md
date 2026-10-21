# ProjectT 구조

클라이언트는 Unity 6(6000.3.14f1) 프로젝트 하나(`Client/`)이고, 게임 코드는 `Client/Assets/Script/` 아래에 있다. 진입점은 `Global` 컴포넌트 하나다. `Global`이 매니저 9개를 정해진 순서로 초기화하고 역순으로 종료한다. 게임 코드는 어디서든 `Global.Resource`, `Global.UI`처럼 `Global`을 거쳐 매니저를 호출한다. 어셈블리 구성은 [어셈블리](#어셈블리) 절에 있다.

| 문서 | 내용 |
| --- | --- |
| [error-reporting.md](error-reporting.md) | ErrorReporter와 오류 보고 경로 |
| [actor-lifecycle.md](actor-lifecycle.md) | `ComBaseActor` 생명주기 |
| [ability-system.md](ability-system.md) | Skill·Stat 코어와 테이블 어댑터 |
| [open-issues.md](open-issues.md) | 남은 과제와 보류한 결정 |

## 진입점과 매니저 생명주기

세 타입이 생명주기를 나눠 맡는다.

| 타입 | 파일 | 책임 |
| --- | --- | --- |
| `Global` | `Common/Global.cs` | 싱글턴 MonoBehaviour. Unity 콜백을 호스트에 전달하고, 조회·대기 API와 로그 진입점을 제공한다 |
| `Global` (partial) | `Common/Global.Managers.cs` | 매니저 접근자 9개, `Table`, 등록 목록 `RegisterManagers` |
| `ManagerHost` | `Managers/Base/ManagerHost.cs` | 등록, 순차 초기화, 역순 종료, `Update`·`Pause` 전달 |
| `ManagerBase` | `Managers/Base/ManagerBase.cs` | 매니저 하나의 상태(`ManagerState`), 수명 토큰, 루트 오브젝트 |

### 부팅

`Global`은 `[DefaultExecutionOrder(-10000)]`이다. 씬에 놓인 `Global`의 `Awake`가 `Init`을 호출하고, `AfterSceneLoad` 시점에 활성 `Global`마다 `Init`을 다시 호출한다. `Init`은 이미 등록된 인스턴스면 바로 돌아오고, 두 번째 `Global`이면 그 컴포넌트를 파괴한다. 첫 인스턴스는 다음 순서로 진행한다.

1. `ErrorReporter`에 `FileErrorSink`(`persistentDataPath/ErrorReports`)를 지정한다.
2. `DontDestroyOnLoad`를 호출한다.
3. `ManagerHost`를 만들고 `RegisterManagers(host)`를 동기로 실행한다.
4. `InitializeAsync().Forget(LogException)`로 초기화를 시작한다.

인스펙터 필드 `LoadData`는 `DataManager`가 초기화 중에 테이블을 읽을지, `UseRemoteResource`는 `PatchManager`가 Firebase 원격 카탈로그를 준비할지 정한다. `UseDebugLog`는 `Global.Log`·`LogWarning` 출력 여부다.

### 등록 순서가 초기화 순서다

`RegisterManagers`의 등록 순서가 곧 초기화 순서이고, 그 역순이 종료 순서다. `Update`와 `Pause`도 등록 순서로 전달된다. 순서는 이 함수 한 곳에만 있다.

| 순서 | 매니저 | 접근자 | 이 위치에 있는 이유 |
| --- | --- | --- | --- |
| 1 | `InputManager` | `Global.Input` | Actor 활성화보다 먼저 Ready여야 한다 |
| 2 | `PatchManager` | `Global.Patch` | 첫 Addressables 호출 전에 예외 핸들러와 Firebase 프로바이더를 등록한다 |
| 3 | `ResourceManager` | `Global.Resource` | 이후 매니저가 Scope와 로드를 쓴다 |
| 4 | `NotificationManager` | `Global.Notify` | |
| 5 | `DataManager` | `Global.Data`, `Global.Table` | 테이블을 `Global.Resource`로 읽는다 |
| 6 | `PoolManager` | `Global.Pool` | 대여 시 `Global.Resource`로 원본을 읽는다 |
| 7 | `SceneManager` | `Global.Scene` | 전환 중 UI·Sound·Pool·Input을 호출한다 |
| 8 | `UIManager` | `Global.UI` | |
| 9 | `SoundManager` | `Global.Sound` | 초기화 때 Resource Scope와 Pool을 만든다 |

`ManagerHost`는 매니저마다 `await manager.InitializeAsync(token)`을 차례로 실행한다. 동기로 끝나는 `OnInitializeAsync`는 첫 실제 대기 전까지 같은 호출 스택에서 이어진다. `InputManager`는 동기로 초기화되고 맨 앞에 등록되어 있으므로 `Global.Awake` 안에서 Ready가 된다. 그래서 다른 오브젝트의 `Awake`·`OnEnable`보다 먼저 `Global.Input`을 쓸 수 있고, 별도 대기 로직이 없다. `RegisterManagers`를 `Init`에서 동기로 실행하는 것이 이 보장의 전제다.

### Ready 계약

- `Global.GetManager<T>()`와 접근자는 Ready인 매니저만 반환한다. `Global`이 없거나, 등록되지 않았거나, Ready가 아니면 `InvalidOperationException`을 던진다. 그래서 초기화 도중에도 앞서 Ready가 된 매니저는 조회된다.
- `Global.State`와 `Global.IsReady`는 호스트 전체 상태다. `IsReady`는 9개가 모두 Ready일 때만 true다.
- `Global.WhenReadyAsync(token)`은 `Global`이 아직 없으면 생길 때까지 기다린 뒤 호스트의 `WhenReady`를 기다린다. `WhenReady`는 성공 또는 취소로만 끝난다. 대기 후 호스트가 Ready가 아니면 `OperationCanceledException`을 던진다.
- 초기화 실패는 대기자에게 전달하지 않는다. 대기자마다 같은 오류를 다시 보고하지 않기 위해서다. 실패하면 호스트는 시작한 매니저를 역순으로 `Shutdown(ShutdownReason.InitializationFailure)`하고 `Failed`가 된다. 오류는 초기화를 시작한 `Global.InitializeAsync`에만 전달되고, `Forget(LogException)`이 한 번 보고한다. 정리 오류가 함께 났거나 원인이 취소 예외면 `AggregateException`으로 묶는다. UniTask가 취소 예외를 취소로 바꿔 메시지를 버리기 때문에 취소 예외도 감싼다.

### 종료

`OnApplicationQuit`은 `Shutdown(ShutdownReason.ApplicationExit)`, `OnDestroy`는 `Shutdown()`을 호출한다. `ManagerHost.Shutdown`은 다음 순서로 진행한다.

1. 호스트를 `Stopping`으로 바꾸고 모든 매니저가 공유하는 수명 토큰을 취소한다.
2. 시작한 매니저를 역순으로 `Shutdown`한다. 각 매니저는 `Stopping`이 되고 `OnShutdown`을 실행한 뒤 루트 오브젝트를 파괴하고 `Stopped`가 된다.
3. 매니저별 예외는 `ErrorCollector.Run`으로 모으고 다음 매니저를 계속 정리한다. 모은 예외는 `AggregateException`으로 던지고, `Global.Shutdown`의 catch가 한 번 보고한다.

역순이므로 정리 중인 매니저보다 먼저 등록된 매니저는 아직 Ready다. 정리 코드는 그 매니저를 `Global`로 조회해 쓸 수 있다. 이미 `Stopping`·`Stopped`인 매니저는 조회하지 않는다. 종료 순서 문제를 필드 캐싱으로 우회하지 않는다.

조회 허용과 새 작업 허용은 다르다. 토큰이 취소된 뒤에도 Ready 매니저는 조회되지만 새 로드나 대여는 시작하지 않는다. `ManagerBase`가 검사 함수를 제공한다.

| 함수 | 허용 조건 | 쓰는 곳의 예 |
| --- | --- | --- |
| `ThrowIfWorkUnavailableInternal()` | 토큰 미취소 + Ready | `NotificationManager.Subscribe`, `PatchManager` 다운로드 |
| `ThrowIfWorkUnavailableDuringInitInternal()` | 토큰 미취소 + Initializing 또는 Ready | `ResourceManager.CreateScope`, `DataManager.GetTableDatas` |
| `CanAcceptWorkInternal()` | 위 첫 조건을 bool로 반환 | `SoundManager.PlayAt`, `NotificationManager.Publish` 순회 |

## 매니저

각 매니저의 공개 진입점만 적는다. 자세한 동작은 각 파일(`Client/Assets/Script/Managers/`)을 읽는다.

### Input

`InputManager`는 프로젝트 전역 입력 에셋(`InputSystem.actions`)으로 `Controller` 하나와 `EventSystem`·`InputSystemUIInputModule`을 직접 만든다. 공개 진입점은 `SetInputAllowed`(SceneManager의 전환 잠금), `AddContext`·`RemoveContext`, `CurrentDevice`, `IsDeviceLost`다. Actor는 `ComBaseActor.RegisterInputContext`로 등록하고 이 함수가 `AddContext`를 호출한다. UI 포커스는 `UIContainer`가 internal `SetUIFocused`로 알린다. 모든 입력 허용 판정은 `ApplyInputStateInternal` 한 곳에서 한다.

- 게임 입력: `sceneInputAllowed && !uiFocused && !IsDeviceLost && !switchPending`
- UI 입력 모듈: `sceneInputAllowed`
- 패드 탐색 이벤트: UI 포커스를 얻은 다음 프레임부터, 장치 전환 프레임이 아닐 때. 게임 입력 콜백이 UI를 연 프레임에 같은 Submit이 새 버튼을 누르지 않게 하기 위해서다.

장치 정책은 다음과 같다.

- 시작 장치는 PC에서 키보드, 콘솔(`Application.isConsolePlatform`)에서 패드다. 페어링되지 않은 장치의 버튼이 눌림 기준을 넘으면 그 장치로 전환하고, 그 프레임의 게임 입력과 탐색을 막는다. 축·트리거의 미세 입력으로는 전환하지 않는다.
- 페어링된 장치가 하나도 남지 않을 때만 분실로 본다. 분실 중에는 게임 입력만 막고, 일시정지·재연결 UI는 조작할 수 있다.
- Switch에서는 UI Submit·Cancel의 패드 바인딩을 맞바꾼다(오른쪽 A가 확인).
- 장치 변경은 `NotificationId.InputControlsChanged`, `InputDeviceLost`, `InputDeviceRegained`로 통지한다.

씬 잠금 기본값은 허용이다. 부팅 씬은 SceneManager 전환 없이 UI 버튼으로 다음 씬으로 넘어가므로, 잠금의 소유자인 SceneManager의 기본값(허용)에 맞췄다.

### Patch와 Resource

`PatchManager`는 원격 번들 부팅과 다운로드를 맡는다. Firebase SDK 의존을 에셋 소유권(`ResourceManager`)과 분리하려고 나눴다. 초기화에서 하는 일은 두 가지다.

- `UnityEngine.ResourceManagement.ResourceManager.ExceptionHandler`를 빈 핸들러로 채운다. 실패는 예외로 호출자에게 전달되어 경계에서 한 번 보고되므로, 작업마다 오류 로그를 남기는 기본 핸들러를 쓰지 않는다. Addressables는 첫 초기화 때 핸들러가 비어 있을 때만 기본 핸들러를 넣으므로 첫 Addressables 호출보다 먼저 채운다.
- `UseRemoteResource`가 true면 `FirebaseAddressablesManager.InitializeAsync`를 호출한다. 이 함수는 `gs://` 주소용 프로바이더 3개(`FirebaseStorageAssetBundleProvider`, `FirebaseStorageJsonAssetProvider`, `FirebaseStorageHashProvider`)와 `Addressables.WebRequestOverride`를 등록하고, Firebase 의존성을 확인한 뒤 `Addressables.InitializeAsync`를 기다린다.

주소는 `gs://`로 두고, 나가는 웹 요청의 URL만 `FirebaseAddressablesCache.OverrideWebRequest`가 받은 다운로드 URL로 바꾼다. 번들 캐시 키가 요청 시점의 주소로 정해지므로, 주소 자체를 바꾸면 같은 번들을 두 번 로드한다. 공개 진입점은 `GetDownloadSizeAsync`와 `DownloadAsync`이고, 둘 다 먼저 `FirebaseAddressablesCache.PreWarmDependenciesAsync`로 매핑을 채운다. Firebase Storage는 배포용으로 붙인 것이고 배포 저장소는 S3로 바뀔 수 있다.

`ResourceManager`는 로드한 에셋을 `ResourceScope` 단위로 소유한다. 리소스는 소유 Scope 목록을 갖고, 마지막 소유 Scope가 해제되면 핸들을 해제한다. 공개 진입점은 다음과 같다.

- Scope: `CreateScope()`, `AppScope`(앱 수명). 로드 함수의 `scope`를 생략하면 현재 씬 Scope를 쓴다.
- 로드: `LoadAndGet`·`LoadAndGetAsync`, `LoadResource`·`LoadResourceAsync`, `PreloadAsync`·`PreloadResourceAsync`, `GetSprite`·`GetSpriteAsync`

씬 로드·언로드와 씬 Scope 교체(`SetSceneScope`, `ReleaseAll`)는 internal이고 SceneManager만 호출한다. 앱 종료(`ApplicationExit`)에서는 씬 언로드를 건너뛴다. Unity가 씬을 정리하고, 마지막 씬 언로드는 거부하며 경고만 남기기 때문이다.

### Notification

`NotificationManager`의 공개 진입점은 `Subscribe(NotificationId id, Action listener, CancellationToken ownerToken)`와 `Publish(NotificationId id)` 두 개다. 같은 리스너를 다시 구독하면 무시한다. 소유자 토큰이 취소된 구독은 즉시 받지 않고, 같은 id의 다음 `Subscribe`나 `Publish`에서 지워진다. `Publish`는 구독 목록의 스냅샷을 순회하고, 순회 중 매니저가 작업을 받지 못하게 되면 멈춘다. 공개 API는 메인 스레드에서만 호출한다.

### Data

`DataManager`는 로컬 문자열(`Assets/Automation/Local/{Ko|Jp|En}.bytes`)과 테이블(`Assets/Automation/Table/{TableId}.bytes`)을 임시 Scope로 읽은 뒤 한 번에 교체한다. 공개 진입점은 `GetTableDatas(token)`, `Table`(`Global.Table`), `GetLocalString`이다. 동시에 호출된 `GetTableDatas`는 진행 중인 로드 하나를 함께 기다린다. 로드가 끝나기 전에 `Table`이나 `GetLocalString`을 읽으면 `InvalidOperationException`이다.

### Pool

`PoolManager`는 `ResourceScope`마다 원본 프리팹별 `GameObjectPool`을 둔다. 공개 진입점은 `CreatePool`, `Get`(원본 또는 경로), `GetAsync`, `Return`, `Destroy`, `GetOriginal`, `Clear`, `ClearScope`다. Scope가 해제되면 그 Scope의 풀이 함께 정리된다. `ClearScope`는 풀을 해제하기 전에 Scope를 닫으므로, 정리 중 콜백의 대여를 포함해 이후 같은 Scope로 들어오는 대여를 거부한다. 닫힌 항목은 Scope가 해제될 때 지워진다.

### Scene

`SceneManager`의 공개 진입점은 `TransitionAsync<T>(resourceName, mode, data)`, 콜백형 `Transition<T>`, `UnloadAdditiveAsync<T>`, `GetScene<T>`, `HasScene<T>`, `CurrentScene`, `IsTransitioning`, `IsInputAllowed`다. 씬 경로는 `Global.Table.SceneDataInfos`에서 찾는다. 전환 중에는 다른 전환을 거부한다. 모든 전환은 입력을 막고 시작해 끝나면 다시 허용한다(UI 캔버스와 `Global.Input` 모두). Single 전환은 전환 화면을 띄운 뒤 다음 순서로 진행한다.

1. 기존 씬을 정지하고, 각 씬 Scope의 풀을 `Global.Pool.ClearScope`로 정리한다.
2. `Global.UI.ClearTransientWidgets()`와 `Global.Sound.StopAllSpatialSounds()`를 호출한다. 씬 오브젝트를 참조한 UI와 씬 Scope 클립을 재생하는 공간 음원이 언로드 뒤에 남지 않게 하기 위해서다.
3. 임시 씬 `SceneTransition`을 활성으로 두고 이전 씬을 언로드한다.
4. 새 Scope를 씬 Scope로 지정하고 이전 Scope를 해제한 뒤 미사용 에셋을 정리한다.
5. 새 씬을 로드하고 `SceneBase`를 붙여 `EnterAsync(scope, token, data)`를 기다린다. 전환 화면을 숨긴 뒤 씬을 활성화한다.

이미 로드된 시작 씬은 다시 로드하지 않고 첫 Single 전환의 씬으로 쓴다. 콜백형 `Transition`은 결과를 `eSceneTransitionResult`(Success, Failure, Canceled)로 전달하고 실패는 직접 보고한다.

### UI

`UIManager`는 `UIContainer` 하나와 HUD 목록을 소유한다. `UIContainer`는 2D 캔버스 아래에 레이어 루트 4개를 Static, HUD, Dynamic, System 순서로 만들어 System UI가 항상 맨 위에 그려지게 한다. 위젯 종류(`eUIContainerType`)는 세 가지다.

| 종류 | 동작 |
| --- | --- |
| Static | 스택에 쌓이고 최상단만 보인다. 표시된 최상단 Static UI가 입력 포커스를 갖는다. 닫히면 이전 UI를 복원한다 |
| Dynamic | 스택 밖의 일반 UI |
| System | 씬 전환에서 지워지지 않는다. 프리팹에 앱 Scope 소유를 추가해 씬 Scope와 함께 해제되지 않게 한다 |

공개 진입점은 `CreateWidget`·`CreateWidgetAsync`, `FindWidget`, `RemoveWidget`, `ClearTransientWidgets`(System을 뺀 위젯 제거), `SetInputAllowed`, `WidgetStack`, `TopWidget`, `GetHud`·`GetHudAsync`다. HUD는 `AppScope` 풀에서 빌려 HUD 루트에 붙이고, `UIManager`가 프레임마다 한 번에 월드 위치를 갱신한다. HUD는 `ComHudAgent.Release()`로 반납한다. `UIManager`는 Actor보다 먼저 종료되므로, 종료 때는 HUD를 분리만 하고 인스턴스 정리는 루트 파괴와 풀 정리에 맡긴다.

### Sound

`SoundManager`는 `eSound`(Bgm, FX, UI)마다 2D `AudioSource` 채널을 하나씩 둔다. 2D 재생은 `Play`, `PlayFade`, `StopBgm`, `CurrentBgm`이다. 3D 재생은 `PlayAt(path 또는 clip, position, minDistance, maxDistance, volume, pitch)`이다. 생성자 인자 `spatialVoiceCapacity`(기본 32)개의 음원을 초기화 때 풀로 만든다. `PlayAt`은 작업을 받을 수 없거나 빈 음원이 없으면 false를 반환한다. 끝난 음원은 0.1초 간격으로 풀에 반납하고, `StopAllSpatialSounds`는 모두 멈춘다. 볼륨은 `SetMasterVolume`·`SetMasterMuted`와 카테고리별 `SetCategoryVolume`·`SetCategoryMuted`로 조절한다.

## 오류 처리

처리할 수 없는 오류는 호출자에게 전파하고, 큰 흐름의 경계에서 `Global.LogException` 한 번으로 보고한다. 게임 코드는 `Global.LogException`과 `Global.LogError`만 쓰고, 두 함수의 오류는 `ErrorReporter`로 모인다. 여러 단계를 모두 실행해야 하는 정리 경로는 `ErrorCollector`로 예외를 모아 다시 던지고, 보고는 상위 경계에 맡긴다.

규칙의 정본은 루트 [AGENTS.md](../AGENTS.md)의 "오류 처리"이고, catch 위치 목록의 정본은 [error-reporting.md의 try/catch 위치](error-reporting.md#12-trycatch-위치)다. ErrorReporter의 구성, 묶음과 반복 제한, 자동 수집 경로도 [error-reporting.md](error-reporting.md)에 있다.

## Actor와 Skill

`ComBaseActor`(`Actor/ComBaseActor.cs`)는 모든 Actor의 기반 MonoBehaviour이고 `IPoolable`과 `IAbilityOwner`를 구현한다. 생명주기는 `Spawn()`으로 시작해 풀 반납(`IPoolable.OnReturn`)이나 파괴로 끝나며, 파생 클래스는 `OnInit`, `OnEnter`, `OnUpdate`, `OnLateUpdate`, `Enable`, `Disable`, `OnRelease` 훅을 재정의한다. 입력 컨텍스트(`RegisterInputContext`·`UnregisterInputContext`)와 `SkillAgent`·`Status`(`AddSkillAgent`, `AddStatus`)는 Actor가 소유한다. 상태 전이와 실패 계약은 [actor-lifecycle.md](actor-lifecycle.md)에 있다.

Skill 시스템은 두 폴더로 나뉜다. `AbilitySystem/`(네임스페이스 `ProjectT.Skill`, `ProjectT.Stat`)은 `SkillAgent`, `Status`, 효과·액션 정의를 담은 코어다. 코어는 `System` 계열과 자기 네임스페이스만 `using`하고 `UnityEngine`, `Global`, 테이블을 참조하지 않는다. `SkillAdapter/`는 `SkillDefinitions`가 `Global.Table`의 행을 `SkillDefinition`·`EffectDefinition`으로 바꾸는 게임 쪽 어댑터다. 구조와 계약은 [ability-system.md](ability-system.md)에 있다.

## 어셈블리

`Client/Assets` 아래에는 asmdef가 하나도 없다. 런타임 스크립트는 모두 `Assembly-CSharp`, `Editor` 폴더는 `Assembly-CSharp-Editor`로 컴파일된다. 의도한 결정이다. `AbilitySystem`도 별도 asmdef를 두었다가 별도 어셈블리로 둘 필요가 없다고 보고 지웠다.

이 결정에서 생기는 결과는 두 가지다.

- `AbilitySystem` 코어가 엔진·`Global`·테이블에 의존하지 않는다는 규칙을 컴파일러가 강제하지 않는다. 코드 리뷰로 유지한다.
- `internal`은 `Assembly-CSharp` 전체에 열려 있다. `internal`은 "같은 기능 묶음 안에서만 쓴다"는 의도 표시일 뿐 접근을 막지 못한다. `SceneManager`가 쓰는 `ResourceManager.SetSceneScope`, `UIContainer`가 쓰는 `InputManager.SetUIFocused`가 그 예다.

`Global`을 `Global.cs`와 `Global.Managers.cs`로 나눈 이유는 테스트다. 테스트가 실제 `Global`을 못 쓰던 원인은 매니저를 `new`로 만드는 것이 아니라, 접근자가 매니저 구체 타입 9종과 `DesignTable.DataMgr`를 반환 타입으로 이름 붙이는 컴파일 의존이었다. 그래서 접근자와 등록 목록만 `Global.Managers.cs`로 옮겼다. `Global.cs`는 매니저 구체 타입을 이름 붙이지 않으므로 테스트가 `Global.cs`만 실물로 컴파일하고, 필요한 접근자와 등록은 테스트 쪽 partial로 제공한다. 비교한 다른 두 방안은 채택하지 않았다.

- 등록 목록을 별도 static 클래스로 옮기는 방안: 파일만 늘고 접근자의 컴파일 의존이 그대로 남는다.
- internal 호스트 주입 API를 추가하는 방안: 컴파일 의존이 남고, 제품 코드에 테스트 전용 진입점이 생긴다.

partial 분할은 공개 API, 초기화·종료 순서, 직렬화 필드 위치를 바꾸지 않는다. Unity는 클래스 이름과 같은 파일(`Global.cs`)로 MonoScript를 연결하므로 partial 파일은 영향이 없다. 실물로 바꾸는 과정에서 테스트가 실패하면 기대값을 바꾸지 않고 결함 후보로 보고한다는 원칙을 따른다.

## 테스트

이 절이 테스트 실행법과 기준선의 정본이다. 다른 문서는 수치를 적지 않고 이 절로 링크한다. 작업 전후에 `Tools/run-tests.ps1`을 실행해 아래 기준선과 비교한다. 씬, 풀, UI, 입력에 닿는 변경은 Unity Test Runner 테스트 4개도 전후로 실행한다.

테스트는 두 종류다. `Tools/` 아래 .NET 콘솔 테스트는 Unity 없이 제품 소스를 스텁과 함께 컴파일해 실행한다. `Client/Assets/Tests/`의 Unity Test Runner 테스트는 실제 에디터에서 Play Mode 경로를 실행한다.

### 기준선

콘솔 테스트 수치는 2026-10-21 `run-tests.ps1` 실행 결과이고, Unity Test Runner 수치는 테스트 코드의 단언에 적힌 기대값이다. 수치를 바꾸는 커밋에서 이 표도 함께 고친다.

| 실행 | 기준선 | 다시 확인하는 방법 |
| --- | --- | --- |
| `run-tests.ps1` 전체 | `ALL PASSED: 10 test runs` | 출력의 마지막 줄 |
| `LifecycleTests` | `PASS` 252줄, `ALL PASSED` | 이 프로젝트 출력에서 `PASS `로 시작하는 줄 수. 마지막 줄에는 개수가 없다 |
| `NotificationTests` | `13/13 notification tests passed` | 프로젝트 출력의 마지막 줄 |
| `SkillTests` | `151/151 passed` | 프로젝트 출력의 마지막 줄 |
| `SoundTests` | `43/43 sound tests passed` | 프로젝트 출력의 마지막 줄 |
| `UITests` | `42/42 passed` | 프로젝트 출력의 마지막 줄 |
| `RefactoringTests/Downloader` | `ALL PASSED: 12 downloader scenarios` | 프로젝트 출력의 마지막 줄 |
| `RefactoringTests/FirebaseCache` | `ALL PASSED: 19 firebase scenarios` | 프로젝트 출력의 마지막 줄 |
| `RefactoringTests/UISoundTool` | `21/21 UISoundTool scenarios passed` | 프로젝트 출력의 마지막 줄 |
| `RefactoringTests/UploadFlow` | `ALL PASSED: 3 upload scenarios` | 프로젝트 출력의 마지막 줄 |
| `RefactoringTests/run-build.ps1` | `ALL PASSED: 11 build flow scenarios`, `ALL PASSED: 12 build argument scenarios` | 스크립트 출력의 두 `ALL PASSED` 줄 |
| `ProjectTSamplePlayModeTests.P0FeatureSample` | `pass=103 fail=0` | `ProjectTSamplePlayModeTests.cs`의 `AssertSummary` 인자 |
| `ProjectTSamplePlayModeTests.TurnBattleSample` | `pass=61 fail=0` | `ProjectTSamplePlayModeTests.cs`의 `AssertSummary` 인자 |
| `ProjectTUIPlayModeTests.UIContainerStackAndNativeActivation` | `ProjectTUIPlayProbe.Verify()`가 끝까지 실행되고 `PROJECTT_UI_PLAYMODE_PASS` 로그를 남긴다 | `ProjectTUIPlayModeTests.cs`의 `Assert.That(verified, Is.True, ...)`와 `LogAssert.Expect` |
| `RefactoringProbeTests.UISoundToolPrefabEdits` | 검사 14개 | `RefactoringProbeTests.cs`의 `Assert.That(RefactoringProbe.Checks, Is.EqualTo(14))` |

### 콘솔 테스트

저장소 루트에서 다음 명령을 실행한다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/run-tests.ps1
```

`run-tests.ps1`은 아래 9개 프로젝트를 `dotnet run`으로 실행하고, 이어서 `RefactoringTests/run-build.ps1`을 실행한다. 모두 통과하면 `ALL PASSED: 10 test runs`를 출력한다. 하나라도 실패하면 `FAILED:` 뒤에 실패 목록을 출력하고 exit 1로 끝난다.

| 프로젝트 | `UseUniTask` | `UseUnityStubs` | `UseRealGlobal` |
| --- | --- | --- | --- |
| `LifecycleTests/LifecycleTests.csproj` | O | O | O |
| `NotificationTests/NotificationTests.csproj` | O | O | O |
| `SkillTests/SkillTests.csproj` | | | |
| `SoundTests/SoundTests.csproj` | O | O | |
| `UITests/UITests.csproj` | O | O | |
| `RefactoringTests/Downloader/Downloader.csproj` | O | | |
| `RefactoringTests/FirebaseCache/FirebaseCache.csproj` | O | O | |
| `RefactoringTests/UISoundTool/UISoundTool.csproj` | | O | |
| `RefactoringTests/UploadFlow/UploadFlow.csproj` | | | |

`run-build.ps1`은 `RefactoringTests/BuildFlow/BuildFlow.csproj`를 빌드한 뒤 BuildMgr 흐름 특성 검사와 명령줄 인자 경우 12개를 실행한다. `Tools/Tests.slnx`는 이 10개 csproj를 IDE에서 여는 솔루션이다.

프로젝트 하나만 실행하려면 그 csproj나 스크립트를 지정한다.

```powershell
dotnet run --project Tools/LifecycleTests/LifecycleTests.csproj
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/RefactoringTests/run-build.ps1
```

모든 테스트 csproj는 `Tools/Tests.Build.props`를 명시적으로 Import한다. `Directory.Build.props`로 두면 `Tools/DesignGenerator`의 프로젝트까지 이 설정을 가져가 깨지기 때문이다. props는 net9.0 Exe와 `ProductScript`(`Client/Assets/Script/`) 경로를 정하고, 세 묶음을 프로젝트가 골라 켜게 한다.

| 묶음 | 컴파일하는 것 |
| --- | --- |
| `UseUniTask` | `Client/Library/PackageCache/com.cysharp.unitask@*/Runtime`의 소스. PlayerLoop·UnityEngine에 의존하는 파일은 제외한다. Unity 프로젝트를 한 번도 열지 않아 패키지가 없으면 "Open the Unity project once to restore the UniTask package." 오류로 빌드가 멈춘다 |
| `UseUnityStubs` | `Tools/Shared/UnityStubs/*.cs`의 공용 UnityEngine 스텁 |
| `UseRealGlobal` | 실제 `Global.cs`, `ErrorCollector.cs`, `ManagerHost.cs`, `ManagerBase.cs`, `ErrorReport/*.cs`와 `Tools/Shared/GlobalTestSupport.cs`, `UtilFuncStub.cs` |

`UseRealGlobal`을 켠 프로젝트는 `Global.Managers.cs`를 컴파일하지 않는다. 대신 `GlobalTestSupport.cs`가 `RegisterManagers`와 테스트용 진입점(`BindForTests`, `InitForTests`, `UpdateForTests`, `InitializeForTests`, `ResetStaticsForTests`)을 같은 partial 클래스로 제공하고, 각 프로젝트가 쓰는 접근자만 담은 partial을 둔다(예: `LifecycleTests/GlobalAccessors.cs`). private 멤버를 리플렉션 없이 호출하므로, 제품 쪽 이름이 바뀌면 런타임이 아니라 빌드에서 깨진다. `SoundTests`, `UITests`, `SkillTests`는 아직 각 `Stubs.cs`의 가짜 `Global`을 쓴다.

`Tools/RefactoringTests/run-table-generator.ps1`(`TableGeneratorTests.cs`)은 `run-tests.ps1`에 포함되지 않는다. 생성기 경로(`-GeneratorPath`)와 출력 경로(`-OutputPath`)를 받아 따로 실행한다.

### Unity 참조 컴파일

`Tools/SoundTests/compile-unity.ps1`은 Unity 에디터를 띄우지 않고 실제 Unity 참조로 `Assembly-CSharp`와 `Assembly-CSharp-Editor`를 컴파일한다. `Client/Library/Bee/artifacts`의 최신 컴파일러 응답 파일(`.rsp`)을 복사해 출력 경로를 `Tools/SoundTests/bin/unity-compile`로 바꾸고, Unity에 포함된 Roslyn으로 컴파일한다. 응답 파일이 마지막 Unity 실행 이후의 변경을 반영하지 못하는 경우를 보정한다.

- asmdef 시절의 `ProjectT.AbilitySystem` dll 참조를 지운다.
- `Input`, `Managers`, `SkillAdapter`, `AbilitySystem` 폴더의 새 소스를 추가한다.
- 삭제된 소스를 목록에서 뺀다.

Unity 프로젝트를 한 번 열어 Bee 응답 파일이 있어야 실행된다. 기본 Unity 경로(`6000.3.14f1`)는 `-UnityData`로 바꾼다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/SoundTests/compile-unity.ps1
```

### Unity Test Runner

`Client/Assets/Tests/Editor`에 EditMode 테스트 4개가 있다. Window > General > Test Runner의 EditMode 탭에서 실행한다. 테스트는 씬을 바꾸므로 열린 씬을 먼저 저장한다. 기대값은 [기준선](#기준선) 표에 있다.

| 테스트 | 하는 일 |
| --- | --- |
| `ProjectTSamplePlayModeTests.P0FeatureSample` | FeatureSample 씬에서 Play Mode에 들어가 `Global.IsReady`를 기다린 뒤 `ProjectTP0PlayProbe.Run()`을 실행한다. Play Mode를 나온 뒤 프로브 요약을 단언한다 |
| `ProjectTSamplePlayModeTests.TurnBattleSample` | TurnBattleSample 씬에서 `ProjectTTurnBattleProbe.Run()`을 실행하고 프로브 요약을 단언한다 |
| `ProjectTUIPlayModeTests.UIContainerStackAndNativeActivation` | 빈 씬에 `Global`(`UseRemoteResource`, `LoadData` 끔)을 만들고 `ProjectTUIPlayProbe.Verify()`를 끝까지 실행한다 |
| `RefactoringProbeTests.UISoundToolPrefabEdits` | 프리팹 픽스처를 만들어 `RefactoringProbe.PrefabSoundEdits()`를 실행하고 검사 수를 단언한다 |

asmdef가 없으므로 `Tests/Editor`의 테스트는 `Assembly-CSharp-Editor`로 컴파일된다. 프로브(`Tests/Probes/*.cs`)는 Editor 폴더 밖에 있어 `Assembly-CSharp`로 컴파일되고, 파일 전체가 `#if UNITY_EDITOR`로 감싸여 플레이어 빌드에서 빠진다. P0 테스트는 의도한 예외 2건(`Scene transition is unavailable.`, `[Sample] SampleSceneB OnEnter failure test`)을 `LogAssert.Expect`로 처리하고, 다른 Error·Exception 로그는 실패로 본다.

EditMode 테스트 안의 `EnterPlayMode`·`ExitPlayMode`는 도메인을 다시 로드한다. Unity Test Framework는 리로드 뒤 열거자의 위치와 `JsonUtility`로 저장한 테스트 객체의 필드만 복원한다. 그래서 리로드를 넘는 상태는 다음 규칙을 따른다.

- `EnterPlayMode`·`ExitPlayMode`는 테스트 메서드 최상위에만 둔다. Play Mode 안에서는 리로드가 없으므로 중첩 열거자와 지역 변수를 써도 된다.
- 리로드를 넘는 값은 `[SerializeField]` 필드로만 넘긴다.
- `JsonUtility`가 null 문자열을 `""`로 되돌리므로 null에 의미를 두지 않는다. 필드는 `string.Empty`로 초기화하고 비었는지로 판단한다.
- yield를 넘어 지역 변수를 캡처하는 람다를 쓰지 않는다. 첫 yield 전에 만든 클로저 객체는 리로드 뒤 null이 된다.

### 시스템별 테스트

시스템을 바꿀 때 실행할 테스트다. 콘솔 테스트는 각 csproj의 `Compile Include`를, Unity Test Runner는 P0 프로브의 검사 그룹(`setup`, `actor`, `skill`, `input`, `ui`, `scene`)을 기준으로 정리했다.

| 시스템 | 콘솔 테스트(대상 제품 소스) | Unity Test Runner |
| --- | --- | --- |
| 매니저 생명주기 | `LifecycleTests`, `NotificationTests` (실제 `Global`·`ManagerHost`·`ManagerBase`) | P0 `setup` |
| Resource | `LifecycleTests`의 `ResourceTests` (`ResourceManager`, `Resource`, `ResourceBase`, `ResourceScope`, `SpriteAtlasResource`) | |
| Scene | `LifecycleTests`의 `SceneTests`, `SceneContractTests` (`SceneManager`, `SceneBase`) | P0 `scene` |
| Pool | `LifecycleTests`의 `PoolTests`, `SoundTests` (`PoolManager`, `GameObjectPool`, `Pool`, `PooledObject`) | P0 `actor` |
| ErrorReporter | `LifecycleTests`의 `ErrorReporterTests` (`ErrorReport/*.cs`) | |
| UI | `UITests` (`UIManager`, `UIContainer`, `UIBase`, `UIDefine`) | P0 `ui`, `UIContainerStackAndNativeActivation` |
| Input | `LifecycleTests`의 `Controller*Tests`, `ActorInputTests` (`InputManager`, `Controller`, `InputContext`) | P0 `input` |
| Notification | `NotificationTests` (`NotificationManager`) | |
| Sound | `SoundTests` (`SoundManager`, `UIClickSound`), `RefactoringTests/UISoundTool` (`UISoundTool`) | `UISoundToolPrefabEdits` |
| Actor | `LifecycleTests`의 `ActorTests`, `ActorSpawnTests`, `ActorReturnTests`, `ActorInputTests`, `ActorSkillTests` (`ComBaseActor`) | P0 `actor` |
| Skill | `SkillTests` (`AbilitySystem/**`, `SkillAdapter`), `LifecycleTests`의 `ActorSkillTests` | P0 `skill`, `TurnBattleSample` |
| Build | `run-build.ps1` (`BuildMgr`, `AddressableBuild`), `RefactoringTests/UploadFlow` (`FirebaseUploader`) | |
| Patch(원격 번들) | `RefactoringTests/Downloader` (`Addressable/Downloader.cs`), `RefactoringTests/FirebaseCache` (`Addressable/FireBase/*.cs`) | |
| TableGenerator | `run-table-generator.ps1` (`run-tests.ps1` 밖) | |

## 빌드

빌드 코드는 `Client/Assets/Script/Build/Editor/`의 `BuildMgr`과 `AddressableBuild`다. CI 진입점은 `ProjectT.Build.BuildMgr.PreBuild`이고 `Tools/Build/build.ps1`이 호출한다. 메뉴 진입점(`Build/AOS`, `Build/IOS`, `Build/EXE`, `Build/OnlyAddressable`)은 에디터에서 쓰고 에디터를 종료하지 않는다.

`PreBuild`는 다음 순서로 진행한다.

1. `-BUILD_TARGET`(Android, iOS, StandaloneWindows), `-BUILD_VERSION`, `-BUILD_NUMBER`를 읽고 검증한다. 하나라도 잘못되면 외부 작업 전에 멈춘다.
2. `PlayerSettings.bundleVersion`, Android `bundleVersionCode`, iOS `buildNumber`에 버전과 빌드 번호를 적용한다.
3. `AddressableBuild.Build()`로 Addressables를 빌드한다. `FireBaseBuild` 프로필과 `BuildScriptFirebaseMode` 빌더를 활성화하고, `CleanPlayerContent` 뒤 `BuildPlayerContent`를 실행한다. 결과의 `Error`가 있으면 `BuildFailedException`을 던진다.
4. `FirebaseUploader.UploadFiles()`로 번들을 올리고 모든 업로드를 기다린다.
5. 플레이어를 빌드한다. `BuildReport.summary.result`가 `Succeeded`가 아니면 `StartBuild`가 `BuildFailedException`을 던진다.
6. 로컬 번들 폴더를 지운다.

어느 단계든 예외가 나면 그 뒤 단계(업로드, 빌드, 삭제)를 실행하지 않는다. `PreBuild`는 예외를 `Debug.LogException`으로 한 번 기록하고 `EditorApplication.Exit(1)`, 성공하면 `Exit(0)`을 호출한다. `-executeMethod`는 async 완료를 기다리지 않으므로 `-quit` 없이 실행하고 `PreBuild`가 직접 종료 코드를 남긴다. Addressables는 플레이어 빌드에 맡기지 않고(`m_BuildAddressablesWithPlayerBuild: 0`) 명시적으로 빌드한다.

`build.ps1`은 `-Target`, `-Version`, `-BuildNumber`(1~2100000000, Android versionCode 상한)를 받는다. 요청한 타깃으로 Unity를 `-batchmode`로 띄우고(Addressables가 활성 타깃 기준으로 빌드되기 때문), 종료를 기다린 뒤 Unity의 종료 코드로 끝난다. 로그는 기본으로 `Tools/Build/bin/build-<Target>.log`에 남는다.

루트의 `Jenkinsfile`은 `unity` 라벨 에이전트에서 `TARGET`(Android, StandaloneWindows)과 `VERSION` 매개변수, Jenkins `BUILD_NUMBER`로 `build.ps1`을 실행한다. `$ErrorActionPreference = 'Stop'`을 앞에 둔다. 인자 바인딩 오류가 기본적으로 종료 오류가 아니어서, 그대로 두면 `exit $LASTEXITCODE`가 0으로 끝나기 때문이다. Unity Library를 재사용하려고 workspace를 정리하지 않으므로 동시 빌드를 막는다(`disableConcurrentBuilds`). 빌드 로그는 항상, 산출물(`Client/Builds/AOS/**` 또는 `Client/Builds/EXE/**`)은 성공 때만 보관한다.

빌드 흐름을 바꿀 때 피할 것은 다음과 같다.

- 타깃·환경 조합마다 진입점 메서드를 만드는 방식. 명령줄 인자 하나로 처리한다.
- 실패 경로에서 `Exit(0)`을 호출하거나, 업로드 예외를 로그만 남기고 삼키는 것
- 빌드 전에 원격 산출물을 지우는 순서. 빌드가 실패하면 배포 위치가 빈다.
- `BuildReport`나 외부 프로세스의 종료 코드를 확인하지 않는 것
- Addressables 빌드 여부를 개인 Preferences에 맡기는 것
