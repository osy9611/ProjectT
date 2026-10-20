# 비소켓 리팩토링 검증

## 실행

저장소 루트 `D:\Project\ProjectT`에서 PowerShell로 실행한다.

```powershell
& Tools/run-tests.ps1
& Tools/RefactoringTests/run-unity.ps1
```

`run-build.ps1`은 실제 BuildMgr와 AddressableBuild 소스를 .NET 9에서 컴파일한다. Addressables API(AssetDatabase, AddressableAssetSettings), FirebaseUploader, Unity BuildPipeline, EditorApplication.Exit는 호출을 기록하는 대체물이다. 실제 업로드·플레이어 빌드·서버 파일 삭제는 수행하지 않는다. `PreBuild`의 정상/실패/대기와 Firebase 설정·Addressables 빌드 오류 11개와 CLI 인자 12개(미지원 타깃·값 누락·잘못된 빌드 번호·중복·iOS 등)에서 실행 순서, 버전 적용, 단계 시간 로그, 종료 코드를 확인한다.

`UploadFlow/UploadFlow.csproj`는 실제 FirebaseUploader 소스를 Firebase Storage 대체물과 함께 컴파일한다. 업로드 대기·실패 전달·완료 3개를 확인한다. 실제 업로드는 수행하지 않는다.

변경 전 소스와 비교하려면 아래처럼 별도 스냅샷을 지정한다. 스냅샷은 저장소에 포함하지 않는다.

```powershell
& Tools/RefactoringTests/run-build.ps1 -ScriptRoot '<변경 전 Assets/Script 경로>'
```

`run-unity.ps1`은 Unity 6000.3.14f1을 사용한다. 에디터가 종료된 상태와 정상 라이선스가 필요하다. `-UnityPath`로 실행 파일을 지정할 수 있다. 실제 프로젝트에 임시 `Assets/Tests/RefactoringFixtures`를 만들고, 에디터 배치 모드에서 프리팹을 검사한 뒤 해당 폴더를 정리한다. 같은 검사를 Test Runner EditMode의 `RefactoringProbeTests.UISoundToolPrefabEdits`로도 실행할 수 있다.

Unity 검증 항목은 프리팹 추가·변경·삭제, 비활성 자식과 동일 이름 형제 처리, 렌더러 생성자 필드, 첫 자식 분리와 좌표 유지, 부착 Transform 초기화, 잘못된 코스튬 인자의 조기 반환, Mesh/Material/Color 적용이다. 이전 실행은 24개 assertion이었다. R2에서 프리팹 실패·해제·선택 검사를 보강했고 참조 컴파일은 성공했으나, 실행 중인 Unity 때문에 확장 fixture의 실제 실행은 미검증이다. 기존 Mesh 교체 동작도 특성 테스트로 고정한다.

## R2 UISoundTool 검증

```powershell
dotnet run --project Tools/RefactoringTests/UISoundTool
& Tools/RefactoringTests/run-unity.ps1 -PrefabOnly -OutputPath Tools/RefactoringTests/bin/r2-unity
```

첫 명령은 실제 UISoundTool 소스를 CP949로 읽어 .NET 9에서 컴파일한다. Unity 편집 API는 호출을 기록하는 대체물이며 정상/실패/선택 처리 23개 시나리오와 TRACE를 확인한다. `-p:UISoundToolPath=`로 원본 스냅샷을 지정할 수 있다. 두 번째 명령은 에디터가 종료된 상태에서 실제 Unity 프리팹 fixture만 실행한다. 2026-09-27의 전후 23/23 및 실제 엔진 실행 제한은 [R2 보고서](../../reports/tests/RefactoringTests/UISoundToolReport.md)를 참고한다.

## A1 Downloader 검증

```powershell
dotnet run --project Tools/RefactoringTests/Downloader
```

실제 Downloader와 프로젝트에 설치된 UniTask 소스를 .NET 9에서 컴파일한다. Addressables 핸들·위치 조회 및 PlayerLoop Yield/Delay는 제어 가능한 대체물이다. 진행/완료 통지, 콜백 없음·예외, 재시도·취소와 해제를 12개 시나리오로 확인한다. 실제 네트워크·Unity 프레임 시간·Firebase 초기화는 검증하지 않는다.

변경 전후 동일 시나리오의 호출 순서를 비교하려면 원본 소스 스냅샷으로 한 번, 현재 소스로 한 번 실행해 출력을 비교한다.

```powershell
dotnet run --project Tools/RefactoringTests/Downloader -p:DownloaderPath='<변경 전 Downloader.cs 경로>' > Tools/RefactoringTests/bin/downloader-before.txt
dotnet run --project Tools/RefactoringTests/Downloader > Tools/RefactoringTests/bin/downloader-after.txt
Compare-Object (Get-Content 'Tools/RefactoringTests/bin/downloader-before.txt') (Get-Content 'Tools/RefactoringTests/bin/downloader-after.txt') -SyncWindow 0
```

2026-09-27 A1의 구현·검증·리뷰 결과는 [Downloader 보고서](../../reports/tests/RefactoringTests/DownloaderReport.md)에 별도로 기록한다. 기존 7개 항목의 완료 기록과 구분한다.

## FirebaseCache 검증

```powershell
dotnet run --project Tools/RefactoringTests/FirebaseCache
```

`Addressable/FireBase` 폴더의 런타임 소스 전체(Cache, Manager, AssetBundle·Hash·Json 프로바이더)와 UniTask 소스를 .NET 9에서 컴파일한다. 실제 `FirebaseAddressablesManager.InitializeAsync`로 프로바이더를 등록한 뒤 시나리오를 실행한다. Firebase Storage URL 요청, 메인 스레드 후속 처리, Addressables 위치 조회·`ProvideHandle`·내부 `ProvideResource`는 테스트가 응답 시점을 정하는 대체물이다. `ProvideHandle.Complete`는 실제 Addressables처럼 요청 타입으로 바꿀 수 없는 결과를 변환 오류로 실패시킨다. PreWarm 5개와 프로바이더 성공·실패·기본 프로바이더 전달 시나리오를 확인한다. `-p:FirebasePath=`로 변경 전 FireBase 폴더 사본을 지정할 수 있다. 실제 Firebase 접속과 Addressables 번들 로드는 검증하지 않는다.

## 결과와 한계

2026-09-27 실행 결과는 [리팩토링 보고서](../../reports/tests/RefactoringTests/RefactoringReport.md)의 당시 기록이다. 이후 현재 소스 대조에서 7개 항목 중 6개가 미반영 상태임을 확인했다. 현재 코드 반영 및 검증 상태는 [갱신된 작업 목록](../../ADDITIONAL_REFACTORING_ANALYSIS.md)을 따른다. 로그와 컴파일 결과는 무시되는 `bin/`에 생성한다. Unity 에디터 검증은 플레이어 빌드·모바일 프로파일링·실제 Firebase 업로드 검증을 대신하지 않는다.

## TableGenerator

```powershell
Copy-Item Tools/DesignGenerator/DesignGenerator/bin/Release '<임시 폴더>/gen' -Recurse
& Tools/RefactoringTests/run-table-generator.ps1 -GeneratorPath '<임시 폴더>/gen/DesignGenerator.exe' -OutputPath '<임시 폴더>/out' -InputPath DesignTable/Data
```

`run-table-generator.ps1`은 Visual Studio의 csc.exe로 `TableGeneratorTests.cs`를 컴파일하고 `-GeneratorPath`의 DesignGenerator.exe를 참조해 실행한다. 테스트 exe를 생성기 폴더에 쓰므로, git이 추적하는 `bin/Release`가 아닌 사본을 지정한다. Windows PowerShell 5.1에서는 `-InputPath` 기본값이 빈 `$PSScriptRoot` 때문에 실패하므로 경로를 명시한다. `-PeerBytesPath`를 주면 다른 생성기의 산출물과 바이트 단위로 비교한다.
