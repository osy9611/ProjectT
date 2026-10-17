# Skill·Effect·Status 회귀 테스트

프로젝트 루트에서 `dotnet run --project Tools/SkillTests`를 실행한다. .NET 9 참조 팩이 필요하다.

실제 코어 `AbilitySystem/`(Skill·Stat의 모든 .cs)와 `SkillAdapter/`(SkillDefinitions, Action_*)를 한 어셈블리로 함께 컴파일한다(InternalsVisibleTo 불필요). 어댑터가 쓰는 `Global.Table`, 테이블(`skillInfo`, `buffInfo`, `DesignEnum`), `ComBaseActor`(설정 가능한 `Status`·`SkillAgent`·`SpawnSerial`을 가진 `IAbilityOwner` 구현), 어댑터 `using`용 빈 네임스페이스만 스텁으로 대체한다.

픽스처: 스킬(`TestSkillAction`)은 정의의 팩토리(`Skill` 도우미)로 만든다. 스턴 규칙이 필요한 테스트는 테이블 기본값과 같은 `StunSkill`을 쓴다. 효과는 정의의 동작 팩토리로 `TestBehavior`(횟수·사유·훅), `OrderBehavior`(훅 순서·인스턴스)를 붙인다. 테이블 행은 실제 enum 값을 쓰므로 운영 enum에 테스트 값을 두지 않는다.

## 현재 결과: 170/170 통과 (M6 포함)

- P0~M1 테스트는 효과 API(`ApplyEffect`, `OnRemove`, `EffectBehavior`)로 옮기고 의도를 유지했다.
- 삭제(효과 레지스트리·`BuffKind` 등록만 검증): `[P0 D2] 버프 첫 사용 등록`, `[P2b] 스탯 버프 4종 StatBuff 등록`, `[P2b D-15] 레지스트리에 없는 버프 종류`, `[P3 상태] Stun은 StateBuff`, `[P4 도트] Dot은 DotBuff`. `[M1] 다른 네임스페이스 Attribute 탐색`은 스킬 부분만 남겼다.
- 만료 정책 A로 의도가 바뀐 테스트: `[P4 Refresh] 만료 판정 뒤 다른 만료 콜백에서 갱신된 주기 버프` → `[M2 만료 A] 다시 적용하면 새로 적용`.
- 도트·피해 테스트는 `DotBuff` 직접 호출 대신 `DamageExecution`을 가진 정의를 적용해 확인한다.
- M3: 상태 테스트는 태그 API(`HasTag`, `OnTagChanged`)와 어댑터 기본값(`SkillDefinitions.StunTag`)으로 옮기고 의도를 유지했다. 추가(10): 태그 등록·비교·default, 여러 태그 부여, 효과 간 겹친 카운트, 스킬별 BlockedTags, 태그 획득 시 해당 스킬만 취소, 발동 직후 취소 일반화, 여러 태그 획득 중 정리, 어댑터 기본 규칙. 태그를 부여하는 Instant는 `[M2 Instant]` 정의 오류 테스트에 포함했다.
- M3 리뷰 추가(8): 콜백 안에서 반납 후 같은 호출에서 다시 스폰(풀 LIFO)할 때 태그 획득 알림 중복·새 스폰 태그 회수(획득 취소, 여러 태그 취소, 제거 알림), 새 스폰 스킬의 취소·갱신(CancelSkillsByTag, OnUpdate, CancelActiveSkills), 빈 태그 이름과 정의의 default 태그 거부.
- M4: 스탯은 어댑터 id(`SkillDefinitions.AtkStat` 등), Hp는 `SetResource`/`GetResource`/`OnResourceChanged`로 옮기고 의도를 유지했다. Hp를 쓰는 Status는 `DefineResource(Hp, MaxHp)`를 호출한다. `RemoveModifiers` 알림 순서가 enum 순서에서 스탯을 처음 쓴 순서로 바뀌어 `[P2a] 변경 이벤트` 기대값을 고쳤다. 추가(15): StatId 등록·비교·default·빈 이름·태그와 별도 저장소, 설정하지 않은 스탯 0, 상한이 다른 자원의 독립 제한, 상한 감소 시 같은 상한 자원 모두 제한(스탯 → 자원 순서), 정의하지 않은 자원·중복 정의 예외, 정리 시 정의 유지, 피해 자원(Mp)·정의 오류·미정의 자원 사전 확인, 스킬별 쿨다운 감소 스탯, 어댑터 id.
- M2 추가(22): Instant(1회 실행·미등록, 잘못된 정의), Infinite(만료 없음, 주기 실행, 핸들 제거), 핸들(Removed, 이전·기본 핸들, 재사용 없음), 이벤트(사유, 재적용 시 없음), 중첩(Ignore, Stack 상한·수정자·피해·갱신·상태·정리 중 교체), 만료 A(순서 무관, 콜백 중 정리), Tick 중 제거, 피해 알림 중 제거, 동작 훅 순서.

- M5: 스킬 레지스트리 테스트는 팩토리 테스트로 바꿨다. 삭제: `[P0 D1] 레지스트리가 구체 스킬 액션을 등록한다`(→ `[M5 어댑터]`), `[M1] 레지스트리는 네임스페이스와 관계없이 Attribute로 스킬 클래스를 찾는다`(→ `[M5 팩토리]`). `[P2b D-15]`는 팩토리 실패 시 미등록으로 의도를 유지했다. 추가(17): 팩토리 1회 생성, ApplyEffects(대상·자신, 출처, 대상 반납 시 중단), 비용(부족, 차감 시점·같은 값, 여러 비용, Status·자원 누락 예외, 정의 거부, 반납 시 다음 스폰 미차감), 이벤트(일반·즉시형·Activate 중 취소 순서, 취소 경로별 1회, 막힌 발동, 발동 직후 태그 취소, 발동 알림 중 태그 취소·반납), 어댑터(skill_type → 팩토리, skill_buffId → Effects).
- M5 리뷰 추가(8): Activate 중 끝난 스킬의 발동 알림·비용 알림 재발동 무시(발동·종료 한 쌍, 종료 알림 재발동), Activate 중 비활성화·취소 태그(첫 발동·목록에 남은 이전 항목) 이벤트 없는 취소, 같은 자원 중복·NaN 비용 거부, 정리 중 발동 무시, 이미 초기화된 실행 인스턴스 거부, 시전자 반납 시 ApplyEffects 중단.
- M6: `[P4 데미지]`의 파괴된 시전자 경우는 `[M6 출처]`로 옮겼다(파괴·반납은 `== null` 대신 `SpawnSerial` 변화로 판단). 추가(2): 적용 뒤 시전자 스폰 번호가 바뀌면 주기 피해 source가 null(반납 후 재스폰한 시전자 포함), ComBaseActor가 아닌 `IAbilityOwner` 소유자·시전자.

`ComBaseActor` 연결(SkillAgent·Status 정리 순서 포함)은 `Tools/LifecycleTests`의 `actor skill:` 테스트가 검증한다.
