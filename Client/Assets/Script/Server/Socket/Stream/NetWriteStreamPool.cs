using ProjectT.Concurrent;
using ProjectT.Server.Messages;
using System.Collections;
using System.Collections.Generic;
namespace ProjectT.Server.Stream
{
    public class NetWriteStreamPool
    {
        private ConcurrentPool<NetWriteStream> pool;

        public int Count => pool.ActiveCount;

        public NetWriteStreamPool(int bufferSize, bool collectionChecks, int initGenerateCount, int initialCapacity)
        {
            pool = new ConcurrentPool<NetWriteStream>(() => new NetWriteStream(bufferSize), collectionChecks, initGenerateCount, initialCapacity);
        }

        public NetWriteStream Get(eMessageBuiltInDataId messageDataId)
        {
            var netWriteStream = pool.Get();
            netWriteStream.MessageDataId = messageDataId;
            return netWriteStream;
        }

        public void Return(NetWriteStream item)
        {
            item.Reset();
            pool.Return(item);
        }

        public void Clear()
        {
            pool.Clear();
        }
    }
}
