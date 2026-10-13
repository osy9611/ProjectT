using System;
using UnityEngine;

namespace ProjectT.Pool
{
    public class GameObjectPool : IDisposable
    {
        public GameObject Original { get; private set; }
        public Transform Root { get; private set; }

        private readonly Pool<PooledObject> pool;
        private bool disposed;

        private Transform spawnParent;
        public int ActiveCount => pool.ActiveCount;
        public int InactiveCount => pool.InactiveCount;

        public GameObjectPool(GameObject original, int count = 5, Transform parent = null)
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));

            Original = original;

            Root = new GameObject($"{original.name}_Pool").transform;
            Root.SetParent(parent, false);
            Root.gameObject.SetActive(false);

            pool = new Pool<PooledObject>(
                objectGenerator: Create,
                collectionChecks: true,
                initGenerateCount: count,
                initialCapacity: count,
                destroy: DestroyItem,
                isValid: item => item != null);
        }

        private PooledObject Create()
        {
            var obj = UnityEngine.Object.Instantiate(Original, Root, false);
            obj.SetActive(false);

            var item = obj.GetComponent<PooledObject>();
            if (item == null)
                item = obj.AddComponent<PooledObject>();

            item.Initialize(this);
            return item;
        }

        private void DestroyItem(PooledObject item)
        {
            if (item == null)
                return;

            item.Owner = null;
            item.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(item.gameObject);
        }

        public GameObject Get(Transform parent)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(GameObjectPool));

            // IPoolable.OnGet 콜백이 부모가 지정된 상태에서 실행되도록 Prepare가 이 값을 읽는다. 콜백 안의 재진입 대여는 거부되므로 바깥 대여의 값을 복원한다.
            var previousParent = spawnParent;
            spawnParent = parent;

            try
            {
                var item = pool.Get();
                item.gameObject.SetActive(true);
                if (item == null || !pool.IsActive(item))
                    throw new InvalidOperationException("Object was returned or destroyed during activation.");

                return item.gameObject;
            }
            finally
            {
                spawnParent = previousParent;
            }
        }

        internal void Prepare(PooledObject item)
        {
            item.transform.SetParent(spawnParent, false);
        }

        public void Return(GameObject obj)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(GameObjectPool));

            var item = obj == null ? null : obj.GetComponent<PooledObject>();
            if (item == null || !ReferenceEquals(item.Owner, this))
                throw new InvalidOperationException("Object belongs to another pool.");

            pool.Return(item);
        }

        internal void FinishReturn(PooledObject item)
        {
            if (item == null)
                return;

            item.gameObject.SetActive(false);
            item.transform.SetParent(Root, false);
        }

        public void ReturnAll()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(GameObjectPool));

            pool.ReturnAll();
        }

        public bool Remove(GameObject obj)
        {
            if (disposed)
                return false;

            var item = obj == null ? null : obj.GetComponent<PooledObject>();

            return item != null && ReferenceEquals(item.Owner, this) && pool.Remove(item);
        }

        internal void RemoveDestroyed(PooledObject item)
        {
            if (disposed)
                return;

            pool.Remove(item);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            try
            {
                pool.Dispose();
            }
            finally
            {
                // 콜백 재진입으로 거부된 해제는 풀을 바꾸지 않으므로 Root를 남기고, 해제가 시작된 뒤 정리가 실패하면 Root를 파괴한다.
                if (pool.IsDisposed)
                {
                    disposed = true;

                    try
                    {
                        if (Root != null)
                            UnityEngine.Object.Destroy(Root.gameObject);
                    }
                    finally
                    {
                        Root = null;
                        Original = null;
                    }
                }
            }
        }
    }
}
