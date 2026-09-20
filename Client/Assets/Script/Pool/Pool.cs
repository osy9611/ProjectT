using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ProjectT.Pool
{
    public class Pool<T> : IDisposable
    {
        private static readonly bool isValueType = typeof(T).IsValueType;
        private static readonly ItemComparer itemComparer = new ItemComparer();

        private sealed class ItemComparer : IEqualityComparer<T>
        {
            public bool Equals(T x, T y)
            {
                return isValueType ? EqualityComparer<T>.Default.Equals(x, y) : ReferenceEquals(x, y);
            }

            public int GetHashCode(T obj)
            {
                return isValueType ? EqualityComparer<T>.Default.GetHashCode(obj) : RuntimeHelpers.GetHashCode(obj);
            }
        }

        private readonly Stack<T> inactiveObjects;
        private readonly HashSet<T> activeObjects = new HashSet<T>(itemComparer);
        private readonly Func<T> objectGenerator;
        private readonly Action<T> destroy;
        private readonly Predicate<T> isValid;
        private readonly bool collectionChecks;
        private readonly int maxPoolSize;

        private bool disposed;
        private bool changing;

        public int InactiveCount => inactiveObjects.Count;
        public int ActiveCount => activeObjects.Count;
        public int Count => InactiveCount + ActiveCount;
        public IEnumerable<T> ActiveObjects { get { foreach (var item in activeObjects) yield return item; } }
        public IEnumerable<T> InactiveObjects { get { foreach (var item in inactiveObjects) yield return item; } }

        public Pool(Func<T> objectGenerator, bool collectionChecks, int initGenerateCount, int initialCapacity, int maxPoolSize = 0, Action<T> destroy = null, Predicate<T> isValid = null)
        {
            if (objectGenerator == null)
                throw new ArgumentNullException(nameof(objectGenerator));

            if (initGenerateCount < 0)
                throw new ArgumentOutOfRangeException(nameof(initGenerateCount));

            if (initialCapacity < 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));

            if (maxPoolSize < 0)
                throw new ArgumentOutOfRangeException(nameof(maxPoolSize));

            this.objectGenerator = objectGenerator;
            this.collectionChecks = collectionChecks;
            this.maxPoolSize = maxPoolSize;
            this.destroy = destroy;
            this.isValid = isValid;

            if (maxPoolSize > 0)
                initGenerateCount = Math.Min(initGenerateCount, maxPoolSize);

            inactiveObjects = new Stack<T>(Math.Max(initialCapacity, initGenerateCount));

            try
            {
                var generated = new HashSet<T>(itemComparer);
                for (int i = 0; i < initGenerateCount; ++i)
                {
                    var item = Create();

                    if (!generated.Add(item))
                        throw new InvalidOperationException("Pool generator returned the same object twice.");

                    inactiveObjects.Push(item);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private T Create()
        {
            var item = objectGenerator();

            if (ReferenceEquals(item, null))
                throw new InvalidOperationException("Pool generator returned null.");

            return item;
        }

        private void BeginChange()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(Pool<T>));

            if (changing)
                throw new InvalidOperationException("Pool callbacks cannot change the same pool.");

            changing = true;
        }

        public T Get()
        {
            BeginChange();

            try
            {
                T item = PopValidOnCreate();

                if (!activeObjects.Add(item))
                    throw new InvalidOperationException("Object is already active in this pool.");

                try
                {
                    (item as IPoolable)?.OnGet();
                }
                catch (Exception error)
                {
                    activeObjects.Remove(item);

                    List<Exception> errors = null;
                    ErrorCollector.Run(ref errors, item, InvokeOnReturn);
                    ErrorCollector.Run(ref errors, item, DestroyInternal);

                    if (errors != null)
                    {
                        errors.Insert(0, error);
                        throw new AggregateException(errors);
                    }

                    throw;
                }

                return item;
            }
            finally
            {
                changing = false;
            }
        }

        private T PopValidOnCreate()
        {
            while(inactiveObjects.Count >0)
            {
                var item = inactiveObjects.Pop();
                if (isValid == null || isValid(item))
                    return item;

                destroy?.Invoke(item);
            }

            if (maxPoolSize > 0 && ActiveCount >= maxPoolSize)
                throw new InvalidOperationException("Pool capacity reached.");

            var created = Create();
            if (isValid != null && !isValid(created))
            {
                destroy?.Invoke(created);
                throw new InvalidOperationException("Pool generator returned an invalid object.");
            }
            return created;
        }

        internal bool IsActive(T item)
        {
            return activeObjects.Contains(item);
        }

        public void Return(T item)
        {
            BeginChange();

            try
            {
                ReturnInternal(item);
            }
            finally
            {
                changing = false;
            }
        }

        private void ReturnInternal(T item)
        {
            if (!activeObjects.Remove(item))
            {
                if (collectionChecks)
                    throw new InvalidOperationException("Object is not borrowed from this pool.");
                return;
            }

            try
            {
                (item as IPoolable)?.OnReturn();
            }
            catch
            {
                destroy?.Invoke(item);
                throw;
            }

            inactiveObjects.Push(item);
        }

        public void ReturnAll()
        {
            BeginChange();

            try
            {
                List<Exception> errors = null;
                Action<T> returnItem = ReturnInternal;

                foreach (var item in new List<T>(activeObjects))
                    ErrorCollector.Run(ref errors, item, returnItem);

                ErrorCollector.ThrowIfAny(errors);
            }
            finally
            {
                changing = false;
            }
        }

        public bool Remove(T item)
        {
            BeginChange();
            try
            {
                bool active = activeObjects.Remove(item);
                if (!active)
                {
                    var items = inactiveObjects.ToArray();

                    bool found = Array.Exists(items, other => itemComparer.Equals(item, other));
                    if (!found)
                        return false;

                    inactiveObjects.Clear();

                    for (int i = items.Length - 1; i >= 0; --i)
                        if (!itemComparer.Equals(item, items[i]))
                            inactiveObjects.Push(items[i]);
                }

                try
                {
                    if (active)
                        (item as IPoolable)?.OnReturn();
                }
                finally
                {
                    destroy?.Invoke(item);
                }

                return true;
            }
            finally
            {
                changing = false;
            }
        }

        public void Clear()
        {
            BeginChange();

            try
            {
                ClearInternal();
            }
            finally
            {
                changing = false;
            }
        }

        private void ClearInternal()
        {
            var active = new List<T>(activeObjects);
            var inactive = inactiveObjects.ToArray();

            activeObjects.Clear();
            inactiveObjects.Clear();

            List<Exception> errors = null;

            foreach (var item in active)
            {
                ErrorCollector.Run(ref errors, item, InvokeOnReturn);
                ErrorCollector.Run(ref errors, item, DestroyInternal);
            }

            foreach (var item in inactive)
            {
                ErrorCollector.Run(ref errors, item, DestroyInternal);
            }

            ErrorCollector.ThrowIfAny(errors);
        }

        private static void InvokeOnReturn(T item)
        {
            (item as IPoolable)?.OnReturn();
        }

        private void DestroyInternal(T item)
        {
            destroy?.Invoke(item);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            BeginChange();

            disposed = true;

            try
            {
                ClearInternal();
            }
            finally
            {
                changing = false;
            }
        }
    }
}
