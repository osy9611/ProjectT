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
            string firebaseUrl = provideHandle.Location.InternalId;
            if (FirebaseAddressablesManager.IsFirebaseStorageLocation(firebaseUrl) == false || FirebaseAddressablesCache.HasStorageUrl(firebaseUrl))
            {
                base.Provide(provideHandle);
                return;
            }

            // 기본 프로바이더는 웹 요청을 보내기 전에 WebRequestOverride를 부르므로 매핑을 먼저 등록하면 받은 URL에서 번들을 읽는다.
            FirebaseAddressablesManager.ResolveDownloadUrl(provideHandle, firebaseUrl, url =>
            {
                FirebaseAddressablesCache.SetInternalIdToStorageUrlMapping(firebaseUrl, url);
                base.Provide(provideHandle);
            });
        }
    }

}
