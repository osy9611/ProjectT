using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProjectT.Build;

internal static class BuildFlowTests
{
    internal static readonly List<string> Events = new List<string>();
    internal static string Failure;
    internal static TaskCompletionSource<bool> UploadGate;

    private sealed class Context : SynchronizationContext
    {
        private readonly Queue<Action> callbacks = new Queue<Action>();
        internal readonly List<Exception> Errors = new List<Exception>();

        public override void Post(SendOrPostCallback callback, object state)
        {
            callbacks.Enqueue(() => callback(state));
        }

        internal void Drain()
        {
            while (callbacks.Count > 0)
            {
                try { callbacks.Dequeue()(); }
                catch (Exception error) { Errors.Add(error); }
            }
        }
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 1 && args[0] == "--argument-case")
            {
                RunArgumentCase(args[1]);
                return 0;
            }

            foreach (string scenario in new[] { "Success", "Addressables", "Upload", "BuildThrow", "BuildFailedReport", "PendingUpload", "NoSettings", "NoProfile", "NoBuilder", "UnregisteredBuilder", "ContentBuildError" })
            {
                Events.Clear();
                Failure = scenario;
                UploadGate = scenario == "PendingUpload" ? new TaskCompletionSource<bool>() : null;
                var context = new Context();
                SynchronizationContext.SetSynchronizationContext(context);
                BuildMgr.PreBuild();
                context.Drain();

                if (UploadGate != null)
                {
                    Check(!Events.Any(item => item.StartsWith("Build:")) && !Events.Contains("Delete") && !Events.Any(item => item.StartsWith("Exit:")), "Pending upload must block later steps and exit");
                    UploadGate.SetResult(true);
                    context.Drain();
                }

                bool failed = scenario != "Success" && scenario != "PendingUpload";
                bool settingFailed = scenario == "NoSettings" || scenario == "NoProfile" || scenario == "NoBuilder" || scenario == "UnregisteredBuilder";
                Check(context.Errors.Count == 0, "Exceptions are handled at the command-line boundary");
                Check(Events.Contains("Delete") != failed, "Cleanup only after every step succeeded");
                Check(Events.Last() == (failed ? "Exit:1" : "Exit:0"), "Exit code is the final operation");
                Check(Events.Count(item => item.StartsWith("Exit:")) == 1, "Exit once");
                Check(Events.Count(item => item.StartsWith("Exception:")) == (failed ? 1 : 0), "Failure is reported once");
                if (scenario == "ContentBuildError")
                    Check(Events.Contains("Exception:Addressable Build Failed : ContentBuildError") && !Events.Any(item => item.StartsWith("Uploader:") || item.StartsWith("Build:")), "Content build error stops before upload and player build");
                if (scenario != "Addressables" && scenario != "ContentBuildError" && !settingFailed)
                    Check(Events.Contains("Uploader:StandaloneWindows:3.2.1"), "Build target and version");
                if (settingFailed)
                {
                    Check(!Events.Contains("Addressables") && !Events.Any(item => item.StartsWith("Uploader:") || item.StartsWith("Build:")), "Firebase setting failure stops before content build, upload and player build");
                    Check(!Events.Any(item => item.StartsWith("Error:") || item.StartsWith("Warning:")), "Firebase setting failure is reported only by the exception");
                }
                if (scenario == "Upload")
                    Check(!Events.Any(item => item.StartsWith("Build:")), "Failed upload stops before player build");
                Check(Events.IndexOf("Version:3.2.1") == 0 && Events[1] == "VersionCode:7" && Events[2] == "BuildNumber:7", "Version is applied before Addressables");
                if (scenario == "Upload")
                    Check(!Events.Contains("Log:[Build Time] Upload : 0.0s"), "No upload time after failed upload");
                if (!failed)
                {
                    Check(new[] { "Addressable Build", "Upload", "Player Build" }.All(step => Events.Contains("Log:[Build Time] " + step + " : 0.0s")), "Each step logs its elapsed time");
                    Check(Events.IndexOf("Upload") < Events.FindIndex(item => item.StartsWith("Build:")), "Upload before player build");
                    Check(Events.Contains("Build:StandaloneWindows:Builds/EXE/ProjectJ.exe:Development:enabled-scene"), "Player options and enabled scenes");
                    Check(Events.IndexOf("ActiveProfile:firebase-profile") < Events.IndexOf("ActiveBuilder:1") && Events.IndexOf("ActiveBuilder:1") < Events.IndexOf("Clean") && Events.IndexOf("Clean") < Events.IndexOf("Addressables"), "Firebase profile and builder are active before the content build");
                }
                Console.WriteLine("TRACE PreBuild/" + scenario + " " + string.Join("|", Events) + " errors=" + context.Errors.Count);
            }
            Console.WriteLine("ALL PASSED: 11 build flow scenarios");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }
    }

    private static void RunArgumentCase(string scenario)
    {
        Failure = "Success";
        var context = new Context();
        SynchronizationContext.SetSynchronizationContext(context);
        BuildMgr.PreBuild();
        context.Drain();

        Check(context.Errors.Count == 0, "Exceptions are handled at the command-line boundary");
        var rejected = new Dictionary<string, string>
        {
            { "Defaults", "Error:Build Target Is Not Supported : NoTarget" },
            { "UnsupportedTarget", "Error:Build Target Is Not Supported : WebGL" },
            { "InvalidTarget", "Log:Build Target Is Not Found : invalid" },
            { "MissingTarget", "Exception:IndexOutOfRangeException" },
            { "MissingVersion", "Exception:IndexOutOfRangeException" },
            { "MissingNumber", "Exception:IndexOutOfRangeException" },
            { "NoVersion", "Error:Build Version Or Number Is Missing :  / 7" },
            { "NoNumber", "Error:Build Version Or Number Is Missing : 1.0 / 0" },
            { "ZeroNumber", "Error:Build Version Or Number Is Missing : 1.0 / 0" },
            { "InvalidNumber", "Error:Build Number Is Invalid : abc" },
        };
        if (rejected.TryGetValue(scenario, out string reason))
        {
            Check(Events.SequenceEqual(new[] { reason, "Exit:1" }), "Rejected arguments fail before applying versions or external operations");
        }
        else
        {
            Check(Events.Last() == "Exit:0" && Events[Events.Count - 2] == "Delete", "Accepted arguments complete the flow");
            string expected = scenario == "IOS" ? "Uploader:iOS:1.0" : "Uploader:StandaloneWindows:3.2.1";
            Check(Events.Contains(expected), "Repeated options keep the last value");
            Check(Events.Contains(scenario == "IOS" ? "Version:1.0" : "Version:3.2.1") && Events.Contains(scenario == "IOS" ? "BuildNumber:9" : "BuildNumber:7"), "Version and build number are applied");
            if (scenario == "IOS")
                Check(Events.Contains("Build:iOS:Builds/IOS:None:enabled-scene"), "iOS runs the player build");
        }
        Console.WriteLine("TRACE Arguments/" + scenario + " " + string.Join("|", Events) + " errors=" + context.Errors.Count);
    }

    internal static Task Step(string name)
    {
        Events.Add(name);
        if (Failure == name)
            return Task.FromException(new InvalidOperationException(name));
        return name == "Upload" && UploadGate != null ? UploadGate.Task : Task.CompletedTask;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

// External operations are recorded, never executed by this fixture.
namespace UnityEditor.AddressableAssets.Settings
{
    public sealed class AddressableAssetSettings : UnityEngine.ScriptableObject
    {
        internal static readonly UnityEngine.ScriptableObject FirebaseBuilder = new DataBuilder();
        public readonly ProfileSettings profileSettings = new ProfileSettings();
        public readonly ProfileValue RemoteCatalogBuildPath = new ProfileValue();
        public List<UnityEngine.ScriptableObject> DataBuilders = BuildFlowTests.Failure == "UnregisteredBuilder"
            ? new List<UnityEngine.ScriptableObject> { new DataBuilder() }
            : new List<UnityEngine.ScriptableObject> { new DataBuilder(), FirebaseBuilder };
        public string activeProfileId { set { BuildFlowTests.Events.Add("ActiveProfile:" + value); } }
        public int ActivePlayerDataBuilderIndex { set { BuildFlowTests.Events.Add("ActiveBuilder:" + value); } }
        public static void CleanPlayerContent() { BuildFlowTests.Events.Add("Clean"); }
        public static void BuildPlayerContent(out Build.AddressablesPlayerBuildResult result)
        {
            BuildFlowTests.Step("Addressables").GetAwaiter().GetResult();
            result = new Build.AddressablesPlayerBuildResult { Error = BuildFlowTests.Failure == "ContentBuildError" ? "ContentBuildError" : null };
        }
    }

    public sealed class DataBuilder : UnityEngine.ScriptableObject, Build.IDataBuilder { }

    public sealed class ProfileSettings
    {
        public string GetProfileId(string name) { return BuildFlowTests.Failure == "NoProfile" || name != "FireBaseBuild" ? null : "firebase-profile"; }
    }

    public sealed class ProfileValue
    {
        public string GetValue(AddressableAssetSettings settings) { return string.Empty; }
    }
}

namespace UnityEditor.AddressableAssets.Build
{
    public interface IDataBuilder { }
    public sealed class AddressablesPlayerBuildResult { public string Error; }
}

namespace ProjectT.Server
{
    public sealed class FirebaseUploader
    {
        public FirebaseUploader(UnityEditor.BuildTarget target, string version)
        {
            BuildFlowTests.Events.Add("Uploader:" + target + ":" + version);
        }

        public Task UploadFiles() { return BuildFlowTests.Step("Upload"); }
        public void DeleteFiles() { BuildFlowTests.Events.Add("Delete"); }
    }
}

namespace UnityEngine
{
    public static class Debug
    {
        public static void Log(object value) { BuildFlowTests.Events.Add("Log:" + value); }
        public static void LogWarning(object value) { BuildFlowTests.Events.Add("Warning:" + value); }
        public static void LogError(object value) { BuildFlowTests.Events.Add("Error:" + value); }
        public static void LogException(Exception error) { BuildFlowTests.Events.Add("Exception:" + (error is InvalidOperationException || error is UnityEditor.Build.BuildFailedException ? error.Message : error.GetType().Name)); }
    }
}

namespace UnityEngine
{
    public class ScriptableObject { }
}

namespace UnityEditor
{
    public static class AssetDatabase
    {
        public static T LoadAssetAtPath<T>(string path) where T : class
        {
            if (path == "Assets/AddressableAssetsData/AddressableAssetSettings.asset")
                return BuildFlowTests.Failure == "NoSettings" ? null : new AddressableAssets.Settings.AddressableAssetSettings() as T;
            if (path == "Assets/AddressableAssetsData/DataBuilders/BuildScriptFirebaseMode.asset")
                return BuildFlowTests.Failure == "NoBuilder" ? null : AddressableAssets.Settings.AddressableAssetSettings.FirebaseBuilder as T;
            throw new InvalidOperationException("Unexpected asset " + path);
        }
    }
    public enum BuildTarget { NoTarget, Android, iOS, StandaloneWindows, WebGL }
    public static class PlayerSettings
    {
        public static string bundleVersion { set { BuildFlowTests.Events.Add("Version:" + value); } }
        public static class Android
        {
            public static int bundleVersionCode { set { BuildFlowTests.Events.Add("VersionCode:" + value); } }
        }
        public static class iOS
        {
            public static string buildNumber { set { BuildFlowTests.Events.Add("BuildNumber:" + value); } }
        }
    }
    public enum BuildOptions { None, Development }
    public sealed class MenuItem : Attribute { public MenuItem(string value) { } }
    public static class EditorApplication
    {
        public static bool isCompiling => false;
        public static void Exit(int code) { BuildFlowTests.Events.Add("Exit:" + code); }
    }
    public struct BuildPlayerOptions
    {
        public string[] scenes;
        public BuildTarget target;
        public BuildOptions options;
        public string locationPathName;
    }
    public sealed class EditorBuildSettingsScene
    {
        public bool enabled;
        public string path;
    }
    public static class EditorBuildSettings
    {
        public static EditorBuildSettingsScene[] scenes = {
            new EditorBuildSettingsScene { enabled = true, path = "enabled-scene" },
            new EditorBuildSettingsScene { enabled = false, path = "disabled-scene" }
        };
    }
    public static class BuildPipeline
    {
        public static Build.Reporting.BuildReport BuildPlayer(BuildPlayerOptions options)
        {
            BuildFlowTests.Events.Add($"Build:{options.target}:{options.locationPathName}:{options.options}:{string.Join(",", options.scenes)}");
            if (BuildFlowTests.Failure == "BuildThrow")
                throw new InvalidOperationException("BuildThrow");
            return new Build.Reporting.BuildReport { summary = new Build.Reporting.BuildSummary {
                result = BuildFlowTests.Failure == "BuildFailedReport" ? Build.Reporting.BuildResult.Failed : Build.Reporting.BuildResult.Succeeded,
                totalSize = 123
            } };
        }
    }
}

namespace UnityEditor.Build
{
    public sealed class BuildFailedException : Exception { public BuildFailedException(string message) : base(message) { } }
}

namespace UnityEditor.Build.Reporting
{
    public enum BuildResult { Succeeded, Failed }
    public sealed class BuildReport { public BuildSummary summary; }
    public struct BuildSummary { public BuildResult result; public ulong totalSize; public int totalErrors; }
}

namespace JetBrains.Annotations { }
namespace UnityEditor.AddressableAssets { }
namespace UnityEditor.AddressableAssets.Build.DataBuilders { }
namespace UnityEditor.AddressableAssets.Settings { }
namespace UnityEngine.AddressableAssets { }
namespace UnityEngine.Profiling { }
