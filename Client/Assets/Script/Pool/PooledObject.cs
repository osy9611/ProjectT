using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectT.Pool
{
    public sealed class PooledObject : MonoBehaviour, IPoolable
    {
        internal GameObjectPool Owner { get; set; }
        private IPoolable[] callbacks = Array.Empty<IPoolable>();

        internal void Initialize(GameObjectPool owner)
        {
            Owner = owner;

            var items = new List<IPoolable>();
            foreach (var component in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component != this && component is IPoolable callback)
                    items.Add(callback);
            }


            callbacks = items.ToArray();
        }

        public void OnGet()
        {
            Owner.Prepare(this);

            foreach (var callback in callbacks)
            {
                callback.OnGet();
            }
        }

        public void OnReturn()
        {
            try
            {
                foreach (var callback in callbacks)
                {
                    if (callback is UnityEngine.Object component && component == null)
                        continue;

                    callback.OnReturn();
                }
            }
            finally
            {
                Owner?.FinishReturn(this);
            }
        }

        private void OnDestroy()
        {
            var owner = Owner;
            Owner = null;
            owner?.RemoveDestroyed(this);
        }
    }
}
