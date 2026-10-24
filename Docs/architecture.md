# ProjectT 구조

클라이언트의 진입점은 `Global` 컴포넌트 하나이고, `Global`이 매니저 9개를 등록 순서대로 초기화하고 역순으로 종료한다. 게임 코드는 `Global.Resource`, `Global.UI`처럼 `Global`을 거쳐 Ready인 매니저만 호출한다. 이 문서는 매니저 등록 순서, Ready 계약, 종료 순서의 상세 문서고, 매니저별 공개 진입점과 어셈블리, 빌드 흐름을 정리한다.

## 대상 코드

클라이언트는 Unity 6(6000.3.14f1) 프로젝트 하나(`Client/`)이고, 게임 코드는 `Client/Assets/Script/` 아래에 있다. 아래 표의 경로는 이 폴더 기준이다.

| 경로 | 내용 |
| --- | --- |
| `Common/Global.cs`, `Common/Global.Managers.cs` | 진입점 `Global`과 매니저 접근자, 등록 목록 |
| `Managers/Base/` | `ManagerHost`, `ManagerBase` |
| `Managers/` | 매니저 9개 |
| `Addressable/` | `Resource`, `ResourceScope`, 원격 번들 다운로드(`Downloader`), Firebase 프로바이더(`Addressable/FireBase/`) |
| `Build/Editor/` | `BuildMgr`, `AddressableBuild` |

빌드 스크립트는 `Tools/Build/build.ps1`, CI 정의는 저장소 루트의 `Jenkinsfile`이다.

## 진입점과 매니저 생명주기

세 타입이 생명주기를 나눠 맡는다.

| 타입 | 파일 | 책임 |
| --- | --- | --- |
| `Global` | `Common/Global.cs` | 싱글턴 MonoBehaviour. Unity 콜백을 호스트에 전달하고, 조회·대기 API와 로그 진입점을 제공한다 |
| `Global` (partial) | `Common/Global.Managers.cs` | 매니저 접근자 9개, `Table`, 등록 목록 `RegisterManagers` |
| `ManagerHost` | `Managers/Base/ManagerHost.cs` | 등록, 순차 초기화, 역순 종료, `Update`·`Pause` 전달 |
| `ManagerBase` | `Managers/Base/ManagerBase.cs` | 매니저 하나의 상태(`ManagerState`), 수명 토큰, 루트 오브젝트 |

### 부팅은 첫 Global의 Init에서 한 번 일어난다

`Global`은 `[DefaultExecutionOrder(-10000)]`이다. 씬에 놓인 `Global`의 `Awake`가 `Init`을 호출하고, `AfterSceneLoad` 시점에 활성 `Global`마다 `Init`을 다시 호출한다. `Init`은 이미 등록된 인스턴스면 바로 돌아오고, 두 번째 `Global`이면 그 컴포넌트를 파괴한다. 첫 인스턴스는 다음 순서로 진행한다.

1. `ErrorReporter`에 `FileErrorSink`(`persistentDataPath/ErrorReports`)를 지정한다.
2. `DontDestroyOnLoad`를 호출한다.
3. `ManagerHost`를 만들고 `RegisterManagers(host)`를 동기로 실행한다.
4. `InitializeAsync().Forget(LogException)`로 초기화를 시작한다.

인스펙터 필드 세 개가 부팅 동작을 정한다.

| 필드 | 기본값 | 정하는 것 |
| --- | --- | --- |
| `LoadData` | false | `DataManager`가 초기화 중에 테이블을 읽을지 |
| `UseRemoteResource` | true | `PatchManager`가 Firebase 원격 카탈로그를 준비할지 |
| `UseDebugLog` | true | `Global.Log`·`LogWarning`을 출력할지 |

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

`ManagerHost`는 매니저마다 `await manager.InitializeAsync(token)`을 차례로 실행한다. 동기로 끝나는 `OnInitializeAsync`는 첫 실제 대기 전까지 같은 호출 스택에서 이어진다. `InputManager`는 동기로 초기화되고 맨 앞에 등록되어 있으므로 `Global.Awake` 안에서 Ready가 된다. 그래서 다른 오브젝트는 `Awake`·`OnEnable`에서 별도 대기 없이 `Global.Input`을 쓸 수 있다. 이 보장은 `Init`이 `RegisterManagers`를 동기로 실행한다는 전제에 기댄다.

### Ready 계약

- `Global.GetManager<T>()`와 접근자는 Ready인 매니저만 반환한다. `Global`이 없거나, 매니저가 등록되지 않았거나, Ready가 아니면 `InvalidOperationException`을 던진다. 그래서 초기화 도중에도 앞서 Ready가 된 매니저는 조회된다.
- `Global.State`와 `Global.IsReady`는 호스트 전체 상태다. `IsReady`는 9개가 모두 Ready일 때만 true다.
- `Global.WhenReadyAsync(token)`은 `Global`이 아직 없으면 생길 때까지 기다린 뒤 호스트의 `WhenReady`를 기다린다. `WhenReady`는 성공 또는 취소로만 끝난다. 대기 후 호스트가 Ready가 아니면 `OperationCanceledException`을 던진다.
- 초기화 실패는 대기자에게 전달하지 않는다. 대기자마다 같은 오류를 다시 보고하지 않기 위해서다. 실패하면 호스트는 시작한 매니저를 역순으로 `Shutdown(ShutdownReason.InitializationFailure)`하고 `Failed`가 된다. 오류는 초기화를 시작한 `Global.InitializeAsync`에만 전달되고, `Forget(LogException)`이 한 번 보고한다.
- 정리 오류가 함께 났거나 원인이 취소 예외면 `AggregateException`으로 묶는다. UniTask가 취소 예외를 취소로 바꾸면서 메시지를 버리기 때문에 취소 예외도 감싼다.

### 종료

`OnApplicationQuit`은 `Shutdown(ShutdownReason.ApplicationExit)`, `OnDestroy`는 `Shutdown()`을 호출한다. `ManagerHost.Shutdown`은 다음 순서로 진행한다.

1. 호스트를 `Stopping`으로 바꾸고 모든 매니저가 공유하는 수명 토큰을 취소한다.
2. 시작한 매니저를 역순으로 `Shutdown`한다. 각 매니저는 `Stopping`이 되고 `OnShutdown`을 실행한 뒤 루트 오브젝트를 파괴하고 `Stopped`가 된다.
3. 매니저별 예외는 `ErrorCollector.Run`으로 모으고 다음 매니저를 계속 정리한다. 모은 예외는 `AggregateException`으로 던지고, `Global.Shutdown`의 catch가 한 번 보고한다.

종료 경로의 세부 계약은 다음과 같다.

| 상황 | 동작 |
| --- | --- |
| 외부에서 `Shutdown(ShutdownReason.InitializationFailure)` 호출 | `ArgumentException`을 던진다. 이 사유는 초기화 실패 정리에 호스트만 쓴다 |
| 호스트가 이미 `Stopping`, `Stopped`, `Failed` | `Shutdown`은 아무것도 하지 않고 돌아온다 |
| 초기화 도중 종료된 뒤 매니저 초기화가 늦게 실패 | 초기화 결과가 이미 취소되어 전달할 곳이 없으므로, `ManagerHost.InitializeAsync`가 건 `Forget(Global.LogException)`이 보고한다. 수명 토큰 취소로 생긴 취소 예외는 보고하지 않는다 |
| `Update`·`Pause` 순회 중 호스트가 Ready가 아니게 됨 | 남은 매니저를 건너뛴다. 호스트가 Ready가 아니면 처음부터 전달하지 않는다 |

종료가 역순이므로 정리 중인 매니저보다 먼저 등록된 매니저는 아직 Ready다. 정리 코드는 그 매니저를 `Global`로 조회해 쓸 수 있다. 이미 `Stopping`·`Stopped`인 매니저는 조회하지 않는다. 종료 순서 문제를 필드 캐싱으로 우회하지 않는다.

조회 허용과 새 작업 허용은 다르다. 수명 토큰이 취소된 뒤에도 Ready 매니저는 조회되지만 새 로드나 대여는 시작하지 않는다. `ManagerBase`가 검사 함수를 제공한다.

| 함수 | 허용 조건 | 쓰는 곳의 예 |
| --- | --- | --- |
| `ThrowIfWorkUnavailableInternal()` | 토큰 미취소이고 Ready | `NotificationManager.Subscribe`, `PatchManager` 다운로드 |
| `ThrowIfWorkUnavailableDuringInitInternal()` | 토큰 미취소이고 Initializing 또는 Ready | `ResourceManager.CreateScope`, `DataManager.GetTableDatas` |
| `CanAcceptWorkInternal()` | 첫 함수의 조건을 bool로 반환 | `SoundManager.PlayAt`, `NotificationManager.Publish` 순회 |

## 매니저별 공개 진입점

각 매니저의 공개 진입점과 계약만 적는다. 자세한 동작은 `Client/Assets/Script/Managers/`의 각 파일에 있다.

### Input

`InputManager`는 프로젝트 전역 입력 에셋(`InputSystem.actions`)으로 `Controller` 하나와 `EventSystem`·`InputSystemUIInputModule`을 직접 만든다. 공개 진입점은 `SetInputAllowed`(SceneManager의 전환 잠금), `AddContext`·`RemoveContext`, `CurrentDevice`, `IsDeviceLost`다. Actor는 `ComBaseActor.RegisterInputContext`로 입력 컨텍스트를 등록하고, 이 함수가 `AddContext`를 호출한다. UI 포커스는 `UIContainer`가 internal `SetUIFocused`로 알린다. 모든 입력 허용 판정은 `ApplyInputStateInternal` 한 곳에서 한다.

| 대상 | 허용 조건 |
| --- | --- |
| 게임 입력 | `sceneInputAllowed && !uiFocused && !IsDeviceLost && !switchPending` |
| UI 입력 모듈 | `sceneInputAllowed` |
| 패드 탐색 이벤트 | UI 포커스를 얻은 다음 프레임부터, 장치 전환 프레임이 아닐 때 |

패드 탐색을 한 프레임 늦추는 이유는 게임 입력 콜백이 UI를 연 프레임에 같은 Submit이 새 버튼을 누르지 않게 하기 위해서다.

장치 정책은 다음과 같다.

- 시작 장치는 PC에서 키보드, 콘솔(`Application.isConsolePlatform`)에서 패드다.
- 페어링되지 않은 장치의 버튼이 눌림 기준을 넘으면 그 장치로 전환하고, 그 프레임의 게임 입력과 탐색을 막는다. 축·트리거의 미세 입력으로는 전환하지 않는다.
- 페어링된 장치가 하나도 남지 않을 때만 분실로 본다. 분실 중에는 게임 입력만 막고, 일시정지·재연결 UI는 조작할 수 있다.
- Switch에서는 UI Submit·Cancel의 패드 바인딩을 맞바꾼다(오른쪽 A가 확인).
- 장치 변경은 `NotificationId.InputControlsChanged`, `InputDeviceLost`, `InputDeviceRegained`로 통지한다.

씬 잠금 기본값은 허용이다. 부팅 씬은 SceneManager 전환 없이 UI 버튼으로 다음 씬으로 넘어가므로, 잠금의 소유자인 SceneManager의 기본값(허용)에 맞췄다.

### Patch와 Resource

`PatchManager`는 원격 번들 부팅과 다운로드를 맡는다. 초기화에서 하는 일은 두 가지다.

- `UnityEngine.ResourceManagement.ResourceManager.ExceptionHandler`를 빈 핸들러로 채운다. 실패는 예외로 호출자에게 전달되어 경계에서 한 번 보고되므로, 작업마다 오류 로그를 남기는 기본 핸들러를 쓰지 않는다. Addressables는 첫 초기화 때 핸들러가 비어 있을 때만 기본 핸들러를 넣으므로 첫 Addressables 호출보다 먼저 채운다.
- `UseRemoteResource`가 true면 `FirebaseAddressablesManager.InitializeAsync`를 호출한다. 이 함수는 `gs://` 주소용 프로바이더 3개(`FirebaseStorageAssetBundleProvider`, `FirebaseStorageJsonAssetProvider`, `FirebaseStorageHashProvider`)와 `Addressables.WebRequestOverride`를 등록하고, Firebase 의존성을 확인한 뒤 `Addressables.InitializeAsync`를 기다린다.

주소는 `gs://`로 두고, 나가는 웹 요청의 URL만 `FirebaseAddressablesCache.OverrideWebRequest`가 받은 다운로드 URL로 바꾼다. 번들 캐시 키가 요청 시점의 주소로 정해지므로, 주소 자체를 바꾸면 같은 번들을 두 번 로드한다. 공개 진입점은 `GetDownloadSizeAsync`와 `DownloadAsync`이고, 둘 다 먼저 `FirebaseAddressablesCache.PreWarmDependenciesAsync`로 매핑을 채운다.

`ResourceManager`는 로드한 에셋을 `ResourceScope` 단위로 소유한다. 리소스는 소유 Scope 목록을 갖고, 마지막 소유 Scope가 해제되면 핸들을 해제한다. 공개 진입점은 다음과 같다.

- Scope는 `CreateScope()`와 앱 수명의 `AppScope`다. 로드 함수의 `scope`를 생략하면 현재 씬 Scope를 쓴다.
- 로드 함수는 `LoadAndGet`·`LoadAndGetAsync`, `LoadResource`·`LoadResourceAsync`, `PreloadAsync`·`PreloadResourceAsync`, `GetSprite`·`GetSpriteAsync`다.

씬 로드·언로드와 씬 Scope 교체(`SetSceneScope`, `ReleaseAll`)는 internal이고 SceneManager만 호출한다. `UnloadAddressableSceneAsync`는 같은 씬의 진행 중인 언로드가 있으면 새로 시작하지 않고 그 대기를 함께 반환한다. 앱 종료(`ApplicationExit`)에서는 씬 언로드를 건너뛴다. Unity가 씬을 정리하고, 마지막 씬 언로드는 거부하며 경고만 남기기 때문이다.

리소스 하나(`ResourceBase`)의 상태는 다섯 가지다. `Loading`에서만 `Ready`, `Failed`, `ReleasedBeforeReady`로 넘어가고, `Ready`는 해제되면 `Released`가 된다. `IsDone`은 `Ready`, `Failed`, `Released`일 때 true다.

| 상태 | 뜻 | 결과를 읽을 때 |
| --- | --- | --- |
| `Loading` | 로드 중 | |
| `Ready` | 로드 완료 | 결과를 반환한다 |
| `Failed` | 로드 실패. 핸들을 해제한다 | 최초 예외를 원래 스택 그대로 다시 던진다 |
| `ReleasedBeforeReady` | 로드가 끝나기 전에 해제됨. `IsDone`은 false로 남는다 | `ObjectDisposedException` |
| `Released` | 완료 뒤 해제됨 | `ObjectDisposedException` |

### Notification

`NotificationManager`의 공개 진입점은 `Subscribe(NotificationId id, Action listener, CancellationToken ownerToken)`와 `Publish(NotificationId id)` 두 개다.

- 같은 리스너를 다시 구독하면 무시한다.
- 소유자 토큰이 취소된 구독은 즉시 통지를 받지 않고, 같은 id의 다음 `Subscribe`나 `Publish`에서 지워진다.
- `Publish`는 구독 목록의 스냅샷을 순회하고, 순회 중 매니저가 작업을 받지 못하게 되면 멈춘다.
- 공개 API는 메인 스레드에서만 호출한다.

### Data

`DataManager`는 로컬 문자열(`Assets/Automation/Local/{Ko|Jp|En}.bytes`)과 테이블(`Assets/Automation/Table/{TableId}.bytes`)을 임시 Scope로 읽은 뒤 한 번에 교체한다. 공개 진입점은 `GetTableDatas(token)`, `Table`(`Global.Table`), `GetLocalString`이다. 동시에 호출된 `GetTableDatas`는 진행 중인 로드 하나를 함께 기다린다. 로드가 끝나기 전에 `Table`이나 `GetLocalString`을 읽으면 `InvalidOperationException`을 던진다.

### Pool

`PoolManager`는 `ResourceScope`마다 원본 프리팹별 `GameObjectPool`을 둔다. 공개 진입점은 `CreatePool`, `Get`(원본 또는 경로), `GetAsync`, `Return`, `Destroy`, `GetOriginal`, `Clear`, `ClearScope`다.

- Scope가 해제되면 그 Scope의 풀이 함께 정리된다.
- `ClearScope`는 풀을 해제하기 전에 Scope를 닫는다. 그래서 정리 중 콜백의 대여를 포함해 이후 같은 Scope로 들어오는 대여를 거부한다.
- 닫힌 항목은 Scope가 해제될 때 지워진다.

### Scene

`SceneManager`의 공개 진입점은 `TransitionAsync<T>(resourceName, mode, data)`, 콜백형 `Transition<T>`, `UnloadAdditiveAsync<T>`, `GetScene<T>`, `HasScene<T>`, `CurrentScene`, `IsTransitioning`, `IsInputAllowed`다. 씬 경로는 `Global.Table.SceneDataInfos`에서 찾는다. 전환 중에는 다른 전환을 거부한다. 모든 전환은 UI 캔버스와 `Global.Input`의 입력을 막고 시작해, 끝나면 다시 허용한다. Single 전환은 전환 화면을 띄운 뒤 다음 순서로 진행한다.

1. 기존 씬을 정지하고, 각 씬 Scope의 풀을 `Global.Pool.ClearScope`로 정리한다.
2. `Global.UI.ClearTransientWidgets()`와 `Global.Sound.StopAllSpatialSounds()`를 호출한다. 씬 오브젝트를 참조한 UI와 씬 Scope 클립을 재생하는 공간 음원이 언로드 뒤에 남지 않게 하기 위해서다.
3. 임시 씬 `SceneTransition`을 활성으로 두고 이전 씬을 언로드한다.
4. 새 Scope를 씬 Scope로 지정하고 이전 Scope를 해제한 뒤 미사용 에셋을 정리한다.
5. 새 씬을 로드하고 `SceneBase`를 붙여 `EnterAsync(scope, token, data)`를 기다린다. 전환 화면을 숨긴 뒤 씬을 활성화한다.

씬 컴포넌트는 `SceneRoot` 아래 새 GameObject에 붙는다. 나머지 계약은 다음과 같다.

| 항목 | 계약 |
| --- | --- |
| 시작 씬 재사용 | Single 전환이고, 등록된 씬이 없고, 현재 활성 씬과 이름이 같으면(대소문자 무시, 또는 이름이 비었으면) 다시 로드하지 않는다. 이 씬은 현재 씬 Scope를 이어받는다 |
| 씬 정지 | `SceneBase.Stop`은 씬 수명 토큰을 취소하고, 진입했던 씬이면 `OnFinalize`를 호출한 뒤 코루틴을 멈추고 오브젝트를 비활성화해 파괴한다. 단계별 오류는 모아 마지막에 던지고, 이미 `Stopped`면 무시한다 |
| `PrevSceneName` | Single 전환 직전 주 씬의 타입 이름이다 |
| 콜백형 `Transition` | 결과를 `eSceneTransitionResult`(Success, Failure, Canceled)로 전달한다. 취소는 로그 없이 Canceled, 그 밖의 오류는 `Global.LogException`으로 한 번 보고하고 Failure를 전달한다 |

### UI

`UIManager`는 `UIContainer` 하나와 HUD 목록을 소유한다. `UIContainer`는 2D 캔버스 아래에 레이어 루트 4개를 Static, HUD, Dynamic, System 순서로 만들어 System UI가 항상 맨 위에 그려지게 한다. 위젯 종류(`eUIContainerType`)는 세 가지다.

| 종류 | 동작 |
| --- | --- |
| Static | 스택에 쌓이고 최상단만 보인다. 표시된 최상단 Static UI가 입력 포커스를 갖는다. 닫히면 이전 UI를 복원한다 |
| Dynamic | 스택 밖의 일반 UI |
| System | 씬 전환에서 지워지지 않는다. 프리팹에 앱 Scope 소유를 추가해 씬 Scope와 함께 해제되지 않게 한다 |

공개 진입점은 `CreateWidget`·`CreateWidgetAsync`, `FindWidget`, `RemoveWidget`, `ClearTransientWidgets`(System을 뺀 위젯 제거), `SetInputAllowed`, `WidgetStack`, `TopWidget`, `GetHud`·`GetHudAsync`다.

위젯 작성과 생성 규칙은 다음과 같다.

| 항목 | 계약 |
| --- | --- |
| 바인딩 | `UIBase.Bind<T>(typeof(eEnum))`는 enum 이름과 같은 자식(루트 포함)을 찾는다. enum 값은 0부터 연속이어야 하고, 대상이 하나라도 없거나 같은 `T`를 두 번 바인딩하면 예외다. 조회는 `Get<T>(idx)`, `GetText`, `GetButton`이다 |
| 패드 선택 대상 | 마지막 선택, `firstSelected`, 상호작용 가능한 첫 자식 `Selectable` 순서로 정한다 |
| `Hide(false)` | 이전 Static UI를 복원하지 않고 스택에서 빼기만 한다. 아래 UI는 비활성으로 남고 포커스는 해제된다 |
| 프리팹 루트 | 활성 상태로 저장해야 한다. 비활성 루트는 인스턴스를 만들기 전에 예외로 거절한다. `OnDestroy`는 활성화된 적 있는 오브젝트에서만 불리므로, 외부 `Destroy`에도 해제 경로가 돌게 하기 위해서다 |
| 생성 실패 보고 | 같은 타입의 비동기 생성은 하나를 공유한다. 실패는 대기자가 있으면 대기자에게만 전달하고, 모든 대기자가 취소한 뒤 실패하면 `Forget(Global.LogException)`이 한 번 보고한다 |

HUD는 `AppScope` 풀에서 대여해 HUD 루트에 붙이고, `UIManager`가 프레임마다 한 번에 월드 위치를 갱신한다. 메인 카메라가 없는 프레임은 갱신하지 않고, 카메라 뒤의 대상은 숨긴다. HUD는 `ComHudAgent.Release()`로 반납한다. `UIManager`는 Actor보다 먼저 종료되므로, 종료 때는 HUD를 분리만 하고 인스턴스 정리는 루트 파괴와 풀 정리에 맡긴다.

### Sound

`SoundManager`는 `eSound`(Bgm, FX, UI)마다 2D `AudioSource` 채널을 하나씩 둔다.

- 2D 재생 진입점은 `Play`, `PlayFade`, `StopBgm`, `CurrentBgm`이다.
- 3D 재생 진입점은 `PlayAt(path 또는 clip, position, minDistance, maxDistance, volume, pitch)`이다. 생성자 인자 `spatialVoiceCapacity`(기본 32)개의 음원을 초기화 때 풀로 만든다.
- `PlayAt`은 작업을 받을 수 없거나 빈 음원이 없으면 false를 반환한다.
- 끝난 음원은 0.1초 간격으로 풀에 반납하고, `StopAllSpatialSounds`는 모두 멈춘다.
- 볼륨은 `SetMasterVolume`·`SetMasterMuted`와 카테고리별 `SetCategoryVolume`·`SetCategoryMuted`로 조절한다.

| 항목 | 계약 |
| --- | --- |
| BGM 같은 클립 | `Play`로 재생 중인 클립을 다시 요청하면 처음부터 재생하지 않고 pitch만 갱신한다. `PlayFade`는 같은 클립도 처음부터 재생한다 |
| 페이드 | `fadeTime`은 페이드아웃과 페이드인 각 구간의 시간이다. 새 페이드는 진행 중이던 페이드의 현재 배율에서 시작한다 |
| 3D 볼륨 | Master × FX 카테고리 × 요청 볼륨이다. 페이드 배율은 적용하지 않는다 |
| 완료 검사 제외 | 앱 일시정지, `AudioListener.pause`, 재생을 시작한 프레임, 로딩 중이거나 언로드된 클립은 끝났다고 판정하지 않는다 |
| `Clear` | 공간 음원을 반납하고 채널의 페이드를 취소해 멈춘 뒤 BGM Scope를 해제한다. 볼륨 설정은 유지한다 |
| 공간 음원 풀 | 초기화 때 만든 전용 `spatialPoolScope`에 속하고, 매니저 종료(`OnShutdown`) 때 해제한다 |

## 오류 처리

처리할 수 없는 오류는 호출자에게 전파하고, 큰 흐름의 경계에서 `Global.LogException` 한 번으로 보고한다. 게임 코드는 `Global.LogException`과 `Global.LogError`만 쓰고, 두 함수의 오류는 `ErrorReporter`로 모인다. 여러 단계를 모두 실행해야 하는 정리 경로는 `ErrorCollector`로 예외를 모아 다시 던지고, 보고는 상위 경계에 맡긴다.

규칙의 상세 문서는 루트 [AGENTS.md](../AGENTS.md)의 "오류 처리"이고, catch 위치 목록의 상세 문서는 [error-reporting.md의 catch 위치와 보고 경계](error-reporting.md#catch-위치와-보고-경계)다. ErrorReporter의 구성, 묶음과 반복 제한, 자동 수집 경로도 [error-reporting.md](error-reporting.md)에 있다.

## Actor와 스킬 시스템

`ComBaseActor`(`Actor/ComBaseActor.cs`)는 모든 Actor의 기반 MonoBehaviour이고 `IPoolable`과 `IAbilityOwner`를 구현한다. 생명주기는 `Spawn()`으로 시작해 풀 반납(`IPoolable.OnReturn`)이나 파괴로 끝난다. 파생 클래스는 `OnInit`, `OnEnter`, `OnUpdate`, `OnLateUpdate`, `Enable`, `Disable`, `OnRelease` 훅을 재정의한다. 입력 컨텍스트(`RegisterInputContext`·`UnregisterInputContext`)와 `SkillAgent`·`Status`(`AddSkillAgent`, `AddStatus`)는 Actor가 소유한다. 상태 전이와 격리 상태 계약은 [actor-lifecycle.md](actor-lifecycle.md)에 있다.

스킬 시스템(`AbilitySystem`)은 두 폴더로 나뉜다.

- `AbilitySystem/`(네임스페이스 `ProjectT.Skill`, `ProjectT.Stat`)은 `SkillAgent`, `Status`, 효과·액션 정의를 담은 코어다. 코어는 `System` 계열과 자기 네임스페이스만 `using`하고 `UnityEngine`, `Global`, 테이블을 참조하지 않는다.
- `SkillAdapter/`는 게임 쪽 어댑터다. `SkillDefinitions`가 `Global.Table`의 행을 `SkillDefinition`·`EffectDefinition`으로 바꾼다.

구조와 계약은 [ability-system.md](ability-system.md)에 있다.

## 어셈블리는 기본 어셈블리 두 개뿐이다

`Client/Assets` 아래에는 asmdef가 하나도 없다. 런타임 스크립트는 모두 `Assembly-CSharp`로, `Editor` 폴더는 `Assembly-CSharp-Editor`로 컴파일된다. 이 구성에서 생기는 결과는 두 가지다.

- `AbilitySystem` 코어가 엔진·`Global`·테이블에 의존하지 않는다는 규칙을 컴파일러가 강제하지 않는다. 코드 리뷰로 유지한다.
- `internal`은 `Assembly-CSharp` 전체에 열려 있다. `internal`은 같은 기능 묶음 안에서만 쓴다는 의도 표시일 뿐 접근을 막지 못한다. `SceneManager`가 쓰는 `ResourceManager.SetSceneScope`, `UIContainer`가 쓰는 `InputManager.SetUIFocused`가 그 예다.

`Global`은 `Global.cs`와 `Global.Managers.cs` 두 partial 파일로 나뉜다. `Global.cs`는 매니저 구체 타입을 이름 붙이지 않으므로, 콘솔 테스트는 `Global.cs`를 그대로 컴파일하고 필요한 접근자와 등록 목록을 테스트 쪽 partial로 제공한다. partial 분할은 공개 API, 초기화·종료 순서, 직렬화 필드 위치를 바꾸지 않는다. Unity는 클래스 이름과 같은 파일(`Global.cs`)로 MonoScript를 연결하므로 partial 파일은 영향이 없다.

## 빌드

빌드 코드는 `Client/Assets/Script/Build/Editor/`의 `BuildMgr`과 `AddressableBuild`다. CI 진입점은 `ProjectT.Build.BuildMgr.PreBuild`이고 `Tools/Build/build.ps1`이 호출한다. 메뉴 진입점(`Build/AOS`, `Build/IOS`, `Build/EXE`, `Build/OnlyAddressable`)은 에디터에서 쓰고 에디터를 종료하지 않는다.

`PreBuild`는 다음 순서로 진행한다.

1. `-BUILD_TARGET`(Android, iOS, StandaloneWindows), `-BUILD_VERSION`, `-BUILD_NUMBER`를 읽고 검증한다. 하나라도 잘못되면 외부 작업 전에 멈춘다.
2. `PlayerSettings.bundleVersion`, Android `bundleVersionCode`, iOS `buildNumber`에 버전과 빌드 번호를 적용한다. Addressables 설정(`Client/Assets/AddressableAssetsData/AddressableAssetSettings.asset`)의 플레이어 버전이 `bundleVersion`이므로 원격 카탈로그 이름은 `catalog_{버전}`이 된다.
3. `AddressableBuild.Build()`로 Addressables를 빌드한다. `FireBaseBuild` 프로필과 `BuildScriptFirebaseMode` 빌더를 활성화하고, `CleanPlayerContent` 뒤 `BuildPlayerContent`를 실행한다. 결과에 `Error`가 있으면 `BuildFailedException`을 던진다.
4. `FirebaseUploader.UploadFiles()`로 번들을 올리고 모든 업로드를 기다린다.
5. 플레이어를 빌드한다. `BuildReport.summary.result`가 `Succeeded`가 아니면 `StartBuild`가 `BuildFailedException`을 던진다.
6. 로컬 번들 폴더를 지운다.

어느 단계든 예외가 나면 그 뒤 단계(업로드, 빌드, 삭제)를 실행하지 않는다. `PreBuild`는 예외를 `Debug.LogException`으로 한 번 기록하고 `EditorApplication.Exit(1)`을 호출한다. 성공하면 `Exit(0)`을 호출한다. `-executeMethod`는 async 완료를 기다리지 않으므로 `-quit` 없이 실행하고 `PreBuild`가 직접 종료 코드를 남긴다. Addressables는 플레이어 빌드에 맡기지 않고(`m_BuildAddressablesWithPlayerBuild: 0`) 명시적으로 빌드한다.

`build.ps1`은 `-Target`, `-Version`, `-BuildNumber`(1~2100000000, Android versionCode 상한)를 받는다. Addressables가 활성 타깃 기준으로 빌드되므로, 요청한 타깃으로 Unity를 `-batchmode`로 띄운다. 종료를 기다린 뒤 Unity의 종료 코드로 끝난다. 로그는 기본으로 `Tools/Build/bin/build-<Target>.log`에 남는다.

루트의 `Jenkinsfile`은 `unity` 라벨 에이전트에서 `TARGET`(Android, StandaloneWindows)과 `VERSION` 매개변수, Jenkins `BUILD_NUMBER`로 `build.ps1`을 실행한다.

- 명령 앞에 `$ErrorActionPreference = 'Stop'`을 둔다. 인자 바인딩 오류는 기본적으로 종료 오류가 아니어서, 그대로 두면 `exit $LASTEXITCODE`가 0으로 끝나기 때문이다.
- Unity Library를 재사용하려고 workspace를 정리하지 않으므로 동시 빌드를 막는다(`disableConcurrentBuilds`).
- 빌드 로그는 항상 보관하고, 산출물(`Client/Builds/AOS/**` 또는 `Client/Builds/EXE/**`)은 성공 때만 보관한다.

## 테이블 생성

테이블 코드는 `Tools/DesignGenerator`가 `DesignTable/Data`의 원본에서 CodeDom으로 생성한다. 생성 코드의 계약은 생성기의 `TableGenerator`와 `LocalizeGenerator`가 정한다.

| 생성 멤버 | 계약 |
| --- | --- |
| 테이블의 `Insert` | 같은 PK가 이미 있으면 `false`를 반환하고 아무것도 바꾸지 않는다 |
| 테이블의 `Initialize` | 새 인덱스를 만든 뒤 교체한다. PK가 중복되면 `InvalidOperationException`을 던지고 기존 인덱스는 그대로 남는다 |
| 로컬라이징 | `StringDef` enum과 `LocalData`(`Dictionary<StringDef, string>`)를 만든다. 런타임은 언어별 byte 파일을 `LoadData(byte[])`로 protobuf 역직렬화한다 |

## 설계 결정

### Patch를 Resource에서 분리했다

`PatchManager`를 따로 둔 이유는 Firebase SDK 의존을 에셋 소유권을 가진 `ResourceManager`와 분리하기 위해서다. Firebase Storage는 배포용으로 붙인 것이고, 배포 저장소는 S3로 바뀔 수 있다.

### Global을 partial 두 파일로 나눴다

나눈 이유는 테스트다. 테스트가 실제 `Global`을 쓰지 못한 원인은 매니저를 `new`로 만드는 코드가 아니었다. 접근자가 매니저 구체 타입 9종과 `DesignTable.DataMgr`를 반환 타입으로 이름 붙이는 컴파일 의존이 원인이었다. 그래서 접근자와 등록 목록만 `Global.Managers.cs`로 옮겼다. 비교한 다른 두 방안은 채택하지 않았다.

- 등록 목록을 별도 static 클래스로 옮기는 방안은 파일만 늘고 접근자의 컴파일 의존이 그대로 남는다.
- internal 호스트 주입 API를 추가하는 방안은 컴파일 의존이 남고, 제품 코드에 테스트 전용 진입점이 생긴다.

### UnregisterInputContext를 남겼다

`ComBaseActor.UnregisterInputContext`는 지금 제품 코드에 호출하는 곳이 없지만 지우지 않았다. 탑승처럼 런타임에 입력 컨텍스트를 교체하는 파생 Actor가 쓰는 protected API이기 때문이다.

### asmdef를 두지 않는다

의도한 결정이다. `AbilitySystem`도 별도 asmdef를 두었다가, 별도 어셈블리로 둘 필요가 없다고 보고 지웠다. 그 대가로 코어의 비의존 규칙은 컴파일러 대신 코드 리뷰가 지킨다.

### 빌드 흐름에서 버린 방식

빌드 흐름은 다음 방식을 쓰지 않는다.

- 타깃·환경 조합마다 진입점 메서드를 만드는 방식. 명령줄 인자 하나로 처리한다.
- 실패 경로에서 `Exit(0)`을 호출하거나, 업로드 예외를 로그만 남기고 삼키는 방식
- 빌드 전에 원격 산출물을 지우는 순서. 빌드가 실패하면 배포 위치가 빈다.
- `BuildReport`나 외부 프로세스의 종료 코드를 확인하지 않는 방식
- Addressables 빌드 여부를 개인 Preferences에 맡기는 방식

## 관련 문서

| 문서 | 내용 |
| --- | --- |
| [actor-lifecycle.md](actor-lifecycle.md) | `ComBaseActor` 상태와 전이, 스폰 종료, 격리 상태 |
| [ability-system.md](ability-system.md) | 스킬 시스템의 코어와 테이블 어댑터 |
| [error-reporting.md](error-reporting.md) | `ErrorReporter`와 오류 보고 경로 |
| [testing.md](testing.md) | 테스트 실행법과 기준선, 시스템별 테스트 |
| [glossary.md](glossary.md) | 문서 용어 |
| [open-issues.md](open-issues.md) | 남은 과제와 보류한 결정 |
| [AGENTS.md](../AGENTS.md) | 작업 규칙 |
