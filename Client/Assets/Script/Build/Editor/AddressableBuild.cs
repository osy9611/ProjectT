using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets;
using UnityEditor;
using UnityEngine;
using System.Web;
using UnityEngine.Profiling;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.Build;

namespace ProjectT.Build
{
    public class AddressableBuild
    {
        public static string GetLocalPath(string profileName)
        {
            string result = string.Empty;

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

            result =  settings.RemoteCatalogBuildPath.GetValue(settings);

            Debug.Log(result);
            return result;
        }
        public static async Task Build()
        {
            //Addressable Build를 하면 되는데 지금은 파이어 베이스 어드레서블 빌드만 하기 때문에 우선 구현함
            Debug.Log("Start FireBase Setting");
            FireBaseBuildSetting();

            while (EditorApplication.isCompiling)
            {
                Debug.Log("Compiling... Wait for a sec");
                await Task.Delay(5000);
            }

            Debug.Log("Clean Player Content");
            AddressableAssetSettings.CleanPlayerContent();

            Debug.Log("Build Player Content");
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
            if (!string.IsNullOrEmpty(result.Error))
                throw new BuildFailedException($"Addressable Build Failed : {result.Error}");
        }


        //FireBase는 기본 빌드랑은 다르게 빌드를 해야한다.
        private static void FireBaseBuildSetting()
        {
            string buildScript = "Assets/AddressableAssetsData/DataBuilders/BuildScriptFirebaseMode.asset";
            string settingAsset = "Assets/AddressableAssetsData/AddressableAssetSettings.asset";

            //Get Setting Object
            AddressableAssetSettings settings;
            settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(settingAsset) as AddressableAssetSettings;

            if (settings == null)
                throw new BuildFailedException($"{settingAsset} Couldn't be found or isn't a setting object .");

            //Set Profile
            string profileName = "FireBaseBuild";
            string profileId = settings.profileSettings.GetProfileId(profileName);
            if (string.IsNullOrEmpty(profileId))
                throw new BuildFailedException($"Couldn't find a profile named, {profileName}");
            settings.activeProfileId = profileId;

            //Set Builder
            IDataBuilder builderScript = AssetDatabase.LoadAssetAtPath<ScriptableObject>(buildScript) as IDataBuilder;
            if (builderScript == null)
                throw new BuildFailedException($"{buildScript} couldn't be found isn't a build script");

            int index = settings.DataBuilders.IndexOf((ScriptableObject)builderScript);
            if (index < 0)
                throw new BuildFailedException($"{builderScript} must be added to ths DataBuilders list before it can be made active.");
            settings.ActivePlayerDataBuilderIndex = index;
        }
    }
}