# RefactoringTests 실행

저장소 루트에서 PowerShell로 `& Tools/run-tests.ps1`을 실행하면 이 폴더의 Downloader, FirebaseCache, UISoundTool, UploadFlow 프로젝트와 `run-build.ps1`이 다른 콘솔 테스트와 함께 실행된다. 하나만 실행하려면 `dotnet run --project Tools/RefactoringTests/<프로젝트>`나 `& Tools/RefactoringTests/run-build.ps1`을 쓴다. .NET 9 SDK가 필요하고, Downloader와 FirebaseCache는 Unity가 `Client/Library/PackageCache`에 복원한 UniTask 패키지도 필요하다. 각 실행의 마지막 줄 통과 수가 전체 수와 같거나 `ALL PASSED`로 시작하면 통과다. 전체 기준선과 수치는 [testing.md](../../Docs/testing.md)에 있다. 로그와 컴파일 결과는 git이 무시하는 `bin/`에 생긴다.

## 무엇을 검사하나

각 프로젝트는 제품 소스 하나 또는 폴더 하나를 그대로 컴파일하고, Unity와 외부 서비스 경계만 호출을 기록하는 대체물로 바꾼다.

- `BuildFlow`(`run-build.ps1`로 실행)는 `Build/Editor/BuildMgr.cs`와 `AddressableBuild.cs`를 컴파일한다. AssetDatabase, AddressableAssetSettings, FirebaseUploader, BuildPipeline, EditorApplication.Exit는 대체물이다. `PreBuild`의 정상, 실패, 대기 흐름과 Firebase 설정, Addressables 빌드 오류에서 실행 순서, 버전 적용, 단계 시간 로그, 종료 코드를 확인한다. 이어서 명령줄 인자 경우(미지원 타깃, 값 누락, 잘못된 빌드 번호, 중복, iOS 등)를 하나씩 실행한다.
- `UploadFlow`는 `Server/Firebase/Editor/FirebaseUploader.cs`를 Firebase Storage 대체물과 함께 컴파일한다. 업로드 대기, 실패 전달, 완료를 확인한다.
- `Downloader`는 `Addressable/Downloader.cs`와 UniTask를 컴파일한다. Addressables 핸들, 위치 조회, PlayerLoop의 Yield와 Delay는 제어 가능한 대체물이다. 진행과 완료 통지, 콜백 없음과 콜백 예외, 재시도, 취소, 핸들 해제를 확인하고 호출 순서를 `TRACE` 줄로 출력한다.
- `FirebaseCache`는 `Addressable/FireBase/`의 런타임 소스 전체(Cache, Manager, AssetBundle, Hash, Json 프로바이더)와 UniTask를 컴파일한다. 실제 `FirebaseAddressablesManager.InitializeAsync`로 프로바이더를 등록한 뒤 시나리오를 실행한다. Firebase Storage URL 요청, 메인 스레드 후속 처리, Addressables 위치 조회와 `ProvideHandle`, 내부 `ProvideResource`는 테스트가 응답 시점을 정하는 대체물이다. `ProvideHandle.Complete`는 실제 Addressables처럼 요청 타입으로 바꿀 수 없는 결과를 변환 오류로 실패시킨다. PreWarm과 프로바이더의 성공, 실패, 기본 프로바이더 전달을 확인한다.
- `UISoundTool`은 `Sound/Editor/UISoundTool.cs`를 컴파일한다. Unity 편집 API는 호출을 기록하는 대체물이다. 정상, 실패, 선택 처리 시나리오와 `TRACE`를 확인한다.

실제 Unity 프리팹 편집은 Unity Test Runner EditMode 탭의 `RefactoringProbeTests.UISoundToolPrefabEdits`가 확인한다. SetUp이 `Assets/Tests/RefactoringFixtures`를 만들고 TearDown이 지우며, `Client/Assets/Tests/Editor/RefactoringProbe.cs`의 검사가 모두 실행됐는지 단언한다. 검사 항목은 실제 프리팹에 대한 UISoundTool 추가, 변경, 삭제와 비활성 자식, 동일 이름 형제 처리, 편집 실패 전파와 프리팹 내용 해제, 선택 인덱스 조정이다.

## 검증하지 않는 것

- 실제 업로드, 플레이어 빌드, 서버 파일 삭제.
- 실제 네트워크, Unity 프레임 시간, Firebase 접속과 초기화, Addressables 번들 로드.
- 모바일 프로파일링. Unity 에디터 검증은 플레이어 빌드와 실제 Firebase 업로드 검증을 대신하지 않는다.

## 변경 전후 비교

변경 전 소스 사본을 지정해 한 번, 현재 소스로 한 번 실행해 출력을 비교한다. 사본은 저장소에 넣지 않는다.

```powershell
& Tools/RefactoringTests/run-build.ps1 -ScriptRoot '<변경 전 Assets/Script 경로>'
dotnet run --project Tools/RefactoringTests/UploadFlow -p:ScriptRoot='<변경 전 Assets/Script 경로>'
dotnet run --project Tools/RefactoringTests/FirebaseCache -p:FirebasePath='<변경 전 FireBase 폴더 경로>/'
dotnet run --project Tools/RefactoringTests/UISoundTool -p:UISoundToolPath='<변경 전 UISoundTool.cs 경로>'
```

UISoundTool 테스트는 `UISoundTool`의 internal 필드와 메서드를 직접 쓰므로, 사본도 같은 멤버가 internal이어야 컴파일된다.

Downloader는 두 출력을 파일로 남겨 줄 단위로 비교한다.

```powershell
dotnet run --project Tools/RefactoringTests/Downloader -p:DownloaderPath='<변경 전 Downloader.cs 경로>' > Tools/RefactoringTests/bin/downloader-before.txt
dotnet run --project Tools/RefactoringTests/Downloader > Tools/RefactoringTests/bin/downloader-after.txt
Compare-Object (Get-Content 'Tools/RefactoringTests/bin/downloader-before.txt') (Get-Content 'Tools/RefactoringTests/bin/downloader-after.txt') -SyncWindow 0
```

## TableGenerator 검사

`run-table-generator.ps1`은 `run-tests.ps1`에 포함되지 않는다. 따로 실행한다.

```powershell
Copy-Item Tools/DesignGenerator/DesignGenerator/bin/Release '<임시 폴더>/gen' -Recurse
& Tools/RefactoringTests/run-table-generator.ps1 -GeneratorPath '<임시 폴더>/gen/DesignGenerator.exe' -OutputPath '<임시 폴더>/out'
```

스크립트는 Visual Studio의 csc.exe로 `TableGeneratorTests.cs`를 컴파일하고, `-GeneratorPath`의 DesignGenerator.exe를 참조해 실행한다. 테스트 exe를 생성기 폴더에 쓰므로 DesignGenerator 빌드 출력을 직접 지정하지 말고 사본을 지정한다. `-InputPath`의 기본값은 저장소의 `DesignTable/Data`다. `-PeerBytesPath`를 주면 다른 생성기의 산출물과 바이트 단위로 비교한다.

## 관련 문서

- 빌드 흐름: [architecture.md의 빌드](../../Docs/architecture.md#빌드)
- Downloader와 Firebase 리소스 경로: [architecture.md의 Patch와 Resource](../../Docs/architecture.md#patch와-resource)
