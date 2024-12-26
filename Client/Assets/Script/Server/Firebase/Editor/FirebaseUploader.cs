using Firebase.Storage;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace ProjectT.Server
{
    public class UploadData
    {
        public string URL;
        public string LocalFolderPath;
        public string ServerFolderPath;
        public string[] FilePaths;

        public void SetData(BuildTarget buildTarget, string buildVersion)
        {
            //Set URL
            URL = $"gs://projectt-1179c.appspot.com";

            //Set LocalFilePath
            string profileName = "FireBaseBuild";
            LocalFolderPath = Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length);
            LocalFolderPath += GetLocalPath(profileName);

            //Set ServerFilePath
            switch (buildTarget)
            {
                case BuildTarget.Android:
                    ServerFolderPath = $"Android/{buildVersion}";
                    break;
                case BuildTarget.iOS:
                    ServerFolderPath = $"IPhone/{buildVersion}";
                    break;
                case BuildTarget.StandaloneWindows:
                    ServerFolderPath = $"Window/{buildVersion}";
                    break;
            }

            FilePaths = Directory.GetFiles(LocalFolderPath);
        }

        public static string GetLocalPath(string profileName)
        {
            string settingAsset = "Assets/AddressableAssetsData/AddressableAssetSettings.asset";
            AddressableAssetSettings settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(settingAsset) as AddressableAssetSettings;

            if (settings == null)
                return string.Empty;

            //Set Profile
            string profileId = settings.profileSettings.GetProfileId(profileName);
            if (string.IsNullOrEmpty(profileId))
            {
                Debug.LogWarning($"Couldn't find a profile named, {profileName}");
                return string.Empty;
            }

            string result = settings.RemoteCatalogBuildPath.GetValue(settings);
            return result;
        }
    }

    public class FirebaseUploader
    {
        private FirebaseStorage storage;
        private StorageReference storageRef;
        private UploadData uploadData;
        public FirebaseUploader(BuildTarget buildTarget, string buildVersion)
        {
            uploadData = new UploadData();
            uploadData.SetData(buildTarget, buildVersion);
        }

        public async System.Threading.Tasks.Task UploadFiles()
        {
            storage = FirebaseStorage.DefaultInstance;
            storageRef = storage.GetReferenceFromUrl(uploadData.URL);

            Debug.Log($"Upload File Count {uploadData.FilePaths.Length}");

            var uploadTasks = uploadData.FilePaths.Select(async filePath =>
            {
                string fileName = System.IO.Path.GetFileName(filePath);
                StorageReference targetRef = storageRef.Child($"{uploadData.ServerFolderPath}/{fileName}");
                var newMetaData = new MetadataChange();

                return targetRef.PutFileAsync(filePath, newMetaData, new StorageProgress<UploadState>
                    (state =>
                    {
                        Debug.Log($"{fileName} - Progress : {state.BytesTransferred} of {state.TotalByteCount} bytes transferred.");
                    }))
                    .ContinueWith(task =>
                    {
                        if (task.IsFaulted)
                        {
                            // 실패 처리 추가
                            Debug.LogError($"{fileName} upload failed: {task.Exception}");
                        }
                        else if (task.IsCanceled)
                        {
                            // 취소 처리 추가
                            Debug.LogWarning($"{fileName} upload canceled.");
                        }
                        else
                        {
                            // 성공 처리 추가
                            Debug.Log($"{fileName} upload completed successfully.");
                        }
                    });
            });

            await Task.WhenAll(uploadTasks);

            Debug.Log("All uploads completed successfully.");
        }

        public void DeleteFiles()
        {
            Directory.Delete(uploadData.LocalFolderPath, true);
        }
    }


}
