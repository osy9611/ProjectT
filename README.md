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

---

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

---
