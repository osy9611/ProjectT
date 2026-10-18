using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectT.Addressable;
using ProjectT.Pool;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectT
{
    public class PoolManager : ManagerBase
    {
        // Scope 하나의 풀 상태다. Scope가 해제되면 Registration이 항목을 지운다.
        private sealed class ScopePools
        {
            public readonly Dictionary<GameObject, GameObjectPool> Pools = new Dictionary<GameObject, GameObjectPool>();
            public CancellationTokenRegistration Registration;
            // ClearScope 이후 해제 전까지 같은 Scope로 새 풀이 만들어지지 않도록 막는다.
            public bool IsClosed;
        }

        private readonly Dictionary<ResourceScope, ScopePools> scopes = new Dictionary<ResourceScope, ScopePools>();
        // 경로로 로드한 원본도 PoolManager 종료 시 풀 인스턴스와 함께 반환되도록 ResourceManager.AppScope 대신 전용 Scope를 사용한다.
        private ResourceScope appScope;

        protected override UniTask OnInitializeAsync(CancellationToken token)
        {
            CreateRootObject("PoolManager");
            appScope = Global.Resource.CreateScope();
            return UniTask.CompletedTask;
        }

        protected override void OnShutdown(ShutdownReason reason)
        {
            try
            {
                Clear();
            }
            finally
            {
                appScope?.Dispose();
                appScope = null;
            }
        }

        public void CreatePool(GameObject original, int count = 5, ResourceScope scope = null)
        {
            GetOrCreatePoolInternal(original, count, scope);
        }

        public GameObject Get(GameObject original, Transform parent = null, ResourceScope scope = null)
        {
            return GetOrCreatePoolInternal(original, 0, scope).Get(parent);
        }

        public GameObject Get(string path, Transform parent = null, ResourceScope scope = null)
        {
            scope = scope ?? appScope;
            ThrowIfScopeClosedInternal(scope);
            var original = Global.Resource.LoadAndGet<GameObject>(path, scope: scope);
            return GetOrCreatePoolInternal(original, 0, scope).Get(parent);
        }

        public async UniTask<GameObject> GetAsync(string path, Transform parent = null, ResourceScope scope = null, CancellationToken cancelToken = default)
        {
            scope = scope ?? appScope;
            ThrowIfScopeClosedInternal(scope);
            var original = await Global.Resource.LoadAndGetAsync<GameObject>(path, cancelToken: cancelToken, scope: scope);
            return GetOrCreatePoolInternal(original, 0, scope).Get(parent);
        }

        public bool Return(GameObject obj)
        {
            var item = obj == null ? null : obj.GetComponent<PooledObject>();
            if (item == null || item.Owner == null)
                return false;

            item.Owner.Return(obj);
            return true;
        }

        public void Destroy(GameObject obj)
        {
            if (obj == null)
                return;

            var item = obj.GetComponent<PooledObject>();
            if (item == null || item.Owner == null || !item.Owner.Remove(obj))
                UnityEngine.Object.Destroy(obj);
        }

        public GameObject GetOriginal(string name)
        {
            GameObject result = null;

            foreach (var entry in scopes.Values)
            {
                foreach (var pool in entry.Pools.Values)
                {
                    if (pool.Original == null || pool.Original.name != name)
                        continue;

                    if (result != null && !ReferenceEquals(result, pool.Original))
                        throw new InvalidOperationException($"Ambiguous original name: {name}");

                    result = pool.Original;
                }
            }

            return result;
        }

        public void Clear()
        {
            var objects = new List<GameObjectPool>();
            foreach (var entry in scopes.Values)
            {
                entry.Registration.Dispose();
                objects.AddRange(entry.Pools.Values);
            }

            scopes.Clear();

            List<Exception> errors = null;
            foreach (var pool in objects)
            {
                ErrorCollector.Run(ref errors, pool, item => item.Dispose());
            }

            ErrorCollector.ThrowIfAny(errors);
        }

        public void ClearScope(ResourceScope scope)
        {
            // 해제된 Scope는 ResolveScope가 거부하므로 닫지 않고 남은 풀만 정리한다.
            if (scope.Token.IsCancellationRequested)
            {
                ReleaseScopeInternal(scope);
                return;
            }

            // 정리 중 콜백의 대여도 거부되도록 풀 해제보다 먼저 닫는다. 닫힌 항목은 Scope가 해제될 때 함께 지워진다.
            if (!scopes.TryGetValue(scope, out var entry))
                entry = AddScopeInternal(scope);

            entry.IsClosed = true;
            ErrorCollector.ThrowIfAny(DisposePoolsInternal(entry));
        }

        private GameObjectPool GetOrCreatePoolInternal(GameObject original, int count, ResourceScope scope)
        {
            ThrowIfWorkUnavailableInternal();

            if (original == null)
                throw new ArgumentNullException(nameof(original));

            // 해제되었거나 ResourceManager가 관리하지 않는 Scope로는 대여를 시작하지 않는다.
            scope = Global.Resource.ResolveScope(scope ?? appScope);
            ThrowIfScopeClosedInternal(scope);

            scopes.TryGetValue(scope, out var entry);
            if (entry != null && entry.Pools.TryGetValue(original, out var pool))
                return pool;

            pool = new GameObjectPool(original, count, RootObject);
            if (entry == null)
                entry = AddScopeInternal(scope);

            entry.Pools.Add(original, pool);
            return pool;
        }

        private ScopePools AddScopeInternal(ResourceScope scope)
        {
            var entry = new ScopePools();
            scopes.Add(scope, entry);
            entry.Registration = scope.Token.Register(() => ReleaseScopeInternal(scope));
            return entry;
        }

        private void ThrowIfScopeClosedInternal(ResourceScope scope)
        {
            if (scopes.TryGetValue(scope, out var entry) && entry.IsClosed)
                throw new InvalidOperationException("Resource scope is closed for new pooled objects.");
        }

        // Scope 취소 콜백은 리소스 반환보다 먼저 실행되므로 원본이 해제되기 전에 인스턴스 파괴를 요청한다.
        private void ReleaseScopeInternal(ResourceScope scope)
        {
            if (!scopes.TryGetValue(scope, out var entry))
                return;

            var errors = DisposePoolsInternal(entry);
            scopes.Remove(scope);
            ErrorCollector.ThrowIfAny(errors);
        }

        // 정리 콜백이 남은 풀을 볼 수 있도록 풀마다 목록에서 뺀 직후 해제한다.
        private static List<Exception> DisposePoolsInternal(ScopePools entry)
        {
            List<Exception> errors = null;
            foreach (var pair in new List<KeyValuePair<GameObject, GameObjectPool>>(entry.Pools))
            {
                entry.Pools.Remove(pair.Key);
                ErrorCollector.Run(ref errors, pair.Value, pool => pool.Dispose());
            }

            return errors;
        }
    }
}
