# ErrorReporter

게임 실행 중 생긴 오류는 `Global.LogException`, `Global.LogError`와 자동 수집 경로를 거쳐 `ErrorReporter`의 큐에 들어간다. 메인 스레드의 `ErrorReporter.Flush`가 같은 오류를 지문으로 묶고 반복 전달을 제한한 뒤 교체 가능한 전송 대상(`IErrorSink`)에 넘긴다. 한 오류는 큰 흐름의 경계에서 한 번만 보고하며, 이 문서가 catch 위치와 보고 경계의 정본이다.

## 대상 코드

- `Client/Assets/Script/Common/ErrorReport/ErrorReporter.cs`
- `Client/Assets/Script/Common/ErrorReport/ErrorEvent.cs`
- `Client/Assets/Script/Common/ErrorReport/IErrorSink.cs`
- `Client/Assets/Script/Common/ErrorReport/FileErrorSink.cs`
- `Client/Assets/Script/Common/Global.cs`의 `Init`, `InitializeAsync`, `Update`, `OnApplicationPause`, `Shutdown`, `LogError`, `LogException`
- `Client/Assets/Script/Common/ErrorCollector.cs`
- 테스트는 `Tools/LifecycleTests/ErrorReporterTests.cs`

## 게임 코드는 진입점 두 개만 쓴다

| 항목 | 결정 |
| --- | --- |
| 공개 진입점 | `Global.LogException(Exception)`, `Global.LogError(string)`. 게임 코드는 이 둘만 쓴다 |
| 자동 수집 | Unity 콜백에서 빠져나간 예외, `Debug.LogError`·`LogException`·`LogAssert`, UniTask 미관찰 예외 |
| 보고하지 않음 | `Debug.Log`·`LogWarning`, null |
| 취소 예외 | 정상 취소는 호출 지점의 catch(`when` 조건)나 `SuppressCancellationThrow`가 거른다. `Report`까지 온 `OperationCanceledException`은 예상하지 못한 취소로 보고한다. UniTask 미관찰 예외 경로는 `UniTaskScheduler.PropagateOperationCanceledException`이 `false`라 취소가 들어오지 않는다 |
| 현재 sink | `FileErrorSink` (`persistentDataPath/ErrorReports/errors.log`) |
| 범위 밖 | Crashlytics·서버 전송, 씬·빌드 등 문맥 정보 부착 |

## 구성 요소는 정적 클래스 하나와 sink 계약이다

```
Client/Assets/Script/Common/
 ├─ Global.cs                     공개 진입점 + 생명주기 연결
 │    ├─ LogException(Exception) ──► ErrorReporter.Report
 │    ├─ LogError(string)        ──► Debug.LogError (로그 훅이 수집)
 │    ├─ Init                    ──► ErrorReporter.SetSinks(FileErrorSink)
 │    ├─ Update (finally)        ──► ErrorReporter.Flush
 │    ├─ OnApplicationPause(true)──► ErrorReporter.Flush
 │    └─ Shutdown                ──► host.Shutdown 예외를 LogException
 │                                    finally: ErrorReporter.Flush → SetSinks()
 │
 └─ ErrorReport/
      ├─ ErrorReporter.cs   (static)  수집 큐 · 집계 · sink 전달
      ├─ ErrorEvent.cs      (struct)  오류 한 건 + 지문 계산
      ├─ IErrorSink.cs      (interface) Write(묶음)
      └─ FileErrorSink.cs   (class)   파일 기록 · 회전 · 일시적 I/O 복구
```

| 타입 | 책임 | 접근 범위 |
| --- | --- | --- |
| `ErrorReporter` | 어느 스레드에서든 오류를 받아 큐에 넣고, 메인 스레드에서 지문과 횟수를 집계해 sink에 전달한다 | `public static class`. 호출 가능한 멤버는 모두 `internal`이라 게임 코드는 `Global`을 통해서만 쓴다 |
| `ErrorEvent` | 오류 한 건의 데이터와 지문 계산 규칙 | `public readonly struct`. 생성자와 지문 함수는 `internal` |
| `IErrorSink` | 전송 대상 계약. `Flush` 한 번에 한 묶음을 받는다 | `public interface` |
| `FileErrorSink` | 묶음을 파일에 한 번에 추가하고, 크기 상한에서 회전하고, 일시적 파일 오류를 복구한다 | `public sealed class` |

## 생산자는 큐에 넣기만 하고 메인 스레드가 집계한다

```
 [생산자: 아무 스레드]                         [큐]                 [소비자: 메인 스레드]

 Global.LogException(ex) ─► Report ─┐
   · null 무시                       │
   · Debug.LogException (콘솔)       │
   · Aggregate → 내부 예외별 분리     │
                                     ├─► EnqueueInternal ─► ConcurrentQueue ─► Flush ─► sink.Write(묶음)
 Unity 로그 ─► OnLogMessageReceived ─┤     · 상한 1000         (s_queue)        │      ├─ FileErrorSink
   · Exception/Error/Assert만        │     · 넘치면 s_dropped++                  │      └─ (Crashlytics…)
   · 자신이 쓴 콘솔 로그는 무시        │                                          │
                                     │                                          ├─ 지문 계산
 UniTask 미관찰 예외 ─► Report ──────┘                                          ├─ 횟수 집계·반복 제한
                                                                                └─ 실패한 sink 격리
```

Unity는 `Application.logMessageReceivedThreaded`를 로그를 남긴 스레드에서 호출한다. 패키지 내부 스레드나 `FirebaseDB.GetBuildVersion`의 `ContinueWith`처럼 스레드 풀에서 도는 코드도 로그를 남긴다. 그래서 생산자는 큐에 넣기만 한다. 집계용 `Dictionary`, 파일 I/O, sink 호출은 스레드에 안전하지 않고 비용이 크므로 메인 스레드에서 프레임당 한 번 몰아서 처리한다.

| 멤버 | 접근 스레드 | 보호 방식 |
| --- | --- | --- |
| `Report`, `OnLogMessageReceived`, UniTask 훅 | 아무 스레드 | 큐에 넣기만 한다 |
| `s_queue` | 생산자는 아무 스레드, 소비자는 메인 스레드 | `ConcurrentQueue` |
| `s_queued`, `s_dropped` | 아무 스레드 | `Interlocked`. 생산자는 `Count`를 읽지 않는다 |
| `s_writing` | 각 스레드 | `[ThreadStatic]`. 로그를 남긴 스레드에서 훅이 동기로 호출된다는 Unity 동작에 의존한다 |
| `s_counts`, `s_batch` | 메인 스레드(`Flush`) | 메인 스레드 전용 |
| `s_sinks` | 메인 스레드(`Flush`, `SetSinks`) | 배열을 통째로 교체한다. `Flush`는 지역 복사본을 순회한다 |
| sink 구현체 | 메인 스레드(`Flush`) | 스레드 안전이 필요 없다 |

## 수집 경로마다 Kind와 스택이 다르다

| 오류 발생 | 경로 | 이벤트 종류(`Kind`) | 예외 객체 | 스택 |
| --- | --- | --- | --- | --- |
| `Global.LogException(ex)`, `Forget(Global.LogException)` | `Report` | 예외 전체 타입 이름 (`System.InvalidOperationException`) | 있음 | `ex.StackTrace` |
| `AggregateException` (`ErrorCollector`가 묶은 오류 등) | `Report`가 내부 예외별로 나누고 같은 `Id`를 붙인다 | 내부 예외 각각의 타입 | 있음 | 내부 예외 각각 |
| `Global.LogError(msg)` | `Debug.LogError`를 거쳐 로그 훅 | `Log` | 없음 | Unity가 붙인 호출 위치 스택 |
| Unity 콜백(`Awake`, `Update` 등)에서 빠져나간 예외 | 로그 훅 (`LogType.Exception`) | 로그 문자열의 `:` 앞 타입 이름 (`NullReferenceException`) | 없음 | Unity 스택 |
| 외부 코드의 `Debug.LogError` (Addressables 등 패키지) | 로그 훅 | `Log` | 없음 | Unity 스택 |
| UniTask 미관찰 예외 | `UniTaskScheduler.UnobservedTaskException`에서 `Report` | 예외 타입 | 있음 | 예외 스택 |
| 큐 상한 초과로 버린 이벤트 | `Flush`가 요약 이벤트를 만든다 | `Log` (`"N error events dropped"`) | 없음 | 없음 |

## 처리는 Report, 로그 훅, Flush 세 단계로 나뉜다

### Report는 Aggregate를 내부 예외별로 나눈다

`ErrorReporter.Report`는 아무 스레드에서 호출된다.

```
Report(error)
 ├─ null ─► 종료
 ├─ AggregateException?
 │    ├─ 콘솔: Debug.LogException(원본 Aggregate) 1회
 │    └─ 내부 예외마다(취소 포함) 같은 Id로 Enqueue
 └─ 일반 예외
      ├─ 콘솔: Debug.LogException(error)
      └─ Enqueue
```

### 로그 훅은 Exception, Error, Assert만 받는다

`ErrorReporter.OnLogMessageReceived`는 Unity가 로그를 남긴 스레드에서 호출된다.

```
OnLogMessageReceived(condition, stackTrace, type)
 ├─ s_writing == true (ErrorReporter 자신의 콘솔 출력) ─► 무시
 ├─ type이 Exception/Error/Assert가 아님 ─► 무시
 ├─ Exception이면 condition의 ':' 앞을 Kind로, 아니면 "Log"
 └─ Enqueue
```

### Flush는 시작 시점의 개수만 꺼내 sink마다 한 묶음으로 전달한다

`ErrorReporter.Flush`는 메인 스레드에서 호출된다.

```
Flush()
 1. 큐가 비었고 버린 이벤트도 없음 ─► 즉시 종료 (오류 없는 프레임의 비용)
 2. 버린 이벤트가 있으면 "N error events dropped" 이벤트를 묶음 후보에 추가
 3. 시작 시점 개수만큼만 꺼냄 (다른 스레드가 계속 넣어도 끝나도록)
 4. 각 이벤트: 지문 계산 ─► 횟수 +1 ─► 전달 대상이면 묶음에 추가
 5. 묶음이 비었으면 종료
 6. sink마다 Write(묶음)
      └─ 예외 ─► 그 sink를 목록에서 제거, 실패를 Report (다음 Flush에서 남은 sink로 전달)
 7. finally: 묶음 비우기, s_writing 복원
```

## 지문은 예외를 위치로, 로그를 메시지와 위치로 묶는다

지문은 같은 오류인지 판별하는 키이고, `ErrorEvent.MakeFingerprint`가 메인 스레드의 `Flush`에서 계산한다.

```
예외 이벤트 + 프로젝트 프레임 있음   : Kind | 첫 프로젝트 프레임
로그 이벤트, 또는 프레임 없음        : Kind | 메시지 첫 줄(숫자 정규화) | 첫 프로젝트 프레임(있으면)
```

| 규칙 | 이유 |
| --- | --- |
| 예외는 메시지를 넣지 않는다 | 같은 위치에서 메시지만 다른 예외(id·경로 포함)는 같은 오류로 묶는다 |
| 로그는 메시지를 넣는다 | 한 메서드가 여러 종류의 오류 로그를 남기므로 위치만으로는 구분되지 않는다 |
| 메시지의 연속 숫자를 `#` 하나로 바꾼다 | 타임스탬프·id·개수 때문에 같은 오류가 수천 개 지문으로 쪼개지지 않게 한다 |
| 프레임의 줄 번호는 유지하고 IL 오프셋 `[0x…]`만 지운다 | 같은 메서드의 다른 줄은 다른 오류다. IL 오프셋은 빌드마다 바뀐다 |
| `System.`, `Cysharp.`, `UnityEngine.`, `ProjectT.Global`, `ProjectT.ErrorReporter` 프레임을 건너뛴다 | 엔진, 라이브러리, 보고 경로 자체는 오류 위치가 아니다 |
| 프로젝트 프레임이 없으면 메시지만 쓴다 | Addressables 등 엔진 내부 오류가 `UnityEngine.Debug:LogError` 한 지문으로 합쳐지지 않게 한다 |

| 오류 | 지문 |
| --- | --- |
| `SkillActionController.RegisterSkill`에서 던진 `InvalidOperationException` | `System.InvalidOperationException\|ProjectT.Skill.SkillActionController.RegisterSkill (ProjectT.Skill.SkillDefinition definition)  in …/SkillActionController.cs:40` |
| `Global.LogError(msg)` (빌드) | `Log\|[ERROR][#-#-# #:#:#] [Global] <msg 첫 줄>\|<호출 위치 프레임>` |
| `FeatureSampleRunner`의 `Debug.LogError("[Sample] repeated error")` | `Log\|[Sample] repeated error\|ProjectT.Sample.FeatureSampleRunner:DrawErrorInternal () (at Assets/Script/Sample/FeatureSampleRunner.cs:198)` |
| Addressables 내부의 `Debug.LogError` | `Log\|<메시지 첫 줄>\|` |
| `Update`에서 빠져나간 NRE | `NullReferenceException\|ProjectT.Foo:Bar () (at Assets/…/Foo.cs:42)` |

## 같은 지문은 처음 다섯 번과 10의 거듭제곱 번째만 전달한다

| 상수 | 값 | 동작 |
| --- | --- | --- |
| `MaxDeliveriesPerFingerprint` | 5 | 같은 지문은 1~5번째와 10·100·1000…번째만 sink로 전달한다. 나머지는 횟수만 센다 |
| `MaxQueuedEvents` | 1000 | `Flush`가 돌지 못하는 동안(`Global` 없음 등) 큐가 무한히 커지지 않게 한다. 넘친 이벤트는 개수만 세고 다음 `Flush`에서 요약 이벤트 1건으로 보고한다 |
| `MaxFingerprints` | 1000 | 지문 종류가 상한을 넘으면 새 지문은 `Overflow` 한 칸에서 횟수를 센다. 이벤트 자체는 원래 지문을 유지한다 |

전달된 이벤트의 `Occurrence`에는 세션 안에서의 발생 순번이 담긴다. 그래서 파일에서 `#100`처럼 반복 규모를 볼 수 있다. 세션 종료 때 별도 요약을 만들지 않으므로 크래시로 끝나도 그때까지의 규모가 남는다.

```
같은 오류가 매 프레임 발생 (120회)
 발생:  1  2  3  4  5  6 … 9  10  11 … 99  100  101 … 120
 전달:  ✔  ✔  ✔  ✔  ✔  ✘ … ✘  ✔   ✘ …  ✘   ✔   ✘  …  ✘     → sink에는 7건
```

## Global 생명주기가 sink 지정과 Flush 시점을 정한다

```
앱 시작
 └─ [SubsystemRegistration] ErrorReporter.ResetStatics
      · 큐·카운터·집계·sink 초기화
      · 로그 훅, UniTask 훅 구독 (-= 후 += 로 중복 방지)
      · 이 시점부터 오류를 큐에 받음 (sink는 아직 없음)

Global.Awake → Init, [AfterSceneLoad] 활성 Global마다 Init 재호출
 ├─ SetSinks(FileErrorSink)          · 교체 방식이라 재생성돼도 sink 중복 없음
 │                                   · 이미 등록된 인스턴스는 Init에서 바로 돌아가고,
 │                                     중복 Global은 파괴되어 여기까지 오지 않음
 └─ InitializeAsync().Forget(LogException)
      · 초기화 실패: ManagerHost가 시작한 매니저를 정리하고 Failed가 된 뒤 오류를 초기화 결과로 전달
        정리 오류가 있거나 원래 오류가 취소 예외면 AggregateException으로 감싼다
        Global의 Forget 경계에서 한 번 보고
      · 초기화 중 종료: 취소로 끝나며 InitializeAsync가 삼킨다
        종료 뒤 늦게 실패한 매니저의 오류는 ManagerHost의 Forget(Global.LogException)이 보고
      · WhenReady·WhenReadyAsync는 성공 또는 취소로만 끝나 대기자에게 초기화 오류가 가지 않는다

매 프레임 Global.Update
 └─ try { host.Update } finally { Flush }   · 매니저가 예외를 던져도 Flush 실행

OnApplicationPause(true)
 └─ Flush                            · 모바일은 종료 콜백 없이 프로세스가 끝날 수 있음

Global.Shutdown (OnApplicationQuit, OnDestroy)
 ├─ try: host.Shutdown               · 매니저 종료 오류를 ErrorCollector로 모아 AggregateException으로 던진다
 │                                   · 이미 Failed·Stopping·Stopped면 아무것도 하지 않는다
 ├─ catch: LogException(error)       · 종료 오류를 한 번 보고
 └─ finally: Flush → SetSinks() → s_instance = null
                                     · 종료 오류까지 기록한 뒤 이 인스턴스의 sink 해제
```

`SetSinks`는 보낼 곳을 추가하지 않고 통째로 교체한다. `Flush`는 큐를 비우면서 걸러 낸 묶음을 그 목록에 전달한다. sink가 없으면 `Flush`는 큐를 비우고 횟수만 센다.

## 자신의 콘솔 출력은 다시 수집하지 않는다

`Report`는 콘솔에 `Debug.LogException`을 쓰고, Unity는 이 출력을 다시 로그 훅으로 보낸다. 그대로 두면 같은 오류가 큐에 두 번 들어간다. `s_writing` 플래그가 이 재수집을 막는다.

```
Report(ex)
 ├─ s_writing = true
 ├─ Debug.LogException(ex) ──► Unity ──► OnLogMessageReceived ──► s_writing이 true라 무시
 ├─ s_writing = 이전 값
 └─ Enqueue(ex)                                                    → 큐에는 1건
```

`Flush`는 sink를 호출하는 동안에도 같은 플래그를 켜 둔다. sink가 안에서 로그를 남겨도 다시 수집되지 않으므로, sink 로그가 큐를 거쳐 다시 sink로 가는 순환이 생기지 않는다.

## FileErrorSink는 파일 두 개를 돌려 쓰고 일시적 I/O 오류를 스스로 복구한다

| 항목 | 내용 |
| --- | --- |
| 경로 | `Application.persistentDataPath/ErrorReports/errors.log` |
| 기록 단위 | `Flush` 한 번의 묶음을 문자열로 만든 뒤 `File.AppendAllText`를 1회 호출한다. 파일 핸들을 계속 잡지 않는다 |
| 크기 상한 | 1MB. 기존 크기와 이번 묶음의 합이 상한을 넘으면 `errors.1.log`를 지우고 `errors.log`를 그 이름으로 옮긴 뒤 새로 기록한다. 파일은 최대 2개다 |
| 일시적 오류 | `IOException`, `UnauthorizedAccessException`(다른 프로세스의 잠금, 디스크 부족 등)은 sink 안에서 복구한다. 회전에 실패해도 추가는 시도한다. 추가에 실패하면 묶음을 버리고, 다음에 성공한 기록 맨 앞에 버린 묶음 수를 남긴다 |
| 그 밖의 오류 | 전파한다. `ErrorReporter`가 이 sink를 세션 동안 목록에서 뺀다 |

```
2026-10-04T06:39:13.1234567Z [3f2a…c91e] #1 System.InvalidOperationException|ProjectT.Skill.SkillActionController.RegisterSkill (…) in …/SkillActionController.cs:40
System.InvalidOperationException: [SkillActionController] ActionFactory returned an action that is already registered. SkillID 1001
  at ProjectT.Skill.SkillActionController.RegisterSkill (ProjectT.Skill.SkillDefinition definition) …
2026-10-04T06:39:14.0000000Z [77b0…01aa] #10 Log|[Sample] repeated error|ProjectT.Sample.FeatureSampleRunner:DrawErrorInternal () (at …:198)
[Sample] repeated error
UnityEngine.Debug:LogError (object)
ProjectT.Sample.FeatureSampleRunner:DrawErrorInternal () (at Assets/Script/Sample/FeatureSampleRunner.cs:198)
…
2026-10-04T06:40:02.5000000Z 3 error batches dropped
```

- 첫 줄은 시각(UTC), 사건 id, 발생 순번, 지문이다. 같은 Aggregate에서 나온 이벤트는 사건 id가 같다.
- 이어지는 줄은 예외가 있으면 `Exception.ToString()`이고, 없으면 메시지와 Unity 스택이다.

## catch 위치와 보고 경계

[AGENTS.md의 오류 처리](../AGENTS.md#오류-처리)는 catch를 큰 흐름의 경계에만 두라고 정한다. 그 규칙에 따라 오류 보고 코드와 `Global` 연결부의 catch는 네 곳이다.

| 위치 | 경계 종류 | 동작 |
| --- | --- | --- |
| `Global.Shutdown`의 catch | 매니저 종료 경계 | `host.Shutdown`이 던진 종료 오류를 `LogException`으로 한 번 보고하고 finally의 `Flush`로 기록한다 |
| `Global.InitializeAsync`의 `when (Stopping·Stopped)` | 확인된 정상 취소 | 초기화 중 종료로 생긴 취소만 삼킨다. 초기화 실패는 이 필터에 걸리지 않고 Forget 경계로 간다 |
| `ErrorReporter.Flush`의 sink별 catch | 최종 보고 경계 | 실패한 sink만 격리하고 나머지 sink에 계속 전달한다. 실패는 다시 throw하지 않고 한 번 보고한다 |
| `FileErrorSink.Write`의 `when (IsTransientInternal)` | 외부 I/O 복구 | 일시적 파일 오류에서 회전을 건너뛰거나 묶음을 버려 복구한다 |

나머지는 플래그와 묶음을 정리하는 `try/finally`다.

catch가 아닌 보고 경계도 있다. `Global.Init`의 `InitializeAsync().Forget(LogException)`과 `SceneManager`의 콜백형 `Transition`이 쓰는 `Forget(Global.LogException)`은 Forget 경계에서 실패를 한 번 보고한다. 콜백형 `Transition`은 정상 취소를 `SuppressCancellationThrow`로 받아 `eSceneTransitionResult.Canceled` 결과로 바꾼다.

여러 단계를 모두 실행해야 하는 정리 경로는 `ErrorCollector.Run`으로 예외를 모은 뒤 `ErrorCollector.ThrowIfAny`로 다시 던진다. 원래 예외가 있으면 그 예외를 맨 앞에 둔 `AggregateException`을 던진다. 정리 오류가 없으면 `ThrowIfAny`는 그냥 돌아가고, 호출자가 `throw;`로 원래 예외를 원래 스택 그대로 다시 던진다. `ErrorCollector`는 보고하지 않는다. 보고는 상위 경계가 한다.

## 테스트가 다루는 범위

`Tools/LifecycleTests/ErrorReporterTests.cs`가 `LifecycleTests` 프로젝트 안에서 아래 내용을 검증한다. Unity API는 `Tools/Shared/UnityStubs/UnityEngine.cs`가 대체하며, 이 대체물의 `Debug.LogError`는 Unity 형식 스택을 로그 훅에 넘긴다. 테스트용 sink는 `ListSink`, `ThrowingSink`, `LoggingSink`다. Unity 테스트(`Client/Assets/Tests`)에는 ErrorReporter 테스트가 없다. 실행 방법과 기준선은 [testing.md](testing.md)에 있다.

- Aggregate를 내부 예외별로 나누고 같은 id로 한 번만 전달하며, 콘솔에는 1회 출력한다.
- 보고 경계까지 온 취소(단일 예외와 Aggregate 내부)는 보고하고, null은 무시한다.
- 자신의 콘솔 출력은 다시 수집하지 않고 외부 로그는 수집한다. `LogError`의 지문은 호출 위치를 가리킨다.
- UniTask 미관찰 예외 경로.
- 지문 규칙. 같은 프레임의 다른 메시지 분리, 숫자 정규화, 다른 줄 분리, 엔진 프레임뿐일 때 메시지 사용, IL 오프셋 무시, 예외 타입 파싱을 확인한다.
- 반복 제한. 1, 2, 3, 4, 5, 10, 100번째만 전달한다.
- 큐 상한과 버린 개수 요약.
- 지문 사전 상한과 `Overflow`.
- 실패한 sink 격리와 재귀 없음.
- `host.Update`가 예외를 던져도 `Flush` 실행.
- `host.Shutdown` 종료 오류 1회 보고.
- 취소 예외로 인한 초기화 실패가 호출자에게 전달되고 메시지가 남는 것, 초기화 중 종료 뒤 늦게 실패한 매니저 오류의 1회 보고.
- 초기화 실패를 대기자 0명, 1명, 3명일 때 모두 1회만 보고.
- `Global` 재생성과 중복 인스턴스에서 sink 중복 없음.
- 파일 회전.
- 일시적 파일 잠금 뒤 복구와 버린 묶음 기록.

## 제약과 알려진 한계

| 항목 | 내용 |
| --- | --- |
| Unity 동작 의존 | `logMessageReceivedThreaded`가 로그를 남긴 스레드에서 동기로 호출된다고 가정한다(`s_writing` 중복 방지). Play Mode 확인이 필요하다 |
| 스택 설정 | 로그 이벤트의 호출 위치는 `Player Settings > Stack Trace`가 Error에 대해 ScriptOnly 이상이어야 남는다 |
| 종류 이름 | `Report` 경로는 전체 타입 이름을, 로그 훅 경로는 짧은 타입 이름을 쓴다. 대시보드와 연동할 때 통일이 필요하다 |
| 회전 실패 지속 | `errors.1.log`가 계속 잠겨 있으면 `errors.log`가 상한을 넘을 수 있다 |
| Global 내부 오류 | `ProjectT.Global` 프레임을 건너뛰므로 `Global` 자체 코드의 오류는 다음 프레임이나 메시지로 지문이 정해진다 |
| 종료 후 오류 | `Shutdown` 이후 생긴 오류는 큐에 남고(상한 1000) 기록되지 않는다 |
| 에디터 | Play Mode가 끝난 뒤에도 훅이 구독된 채 Edit Mode 오류를 큐에 받는다. 큐는 1000건에서 멈추고 다음 Play 진입 때 초기화된다 |
| 문맥 정보 | 씬, 빌드 버전, 매니저 상태는 아직 붙지 않는다 |

## 설계 결정

### 보고를 정적 클래스 한 곳에 모았다

이전 `Global.LogException`은 예외를 문자열로 바꿔 `Debug.LogError`로 출력했다. 그래서 타입과 내부 예외 구조가 사라졌고 외부로 추적할 수단이 없었다. Unity 콜백 예외, 메인 스레드 밖의 로그, `Console.WriteLine`처럼 프로젝트 코드가 받지 못하거나 빌드에서 사라지는 경로도 있었다.

버린 대안은 둘이다.

- `ErrorCollector`에서 보고하는 안. `ErrorCollector`는 정리 경로의 실패를 모아 다시 throw하는 제어 흐름 도구다. 여기서 보고하면 상위 경계에서 또 보고되어 중복이 생기고, 대부분의 오류 경로는 이곳을 지나지 않는다.
- 매니저(`ManagerBase`)로 만드는 안. `Global` 규칙상 매니저는 Ready일 때만 조회되므로 초기화 실패와 종료 중 오류를 받을 수 없다.

추가 비용은 정적 클래스 1개, 데이터 구조체 1개, 인터페이스 1개, sink 구현 1개, 그리고 `Global`의 `Init`·`Update`·`OnApplicationPause`·`Shutdown`에 각 1~2줄이다.

### 운영 sink가 하나여도 IErrorSink를 둔다

운영 sink는 `FileErrorSink` 하나지만 Crashlytics와 자체 서버 sink를 같은 구조에 추가할 예정이다. 테스트도 `ListSink`, `ThrowingSink`, `LoggingSink` 세 구현으로 집계, 실패한 sink 격리, 재귀 방지를 검증한다.

### Global.LogError는 Debug.LogError를 거친다

`Global.LogError`는 `ErrorReporter`를 직접 부르지 않는다. Unity가 로그에 붙여 주는 호출 위치 스택을 그대로 받기 위해서다. 직접 큐에 넣으면 호출 위치가 사라진다.

### 종료 뒤 Flush와 에디터 전용 해제는 두지 않는다

`Application.quitting`에서 한 번 더 `Flush`하는 방법은 기각했다. 늦게 온 오류는 프로세스 종료와 경쟁하므로 얻는 것이 적고, 생명주기 연동만 늘어난다. Edit Mode에서 훅을 해제하는 에디터 전용 코드도 두지 않는다. 큐는 상한에서 멈추고 다음 Play 진입 때 초기화되기 때문이다.

### 외부 전송 대상은 Crashlytics로 정했다

Firebase Crashlytics는 비치명 예외 보고와 자동 묶기를 지원하고, Firebase App이 이미 설치되어 있어 추가 비용이 가장 작다. Crashlytics는 세션당 저장하는 비치명 예외 수에 제한이 있으므로 클라이언트 쪽 반복 제한이 함께 필요하다. Firebase Analytics(GA4)는 이벤트 파라미터 값 100자, 이벤트당 파라미터 25개 제한 때문에 스택을 담을 수 없다. 그래서 Analytics는 오류율 같은 지표 이벤트에만 쓴다.

도입 순서는 다음과 같다.

1. Crashlytics SDK와 `CrashlyticsErrorSink`를 넣고, IL2CPP 심볼 업로드를 `BuildMgr`에 연결하고, 종류 이름을 통일한다.
2. 씬, 빌드 버전, 매니저 상태 문맥과 Analytics 오류율 이벤트를 붙인다.
3. 필요하면 자체 서버 HTTP sink와 오프라인 재전송을 둔다.

두 가지는 정하지 않았다.

- 외부 sink에 예외만 보낼지, `Global.LogError`로 남긴 처리된 오류까지 보낼지.
- 이벤트에 사용자 ID(Firebase Auth UID)를 넣을지, 넣는다면 개인정보 고지 문구를 어떻게 할지.

## 사용법

### 게임 코드에서 오류 보고하기

```csharp
// 처리할 수 없는 예외: 호출자에게 전파한다. 최종 경계(Forget, Unity 콜백)가 자동 보고한다.
throw new InvalidOperationException($"[SkillAgent] Registration is closed. SkillID {definition.Id}");

// 비동기 실행 경계
DoAsync().Forget(Global.LogException);

// 예외를 잡고도 흐름을 이어가야 하는 경계(씬 전환, 매니저 종료 등)에서만 직접 보고
// SceneManager의 콜백형 Transition: 실패를 보고한 뒤 completed 콜백은 항상 호출한다
catch (Exception error)
{
    Global.LogException(error);
}

// 예외가 아닌 처리된 오류 (테이블 누락 등)
Global.LogError(message);
```

다음은 하지 않는다.

| 금지 | 이유 |
| --- | --- |
| `Global.LogError($"... {ex.StackTrace}")`, `Global.LogError(ex.ToString())` | 예외 타입과 구조가 사라지고 지문이 메시지 기준으로 뭉개진다. `Global.LogException(ex)`를 쓴다 |
| catch에서 `LogException` 후 `throw` | 상위 경계에서 한 번 더 보고된다 |
| `ErrorCollector`나 개별 함수에서 보고 | 중복 보고다. `ErrorCollector`는 모아서 다시 throw만 한다 |
| `Console.WriteLine` | Unity 빌드에서 출력되지 않아 오류가 사라진다 |
| 정상 취소를 거르지 않고 `Forget(Global.LogException)`에 넘김 | 보고 경계는 취소를 거르지 않으므로 오류로 보고된다. 정상 취소는 호출 지점에서 `catch (OperationCanceledException) when (token.IsCancellationRequested)`나 `SuppressCancellationThrow`로 처리한다 |

### 새 sink 추가하기

Crashlytics SDK는 아직 설치되어 있지 않다. 아래 `CrashlyticsErrorSink`는 추가 방법을 보이는 예시 코드다.

1. `IErrorSink`를 구현한다. `Write`는 메인 스레드에서 묶음 단위로 호출된다. 묶음 리스트는 호출 뒤 재사용되므로 보관하지 않는다.

```csharp
public sealed class CrashlyticsErrorSink : IErrorSink
{
    public void Write(IReadOnlyList<ErrorEvent> errors)
    {
        foreach (var error in errors)
        {
            if (error.Exception != null)
                Crashlytics.LogException(error.Exception);
            else
                Crashlytics.Log(error.Message);
        }
    }
}
```

2. `Global.Init`의 `SetSinks` 호출에 추가한다.

```csharp
ErrorReporter.SetSinks(
    new FileErrorSink(Path.Combine(Application.persistentDataPath, "ErrorReports")),
    new CrashlyticsErrorSink());
```

3. 네트워크 같은 일시적 실패는 sink 안에서 복구한다. sink 밖으로 던진 예외는 그 sink를 세션 동안 목록에서 뺀다.

## 관련 문서

- 오류 처리 규칙의 정본: [AGENTS.md의 오류 처리](../AGENTS.md#오류-처리)
- 매니저 초기화, Ready 계약, 종료: [architecture.md](architecture.md)
- 테스트 실행법과 기준선: [testing.md](testing.md)
- 남은 과제: [open-issues.md](open-issues.md)
