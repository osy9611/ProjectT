using ProjectT.Concurrent;
using System;
using System.Collections.Concurrent;

namespace ProjectT.Server.Stream
{
    public class SendPipe 
    {
        private readonly ConcurrentQueue<NetWriteStream> queue = new ConcurrentQueue<NetWriteStream>();
        private NetWriteStreamPool pool;
        private NetWriteStream sendStream;
        private int maxBufferSize = 0;
        private ConcurrentObject<NetWriteStream> pendingStream;

        public SendPipe(int maxBufferSize, int bufferSize, bool collectionChecks, int initialCapacity)
        {
            pool = new NetWriteStreamPool(bufferSize, collectionChecks, initialCapacity, initialCapacity);
            sendStream = new NetWriteStream(maxBufferSize);
            this.maxBufferSize = maxBufferSize;
            pendingStream = new ConcurrentObject<NetWriteStream>();
        }

        public NetWriteStream GetWriteStream()
        {
            return pool.Get(Messages.eMessageBuiltInDataId.ExternalData);
        }

        internal NetWriteStream GetWriteStream(Messages.eMessageBuiltInDataId messageDataId)
        {
            return pool.Get(messageDataId);
        }

        public int Count
        {
            get
            {
                if(pendingStream.Exist())
                {
                    return queue.Count + 1;
                }
                else
                {
                    return queue.Count;
                }
            }
        }

        public void Enqueue(NetWriteStream message)
        {
            queue.Enqueue(message);
        }

        public void Enqueue(ArraySegment<byte> message)
        {
            var stream = pool.Get(Messages.eMessageBuiltInDataId.ExternalData);
            stream.WriteBytes(message.Array, message.Offset, message.Count);
        }

        public bool DequeueAndSerializeAll(ref ArraySegment<byte> payload)
        {
            if (queue.Count == 0 && !this.pendingStream.Exist())
                return false;

            //여러 대기 중인 메시지가 있을때 이를 하나의 패킷으로 병합해 
            //TCP 오버헤드를 피하고 성능을 향상시킬 수 있다.
            sendStream.Reset();

            //모든 byte[] 메시지를 대기열에서 빼고 패킷으로 직렬화한다.
            if(this.pendingStream.Try(out NetWriteStream pendingStream))
            {
                NetStreamUtility.WriteUInt16BigEndian((ushort)pendingStream.Count, sendStream.Buffer, sendStream.Position);
                sendStream.IgnoreByte(2);
                NetStreamUtility.WriteUInt16BigEndian((ushort)pendingStream.MessageDataId, sendStream.Buffer, sendStream.Position);
                sendStream.IgnoreByte(2);

                if(pendingStream.Count > 0)
                {
                    sendStream.WriteBytes(pendingStream.Buffer, 0, pendingStream.Count);
                }

                pool.Return(pendingStream);
            }

            while(queue.TryDequeue(out NetWriteStream stream))
            {
                int len = 2 + stream.Count;

                if(sendStream.Position + len > maxBufferSize)
                {
                    this.pendingStream.Set(stream);
                    break;
                }

                NetStreamUtility.WriteUInt16BigEndian((ushort)stream.Count, sendStream.Buffer, sendStream.Position);
                sendStream.IgnoreByte(2);
                NetStreamUtility.WriteUInt16BigEndian((ushort)stream.MessageDataId, sendStream.Buffer, sendStream.Position);
                sendStream.IgnoreByte(2);

                if (stream.Count > 0)
                    sendStream.WriteBytes(stream.Buffer, 0, stream.Count);

                pool.Return(stream);
            }

            payload = new ArraySegment<byte>(sendStream.Buffer, 0, sendStream.Count);

            return true;
        }

        public void Clear()
        {
            if(this.pendingStream.Try(out NetWriteStream pendingStream))
            {
                pool.Return(pendingStream);
            }

            while(queue.TryDequeue(out NetWriteStream stream))
            {
                pool.Return(stream);
            }
        }
    }

}