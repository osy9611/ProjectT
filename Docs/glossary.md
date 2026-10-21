# 용어집

ProjectT 문서는 한 개념을 한 이름으로 부른다. 이 문서는 권장어와 쓰지 않는 말을 정하고, `Tools/check-docs.ps1`은 아래 "검사 규칙" 표로 문서를 검사한다.

## 권장어

| 개념 | 권장어 | 쓰지 않는 말 | 비고 |
| --- | --- | --- | --- |
| 풀에서 꺼냄 (`Get`, `GetAsync`) | 대여 | 빌리다 | API를 말할 때는 `Get` |
| 풀에 돌려줌 (`Return`, `IPoolable.OnReturn`) | 반납 | 반환 | 함수의 반환값은 "반환"이라고 쓴다 |
| 반납과 파괴가 함께 지나는 경로 (`EndSpawnInternal`) | 스폰 종료 | 반납 정리, Actor 정리 | |
| `ResourceScope.Dispose`, 리소스 `Release`, 구독·입력 컨텍스트 등록 취소 | 해제 | Scope 반환 | |
| `SkillAgent`·`Status`의 `ReleaseInternal`, 매니저의 `Clear` | 정리 | | |
| `Faulted`, `EndFaulted` | 격리 상태 | 실패 상태 | `EnterFailed`는 진입 실패 |
| Actor의 `OnEnable`, `OnDisable` | 활성화, 비활성화 | | 스킬에는 쓰지 않는다 |
| 스킬의 `ActivateSkill` | 발동 | 스킬 활성화 | |
| C# `event` (`OnSkillActivated` 등) | 이벤트 | 알림 | |
| `NotificationManager.Publish` | 통지 | 알림 | |
| `ManagerHost` | 호스트 | host | 코드 이름은 `ManagerHost` |
| `ManagerBase.LifetimeToken` | 수명 토큰 | 종료 토큰, lifetime 토큰 | |
| `ResourceManager.AppScope` | 앱 Scope | App Scope | 코드 이름은 `AppScope` |
| 씬마다 만드는 Scope | 씬 Scope | | |
| `Client/Assets/Script/AbilitySystem` | 스킬 시스템 | 능력 시스템, Skill 시스템, SkillSystem | 처음 나올 때 "스킬 시스템(`AbilitySystem`)" |
| `Tools/`의 dotnet 테스트 프로젝트 | 콘솔 테스트 | .NET 테스트 프로젝트 | |
| `Client/Assets/Tests/Editor`의 테스트 | Unity 테스트 | | 실행 도구는 Unity Test Runner |
| `Client/Assets/Tests/Probes`의 코드 | 프로브 | | |

## 형식

- 문체는 한다체로 쓴다.
- 날짜는 `YYYY-MM-DD`로 쓴다.
- 커밋은 8자리 해시와 링크로 쓴다.
- 코드 위치는 줄 번호 대신 타입·함수 이름으로 쓴다.
- 장대시, 굵은 글씨 뒤 콜론, 화살표로 이은 문장 조각은 쓰지 않는다. 표의 순서 칸은 예외다.
- 제목은 요점을 담고, h1 다음은 h2부터 쓴다.

## 검사 규칙

`Tools/check-docs.ps1`은 아래 블록의 규칙으로 문서 본문을 검사한다. 한 줄이 규칙 하나이고, 칸은 탭으로 나눈다. 칸은 이름, 대상(`all`, `wiki`, `not-testing`), 정규식 순서다. 코드 블록과 인라인 코드는 검사하지 않는다.

```text
풀 문맥의 반환	all	(풀|Pool|반납)[^.]{0,30}반환|반환(한다|하고|되|된다|했다)[^.]{0,20}(풀|Pool)
Scope 반환	all	Scope[^.]{0,10}반환
반납 정리, Actor 정리	all	반납 정리|Actor 정리
실패 상태	all	실패 상태
스킬 활성화	all	스킬[을를이가]? 활성화
host	all	(?<![A-Za-z_.])host(?![A-Za-z_(])
lifetime 토큰, 종료 토큰	all	lifetime 토큰|종료 토큰
App Scope	all	App Scope
능력 시스템 등	all	능력 시스템|Skill 시스템|SkillSystem
.NET 테스트 프로젝트	all	\.NET 테스트 프로젝트
빌리다	all	빌[려린리]
장대시	all	 — 
내부 백로그 번호	wiki	(?<![A-Za-z0-9_])(R[0-9]|A[0-9]|F[0-9]{1,2}|B[0-9]|D[0-9]|P[1-9][ab]?|M[0-9])(?![A-Za-z0-9_])|(?<![0-9A-Za-z])2[0-9][a-h](?![A-Za-z0-9])
정본 밖 기준선 수치	not-testing	test runs|pass=[0-9]|[0-9]+/[0-9]+ (passed|통과)
```
