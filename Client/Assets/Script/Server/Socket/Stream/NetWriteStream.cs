using ProjectT.Server.Messages;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace ProjectT.Server.Stream
{
    public enum eWriteStreamType
    {
        OnlyOne,
        Ref
    }

    public class NetWriteStream
    {
        private byte[] buffer;

        private int position;
        private eWriteStreamType writeType;
        private eMessageBuiltInDataId messageDataId;

        public byte[] Buffer => buffer;
        public int Position => position;
        public int Count => position;

        public eMessageBuiltInDataId MessageDataId
        {
            get => messageDataId;
            internal set => messageDataId = value;
        }

        /*
         * [Note]
         * BinaryWriter가 너무 많이 할당하므로 MemoryStream으로만 사용한다.
         * 기본적으로 1500바이트이다.(패킷 크기가 MTU보다 작을 경우 평균적으로 1500바이트이기 때문)
         */
        public NetWriteStream(int bufferSize = 1500)
        {
            buffer = new byte[bufferSize];
            writeType = eWriteStreamType.OnlyOne;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            position = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureCapacity(int value)
        {
            if(buffer.Length < value)
            {
                int capacity = Math.Max(value, buffer.Length * 2);
                Array.Resize(ref buffer, capacity);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void IgnoreByte(int size)
        {
            position += size;
        }



        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBytes(byte[] buffer, int offset, int count)
        {
            EnsureCapacity(position + count);
            System.Buffer.BlockCopy(buffer, offset, buffer, position, count);
            position += count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBytes(ArraySegment<byte> segment)
        {
            EnsureCapacity(position + segment.Count);
            System.Buffer.BlockCopy(segment.Array, segment.Offset, buffer, position, segment.Count);
            position += segment.Count;
        }
    }
}