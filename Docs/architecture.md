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

매니저는 virtual 훅 `OnInitializeAsync(token)`과 `OnShutdown(reason)` 가운데 필요한 것만 재정의한다. `ManagerState`는 `Created`, `Initializing`, `Ready`, `Stopping`, `Stopped`, `Failed`다. `Failed`는 호스트만 갖는 최종 상태다. 초기화가 실패하면 시작하지 않은 매니저는 `Created`로 남는다.

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

`OnApplicationQuit`은 `Shutdown(ShutdownReason.ApplicationExit)`, `OnDestroy`는 `Shutdown()`을 호출한다. `Global.Shutdown`은 등록된 인스턴스에서만 진행한다. 끝나면 `ErrorReporter`를 비우고 sink를 해제한 뒤 등록을 지운다. `ManagerHost.Shutdown`은 다음 순서로 진행한다.

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

`InputManager`는 프로젝트 전역 입력 에셋(`InputSystem.actions`)을 복제하지 않고 그대로 써서 `Controller` 하나와 `EventSystem`·`InputSystemUIInputModule`을 직접 만든다. 장치는 주 유저(`InputUser`) 1명에 페어링한다. 대상 장치는 콘솔 패드이고, PC 키보드·마우스는 개발과 검증용이다. UI 입력 모듈도 같은 에셋의 `UI` 맵을 쓰므로 한 입력을 UI와 게임이 따로 받지 않는다. 공개 진입점은 `SetInputAllowed`(SceneManager의 전환 잠금), `AddContext`·`RemoveContext`, `CurrentDevice`, `IsDeviceLost`다. Actor는 `ComBaseActor.RegisterInputContext`로 입력 컨텍스트를 등록하고, 이 함수가 `AddContext`를 호출한다. UI 포커스는 `UIContainer`가 internal `SetUIFocused`로 알린다. 모든 입력 허용 판정은 `ApplyInputStateInternal` 한 곳에서 한다.

| 대상 | 허용 조건 |
| --- | --- |
| 게임 입력 | `sceneInputAllowed && !uiFocused && !IsDeviceLost && !switchPending` |
| UI 입력 모듈 | `sceneInputAllowed` |
| 패드 탐색 이벤트 | UI 포커스를 얻은 다음 프레임부터, 장치 전환 프레임이 아닐 때 |

`Controller`는 `Player` 맵 전용 sealed 클래스이고 `InputManager`가 internal 속성으로 하나만 가진다. 입력은 `InputContext`(이름과 우선순위를 가진 행동 묶음)로 받는다.

- 바인딩은 `Bind(actionPath, callback, events, consume, onReset)`와 `Unbind`로 정의한다. 경로는 `Map/Action` 형식이다.
- 우선순위가 높은 컨텍스트부터 입력을 받는다. `consume`(기본값 true)이면 같은 액션의 같은 이벤트를 아래 컨텍스트에 전달하지 않고, 아래 컨텍스트의 눌린 바인딩을 리셋한다.
- 비활성 Actor가 등록한 컨텍스트는 Actor 목록에만 기록된다. 없는 액션 같은 Controller 검증은 활성화 때 일어난다.

패드 탐색을 한 프레임 늦추는 이유는 게임 입력 콜백이 UI를 연 프레임에 같은 Submit이 새 버튼을 누르지 않게 하기 위해서다.

장치 정책은 다음과 같다.

- 시작 장치는 PC에서 키보드, 콘솔(`Application.isConsolePlatform`)에서 패드다.
- 페어링되지 않은 장치의 버튼이 눌림 기준을 넘으면 그 장치로 전환하고, 그 프레임의 게임 입력과 탐색을 막는다. 축·트리거의 미세 입력과 노이즈 컨트롤로는 전환하지 않는다. PC 포인터 이동으로 전환하는 방안은 개발 편의라서 넣지 않았다.
- 페어링된 장치가 하나도 남지 않을 때만 분실로 본다. 키보드가 남은 채 마우스만 빠지면 분실이 아니다. 분실 중에는 게임 입력만 막고, 일시정지·재연결 UI는 조작할 수 있다.
- Switch에서는 UI Submit·Cancel의 패드 바인딩을 맞바꾼다(오른쪽 A가 확인).
- 장치 변경은 새 C# 이벤트 대신 `NotificationId.InputControlsChanged`, `InputDeviceLost`, `InputDeviceRegained`로 통지한다. 통지는 모든 매니저가 Ready일 때만 보낸다.

씬 잠금 기본값은 허용이다. 잠금의 소유자인 SceneManager의 기본값에 맞춘 것이다. 부팅 씬은 SceneManager 전환 없이 UI 버튼으로 다음 씬으로 넘어가므로, 처음처럼 차단이면 부팅 씬을 빠져나갈 수 없다. `InputManager`는 분실·포커스 상태를 공유하는 단일 판정이 핵심이라 나누지 않았다.

### Patch와 Resource

`PatchManager`는 원격 번들 부팅과 다운로드를 맡는다. 초기화에서 하는 일은 두 가지다.

- `UnityEngine.ResourceManagement.ResourceManager.ExceptionHandler`를 빈 핸들러로 채운다. 실패는 예외로 호출자에게 전달되어 경계에서 한 번 보고되므로, 작업마다 오류 로그를 남기는 기본 핸들러를 쓰지 않는다. Addressables는 첫 초기화 때 핸들러가 비어 있을 때만 기본 핸들러를 넣으므로 첫 Addressables 호출보다 먼저 채운다.
- `UseRemoteResource`가 true면 `FirebaseAddressablesManager.InitializeAsync`를 호출한다. 이 함수는 `gs://` 주소용 프로바이더 3개(`FirebaseStorageAssetBundleProvider`, `FirebaseStorageJsonAssetProvider`, `FirebaseStorageHashProvider`)와 `Addressables.WebRequestOverride`를 등록하고, Firebase 의존성을 확인한 뒤 `Addressables.InitializeAsync`를 기다린다.

주소는 `gs://`로 두고, 나가는 웹 요청의 URL만 `FirebaseAddressablesCache.OverrideWebRequest`가 받은 다운로드 URL로 바꾼다. 번들 캐시 키가 요청 시점의 주소로 정해지므로, 주소 자체를 바꾸면 같은 번들을 두 번 로드한다. 공개 진입점은 `GetDownloadSizeAsync`와 `DownloadAsync`이고, 둘 다 먼저 `FirebaseAddressablesCache.PreWarmDependenciesAsync`로 매핑을 채운다.

`ResourceManager`는 로드한 에셋을 `ResourceScope` 단위로 소유한다. 리소스는 소유 Scope 목록(`Owners`)을 갖고, 마지막 소유 Scope가 해제되면 핸들을 해제한다. 키는 Addressables 주소 문자열이고, 주소마다 리소스 하나를 둔다. 같은 주소를 다른 Scope에서 요청하면 같은 리소스를 함께 소유한다. 로드 중인 리소스를 기다리는 호출자는 로드 하나(`WhenReady`)를 공유한다. 공개 진입점은 다음과 같다.

- Scope는 `CreateScope()`와 앱 수명의 `AppScope`다. 앱 Scope와 첫 씬 Scope는 초기화에서 만든다. `CreateScope()`로 만든 Scope는 호출자가 `Dispose`한다. 로드 함수의 `scope`를 생략하면 현재 씬 Scope를 쓴다.
- 로드 함수는 `LoadAndGet`·`LoadAndGetAsync`, `LoadResource`·`LoadResourceAsync`, `PreloadAsync`·`PreloadResourceAsync`, `GetSprite`·`GetSpriteAsync`다. 아틀라스(`SpriteAtlasResource`)는 이름별 스프라이트를 캐시한다. 캐시한 스프라이트는 리소스가 해제될 때 파괴한다.

| 상황 | 동작 |
| --- | --- |
| 관리하지 않거나 해제된 Scope | `InvalidOperationException` |
| 같은 주소를 다른 리소스 타입으로 이미 관리 중 | `InvalidOperationException`. 캐시된 리소스가 요청한 타입의 파생 타입이면 그 리소스를 쓴다(`SpriteAtlasResource`를 `LoadAndGet<SpriteAtlas>`로 읽는 경우) |
| 빈 주소 | `ArgumentException` |
| 호출자 토큰 취소 | 그 대기만 끝난다. 요청한 Scope는 `Owners`에 남는다. 리소스는 그 Scope가 해제될 때 정리된다 |
| Scope 해제 | `ResourceScope.Dispose`는 Scope 토큰을 먼저 취소한 뒤 소유를 뗀다. 토큰 취소가 그 Scope로 기다리던 호출자와 토큰에 등록한 풀을 끝낸다. 한 리소스의 해제가 실패해도 나머지를 해제하고, 예외는 모아 마지막에 던진다 |
| 로드 실패 | 실패한 리소스를 목록에서 지우므로 다음 요청은 새로 로드한다 |

씬 로드·언로드와 씬 Scope 교체(`SetSceneScope`, `ReleaseAll`)는 internal이고 SceneManager만 호출한다. `UnloadAddressableSceneAsync`는 같은 씬의 진행 중인 언로드가 있으면 새로 시작하지 않고 그 대기를 함께 반환한다. 종료(`OnShutdown`)는 로드한 Addressables 씬을 언로드하고 남은 Scope를 모두 해제한다. 앱 종료(`ApplicationExit`)에서는 씬 언로드를 건너뛴다. Unity가 씬을 정리하고, 마지막 씬 언로드는 거부하며 경고만 남기기 때문이다.

리소스 하나(`ResourceBase`)의 상태는 다섯 가지다. `Loading`에서만 `Ready`, `Failed`, `ReleasedBeforeReady`로 넘어가고, `Ready`는 해제되면 `Released`가 된다. `IsDone`은 `Ready`, `Failed`, `Released`일 때 true다.

| 상태 | 뜻 | 결과를 읽을 때 |
| --- | --- | --- |
| `Loading` | 로드 중 | |
| `Ready` | 로드 완료 | 결과를 반환한다 |
| `Failed` | 로드 실패. 핸들을 해제한다 | 최초 예외를 원래 스택 그대로 다시 던진다 |
| `ReleasedBeforeReady` | 로드가 끝나기 전에 해제됨. `IsDone`은 false로 남는다 | `ObjectDisposedException` |
| `Released` | 완료 뒤 해제됨 | `ObjectDisposedException` |

### Notification

`NotificationManager`의 공개 진입점은 `Subscribe(NotificationId id, Action listener, CancellationToken ownerToken)`와 `Publish(NotificationId id)` 두 개다. 구독자는 `Global.Notify`를 직접 호출하고, 별도 이벤트 버스나 `SceneBase` 전달용 API는 두지 않는다. 구독(`Subscription`)은 리스너와 소유자 토큰만 담는 내부 `readonly struct`다. 구독은 id별 목록에 등록 순서대로 쌓인다. `NotificationId`는 지금 입력 장치 통지 `InputDeviceLost`, `InputDeviceRegained`, `InputControlsChanged` 세 개뿐이다. 통지처도 `InputManager` 하나다. 종료(`OnShutdown`)는 모든 목록을 비운다.

- 검사는 목록을 바꾸기 전에 끝난다. `NotificationId.None`은 `ArgumentOutOfRangeException`, null 리스너는 `ArgumentNullException`이다. Ready가 아닌 매니저는 `InvalidOperationException`, 이미 취소된 수명 토큰이나 소유자 토큰은 `OperationCanceledException`이다.
- 같은 리스너를 다시 구독하면 무시한다. 판정은 델리게이트 비교라서 매번 새로 만드는 람다는 다른 리스너다.
- 수동 해제 API는 없다. 구독을 끝내려면 소유자 토큰을 취소한다. 토큰은 어느 스레드에서 취소해도 된다. MonoBehaviour는 `GetCancellationTokenOnDestroy()`, 씬은 `SceneBase.LifetimeToken`처럼 소유자의 수명 토큰을 넘긴다.
- 소유자 토큰이 취소된 구독은 즉시 통지를 받지 않는다. 목록에서는 같은 id의 다음 `Subscribe`나 `Publish`에서 지워진다. 취소 콜백을 등록하지 않으므로 작업 스레드 취소에 대비한 잠금이 필요 없다. 소유자가 취소된 리스너를 새 토큰으로 다시 구독하면 목록 끝에 새로 등록된다.
- `Publish`는 구독 목록의 스냅샷을 순회한다. 순회 중 매니저가 작업을 받지 못하게 되면 멈춘다. 통지 중에 추가한 구독은 다음 통지부터 받는다. 리스너 안에서 같은 id를 다시 통지하면 안쪽 통지가 자기 스냅샷을 끝까지 돈 뒤 바깥 순회가 이어진다.
- 리스너는 인자를 받지 않으므로 필요한 상태는 담당 매니저에서 읽는다(`InputControlsChanged`를 받으면 `Global.Input.CurrentDevice`).
- 리스너 예외는 잡지 않고 `Publish` 호출자에게 그대로 전파한다. 같은 통지의 남은 리스너는 호출되지 않는다.
- 공개 API는 메인 스레드에서만 호출한다. 매니저가 Ready가 되기 전일 수 있는 통지처는 `InputManager`처럼 `Global.IsReady`를 먼저 확인한다.

### Data

`DataManager`는 로컬 문자열(`Assets/Automation/Local/{Ko|Jp|En}.bytes`)과 테이블(`Assets/Automation/Table/{TableId}.bytes`)을 임시 Scope로 읽은 뒤 한 번에 교체한다. 공개 진입점은 `GetTableDatas(token)`, `Table`(`Global.Table`), `GetLocalString`이다. 동시에 호출된 `GetTableDatas`는 진행 중인 로드 하나를 함께 기다린다. 로드가 끝나기 전에 `Table`이나 `GetLocalString`을 읽으면 `InvalidOperationException`을 던진다.

- `GetTableDatas` 호출자의 토큰은 그 호출자의 대기만 끝낸다. 진행 중인 로드는 매니저 수명 토큰으로만 취소된다.
- `Global`의 `LoadData`가 true면 매니저 초기화 중에 로드한다. false면 `TitleScene.OnEnter`처럼 사용하는 쪽이 `Global.Data.GetTableDatas(token)`을 기다린다.
- 다국어 bytes는 시스템 언어가 한국어면 `Ko`, 일본어면 `Jp`, 그 밖에는 `En`을 읽는다.
- `GetLocalString`은 bytes에 없는 `StringDef`에 `KeyNotFoundException`을 던진다. 그래서 `LocalData.dll`과 다국어 bytes는 같은 `Local` 실행의 결과를 함께 둔다.

### Pool

`PoolManager`는 `ResourceScope`마다 원본 프리팹별 `GameObjectPool`을 둔다. 풀 키는 이름이 아니라 Scope와 원본 참조다. 같은 원본도 Scope가 다르면 풀이 따로 생기고 인스턴스를 공유하지 않는다. 공개 진입점은 `CreatePool`, `Get`(원본 또는 경로), `GetAsync`, `Return`, `Destroy`, `GetOriginal`, `Clear`, `ClearScope`다.

| 타입 | 책임 |
| --- | --- |
| `PoolManager` | Scope 하나의 원본별 풀, Scope 토큰의 해제 등록, 닫힘 표시를 `ScopePools` 하나에 모은다 |
| `GameObjectPool` | `Pool<PooledObject>`를 감싼다. 비활성 Root 아래에 인스턴스를 만든다. 파괴된 객체는 `isValid`로 거른다 |
| `PooledObject` | 인스턴스에 붙어 소유 풀을 기억하고 자식의 `IPoolable` 콜백을 모아 호출한다 |
| `Pool<T>` | 쉬는 객체는 `Stack`, 대여 중인 객체는 참조 동등 `HashSet`으로 관리한다 |

대여와 반납의 계약은 다음과 같다.

| 항목 | 계약 |
| --- | --- |
| `scope` | 생략할 수 없는 인자다. null이면 현재 씬 Scope를 쓴다. 해제됐거나 `ResourceManager`가 관리하지 않는 Scope는 거부한다. 앱 수명은 `AppScope`로 명시한다 |
| 경로 대여 | 원본을 대여한 Scope 소유로 로드한다. 같은 경로를 쓰는 다른 Scope가 남아 있으면 원본 리소스는 해제되지 않는다 |
| 풀 확인 | 원본 대여와 `CreatePool`은 로드를 건너뛴다. 풀을 찾기 직전에 수명 토큰, Ready, Scope를 다시 확인한다. 그래서 로드를 기다리는 사이 닫히거나 해제된 Scope도 거부한다. Scope의 첫 풀을 만들 때 Scope 토큰에 해제 콜백을 등록한다 |
| `Return` | 풀 객체가 아니거나 소유 풀이 정리된 객체면 결과는 `false`다. 이미 반납한 객체는 `InvalidOperationException`이다. 수명 토큰이 취소된 뒤에도 반납은 받는다 |
| `Destroy` | 대여 중이면 `OnReturn`을 호출한 뒤 파괴한다. 풀 객체가 아니면 그대로 파괴한다. `Object.Destroy`로 직접 파괴한 객체는 `PooledObject.OnDestroy`가 풀에서 빼므로 대여 수와 용량에 남지 않는다 |
| `GetOriginal` | 이름으로 원본을 찾는다. 이름이 같은 원본이 둘 이상이면 `InvalidOperationException`을 던진다 |

반납 순서, 실패한 반납의 추적, 풀 콜백 안의 재진입 제약은 [actor-lifecycle.md](actor-lifecycle.md)에 있다. Scope 정리 계약은 다음과 같다.

- Scope가 해제되면 그 Scope의 풀이 함께 정리된다. `ResourceScope.Dispose`가 토큰을 먼저 취소하므로 풀이 인스턴스 파괴를 요청한 뒤에 원본 소유가 떨어진다.
- `ClearScope`는 풀을 해제하기 전에 Scope를 닫는다. 그래서 정리 중 콜백의 대여를 포함해 이후 같은 Scope로 들어오는 대여를 거부한다.
- 닫힌 항목은 Scope가 해제될 때 지워진다.
- 종료(`OnShutdown`)는 `Clear()`로 앱 Scope를 포함한 모든 풀을 해제한다.

### Scene

`SceneManager`의 공개 진입점은 `TransitionAsync<T>(resourceName, mode, data)`, 콜백형 `Transition<T>`, `UnloadAdditiveAsync<T>`, `GetScene<T>`, `HasScene<T>`, `CurrentScene`, `IsTransitioning`, `IsInputAllowed`다. 씬 경로는 `Global.Table.SceneDataInfos`에서 찾는다. 전환 중에는 다른 전환을 거부한다. 모든 전환은 UI 캔버스와 `Global.Input`의 입력을 막고 시작해, 끝나면 다시 허용한다. Single 전환은 전환 화면을 띄운 뒤 다음 순서로 진행한다.

1. 기존 씬을 정지하고, 각 씬 Scope의 풀을 `Global.Pool.ClearScope`로 정리한다.
2. `Global.UI.ClearTransientWidgets()`와 `Global.Sound.StopAllSpatialSounds()`를 호출한다. 씬 오브젝트를 참조한 UI와 씬 Scope 클립을 재생하는 공간 음원이 언로드 뒤에 남지 않게 하기 위해서다.
3. 임시 씬 `SceneTransition`을 활성으로 두고 이전 씬을 언로드한다.
4. 새 Scope를 씬 Scope로 지정하고 이전 Scope를 해제한 뒤 미사용 에셋을 정리한다.
5. 새 씬을 로드하고 `SceneBase`를 붙여 `EnterAsync(scope, token, data)`를 기다린다. 전환 화면을 숨긴 뒤 씬을 활성화한다.

씬 컴포넌트는 `SceneRoot` 아래 새 GameObject에 붙는다. 내부 클래스 `SceneEntry`가 씬 컴포넌트, 씬 Scope, Addressables 씬 핸들, Additive 여부를 묶는다. 전환 화면 `SceneTransitionView`는 맨 위 오버레이 캔버스를 페이드로 보이고 숨긴다. 보이는 동안에는 레이캐스트를 막는다. 나머지 계약은 다음과 같다.

| 항목 | 계약 |
| --- | --- |
| 요청 검사 | 입력 차단보다 먼저 끝나므로, 거부된 요청은 현재 씬을 정지하지 않는다. 매니저가 Ready가 아니거나 전환 중이면 `InvalidOperationException`이다. 이때 진행 중인 전환은 그대로 끝난다. Single은 이미 Active인 같은 타입을, Additive는 이미 등록된 같은 타입을 거부한다. Additive는 Active인 주 씬이 있어야 한다. 모드나 테이블 키가 잘못돼도 거부한다 |
| 조회 | `CurrentScene`은 Active인 주 씬을, `GetScene<T>`와 `HasScene<T>`는 Active인 씬만 반환한다. 씬의 `OnUpdate`도 전환 중이 아닐 때 Active인 씬에만 전달된다. 전환 중에는 `IsInputAllowed`가 false다 |
| 씬 구현 | `OnInitialize`와 `OnFinalize`는 abstract다. `OnEnter`, `OnActivate`, `OnUpdate`는 필요하면 재정의한다. 씬 수명 토큰(`LifetimeToken`)은 매니저 수명 토큰에 연결되어 있어, 씬이 정지하거나 매니저가 종료되면 취소된다 |
| Additive 전환 | 자기 Scope를 새로 만들어 로드한다. 이전 씬 정지부터 미사용 에셋 정리까지의 단계는 없고, 현재 주 씬은 바꾸지 않는다. 씬 오브젝트를 참조하는 UI와 공간 음원도 정리하지 않는다 |
| `UnloadAdditiveAsync<T>` | 입력을 막은 채 그 씬을 정지하고 풀을 정리한다. 한 프레임 뒤 언로드하고 Scope를 해제한다 |
| 전환 실패 | `TransitionAsync`는 예외를 던진다. 실패한 분기도 `ResetView`와 입력 허용을 거친다. 이번 전환에서 만든 Scope와 엔트리는 정리한다. Single 로드가 실패하면 이전 씬 Scope는 이미 해제됐으므로 빈 Scope를 씬 Scope로 지정한다. 진입이나 활성화에 실패한 Single 씬은 정지된 엔트리로 남겨 다음 Single 전환이 언로드와 Scope 해제를 맡는다 |
| 종료 중 | 매니저가 종료 중이면 새 Scope를 만들거나 언로드를 시작하지 않는다. `ResetView`와 입력 허용도 건너뛴다. `OnShutdown`은 모든 씬을 정지하고 풀을 정리한다 |
| 시작 씬 재사용 | Single 전환이고, 등록된 씬이 없고, 현재 활성 씬과 이름이 같으면(대소문자 무시, 또는 이름이 비었으면) 다시 로드하지 않는다. 이 씬은 현재 씬 Scope를 이어받는다 |
| 씬 정지 | `SceneBase.Stop`은 씬 수명 토큰을 취소하고, 진입했던 씬이면 `OnFinalize`를 호출한 뒤 코루틴을 멈추고 오브젝트를 비활성화해 파괴한다. 단계별 오류는 모아 마지막에 던지고, 이미 `Stopped`면 무시한다 |
| `PrevSceneName` | Single 전환 직전 주 씬의 타입 이름이다 |
| 콜백형 `Transition` | 결과를 `eSceneTransitionResult`(Success, Failure, Canceled)로 전달한다. 취소는 로그 없이 Canceled, 그 밖의 오류는 `Global.LogException`으로 한 번 보고하고 Failure를 전달한다 |

### UI

`UIManager`는 `UIContainer` 하나와 HUD 목록을 소유한다. `UIContainer`는 UI 카메라와 2D 캔버스, 레이어 루트, 위젯 저장소, 진행 중 생성, Static 스택, 포커스를 소유한다. 2D 캔버스 아래에 레이어 루트 4개를 Static, HUD, Dynamic, System 순서로 만들어 System UI가 항상 맨 위에 그려지게 한다. HUD는 위젯 종류가 아니라 레이어 루트 하나다. 위젯은 모두 `UIBase`를 상속한다. 경로는 `UIDefine`이 `eUIType`마다 정하고, 정의되지 않은 타입은 `ArgumentOutOfRangeException`이다. 안전 영역은 `UISafeArea`가 앵커로 적용한다. 위젯 루트의 앵커는 `UIContainer`가 다시 정하므로 `UISafeArea`는 루트 아래 패널에 붙인다. 위젯 종류(`eUIContainerType`)는 세 가지다.

| 종류 | 동작 |
| --- | --- |
| Static | 스택에 쌓이고 최상단만 보인다. 표시된 최상단 Static UI가 입력 포커스를 갖는다. 닫히면 이전 UI를 복원한다 |
| Dynamic | 스택 밖의 일반 UI. `Show()` 때 같은 레이어의 맨 앞으로 간다 |
| System | 스택 밖이고 씬 전환에서 지워지지 않는다. 프리팹에 앱 Scope 소유를 추가해 씬 Scope와 함께 해제되지 않게 한다. 이 소유는 등록 직전에 추가하므로 생성이 실패하면 앱 Scope에 참조가 남지 않는다 |

씬은 UI를 소유하지 않는다. 씬이 소유하면 공통 UI의 수명이 씬 전환 순서에 묶인다. 이전 씬 오브젝트를 참조하는 UI는 Static이나 Dynamic으로 만든다.

공개 진입점은 `CreateWidget`·`CreateWidgetAsync`, `FindWidget`, `RemoveWidget`, `ClearTransientWidgets`(System을 뺀 위젯 제거), `SetInputAllowed`, `WidgetStack`, `TopWidget`, `GetHud`·`GetHudAsync`다.

위젯 작성과 생성 규칙은 다음과 같다.

| 항목 | 계약 |
| --- | --- |
| 요청 검사 | 수명 토큰이 취소됐으면 `OperationCanceledException`이다. 정리 중이거나 종료된 컨테이너면 `InvalidOperationException`이다 |
| 생성 공유 | 같은 타입의 비동기 요청은 진행 중 생성 하나에 합류해 같은 인스턴스를 받는다. 결과는 `UniTaskCompletionSource`로 넘긴다. 호출자 토큰 취소는 그 호출자의 대기만 끝낸다. 결과가 요청한 제네릭 타입이 아니면 `InvalidOperationException`이다. 비동기 생성 중인 타입을 동기 `CreateWidget`으로 요청해도 `InvalidOperationException`이다. 동기 생성도 진행 중 생성으로 등록한다. 그래서 `OnEnter` 안의 같은 타입 비동기 요청이 합류하고, 제거와 정리가 등록 전에 생성을 취소할 수 있다 |
| 수명 훅 | 새 UI는 비활성이고 `Show()`해야 보인다. `OnShow`와 `OnHide`는 abstract다. `OnEnter`는 인스턴스마다 한 번, `OnLeave`는 해제 때 한 번 불린다. 가려진 Static UI는 `OnHide`를 받고, 복원될 때 `OnShow`를 다시 받는다 |
| `Show()` | 표시 순서, 스택, 포커스를 `OnShow`보다 먼저 바꾼다. `OnShow`에서 연 UI가 앞에 남게 하기 위해서다. Dynamic과 System은 스택과 포커스를 바꾸지 않는다. 이미 보이는 UI는 맨 앞으로만 옮기고 `OnShow`를 다시 부르지 않는다. 해제된 UI의 `Show()`는 예외다. `OnShow`가 실패하면 예외가 전파되고 UI는 최상단에 열린 채 남는다. 호출자는 `Hide()`로 닫을 수 있다 |
| 해제 | 제거, 씬 전환 정리, 종료, 생성 실패, 외부 `Destroy`는 모두 `UIContainer.ReleaseWidget`을 지난다. 등록, 스택, 이전 UI 복원을 끝낸 뒤 `OnHide`와 `OnLeave`를 부르므로 콜백이 실패해도 스택과 포커스가 맞는다. 해제는 한 번만 실행된다. 해제된 UI의 `Hide()`는 아무것도 하지 않는다 |
| 정리 | 씬 전환 정리와 종료는 같은 함수를 쓴다. 진행 중 생성을 취소해 이전 씬 UI의 늦은 등록을 막는다. 콜백이 실패해도 나머지 UI를 파괴한 뒤 예외를 모아 던진다. 정리 중의 재진입은 무시한다 |
| 포커스 | `Global.Input.SetUIFocused`는 보이는 Static 최상단이 생기거나 없어질 때만 호출한다. 최상단이 바뀌면 패드 선택 대상만 옮긴다. `Hide(false)`로 남은 비활성 UI는 포커스로 보지 않는다 |
| `SetInputAllowed` | false면 2D 캔버스의 `CanvasGroup`을 막는다. 씬 전환이 막고, 전환이 끝나면 다시 허용한다 |
| 바인딩 | `UIBase.Bind<T>(typeof(eEnum))`는 enum 이름과 같은 자식(루트 포함)을 찾는다. enum 값은 0부터 연속이어야 하고, 대상이 하나라도 없거나 같은 `T`를 두 번 바인딩하면 예외다. 조회는 `Get<T>(idx)`, `GetText`, `GetButton`이다 |
| 패드 선택 대상 | 마지막 선택, `firstSelected`, 상호작용 가능한 첫 자식 `Selectable` 순서로 정한다 |
| `Hide(false)` | 이전 Static UI를 복원하지 않고 스택에서 빼기만 한다. 아래 UI는 비활성으로 남고 포커스는 해제된다 |
| 프리팹 루트 | 활성 상태로 저장해야 한다. 비활성 루트는 인스턴스를 만들기 전에 예외로 거절한다. `OnDestroy`는 활성화된 적 있는 오브젝트에서만 불리므로, 외부 `Destroy`에도 해제 경로가 돌게 하기 위해서다 |
| 생성 실패 보고 | 같은 타입의 비동기 생성은 하나를 공유한다. 실패는 대기자가 있으면 대기자에게만 전달하고, 모든 대기자가 취소한 뒤 실패하면 `Forget(Global.LogException)`이 한 번 보고한다 |

HUD는 `AppScope` 풀에서 대여해 HUD 루트에 붙이고, `UIManager`가 프레임마다 한 번에 월드 위치를 갱신한다. 메인 카메라가 없는 프레임은 갱신하지 않고, 카메라 뒤의 대상은 숨긴다. HUD는 그 Actor가 `ComHudAgent.Release()`로 반납한다. 씬 전환이 반납하면 앱 Scope Actor가 계속 쓰는 HUD가 다른 Actor에게 대여될 수 있기 때문이다. 추적하지 않는 HUD를 반납하면 예외다. HUD 컴포넌트는 대여 전에 검사해, 실패했을 때 반납할 부분 상태를 만들지 않는다. `UIManager`는 Actor보다 먼저 종료되므로, 종료 때는 HUD를 분리만 하고 인스턴스 정리는 루트 파괴와 풀 정리에 맡긴다.

### Sound

`SoundManager`는 `eSound`(Bgm, FX, UI)마다 2D `AudioSource` 채널을 하나씩 둔다.

- 2D 재생 진입점은 `Play(path 또는 clip, type, pitch)`, `PlayFade(path, type, fadeTime, pitch)`, `StopBgm`, `CurrentBgm`이다. FX와 UI는 채널의 `AudioSource` 하나에 `PlayOneShot`으로 겹쳐 재생한다. 그래서 겹친 재생의 pitch를 하나씩 조절할 수 없다. `UIClickSound`는 `Toggle`, `Button`, `EventTrigger` 중 한 경로에 클릭음을 등록해 UI 채널로 재생한다.
- 3D 재생 진입점은 `PlayAt(path 또는 clip, position, minDistance, maxDistance, volume, pitch)`이다. 생성자 인자 `spatialVoiceCapacity`(기본 32)개의 음원을 초기화 때 풀로 만든다.
- `PlayAt`은 작업을 받을 수 없거나 빈 음원이 없으면 false를 반환한다. 반환값은 재생을 시작했는지뿐이고, 재생 중인 공간 음원을 제어하는 핸들은 없다. 슬롯이 가득 찬 요청은 클립을 로드하기 전에 끝난 음원을 먼저 회수한다.
- 공간 음원 오브젝트의 생성, 보관, 파괴는 `Global.Pool`에 맡긴다. `SoundManager`는 슬롯(`SpatialSound`)에 재생 상태와 동시 재생 수만 기록한다.
- 작업을 받을 수 없는 매니저에서 `Play`, `PlayFade`는 아무것도 하지 않는다. 인자도 검사하지 않는다. 정지, 정리, 볼륨 함수는 종료 중에도 쓸 수 있다.
- pitch는 0 초과 3 이하여야 한다. 위치·거리·볼륨·`fadeTime`은 유한값, `minDistance`는 양수여야 한다. `maxDistance`는 `minDistance`보다 커야 한다. 어기면 클립을 로드하기 전에 `ArgumentOutOfRangeException`을 던진다. 범위 밖의 유한한 볼륨은 0과 1 사이로 자른다.
- 끝난 음원은 `Time.unscaledDeltaTime`으로 센 0.1초 간격으로 풀에 반납한다. `StopAllSpatialSounds`는 모두 멈춘다. 긴 프레임 뒤에도 밀린 검사를 몰아서 하지 않는다. 진행 중인 음원이 없으면 주기를 세지 않는다.
- `PoolManager.Clear`로 소유 풀이 없어진 음원은 반납하지 않고 슬롯만 비운다. 다음 재생 요청이 새로 대여한다.
- 볼륨은 `SetMasterVolume`·`SetMasterMuted`와 카테고리별 `SetCategoryVolume`·`SetCategoryMuted`로 조절한다.

| 항목 | 계약 |
| --- | --- |
| BGM 같은 클립 | `Play`로 재생 중인 클립을 다시 요청하면 처음부터 재생하지 않고 pitch만 갱신한다. `PlayFade`는 같은 클립도 처음부터 재생한다 |
| 클립 Scope | BGM 클립은 사운드 전용 `bgmScope`에 두어 씬 전환 뒤에도 이어 재생한다. `bgmScope`는 첫 BGM 로드 때 만든다. FX·UI 클립과 경로로 재생한 공간 음원 클립은 현재 씬 Scope에 두므로 Single 전환 뒤에 남지 않는다. 씬을 넘어 이어야 하는 소리는 BGM 채널로 재생한다 |
| 채널 볼륨 | Master × 카테고리 × 페이드 배율이다. 음소거는 볼륨과 따로 둔다 |
| 페이드 | 채널마다 토큰과 배율을 따로 둔다. `fadeTime`은 페이드아웃과 페이드인 각 구간의 시간이다. 페이드는 `Time.unscaledDeltaTime`으로 진행한다. 새 페이드는 진행 중이던 페이드의 현재 배율에서 시작한다. 페이드가 끝나거나 취소되면, 여전히 채널의 소유자일 때만 배율을 1로 되돌린다 |
| 페이드 취소 | `Play`와 `fadeTime`이 0 이하인 `PlayFade`는 채널의 페이드를 취소하고 바로 재생한다. 클립이 null이면 기존 페이드를 건드리지 않는다. BGM 페이드는 수명 토큰에만 연결한다. FX·UI 페이드는 씬 Scope 토큰에도 연결해 해제된 클립으로 페이드인하지 않는다 |
| 3D 볼륨 | Master × FX 카테고리 × 요청 볼륨이다. 페이드 배율은 적용하지 않는다 |
| 완료 검사 제외 | 앱 일시정지, `AudioListener.pause`, 재생을 시작한 프레임, 로딩 중이거나 언로드된 클립은 끝났다고 판정하지 않는다 |
| `Clear` | 공간 음원을 반납하고 채널의 페이드를 취소해 멈춘 뒤 BGM Scope를 해제한다. 볼륨 설정과 공간 음원 풀은 유지한다. `StopAllSpatialSounds`, `Clear`, `OnShutdown`은 반납 실패를 모아 마지막에 던진다 |
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

빌드 코드는 `Client/Assets/Script/Build/Editor/`의 `BuildMgr`과 `AddressableBuild`다. CI 진입점은 `ProjectT.Build.BuildMgr.PreBuild`이고 `Tools/Build/build.ps1`이 호출한다. 메뉴 진입점 `Build/AOS`, `Build/IOS`, `Build/EXE`는 플레이어만, `Build/OnlyAddressable`은 Addressables만 빌드한다. 메뉴 빌드는 업로드하지 않고, 에디터도 종료하지 않는다.

`PreBuild`는 다음 순서로 진행한다.

1. `-BUILD_TARGET`(Android, iOS, StandaloneWindows), `-BUILD_VERSION`, `-BUILD_NUMBER`를 읽고 검증한다. 지원하지 않는 타깃, 빈 버전, 0 이하나 숫자가 아닌 빌드 번호는 로그를 남기고 외부 작업 전에 멈춘다. 값 없이 끝난 인자는 `IndexOutOfRangeException`이 되어 `Exit(1)`로 끝난다. 같은 인자를 여러 번 주면 마지막 값을 쓴다.
2. `PlayerSettings.bundleVersion`, Android `bundleVersionCode`, iOS `buildNumber`에 버전과 빌드 번호를 적용한다. Addressables 설정(`Client/Assets/AddressableAssetsData/AddressableAssetSettings.asset`)의 플레이어 버전이 `bundleVersion`이므로 원격 카탈로그 이름은 `catalog_{버전}`이 된다.
3. `AddressableBuild.Build()`로 Addressables를 빌드한다. `FireBaseBuild` 프로필과 `BuildScriptFirebaseMode` 빌더를 활성화한다. 에디터가 컴파일 중이면 5초 간격으로 기다린 뒤 `CleanPlayerContent`와 `BuildPlayerContent`를 실행한다. 결과에 `Error`가 있으면 `BuildFailedException`을 던진다. 설정 에셋, 프로필, 빌더 중 하나라도 없거나 빌더가 `DataBuilders`에 없어도 `BuildFailedException`을 던진다. 다른 프로필이나 빌더로 대체하면 Firebase용이 아닌 번들을 올릴 수 있기 때문이다.
4. `FirebaseUploader.UploadFiles()`로 번들을 올리고 모든 업로드를 기다린다. 업로드 목록은 Addressables 빌드가 끝난 뒤 원격 카탈로그 빌드 폴더에서 읽는다. 하위 폴더는 포함하지 않는다. 번들은 `gs://projectt-1179c.appspot.com`의 `Android/{버전}`, `IPhone/{버전}`, `Window/{버전}`에 올라간다.
5. 플레이어를 빌드한다. `BuildReport.summary.result`가 `Succeeded`가 아니면 `StartBuild`가 `BuildFailedException`을 던진다. 결과는 `Client` 아래 `Builds/AOS/test.apk`, `Builds/EXE/ProjectJ.exe`(Development 빌드), `Builds/IOS`(Xcode 프로젝트)에 생긴다.
6. 로컬 번들 폴더를 지운다.

`FireStorageBuildScript`는 `BuildScriptFirebaseMode` 빌더 에셋의 스크립트다. `BuildScriptPackedMode`를 상속해 원격 데이터를 Firebase Storage 로더로 받게 한다. Firebase 주소는 Unity가 지원하지 않는 `gs://` 형식이라 [RobinBird Studios의 Firebase Tools](https://gitlab.com/robinbird-studios/libraries/unity-plugins/firebase-tools) 방식을 적용했다. 빌드 코드는 Editor와 Firebase SDK의 외부 API 경계라서 UniTask 대신 `Task`를 쓴다.

어느 단계든 예외가 나면 그 뒤 단계(업로드, 빌드, 삭제)를 실행하지 않는다. `PreBuild`는 예외를 `Debug.LogException`으로 한 번 기록하고 `EditorApplication.Exit(1)`을 호출한다. 배치 빌드에서는 런타임 `ErrorReporter`가 초기화되지 않으므로 `Global.LogException`을 쓰지 않는다. 업로드 전에 실패하면 원격에는 아무것도 올라가지 않는다. 업로드 뒤 플레이어 빌드에서 실패하면 번들은 이미 올라간 상태다. 성공하면 `Exit(0)`을 호출한다. `-executeMethod`는 async 완료를 기다리지 않으므로 `-quit` 없이 실행하고 `PreBuild`가 직접 종료 코드를 남긴다. Addressables는 플레이어 빌드에 맡기지 않고(`m_BuildAddressablesWithPlayerBuild: 0`) 명시적으로 빌드한다.

`build.ps1`은 `-Target`, `-Version`, `-BuildNumber`(1~2100000000, Android versionCode 상한)를 받는다. Addressables가 활성 타깃 기준으로 빌드되므로, 요청한 타깃으로 Unity를 `-batchmode`로 띄운다. 종료를 기다린 뒤 Unity의 종료 코드로 끝난다. 로그는 기본으로 `Tools/Build/bin/build-<Target>.log`에 남는다. Unity 경로는 `-UnityPath`, 로그 위치는 `-LogPath`로 바꾼다. 에이전트에서 직접 실행하는 명령은 `Tools/Build/build.ps1 -Target <Android|iOS|StandaloneWindows> -Version <버전> -BuildNumber <번호>`다.

루트의 `Jenkinsfile`은 `unity` 라벨 에이전트에서 `build.ps1`을 실행한다. checkout은 Git LFS와 함께 한다. 인자는 `TARGET`(Android, StandaloneWindows)과 `VERSION`(기본값 `0.0.1`) 매개변수, Jenkins `BUILD_NUMBER`다. iOS는 `build.ps1`을 직접 실행할 때만 고를 수 있다.

- 명령 앞에 `$ErrorActionPreference = 'Stop'`을 둔다. 인자 바인딩 오류는 기본적으로 종료 오류가 아니어서, 그대로 두면 `exit $LASTEXITCODE`가 0으로 끝나기 때문이다.
- Unity Library를 재사용하려고 workspace를 정리하지 않으므로 동시 빌드를 막는다(`disableConcurrentBuilds`).
- 빌드 로그는 항상 보관하고, 산출물(`Client/Builds/AOS/**` 또는 `Client/Builds/EXE/**`)은 성공 때만 보관한다.

## 테이블 생성

테이블 코드는 `Tools/DesignGenerator`가 `DesignTable/Data`의 원본에서 CodeDom으로 생성한다. 생성 코드의 계약은 생성기의 `TableGenerator`와 `LocalizeGenerator`가 정한다.

| 구성 요소 | 책임 |
| --- | --- |
| `DesignTable/Data` | 엑셀 원본, 변환 XML `Table/Xml`, 생성 결과 `Table/Client`와 `Dll` |
| `DesignGenerator` | Unity 밖의 CLI. `Program`은 명령, 종료 코드, DLL 배치를 맡는다. `TableGenerator`·`TableValidator`는 테이블, `LocalizeGenerator`는 다국어, `DataMgrGenerator`·`DataMgrSerializerGenerator`는 관리자를 만든다 |
| `DesignTool` | exe 옆 `settings.ini`의 경로와 옵션으로 `DesignGenerator.exe`를 자식 프로세스로 실행하는 GUI |
| `DesignTable/Gen` | 빌드한 두 exe와 `TableGenerate ... All`을 실행하는 `Gen.bat` |
| 생성 테이블 클래스 | 테이블마다 행 `{테이블}Info`와 컨테이너 `{테이블}Infos`. 컨테이너는 protobuf 행 목록 `dataInfo`와 PK 인덱스 `datas`를 갖는다. 목록 규칙이 있으면 `listData`도 갖는다 |
| `DataMgr`, `DataMessageSerializer` | `TableId` enum, 테이블별 로드 핸들러, 참조 연결 `SetUpRef`를 갖는 관리자. 생성 클래스와 함께 `DataMgr.dll`로 컴파일된다 |

명령은 네 개다. 출력 경로는 `Gen.bat`과 `DesignTool`의 기본값대로 `Client/Assets`를 준다.

| 명령 | 입력 | 출력 |
| --- | --- | --- |
| `Enum <dataPath> <outputPath>` | `Enum/enum.xlsx` | `Automation/Enum/DesignEnum.cs` |
| `TableCreate <dataPath> <tableName\|null>` | `table명세서.xlsx` | `Table/Tables`의 테이블 엑셀 |
| `TableGenerate <dataPath> <outputPath> <All\|One> [tableName]` | `Table/Tables`의 엑셀 | `Table/Client`의 C#, `Dll/DataMgr.dll`, `Automation/Dll`, `Automation/Table/{테이블}.bytes` |
| `Local <dataPath> <outputPath>` | `Local`의 엑셀 | `Dll/LocalData.dll`, `Automation/Dll`, `Automation/Local/{Ko\|En\|Jp}.bytes` |

옵션은 `--full`(기본은 바뀐 엑셀만 변환), `--format=json`, `--warn-as-error`(경고도 실패), `--merge`다. 종료 코드는 성공 0, 실패 1, 사용법 오류 2다. 실행 규칙은 다음과 같다.

- 엑셀 변환은 Excel COM을 쓰므로 Excel이 설치된 Windows에서만 돈다.
- 검사 오류가 하나라도 있으면 코드, DLL, bytes는 쓰지 않고 종료 코드 1로 끝난다. 변환 XML은 검사 전에 쓴다.
- 검사 오류는 `DiagnosticBag`에 모두 모은 뒤 한 번에 실패한다. 그래서 한 번 실행으로 원본의 오류를 모두 볼 수 있다.
- `--format=json`은 `TG` 코드, 파일, 행, 컬럼이 담긴 진단을 출력한다. CI는 종료 코드와 이 진단으로 결과를 판단한다.
- `enum.xml`이 없으면 검사 단계의 진단 오류가 된다. enum을 고쳤으면 먼저 `Enum`을 실행한다.
- `One`은 참조 해석을 위해 모든 XML을 읽는다. PK 중복 검사와 클래스·bytes 생성은 대상 테이블만 한다. `DataMgr`와 `DataMessageSerializer`는 모든 테이블로 다시 만든다.
- 모든 테이블에는 부동소수점이 아닌 PK가 있어야 한다. 참조 컬럼은 PK가 하나인 테이블만 가리킨다. -1은 참조 없음이다.
- 키 타입은 PK가 하나면 그 컬럼 타입, 둘 이상이면 `System.ValueTuple`이다. PK 7개 이상의 결함은 [open-issues.md](open-issues.md)에 있다.
- 다국어는 `USE`가 `Y`인 행만 들어간다. 빈 번역 칸은 오류 없이 빈 문자열이 된다. `LOCALNAME`은 `StringDef`의 멤버 이름이 되므로 C# 식별자여야 한다.
- 생성 결과는 손으로 고치지 않는다.

| 생성 멤버 | 계약 |
| --- | --- |
| 테이블의 `Insert` | 같은 PK가 이미 있으면 `false`를 반환하고 아무것도 바꾸지 않는다 |
| 테이블의 `Initialize` | 새 인덱스를 만든 뒤 교체한다. PK가 중복되면 `InvalidOperationException`을 던지고 기존 인덱스는 그대로 남는다 |
| 테이블의 `Get`, `GetListById` | 없는 키에는 null을 반환한다. 문자열 PK에 null을 넘기면 `ArgumentNullException`을 던진다 |
| 로컬라이징 | `StringDef` enum과 `LocalData`(`Dictionary<StringDef, string>`)를 만든다. 런타임은 언어별 byte 파일을 `LoadData(byte[])`로 protobuf 역직렬화한다 |

## 설계 결정

### Patch를 Resource에서 분리했다

`PatchManager`를 따로 둔 이유는 Firebase SDK 의존을 에셋 소유권을 가진 `ResourceManager`와 분리하기 위해서다. Firebase Storage는 배포용으로 붙인 것이고, 배포 저장소는 S3로 바뀔 수 있다.

### 리소스 수명을 소유 Scope로 정한다

소유 Scope가 모두 사라진 리소스만 해제한다. 그래서 씬과 앱이 같은 리소스를 함께 써도 한쪽 해제가 다른 쪽에 영향을 주지 않는다.

이전 구조는 리소스마다 `DontDestroy` 플래그를 두었다. 씬 전환 때는 플래그가 없는 리소스를 `ReleaseAll(isAll)`로 전부 해제했다. Resources 폴더 로드는 프로덕션 호출이 없어서 출처 인자, 출처별 키와 함께 지웠다.

### 풀의 scope를 필수 인자로 받는다

두 매니저 모두 null `scope`를 현재 씬 Scope로 해석한다. 앱 수명은 `AppScope`로 명시한다. 이전에는 같은 시그니처가 매니저마다 다른 수명을 뜻했다. `ResourceManager`는 null을 씬 Scope로, `PoolManager`는 전용 앱 Scope로 해석했다.

예전 풀은 원본 이름(`original.name`)을 키로 써서 이름이 같은 원본을 구분하지 못했다. 소멸자(`~Pool`)에서도 풀을 정리했다.

### 씬은 Scope로 소유하고 테이블 키로 찾는다

씬마다 Scope를 두어 그 씬이 소유한 리소스만 해제한다. 앱 Scope가 함께 소유한 리소스는 남는다. 첫 `SceneBase` 구조에는 씬 단위 소유가 없었다. 전환 때 `ReleaseAllAsync()`로 `DontDestroy`가 아닌 리소스를 전부 해제했다. 정지, 풀 정리, 언로드의 순서도 정해져 있지 않았고, 실패하면 복구하지 않았다.

씬 경로는 씬 이름 대신 `SceneData` 테이블 키로 찾는다. Unity 6의 Addressables 씬 로딩 방식에 맞춘 것이다.

쓰지 않던 `OnEnter(float progress, ...)`와 `SubScenes`는 지웠다. `Stopped` 이벤트와 `BindScene`은 Actor 통합 때 지웠다.

### 통지를 id와 델리게이트로 바꿨다

통지는 id를 구독한 리스너만 직접 호출한다. 그래서 이름 오류가 컴파일 때 드러난다. 이전 통지는 `NotifyToEventHandler("메서드명", ...)`가 연결된 모든 핸들러에서 `GetMethod`로 메서드를 찾아 `Invoke`했다. 핸들러는 `ConnectHandler`와 `DisconnectHandler`를 직접 호출해야 했고, `SceneBase`에도 핸들러 목록과 전달용 `ConnectHandler` API가 있었다.

사용처가 없던 우선순위, `object[]` 인자, 수동 해제, 다음 프레임·지연 통지, `NotifyHandlerBehaviour`는 지웠다. 데미지 같은 콘텐츠 통지 id는 그 기능을 추가할 때 선언한다.

### 사운드 API를 줄였다

운영 코드가 쓰지 않던 사운드 API를 지웠다. 3D 핸들과 슬롯 세대 번호, `PlayBgm`·`PlayOneShot` 오버로드, BGM `restart` 옵션이 대상이다. 테스트 스텁으로만 재현되던 롤백 try/catch도 지웠다. `AudioSource`의 `Play`, `Stop`, `Pause`는 사용자 코드를 동기로 호출하지 않기 때문이다.

BGM·FX·공간 음원은 별도 매니저로 나누지 않았다. 재생, 페이드, 볼륨 함수가 이미 나뉘어 있다.

이전 `SoundManager`의 문제는 다음과 같았다.

- 채널들이 페이드 취소 토큰 하나를 공유했다.
- 모든 클립을 자체 `audioClips` 캐시에 앱 종료까지 보관했다.
- 페이드를 `Time.deltaTime`으로 진행해 `timeScale`이 0이면 끝나지 않았다.
- 볼륨·음소거, BGM 정지·조회, 3D 공간 음원, 종료 뒤 새 요청 차단이 없었다.

### UI 스택을 UIContainer로 옮겼다

`UIContainer`가 스택을 소유한다. 스택은 `Show()`, `Hide()`, 제거로만 바뀐다. 첫 ProjectT 판은 공개 필드 `List<UIBase> UIStack`에 호출자가 `UIManager.RegisterStackUI`로 직접 등록했다. 그 판은 같은 UI를 동시에 요청하면 따로 로드했고, 취소 토큰과 종료 확인도 없었다. 공개 API에는 `CreatWidgetAsync`, `FindWiget` 같은 오타가 있었다. 그 전의 Project J는 Scene, Popup, Element UI를 따로 관리했다.

### 패드 선택 대상 API를 UI에 남겼다

선택 대상 API는 `InputManager`로 옮기지 않았다. 무엇을 선택할지는 UI의 책임이고, EventSystem은 공유 인프라다. 입력 차단 소유권, 공통 뒤로 가기, 닫기 애니메이션 상태는 실제 사용 사례가 생길 때 추가한다.

### 테이블 생성기를 CLI로 옮겼다

생성기를 Unity 밖의 CLI로 옮기고 코드를 CodeDom으로 만든다. 결정과 이유는 다음과 같다.

- 생성기는 Editor 없이 돈다. CI는 종료 코드와 JSON 진단으로 결과를 판단한다.
- CodeDom을 쓰면 생성할 코드의 구조가 생성기 코드에 드러난다.
- PK는 컬럼 타입 그대로 키로 쓴다.
- `Insert`와 `Initialize`는 같은 Dictionary 코드지만 실패 정책과 교체 시점이 달라 공통 삽입 함수로 합치지 않았다.
- `GenerateInfoData`는 진입점과 private 함수 여섯 개로만 나눴다. 새 클래스나 생성 프레임워크는 추가하지 않았다.
- `StringDef` ID는 `LOCALNAME`의 해시로 정한다. 등장 순번으로 정하면 문자열 하나를 넣거나 지울 때 뒤의 ID가 모두 바뀐다.
- ILRepack 병합은 기본으로 끈다. 병합 산출물은 PE 리소스 섹션이 깨져 Unity가 로드하지 못할 수 있다. `--merge`일 때만 병합하고 `ValidateForUnity`로 산출물을 검사한다. Roslyn이 만든 `DataMgr.dll`과 `LocalData.dll`은 정상이다.

이전 구조의 문제는 다음과 같았다.

- Project J는 테이블을 바꿀 때마다 Unity Editor를 거쳤다.
- Project J는 클래스를 String Format으로 만들어 생성될 코드의 구조가 보이지 않았다.
- Project J는 다국어 문자열을 생성하지 않았다.
- Project T 초기 판은 `GenerateInfoData` 함수 하나가 행 클래스부터 참조 연결과 파일 출력까지 만들었다.
- PK를 `ArraySegment<byte>`로 직렬화해 키마다 바이트 배열과 비교자 `DataComparer`가 필요했다.
- 생성된 `Initialize`는 중복 PK 행을 건너뛰었다.
- `DataManager`는 쓰던 객체에 바로 읽어 넣고 콜백으로 결과를 알렸다. 그래서 중간에 실패하면 일부만 읽힌 데이터가 남았다.

### Global을 partial 두 파일로 나눴다

나눈 이유는 테스트다. 테스트가 실제 `Global`을 쓰지 못한 원인은 매니저를 `new`로 만드는 코드가 아니었다. 접근자가 매니저 구체 타입 9종과 `DesignTable.DataMgr`를 반환 타입으로 이름 붙이는 컴파일 의존이 원인이었다. 그래서 접근자와 등록 목록만 `Global.Managers.cs`로 옮겼다. 비교한 다른 두 방안은 채택하지 않았다.

- 등록 목록을 별도 static 클래스로 옮기는 방안은 파일만 늘고 접근자의 컴파일 의존이 그대로 남는다.
- internal 호스트 주입 API를 추가하는 방안은 컴파일 의존이 남고, 제품 코드에 테스트 전용 진입점이 생긴다.

### UnregisterInputContext를 남겼다

`ComBaseActor.UnregisterInputContext`는 지금 제품 코드에 호출하는 곳이 없지만 지우지 않았다. 탑승처럼 런타임에 입력 컨텍스트를 교체하는 파생 Actor가 쓰는 protected API이기 때문이다.

### asmdef를 두지 않는다

의도한 결정이다. `AbilitySystem`도 별도 asmdef를 두었다가, 별도 어셈블리로 둘 필요가 없다고 보고 지웠다. 그 대가로 코어의 비의존 규칙은 컴파일러 대신 코드 리뷰가 지킨다.

### 빌드 흐름에서 버린 방식

Project J는 Jenkins로 Unity 플레이어만 빌드했다. Addressables 빌드와 Firebase 업로드를 더한 첫 판은 업로드 람다가 `ContinueWith` Task를 반환해 `Task<Task>`가 되어 `WhenAll`이 업로드를 기다리지 않았고, `async void` 진입점이 종료 코드를 남기지 않았다. 타깃을 빠뜨리면 `NoTarget`으로 서버 루트에 업로드했고, iOS는 플레이어를 빌드하지 않았으며, `-BUILD_VERSION`이 업로드 경로에만 쓰였다. CI 진입점과 테스트 빌드 메뉴가 업로드 이후 흐름을 따로 가져 고칠 곳이 두 군데였다. 빌드 흐름은 다음 방식을 쓰지 않는다.

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
