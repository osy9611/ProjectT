# Sound 회귀 테스트

최신 결과는 **52/52 통과**다. 테스트의 리플렉션 필드명을 현재 `spatialSounds`로 보정하고, FX/UI 페이드를 실제 로드에 사용한 SceneScope의 취소 토큰에 연결했다. 씬 종료·페이드 교체·BGM 유지 회귀 4개를 추가했으며 수정 전 코드는 50/52였다. 주 에이전트 최종 리뷰와 검증 한계는 [씬 페이드 수명 보고서](../../reports/tests/SoundTests/SceneFadeLifetimeReport.md)를 참고한다.

이전 단순화 결과는 **48/48 통과**였다. Unity에서 발생하지 않는 AudioSource 실패·재진입 주입 테스트 10개를 제거하고, 채널별 클립 Scope와 페이드 중 클립 해제 테스트 2개를 추가했다. 변경 이유와 동작 변경은 [단순화 보고서](../../reports/tests/SoundTests/SimplificationReport.md)를 참고한다. 아래 기록 중 실패 주입 관련 내용은 당시 기준이다.

이전 품질 검토 결과는 **56/56 통과**였다. 수동 보유 정지 실패, 소스 초기화 실패, 완료 일괄 정리의 실패 경계 5개를 추가했다. 수정 전 실패 재현과 최종 직접 리뷰는 [2차 품질 보고서](../../reports/tests/SoundTests/SoundQualityReport.md)를 참고한다.

제어 핸들·자동 회수 도입 시에는 **51/51 통과**했다. 기존 36개 시나리오를 유지하고 개별 제어, 수동 보유, 핸들 무효화, 취소, 실패 롤백, 지연 파괴를 포함한 15개 시나리오를 추가했다. API·변경 이유·주 에이전트의 리뷰와 재작업 기록은 [제어 재생 보고서](../../reports/tests/SoundTests/ControlledPlaybackReport.md), 사전 설계는 [제안서](../../reports/tests/SoundTests/ControlledPlaybackProposal.md)를 참고한다.

프로젝트 루트에서 `dotnet run --project Tools/SoundTests`를 실행한다. .NET 9 참조 팩과 Unity가 복원한 UniTask 패키지가 필요하다.

이 실행기는 실제 `SoundManager`, `PoolManager`, `GameObjectPool`, `Pool<T>`, `PooledObject`, `IPoolable`, `ManagerBase`, `ResourceScope`, `ErrorCollector`, `UIClickSound` 소스와 설치된 UniTask 소스를 C# 9로 함께 컴파일한다. Unity 엔진 객체와 리소스 로더만 최소 스텁으로 대체한다. `UniTask.Yield`는 수동 프레임 스케줄러에 연결되어 페이드의 프레임별 상태와 취소 순서를 결정적으로 검사한다.

검증 범위는 채널별 fade 교체와 독립성, 최신 재생 요청 우선, master/category 볼륨과 mute 유지, unscaled time, Clear의 재생·캐시·scope 정리, 잘못된 요청의 무해성, 종료 뒤 새 작업 차단, 동일 BGM 재시작 정책, UI 이벤트 재등록과 비활성화 정리다.

스텁 테스트는 Unity의 실제 오디오 출력, Addressables 제공자, 직렬화, UnityEvent의 엔진 내부 호출 순서를 대체하지 않는다. 실제 Unity 어셈블리 컴파일과 Play Mode 확인은 별도 검증한다.

이전 완료 검사 주기 변경 시점의 기준은 36개 시나리오다. R3 당시 슬롯 정리 시점·반복 반환, 활성화 전 실패, 재생/초기화/반환 복수 오류 순서를 추가하여 원본·수정본 각각 31/31을 통과했다. 이후 완료 검사 주기를 0.1초로 변경하면서 주기·포화 시 즉시 반환·일시정지·누적 시간 초기화·긴 프레임 검증 5개를 추가하고 기존 시간 의존 테스트 2개를 조정했다. 당시 36/36 및 Unity runtime/editor 참조 컴파일을 통과했다. 실제 Play Mode는 미검증이며 [주기 변경 보고서](../../reports/tests/SoundTests/SpatialPollingReport.md), 과거 기록은 [R3 보고서](../../reports/tests/SoundTests/SpatialSlotReport.md)를 참고한다. 3D 검증에는 실제 `GameObjectPool`의 프리웜과 `PooledObject.Owner`, 동일 AudioSource 재사용, 재생 중인 음원으로 포화 시 로드·생성 전 거절, 위치·거리·pitch 재설정, 로딩·일시정지·생성 프레임의 반환 보류, 클립 손실 시 반환, Master/FX 음량 적용, Clear 뒤 풀 재사용을 포함한다. 전용 사운드 scope 해제가 다른 풀을 보존하는지, 수명 토큰 취소 뒤 Return은 허용하고 새 Get은 거절하는지, 외부 `PoolManager.Clear` 뒤 재생이 복구되는지, 종료 시 풀 복제본이 사운드 템플릿보다 먼저 파괴되는지도 확인한다. 재생 실패 롤백과 IPoolable 반환 콜백 실패 후 나머지 자원 정리·오류 보존도 검증한다. 동시 재생 한도는 SoundManager가 적용한다.

각 fixture는 공유 수명 토큰을 취소한 뒤 Sound → Pool → Resource 순서로 종료한다. 리소스 스텁도 실제 `ResourceScope`의 취소 등록을 사용하며 유효 scope와 취소된 scope를 구분한다. 기본 스텁은 GameObject와 자식 컴포넌트를 즉시 destroyed 상태로 표시한다. 지연 파괴 전용 시나리오에서만 Destroy 요청을 큐에 보관했다가 flush하여 Owner 해제와 파괴 사이의 핸들 보호를 확인한다. 실제 Unity 파괴 프레임과 콜백 순서는 Play Mode에서 별도 확인해야 한다. 재생 중 GameObject 생성·파괴와 비동기 프레임 작업 유무는 스텁으로 확인하지만, 관리 힙 GC 할당량은 측정하지 않는다.

Unity 어셈블리 컴파일은 `./Tools/SoundTests/compile-unity.ps1`로 검사한다. 최신 구조는 [공용 풀 통합 보고서](../../reports/tests/SoundTests/PoolIntegrationReport.md), API 사용 예시는 [리팩토링 보고서](../../reports/tests/SoundTests/RefactoringReport.md)의 3D 추가 절을 참고한다.
