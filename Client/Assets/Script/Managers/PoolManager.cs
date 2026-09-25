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
        private readonly struct PoolKey : IEquatable<PoolKey>
        {
            public readonly ResourceScope Scope;
            public readonly GameObject Original;

            public PoolKey(ResourceScope scope, GameObject original)
            {
                Scope = scope;
                Original = original;
            }

            public bool Equals(PoolKey other)
            {
                return ReferenceEquals(Scope, other.Scope) && ReferenceEquals(Original, other.Original);
            }

            public override bool Equals(object obj)
            {
                return obj is PoolKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Scope, Original);
            }
        }

        private readonly Dictionary<PoolKey, GameObjectPool> gameObjectPools = new Dictionary<PoolKey, GameObjectPool>();
        private readonly Dictionary<Type, IDisposable> genericPools = new Dictionary<Type, IDisposable>();
        private readonly Dictionary<ResourceScope, CancellationTokenRegistration> scopeRegistrations = new Dictionary<ResourceScope, CancellationTokenRegistration>();
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

        public void CreatePool<T>() where T : new()
        {
            GetOrCreatePoolInternal<T>();
        }

        public T Get<T>() where T : new()
        {
            return GetOrCreatePoolInternal<T>().Get();
        }

        public void Return<T>(T obj)
        {
            if (obj == null)
                return;

            if (!genericPools.TryGetValue(typeof(T), out var pool))
                throw new InvalidOperationException($"Pool<{typeof(T).Name}> not found.");

            ((Pool<T>)pool).Return(obj);
        }

        public void CreatePool(GameObject original, int count = 5, ResourceScope scope = null)
        {
            GetOrCreatePoolInternal(original, count, scope);
        }

        public GameObject Get(GameObject original, Transform parent = null, ResourceScope scope = null)
        {
            return GetOrCreatePoolInternal(original, 0, scope).Get(parent);
        }

        public GameObject Get(string path, Transform parent = null, ResourceScope scope = null, ResourceSource source = ResourceSource.Addressables)
        {
            scope = scope ?? appScope;
            var original = Global.Resource.LoadAndGet<GameObject>(path, scope: scope, source: source);
            return GetOrCreatePoolInternal(original, 0, scope).Get(parent);
        }

        public async UniTask<GameObject> GetAsync(string path, Transform parent = null, ResourceScope scope = null, ResourceSource source = ResourceSource.Addressables, CancellationToken cancelToken = default)
        {
            scope = scope ?? appScope;
            var original = await Global.Resource.LoadAndGetAsync<GameObject>(path, cancelToken: cancelToken, scope: scope, source: source);
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

        public void Release(GameObject obj, bool isDestroy = false)
        {
            if (obj == null)
                return;

            if (!isDestroy)
            {
                Return(obj);
                return;
            }

            var item = obj.GetComponent<PooledObject>();
            if (item == null || item.Owner == null || !item.Owner.Remove(obj))
                UnityEngine.Object.Destroy(obj);
        }

        public GameObject GetOriginal(string name)
        {
            GameObject result = null;

            foreach (var pool in gameObjectPools.Values)
            {
                if (pool.Original == null || pool.Original.name != name)
                    continue;

                if (result != null && !ReferenceEquals(result, pool.Original))
                    throw new InvalidOperationException($"Ambiguous original name: {name}");

                result = pool.Original;
            }

            return result;
        }

        public void Clear()
        {
            var objects = new List<GameObjectPool>(gameObjectPools.Values);
            var generics = new List<IDisposable>(genericPools.Values);

            gameObjectPools.Clear();
            genericPools.Clear();

            foreach (var registration in scopeRegistrations.Values)
            {
                registration.Dispose();
            }

            scopeRegistrations.Clear();

            List<Exception> errors = null;
            foreach (var pool in objects)
            {
                ErrorCollector.Run(ref errors, pool, item => item.Dispose());
            }

            foreach (var pool in generics)
            {
                ErrorCollector.Run(ref errors, pool, item => item.Dispose());
            }

            ErrorCollector.ThrowIfAny(errors);
        }

        private Pool<T> GetOrCreatePoolInternal<T>() where T : new()
        {
            ThrowIfWorkUnavailableInternal();

            if (!genericPools.TryGetValue(typeof(T), out var pool))
            {
                pool = new Pool<T>(() => new T(), true, 0, 20);
                genericPools.Add(typeof(T), pool);
            }

            return (Pool<T>)pool;
        }

        private GameObjectPool GetOrCreatePoolInternal(GameObject original, int count, ResourceScope scope)
        {
            ThrowIfWorkUnavailableInternal();

            if (original == null)
                throw new ArgumentNullException(nameof(original));

            // 해제되었거나 ResourceManager가 관리하지 않는 Scope로는 대여를 시작하지 않는다.
            scope = Global.Resource.ResolveScope(false, scope ?? appScope);

            var key = new PoolKey(scope, original);
            if (gameObjectPools.TryGetValue(key, out var pool))
                return pool;

            pool = new GameObjectPool();
            pool.Init(original, count, RootObject);
            gameObjectPools.Add(key, pool);

            if (!scopeRegistrations.ContainsKey(scope))
                scopeRegistrations.Add(scope, scope.Token.Register(() => ReleaseScopeInternal(scope)));

            return pool;
        }

        // Scope 취소 콜백은 리소스 반환보다 먼저 실행되므로 원본이 해제되기 전에 인스턴스 파괴를 요청한다.
        private void ReleaseScopeInternal(ResourceScope scope)
        {
            scopeRegistrations.Remove(scope);

            List<Exception> errors = null;
            foreach (var pair in new List<KeyValuePair<PoolKey, GameObjectPool>>(gameObjectPools))
            {
                if (!ReferenceEquals(pair.Key.Scope, scope))
                    continue;

                gameObjectPools.Remove(pair.Key);
                ErrorCollector.Run(ref errors, pair.Value, pool => pool.Dispose());
            }

            ErrorCollector.ThrowIfAny(errors);
        }
    }
}
