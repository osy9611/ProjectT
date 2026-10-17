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

            foreach (string scenario in new[] { "Success", "Addressables", "Upload", "BuildThrow", "BuildFailedReport", "PendingUpload" })
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
                Check(context.Errors.Count == 0, "Exceptions are handled at the command-line boundary");
                Check(Events.Contains("Delete") != failed, "Cleanup only after every step succeeded");
                Check(Events.Last() == (failed ? "Exit:1" : "Exit:0"), "Exit code is the final operation");
                Check(Events.Count(item => item.StartsWith("Exit:")) == 1, "Exit once");
                Check(Events.Count(item => item.StartsWith("Exception:")) == (failed ? 1 : 0), "Failure is reported once");
                if (scenario != "Addressables")
                    Check(Events.Contains("Uploader:StandaloneWindows:3.2.1"), "Build target and version");
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
                }
                Console.WriteLine("TRACE PreBuild/" + scenario + " " + string.Join("|", Events) + " errors=" + context.Errors.Count);
            }
            Console.WriteLine("ALL PASSED: 6 build flow scenarios");
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
namespace ProjectT.Build
{
    public static class AddressableBuild
    {
        public static Task Build() { return BuildFlowTests.Step("Addressables"); }
    }
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
        public static void LogError(object value) { BuildFlowTests.Events.Add("Error:" + value); }
        public static void LogException(Exception error) { BuildFlowTests.Events.Add("Exception:" + (error is InvalidOperationException || error is UnityEditor.Build.BuildFailedException ? error.Message : error.GetType().Name)); }
    }
}

namespace UnityEditor
{
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
