# PlayModeTests 실행

이 폴더에는 README만 있다. 테스트 코드는 `Client/Assets/Tests/Editor`, 프로브는 `Client/Assets/Tests/Probes`에 있다. Unity 에디터에서 Window > General > Test Runner를 열고 EditMode 탭에서 아래 테스트를 실행한다. 테스트가 씬을 바꾸므로 열린 씬을 먼저 저장한다. 각 테스트는 Play Mode를 빠져나온 뒤 프로브 결과를 단언하며, 모두 초록색이면 통과다. 기대값은 각 테스트 코드의 단언에 있다. 전체 기준선과 수치는 [testing.md](../../Docs/testing.md)에 있다.

## 무엇을 검사하나

| 테스트 | 하는 일 |
| --- | --- |
| `ProjectTUIPlayModeTests.UIContainerStackAndNativeActivation` | 빈 씬에 원격 리소스와 데이터 로드를 끈 `Global`을 띄우고 `ProjectTUIPlayProbe.Verify()`를 끝까지 실행한다. `PROJECTT_UI_PLAYMODE_PASS` 로그를 기대한다. |
| `ProjectTRuntimePlayModeTests.RuntimeFixtures` | 빈 씬에 데이터 로드를 켠 `Global`을 띄우고 `ProjectTRuntimePlayProbe.Run()`을 실행한다. 샘플 없이 프로브가 만든 픽스처와 테스트 전용 씬 `TestSceneA`·`TestSceneB`로 확인하며, 프로브 요약 문자열을 단언한다. |
| `RefactoringProbeTests.UISoundToolPrefabEdits` | SetUp이 `Assets/Tests/RefactoringFixtures`에 프리팹 픽스처를 만들고 TearDown이 지운다. `RefactoringProbe.PrefabSoundEdits()`의 검사가 모두 실행됐는지 단언한다. |

`ProjectTRuntimePlayProbe`는 actor, skill, input, ui, scene, cleanup 단계 순서로 실행한다. Actor는 프로브가 만든 비활성 템플릿을 `Global.Pool`로 대여하고, 위젯은 프리팹 대신 `Global.UI`의 컨테이너에 직접 등록한다. scene 단계는 SceneData의 테스트 전용 씬 `TestSceneA`·`TestSceneB`를 쓴다. 입력은 가상 Gamepad 상태 이벤트로 넣는다. 한 단계가 예외로 끝나도 다음 단계를 계속 실행한다.

`AutoTickSkills`가 false인 Actor의 수동 틱 경로는 콘솔 `LifecycleTests`의 `ActorSkillTests`가 확인한다.

## 결과 읽기

- `RuntimeFixtures`는 의도한 예외 2건(`Scene transition is unavailable.`, `[Probe] TestSceneB OnEnter failure test`)을 `LogAssert.Expect`로 처리한다. 다른 Error와 Exception 로그는 테스트를 실패시킨다.
- 실패하면 테스트 메시지에 `[Runtime] FAIL`, `ERROR LOG` 줄이 담긴다.
- `ProjectTRuntimePlayProbe`의 전체 결과는 콘솔의 `[Runtime] PASS`, `[Runtime] FAIL`, `[Runtime] NOTE` 줄과 `PROJECTT_RUNTIME_PLAYMODE_RESULT` 줄에 남는다.

`ProjectTRuntimePlayProbe`는 실행 중에만 `backgroundBehavior=IgnoreFocus`, `editorInputBehaviorInPlayMode=AllDeviceInputAlwaysGoesToGameView`인 Input System 설정 복제본을 쓰고, 끝나면 원래 설정으로 되돌린다. 에셋은 수정하지 않는다. 게임 뷰에 포커스가 없으면 원래 설정에서 동기 `InputSystem.Update()`가 Editor 업데이트로 실행되어 가상 Gamepad 이벤트를 처리하지 않기 때문이다. 포커스는 실행 도중에도 바뀌므로 프로브는 시작 시점의 포커스와 관계없이 항상 복제본을 쓴다.

## 검증하지 않는 것

- 실제 UI 프리팹 로드와 같은 타입 비동기 생성의 공유. 콘솔 `UITests`가 확인한다.
- EventSystem의 자동 포인터 라우팅과 Addressables 원격 로딩.
- 플레이어 빌드와 모바일 기기 동작.

## 관련 문서

- Test Runner 테스트 목록과 도메인 리로드 규칙: [testing.md의 Unity 테스트](../../Docs/testing.md#unity-테스트)
- [actor-lifecycle.md](../../Docs/actor-lifecycle.md)
- [ability-system.md](../../Docs/ability-system.md)
