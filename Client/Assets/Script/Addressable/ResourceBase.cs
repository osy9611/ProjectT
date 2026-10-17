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

    public abstract class ResourceBase
    {
        // Loading에서만 Ready·Failed·ReleasedBeforeReady로 넘어가고, Ready는 해제되면 Released가 된다.
        private enum State
        {
            Loading,
            Ready,
            Failed,
            // 완료 전에 해제되었으므로 IsDone은 false로 남는다.
            ReleasedBeforeReady,
            Released
        }

        private readonly UniTaskCompletionSource<bool> ready = new UniTaskCompletionSource<bool>();
        private Action<ResourceBase> onFailed;
        protected object resourceData;
        private State state;
        // Failed일 때만 값이 있다.
        private Exception error;
        public string Path { get; private set; }
        public ResourceSource Source { get; private set; }
        internal ResourceRequest Request { get; private set; }
        public abstract Type AssetType { get; }
        private AsyncOperationHandle Handle { get; set; }
        internal HashSet<ResourceScope> Owners { get; } = new HashSet<ResourceScope>();
        public bool IsDone => state == State.Ready || state == State.Failed || state == State.Released;
        internal UniTask<bool> WhenReady => ready.Task;

        internal void Initialize(string path, ResourceSource source, bool asynchronous, Action<ResourceBase> onFailed)
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

            // OnInitialize 안에서 해제·실패·재완료될 수 있어 그 결과를 덮어쓰지 않는다.
            if (state == State.Loading)
                state = State.Ready;
            else if (state == State.ReleasedBeforeReady)
                state = State.Released;
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
            if (state != State.Loading)
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

                // 동기 Resources 로드는 요청을 만들지 않으므로 관찰 시점에 바로 로드를 완료한다.
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
            if (state != State.Loading)
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
            if (state != State.Loading)
                return;

            state = State.Failed;
            error = exception;

            onFailed(this);

            try
            {
                ReleaseHandleInternal();
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
            ThrowIfFailed();

            return (T)resourceData;
        }

        internal void ThrowIfFailed()
        {
            // 공유 리소스의 실패는 여러 호출자에게 전달되므로 최초 실패 지점의 스택을 보존한다.
            if (state == State.Failed)
                ExceptionDispatchInfo.Capture(error).Throw();

            if (state == State.Released || state == State.ReleasedBeforeReady)
                throw new ObjectDisposedException(Path);
        }

        // 호출자가 상태를 먼저 바꾼 뒤 호출하므로, OnRelease 안에서 다시 해제를 요청해도 한 번만 실행된다.
        private void ReleaseHandleInternal()
        {
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
                if (state == State.Loading || state == State.Ready)
                {
                    state = state == State.Loading ? State.ReleasedBeforeReady : State.Released;
                    ReleaseHandleInternal();
                }
            }
            finally
            {
                ready.TrySetCanceled();
            }
        }
    }
}
