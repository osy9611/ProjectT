using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ProjectT.Addressable
{
    public class Resource<T> : ResourceBase
    {
        public T Asset => GetResult<T>();
        protected T ResourceData => (T)resourceData;

        protected sealed override AsyncOperationHandle LoadAddressable(string path)
        {
            return Addressables.LoadAssetAsync<T>(path);
        }
    }
}
