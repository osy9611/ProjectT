# ProjectT

ProjectT는 Unity 게임 프로젝트 Project J의 클라이언트 시스템을 다시 설계한 프로젝트다. 리소스 로드, 테이블 생성, UI, 씬 전환, 빌드 자동화, 스킬 시스템을 모듈로 나누고 매니저 생명주기와 오류 처리 규칙을 하나로 맞췄다. 각 변경은 Unity 없이 도는 콘솔 테스트와 Unity 테스트로 전후 동작을 비교해 검증한다.

- 엔진과 언어는 Unity, C#이다. 비동기는 UniTask, 리소스는 Addressables를 쓴다.
- 빌드는 Jenkins가 실행하고, 원격 번들은 Firebase Storage에 올린다.

## 독자별로 읽을 순서

| 독자 | 알고 싶은 것 | 읽을 순서 |
| --- | --- | --- |
| 포트폴리오 방문자 | 무엇을 왜 바꿨고 결과가 무엇인지 | 이 문서, [위키 Home](https://github.com/osy9611/ProjectT/wiki), [위키 Refactoring](https://github.com/osy9611/ProjectT/wiki/Refactoring), 위키 시스템 페이지의 "설계 결정" |
| 처음 온 개발자 | 구조와 계약, 코드 위치 | 이 문서, [구조](Docs/architecture.md), 필요한 시스템의 `Docs/*.md`, [테스트](Docs/testing.md) |
| 유지보수자(사람·에이전트) | 작업 규칙, 검증 방법, 남은 과제 | [작업 규칙](AGENTS.md), [리팩토링 판정 기준](REFACTORING_CRITERIA.md), [테스트](Docs/testing.md), `Tools/*/README.md`, [남은 과제](Docs/open-issues.md) |

## 문서 지도

저장소의 `Docs/`가 현재 코드 기준의 정본이다. 위키는 설계 이유와 이력을 요약하고 세부 계약은 `Docs/`로 링크한다.

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

## 폴더 구성

| 폴더 | 내용 |
| --- | --- |
| `Client/` | Unity 프로젝트. 게임 코드는 `Client/Assets/Script/`, Unity 테스트는 `Client/Assets/Tests/Editor`에 있다 |
| `Tools/` | 콘솔 테스트, 빌드 스크립트(`Tools/Build/build.ps1`), 테이블 생성기(`Tools/DesignGenerator`) |
| `DesignTable/` | 테이블 원본(`DesignTable/Data`)과 생성 도구 실행 파일(`DesignTable/Gen`) |
| `Server/` | 빌드 서버용 Docker 구성(Jenkins, GitLab) |
| `Docs/` | 현재 코드 기준의 설계 문서 |

## Project J에서 바뀐 것

| 시스템 | Project J | ProjectT | 위키 |
| --- | --- | --- | --- |
| 테이블 처리 | XML 기반 테이블 관리와 코드 생성 | CodeDom으로 클래스 생성, 다국어 문자열 생성 추가 | [TableGenerator](https://github.com/osy9611/ProjectT/wiki/TableGenerator) |
| 리소스 관리 | Addressables와 기본 풀링, 코루틴 로드 | UniTask 비동기 로드, `ResourceScope` 단위 수명 관리 | [ResourceManager](https://github.com/osy9611/ProjectT/wiki/ResourceManager) |
| UI | Scene, Popup, Element UI 구조 | Static, Dynamic, System UI 구조 | [UI System](https://github.com/osy9611/ProjectT/wiki/UI-System) |
| 씬 관리 | Addressables 기반 씬 로드 | 씬 전환 중 데이터 유지와 씬 상태 관리 | [SceneManager](https://github.com/osy9611/ProjectT/wiki/SceneManager) |
| 빌드 | Jenkins 빌드 | 빌드 단계 세분화, Firebase용 Addressables 빌드와 업로드 | [Build System](https://github.com/osy9611/ProjectT/wiki/Build-System) |
| 스킬 | switch 문으로 등록하고 직접 실행 | `SkillAgent`, 효과, 태그(`SkillTag`), `Status`로 모듈화, `SkillDefinition`·`EffectDefinition` 팩토리 등록 | [Skill System](https://github.com/osy9611/ProjectT/wiki/Skill-System) |

## 링크

- [GitHub 위키](https://github.com/osy9611/ProjectT/wiki)
- [Project J 저장소](https://github.com/osy9611/ProjectJ)
- [시연 영상](https://youtu.be/zOuUQhBBtfM)

<a href="https://github.com/osy9611/ProjectT/wiki/Skill-System">
  <img src="https://github.com/user-attachments/assets/c20c3c32-73c2-46ed-aa15-b789701f6737" width="50%" alt="스킬 시스템 시연">
</a>
