using Cysharp.Threading.Tasks;
using ProjectT.Pool;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

namespace ProjectT
{
    public class PoolManager : ManagerBase
    {
        private Dictionary<string, GameObjectPool> pools = new Dictionary<string, GameObjectPool>();

        #region ManagerBase
        public override void OnEnter()
        {
            CreateRootObject(Global.Instance.transform, "PoolManager");
        }

        public override void OnFixedUpdate(float dt)
        {
        }

        public override void OnLateUpdate()
        {
        }

        public override void OnLeave()
        {
        }

        public override void OnUpdate(float dt)
        {
        }

        public override void OnAppStart()
        {
        }
        public override void OnAppEnd()
        {
        }

        public override void OnAppFocuse(bool focused)
        {
        }

        public override void OnAppPause(bool paused)
        {
        }
        #endregion

        public void CreatePool(GameObject original, int count = 5)
        {
            GameObjectPool pool = new GameObjectPool();
            pool.Init(original, count);
            pool.Root.parent = RootObject;

            pools.Add(original.name, pool);
        }

        public async UniTask CreatePoolAsync(GameObject original, int count = 5)
        {
            await UniTask.Yield();
            GameObjectPool pool = new GameObjectPool();
            pool.Init(original, count);
            pool.Root.parent = RootObject;
            pools.Add(original.name, pool);
        }

        public bool Return(GameObject obj)
        {
            string name = obj.name;
            if (pools.TryGetValue(name, out var pool))
            {
                pool.Return(obj);
                return true;
            }
            GameObject.Destroy(obj);

            return false;
        }

        public async UniTask<bool> ReturnAsync(GameObject obj)
        {
            await UniTask.Yield();
            string name = obj.name;
            if (pools.TryGetValue(name, out var pool))
            {
                pool.Return(obj);
                return true;
            }
            GameObject.Destroy(obj);

            return false;
        }

        public GameObject Get(GameObject original, Transform parent = null)
        {
            if (!pools.ContainsKey(original.name))
                CreatePool(original);

            return pools[original.name].Get(parent);
        }

        public async UniTask<GameObject> GetAsync(GameObject original, Transform parent = null)
        {
            if (!pools.ContainsKey(original.name))
                await CreatePoolAsync(original);

            return pools[original.name].Get(parent);
        }

        public GameObject GetOriginal(string name)
        {
            if (!pools.ContainsKey(name))
                return null;

            return pools[name].Original;
        }

        public void Release(GameObject go, bool isDestroy = false)
        {
            if (go == null)
            {
                Global.Instance.LogError($"Poolable Component Is Null {go.name}");
                return;
            }

            if (isDestroy)
            {
                Return(go);
            }
            else
            {
                if (pools.TryGetValue(go.name, out var gameObjectPool))
                {
                    UnityEngine.Object.Destroy(go);
                }
            }
        }

        public async UniTask ReleaseAsync(GameObject go, bool isDestroy = false)
        {
            if (go == null)
            {
                Global.Instance.LogError($"Poolable Component Is Null {go.name}");
                return;
            }

            if (isDestroy)
            {
                await ReturnAsync(go);
            }
            else
            {
                if (pools.TryGetValue(go.name, out var gameObjectPool))
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
            foreach (Transform child in RootObject)
                GameObject.Destroy(child.gameObject);

            pools.Clear();
        }
    }
}