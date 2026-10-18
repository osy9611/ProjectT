> 러너는 실패가 있어도 전체 시나리오를 실행한 뒤 FAIL 목록과 실패 개수를 출력한다. 모듈별 리팩토링 중 임시로 제외했던 보류 묶음(PendingSuites.cs)은 모두 정리했다.

> 2026-09-30 Controller 입력 컨텍스트 단순화: InputContext.Bind와 우선순위·소비에 따른 콜백 전달로 변경. 입력 22개, Actor 26개, 기타 108개로 총 156/156 통과. 이전 물리 키 차단 계약의 입력 테스트 38개를 새 계약으로 교체했다. 직접·독립 리뷰에서 확인한 입력 상태 정리 문제까지 보완했고 Unity runtime/editor 참조 컴파일도 통과했다. 사용 예시·변경 이유·검증 한계는 [입력 컨텍스트 보고서](../../reports/tests/LifecycleTests/ControllerContextReport.md)를 참고한다. 아래 기록의 수치는 당시 기준이다.

> 2026-09-29 Actor 중심 Controller 연동: Actor 신규 26개와 기존 136개, 총 162/162 통과. Controller 소유권·사용 예시·실패/재진입 검토와 Unity 검증 한계는 [Actor Controller 보고서](../../reports/tests/LifecycleTests/ActorControllerReport.md)를 참고한다.

> 2026-09-27 Controller 우선순위·소비 기능: 신규 입력 28개와 기존 수명 108개, 총 136/136 통과. Unity runtime/editor 참조 컴파일과 독립·최종 리뷰 완료. 실제 입력 Play Mode는 미검증이다. API 예시·수명 정책·변경 이유·검토 피드백은 [입력 우선순위 보고서](../../reports/tests/LifecycleTests/ControllerPriorityReport.md)를 참고한다. 아래의 이전 테스트 개수는 당시 기록이다.

> UI 수명 정책은 [UILifetime.md](../../reports/tests/LifecycleTests/UILifetime.md)를 참고한다. 씬별 UI 소유권 설계는 폐기했고, UI 분류에 따라 앱 유지와 전환 단위 제거로 나눈다. 사운드는 다음 단계에서 정리한다.

풀링 변경과 수명 계약: [Pooling.md](../../reports/tests/LifecycleTests/Pooling.md)

> 최신 변경 전후 분석과 검증 범위는 [모듈 리팩토링 통합 보고서](../../reports/tests/LifecycleTests/RefactoringReport.md)를 기준으로 한다. 아래의 과거 테스트 개수와 단계별 기록은 당시 기준이다.

최신 리소스 직접 생성 구조: 80개 테스트. ResourceConvenience.md 참고.

# 현재 씬·리소스 검증

총 74개 테스트를 실행한다. 최신 계약은 [SceneManager.md](../../reports/tests/LifecycleTests/SceneManager.md)와 [ResourceConvenience.md](../../reports/tests/LifecycleTests/ResourceConvenience.md)를 참고한다. 아래 수치는 이전 단계 기록이다.

# ManagerContext 제거 이후 계약

현재 총 59개 시나리오를 실행한다. Global도 스텁 대신 실제 생산 코드(Global.cs)를 컴파일하며, 매니저 접근자는 GlobalAccessors.cs가, 테스트 진입점은 Tools/Shared/GlobalTestSupport.cs가 partial로 제공한다. Unity 및 테스트 대상 밖의 매니저는 대체한다.

- Global.Resource 등 개별 속성과 GetManager<T>()는 초기화·종료 중에도 해당 매니저가 Ready면 반환한다. 미등록 또는 Ready가 아닌 매니저 조회는 예외로 차단한다. 종료 토큰 취소 후 새 리소스 로드·풀 대여는 시작하지 않는다.
- IsReady, WhenReadyAsync, TryGetReady<T>()는 전체 매니저 준비 완료 기준을 유지한다. 업데이트도 전체 준비 완료 후 시작한다.
- 종료에 필요한 의존성은 Global에서 직접 조회한다. 역순 종료 중 대상 매니저가 아직 Ready일 때만 접근 가능하며 새 리소스 로드·풀 대여는 취소 토큰으로 차단한다.
- ManagerHost 생성자는 인자를 받지 않는다. ManagerBase는 CreateRootObject(name)에서 Global.Instance.transform을 부모로 지정하고 생성한 루트를 보관한다.
- LoadData는 Global이 DataManager 생성자에 전달한다. 생성자의 기본값은 false다.

아래는 이전 단계의 검증 기록이다.

# Manager 생명주기 검증

SceneManager 리팩토링 이후에는 기존 생명주기/Pool 14개, 리소스 14개, 씬 관련 18개를 포함해 총 46개를 실행했던 단계에서 모바일 혼합 리소스 검증 9개를 추가해 현재 총 55개를 실행한다. 현재 정책은 [SceneResourcePolicy.md](../../reports/tests/LifecycleTests/SceneResourcePolicy.md)를 참고한다. 최신 씬 사용 계약과 검증 한계는 [SceneManager.md](../../reports/tests/LifecycleTests/SceneManager.md)를 참고한다. 아래의 이전 검증 수치는 각 단계의 기록이다.

ResourceManager 검증 14개가 추가되어 총 28개 시나리오를 실행한다. 리소스 사용 계약과 API 변경은 [ResourceManager.md](../../reports/tests/LifecycleTests/ResourceManager.md)를 참고한다. 아래 14개 목록은 기존 생명주기/Pool 검증 목록이다.

프로젝트 루트에서 PowerShell로 실행한다.

```powershell
dotnet run --project Tools/LifecycleTests
```

.NET SDK, .NET 9 참조 팩·런타임, Unity가 복원한 UniTask PackageCache가 필요하다. NuGet 다운로드 없이 프로젝트의 ManagerBase·ManagerHost와 설치된 UniTask의 엔진 비의존 소스를 직접 컴파일한다. Unity 경계만 대체하므로 실제 PlayerLoop·GameObject 파괴 시점·Addressables 동작을 검증하지는 않는다.

검증하는 14개 시나리오:

1. 필수 준비 완료까지 Ready와 업데이트 차단, 복수 대기.
2. 의존성 유지와 역순 종료, 같은 표시 이름의 타입 구분.
3. 부분 초기화 실패 복구와 원인 보존.
4. 종료 예외가 발생해도 다른 관리자 및 루트 정리.
5. 초기화 중 종료 후 늦게 완료되어도 Ready로 돌아가지 않음.
6. 취소 콜백이 즉시 실행되어도 정상 종료 유지.
7. 취소 콜백 예외가 정리를 막지 않음.
8. 중복 등록 및 시작 이후 등록 차단.
9. 준비되지 않은 의존성 접근 시 실패·복구.
10. 초기화 전 종료 시 대기 완료 및 초기화 방지.
11. 개별 대기자의 취소가 공유 Ready를 취소하지 않음.
12. 등록 시 동일 참조 반환 및 미등록 타입 조회 처리.
13. 업데이트 중 종료되면 나머지 관리자 업데이트 중단.
14. Pool의 0·1·4·127개 객체 전부 반환, 중복 반환 방지 및 재사용.

## 사용 계약

- Global이 깨어난 이후 `await Global.Instance.WhenReadyAsync(token)`으로 준비를 기다린다. Global 생성 이전에도 동작할 수 있는 컴포넌트는 먼저 인스턴스 생성까지 기다려야 한다. MonoBehaviour 알림 수신자는 `Global.Notify.Subscribe(id, listener, token)`에 `GetCancellationTokenOnDestroy()` 같은 소유 토큰을 넘기고, 토큰이 취소되면 자동 해제된다. SceneBase에서는 기존 LifetimeToken을 직접 전달한다.
- `Global.Resource` 등은 대상 매니저가 Ready이면 초기화·종료 중에도 조회할 수 있다. 전체 준비 완료는 `WhenReadyAsync`로 기다리며, `Global.TryGetReady<T>(out manager)`는 전체 Host가 Ready인 경우에만 성공한다.
- 초기화 내부의 의존성도 `Global`을 사용한다. 등록 순서상 먼저 초기화한 관리자만 가져올 수 있다.
- Global로 접근 가능한 매니저는 사용 지점에서 Global로 직접 조회하고 필드에 캐싱하지 않는다. 정적 속성은 GetManager<T>()를 통해 대상 매니저의 Ready 상태를 확인한다.
- Host는 타입 조회용 Dictionary, 실행 순서용 리스트와 startedCount를 사용한다. 범용 타입 조회는 Dictionary의 TryGetValue로 처리한다.
- Pool.ReturnAll은 활성 리스트를 역순으로 반환한다. OnReturn 콜백도 해당 순서로 호출된다.
- 관리자별 구현은 `OnInitializeAsync(CancellationToken)`과 `OnShutdown(ShutdownReason)`으로 옮겼다. 초기화는 UniTask를 반환한다.
- Ready는 UniTaskCompletionSource를 사용해 공유한다. 일반 비동기 UniTask를 필드에 저장해 반복 await하는 방식은 사용하지 않는다.
- LoadData=false의 Ready는 테이블 로딩 완료를 의미하지 않는다.
- 관리자는 메인 스레드에서 사용한다. 종료 요청 시 생명주기 토큰이 취소되며, 각 비동기 구현도 취소 확인과 자신이 보유한 자원 반환을 수행해야 한다.
- 로컬 저장소 SaveDataAsync는 다음 PlayerLoop에서 동기 저장을 수행한다. 종료 저장과 백그라운드 쓰기가 충돌하는 것을 방지하기 위한 변경이며, 대용량 비동기 저장 설계는 별도 범위다.

## 검증 범위와 후속 확인

단순화 적용 후 14개 시나리오 통과. 기존 Unity Bee 응답 파일의 참조와 ManagerHost 소스를 이용한 런타임·에디터 C# 컴파일 통과. 이는 Unity의 새 임포트·플레이어 빌드 성공을 의미하지 않는다. .NET 테스트에서 UniTask 패키지 자체의 사용 중단 API 경고 4개가 발생한다.

Unity에서는 중복 Global이 포함된 씬 로드, Play 재진입, Domain/Scene Reload 설정 조합, LoadData 설정 두 가지, 로딩 도중 종료, 풀에서 대여한 객체와 UI 루트의 실제 제거를 확인해야 한다. Addressables 씬 언로드는 비동기이므로 Shutdown 반환만으로 언로드 완료를 의미하지 않는다. 사용자 정의 SceneBase 비동기 구현의 내부 작업도 후속 Scene 리팩토링에서 검증해야 한다.

GC·메모리 개선 수치는 아직 측정하지 않았다. UniTaskCompletionSource 등 공유 상태를 위한 할당은 남아 있다.

## 예외 처리 경계

- await 가능한 API는 실패를 호출자에게 전달한다. 중간 계층에서 로그만 남기고 성공으로 바꾸지 않는다.
- 기다리는 호출자가 없는 실행은 Forget(Global.LogException)으로 최종 보고한다. Unity 동기 이벤트에서 전파된 예외는 Unity가 보고한다.
- 씬 진행률 콜백 실패는 로드·언로드 호출자에게 전달한다. 로드 핸들은 finally에서 반환하거나 완료 후 언로드하며, 언로드 핸들도 반환한다.
- 씬 전환 실패와 롤백 실패가 함께 발생하면 AggregateException으로 묶어 전달한다. 종료 취소 중에는 새 Scope를 만들어 복구하지 않는다.
- SceneBase 종료, 매니저 역순 종료, 풀·리소스 일괄 해제는 남은 정리를 수행한 뒤 집계 오류를 상위로 전달한다. 이 지점의 catch는 오류를 숨기기 위한 처리가 아니다.
- Resource의 공유 대기 완료 처리는 TrySetException 또는 실패 상태를 통해 모든 대기자에게 오류를 전달하므로 유지한다.

기존 전체 테스트는 제거된 UIManager.Scene.cs/SoundManager.Scene.cs를 참조한다. 이번 예외 정책 검증은 해당 UI·사운드 전용 시나리오를 제외한 격리 실행으로 수행했으며 전체 테스트 통과를 의미하지 않는다.
