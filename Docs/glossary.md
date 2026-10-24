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
| 한 주제의 자세한 규칙을 맡는 저장소 문서 | 상세 문서 | 정본 | 위키와 요약 문서는 상세 문서로 링크한다 |

## 형식

- 문체는 한다체로 쓴다.
- 날짜는 `YYYY-MM-DD`로 쓴다.
- 커밋은 8자리 해시와 링크로 쓴다.
- 코드 위치는 줄 번호 대신 타입·함수 이름으로 쓴다.
- 장대시, 굵은 글씨 뒤 콜론, 화살표로 이은 문장 조각은 쓰지 않는다. 표의 순서 칸은 예외다.
- 제목은 요점을 담고, h1 다음은 h2부터 쓴다.

## 위키 페이지 구성

위키 시스템 페이지는 설계 이유와 호출자가 알아야 할 계약을 요약하고, 세부 계약은 `Docs/`의 상세 문서로 링크한다. 절은 아래 순서로 쓰고, 다룰 내용이 없는 절은 뺀다. 표의 절 이름은 순서를 가리키는 이름이고, 실제 제목은 형식 규칙대로 그 페이지의 요점을 담는다.

| 순서 | 절 | 쓰는 것 |
| --- | --- | --- |
| 1 | 첫 문단 | 이 시스템이 보장하는 것을 두세 문장으로 |
| 2 | 상세 문서 안내 | 이 페이지가 요약하는 범위와 `Docs/`의 상세 문서 링크 |
| 3 | 이전 구조의 문제 | 바꾸기 전 구조에서 실제로 생긴 문제 |
| 4 | 현재 구조 | 구성 요소와 책임 표, 필요하면 상태도 |
| 5 | 흐름 | 초기화, 요청 처리, 종료처럼 순서가 있는 흐름을 흐름도로. 그림에 담지 않은 조건만 짧은 목록으로 |
| 6 | 사용 계약 | 호출자가 알아야 할 계약만. 세부 규칙은 상세 문서에만 두고 링크한다 |
| 7 | 설계 선택과 이유 | 고른 방식과 이유, 버린 대안 |
| 8 | 검증 | [테스트](testing.md) 문서로 가는 링크 한 줄. 시나리오 목록과 기준선 수치는 테스트 문서에만 둔다 |
| 9 | 구현 예시 | 선택. 아래 예시 규칙을 따른다 |

- 커밋 이력은 시스템 페이지에 쓰지 않고 Refactoring 페이지에 모은다.
- 흐름도는 왼쪽에 정상 흐름, 오른쪽에 점선으로 이은 실패 결과를 둔다. 실패 분기가 다섯 개를 넘으면 그림을 나눈다. 원본은 위키 저장소의 `diagrams/<페이지>-<번호>.mmd`에 mermaid로 두고, `Tools/build-wiki-diagrams.ps1`이 만든 `images/<같은 이름>.svg`를 페이지에 넣는다. 그림을 고칠 때는 원본을 고친 뒤 스크립트를 다시 실행한다.
- C# 예시는 `csharp` 코드 블록에 실제 코드를 줄 그대로 옮긴다. 생략한 부분은 `// ...` 주석 줄로 표시하고, 코드 위치는 블록 앞 문장에 타입·함수 이름으로 쓴다. `Tools/check-docs.ps1`은 위키 예시의 주석이 아닌 줄이 `Client/Assets`, `Tools`, `DesignTable`의 C# 코드(`bin`, `obj` 제외)에 있는지 검사한다.

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
testing.md 밖 기준선 수치	not-testing	test runs|pass=[0-9]|[0-9]+/[0-9]+ (passed|통과)
정본	all	(?<![가-힣])정본
```
