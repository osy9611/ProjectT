# 남은 지적과 보류 결정

코드 품질 백로그(09_backlog.md)의 1~28번은 모두 끝났다. 이 문서는 그다음 작업을 고를 때 보는 목록이다. A는 아직 처리하지 않은 지적, B는 의도적으로 남긴 결정과 알려진 위험이다. 모든 항목은 main [`7a4f0178`](https://github.com/osy9611/ProjectT/commit/7a4f0178) 코드에서 grep과 read로 다시 확인했다.

근거 표기에서 "리뷰 NN"은 `Docs/archive/PROJECT_CODE_QUALITY_REVIEW/NN_*.md`, "UI 리뷰 NN"은 `Docs/archive/UI_CODE_QUALITY_REVIEW/NN_*.md`, "백로그 N"은 `Docs/archive/PROJECT_CODE_QUALITY_REVIEW/09_backlog.md`의 N번 항목이다. `Docs/archive/`는 로컬 보관 폴더라 git에 없다. 성격은 결함, 정리, 구조 중 하나다. 항목을 고를 때와 진행할 때의 작업 규칙은 [AGENTS.md](../AGENTS.md)를 따른다. 공개 API, 직렬화 데이터, 생명주기 순서가 바뀌는 항목은 [착수 전에 확인](../AGENTS.md#착수-전-확인)을 받는다.

## A. 남은 유효 지적

### 씬과 코어

| 위치 | 문제 | 근거 | 성격 |
|---|---|---|---|
| `Managers/SceneManager.cs` | `EnterEntryAsync`가 `scenes.Add`를 GameObject 생성 뒤에 해서 catch에 `else if (!scenes.ContainsKey)` 분기가 생겼다. `SceneEntry.IsAddressable`은 `Instance`에서 알 수 있는 값이다. | 리뷰 01 F6 | 정리 |
| `Managers/SceneManager.cs` | 콜백형 `Transition<T>`과 `eSceneTransitionResult`가 `TransitionAsync`와 함께 공개 진입점을 둘로 만든다. `PrevSceneName`은 샘플 라벨에만 쓰인다. 공개 API 변경이다. | 리뷰 01 F7 | 구조 |
| `Server/Firebase/FirebaseDB.cs` | `GetBuildVersion`이 `ContinueWith`로 실패를 삼키고 빈 문자열을 돌려준다. 미사용 `async void SaveBuildData`와 `using Unity.VisualScripting`이 남아 있다. | 리뷰 01 F8 | 결함 |
| `Common/Define.cs`, `Util/UtilFunc.cs`, `Util/ComUtilFunc.cs` | `Define`은 빈 클래스다. 유틸이 두 클래스로 나뉘어 있고 `GetOrAddChildComponent`·`AddChildComponent`는 호출이 없다. | 리뷰 01 F9 | 정리 |
| `Managers/DataManager.cs` | `isDone`이 `tableData != null`과 같은 뜻이다. 같은 일을 하는 catch가 2개 있고 `GetLocalString`은 호출이 없다. | 리뷰 01 F10 | 정리 |
| `Scene/TitleScene.cs`, `Managers/SceneManager.cs` | 테이블 로드는 `TitleScene.OnEnter`가 하는데 `ResolvePath`는 `Global.Table`을 읽는다. "테이블이 있어야 씬 이름 전환이 된다"는 전제가 한 씬 구현에 숨어 있다. | 리뷰 01 F10 | 구조 |

### 리소스, 풀, 빌드

| 위치 | 문제 | 근거 | 성격 |
|---|---|---|---|
| `Addressable/ResourceBase.cs` | `Initialize`가 받는 `onFailed` 콜백으로 리소스가 매니저 사전을 거꾸로 고친다. | 리뷰 02 F2 | 구조 |
| `Managers/ResourceManager.cs`, `Managers/PoolManager.cs`, `Pool/GameObjectPool.cs`, `Addressable/ResourceBase.cs`, `Addressable/Resource.cs` | 프로덕션 호출이 없는 공개 멤버가 테스트 때문에 남아 있다. 대상은 `LoadResource*`, `PreloadAsync`, `PreloadResourceAsync`, `GetSpriteAsync`, `OnInitialize`, `ResourceData`, `PoolManager.GetOriginal`, public `Clear`, `GameObjectPool.ReturnAll`이다. 공개 API 축소다. | 리뷰 02 F3 | 정리 |
| `Pool/GameObjectPool.cs`, `Pool/PooledObject.cs`, `Pool/Pool.cs` | 대여 한 번에 `spawnParent` 필드, `Prepare`, `FinishReturn`으로 콜백이 왕복한다. `Pool<T>`는 `as IPoolable` 캐스트로 콜백을 찾는다. | 리뷰 02 F6 | 구조 |
| `Pool/Pool.cs` | 쓰이지 않는 `maxPoolSize`, `isValueType`, `ItemComparer`가 남아 있다. | 리뷰 02 F7 | 정리 |
| `Build/Editor/BuildMgr.cs`, `Build/Editor/AddressableBuild.cs`, `Server/Firebase/Editor/FirebaseUploader.cs` | `Build_AOS`·`Build_IOS`·`Build_EXE`가 복사본 세 벌이다. `Build`·`PostBuild`는 `await` 없는 `async Task`이고 `PostBuild`는 비어 있다. `OnlyAddressable`이 메인 스레드에서 `task.Wait()`를 한다(교착 가능, 추정). 설정 경로와 `"FireBaseBuild"`가 세 곳에 반복되고 `AddressableBuild.GetLocalPath`는 호출이 없다. | 리뷰 02 F10 | 정리 |
| `Managers/ResourceManager.cs`, `Addressable/SpriteAtlasResource.cs` | 스프라이트 이름 공백 검사가 `GetSprite`, `GetSpriteAsync`, `SpriteAtlasResource.GetSprite` 세 곳에 있다. | 리뷰 02 F12 | 정리 |

### Actor와 AbilitySystem

| 위치 | 문제 | 근거 | 성격 |
|---|---|---|---|
| `Actor/ComBaseActor.cs`, `Pivot/ComPivotAgent.cs` | `[RequireComponent(typeof(ComPivotAgent))]`가 모든 Actor에 런타임에서 읽지 않는 컴포넌트를 강제한다. `pivotAgent`는 protected 가변 필드이고 `GenerateData`는 비어 있다. 프리팹·씬 참조 확인이 필요하다. | 리뷰 03 F5 | 구조 |
| `AbilitySystem/Skill/SkillAgent.cs`, `AbilitySystem/Skill/Action/SkillActionController.cs` | `GiveSkill`·`ActivateSkill`·`CancelSkill`이 `SkillActionController`로 넘기기만 하고, 세 객체가 서로를 역참조한다. | 리뷰 03 F6 | 구조 |
| `AbilitySystem/Skill/Action/BaseSkillAction.cs`, `SkillActionController.cs` | 발동 순서(`TryActivateInternal`, `CompleteActivationInternal`, `Activating`·`Ending`, `pendingEndReason`)가 두 클래스의 네 메서드에 나뉘어 있다. | 리뷰 03 F7 | 구조 |
| `SkillAdapter/Action_Normal.cs`, `Action_Melee.cs`, `Action_Range.cs` | 내용이 같은 자리표시자 세 벌이다. | 리뷰 03 F9 | 정리 |
| `AbilitySystem/Skill/SkillAgent.cs`의 `ReleaseInternal`, `AbilitySystem/Stat/Status.cs`의 `ReleaseInternal` | "정리는 알리지 않는다"가 "구독자는 Disable에서 해제한다"는 관례에 기댄다. | 리뷰 03 F11 | 구조 |
| `SkillActionController.cs`의 `OnUpdate` | `OnUpdate(float deletaTime)` 매개변수 이름이 오타다. | 리뷰 03 기타 | 정리 |

### UI와 Sound

| 위치 | 문제 | 근거 | 성격 |
|---|---|---|---|
| `Managers/SoundManager.cs` | 629줄 한 클래스에 2D 채널 믹서와 3D 음원 풀이 함께 있다. | 리뷰 04 F10 | 구조 |
| `Managers/UIManager.cs`, `UGUI/HUD/ComHudAgent.cs` | `GetHud`·`GetHudAsync`·`AttachInternal`이 `PivotTr` 하나를 쓰려고 `PivotInfo` 전체를 받는다. 공개 API 변경이다. | 리뷰 04 F15 | 구조 |
| `Sound/UIClickSound.cs`, `Sound/Editor/UISoundTool.cs` | `UIClickSound`는 GUID 참조가 0건이다. `clickSoundType`은 실제로 Addressable 경로이고, 경로를 비울 때 `"None"` 문자열 센티널을 쓴다. `isToggle`·`GetUIComponent`가 public이다. 이름 변경은 직렬화 필드 변경이다. | 리뷰 04 F18 | 정리 |
| `UGUI/UI/UIContainer.cs` | `uiDatas`만 `readonly`가 없다. `#region Methods`는 두 함수만 감싼다. `AttachWidget`은 `Instantiate(prefab, layer, false)`로 부모를 정한 뒤 `StretchInternal`이 같은 부모를 다시 지정한다. | UI 리뷰 05 | 정리 |
| `Scenes/TitleScene.unity`, `Scenes/DownloadScene.unity` | 씬에 직접 배치한 버튼은 Static UI가 아니어서 패드로 조작할 수 없다. `SceneNavigator`의 `GoTitle`·`GoDownload`에 연결된 버튼이다. | `INPUT_MANAGER_REFACTORING_REPORT.md` 9절 | 구조 |
| `Common/ErrorReport/` | 운영 sink가 `FileErrorSink` 하나뿐이고 Crashlytics sink는 도입하지 않았다. | `Docs/error-reporting.md` | 구조 |

### 도구와 테스트

| 위치 | 문제 | 근거 | 성격 |
|---|---|---|---|
| `Tools/SoundTests/Stubs.cs`, `Tools/UITests/Stubs.cs`, `Tools/SkillTests/Stubs.cs`, `Tools/LifecycleTests/PoolConsumerStubs.cs` | Sound·UI·Skill 테스트가 가짜 `Global`을 쓴다. SkillTests는 가짜 `ComBaseActor`도 쓴다. LifecycleTests의 `SkillType`·`BuffType`은 `sbyte`이고 실제 `DesignEnum.cs`는 `int`다. Sound·UI의 실물 Global 전환이 남았다. SkillTests는 `SkillDefinitions`의 `Global.Table` 의존 때문에 가짜를 유지하기로 했다. | 리뷰 06 F2, 리뷰 07, 리뷰 08 | 구조 |
| `Tools/DesignGenerator/DesignGenerator/Enum/EnumGenerator.cs` | 레거시 `EnumInfo` 모델과 `ToLegacy`, DOM 리더 `XmlDocumentEnumRecordSource`, 호출 없는 `Create`가 남아 있다. `SetEnumInfo`는 진단을 `Console.Write`로 바로 출력해 `--format=json`에 enum 오류가 빠진다. | 리뷰 06 F4 | 정리 |
| `Tools/DesignGenerator/DesignGenerator/Util/ExcelIO.cs`, `Program.cs`, `DesignGenerator.csproj` | `ExcelIO.cs`가 889줄이다. `KillAllExcel`, `MoveDllFile`, 콜백형 `LoadXML`·`LoadAllXML` 같은 죽은 함수가 있고 `GeneratorUtils.WriteIfChanged`가 `GeneratedFileWriter.WriteIfChanged`와 중복된다. csproj가 ILRepack·Costura·Fody를 참조한다. `Program.cs`의 `#if DEBUG` 블록에 `D:\Project\ProjectT` 절대 경로가 있다. | 리뷰 06 F5 | 정리 |
| `Tools/DesignGenerator/DesignGenerator/Table/TableGenerator.cs` | 결과를 `OnMgrSet`·`OnSerializerSet` 델리게이트로 넘기고, `Load`와 `LoadAll`이 같은 흐름을 반복한다. | 리뷰 06 F6 | 정리 |
| `Tools/DesignGenerator/DesignGenerator/Table/TableGenerator.cs` | 7개·9개 복합 PK 입력에서 `datas` 필드 타입이 잘못 생성되어 CS1003·CS0305로 컴파일에 실패한다. 재현 기록 이후 이 파일은 바뀌지 않았다. | `Docs/archive/ADDITIONAL_REFACTORING_ANALYSIS.md` 4절(로컬 보관) | 결함 |
| 루트 `Assembly-CSharp.csproj`, `Tools/TEMP/`, `Tools/LifecycleTests/*.cs.meta` | 0바이트 untracked csproj, 파일 없는 디렉터리 트리, 추적 중인 `.cs.meta` 7개가 남아 있다. | 리뷰 06 F11 | 정리 |
| `AbilitySystem/Skill/Effect/EffectController.cs`의 `EffectHandle.Id`, `Common/Global.cs`의 `Log` | 주석이 지금 코드와 맞지 않는다. 핸들 0은 이제 Instant에서만 나오는데 "적용 중 정리"를 적고 있다. `Log` 주석은 삭제된 "소켓 스레드"를 적고 있다. | 백로그 6, 코드 대조 | 정리 |
| `DesignTable/Gen/` | `DesignGenerator.exe.config`와 `DesignTool.exe.config`가 없다. `App.config`의 bindingRedirect가 실행 시 적용되지 않을 수 있다(추정). | 백로그 24a | 결함 |
| `Tools/DesignGenerator` | 생성 DLL을 결정적(deterministic)으로 컴파일할지 정하지 않았다. | 옛 위키 TableGenerator | 정리 |
| `Tools/DesignGenerator`의 `--merge` | ILRepack 병합 산출물을 Unity가 로드하지 못한다. 그래서 병합은 기본으로 끄고 `ValidateForUnity`가 산출물을 검사한다. | 옛 위키 TableGenerator | 결함 |
| `Tools/DesignGenerator` | Excel COM 변환 전체 경로와 Unity에서 생성 DLL을 로드하는 경로는 검증하지 않았다. | 옛 위키 TableGenerator | 미검증 |

## B. 보류 결정과 알려진 위험

| 항목 | 남긴 것과 이유 | 다시 볼 조건 |
|---|---|---|
| Firebase 실환경 | 4·22c·22d·22f·22h는 실제 Firebase 접속, 원격 번들, DownloadOnly를 검증하지 않았다. 배포 저장소가 S3로 바뀔 수 있어 범위에서 뺐다. | 배포 저장소가 정해질 때 |
| 25에서 유지한 리플렉션 | `PoolTests.ScopeEntries`는 Scope 등록과 닫힘 표시를 공개 API로 관찰할 수 없어 `scopes`·`IsClosed`를 직접 센다. `ErrorReporterTests`의 `s_counts`, `OnEnable`·`Start`·`OnDestroy` 같은 엔진 메시지 호출, `UITests`의 `type`·`firstSelected`, `ProjectTUIPlayProbe`의 `type`·`uiDatas`, `ProjectTRuntimePlayProbe`의 `uiContainer`·`type`·`uiDatas`도 남겼다. | 해당 내부 구조를 바꾸는 작업을 할 때 |
| 28 컨텍스트 누수 검사 축소 | `RuntimeFixtures` 프로브가 Controller 전체 대신 Actor 기준(`ContextOwned`, `RegisteredContexts`)으로 센다. Actor 밖에서 생긴 컨텍스트 누수는 잡지 못한다. | 입력 컨텍스트를 Actor 밖에서 등록하는 코드가 생길 때 |
| 20 콜백 중 반납 거부 | 스킬·효과·Status 호출 중 반납은 거부된다([actor-lifecycle.md](actor-lifecycle.md#호출-중-반납은-거부한다)). 그래서 "효과 틱 안에서 HP 0이 되면 즉시 반납"은 할 수 없다. | 콜백 안 즉시 반납이 필요한 콘텐츠가 생길 때 |
| 25a OnRemove 실패 시 이벤트 없음 | `OnRemove`가 실패하면 효과는 목록에서 빠지지만 `OnEffectRemoved`는 나가지 않는다. 의도된 동작으로 판정하고 `EffectBehavior.cs` 주석과 테스트로 고정했다. | 버프 UI처럼 제거 이벤트에 의존하는 구독자가 생길 때 |
| 25b 틱 실패 비격리 | Tick·OnUpdate 실패는 Actor를 Faulted로 바꾸지 않고 Active로 둔다([actor-lifecycle.md](actor-lifecycle.md#틱과-onupdate의-실패는-격리하지-않는다)). | 틱 실패 뒤 남은 태그가 실제 문제를 일으킬 때 |
| 22e 초기화 OCE 스택 | 초기화가 `OperationCanceledException` 하나로 실패하면 `AggregateException`으로 감싸 메시지는 보존하지만, 원래 스택은 UniTask 특성상 복구할 수 없다. | UniTask를 올리거나 초기화 오류 전달 방식을 바꿀 때 |
| UI F5·F7a 유지 | 22에서 `UIManager`의 위젯 API 전달과 HUD의 `UIContainer` 내부 루트 의존(F5), `AttachWidget`의 롤백 catch(F7a)를 판단 후 유지했다. | UI 계층을 다시 나눌 때 |
| 15 HUD 소유권 | 씬 전환은 HUD를 반납하지 않고 Actor가 `ComHudAgent.Release()`로만 반납한다. pivot이 먼저 파괴되면 `UpdatePositionInternal`이 `pivotInfo.PivotTr`에서 매 프레임 `MissingReferenceException`을 낸다. "Actor 반납이 먼저"라는 순서는 계약으로만 있다(UI 리뷰 02 부수 관찰). | Actor 밖에서 pivot을 파괴하는 경로가 생길 때 |
| 02 F4 닫힌 Scope로의 리소스 로드 | `PoolManager`는 닫힌 Scope의 대여를 거부하지만 `ResourceManager`는 같은 Scope의 로드를 계속 허용한다. 거부까지 할지는 계약 변경이라 정하지 않았다. | 씬 정지 뒤 로드가 문제를 일으킬 때 |
| 13 LocalStorage 삭제 | 저장 엔진과 매니저 등록을 지웠다. 기존 저장 파일은 읽지 않고 무시한다. | 로컬 저장 기능이 다시 필요할 때 |
| 8 디스패치 루프 2개 | `ManagerHost`의 `OnUpdate`·`OnAppPause` 루프는 합치지 않았다. 합치면 매 프레임 할당이나 분기가 생긴다. | 훅을 다시 추가할 때 |
| 21 StopAllSpatialSounds 집계 | 오류 집계를 3단에서 1단(`ErrorCollector.Run`과 `ThrowIfAny`)으로 줄이고 그대로 뒀다. | Sound 오류 처리를 다시 볼 때 |
| 외부 오류 sink 범위 | 외부 sink에 예외만 보낼지 `Global.LogError`로 남긴 처리된 오류까지 보낼지, 이벤트에 사용자 ID를 넣을지 정하지 않았다([error-reporting.md](error-reporting.md#설계-결정)). | Crashlytics sink를 도입할 때 |
| 22b 종료 오류 | `ManagerHost` 종료가 여러 오류를 `AggregateException`으로 던지고 Global 보고 경계가 둘이다. | 종료 순서를 바꿀 때 |
| 24 Build Profile | `Client/Assets/Settings/Build Profiles/New Windows Profile.asset`을 지우지 않고 유지했다. | 빌드 설정을 정리할 때 |
| Check 하네스 미통합 | `static void Check(`가 Tools 아래 11개 파일에 있다. 같은 사본은 4줄짜리뿐이고 나머지는 의도적으로 다르며, 합치면 프로젝트 간 결합이 생겨 하지 않았다(리뷰 07 5단계). | 테스트 프레임워크를 바꿀 때 |
| AbilitySystem asmdef 없음 | asmdef를 의도적으로 지워 Assembly-CSharp에 컴파일된다. `internal`이 어셈블리 전체에 열려 있어 비의존은 리뷰로 지킨다. | AbilitySystem을 다른 프로젝트에서 재사용할 때 |
