

namespace ProjectT.Server.Messages
{
    using ProjectT.Server.Byte;
    using ProjectT.Server.Connection;
    using ProjectT.Server.Stream;
    using System;
    using UnityEngine.Rendering;
    using UnityEngine.UIElements;

    public class MessageResolver
    {
        public delegate void OnMessageDelegate(eMessageBuiltInDataId dataId, PooledBytes pooledBytes);

        public static readonly short headerSize = 4; // Header Size

        private int messageSize;
        private eMessageBuiltInDataId messageDataId;
        private byte[] messageBuffer;

        private int currentPos;
        private int targetReadPos;
        private int remainBytes;
        private BytesPool bytesPool;
        private Connection connection;

        private OnMessageDelegate onMessage;

        public Connection Connection => connection;

        public MessageResolver(Connection connection, int maxMessageSize, OnMessageDelegate onMessage)
        {
            this.connection = connection;

            messageSize = 0;
            currentPos = 0;
            targetReadPos = 0;
            remainBytes = 0;

            this.onMessage = onMessage;
            messageBuffer = new byte[maxMessageSize];
            bytesPool = new BytesPool(InitAllocMask._128 | InitAllocMask._512 | InitAllocMask._1024, true, 2, 1);
        }

        private bool ReadUntil(byte[] buffer, ref int srcPos, int offset, int transffered)
        {
            if (currentPos >= offset + transffered)
                return false;

            int copySize = targetReadPos - currentPos;

            if (remainBytes < copySize)
                copySize = remainBytes;

            Buffer.BlockCopy(buffer, srcPos, messageBuffer, currentPos, copySize);

            srcPos += copySize;

            currentPos += copySize;
            remainBytes -= copySize;

            if (currentPos < targetReadPos)
                return false;

            return true;
        }

        public void Receive(byte[] buffer, int offset, int transffered)
        {
            remainBytes = transffered;

            int srcPos = offset;

            while (remainBytes > 0)
            {
                bool completed = false;

                if (currentPos < headerSize)
                {
                    targetReadPos = headerSize;

                    completed = ReadUntil(buffer, ref srcPos, offset, transffered);
                    if (!completed)
                        return;

                    GetBodySize(ref messageSize, ref messageDataId);
                    targetReadPos = messageSize + headerSize;
                }

                if(messageSize == 0)
                {
                    if (messageDataId == eMessageBuiltInDataId.ExternalData)
                    {
                        throw new Exception("External Data must have a message size greater than 0");
                    }

                    onMessage(messageDataId, null);
                    ClearBuffer();
                }
                else
                {
                    completed = ReadUntil(buffer, ref srcPos, offset, transffered);

                    if(completed)
                    {
                        var pooledBytes = bytesPool.Get(messageSize);
                        Buffer.BlockCopy(messageBuffer, headerSize, pooledBytes.ArraySegment.Array, 0, messageSize);
                        onMessage(messageDataId, pooledBytes);
                        ClearBuffer();
                    }
                }
            }
        }

        private void GetBodySize(ref int len, ref eMessageBuiltInDataId dataId)
        {
            len = NetStreamUtility.ReadUInt16BigEndian(messageBuffer, 0);
            dataId = (eMessageBuiltInDataId)NetStreamUtility.ReadInt16BigEndian(messageBuffer, 2);
        }

        private void ClearBuffer()
        {
            Array.Clear(messageBuffer, 0, messageBuffer.Length);

            currentPos = 0;
            messageSize = 0;
        }

        public void Clear()
        {
            messageSize = 0;
            currentPos = 0;
            targetReadPos = 0;
            remainBytes = 0;
            bytesPool.Clear();
        }


    }


}