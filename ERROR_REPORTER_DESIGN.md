# ErrorReporter 설계

작성일: 2026-10-04
기준 커밋: `88d52596` 에러 리포터 기능추가 및 컨트롤러 리펙토링 진행
코드 위치: `Client/Assets/Script/Common/ErrorReport/`, `Client/Assets/Script/Common/Global.cs`
관련 문서: [분석 보고서](ERROR_REPORTING_ANALYSIS.md) · [리팩토링 결과 보고서](ERROR_REPORTING_REFACTORING_REPORT.md)

## 1. 목적과 범위

게임 실행 중 발생하는 오류를 한 곳에서 모아, 같은 오류를 묶고 반복을 제한한 뒤 교체 가능한 전송 대상(sink)으로 내보낸다. 현재 sink는 로컬 파일 하나이고, Crashlytics·자체 서버 sink를 같은 구조에 추가하는 것이 다음 단계다.

| 항목 | 결정 |
| --- | --- |
| 공개 진입점 | `Global.LogException(Exception)`, `Global.LogError(string)` — 게임 코드는 이 둘만 사용 |
| 자동 수집 | Unity 콜백에서 빠져나간 예외, `Debug.LogError`·`LogException`·`LogAssert`, UniTask 미관찰 예외 |
| 보고하지 않음 | `OperationCanceledException` (정상 취소), `Debug.Log`·`LogWarning` |
| 현재 sink | `FileErrorSink` (`persistentDataPath/ErrorReports/errors.log`) |
| 범위 밖 | Crashlytics·서버 전송, 씬·빌드 등 문맥 정보 부착 |

## 2. 왜 이 구조인가

**해결할 문제:**
- 기존 `Global.LogException`은 예외를 문자열로 바꿔 `Debug.LogError`로 출력했다. 타입과 내부 예외 구조가 사라지고, 외부 추적 수단이 없었다.
- Unity 콜백 예외, 소켓 스레드 오류, `Console.WriteLine`처럼 프로젝트 코드가 받지 못하거나 빌드에서 사라지는 경로가 있었다.

**기존 구조로 어려운 이유:**
- `ErrorCollector`는 정리 경로의 실패를 모아 **다시 throw**하는 제어 흐름 도구다. 여기서 보고하면 상위 경계에서 또 보고되어 중복이 생기고, 대부분의 오류 경로는 이곳을 지나지 않는다.
- 매니저(`ManagerBase`)로 만들면 Global 규칙상 Ready일 때만 조회되므로 초기화 실패·종료 중 오류를 받을 수 없다.

**추가 비용:** 정적 클래스 1개, 데이터 구조체 1개, 인터페이스 1개, sink 구현 1개. `Global`의 `Init`·`Update`·`OnApplicationPause`·`Shutdown`에 각 1~2줄.

## 3. 구성 요소

```
Client/Assets/Script/Common/
 ├─ Global.cs                     공개 진입점 + 생명주기 연결
 │    ├─ LogException(Exception) ──► ErrorReporter.Report
 │    ├─ LogError(string)        ──► Debug.LogError (로그 훅이 수집)
 │    ├─ Init                    ──► ErrorReporter.SetSinks(FileErrorSink)
 │    ├─ Update (finally)        ──► ErrorReporter.Flush
 │    ├─ OnApplicationPause(true)──► ErrorReporter.Flush
 │    └─ Shutdown (finally)      ──► ErrorReporter.Flush → SetSinks()
 │
 └─ ErrorReport/
      ├─ ErrorReporter.cs   (static)  수집 큐 · 집계 · sink 전달
      ├─ ErrorEvent.cs      (struct)  오류 한 건 + 지문 계산
      ├─ IErrorSink.cs      (interface) Write(묶음)
      └─ FileErrorSink.cs   (class)   파일 기록 · 회전 · 일시적 I/O 복구
```

| 타입 | 책임 | 접근 범위 |
| --- | --- | --- |
| `ErrorReporter` | 어느 스레드에서든 오류를 받아 큐에 넣고, 메인 스레드에서 지문·횟수를 집계해 sink에 전달 | `public static class`, 멤버는 모두 `internal` (게임 코드는 Global을 통해서만 사용) |
| `ErrorEvent` | 오류 한 건의 데이터와 지문 계산 규칙 | `public readonly struct`, 생성자·지문 함수는 `internal` |
| `IErrorSink` | 전송 대상 계약. Flush 한 번에 한 묶음을 받음 | `public interface` |
| `FileErrorSink` | 묶음을 파일에 한 번에 추가, 크기 상한 회전, 일시적 파일 오류 복구 | `public sealed class` |

## 4. 전체 흐름

```
 [생산자: 아무 스레드]                         [큐]                 [소비자: 메인 스레드]

 Global.LogException(ex) ─► Report ─┐
   · OCE·null 무시                   │
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

**생산자와 소비자를 나눈 이유:** 소켓 콜백처럼 메인 스레드가 아닌 곳에서도 오류가 나므로 생산자는 큐에 넣기만 한다. 집계용 `Dictionary`, 파일 I/O, sink 호출은 스레드에 안전하지 않고 비용이 커서 메인 스레드에서 프레임당 한 번 몰아서 처리한다.

## 5. 수집 경로

| 오류 발생 | 경로 | 이벤트 종류(`Kind`) | 예외 객체 | 스택 |
| --- | --- | --- | --- | --- |
| `Global.LogException(ex)` / `Forget(Global.LogException)` | `Report` | 예외 전체 타입 이름 (`System.InvalidOperationException`) | 있음 | `ex.StackTrace` |
| `AggregateException` (`ErrorCollector`가 묶은 오류 등) | `Report` → 내부 예외별 분리, 같은 `Id` 공유 | 내부 예외 각각의 타입 | 있음 | 내부 예외 각각 |
| `Global.LogError(msg)` | `Debug.LogError` → 로그 훅 | `Log` | 없음 | Unity가 붙인 호출 위치 스택 |
| Unity 콜백(Awake·Update 등)에서 빠져나간 예외 | 로그 훅 (`LogType.Exception`) | 로그 문자열의 `:` 앞 타입 이름 (`NullReferenceException`) | 없음 | Unity 스택 |
| 외부 코드의 `Debug.LogError` (Firebase Provider, 패키지) | 로그 훅 | `Log` | 없음 | Unity 스택 |
| UniTask 미관찰 예외 | `UniTaskScheduler.UnobservedTaskException` → `Report` | 예외 타입 | 있음 | 예외 스택 |
| 큐 상한 초과로 버린 이벤트 | `Flush`가 요약 이벤트 생성 | `Log` (`"N error events dropped"`) | 없음 | 없음 |

`Global.LogError`가 `ErrorReporter`를 직접 부르지 않고 `Debug.LogError`를 쓰는 이유: Unity가 로그에 붙여 주는 스택(호출 위치)을 그대로 받기 위해서다. 직접 큐에 넣으면 호출 위치가 사라진다.

## 6. 처리 단계

### 6.1 Report — 아무 스레드 ([ErrorReporter.cs:45](Client/Assets/Script/Common/ErrorReport/ErrorReporter.cs))

```
Report(error)
 ├─ null 또는 OperationCanceledException ─► 종료
 ├─ AggregateException?
 │    ├─ Flatten 후 모두 OCE ─► 종료
 │    ├─ 콘솔: Debug.LogException(원본 Aggregate) 1회
 │    └─ OCE가 아닌 내부 예외마다 같은 Id로 Enqueue
 └─ 일반 예외
      ├─ 콘솔: Debug.LogException(error)
      └─ Enqueue
```

### 6.2 로그 훅 — Unity가 로그를 남긴 스레드 ([ErrorReporter.cs:122](Client/Assets/Script/Common/ErrorReport/ErrorReporter.cs))

```
OnLogMessageReceived(condition, stackTrace, type)
 ├─ s_writing == true (ErrorReporter 자신의 콘솔 출력) ─► 무시
 ├─ type이 Exception/Error/Assert가 아님 ─► 무시
 ├─ Exception이면 condition의 ':' 앞을 Kind로, 아니면 "Log"
 └─ Enqueue
```

### 6.3 Flush — 메인 스레드 ([ErrorReporter.cs:76](Client/Assets/Script/Common/ErrorReport/ErrorReporter.cs))

```
Flush()
 1. 큐가 비었고 버린 이벤트도 없음 ─► 즉시 종료 (오류 없는 프레임의 비용)
 2. 버린 이벤트가 있으면 "N error events dropped" 이벤트를 묶음 후보에 추가
 3. 시작 시점 개수만큼만 꺼냄 (다른 스레드가 계속 넣어도 끝나도록)
 4. 각 이벤트: 지문 계산 ─► 횟수 +1 ─► 전달 대상이면 묶음에 추가 (7장)
 5. 묶음이 비었으면 종료
 6. sink마다 Write(묶음)
      └─ 예외 ─► 그 sink를 목록에서 제거, 실패를 Report (다음 Flush에서 남은 sink로 전달)
 7. finally: 묶음 비우기, s_writing 복원
```

## 7. 지문(Fingerprint)과 반복 제한

### 7.1 지문 규칙 ([ErrorEvent.cs:44](Client/Assets/Script/Common/ErrorReport/ErrorEvent.cs))

지문은 "같은 오류인가"를 판별하는 키다. 메인 스레드 Flush에서 계산한다.

```
예외 이벤트 + 프로젝트 프레임 있음   : Kind | 첫 프로젝트 프레임
로그 이벤트, 또는 프레임 없음        : Kind | 메시지 첫 줄(숫자 정규화) | 첫 프로젝트 프레임(있으면)
```

| 규칙 | 이유 |
| --- | --- |
| 예외는 메시지를 넣지 않음 | 같은 위치에서 메시지만 다른 예외(id·경로 포함)는 같은 오류로 묶는다 |
| 로그는 메시지를 넣음 | 한 메서드가 여러 종류의 오류 로그를 남기므로 위치만으로는 구분되지 않는다 |
| 메시지의 연속 숫자 → `#` | 타임스탬프·id·개수 때문에 같은 오류가 수천 개 지문으로 쪼개지지 않게 |
| 프레임의 줄 번호는 유지, IL 오프셋 `[0x…]`만 제거 | 같은 메서드의 다른 줄은 다른 오류. IL 오프셋은 빌드마다 바뀐다 |
| 건너뛰는 프레임 | `System.`, `Cysharp.`, `UnityEngine.`, `ProjectT.Global`, `ProjectT.ErrorReporter` — 엔진·라이브러리·보고 경로 자체는 오류 위치가 아니다 |
| 프로젝트 프레임이 없으면 메시지만 | Addressables 등 엔진 내부 오류가 `UnityEngine.Debug:LogError` 한 지문으로 합쳐지지 않게 |

예시:

| 오류 | 지문 |
| --- | --- |
| `BuffController.Register`에서 던진 `InvalidOperationException` | `System.InvalidOperationException\|ProjectT.Skill.BuffController.Register (System.Int32 buffId)  in …/BuffController.cs:56` |
| `Global.LogError($"SkillInfo Not Found SkillID {id}")` (빌드) | `Log\|[ERROR][#-#-# #:#:#] [Global] SkillInfo Not Found SkillID #\|ProjectT.Skill.SkillActionController:RegisterSkill (int) (at Assets/…:62)` |
| Addressables 내부의 `Debug.LogError` | `Log\|<메시지 첫 줄>\|` |
| Update에서 빠져나간 NRE | `NullReferenceException\|ProjectT.Foo:Bar () (at Assets/…/Foo.cs:42)` |

### 7.2 반복 제한과 상한

| 상수 | 값 | 동작 |
| --- | --- | --- |
| `MaxDeliveriesPerFingerprint` | 5 | 같은 지문은 1~5번째, 그리고 10·100·1000…번째만 sink로 전달. 나머지는 횟수만 센다 |
| `MaxQueuedEvents` | 1000 | Flush가 돌지 못하는 동안(Global 없음 등) 큐가 무한히 커지지 않게 넘친 이벤트는 개수만 세고, 다음 Flush에서 요약 이벤트 1건으로 보고 |
| `MaxFingerprints` | 1000 | 지문 종류가 상한을 넘으면 새 지문은 `Overflow` 한 칸에서 횟수를 센다 (이벤트 자체는 원래 지문을 유지) |

전달된 이벤트의 `Occurrence`에 세션 내 발생 순번이 담겨, 파일에서 `#100`처럼 반복 규모를 볼 수 있다. 세션 종료 시 별도 요약을 만들지 않으므로 크래시로 종료돼도 그때까지의 규모가 남는다.

```
같은 오류가 매 프레임 발생 (120회)
 발생:  1  2  3  4  5  6 … 9  10  11 … 99  100  101 … 120
 전달:  ✔  ✔  ✔  ✔  ✔  ✘ … ✘  ✔   ✘ …  ✘   ✔   ✘  …  ✘     → sink에는 7건
```

## 8. 스레드 모델

| 멤버 | 접근 스레드 | 보호 방식 |
| --- | --- | --- |
| `Report`, `OnLogMessageReceived`, UniTask 훅 | 아무 스레드 | 큐에 넣기만 함 |
| `s_queue` | 생산자: 아무 스레드 / 소비자: 메인 | `ConcurrentQueue` |
| `s_queued`, `s_dropped` | 아무 스레드 | `Interlocked` (생산자는 `Count`를 읽지 않음) |
| `s_writing` | 각 스레드 | `[ThreadStatic]` — 로그를 남긴 스레드에서 훅이 동기 호출된다는 Unity 동작에 의존 |
| `s_counts`, `s_batch` | 메인 (`Flush`) | 메인 스레드 전용 |
| `s_sinks` | 메인 (`Flush`, `SetSinks`) | 배열 교체 방식. Flush는 지역 복사본을 순회 |
| sink 구현체 | 메인 (`Flush`) | 스레드 안전 불필요 |

## 9. 생명주기

```
앱 시작
 └─ [SubsystemRegistration] ErrorReporter.ResetStatics
      · 큐·카운터·집계·sink 초기화
      · 로그 훅, UniTask 훅 구독 (-= 후 += 로 중복 방지)
      · 이 시점부터 오류를 큐에 받음 (sink는 아직 없음)

Global.Awake → Init (활성 인스턴스만)
 └─ SetSinks(FileErrorSink)          · 교체 방식이라 재생성돼도 sink 중복 없음
                                     · 중복 Global은 Init에서 반환해 여기까지 오지 않음

매 프레임 Global.Update
 └─ try { host.Update } finally { Flush }   · 매니저가 예외를 던져도 Flush 실행

OnApplicationPause(true)
 └─ Flush                            · 모바일은 종료 콜백 없이 프로세스가 끝날 수 있음

Global.Shutdown (OnApplicationQuit / OnDestroy)
 ├─ host.Shutdown + ShutdownErrors 보고 (초기화 실패로 이미 Failed면 InitializeAsync에서 보고했으므로 생략)
 └─ finally: Flush → SetSinks() → s_instance = null
                                     · 종료 오류까지 기록한 뒤 이 인스턴스의 sink 해제
```

**SetSinks와 Flush의 관계:** `SetSinks`는 보낼 곳을 통째로 지정하고(추가가 아닌 교체), `Flush`는 큐를 비우면서 걸러낸 묶음을 그 목록에 전달한다. sink가 없을 때 Flush는 큐를 비우고 횟수만 센다.

## 10. 콘솔 중복 출력 방지

`Report`는 콘솔에 `Debug.LogException`을 쓰는데, Unity는 이 출력을 다시 로그 훅으로 보낸다. 그대로 두면 같은 오류가 큐에 두 번 들어간다.

```
Report(ex)
 ├─ s_writing = true
 ├─ Debug.LogException(ex) ──► Unity ──► OnLogMessageReceived ──► s_writing이 true라 무시
 ├─ s_writing = 이전 값
 └─ Enqueue(ex)                                                    → 큐에는 1건
```

같은 플래그를 Flush의 sink 호출 동안에도 켜 둔다. sink가 내부에서 로그를 남겨도 다시 수집되지 않아 "sink 로그 → 큐 → sink" 순환이 생기지 않는다.

## 11. FileErrorSink

| 항목 | 내용 |
| --- | --- |
| 경로 | `Application.persistentDataPath/ErrorReports/errors.log` |
| 기록 단위 | Flush 한 번의 묶음을 문자열로 만든 뒤 `File.AppendAllText` 1회. 파일 핸들을 계속 잡지 않음 |
| 크기 상한 | 1MB. 기존 크기 + 이번 묶음이 넘으면 `errors.1.log`를 지우고 `errors.log`를 그 이름으로 옮긴 뒤 새로 기록 (최대 2개 파일) |
| 일시적 오류 | `IOException`, `UnauthorizedAccessException` (다른 프로세스 잠금, 디스크 부족 등)은 sink 안에서 복구. 회전 실패 시에도 추가는 시도하고, 추가 실패 시 묶음을 버린 뒤 다음 성공 기록 맨 앞에 버린 묶음 수를 남김 |
| 그 밖의 오류 | 전파 → ErrorReporter가 이 sink를 세션 동안 제거 |

파일 형식:

```
2026-10-04T06:39:13.1234567Z [3f2a…c91e] #1 System.InvalidOperationException|ProjectT.Skill.BuffController.Register (…) in …/BuffController.cs:56
System.InvalidOperationException: Buff already registered
  at ProjectT.Skill.BuffController.Register (System.Int32 buffId) …
2026-10-04T06:39:14.0000000Z [77b0…01aa] #100 Log|[Global] SkillInfo Not Found SkillID #|ProjectT.Skill.SkillActionController:RegisterSkill (int) (at …:62)
[Global] SkillInfo Not Found SkillID 4021
ProjectT.Skill.SkillActionController:RegisterSkill (int) (at Assets/Script/Skill/Action/SkillActionController.cs:62)
…
2026-10-04T06:40:02.5000000Z 3 error batches dropped
```

- 첫 줄: 시각(UTC), 사건 id(같은 Aggregate에서 나온 이벤트는 같음), 발생 순번, 지문
- 이어서: 예외가 있으면 `Exception.ToString()`, 없으면 메시지와 Unity 스택

## 12. try/catch 위치

AGENTS.md의 "catch는 큰 흐름의 경계에만" 규칙에 따라 catch는 두 곳뿐이다.

| 위치 | 경계 종류 | 동작 |
| --- | --- | --- |
| `ErrorReporter.Flush`의 sink별 catch | 최종 보고 경계 | 실패한 sink만 격리하고 나머지 sink에 계속 전달. 실패는 다시 throw하지 않고 한 번 보고 |
| `FileErrorSink.Write`의 `when (IsTransientInternal)` | 외부 I/O 복구 | 일시적 파일 오류에서 회전 건너뛰기·묶음 버리기로 복구 |

나머지는 플래그·묶음 정리용 `try/finally`다.

## 13. 사용 가이드

### 13.1 게임 코드

```csharp
// 처리할 수 없는 예외: 호출자에게 전파한다. 최종 경계(Forget, Unity 콜백)가 자동 보고한다.
throw new InvalidOperationException($"Buff not found: {buffId}");

// 비동기 실행 경계
DoAsync().Forget(Global.LogException);

// 예외 객체를 직접 잡은 경계(소켓 콜백 등)에서만 직접 보고
catch (Exception ex)
{
    Global.LogException(ex);
    onDisconnected?.Invoke(this);
}

// 예외가 아닌 처리된 오류 (테이블 누락 등)
Global.LogError($"SkillInfo Not Found SkillID {skillID}");
```

하지 말 것:

| 금지 | 이유 |
| --- | --- |
| `Global.LogError($"... {ex.StackTrace}")`, `Global.LogError(ex.ToString())` | 예외 타입·구조가 사라지고 지문이 메시지 기준으로 뭉개진다. `Global.LogException(ex)` 사용 |
| catch에서 `LogException` 후 `throw` | 상위 경계에서 한 번 더 보고된다 |
| `ErrorCollector`나 개별 함수에서 보고 | 중복 보고. `ErrorCollector`는 모아서 다시 throw만 한다 |
| `Console.WriteLine` | Unity 빌드에서 출력되지 않아 오류가 사라진다 |
| `OperationCanceledException` 보고 | 정상 취소다. 자동으로 무시되지만 일부러 문자열로 남기지 않는다 |

### 13.2 새 sink 추가 (예: Crashlytics)

1. `IErrorSink` 구현. 메인 스레드에서 묶음 단위로 호출되며, 묶음 리스트는 호출 후 재사용되므로 보관하지 않는다.

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

2. `Global.Init`의 `SetSinks`에 추가.

```csharp
ErrorReporter.SetSinks(
    new FileErrorSink(Path.Combine(Application.persistentDataPath, "ErrorReports")),
    new CrashlyticsErrorSink());
```

3. 일시적 실패(네트워크 등)는 sink 안에서 복구한다. sink 밖으로 던진 예외는 그 sink를 세션 동안 제거한다.

## 14. 테스트

`Tools/LifecycleTests/ErrorReporterTests.cs` (14개, `Tools/LifecycleTests/run.ps1`로 실행). Unity API는 `UnityStubs.cs`가 흉내 내며, `Debug.LogError`는 Unity 형식 스택을 넘긴다.

| # | 검증 내용 |
| --- | --- |
| 1 | Aggregate를 내부 예외별로 나누고 같은 id로 한 번만 전달, 콘솔 출력 1회 |
| 2 | OCE·취소뿐인 Aggregate·null 무시 |
| 3 | 자신의 콘솔 출력은 재수집 안 함, 외부 로그는 수집, `LogError` 지문이 호출 위치를 가리킴 |
| 4 | UniTask 미관찰 예외 경로 |
| 5 | 지문 규칙 (같은 프레임 다른 메시지 분리, 숫자 정규화, 다른 줄 분리, 엔진 프레임뿐이면 메시지, IL 오프셋 무시, 예외 타입 파싱) |
| 6 | 반복 제한 (1,2,3,4,5,10,100) |
| 7 | 큐 상한과 버린 개수 요약 |
| 8 | 지문 사전 상한과 Overflow |
| 9 | 실패한 sink 격리, 재귀 없음 |
| 10 | `host.Update` 예외에도 Flush 실행 |
| 11 | 종료 오류 1회 보고 (초기화 실패 / 초기화 중 종료) |
| 12 | Global 재생성·중복 인스턴스에서 sink 중복 없음 |
| 13 | 파일 회전 |
| 14 | 일시적 파일 잠금 후 복구와 버린 묶음 기록 |

## 15. 제약과 알려진 한계

| 항목 | 내용 |
| --- | --- |
| Unity 동작 의존 | `logMessageReceivedThreaded`가 로그를 남긴 스레드에서 동기로 호출된다고 가정 (`s_writing` 중복 방지). Play Mode 확인 필요 |
| 스택 설정 | 로그 이벤트의 호출 위치는 `Player Settings > Stack Trace`가 Error에 대해 ScriptOnly 이상이어야 남는다 |
| 종류 이름 | `Report` 경로는 전체 타입 이름, 로그 훅 경로는 짧은 타입 이름을 쓴다. 대시보드 연동 시 통일 필요 |
| 회전 실패 지속 | `errors.1.log`가 계속 잠겨 있으면 `errors.log`가 상한을 넘을 수 있다 |
| Global 내부 오류 | `ProjectT.Global` 프레임을 건너뛰므로 Global 자체 코드의 오류는 다음 프레임이나 메시지로 지문이 정해진다 |
| 종료 후 오류 | `Shutdown` 이후 발생한 오류는 큐에 남고(상한 1000) 기록되지 않는다 |
| 에디터 | Play Mode 종료 후에도 훅이 구독된 채 Edit Mode 오류를 큐에 받는다. 상한으로 1000건에서 멈추고 다음 Play 진입 때 초기화된다 |
| 문맥 정보 | 씬·빌드 버전·매니저 상태는 아직 붙지 않는다 |

## 16. 다음 단계

| 단계 | 내용 |
| --- | --- |
| 2 | Crashlytics SDK와 `CrashlyticsErrorSink`, IL2CPP 심볼 업로드를 `BuildMgr`에 연결, 종류 이름 통일 |
| 3 | 씬·빌드 버전·매니저 상태 문맥, Analytics 오류율 이벤트 |
| 4 (선택) | 자체 서버 HTTP sink, 오프라인 재전송 |
