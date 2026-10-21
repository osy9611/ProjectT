# NotificationTests 실행

프로젝트 루트에서 `dotnet run --project Tools/NotificationTests`를 실행한다. .NET 9 SDK와, Unity가 `Client/Library/PackageCache`에 복원한 UniTask 패키지가 필요하다. 출력 마지막 줄 `<통과 수>/<전체 수> notification tests passed`에서 두 수가 같으면 통과다. 전체 기준선과 수치는 [architecture.md의 테스트 절](../../Docs/architecture.md#테스트)에 있다.

## 무엇을 검사하나

러너는 다음 제품 소스를 그대로 컴파일한다.

- `Managers/NotificationManager.cs`와 `Event/NotificationId.cs`.
- 실제 `Global.cs`, `ManagerHost.cs`, `ManagerBase.cs`, `ErrorCollector.cs`, `ErrorReport/*.cs`(`UseRealGlobal`).
- 설치된 UniTask 소스 중 엔진에 의존하지 않는 파일.

UnityEngine 타입은 `Tools/Shared/UnityStubs`가, UniTask의 PlayerLoop 관련 항목은 `Stubs.cs`가 대체한다. 취소 토큰과 작업 스레드는 실제 .NET 구현을 쓴다.

검사 범위는 다음과 같다.

- 생명주기에 따른 새 작업 허용과 종료 시 구독 정리.
- 등록 순서대로 호출, 중복 구독, 소유자 취소 뒤 재구독.
- 발행 중 목록 변경에 대한 스냅샷 처리.
- 발행 중 소유자 취소, 작업 스레드 취소, 매니저 수명 취소, 종료.
- 재귀 발행, 리스너 예외 전파, 인자 검증, 식별자 격리.

공개 API는 Unity 메인 스레드에서 쓴다. 소유자 토큰은 다른 스레드에서 취소될 수 있다. 매니저는 취소 콜백을 등록하지 않으므로, 취소된 구독은 즉시 수신을 멈추고 같은 id의 다음 `Subscribe`나 `Publish` 때 목록에서 빠진다.

## 검증하지 않는 것

- 작업 스레드 취소 시나리오는 Mono와 IL2CPP에서 가능한 모든 스레드 교차 순서를 증명하지 않는다.
- Unity 어셈블리 컴파일과 실제 MonoBehaviour 수신자의 해제 시점은 Unity에서 확인한다.

## 변경 전후 비교

변경 전 `NotificationManager.cs` 사본으로 한 번, 현재 소스로 한 번 실행해 출력을 비교한다.

```powershell
dotnet run --project Tools/NotificationTests -p:NotificationManagerPath='<변경 전 NotificationManager.cs 경로>'
dotnet run --project Tools/NotificationTests
```

## 관련 문서

- [architecture.md의 Notification](../../Docs/architecture.md#notification)
- [architecture.md의 진입점과 매니저 생명주기](../../Docs/architecture.md#진입점과-매니저-생명주기)
