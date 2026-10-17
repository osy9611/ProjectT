# 소켓 리팩토링 동작 보존 검증

`BytesPool`의 풀 구성과 `SendPipe`의 프레임 직렬화 리팩토링을 검증한다. 기존 수명·UI 테스트에 포함되지 않은 크기 경계, 실제 프레임 바이트, pending 순서, 풀 반환과 예외 시점을 확인하기 위해 별도 실행기를 둔다. 제품 코드에 테스트용 API를 추가하지 않는다.

## 실행

PowerShell에서 저장소 루트를 기준으로 실행한다. .NET SDK와 .NET 9 reference pack/runtime이 필요하며 NuGet 복원이나 네트워크 접근은 하지 않는다.

```powershell
& ./Tools/SocketTests/run.ps1
```

변경 전 소켓 소스를 별도 복사한 경우 같은 테스트를 적용할 수 있다. `SocketRoot`는 `Concurrent`, `Default`, `Message`, `Stream` 하위 폴더를 포함하는 경로다.

```powershell
& ./Tools/SocketTests/run.ps1 -SocketRoot 'C:/path/to/baseline/Socket'
```

두 실행은 같은 `bin`을 사용하므로 순서대로 실행한다. `Pool`, `IPoolable`, `ErrorCollector`는 두 실행 모두 현재 저장소 소스를 사용한다. 이 의존성도 바뀌는 작업이라면 비교 기준을 함께 보존해야 한다.

## 검증 범위

- 5개 풀의 모든 마스크 조합과 전체 마스크에 따른 초기 개수.
- 생성자 오류 인자, 바이트 크기 경계, 대여·반환·재사용.
- 메시지 길이와 ID의 big-endian 헤더 및 본문 바이트.
- 빈 큐, pending 우선 처리, 후속 큐 메시지, Clear 반환.
- 직렬화 실패와 풀 반환 실패 시 payload 및 소유권의 기존 상태.

테스트는 실제 소켓 하위 소스와 공통 풀 소스를 직접 컴파일한다. `UnityEngine`의 빈 namespace는 `IPoolable`의 미사용 using을 해석하기 위한 것이며 엔진 동작을 대체하지 않는다. 결과물과 컴파일 응답 파일은 무시되는 `bin`에 저장된다.

## 기존 동작을 확인하는 항목

다음 기대값은 이상적인 제품 동작을 정의하지 않는다. 이번 기능 불변 요구에 따라 변경 전후의 차이를 검출하기 위한 것이다.

- `ExistingCapacityBoundary`: 현재 용량 판정은 `2 + stream.Count`이며 실제 프레임 헤더는 4바이트다.
- `ExistingSegmentEnqueueBehavior`: `Enqueue(ArraySegment<byte>)`는 스트림을 얻고 쓰지만 실제 큐에 넣지 않는다.
- `SerializationFailureKeepsStreamBorrowed`: 직렬화 도중 예외가 나면 이미 꺼낸 스트림은 반환되지 않는다.
- 대형 바이트 할당의 `Enabled == false`와 음수 요청 시 대여 후 예외도 기존 동작으로 확인한다.

이 동작을 수정하는 별도 결함 작업에서는 기대값과 요구사항을 함께 갱신해야 한다.

## 실행 결과와 한계

2026-09-27 작업 시작 스냅샷과 수정본 모두 **12개 사례, 462개 검사 통과**.

추가로 `Tools/NotificationTests/compile-unity.ps1`을 통해 기존 Unity 참조 기반 런타임·에디터 컴파일이 통과했다. 빌드 도구의 기존 `CS1998` 경고는 남아 있다.

이번 검증은 실제 TCP 송수신, 동시 실행, Unity Play Mode, Mono/IL2CPP 실행, 성능 측정을 포함하지 않는다. 테스트 통과는 조사한 입력과 경로의 동작 보존 근거이며 소켓 전체의 제품 준비 완료를 뜻하지 않는다.
