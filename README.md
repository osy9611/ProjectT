# ProjectT

ProjectT는 여러 게임 프로젝트에서 함께 쓰는 Unity 기반 모듈 모음이다. 리소스, 테이블, UI, 씬, 풀, 빌드, 스킬 등 공통 기능을 담당 매니저와 공개 진입점으로 나누고, 매니저 생명주기와 오류 처리 규칙을 통일한다.

## 사용 기술

- 언어: C#
- 엔진: Unity
- 비동기 처리: UniTask
- 리소스 관리: Addressables
- 빌드: Jenkins
- 원격 번들 저장소: Firebase Storage

## 만든 배경과 목표

모듈의 첫 형태는 이전 프로젝트 Project J의 클라이언트 코드에서 가져왔다. ProjectT는 그 코드에서 프로젝트 전용 로직을 걷어내고 공통 기능을 다시 설계했다.

프로젝트마다 게임 로직이 다르고, 프로젝트의 목적에 따라 쓰는 개발 도구도 달라진다. 그래도 리소스 관리, 데이터 테이블, UI, 씬 전환, 빌드처럼 어느 프로젝트에나 다시 등장하는 기능이 있다. ProjectT는 이 공통 기능을 프로젝트별 로직과 분리된 모듈로 만들어, 새 프로젝트가 같은 기반 위에서 게임 로직부터 시작할 수 있게 하는 것을 목표로 한다.

ProjectT는 AI를 활용해 코드를 작성할 때 적용할 규칙을 만들고, 여러 방식을 시도해 그 결과를 기록하는 공간이기도 하다. AI가 코드를 작성하는 일이 쉬워질수록 중요한 것은 코드 생성 자체보다 코드가 따를 구조와 규칙을 정하는 일이라고 생각한다. 규칙을 적용하고 다듬으며 사람과 AI가 일관된 구조로 코드를 작성하고, 흔히 "이상한 코드"라고 부르는 결과를 줄이는 방법을 연습한다.

그래서 ProjectT는 다음 기준을 따른다.

- 모듈은 게임 콘텐츠를 포함하지 않는다. 예를 들어 스킬 시스템은 스킬·효과·태그의 조립 구조만 제공하고, 타격 판정 같은 게임 규칙은 프로젝트가 채운다.
- 모듈의 책임과 공개 진입점은 담당 매니저 한 곳에 두고, 게임 코드는 `Global`을 거쳐 매니저를 호출한다.
- 공통 처리는 호출 지점마다 넣지 않고 매니저 생명주기와 공통 진입점에서 수행한다.

| 시스템 | Project J | ProjectT | 위키 |
| --- | --- | --- | --- |
| 테이블 처리 | XML 기반 테이블 관리와 코드 생성 | CodeDom으로 클래스 생성, 다국어 문자열 생성 추가 | [TableGenerator](https://github.com/osy9611/ProjectT/wiki/TableGenerator) |
| 리소스 관리 | Addressables와 기본 풀링, 코루틴 로드 | UniTask 비동기 로드, `ResourceScope` 단위 수명 관리 | [ResourceManager](https://github.com/osy9611/ProjectT/wiki/ResourceManager) |
| UI | Scene, Popup, Element UI 구조 | Static, Dynamic, System, HUD UI 구조 | [UI System](https://github.com/osy9611/ProjectT/wiki/UI-System) |
| 씬 관리 | Addressables 기반 씬 로드 | 씬 전환 중 데이터 유지와 씬 상태 관리 | [SceneManager](https://github.com/osy9611/ProjectT/wiki/SceneManager) |
| 빌드 | Jenkins 빌드 | 빌드 단계 세분화, Firebase용 Addressables 빌드와 업로드 | [Build System](https://github.com/osy9611/ProjectT/wiki/Build-System) |
| 스킬 | switch 문으로 등록하고 직접 실행 | `SkillAgent`, 효과, 태그(`SkillTag`), `Status`로 모듈화, `SkillDefinition`·`EffectDefinition` 팩토리 등록 | [Skill System](https://github.com/osy9611/ProjectT/wiki/Skill-System) |

## 제공하는 모듈

| 모듈 | 진입점 | 하는 일 |
| --- | --- | --- |
| 리소스 | `Global.Resource` | Addressables 비동기 로드, `ResourceScope` 단위 수명 관리 |
| 패치 | `Global.Patch` | 원격 카탈로그 준비와 번들 다운로드 |
| 테이블 | `Global.Data`, `Global.Table` | 테이블 원본에서 생성한 클래스와 다국어 문자열 로드 |
| 풀 | `Global.Pool` | 오브젝트 대여와 반납 |
| 씬 | `Global.Scene` | 씬 전환, 전환 중 데이터 유지와 씬 상태 관리 |
| UI | `Global.UI` | Static, HUD, Dynamic, System UI 구조 |
| 사운드 | `Global.Sound` | 채널별 2D 재생, 풀을 쓰는 공간 음원 재생 |
| 통지 | `Global.Notify` | `NotificationId` 단위 구독과 통지 |
| 입력 | `Global.Input` | 입력 컨텍스트 관리, 씬 전환·UI 포커스·장치 상태에 따른 입력 허용 판정 |
| Actor | `ComBaseActor` | 스폰 생명주기, 입력·스킬 연동, 풀 반납 처리 |
| 스킬 시스템(`AbilitySystem`) | `SkillAgent` | 스킬, 효과, 태그(`SkillTag`), `Status`의 조립과 팩토리 등록 |
| 오류 보고 | `Global.LogException`, `Global.LogError` | 오류 수집, 중복 제한, sink 전달 |
| 빌드 | `Tools/Build/build.ps1`, `Jenkinsfile` | 빌드 단계 실행, Addressables 빌드와 업로드 |
| 테이블 생성기 | `Tools/DesignGenerator` | 테이블 원본에서 클래스와 다국어 문자열 생성 |

매니저의 초기화·종료 순서와 Ready 계약은 [구조](Docs/architecture.md)에 있다.

## 폴더 구성

| 폴더 | 내용 |
| --- | --- |
| `Client/` | Unity 프로젝트. 게임 코드는 `Client/Assets/Script/`, Unity 테스트는 `Client/Assets/Tests/Editor`에 있다 |
| `Tools/` | 콘솔 테스트, 빌드 스크립트(`Tools/Build/build.ps1`), 테이블 생성기(`Tools/DesignGenerator`) |
| `DesignTable/` | 테이블 원본(`DesignTable/Data`)과 생성 도구 실행 파일(`DesignTable/Gen`) |
| `Server/` | 빌드 서버용 Docker 구성(Jenkins, GitLab) |
| `Docs/` | 공통 구조·계약, 일부 시스템의 상세 규칙, 테스트와 남은 과제 |

## 문서 안내

### 독자별로 읽을 순서

| 독자 | 알고 싶은 것 | 읽을 순서 |
| --- | --- | --- |
| 포트폴리오 방문자 | 무엇을 왜 만들었고 결과가 무엇인지 | 이 문서, [위키 Home](https://github.com/osy9611/ProjectT/wiki), [위키 Refactoring](https://github.com/osy9611/ProjectT/wiki/Refactoring), 위키 시스템 페이지의 "설계 결정" |
| 처음 온 개발자 | 구조와 시스템 사용 계약, 코드 위치 | 이 문서, [구조](Docs/architecture.md), [위키 Home](https://github.com/osy9611/ProjectT/wiki), 필요한 시스템 페이지, [테스트](Docs/testing.md) |
| 유지보수자(사람·에이전트) | 작업 규칙, 검증 방법, 남은 과제 | [작업 규칙](AGENTS.md), [리팩토링 판정 기준](REFACTORING_CRITERIA.md), [테스트](Docs/testing.md), `Tools/*/README.md`, [남은 과제](Docs/open-issues.md) |

### 문서 지도

시스템별 현재 사용 계약은 위키에서 찾고, 여러 시스템에 공통된 규칙과 검증 기준은 `Docs/`에서 확인한다. 매니저 생명주기, Actor, AbilitySystem, 오류 보고의 상세 규칙은 연결된 `Docs/` 문서가 정본이다. 위키는 각 시스템의 구조·사용 계약·연동 흐름과 설계 이유·변경 이력을 설명하고, 상세 규칙은 해당 `Docs/` 문서로 연결한다.

| 문서 | 내용 |
| --- | --- |
| [Docs/architecture.md](Docs/architecture.md) | 진입점 `Global`, 매니저 9개의 초기화·종료 순서, 매니저별 공개 진입점, 어셈블리, 빌드 |
| [Docs/actor-lifecycle.md](Docs/actor-lifecycle.md) | `ComBaseActor`의 상태와 전이, 스폰과 스폰 종료, 풀·Scope 연동 |
| [Docs/ability-system.md](Docs/ability-system.md) | 스킬 시스템(`AbilitySystem`)의 스킬·효과·태그·Status 규칙 |
| [Docs/error-reporting.md](Docs/error-reporting.md) | `ErrorReporter` 구조와 오류 보고 경계, catch 위치 목록 |
| [Docs/testing.md](Docs/testing.md) | 테스트 실행법, 기준선, 시스템별 테스트 |
| [Docs/glossary.md](Docs/glossary.md) | 문서에서 쓰는 용어와 쓰지 않는 말 |
| [Docs/open-issues.md](Docs/open-issues.md) | 남은 지적과 보류한 결정 |
| [AGENTS.md](AGENTS.md) | 작업 규칙 |
| [REFACTORING_CRITERIA.md](REFACTORING_CRITERIA.md) | 리팩토링 착수와 판정 기준 |

### 위키: 시스템 계약과 설계 이력

| 분류 | 문서 |
| --- | --- |
| 진입·전체 이력 | [Home](https://github.com/osy9611/ProjectT/wiki), [Refactoring](https://github.com/osy9611/ProjectT/wiki/Refactoring) |
| 기반 시스템 | [Manager Lifecycle](https://github.com/osy9611/ProjectT/wiki/Manager-Lifecycle), [ResourceManager](https://github.com/osy9611/ProjectT/wiki/ResourceManager), [SceneManager](https://github.com/osy9611/ProjectT/wiki/SceneManager), [Pool System](https://github.com/osy9611/ProjectT/wiki/Pool-System), [Error Reporter](https://github.com/osy9611/ProjectT/wiki/Error-Reporter) |
| 게임 시스템 | [UI System](https://github.com/osy9611/ProjectT/wiki/UI-System), [Input System](https://github.com/osy9611/ProjectT/wiki/Input-System), [Notification](https://github.com/osy9611/ProjectT/wiki/Notification), [Sound](https://github.com/osy9611/ProjectT/wiki/Sound), [Actor](https://github.com/osy9611/ProjectT/wiki/Actor), [Skill System](https://github.com/osy9611/ProjectT/wiki/Skill-System) |
| 도구 | [TableGenerator](https://github.com/osy9611/ProjectT/wiki/TableGenerator), [Build System](https://github.com/osy9611/ProjectT/wiki/Build-System) |
| 제거된 기능의 이력 | [Wiki Home](https://github.com/osy9611/ProjectT/wiki)의 이력 분류에서 Client Local Storage 페이지로 이동. 현재 런타임에는 구현되어 있지 않음 |

## 참고 링크와 시연

- [GitHub 위키](https://github.com/osy9611/ProjectT/wiki)
- [Project J 저장소](https://github.com/osy9611/ProjectJ)
- [시연 영상](https://youtu.be/zOuUQhBBtfM)

<a href="https://github.com/osy9611/ProjectT/wiki/Skill-System">
  <img src="https://github.com/user-attachments/assets/c20c3c32-73c2-46ed-aa15-b789701f6737" width="50%" alt="스킬 시스템 시연">
</a>
