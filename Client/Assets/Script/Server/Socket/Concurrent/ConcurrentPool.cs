using ProjectT.Pool;
using System;

namespace ProjectT.Concurrent
{
    public class ConcurrentPool<T>
    {
        private Pool<T> pool;

        public int ActiveCount
        {
            get { lock(this) { return pool.ActiveCount; } }
        }

        public int Count
        {
            get { lock (this) { return pool.InactiveCount; } }
        }

        public ConcurrentPool(Func<T> objectGenerator, bool collectionChecks, int initGenerateCount, int initialCapacity, int maxPoolSize = 0)
        {
            pool = new Pool<T>(objectGenerator, collectionChecks, initGenerateCount, initialCapacity, maxPoolSize);
        }

        public T Get()
        {
            T item = default(T);

            lock(this)
            {
                item = pool.Get();
            }

            return item;
        }

        public void Return(T item)
        {
            lock(this)
            {
                pool.Return(item);
            }
        }

        public void Clear()
        {
            lock (this)
            {
                pool.Clear();
            }
        }
    }

}
