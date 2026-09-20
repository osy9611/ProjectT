using System;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ProjectT.Addressable
{
    public class Resource<T> : IResource
    {
        public sealed override Type AssetType => typeof(T);
        public T Asset => GetResult<T>();
        protected T ResourceData => (T)resourceData;

        protected sealed override AsyncOperationHandle LoadAddressable(string path)
        {
            return Addressables.LoadAssetAsync<T>(path);
        }
    }
}
