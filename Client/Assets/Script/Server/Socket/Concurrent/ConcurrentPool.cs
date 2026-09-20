using ProjectT.Pool;
using System;

namespace ProjectT.Concurrent
{
    public class ConcurrentPool<T> : IDisposable
    {
        private readonly Pool<T> pool;
        private readonly object sync = new object();

        public int ActiveCount
        {
            get { lock (sync) { return pool.ActiveCount; } }
        }

        public int InactiveCount
        {
            get { lock (sync) { return pool.InactiveCount; } }
        }

        public int Count
        {
            get { lock (sync) { return pool.Count; } }
        }

        public ConcurrentPool(Func<T> objectGenerator, bool collectionChecks, int initGenerateCount, int initialCapacity, int maxPoolSize = 0)
        {
            pool = new Pool<T>(
                objectGenerator: objectGenerator,
                collectionChecks: collectionChecks,
                initGenerateCount: initGenerateCount,
                initialCapacity: initialCapacity,
                maxPoolSize: maxPoolSize);
        }

        public T Get()
        {
            T item = default(T);

            lock (sync)
            {
                item = pool.Get();
            }

            return item;
        }

        public void Return(T item)
        {
            lock (sync)
            {
                pool.Return(item);
            }
        }

        public void ReturnAll()
        {
            lock (sync) { pool.ReturnAll(); }
        }

        public void Dispose()
        {
            lock (sync) { pool.Dispose(); }
        }

        public void Clear()
        {
            lock (sync)
            {
                pool.Clear();
            }
        }
    }

}
