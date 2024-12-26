namespace ProjectT.Addressable
{
    using UnityEngine.ResourceManagement.AsyncOperations;

    public struct AsyncOperationDisposer : System.IDisposable
    {
        public AsyncOperationHandle Handle;

        public AsyncOperationDisposer(AsyncOperationHandle handle)
        {
            Handle = handle;
        }

        public void Dispose() 
        {
            if(Handle.IsValid())
            {
                UnityEngine.AddressableAssets.Addressables.Release(Handle);
            }
        }
    }
}