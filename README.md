# ProjectT - ProjectJ 프로젝트 모듈 개선 프로젝트

ProjectT는 Project J의 기존 시스템을 최적화하고 유지보수성을 향상하기 위해 진행된 프로젝트입니다.

이 프로젝트에서는 리소스 관리, 데이터 테이블 처리, UI 시스템, 빌드 자동화를 개선하여 더 효율적인 구조와 높은 확장성을 목표로 작업했습니다.

## 프로젝트 주요 내용 ##
- Project J에서 사용한 주요 시스템을 개선 및 확장하여 최적화
- 비동기 처리, UI 관리, 빌드 자동화 등의 성능 및 유지보수성 개선
- 신규 기능 모듈화

## 기술 스택 ##
✔ **Engine:** Unity  
✔ **Language:** C#  
✔ **Build System:** Firebase, Jenkins

## 관련 문서 ##
- [ GitHub 위키](https://github.com/osy9611/ProjectT/wiki)
- [ Project J](https://github.com/osy9611/ProjectJ)

## 영상 링크 ##
- [ 영상 링크 ](https://youtu.be/zOuUQhBBtfM)

---

## **Project J vs Project T 비교**
|   | **Project J** | **Project T (개선 프로젝트)** |
|---|--------------|----------------|
| **데이터 처리** | XML 기반 테이블 관리 & 코드 생성 | **CodeDom 기반 자동화** |
| **리소스 관리** | Addressable 사용 및 기본 풀링 | **UniTask 적용, 비동기 로딩 개선** |
| **UI 시스템** | Scene / Popup / Element UI 구조 | **Static / Dynamic / System UI 구조로 변경** |
| **빌드 자동화** | Jenkins 기반 빌드 자동화 | **Firebase 연동, 세분화된 빌드 프로세스** |
| **씬 관리** | Addressable 기반 씬 로딩 | **데이터 유지 및 상태 관리 추가** |
| **스킬 시스템** | Switch 기반 등록 + 직접 실행 | **Attribute 자동 등록 + 모듈 분리 (Action/Buff Controller)** |

## 주요 시스템 개요

### TableGenerator
- 테이블 데이터 자동 처리 과정 개선
- CodeDom 적용으로 클래스 생성 가독성 및 유지보수성 향상
- 다국어 지원 기능 추가
- **[더 보기](https://github.com/osy9611/ProjectT/wiki/TableGenerator)**

### ResourceManager
- 코루틴 -> UniTask 변환으로 비동기 로딩 개선
- Addressable 기반 리소스 로딩 개선
- **[더 보기](https://github.com/osy9611/ProjectT/wiki/ResourceManager)**

### Buid System
- Firebase를 활용한 Addressable Build 자동화
- 기본 Jenkins 빌드 프로세스 세분화
- **[더 보기](https://github.com/osy9611/ProjectT/wiki/Build-System)**

### UI 시스템
- UI 구조를 Static / Dynamic / System으로 분리하여 효율적 관리
- **[더 보기](https://github.com/osy9611/ProjectT/wiki/UI-System)**

### SceneManager
- 씬 로딩 최적화 및 씬 전환 중 데이터 유지 기능 추가
- Addressable 기반 씬 관리 구조 개선
- **[더 보기](https://github.com/osy9611/ProjectT/wiki/SceneManager)**

### SkillSystem
- 기존 Switch 기반 스킬/버프 로직을 Attribute 기반 자동 등록 구조로 변경
- BaseAction/BaseBuff 기반으로 스킬과 버프를 모듈화
- **[더 보기](https://github.com/osy9611/ProjectT/wiki/Skill-System)**

  <a href="https://github.com/osy9611/ProjectT/wiki/Skill-System">
    <img src="https://github.com/user-attachments/assets/c20c3c32-73c2-46ed-aa15-b789701f6737" width="50%" height="50%" alt="Skill2 GIF">
  </a>  

---
