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
                    obj.transform.SetParent(Root);
                    obj.SetActive(false);
                    return obj;
                }, true, count, count);
        }

        public void Return(GameObject obj)
        {
            if (obj == null)
                return;

            obj.transform.SetParent(Root);
            obj.SetActive(false);

            pools.Return(obj);
        }

        public GameObject Get(Transform parent)
        {
            GameObject poolObj;

            poolObj = pools.Get();
            poolObj.SetActive(true);
            poolObj.transform.SetParent(parent);

            return poolObj;
        }

    }
}

