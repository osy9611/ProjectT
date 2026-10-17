# 턴제 게임 Skill System 테스트

결과: **68/68 통과** (2026-10-11, 작업 트리의 `AbilitySystem` 기준). 같은 시점 `Tools/SkillTests` 170/170.

스킬·효과·유닛 정의와 AI 우선순위는 Unity 샘플과 공유하는 `Client/Assets/Script/Sample/TurnBattle/TurnBattleDefinitions.cs`(Unity 비의존)를 함께 컴파일한다. 같은 시나리오의 Play Mode 판정은 `Tools/PlayModeTests`의 `ProjectTTurnBattleProbe`가 한다.

프로젝트 루트에서 `./Tools/TurnGameTests/run.ps1`을 실행한다. .NET 9 참조 팩이 필요하다. `AbilitySystem/`의 모든 .cs와 공유 정의를 `Game.cs`·`Program.cs`와 한 어셈블리로 컴파일하므로 스텁이 필요 없다.

## 게임 규칙

| 항목 | 규칙 |
|---|---|
| 턴 | 자기 턴 끝에 `TickInternal(1f)` 한 번. 효과 지속·주기와 쿨다운 단위가 1턴 |
| 유닛 | `IAbilityOwner` 구현. 스폰·사망 순서는 `ComBaseActor`와 같음(스폰 번호 → 활성 스킬 취소 → SkillAgent → Status 정리) |
| 피해 | 테스트 전용 `AttackDamage`: `max(Atk × power − Def, 1)`, 시전자가 사라지면 Atk 0 |
| 사망 | Hp 0 이하가 되면 행동·턴 종료 뒤 `Die()` |
| 가드 | 다음 자기 턴 시작에 핸들로 `RemoveEffect` |

## 시나리오

| 시나리오 | 확인하는 모듈 기능 |
|---|---|
| 기본 공격 | Instant 피해, 발동·종료 알림 한 쌍 |
| Mp 비용·쿨다운 | `SkillCost` 차감·부족 시 무시, 쿨다운 중 무시 |
| 독 중첩·갱신 | `Stack` 정책(피해 × 중첩), 지속 재시작, 재적용 시 알림 없음, 만료 |
| 스턴 차단·해제 | `GrantedTags`, `BlockedTags`, 태그 해제 알림 |
| 스턴이 채널링 취소 | 턴을 넘겨 활성인 스킬의 `OnUpdate`, `CancelledByTags` 취소 |
| 공격 버프·갱신 | `Multiply` 수정자 적용·회수, `Refresh` |
| 가드 핸들 제거 | `Infinite` 효과, `RemoveEffect` → `Removed` |
| Mp 재생 동작 | `EffectBehavior.OnExecute`, 자원 상한 제한 |
| 사망 정리·출처 상실·재스폰 | 정리 시 `Released`·태그 비움·발동 무시, `SpawnSerial` 변경 후 source null, 재스폰 재등록 |
| AI 전투 | 2:2 자동 전투 종료, 전 유닛 발동 = 완료 + 취소, 적용 = 제거, 태그 없음, Hp·Mp 범위 |

## 범위 밖

- `ComBaseActor`·Unity Play Mode 연동은 확인하지 않는다(→ `ProjectTTurnBattleProbe`).
- AI 전투에서는 Mp가 15 아래로 내려가지 않아 Meditate가 발동되지 않았다. 채널링은 별도 시나리오로 확인한다.
