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

        //���ε� �׽�Ʈ ��
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

        //���� �׽�Ʈ��
        [MenuItem("Build/Test")]
        public static async void TestBuild()
        {
            //�켱 Ŀ��� ���ο� ���� Args�� �д´�.

            BuildTarget buildTarget = BuildTarget.StandaloneWindows;
            string buildVersion = "0.0.1";

            //Addressable FireBase ����
            await AddressableBuild.Build();

            //Addressabe FireBase ���� ���� ���ε�
            FirebaseUploader uploader = new FirebaseUploader(buildTarget, buildVersion);
            //var task = uploader.UploadFiles();
            //await task;
            await uploader.UploadFiles();

            Debug.Log("Start Build");
            await Build(buildTarget, buildVersion);
            await PostBuild();

            //����� ���� ����
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
            //�켱 Ŀ��� ���ο� ���� Args�� �д´�.
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

            //Addressable FireBase ����
            await AddressableBuild.Build();

            Debug.Log("End Addressable Build");


            Debug.Log("Start Upload Addressabe");


            //Addressabe FireBase ���� ���� ���ε�
            FirebaseUploader uploader = new FirebaseUploader(buildTarget, buildVersion);
            var task = uploader.UploadFiles();
            await task;

            Debug.Log("Start Build");
            await Build(buildTarget, buildVersion);
            await PostBuild();


            //����� ���� ����
            uploader.DeleteFiles();

        }

        private static async Task Build(BuildTarget buildTarget, string buildVersion)
        {
            //���������� �÷����� ���� ���� ����

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
            //���� �� �ʿ���� ������ ���� �� ���ε带 ���� ���𰡰� �ʿ���
        }
    }

}
