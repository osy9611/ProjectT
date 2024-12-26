using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Firebase.Database;
using Unity.VisualScripting;
using UnityEngine;

namespace ProjectT.Server.DB
{
    [System.Serializable]
    public class BuildVersionData
    {
        public string Platform;
        public string Version;
    }

    public class FirebaseDB
    {
        private DatabaseReference dbRef;

        public FirebaseDB()
        {
            dbRef = FirebaseDatabase.DefaultInstance.RootReference;
        }

        public async void SaveBuildData(string platfrom, string version )
        {
            BuildVersionData data = new BuildVersionData();
            data.Platform = platfrom;
            data.Version = version;

            string jsonData = JsonUtility.ToJson(data);

            await dbRef.Child(platfrom).SetRawJsonValueAsync(jsonData);
        }

        public async Task<string> GetBuildVersion(string platform)
        {
            string version = string.Empty;

            await dbRef.Child(platform).GetValueAsync().ContinueWith(task =>
            {
                if (!task.IsCanceled && !task.IsFaulted)
                {
                    var resultData = task.Result;

                    foreach (var data in resultData.Children)
                    {
                        if (data.Key == "Version")
                        {
                            version = Convert.ToString(data.Value);
                        }
                    }
                }
            });

            return version;
        }
    }
}
