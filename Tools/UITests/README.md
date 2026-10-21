# UITests 실행

프로젝트 루트에서 `dotnet run --project Tools/UITests`를 실행한다. .NET 9 SDK와, Unity가 `Client/Library/PackageCache`에 복원한 UniTask 패키지가 필요하다. 출력 마지막 줄 `<통과 수>/<전체 수> passed`에서 두 수가 같으면 통과다. 전체 기준선과 수치는 [architecture.md의 테스트 절](../../Docs/architecture.md#테스트)에 있다.

## 무엇을 검사하나

러너는 다음 제품 소스를 그대로 컴파일한다.

- `UGUI/UI/UIContainer.cs`, `UGUI/UI/UIBase.cs`, `UGUI/UI/UIDefine.cs`.
- `Managers/UIManager.cs`, `Common/ErrorCollector.cs`.
- 설치된 UniTask 소스 중 엔진에 의존하지 않는 파일.

UnityEngine 타입(Canvas, Camera, EventSystem 등)은 `Tools/Shared/UnityStubs`와 `Stubs.cs`가 대체한다. `ManagerBase`, `Global`, 리소스 로딩, 풀, 입력, HUD 타입도 `Stubs.cs`의 가짜가 대체한다.

검사 범위는 다음과 같다.

- 동시 생성 결과 공유, 호출자별 취소, 씬 정리와 매니저 수명에 따른 생성 취소와 재시도.
- 초기화 실패 롤백, 잘못된 제네릭 타입, 비활성 프리팹 루트 거절, 동기와 비동기 생성 충돌.
- Show, Hide, 제거의 콜백 횟수와 순서, Static 화면 복원, 팝업 재표시, System UI 유지.
- 종료와 씬 정리 중 오류 처리, 초기화 중 정리 재진입, 외부 파괴.
- 대기자가 없는 실패와 취소의 보고 여부.
- `OnShow` 중 다른 UI 표시, 읽기 전용 스택.
- 필수 바인딩(`BindingTests.cs`)의 enum 검증, 필수 참조, 중복 그룹, 범위 오류, 실패 뒤 재시도와 생성 롤백.

## 검증하지 않는 것

이 러너는 Unity Editor 임포트나 Play Mode 실행을 대체하지 않는다. 다음 항목은 Unity에서 확인한다.

- Awake와 OnEnable 순서, 지연 Destroy, OnEnable과 OnDisable 콜백 시점.
- 실제 Addressables 로딩, Canvas, EventSystem의 포인터 라우팅.

지연 Destroy 후 이전 화면 복원, `OnShow` 중 중첩 표시 실패, 실제 활성화 콜백은 Unity Test Runner EditMode 탭의 `ProjectTUIPlayModeTests.UIContainerStackAndNativeActivation`이 확인한다. 이 테스트는 빈 씬에 원격 리소스와 데이터 로드를 끈 `Global`을 띄우고 `ProjectTUIPlayProbe.Verify()`를 끝까지 실행한다. 실행 방법은 [PlayModeTests 실행](../PlayModeTests/README.md)에 있다.

## 관련 문서

- UI 수명 계약과 스택: [architecture.md의 UI](../../Docs/architecture.md#ui)
- 예외 처리 경계: [architecture.md의 오류 처리](../../Docs/architecture.md#오류-처리)
