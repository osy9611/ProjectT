# SkillTests 실행

프로젝트 루트에서 `dotnet run --project Tools/SkillTests`를 실행한다. .NET 9 SDK가 필요하고 UniTask는 쓰지 않는다. 출력 마지막 줄 `<통과 수>/<전체 수> passed`에서 두 수가 같으면 통과다. 전체 기준선과 수치는 [architecture.md의 테스트 절](../../Docs/architecture.md#테스트)에 있다.

## 무엇을 검사하나

러너는 다음 제품 소스를 한 어셈블리로 함께 컴파일한다. 같은 어셈블리이므로 `InternalsVisibleTo`가 필요 없다.

- `AbilitySystem/`의 모든 `.cs`(Skill, Stat).
- `SkillAdapter/`의 `SkillDefinitions`, `Action_Melee`, `Action_Normal`, `Action_Range`.
- `Automation/Enum/DesignEnum.cs`. 테이블 행은 실제 enum 값을 쓰므로 운영 enum에 테스트 값을 두지 않는다.

`Stubs.cs`는 어댑터가 쓰는 `Global.Table`, 테이블 행(`skillInfo`, `buffInfo`), `ComBaseActor`만 대체한다. 가짜 `ComBaseActor`는 `Status`, `SkillAgent`, `SpawnSerial`을 설정할 수 있는 `IAbilityOwner` 구현이다.

픽스처는 `Program.cs`에 있다.

- 스킬은 `Skill` 도우미로 정의를 만들고, 정의의 팩토리가 `TestSkillAction`을 만든다. 스턴 규칙이 필요한 테스트는 테이블 기본값과 같은 `StunSkill`을 쓴다.
- 효과는 정의의 동작 팩토리로 `TestBehavior`(호출 횟수, 사유, 훅)나 `OrderBehavior`(훅 순서, 인스턴스)를 붙인다.

검사 범위는 다음과 같다.

- 스킬 발동, 쿨다운, 취소, 발동과 종료 이벤트 순서.
- 효과 적용, 중첩, 만료, 주기 실행, 핸들 제거.
- 태그 부여와 차단, 태그 획득에 따른 스킬 취소.
- 스탯, 자원(Hp 등), 비용 차감.
- 시전자 반납과 재스폰 뒤 피해 출처 처리(`SpawnSerial`).
- 스킬과 효과 호출 중 정리 거부.
- 어댑터의 테이블 변환(`skill_type`에서 팩토리, `skill_buffId`에서 효과).

## 검증하지 않는 것

- 실제 `ComBaseActor`와의 연결(`SkillAgent`와 `Status` 정리 순서 포함)은 `Tools/LifecycleTests`의 `actor skill:` 테스트가 검증한다.
- Unity 프레임에서의 발동, 쿨다운, 버프, 스턴은 [PlayModeTests 실행](../PlayModeTests/README.md)의 Play Mode 테스트에서 확인한다.

## 관련 문서

- [ability-system.md](../../Docs/ability-system.md). 테스트 구성은 [ability-system.md의 테스트 절](../../Docs/ability-system.md#테스트)에 있다.
