# SoundTests 실행

프로젝트 루트에서 `dotnet run --project Tools/SoundTests`를 실행한다. .NET 9 SDK와, Unity가 `Client/Library/PackageCache`에 복원한 UniTask 패키지가 필요하다. 출력 마지막 줄 `<통과 수>/<전체 수> sound tests passed`에서 두 수가 같으면 통과다. 전체 기준선과 수치는 [testing.md](../../Docs/testing.md)에 있다.

## 무엇을 검사하나

러너는 다음 제품 소스를 C# 9로 함께 컴파일한다.

- `Managers/SoundManager.cs`, `Managers/PoolManager.cs`, `Managers/Base/ManagerBase.cs`.
- `Pool/`의 `GameObjectPool`, `Pool`, `PooledObject`, `IPoolable`.
- `Addressable/ResourceScope.cs`, `Common/ErrorCollector.cs`, `Sound/UIClickSound.cs`.
- 설치된 UniTask 소스 중 엔진에 의존하지 않는 파일.

다음 경계는 테스트 쪽 대체물로 바꾼다.

- UnityEngine 타입(`AudioSource`, `AudioClip`, `AudioListener`, UI 이벤트)은 `Tools/Shared/UnityStubs`와 `Stubs.cs`가 대체한다.
- 리소스 로더와 `Global`은 `Stubs.cs`의 가짜가 대체한다. 리소스 가짜도 실제 `ResourceScope`의 취소 등록을 쓴다.
- `UniTask.Yield`는 `ManualFrameScheduler`에 연결된다. 테스트가 프레임을 직접 넘겨 페이드의 프레임별 상태와 취소 순서를 결정적으로 검사한다.
- 기본 스텁은 Destroy 요청을 즉시 파괴로 표시한다. 지연 파괴 시나리오만 요청을 큐에 모았다가 한 번에 처리한다.

검사 범위는 다음과 같다.

- 2D 채널별 페이드 교체와 채널 독립성, 최신 재생 요청 우선, 동일 BGM 유지.
- master와 category 볼륨, mute 유지, unscaled time.
- `Clear`의 재생, 캐시, Scope 정리 순서, 종료 뒤 새 작업 거부, 잘못된 요청의 무해성.
- 3D 음원 풀의 프리웜과 재사용, 포화 시 로드 전 거절, 완료 검사 주기, 일시정지 처리, 위치와 pitch 재설정.
- 씬 교체 중 FX와 UI 페이드 취소, BGM 페이드 유지, 채널별 클립 Scope.
- 사운드 풀 Scope 해제가 다른 풀을 보존하는지, 외부 `PoolManager.Clear` 뒤 재생 복구, 종료 시 파괴 순서.
- `UIClickSound`의 등록, 비활성화 정리, 토글 경로, `EventTrigger` 정리.

## 검증하지 않는 것

- 실제 오디오 출력, Addressables 제공자, 직렬화, UnityEvent의 엔진 내부 호출 순서.
- 실제 Unity 파괴 프레임과 콜백 순서. Play Mode에서 확인한다.
- 관리 힙 GC 할당량.

Unity 참조로 `Assembly-CSharp`와 `Assembly-CSharp-Editor`를 컴파일하려면 `./Tools/SoundTests/compile-unity.ps1`을 실행한다. Unity 프로젝트를 한 번 열어 Bee 응답 파일이 있어야 한다. 이 스크립트는 [testing.md의 Unity 참조 컴파일](../../Docs/testing.md#unity-참조-컴파일)에 설명이 있다.

## 변경 전후 비교

변경 전 `SoundManager.cs` 사본으로 한 번, 현재 소스로 한 번 실행해 출력을 비교한다.

```powershell
dotnet run --project Tools/SoundTests -p:SoundManagerPath='<변경 전 SoundManager.cs 경로>'
dotnet run --project Tools/SoundTests
```

## 관련 문서

- [architecture.md의 Sound](../../Docs/architecture.md#sound)
- [architecture.md의 Pool](../../Docs/architecture.md#pool)
