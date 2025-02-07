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
            buildPlayerOptions.locationPathName = "Builds/IOS/test.apk";
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

        //빌드 테스트용
        [MenuItem("Build/Test")]
        public static async void TestBuild()
        {
            //우선 커멘드 라인에 들어온 Args를 읽는다.

            BuildTarget buildTarget = BuildTarget.StandaloneWindows;
            string buildVersion = "0.0.1";

            //Addressable FireBase 빌드
            await AddressableBuild.Build();

            //Addressabe FireBase 빌드 파일 업로드
            FirebaseUploader uploader = new FirebaseUploader(buildTarget, buildVersion);
            //var task = uploader.UploadFiles();
            //await task;
            await uploader.UploadFiles();

            Debug.Log("Start Build");
            await Build(buildTarget, buildVersion);
            await PostBuild();

            //빌드된 파일 제거
            uploader.DeleteFiles();
        }

        private static void StartBuild(BuildPlayerOptions buildOption)
        {
            BuildReport report = BuildPipeline.BuildPlayer(buildOption);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
                Debug.Log($"Build Successed {summary.totalSize} + bytes");

            if (summary.result == BuildResult.Failed)
                Debug.Log("Build Failed");
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

        public static async void PreBuild()
        {
            //우선 커멘드 라인에 들어온 Args를 읽는다.
            string[] commandLienArgs = Environment.GetCommandLineArgs();
            BuildTarget buildTarget = BuildTarget.NoTarget;
            string buildVersion = string.Empty;

            for (int i = 0; i < commandLienArgs.Length; ++i)
            {
                switch (commandLienArgs[i])
                {
                    case "-BUILD_TARGET":
                        if (!System.Enum.TryParse(commandLienArgs[i + 1], out buildTarget))
                        {
                            Debug.Log($"Build Target Is Not Found : {commandLienArgs[i + 1]}");
                            return;
                        }
                        break;
                    case "-BUILD_VERSION":
                        buildVersion = commandLienArgs[i + 1];
                        break;
                }
            }

            Debug.Log("Start Addressable Build");

            //Addressable FireBase 빌드
            await AddressableBuild.Build();

            Debug.Log("End Addressable Build");


            Debug.Log("Start Upload Addressabe");


            //Addressabe FireBase 빌드 파일 업로드
            FirebaseUploader uploader = new FirebaseUploader(buildTarget, buildVersion);
            var task = uploader.UploadFiles();
            await task;

            Debug.Log("Start Build");
            await Build(buildTarget, buildVersion);
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
