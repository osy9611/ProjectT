using System;
using UnityEngine;

namespace ProjectT.Pool
{
    public class GameObjectPool : IDisposable
    {
        public GameObject Original { get; private set; }
        public Transform Root { get; private set; }

        private Pool<PooledObject> pools;
        private bool disposed;

        private Transform spawnParent;
        public int ActiveCount => pools?.ActiveCount ?? 0;
        public int InactiveCount => pools?.InactiveCount ?? 0;
        public bool IsEmpty => InactiveCount == 0;

        public void Init(GameObject original, int count = 5, Transform parent = null)
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));

            if (disposed)
                throw new ObjectDisposedException(nameof(GameObjectPool));

            if (pools != null)
                throw new InvalidOperationException("Pool already initialized.");

            Original = original;

            Root = new GameObject($"{original.name}_Pool").transform;
            Root.SetParent(parent, false);
            Root.gameObject.SetActive(false);

            try
            {
                pools = new Pool<PooledObject>(
                    objectGenerator: Create,
                    collectionChecks: true,
                    initGenerateCount: count,
                    initialCapacity: count,
                    destroy: DestroyItem,
                    isValid: item => item != null);
            }
            catch
            {
                UnityEngine.Object.Destroy(Root.gameObject);
                Root = null;
                Original = null;
                throw;
            }
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

            var previousParent = spawnParent;
            spawnParent = parent;
            PooledObject item = null;

            try
            {
                item = pools.Get();
                item.gameObject.SetActive(true);
                if (item == null || !pools.IsActive(item))
                    throw new InvalidOperationException("Object was returned or destroyed during activation.");

                return item.gameObject;
            }
            catch
            {
                if (item != null)
                    pools.Remove(item);

                throw;
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

            pools.Return(item);
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

            pools.ReturnAll();
        }

        public bool Remove(GameObject obj)
        {
            if (disposed)
                return false;

            var item = obj == null ? null : obj.GetComponent<PooledObject>();

            return item != null && ReferenceEquals(item.Owner, this) && pools.Remove(item);
        }

        internal void RemoveDestroyed(PooledObject item)
        {
            if (disposed)
                return;

            pools.Remove(item);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            pools?.Dispose();
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
