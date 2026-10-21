# LifecycleTests 실행

프로젝트 루트에서 `dotnet run --project Tools/LifecycleTests`를 실행한다. .NET 9 SDK와, Unity가 `Client/Library/PackageCache`에 복원한 UniTask 패키지가 필요하다. 패키지가 없으면 빌드가 "Open the Unity project once" 오류로 멈춘다. 출력 마지막 줄이 `ALL PASSED`이면 통과이고, 통과 수는 `PASS`로 시작하는 줄의 수다. 실패가 있어도 러너는 모든 시나리오를 실행하고 `FAIL` 줄과 마지막 줄의 실패 수를 출력한다. 전체 기준선과 수치는 [testing.md](../../Docs/testing.md)에 있다.

## 무엇을 검사하나

러너는 다음 제품 소스를 `Client/Assets/Script`에서 그대로 컴파일한다.

- `Common/Global.cs`, `Managers/Base/ManagerHost.cs`, `Managers/Base/ManagerBase.cs`, `Common/ErrorCollector.cs`, `Common/ErrorReport/*.cs`. 매니저 등록과 테스트 진입점은 `Tools/Shared/GlobalTestSupport.cs`가, 매니저 접근자는 `GlobalAccessors.cs`가 같은 partial 클래스로 제공한다.
- `InputManager`, `NotificationManager`, `PoolManager`, `ResourceManager`, `SceneManager`.
- `Actor/ComBaseActor.cs`, `Input/Controller.cs`, `Input/InputContext.cs`, `Scene/SceneBase.cs`, `Event/NotificationId.cs`.
- `Pool/`의 `GameObjectPool`, `IPoolable`, `Pool`, `PooledObject`와 `Addressable/`의 `Resource`, `ResourceBase`, `ResourceScope`, `SpriteAtlasResource`.
- `AbilitySystem/` 전체와 `SkillAdapter/`의 `SkillDefinitions`, `Action_*`.
- 설치된 UniTask 소스 중 PlayerLoop와 UnityEngine에 의존하지 않는 파일.

다음 경계는 테스트 쪽 대체물로 바꾼다.

- UnityEngine 타입과 Input System 장치는 `Tools/Shared/UnityStubs`와 `UnityStubs.cs`가 대체한다.
- Addressables, Unity 씬 로드, `Resources`는 `ResourceStubs.cs`가 대체한다. `ResourceManager`, Scope, UniTask는 제품 코드 그대로다.
- 테스트 대상이 아닌 `DataManager`, `UIManager`, `SoundManager`, 테이블, `UIBase`는 `SceneStubs.cs`와 `PoolConsumerStubs.cs`가 대체한다.

검사 범위는 다음과 같다.

- `Global` 조회, 매니저 초기화와 Ready 대기, 역순 종료, 초기화 실패 복구.
- `Pool`과 `PoolManager`의 대여, 반납, Scope별 정리.
- `ResourceManager`의 로드, Scope 해제, 스프라이트, preload.
- `SceneManager` 전환과 언로드, `SceneBase` 계약.
- `Controller`, `InputContext`, `InputManager`의 우선순위와 소비.
- `ComBaseActor`의 스폰, 반납, 실패 격리, 입력 컨텍스트, `SkillAgent`와 `Status` 연결(`actor skill:` 테스트).
- `ErrorReporter`와 `FileErrorSink`.

## 검증하지 않는 것

이 러너는 Unity 경계를 대체하므로 실제 PlayerLoop, GameObject 파괴 시점, Addressables 동작을 확인하지 않는다. 다음 항목은 Unity에서 따로 확인한다.

- 중복 `Global`이 들어 있는 씬 로드와 Play 재진입.
- Domain Reload와 Scene Reload 설정 조합, `LoadData`의 두 설정.
- 로딩 도중 종료.
- 풀에서 대여한 객체와 UI 루트의 실제 제거.
- Addressables 씬 언로드 완료. 언로드는 비동기이므로 `Shutdown`이 반환되어도 언로드가 끝났다는 뜻이 아니다.

GC 할당량은 측정하지 않는다. Play Mode 경로는 [PlayModeTests 실행](../PlayModeTests/README.md)의 Unity Test Runner 테스트가 다룬다.

## 관련 문서

- 매니저 사용 계약(Global 조회, Ready, 종료 순서): [architecture.md의 진입점과 매니저 생명주기](../../Docs/architecture.md#진입점과-매니저-생명주기)
- 예외 처리 경계: [architecture.md의 오류 처리](../../Docs/architecture.md#오류-처리), [error-reporting.md](../../Docs/error-reporting.md)
- Actor 생명주기와 입력 컨텍스트: [actor-lifecycle.md](../../Docs/actor-lifecycle.md)
- 스킬 시스템 연결: [ability-system.md](../../Docs/ability-system.md)
