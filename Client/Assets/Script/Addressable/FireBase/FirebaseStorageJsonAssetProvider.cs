using Firebase;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace ProjectT.Addressable
{
    /// <summary>
    /// Loads JSON data from FirebaseStorage. Needed mainly for ContentCatalog downloads
    /// </summary>
    [DisplayName("Firebase Json Asset Provider")]
    public class FirebaseStorageJsonAssetProvider : JsonAssetProvider
    {
        /// <summary>
        /// Unfortunately we have to override this because the method CanProvide is only called once and when the InternalId
        /// changes this Provider is still selected for non-Firebase Json data. We just call into base when that happens
        /// </summary>
        public override string ProviderId => typeof(JsonAssetProvider).FullName;

        public override void Provide(ProvideHandle provideHandle)
        {
            var url = UnityEngine.AddressableAssets.Addressables.ResourceManager.TransformInternalId(provideHandle.Location);
            if (FirebaseAddressablesManager.IsFirebaseStorageLocation(url) == false)
            {
                base.Provide(provideHandle);
                return;
            }

            Debug.Log("Loading Firebase catalog.");

            FirebaseAddressablesManager.ResolveDownloadUrl(provideHandle, url, downloadUrl =>
            {
                Debug.Log("Resolved Firebase catalog download URL.");

                var catalogLoc = new ResourceLocationBase(downloadUrl, downloadUrl, typeof(JsonAssetProvider).FullName, typeof(IResourceLocator));

                if (provideHandle.Location.ResourceType == typeof(ContentCatalogData))
                {
                    provideHandle.ResourceManager.ProvideResource<ContentCatalogData>(catalogLoc).Completed += handle =>
                    {
                        provideHandle.Complete(handle.Result, handle.Status == AsyncOperationStatus.Succeeded, handle.OperationException);
                        handle.Release();
                    };
                }
                else
                {
                    provideHandle.Complete<object>(null, false,
                        new NotSupportedException($"Firebase JSON provider cannot load {provideHandle.Location.ResourceType}."));
                }
            });
        }
    }
}
