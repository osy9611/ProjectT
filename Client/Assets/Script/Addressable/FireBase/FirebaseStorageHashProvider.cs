using Firebase;

using System;
using System.ComponentModel;
using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace ProjectT.Addressable
{
    /// <summary>
    /// Downloads Hash of the ContentCatalog from FirebaseStorage
    /// </summary>
    [DisplayName("Firebase Hash Provider")]
    public class FirebaseStorageHashProvider : ResourceProviderBase
    {
        public override void Provide(ProvideHandle provideHandle)
        {
            Debug.Log("Loading Firebase catalog hash.");

            FirebaseAddressablesManager.ResolveDownloadUrl(provideHandle, provideHandle.Location.InternalId, url =>
            {
                Debug.Log("Resolved Firebase catalog hash download URL.");

                var catalogLoc =
                    new ResourceLocationBase(url, url, typeof(TextDataProvider).FullName, typeof(string));

                provideHandle.ResourceManager.ProvideResource<string>(catalogLoc).Completed += handle =>
                {
                    Debug.Log("Got hash for catalog: " + handle.Result);
                    provideHandle.Complete(handle.Result, handle.Status == AsyncOperationStatus.Succeeded, handle.OperationException);
                    handle.Release();
                };
            });
        }
    }
}