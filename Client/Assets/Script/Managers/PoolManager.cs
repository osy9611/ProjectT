using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectT.Pool;
using ProjectT.Skill;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

namespace ProjectT
{
    public class PoolManager : ManagerBase
    {
        private Dictionary<string, GameObjectPool> gameObjectPools = new Dictionary<string, GameObjectPool>();
        private Dictionary<System.Type, object> genericPools = new Dictionary<System.Type, object>();
        protected override UniTask OnInitializeAsync(CancellationToken token)
        {
            CreateRootObject(Context.Root, "PoolManager");
            return UniTask.CompletedTask;
        }


        protected override void OnShutdown(ShutdownReason reason)
        {
            ClearAll();
            genericPools.Clear();
        }

        public void CreatePool<T>() where T : new()
        {
            var type = typeof(T);
            if (genericPools.ContainsKey(type))
                return;

            genericPools[type] = new Pool<T>(() => new T(), true, 5, 20);
        }

        public void CreatePool(GameObject original, int count = 5)
        {
            GameObjectPool pool = new GameObjectPool();
            pool.Init(original, count);
            pool.Root.parent = RootObject;

            gameObjectPools.Add(original.name, pool);
        }

        public async UniTask CreatePoolAsync(GameObject original, int count = 5)
        {
            await UniTask.Yield(cancellationToken: LifetimeToken);
            GameObjectPool pool = new GameObjectPool();
            pool.Init(original, count);
            pool.Root.parent = RootObject;
            gameObjectPools.Add(original.name, pool);
        }

        public void Return<T>(T obj)
        {
            if (obj == null)
                return;

            var type = typeof(T);

            if (genericPools.TryGetValue(type, out var pool))
            {
                ((Pool<T>)pool).Return(obj);
            }
            else
            {
                Debug.LogWarning($"[PoolManager] Pool<{type.Name}> not found during Return");
            }
        }

        public bool Return(GameObject obj)
        {
            string name = obj.name;
            if (gameObjectPools.TryGetValue(name, out var pool))
            {
                pool.Return(obj);
                return true;
            }
            GameObject.Destroy(obj);

            return false;
        }

        public async UniTask<bool> ReturnAsync(GameObject obj)
        {
            await UniTask.Yield(cancellationToken: LifetimeToken);
            string name = obj.name;
            if (gameObjectPools.TryGetValue(name, out var pool))
            {
                pool.Return(obj);
                return true;
            }
            GameObject.Destroy(obj);

            return false;
        }

        public T Get<T>() where T : new()
        {
            var type = typeof(T);

            if (!genericPools.TryGetValue(type, out var pool))
            {
                CreatePool<T>();
                pool = genericPools[type];
            }

            return ((Pool<T>)pool).Get();
        }

        public GameObject Get(GameObject original, Transform parent = null)
        {
            if (!gameObjectPools.ContainsKey(original.name))
                CreatePool(original);

            return gameObjectPools[original.name].Get(parent);
        }

        public async UniTask<GameObject> GetAsync(GameObject original, Transform parent = null)
        {
            if (!gameObjectPools.ContainsKey(original.name))
                await CreatePoolAsync(original);

            return gameObjectPools[original.name].Get(parent);
        }

        public async UniTask<T> GetAsync<T>(GameObject original, Transform parent = null) where T : UnityEngine.Object
        {
            UnityEngine.Object result = await GetAsync(original, parent);
            return (T)result;
        }

        public GameObject GetOriginal(string name)
        {
            if (!gameObjectPools.ContainsKey(name))
                return null;

            return gameObjectPools[name].Original;
        }

        public void Release(GameObject go, bool isDestroy = false)
        {
            if (go == null)
            {
                Debug.LogError("Poolable object is null.");
                return;
            }

            if (isDestroy)
            {
                Return(go);
            }
            else
            {
                if (gameObjectPools.TryGetValue(go.name, out var gameObjectPool))
                {
                    UnityEngine.Object.Destroy(go);
                }
            }
        }

        public async UniTask ReleaseAsync(GameObject go, bool isDestroy = false)
        {
            if (go == null)
            {
                Debug.LogError("Poolable object is null.");
                return;
            }

            if (isDestroy)
            {
                await ReturnAsync(go);
            }
            else
            {
                if (gameObjectPools.TryGetValue(go.name, out var gameObjectPool))
                {
                    UnityEngine.Object.Destroy(go);
                }
            }
        }

        public void Clear()
        {

        }

        public void ClearAll()
        {
            foreach (var pool in gameObjectPools.Values) pool.Dispose();
            gameObjectPools.Clear();
        }
    }
}