using System.ComponentModel;

using UnityEngine.ResourceManagement.ResourceProviders;

namespace ProjectT.Addressable
{
    /// <summary>
    /// Loads bundles from the Firebase Storage CDN
    /// </summary>
    [DisplayName("Firebase AssetBundle Provider")]
    public class FirebaseStorageAssetBundleProvider : AssetBundleProvider
    {
        public override void Provide(ProvideHandle provideHandle)
        {
            string firebaseUrl = provideHandle.ResourceManager.TransformInternalId(provideHandle.Location);
            if (FirebaseAddressablesManager.IsFirebaseStorageLocation(firebaseUrl) == false)
            {
                base.Provide(provideHandle);
                return;
            }

            // 기본 프로바이더는 로드를 시작할 때 TransformInternalId로 주소를 읽으므로 매핑을 먼저 등록하면 받은 URL에서 번들을 읽는다.
            FirebaseAddressablesManager.ResolveDownloadUrl(provideHandle, firebaseUrl, url =>
            {
                FirebaseAddressablesCache.SetInternalIdToStorageUrlMapping(firebaseUrl, url);
                base.Provide(provideHandle);
            });
        }
    }

}
