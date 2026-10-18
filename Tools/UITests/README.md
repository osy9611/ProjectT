# ProjectT UI 리팩토링 1차

기존 UIManager → UIContainer → UIBase 구조를 유지한다. UIManager/SceneManager/ResourceManager에 이미 있던 로컬 변경 위에 적용했으며 ResourceManager와 SceneManager는 이번 작업에서 수정하지 않았다.

## 수명 계약

- CreateWidget/CreateWidgetAsync는 기존 인스턴스를 재사용한다. 생성 직후에는 비활성이며 호출자가 Show를 호출한다.
- 인스턴스는 타입 루트 아래에 생성한다. 프리팹 루트가 활성이면 Awake/OnEnable은 생성 시점에 한 번 실행된 뒤 비활성화된다.
- UI 경로가 정의되지 않은 타입은 null을 반환하지 않고 예외로 실패한다.
- UI 프리팹 루트는 활성 상태로 저장한다. 비활성 루트는 생성 전에 예외로 거절한다. Unity는 활성화된 적 없는 오브젝트에 OnDestroy를 호출하지 않으므로, 외부 Destroy에도 해제 경로가 실행되게 하기 위한 제약이다. 등록·스택 조회는 파괴된 항목을 정리하지 않는다.
- OnEnter는 최초 생성 시 한 번 실행한다. 여기서는 바인딩과 초기화를 수행하고 Show를 호출하지 않는다. 초기화가 성공한 다음 저장소에 등록한다.
- 동일 타입 비동기 요청은 UniTaskCompletionSource 결과를 공유한다. 일반 UniTask의 복수 await에 의존하지 않는다.
- 호출자 취소는 해당 대기만 취소한다. 모든 대기자가 취소해도 공유 생성은 완료되어 캐시될 수 있다. 이 상태에서 생성이 실패하면 전달받을 대기자가 없으므로 Forget 경계에서 Global.LogException으로 한 번 보고한다. 대기자가 남아 있으면 대기자에게만 전달한다.
- 비동기 생성 중 동기 CreateWidget 호출은 예외로 거절한다. 잘못된 제네릭 타입 요청도 기존 객체를 대체하지 않고 실패한다.
- RemoveWidget, 씬 전환 정리, 관리자 종료는 진행 중 생성 요청을 취소한다. 초기화 중 정리가 재진입해도 등록 전에 취소를 다시 확인한다.
- 씬 정리 시 아직 생성 중인 System UI 요청도 취소한다. 이미 등록된 System UI만 유지한다.
- System UI의 앱 Scope 소유는 초기화가 끝나 등록 직전에 추가한다. 생성이 실패하거나 취소되면 추가하지 않는다. 등록 후 제거해도 앱 Scope 참조는 앱 종료까지 유지된다.
- OnShow는 닫힌 UI를 열 때 실행한다. 이미 열린 UI에 Show를 호출하면 표시와 앞 순서만 갱신한다. 데이터 재조회가 필요하면 화면별 명시적 갱신 함수를 사용한다.
- OnHide는 열린 상태를 닫을 때 한 번 실행한다. OnLeave는 최종 제거 시 한 번 실행한다. 부분 초기화 실패에도 OnLeave가 호출되므로 획득한 자원만 반환하도록 작성한다.
- Show·Hide·제거는 GameObject 활성화, 스택, 이전 화면 복원, 포커스를 먼저 반영하고 OnShow/OnHide/OnLeave를 마지막에 실행한다. 콜백 예외는 롤백이나 집계 없이 원래 예외 그대로 전파하고 남은 콜백은 건너뛴다. 예를 들어 제거 중 OnHide가 실패하면 OnLeave는 실행되지 않는다.
- Show/Hide는 공통 계약을 보장하기 위해 비가상 메서드로 변경했다. OnEnter/OnLeave/OnShow/OnHide는 protected 훅이므로 외부에서 직접 호출하지 않는다. 사용자 구현은 OnShow/OnHide/OnEnter/OnLeave를 사용한다. 기존 프로젝트에 Show/Hide override 호출부는 없었다.
- 외부 Destroy 시 UIBase.OnDestroy가 UIContainer.ReleaseWidget을 호출해 등록과 스택을 정리하고, 현재 Static UI였다면 이전 화면을 복원한 뒤 OnHide/OnLeave를 실행한다. 파생 OnDestroy를 추가한다면 반드시 base.OnDestroy를 호출한다.

## 화면 이력

UIContainer만 스택을 변경하며 외부에는 수정 불가 뷰를 제공한다. UIManager의 RegisterStackUI/UnRegisterStackUI/ResetStackUI는 제거했다. 현재 프로젝트에 UIBase 이외의 호출부는 없었다.

Static UI를 열면 이전 Static UI는 논리적으로 열린 상태를 유지하면서 GameObject를 비활성화한다. 현재 UI를 Hide하거나 Remove하면 이전 UI를 복원하며 OnShow를 다시 실행하지 않는다. Hide(false)는 이전 화면을 복원하지 않고 현재 UI를 이력에서 제거한다. 씬 정리와 종료는 이전 화면을 복원하지 않는다.

표시 순서와 스택은 OnShow보다 먼저 반영한다. OnShow에서 다른 UI를 열면 나중에 연 UI가 앞에 남고, OnShow에서 이전 Static UI를 다시 열면 그 UI를 가리지 않는다. OnShow가 실패해도 UI는 열린 상태로 스택 최상단에 남고 포커스도 그 UI 기준으로 유지된다. 이후 Hide나 씬 정리로 닫는다.

Dynamic/System은 정적 화면 스택에 포함하지 않는다. 표시 시 같은 패널의 마지막 sibling으로 이동한다. 생성된 UI는 UIContainer가 소유하며 중간 초기화 객체를 FindWidget으로 노출하지 않는다.

## 범위

이번 단계는 동시 생성·롤백, 종료 처리, 스택 소유권과 기본 표시 순서에 집중했다. 입력 차단 소유권, 공통 뒤로 가기 입력, 닫기 애니메이션 상태, 표시 계층과 유지 수명 분리는 추가하지 않았다. 실제 사용 사례에 맞춰 후속 적용한다.

## 검증

프로젝트 루트에서 `dotnet run --project Tools/UITests` 실행. .NET 9 참조 팩과 Unity가 복원한 UniTask 패키지가 필요하다.

실제 UIManager/UIContainer/UIBase/UIDefine/ErrorCollector 소스와 UniTask를 컴파일한다. Unity 엔진 동작은 스텁으로 대체한다. 엔진, 리소스 로딩, ManagerBase는 테스트 경계로 대체한다. 기존 LifecycleTests는 UI를 스텁으로 검사하므로 UI 전용 실행기를 추가했다. 생산 코드에 새 매니저나 공용 계층은 추가하지 않았다.

26개 회귀 시나리오: 동시 생성 공유, 독립 취소, 씬 전환 취소·재시도, 관리자 수명 취소, 초기화 롤백, 오류 보존, 타입 검증, 동기/비동기 충돌, 중복 숨김·제거, Static 복원, 팝업 재표시, System 유지, 종료 오류 집계, 초기화 중 정리 재진입, 표시 실패, 현재 화면 제거, 생성 중 개별 제거, 외부 파괴, 동기 초기화 중 비동기 대기, 읽기 전용 스택, 대기자 없는 실패 보고, 대기자 있는 실패의 중복 보고 방지, 대기자 없는 취소 미보고, OnShow 중 Static 재진입, OnShow 중 이전 화면 재표시, UIManager 연결.

Unity Bee 응답 파일과 실제 Unity 참조를 사용한 런타임 C# 컴파일도 수행했다. 이 검증은 Unity Editor 임포트나 Play Mode 실행을 대체하지 않는다. Awake/OnEnable 순서, 지연 Destroy, 실제 Addressables/Canvas 동작은 Unity에서 확인해야 한다.

기존 LifecycleTests에는 `root: host parent is applied by ManagerBase without context` 실패 1건이 있다. 해당 테스트는 ManagerHost 생성자의 부모를 기대하지만 현재 ManagerBase.CreateRootObject는 Global.Instance.transform을 사용한다. 이 실행기는 UI 생산 코드를 사용하지 않으며 이번 UI 변경과 별개다. 기존 계약을 임의 변경하지 않았다.

## 입력 차단과 후속 회귀 검증

UIContainer.SetInputAllowed는 Canvas2D CanvasGroup의 interactable과 blocksRaycasts를 갱신한다. 사용처가 없던 UIEventHandler와 UIBase.BindEvent는 삭제했다.

기존 26개 테스트에 외부 파괴 복원, 복원 전 정리 순서, 중첩 OnShow 실패, 정리/복원 복수 오류 보존 등의 7개를 추가했다. 구체적인 최신 개수는 실행 출력에서 확인한다.

실제 Unity EventSystem, 지연 Destroy와 OnEnable/OnDisable 콜백 타이밍은 Play Mode에서 추가로 확인해야 한다.

## 2차 리팩토링: 필수 바인딩

Bind는 필수 참조 바인딩이다. enum은 0부터 연속한 고유 값이어야 하며, 문자열 이름은 비어 있으면 안 된다. 같은 컴포넌트 타입의 바인딩 그룹은 한 번만 등록한다. 루트 이름도 탐색 대상이며, 필수 객체/컴포넌트 누락은 초기화 시 예외로 보고한다. 모든 참조가 확인된 후 등록하므로 실패한 바인딩은 재시도할 수 있다. Get<T>(index)는 그룹 미등록과 범위 오류를 설명하는 예외를 제공한다. 기존 Get<T>() 배열 조회의 미등록 시 null 반환은 유지한다. 선택적 참조가 필요해지면 필수 Bind에 누락을 허용하지 말고 해당 화면에서 별도 처리한다.

화면 전환 로직은 UIBase의 OnShow/OnHide 훅에 둔다. Unity OnEnable/OnDisable에서 Show/Hide/Remove를 재진입시키는 사용은 현재 지원 계약에 포함하지 않는다. 해당 콜백에서 직접 화면을 전환해야 한다면 별도의 전환 상태 설계와 엔진 회귀 검증이 필요하다.

`dotnet run --project Tools/UITests`는 현재 수명·스택 33개 시나리오와 바인딩 검증 묶음을 실행한다. 바인딩 검증은 enum 오류, 필수 참조, 중복 그룹, 범위 오류, 실패 후 재시도와 생성 롤백을 확인한다.

## Unity Play Mode smoke 실행

Unity를 종료한 뒤 프로젝트 루트에서 `./Tools/UITests/run-unity.ps1`을 실행한다. 필요하면 `-UnityPath`로 Editor 실행 파일을 지정한다. 이 실행기는 Tools 아래 검증 소스를 임시 Assets 폴더에 복사하고 빈 씬에서 실행한 후 임시 폴더를 정리한다. 기존 임시 경로나 실행 중 Editor가 있으면 중단한다. 테스트 씬을 저장하지 않는다.

유효한 Editor 라이선스, 복원된 프로젝트 패키지와 Unity 사용자 캐시 접근 권한이 필요하다. 로그는 `Tools/UITests/bin/unity-playmode.log`에 남긴다. 180초 제한과 종료 코드, 성공 마커를 모두 검사한다. 검사 전에 실제 Global을 원격 리소스·데이터 로드 없이 띄우고 WhenReadyAsync로 준비를 기다린다. UIContainer가 포커스 변경을 Global.Input으로 알리기 때문이며, 초기화가 실패하면 그 예외를 남기고 실패로 끝난다. batch 실행은 종료 코드로 결과를 넘기고, 열린 에디터에서 실행하면 에디터를 닫지 않고 Play Mode만 끝낸다. 2026-10-16 열린 에디터(포커스 있음)에서 `PROJECTT_UI_PLAYMODE_PASS`를 확인했다. 검증 대상은 지연 Destroy 후 이전 화면 복원, OnShow 중 중첩 표시 실패, 실제 활성화 콜백이다. OnScreenControl sender 검사는 UIEventHandler와 함께 삭제했다. EventSystem의 자동 포인터 라우팅과 Addressables 로딩은 이 smoke 테스트 범위에 포함하지 않는다.

2026-09-25 검증에서는 실제 Unity 참조 C# 컴파일과 스텁 회귀 실행이 성공했다. Editor 실행은 라이선스 오류(종료 코드 198)로 Play Mode 진입 전에 중단됐으므로 엔진 테스트 통과를 의미하지 않는다. 라이선스가 준비된 환경에서 위 명령을 다시 실행해야 한다.
