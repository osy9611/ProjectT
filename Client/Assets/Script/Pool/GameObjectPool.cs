using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

namespace ProjectT.Pool
{
    public class GameObjectPool
    {
        public GameObject Original { get; private set; }

        public Transform Root { get; private set; }

        private Pool<GameObject> pools;
        private readonly HashSet<GameObject> owned = new HashSet<GameObject>();
        private bool disposed;

        public bool IsEmpty
        {
            get => pools.InactiveCount == 0;
        }

        public void Init(GameObject original, int count = 5)
        {
            Original = original;
            Root = new GameObject().transform;
            Root.name = $"{original.name}_Root";

            pools = new Pool<GameObject>(original,
                () =>
                {
                    var obj = GameObject.Instantiate(original);
                    owned.Add(obj);
                    obj.transform.SetParent(Root);
                    obj.SetActive(false);
                    return obj;
                }, true, count, count);
        }

        public void Return(GameObject obj)
        {
            if (obj == null)
                return;

            if (disposed)
            {
                GameObject.Destroy(obj);
                return;
            }
            obj.transform.SetParent(Root);
            obj.SetActive(false);

            pools.Return(obj);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            foreach (var obj in owned)
            {
                if (obj == null)
                    continue;
                obj.SetActive(false);
                GameObject.Destroy(obj);
            }
            owned.Clear();
            if (Root != null)
                GameObject.Destroy(Root.gameObject);
            Root = null;
            Original = null;
            pools = null;
        }

        public GameObject Get(Transform parent)
        {
            if (disposed)
                throw new System.ObjectDisposedException(nameof(GameObjectPool));
            GameObject poolObj;

            poolObj = pools.Get();
            poolObj.SetActive(true);
            poolObj.transform.SetParent(parent);

            return poolObj;
        }

    }
}
