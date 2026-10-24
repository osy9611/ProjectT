# 테스트

변경 전후에 같은 테스트를 실행하고 결과를 [기준선](#기준선)과 비교한다. 이 문서가 테스트 실행법과 기준선 수치의 상세 문서고, 다른 문서는 수치를 적지 않고 이 문서로 링크한다.

테스트는 두 종류다. 콘솔 테스트(`Tools/` 아래 dotnet 프로젝트)는 Unity 없이 제품 소스를 스텁과 함께 컴파일해 실행한다. Unity 테스트(`Client/Assets/Tests/Editor`)는 실제 에디터에서 Play Mode 경로를 실행하고, 검사 본문은 프로브(`Client/Assets/Tests/Probes`)에 있다.

## 변경 전후로 테스트를 비교한다

1. [시스템별 테스트](#시스템별-테스트) 표에서 바꿀 시스템에 해당하는 테스트를 찾는다.
2. 변경 전에 [콘솔 테스트](#콘솔-테스트)를 실행한다. 씬, 풀, UI, 입력에 닿는 변경이면 [Unity 테스트](#unity-테스트) 4개도 실행한다.
3. 변경한 뒤 같은 테스트를 다시 실행하고, 결과를 [기준선](#기준선) 표와 비교한다.
4. 테스트가 실패하면 기대값을 고쳐 맞추지 않고, 출력을 그대로 결함 후보로 보고한다. 보고 규칙은 [작업 규칙](../AGENTS.md)의 "검증과 보고"를 따른다.

## 기준선

콘솔 테스트 수치는 2026-10-21 `run-tests.ps1` 실행 결과이고, Unity 테스트 수치는 테스트 코드의 단언에 적힌 기대값이다. 수치를 바꾸는 커밋에서 이 표도 함께 고친다.

| 실행 | 기준선 | 다시 확인하는 방법 |
| --- | --- | --- |
| `run-tests.ps1` 전체 | `ALL PASSED: 10 test runs` | 출력의 마지막 줄 |
| `LifecycleTests` | `PASS` 252줄, `ALL PASSED` | 이 프로젝트 출력에서 `PASS `로 시작하는 줄 수. 마지막 줄에는 개수가 없다 |
| `NotificationTests` | `13/13 notification tests passed` | 프로젝트 출력의 마지막 줄 |
| `SkillTests` | `151/151 passed` | 프로젝트 출력의 마지막 줄 |
| `SoundTests` | `43/43 sound tests passed` | 프로젝트 출력의 마지막 줄 |
| `UITests` | `42/42 passed` | 프로젝트 출력의 마지막 줄 |
| `RefactoringTests/Downloader` | `ALL PASSED: 12 downloader scenarios` | 프로젝트 출력의 마지막 줄 |
| `RefactoringTests/FirebaseCache` | `ALL PASSED: 19 firebase scenarios` | 프로젝트 출력의 마지막 줄 |
| `RefactoringTests/UISoundTool` | `21/21 UISoundTool scenarios passed` | 프로젝트 출력의 마지막 줄 |
| `RefactoringTests/UploadFlow` | `ALL PASSED: 3 upload scenarios` | 프로젝트 출력의 마지막 줄 |
| `RefactoringTests/run-build.ps1` | `ALL PASSED: 11 build flow scenarios`, `ALL PASSED: 12 build argument scenarios` | 스크립트 출력의 두 `ALL PASSED` 줄 |
| `ProjectTSamplePlayModeTests.P0FeatureSample` | `pass=103 fail=0` | `ProjectTSamplePlayModeTests.cs`의 `AssertSummary` 인자 |
| `ProjectTSamplePlayModeTests.TurnBattleSample` | `pass=61 fail=0` | `ProjectTSamplePlayModeTests.cs`의 `AssertSummary` 인자 |
| `ProjectTUIPlayModeTests.UIContainerStackAndNativeActivation` | `ProjectTUIPlayProbe.Verify()`가 끝까지 실행되고 `PROJECTT_UI_PLAYMODE_PASS` 로그를 남긴다 | `ProjectTUIPlayModeTests.cs`의 `Assert.That(verified, Is.True, ...)`와 `LogAssert.Expect` |
| `RefactoringProbeTests.UISoundToolPrefabEdits` | 검사 14개 | `RefactoringProbeTests.cs`의 `Assert.That(RefactoringProbe.Checks, Is.EqualTo(14))` |

## 콘솔 테스트

저장소 루트에서 다음 명령을 실행한다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/run-tests.ps1
```

`run-tests.ps1`은 아래 9개 프로젝트를 `dotnet run`으로 실행하고, 이어서 `RefactoringTests/run-build.ps1`을 실행한다. 모두 통과하면 `ALL PASSED: 10 test runs`를 출력한다. 하나라도 실패하면 `FAILED:` 뒤에 실패 목록을 출력하고 exit 1로 끝난다.

| 프로젝트 | `UseUniTask` | `UseUnityStubs` | `UseRealGlobal` |
| --- | --- | --- | --- |
| `LifecycleTests/LifecycleTests.csproj` | O | O | O |
| `NotificationTests/NotificationTests.csproj` | O | O | O |
| `SkillTests/SkillTests.csproj` | | | |
| `SoundTests/SoundTests.csproj` | O | O | |
| `UITests/UITests.csproj` | O | O | |
| `RefactoringTests/Downloader/Downloader.csproj` | O | | |
| `RefactoringTests/FirebaseCache/FirebaseCache.csproj` | O | O | |
| `RefactoringTests/UISoundTool/UISoundTool.csproj` | | O | |
| `RefactoringTests/UploadFlow/UploadFlow.csproj` | | | |

`run-build.ps1`은 `RefactoringTests/BuildFlow/BuildFlow.csproj`를 빌드한 뒤 BuildMgr 흐름 특성 검사와 명령줄 인자 경우 12개를 실행한다. `Tools/Tests.slnx`는 이 10개 csproj를 IDE에서 여는 솔루션이다.

프로젝트 하나만 실행하려면 그 csproj나 스크립트를 지정한다.

```powershell
dotnet run --project Tools/LifecycleTests/LifecycleTests.csproj
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/RefactoringTests/run-build.ps1
```

모든 테스트 csproj는 `Tools/Tests.Build.props`를 명시적으로 Import한다. `Directory.Build.props`로 두면 `Tools/DesignGenerator`의 프로젝트까지 이 설정을 가져가 깨지기 때문이다. props는 net9.0 Exe와 `ProductScript`(`Client/Assets/Script/`) 경로를 정하고, 세 묶음을 프로젝트가 골라 켜게 한다.

| 묶음 | 컴파일하는 것 |
| --- | --- |
| `UseUniTask` | `Client/Library/PackageCache/com.cysharp.unitask@*/Runtime`의 소스. PlayerLoop·UnityEngine에 의존하는 파일은 제외한다. Unity 프로젝트를 한 번도 열지 않아 패키지가 없으면 "Open the Unity project once to restore the UniTask package." 오류로 빌드가 멈춘다 |
| `UseUnityStubs` | `Tools/Shared/UnityStubs/*.cs`의 공용 UnityEngine 스텁 |
| `UseRealGlobal` | 실제 `Global.cs`, `ErrorCollector.cs`, `ManagerHost.cs`, `ManagerBase.cs`, `ErrorReport/*.cs`와 `Tools/Shared/GlobalTestSupport.cs`, `UtilFuncStub.cs` |

`UseRealGlobal`을 켠 프로젝트는 `Global.Managers.cs`를 컴파일하지 않는다. 대신 `GlobalTestSupport.cs`가 `RegisterManagers`와 테스트용 진입점(`BindForTests`, `InitForTests`, `UpdateForTests`, `InitializeForTests`, `ResetStaticsForTests`)을 같은 partial 클래스로 제공하고, 각 프로젝트가 쓰는 접근자만 담은 partial을 둔다(예: `LifecycleTests/GlobalAccessors.cs`). private 멤버를 리플렉션 없이 호출하므로, 제품 쪽 이름이 바뀌면 런타임이 아니라 빌드에서 깨진다. `SoundTests`, `UITests`, `SkillTests`는 아직 각 `Stubs.cs`의 가짜 `Global`을 쓴다.

`Tools/RefactoringTests/run-table-generator.ps1`(`TableGeneratorTests.cs`)은 `run-tests.ps1`에 포함되지 않는다. 생성기 경로(`-GeneratorPath`)와 출력 경로(`-OutputPath`)를 받아 따로 실행한다.

## Unity 참조 컴파일

`Tools/SoundTests/compile-unity.ps1`은 Unity 에디터를 띄우지 않고 실제 Unity 참조로 `Assembly-CSharp`와 `Assembly-CSharp-Editor`를 컴파일한다. `Client/Library/Bee/artifacts`의 최신 컴파일러 응답 파일(`.rsp`)을 복사해 출력 경로를 `Tools/SoundTests/bin/unity-compile`로 바꾸고, Unity에 포함된 Roslyn으로 컴파일한다. 응답 파일이 마지막 Unity 실행 이후의 변경을 반영하지 못하는 경우를 보정한다.

- asmdef 시절의 `ProjectT.AbilitySystem` dll 참조를 지운다.
- `Input`, `Managers`, `SkillAdapter`, `AbilitySystem` 폴더의 새 소스를 추가한다.
- 삭제된 소스를 목록에서 뺀다.

Unity 프로젝트를 한 번 열어 Bee 응답 파일이 있어야 실행된다. 기본 Unity 경로(`6000.3.14f1`)는 `-UnityData`로 바꾼다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/SoundTests/compile-unity.ps1
```

## Unity 테스트

`Client/Assets/Tests/Editor`에 EditMode 테스트 4개가 있다. **Window > General > Test Runner**의 **EditMode** 탭에서 실행한다. 테스트는 씬을 바꾸므로 열린 씬을 먼저 저장한다. 기대값은 [기준선](#기준선) 표에 있다.

| 테스트 | 하는 일 |
| --- | --- |
| `ProjectTSamplePlayModeTests.P0FeatureSample` | FeatureSample 씬에서 Play Mode에 들어가 `Global.IsReady`를 기다린 뒤 `ProjectTP0PlayProbe.Run()`을 실행한다. Play Mode를 나온 뒤 프로브 요약을 단언한다 |
| `ProjectTSamplePlayModeTests.TurnBattleSample` | TurnBattleSample 씬에서 `ProjectTTurnBattleProbe.Run()`을 실행하고 프로브 요약을 단언한다 |
| `ProjectTUIPlayModeTests.UIContainerStackAndNativeActivation` | 빈 씬에 `Global`(`UseRemoteResource`, `LoadData` 끔)을 만들고 `ProjectTUIPlayProbe.Verify()`를 끝까지 실행한다 |
| `RefactoringProbeTests.UISoundToolPrefabEdits` | 프리팹 픽스처를 만들어 `RefactoringProbe.PrefabSoundEdits()`를 실행하고 검사 수를 단언한다 |

asmdef가 없으므로 `Tests/Editor`의 테스트는 `Assembly-CSharp-Editor`로 컴파일된다. 프로브(`Tests/Probes/*.cs`)는 Editor 폴더 밖에 있어 `Assembly-CSharp`로 컴파일되고, 파일 전체가 `#if UNITY_EDITOR`로 감싸여 플레이어 빌드에서 빠진다. P0 테스트는 의도한 예외 2건(`Scene transition is unavailable.`, `[Sample] SampleSceneB OnEnter failure test`)을 `LogAssert.Expect`로 처리하고, 다른 Error·Exception 로그는 실패로 본다.

EditMode 테스트 안의 `EnterPlayMode`·`ExitPlayMode`는 도메인을 다시 로드한다. Unity Test Framework는 리로드 뒤 열거자의 위치와 `JsonUtility`로 저장한 테스트 객체의 필드만 복원한다. 그래서 리로드를 넘는 상태는 다음 규칙을 따른다.

- `EnterPlayMode`·`ExitPlayMode`는 테스트 메서드 최상위에만 둔다. Play Mode 안에서는 리로드가 없으므로 중첩 열거자와 지역 변수를 써도 된다.
- 리로드를 넘는 값은 `[SerializeField]` 필드로만 넘긴다.
- `JsonUtility`가 null 문자열을 `""`로 되돌리므로 null에 의미를 두지 않는다. 필드는 `string.Empty`로 초기화하고 비었는지로 판단한다.
- yield를 넘어 지역 변수를 캡처하는 람다를 쓰지 않는다. 첫 yield 전에 만든 클로저 객체는 리로드 뒤 null이 된다.

## 시스템별 테스트

시스템을 바꿀 때 실행할 테스트다. 콘솔 테스트는 각 csproj의 `Compile Include`를, Unity 테스트는 P0 프로브의 검사 그룹(`setup`, `actor`, `skill`, `input`, `ui`, `scene`)을 기준으로 정리했다.

| 시스템 | 콘솔 테스트(대상 제품 소스) | Unity 테스트 |
| --- | --- | --- |
| 매니저 생명주기 | `LifecycleTests`, `NotificationTests` (실제 `Global`·`ManagerHost`·`ManagerBase`) | P0 `setup` |
| Resource | `LifecycleTests`의 `ResourceTests` (`ResourceManager`, `Resource`, `ResourceBase`, `ResourceScope`, `SpriteAtlasResource`) | |
| Scene | `LifecycleTests`의 `SceneTests`, `SceneContractTests` (`SceneManager`, `SceneBase`) | P0 `scene` |
| Pool | `LifecycleTests`의 `PoolTests`, `SoundTests` (`PoolManager`, `GameObjectPool`, `Pool`, `PooledObject`) | P0 `actor` |
| ErrorReporter | `LifecycleTests`의 `ErrorReporterTests` (`ErrorReport/*.cs`) | |
| UI | `UITests` (`UIManager`, `UIContainer`, `UIBase`, `UIDefine`) | P0 `ui`, `UIContainerStackAndNativeActivation` |
| Input | `LifecycleTests`의 `Controller*Tests`, `ActorInputTests` (`InputManager`, `Controller`, `InputContext`) | P0 `input` |
| Notification | `NotificationTests` (`NotificationManager`) | |
| Sound | `SoundTests` (`SoundManager`, `UIClickSound`), `RefactoringTests/UISoundTool` (`UISoundTool`) | `UISoundToolPrefabEdits` |
| Actor | `LifecycleTests`의 `ActorTests`, `ActorSpawnTests`, `ActorReturnTests`, `ActorInputTests`, `ActorSkillTests` (`ComBaseActor`) | P0 `actor` |
| Skill | `SkillTests` (`AbilitySystem/**`, `SkillAdapter`), `LifecycleTests`의 `ActorSkillTests` | P0 `skill`, `TurnBattleSample` |
| Build | `run-build.ps1` (`BuildMgr`, `AddressableBuild`), `RefactoringTests/UploadFlow` (`FirebaseUploader`) | |
| Patch(원격 번들) | `RefactoringTests/Downloader` (`Addressable/Downloader.cs`), `RefactoringTests/FirebaseCache` (`Addressable/FireBase/*.cs`) | |
| TableGenerator | `run-table-generator.ps1` (`run-tests.ps1` 밖) | |

## 관련 문서

- [작업 규칙](../AGENTS.md)의 "검증과 보고"
- [리팩토링 판정 기준](../REFACTORING_CRITERIA.md)
- 프로젝트별 검사 범위는 `Tools/*/README.md`
