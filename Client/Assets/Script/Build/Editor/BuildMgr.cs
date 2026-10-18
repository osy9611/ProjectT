 using JetBrains.Annotations;
using ProjectT.Server;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace ProjectT.Build
{
    public class BuildMgr
    {
        [MenuItem("Build/AOS")]
        public static void Build_AOS()
        {
            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
            buildPlayerOptions.scenes = FindEnableEditorScenes();
            buildPlayerOptions.target = BuildTarget.Android;
            buildPlayerOptions.options = BuildOptions.None;
            buildPlayerOptions.locationPathName = "Builds/AOS/test.apk";

            StartBuild(buildPlayerOptions);
        }

        [MenuItem("Build/IOS")]
        public static void Build_IOS()
        {
            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
            buildPlayerOptions.scenes = FindEnableEditorScenes();
            buildPlayerOptions.target = BuildTarget.iOS;
            buildPlayerOptions.options = BuildOptions.None;
            buildPlayerOptions.locationPathName = "Builds/IOS";

            StartBuild(buildPlayerOptions);
        }

        [MenuItem("Build/EXE")]
        public static void Build_EXE()
        {
            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
            buildPlayerOptions.scenes = FindEnableEditorScenes();
            buildPlayerOptions.target = BuildTarget.StandaloneWindows;
            buildPlayerOptions.options = BuildOptions.Development;
            buildPlayerOptions.locationPathName = "Builds/EXE/ProjectJ.exe";
            StartBuild(buildPlayerOptions);
        }

        [MenuItem("Build/OnlyAddressable")]
        public static void OnlyAddressable()
        {
            Task task = AddressableBuild.Build();
            task.Wait();
        }

        //업로드 테스트 용
        //[MenuItem("Build/BuildAndUploadAddressable")]
        //public static void BuildAndUploadAddressable()
        //{
        //    Task task = AddressableBuild.Build();
        //    task.Wait();

        //    UploadData data = new UploadData();
        //    data.SetData("AOS", "0.0.1");

        //    FirebaseUploader uploader = new FirebaseUploader();
        //    Task t = uploader.UploadFolder(data, null);
        //    t.Start();
        //    t.Wait();
        //}

        private static void StartBuild(BuildPlayerOptions buildOption)
        {
            BuildReport report = BuildPipeline.BuildPlayer(buildOption);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"Build {summary.result} : {summary.totalErrors} errors");

            Debug.Log($"Build Successed {summary.totalSize} + bytes");
        }

        private static string[] FindEnableEditorScenes()
        {
            List<string> editorScenes = new List<string>();

            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (!scene.enabled)
                    continue;
                editorScenes.Add(scene.path);
            }

            return editorScenes.ToArray();
        }

        //-executeMethod는 async 완료를 기다리지 않으므로 -quit 없이 실행하고 여기서 종료 코드를 남긴다.
        public static async void PreBuild()
        {
            int exitCode = 1;
            try
            {
                if (TryReadCommandLineInternal(out BuildTarget buildTarget, out string buildVersion, out int buildNumber))
                {
                    ApplyVersionInternal(buildVersion, buildNumber);

                    Debug.Log("Start Addressable Build");

                    System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();

                    //Addressable FireBase 빌드
                    await AddressableBuild.Build();

                    LogElapsedInternal("Addressable Build", timer);
                    Debug.Log("End Addressable Build");


                    Debug.Log("Start Upload Addressabe");

                    await UploadAndBuildInternalAsync(buildTarget, buildVersion);
                    exitCode = 0;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            EditorApplication.Exit(exitCode);
        }

        private static bool TryReadCommandLineInternal(out BuildTarget buildTarget, out string buildVersion, out int buildNumber)
        {
            string[] commandLienArgs = Environment.GetCommandLineArgs();
            buildTarget = BuildTarget.NoTarget;
            buildVersion = string.Empty;
            buildNumber = 0;

            for (int i = 0; i < commandLienArgs.Length; ++i)
            {
                switch (commandLienArgs[i])
                {
                    case "-BUILD_TARGET":
                        if (!System.Enum.TryParse(commandLienArgs[i + 1], out buildTarget))
                        {
                            Debug.Log($"Build Target Is Not Found : {commandLienArgs[i + 1]}");
                            return false;
                        }
                        break;
                    case "-BUILD_VERSION":
                        buildVersion = commandLienArgs[i + 1];
                        break;
                    case "-BUILD_NUMBER":
                        if (!int.TryParse(commandLienArgs[i + 1], out buildNumber))
                        {
                            Debug.LogError($"Build Number Is Invalid : {commandLienArgs[i + 1]}");
                            return false;
                        }
                        break;
                }
            }

            if (buildTarget != BuildTarget.Android && buildTarget != BuildTarget.iOS && buildTarget != BuildTarget.StandaloneWindows)
            {
                Debug.LogError($"Build Target Is Not Supported : {buildTarget}");
                return false;
            }

            if (string.IsNullOrEmpty(buildVersion) || buildNumber <= 0)
            {
                Debug.LogError($"Build Version Or Number Is Missing : {buildVersion} / {buildNumber}");
                return false;
            }

            return true;
        }

        private static void ApplyVersionInternal(string buildVersion, int buildNumber)
        {
            PlayerSettings.bundleVersion = buildVersion;
            PlayerSettings.Android.bundleVersionCode = buildNumber;
            PlayerSettings.iOS.buildNumber = buildNumber.ToString();
            Debug.Log($"Build Version {buildVersion} ({buildNumber})");
        }

        private static void LogElapsedInternal(string step, System.Diagnostics.Stopwatch timer)
        {
            Debug.Log($"[Build Time] {step} : {timer.Elapsed.TotalSeconds:F1}s");
            timer.Restart();
        }

        private static async Task UploadAndBuildInternalAsync(BuildTarget buildTarget, string buildVersion)
        {
            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();

            //Addressabe FireBase 빌드 파일 업로드
            FirebaseUploader uploader = new FirebaseUploader(buildTarget, buildVersion);
            await uploader.UploadFiles();
            LogElapsedInternal("Upload", timer);

            Debug.Log("Start Build");
            await Build(buildTarget, buildVersion);
            LogElapsedInternal("Player Build", timer);
            await PostBuild();

            //빌드된 파일 제거
            uploader.DeleteFiles();
        }

        private static async Task Build(BuildTarget buildTarget, string buildVersion)
        {
            //본격적으로 플랫폼에 따라 빌드 시작

            switch (buildTarget)
            {
                case BuildTarget.Android:
                    Build_AOS();
                    break;

                case BuildTarget.iOS:
                    Build_IOS();
                    break;

                case BuildTarget.StandaloneWindows:
                    Build_EXE();
                    break;
            }
        }

        private static async Task PostBuild()
        {
            //빌드 후 필요없는 데이터 정리 및 업로드를 위한 무언가가 필요함
        }
    }

}
