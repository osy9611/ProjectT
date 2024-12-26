using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectT.Pool
{
    public class Pool<T> : IDisposable
    {
        private List<T> inactiveObjects;
        private List<T> activeObjects;
        private Func<T> objectGenerator;
        private readonly bool collectionChecks;
        private readonly int maxPoolSize;

        private int activeCount = 0;

        public int InactiveCount => inactiveObjects.Count;

        public int ActiveCount => activeObjects.Count;
        public int Count => inactiveObjects.Count + activeObjects.Count;

        public IReadOnlyList<T> ActiveObjects => activeObjects.AsReadOnly();
        public IReadOnlyList<T> InactiveObjects => inactiveObjects.AsReadOnly();

        public Pool(T preCreatedObject, Func<T> objectGenerator, bool collectionChecks, int initGenerateCount, int initialCapacity, int maxPoolSize = 0)
          : this(objectGenerator, collectionChecks, initGenerateCount, initialCapacity, maxPoolSize)
        {
        }

        public Pool(Func<T> objectGenerator, bool collectionChecks, int initGenerateCount, int initialCapacity, int maxPoolSize = 0)
        {
            this.objectGenerator = objectGenerator;
            this.collectionChecks = collectionChecks;
            this.maxPoolSize = maxPoolSize;

            if(this.maxPoolSize>0)
            {
                if(initGenerateCount > this.maxPoolSize)
                {
                    initGenerateCount = this.maxPoolSize;
                }

                if(initialCapacity > this.maxPoolSize)
                {
                    initialCapacity = this.maxPoolSize;
                }
                else if (initialCapacity < initGenerateCount)
                {
                    initialCapacity = initGenerateCount;
                }
            }

            inactiveObjects = new List<T>(initialCapacity);
            activeObjects = new List<T>(initialCapacity);

            for (int i = 0; i < initGenerateCount; ++i)
                inactiveObjects.Add(objectGenerator());
        }

        ~Pool()
        {
            Clear();
        }

        public T Get()
        {
            T item = default(T);

            if(inactiveObjects.Count > 0)
            {
                int idx = inactiveObjects.Count - 1;
                item = inactiveObjects[idx];
                inactiveObjects.RemoveAt(idx);
            }
            else
            {
                if(maxPoolSize >0 && maxPoolSize <= activeCount)
                {
                    throw new Exception("pool is fulled");
                }

                item = objectGenerator();
            }

            IPoolable iPool = item as IPoolable;

            if(iPool != null)
            {
                iPool.OnGet();
            }

            activeObjects.Add(item);
            activeCount++;

            return item;
        }

        public void Return(T item)
        {
            if(collectionChecks)
            {
                if (!activeObjects.Contains(item))
                    throw new Exception("not found item in active object list");
                if (inactiveObjects.Contains(item))
                    throw new Exception("found item in inactive object list");
            }

            activeObjects.Remove(item);

            IPoolable iPool = item as IPoolable;

            if (iPool != null)
            {
                iPool.OnReturn();
            }

            activeCount--;
            inactiveObjects.Add(item);
        }

        public void ReturnAll()
        {
            for(int i=0;i<activeObjects.Count;++i)
            {
                Return(activeObjects[i]);
            }
        }

        public void Clear()
        {
            foreach (var item in activeObjects)
            {
                IPoolable iPool = item as IPoolable;

                if (iPool != null)
                    iPool.OnReturn();

                inactiveObjects.Add(item);
            }

            activeObjects.Clear();
            activeCount = 0;
        }

        public void Dispose()
        {
            Clear();
        }
    }
}