using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ProjectT.Addressable
{
    public enum ResourceSource
    {
        Addressables,
        Resources
    }

    internal sealed class ResourceEntry : IDisposable
    {
        private readonly UniTaskCompletionSource<bool> ready = new UniTaskCompletionSource<bool>();
        private readonly Action<ResourceEntry> onFailed;
        private object result;
        private Exception error;
        private bool isReleased;
        public string Path { get; }
        public ResourceSource Source { get; }
        public ResourceRequest Request { get; private set; }
        public Type AssetType { get; }
        public AsyncOperationHandle Handle { get; }
        public HashSet<ResourceScope> Owners { get; } = new HashSet<ResourceScope>();
        public bool IsDone { get; private set; }
        public UniTask<bool> WhenReady => ready.Task;

        public ResourceEntry(string path, Type assetType, AsyncOperationHandle handle, Action<ResourceEntry> onFailed)
        {
            Path = path;
            AssetType = assetType;
            Handle = handle;
            this.onFailed = onFailed;
        }

        public ResourceEntry(string path, Type assetType, bool asynchronous, Action<ResourceEntry> onFailed)
        {
            Path = path;
            AssetType = assetType;
            Source = ResourceSource.Resources;
            this.onFailed = onFailed;
            if (asynchronous)
                Request = Resources.LoadAsync(path, assetType);
        }

        public void WaitForCompletion()
        {
            if (Source == ResourceSource.Resources)
                CompleteResource(Resources.Load(Path, AssetType));
            else
            {
                Handle.WaitForCompletion();
                Complete(Handle);
            }
        }

        private void CompleteResource(AsyncOperation operation)
        {
            CompleteResource(Request.asset);
        }

        private void CompleteResource(UnityEngine.Object asset)
        {
            if (IsDone || isReleased)
                return;

            if (asset == null)
            {
                Fail(new InvalidOperationException($"Resources asset load failed: {Path}"));
                return;
            }

            IsDone = true;
            if (Request != null)
                Request.completed -= CompleteResource;
            Request = null;
            result = asset;
            ready.TrySetResult(true);
        }

        public void Observe()
        {
            if (Source == ResourceSource.Resources)
            {
                if (Request == null)
                    WaitForCompletion();
                else
                {
                    Request.completed += CompleteResource;
                    if (Request.isDone)
                        CompleteResource((AsyncOperation)Request);
                }
                return;
            }

            Handle.Completed += Complete;

            if (Handle.IsDone)
                Complete(Handle);
        }

        public void Complete(AsyncOperationHandle handle)
        {
            if (IsDone || isReleased)
                return;

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Fail(handle.OperationException ?? new InvalidOperationException($"Asset load failed: {Path}"));
                return;
            }

            IsDone = true;

            handle.Completed -= Complete;
            result = handle.Result;
            ready.TrySetResult(true);
        }

        public void Fail(Exception exception)
        {
            if (IsDone || isReleased)
                return;

            IsDone = true;
            error = exception;

            onFailed(this);

            try
            {
                ReleaseHandle();
            }
            finally
            {
                ready.TrySetResult(true);
            }
        }

        public T GetResult<T>()
        {
            if (error != null)
                throw error;

            if (isReleased)
                throw new ObjectDisposedException(Path);

            return (T)result;
        }

        private void ReleaseHandle()
        {
            if (isReleased)
                return;

            isReleased = true;

            if (Request != null)
                Request.completed -= CompleteResource;
            Request = null;

            if (Source == ResourceSource.Addressables && Handle.IsValid())
            {
                Handle.Completed -= Complete;
                Addressables.Release(Handle);
            }

            result = null;
        }

        public void Dispose()
        {
            try
            {
                ReleaseHandle();
            }
            finally
            {
                ready.TrySetCanceled();
            }
        }
    }
}
