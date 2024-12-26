namespace ProjectT.Server.Byte
{
    using ProjectT.Pool;
    using ProjectT.Server.Byte;
    using System;

    public class PooledBytes : IDisposable, IPoolable
    {
        private MemSizeType sizeType;
        public MemSizeType SizeType => sizeType;

        private byte[] originBytes;
        private BytesPool bytesPool;
        private ArraySegment<byte> arraySegment;
        public ArraySegment<byte> ArraySegment => arraySegment;

        private bool enabled = false;
        public bool Enabled => enabled;

        internal void SetBytes(byte[] bytes)
        {
            originBytes = bytes;
        }
        public void OnGet()
        {
            enabled = true;
        }

        public void OnReturn()
        {
            enabled = false;
            arraySegment = null;
        }

        internal void SetTakeFromPool(int bytebytesWanted)
        {
            arraySegment = new ArraySegment<byte>(originBytes, 0, bytebytesWanted);
        }


        public void Dispose()
        {
            if(enabled)
            {
                arraySegment = null;
                enabled = false;
                bytesPool.Return(this);
            }
        }

        public PooledBytes(BytesPool bytesPool, MemSizeType sizeType)
        {
            this.bytesPool = bytesPool;
            this.sizeType = sizeType;
            
            switch(sizeType)
            {
                case MemSizeType._128:
                    originBytes = new byte[128];
                    break;
                case MemSizeType._512:
                    originBytes = new byte[512];
                    break;
                case MemSizeType._1024:
                    originBytes = new byte[1024];
                    break;
                case MemSizeType._2048:
                    originBytes = new byte[2048];
                    break;
                case MemSizeType._8192:
                    originBytes = new byte[8192];
                    break;
                case MemSizeType._Large:
                    break;

            }
        }

    }

}