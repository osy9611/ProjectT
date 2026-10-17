using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ProjectT.Server;

internal static class UploadFlowTests
{
    internal static readonly List<string> Events = new List<string>();
    internal static readonly List<TaskCompletionSource<Firebase.Storage.StorageMetadata>> Uploads = new List<TaskCompletionSource<Firebase.Storage.StorageMetadata>>();

    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "ProjectTUploadFlowTests");
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
            Directory.CreateDirectory(Path.Combine(root, "Assets"));
            UnityEngine.Application.dataPath = Path.Combine(root, "Assets");

            foreach (string scenario in new[] { "Success", "Pending", "Failure" })
            {
                Events.Clear();
                Uploads.Clear();
                string folder = Path.Combine(root, "ServerData");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "a.bundle"), "a");
                File.WriteAllText(Path.Combine(folder, "b.bundle"), "b");

                var uploader = new FirebaseUploader(UnityEditor.BuildTarget.Android, "1.0");
                Task upload = uploader.UploadFiles();
                Check(Uploads.Count == 2, "Every local file starts uploading");
                Check(!upload.IsCompleted, "UploadFiles waits for pending uploads");

                if (scenario == "Success")
                {
                    Uploads.ForEach(item => item.SetResult(new Firebase.Storage.StorageMetadata()));
                    Check(upload.Wait(5000) && upload.Status == TaskStatus.RanToCompletion, "Completed uploads complete UploadFiles");
                    Check(Events.Count(item => item.EndsWith("upload completed successfully.")) == 2, "Each file reports completion");
                    Check(Events.Last() == "Log:All uploads completed successfully.", "Completion is logged after every file");
                }
                else if (scenario == "Pending")
                {
                    Uploads[0].SetResult(new Firebase.Storage.StorageMetadata());
                    Check(!upload.Wait(200), "One pending file keeps UploadFiles pending");
                    Check(!Events.Contains("Log:All uploads completed successfully."), "No completion log while pending");
                    Uploads[1].SetResult(new Firebase.Storage.StorageMetadata());
                    Check(upload.Wait(5000), "UploadFiles completes after the last file");
                }
                else
                {
                    Uploads[0].SetResult(new Firebase.Storage.StorageMetadata());
                    Uploads[1].SetException(new InvalidOperationException("upload failed"));
                    Check(WaitFaulted(upload), "Failed upload faults UploadFiles");
                    Check(upload.Exception.InnerException.Message == "upload failed", "Original exception is preserved");
                    Check(!Events.Contains("Log:All uploads completed successfully."), "No completion log after failure");
                }

                Console.WriteLine("TRACE Upload/" + scenario + " " + string.Join("|", Events.Where(item => !item.Contains("Progress"))) + " status=" + upload.Status);
                Check(Directory.Exists(folder), "UploadFiles never deletes local files");
                uploader.DeleteFiles();
            }
            Console.WriteLine("ALL PASSED: 3 upload scenarios");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static bool WaitFaulted(Task task)
    {
        try
        {
            task.Wait(5000);
        }
        catch (AggregateException)
        {
        }
        return task.IsFaulted;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

// Firebase and Unity are replaced by recording stubs; nothing is uploaded.
namespace Firebase.Storage
{
    public sealed class StorageMetadata { }
    public sealed class MetadataChange { }
    public sealed class UploadState
    {
        public long BytesTransferred;
        public long TotalByteCount;
    }

    public sealed class StorageProgress<T> : IProgress<T>
    {
        private readonly Action<T> callback;
        public StorageProgress(Action<T> callback) { this.callback = callback; }
        public void Report(T value) { callback(value); }
    }

    public sealed class FirebaseStorage
    {
        public static FirebaseStorage DefaultInstance { get; } = new FirebaseStorage();
        public StorageReference GetReferenceFromUrl(string url) { return new StorageReference(url); }
    }

    public sealed class StorageReference
    {
        private readonly string path;
        public StorageReference(string path) { this.path = path; }
        public StorageReference Child(string child) { return new StorageReference(path + "/" + child); }

        public Task<StorageMetadata> PutFileAsync(string filePath, MetadataChange metadata, IProgress<UploadState> progress)
        {
            UploadFlowTests.Events.Add("Put:" + path);
            progress.Report(new UploadState { BytesTransferred = 1, TotalByteCount = 1 });
            var upload = new TaskCompletionSource<StorageMetadata>();
            UploadFlowTests.Uploads.Add(upload);
            return upload.Task;
        }
    }
}

namespace UnityEngine
{
    public class Object { }
    public class ScriptableObject : Object { }

    public static class Application
    {
        public static string dataPath;
    }

    public static class Debug
    {
        public static void Log(object value) { lock (UploadFlowTests.Events) UploadFlowTests.Events.Add("Log:" + value); }
        public static void LogWarning(object value) { lock (UploadFlowTests.Events) UploadFlowTests.Events.Add("Warning:" + value); }
        public static void LogError(object value) { lock (UploadFlowTests.Events) UploadFlowTests.Events.Add("Error:" + value); }
    }
}

namespace UnityEditor
{
    public enum BuildTarget { NoTarget, Android, iOS, StandaloneWindows }

    public static class AssetDatabase
    {
        public static T LoadAssetAtPath<T>(string path) where T : UnityEngine.Object
        {
            return new AddressableAssets.Settings.AddressableAssetSettings() as T;
        }
    }
}

namespace UnityEditor.AddressableAssets.Settings
{
    public sealed class AddressableAssetSettings : UnityEngine.ScriptableObject
    {
        public ProfileSettings profileSettings = new ProfileSettings();
        public ProfileValue RemoteCatalogBuildPath = new ProfileValue();
    }

    public sealed class ProfileSettings
    {
        public string GetProfileId(string name) { return name == "FireBaseBuild" ? "profile" : null; }
    }

    public sealed class ProfileValue
    {
        public string GetValue(AddressableAssetSettings settings) { return "ServerData"; }
    }
}
