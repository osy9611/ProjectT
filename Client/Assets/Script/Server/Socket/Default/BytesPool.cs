using ProjectT.Concurrent;
using ProjectT.Server.Byte;
using Unity.VisualScripting;

namespace ProjectT.Server.Byte
{
    internal enum MemCheckSize
    {
        _128 = 128,
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048,
        _8192 = 8192,
    }

    public enum MemSizeType
    {
        _128,
        _512,
        _1024,
        _2048,
        _8192,
        _Large = 255,
    };

    public enum InitAllocMask
    {
        Notthing = 0,
        _128 = 1,
        _512 = 1 << MemSizeType._512,
        _1024 = 1 << MemSizeType._1024,
        _2048 = 1 << MemSizeType._2048,
        _8192 = 1 << MemSizeType._8192,
        Everything = 0xfffffff,
    };


    public class BytesPool 
    {
        private ConcurrentPool<PooledBytes> pool128;
        private ConcurrentPool<PooledBytes> pool512;
        private ConcurrentPool<PooledBytes> pool1024;
        private ConcurrentPool<PooledBytes> pool2048;
        private ConcurrentPool<PooledBytes> pool8192;

        public ConcurrentPool<PooledBytes> Pool128 => pool128;
        public ConcurrentPool<PooledBytes> Pool512 => pool512;
        public ConcurrentPool<PooledBytes> Pool1024 => pool1024;
        public ConcurrentPool<PooledBytes> Pool2048=>pool2048;
        public ConcurrentPool<PooledBytes> Pool8192=>pool8192;

        public BytesPool(InitAllocMask mask, bool collectionChecks, int initGenerateCount, int initialCapacity)
        {
            bool initAllock = (mask & InitAllocMask._128) > 0;

            if (initAllock)
                pool128 = new ConcurrentPool<PooledBytes>(() => new PooledBytes(this, MemSizeType._128), collectionChecks, initialCapacity, initialCapacity);
            else
                pool128 = new ConcurrentPool<PooledBytes>(() => new PooledBytes(this, MemSizeType._128), collectionChecks, initGenerateCount, initialCapacity);


            initAllock = (mask & InitAllocMask._512) > 0;

            if (initAllock)
                pool512 = new ConcurrentPool<PooledBytes>(() => new PooledBytes(this, MemSizeType._512), collectionChecks, initialCapacity, initialCapacity);
            else
                pool512 = new ConcurrentPool<PooledBytes>(() => new PooledBytes(this, MemSizeType._512), collectionChecks, initGenerateCount, initialCapacity);


            initAllock = (mask & InitAllocMask._1024) > 0;

            if (initAllock)
                pool1024 = new ConcurrentPool<PooledBytes>(() => new PooledBytes(this, MemSizeType._1024), collectionChecks, initialCapacity, initialCapacity);
            else
                pool1024 = new ConcurrentPool<PooledBytes>(() => new PooledBytes(this, MemSizeType._1024), collectionChecks, initGenerateCount, initialCapacity);


            initAllock = (mask & InitAllocMask._2048) > 0;

            if (initAllock)
                pool2048 = new ConcurrentPool<PooledBytes>(() => new PooledBytes(this, MemSizeType._2048), collectionChecks, initialCapacity, initialCapacity);
            else
                pool2048 = new ConcurrentPool<PooledBytes>(() => new PooledBytes(this, MemSizeType._2048), collectionChecks, initGenerateCount, initialCapacity);


            initAllock = (mask & InitAllocMask._8192) > 0;

            if (initAllock)
                pool8192 = new ConcurrentPool<PooledBytes>(() => new PooledBytes(this, MemSizeType._8192), collectionChecks, initialCapacity, initialCapacity);
            else
                pool8192 = new ConcurrentPool<PooledBytes>(() => new PooledBytes(this, MemSizeType._8192), collectionChecks, initGenerateCount, initialCapacity);
        }

        public PooledBytes Get(int bytesWanted)
        {
            if(bytesWanted <= (int)MemCheckSize._128)
            {
                var val = pool128.Get();
                val.SetTakeFromPool(bytesWanted);
                return val;
            }
            else if(bytesWanted <= (int)MemCheckSize._512)
            {
                var val = pool512.Get();
                val.SetTakeFromPool(bytesWanted);
                return val;
            }
            else if (bytesWanted <= (int)MemCheckSize._1024)
            {
                var val = pool1024.Get();
                val.SetTakeFromPool(bytesWanted);
                return val;
            }
            else if (bytesWanted <= (int)MemCheckSize._2048)
            {
                var val = pool2048.Get();
                val.SetTakeFromPool(bytesWanted);
                return val;
            }
            else if (bytesWanted <= (int)MemCheckSize._8192)
            {
                var val = pool8192.Get();
                val.SetTakeFromPool(bytesWanted);
                return val;
            }
            else
            {
                PooledBytes item = new PooledBytes(this, MemSizeType._Large);
                item.SetBytes(new byte[bytesWanted]);
                item.SetTakeFromPool(bytesWanted);
                return item;
            }
        }

        public void Return(PooledBytes item)
        {
            switch (item.SizeType)
            {
                case MemSizeType._128:pool128.Return(item); break;
                case MemSizeType._512: pool512.Return(item); break;
                case MemSizeType._1024: pool1024.Return(item); break;
                case MemSizeType._2048: pool2048.Return(item); break;
                case MemSizeType._8192: pool8192.Return(item); break;
                case MemSizeType._Large: break;
                default:
                    throw new System.NotSupportedException("Type not supported");
            }
        }

        public void Clear()
        {
            pool128.Clear();
            pool512.Clear();
            pool1024.Clear();
            pool2048.Clear();
            pool8192.Clear();
        }
    }

}