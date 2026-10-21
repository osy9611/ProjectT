# PlayModeTests 실행

이 폴더에는 README만 있다. 테스트 코드는 `Client/Assets/Tests/Editor`, 프로브는 `Client/Assets/Tests/Probes`에 있다. Unity 에디터에서 Window > General > Test Runner를 열고 EditMode 탭에서 아래 테스트를 실행한다. 테스트가 씬을 바꾸므로 열린 씬을 먼저 저장한다. 각 테스트는 Play Mode를 빠져나온 뒤 프로브 결과를 단언하며, 모두 초록색이면 통과다. 기대값은 각 테스트 코드의 단언에 있다. 전체 기준선과 수치는 [architecture.md의 테스트 절](../../Docs/architecture.md#테스트)에 있다.

## 무엇을 검사하나

| 테스트 | 하는 일 |
| --- | --- |
| `ProjectTSamplePlayModeTests.P0FeatureSample` | FeatureSample 씬을 열고 Play Mode에서 `Global.IsReady`를 기다린 뒤 `ProjectTP0PlayProbe.Run()`을 실행한다. 프로브 요약 문자열(`pass=... fail=...`)을 단언한다. |
| `ProjectTSamplePlayModeTests.TurnBattleSample` | TurnBattleSample 씬에서 `ProjectTTurnBattleProbe.Run()`을 실행하고 프로브 요약 문자열을 단언한다. |
| `ProjectTUIPlayModeTests.UIContainerStackAndNativeActivation` | 빈 씬에 원격 리소스와 데이터 로드를 끈 `Global`을 띄우고 `ProjectTUIPlayProbe.Verify()`를 끝까지 실행한다. `PROJECTT_UI_PLAYMODE_PASS` 로그를 기대한다. |
| `RefactoringProbeTests.UISoundToolPrefabEdits` | SetUp이 `Assets/Tests/RefactoringFixtures`에 프리팹 픽스처를 만들고 TearDown이 지운다. `RefactoringProbe.PrefabSoundEdits()`의 검사가 모두 실행됐는지 단언한다. |

`ProjectTP0PlayProbe`는 FeatureSample 씬의 샘플 경로를 actor, skill, input, ui, scene, cleanup, ui-probe 단계 순서로 실행한다. 샘플 버튼 함수는 internal 메서드로 직접 호출하고, 입력은 가상 Gamepad 상태 이벤트로 넣는다. 마지막 ui-probe 단계는 같은 세션에서 `ProjectTUIPlayProbe.Verify()`를 실행한다. 한 단계가 예외로 끝나도 다음 단계를 계속 실행한다.

`ProjectTTurnBattleProbe`는 `TurnBattleRunner`의 공개 API로 턴제 전투 시나리오를 실제 `ComBaseActor`(`TurnUnit`)와 풀 경로로 실행한다. 확인하는 Unity 전용 항목은 다음과 같다.

- `AutoTickSkills`가 false이면 프레임이 지나도 스턴과 쿨다운이 유지되고 `TickSkills`(EndTurn)로만 진행한다.
- `SetActive(false)`가 활성 채널링을 취소하고, 비활성 중 `TickSkills`는 진행하지 않는다.
- 사망 시 `Global.Pool.Return`으로 `SpawnSerial`이 바뀌고, 재대여한 같은 인스턴스가 깨끗한 상태로 스킬을 다시 등록한다.

## 결과 읽기

- `P0FeatureSample`은 의도한 예외 2건(`Scene transition is unavailable.`, `[Sample] SampleSceneB OnEnter failure test`)을 `LogAssert.Expect`로 처리한다. 다른 Error와 Exception 로그는 테스트를 실패시킨다.
- 실패하면 테스트 메시지에 `[P0] FAIL`, `[Turn] FAIL`, `ERROR LOG` 줄이 담긴다.
- `ProjectTP0PlayProbe`의 전체 결과는 콘솔의 `[P0] PASS`, `[P0] FAIL`, `[P0] NOTE` 줄과 `PROJECTT_P0_PLAYMODE_RESULT` 줄에 남는다.

`ProjectTP0PlayProbe`는 실행 중에만 `backgroundBehavior=IgnoreFocus`, `editorInputBehaviorInPlayMode=AllDeviceInputAlwaysGoesToGameView`인 Input System 설정 복제본을 쓰고, 끝나면 원래 설정으로 되돌린다. 에셋은 수정하지 않는다. 게임 뷰에 포커스가 없으면 원래 설정에서 동기 `InputSystem.Update()`가 Editor 업데이트로 실행되어 가상 Gamepad 이벤트를 처리하지 않기 때문이다. 포커스는 실행 도중에도 바뀌므로 프로브는 시작 시점의 포커스와 관계없이 항상 복제본을 쓴다.

## 검증하지 않는 것

- IMGUI 버튼 클릭. 턴제 샘플의 버튼은 `pendingAction`으로 같은 공개 API를 호출하며, 프로브는 버튼을 누르지 않는다.
- EventSystem의 자동 포인터 라우팅과 Addressables 원격 로딩.
- 플레이어 빌드와 모바일 기기 동작.

## 관련 문서

- Test Runner 테스트 목록과 도메인 리로드 규칙: [architecture.md의 Unity Test Runner](../../Docs/architecture.md#unity-test-runner)
- [actor-lifecycle.md](../../Docs/actor-lifecycle.md)
- [ability-system.md](../../Docs/ability-system.md)
