using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
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

    public abstract class IResource
    {
        private readonly UniTaskCompletionSource<bool> ready = new UniTaskCompletionSource<bool>();
        private Action<IResource> onFailed;
        protected object resourceData;
        private Exception error;
        private bool isReleased;
        public string Path { get; private set; }
        public ResourceSource Source { get; private set; }
        internal ResourceRequest Request { get; private set; }
        public abstract Type AssetType { get; }
        private AsyncOperationHandle Handle { get; set; }
        internal HashSet<ResourceScope> Owners { get; } = new HashSet<ResourceScope>();
        public bool IsDone { get; private set; }
        internal UniTask<bool> WhenReady => ready.Task;

        internal void Initialize(string path, ResourceSource source, bool asynchronous, Action<IResource> onFailed)
        {
            Path = path;
            Source = source;
            this.onFailed = onFailed;

            if (source == ResourceSource.Addressables)
                Handle = LoadAddressable(path);
            else if (asynchronous)
                Request = Resources.LoadAsync(path, AssetType);
        }

        protected abstract AsyncOperationHandle LoadAddressable(string path);

        protected virtual void OnInitialize() { }

        protected virtual void OnRelease() { }

        private void SetResult(object asset)
        {
            resourceData = asset;

            try
            {
                OnInitialize();
            }
            catch (Exception exception)
            {
                Fail(exception);
                return;
            }

            IsDone = true;
            ready.TrySetResult(true);
        }

        internal void WaitForCompletion()
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
            CompleteResource(((ResourceRequest)operation).asset);
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

            if (Request != null)
                Request.completed -= CompleteResource;
            Request = null;
            SetResult(asset);
        }

        internal void Observe()
        {
            if (Source == ResourceSource.Resources)
            {
                // 완료 콜백이 등록 즉시 실행되면 Request가 비워지므로 지역 변수로 고정한다.
                var request = Request;

                if (request == null)
                {
                    WaitForCompletion();
                    return;
                }

                request.completed += CompleteResource;

                if (request.isDone)
                    CompleteResource(request);

                return;
            }

            Handle.Completed += Complete;

            if (Handle.IsDone)
                Complete(Handle);
        }

        internal void Complete(AsyncOperationHandle handle)
        {
            if (IsDone || isReleased)
                return;

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Fail(handle.OperationException ?? new InvalidOperationException($"Asset load failed: {Path}"));
                return;
            }

            handle.Completed -= Complete;
            SetResult(handle.Result);
        }

        internal void Fail(Exception exception)
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
            catch (Exception releaseError)
            {
                error = new AggregateException(exception, releaseError);
            }
            finally
            {
                ready.TrySetResult(true);
            }
        }

        public T GetResult<T>()
        {
            // 공유 리소스의 실패는 여러 호출자에게 전달되므로 최초 실패 지점의 스택을 보존한다.
            if (error != null)
                ExceptionDispatchInfo.Capture(error).Throw();

            if (isReleased)
                throw new ObjectDisposedException(Path);

            return (T)resourceData;
        }

        private void ReleaseHandle()
        {
            if (isReleased)
                return;

            isReleased = true;

            if (Request != null)
                Request.completed -= CompleteResource;
            Request = null;

            try
            {
                OnRelease();
            }
            finally
            {
                resourceData = null;
                if (Source == ResourceSource.Addressables && Handle.IsValid())
                {
                    Handle.Completed -= Complete;
                    Addressables.Release(Handle);
                }
            }
        }

        internal void Dispose()
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
