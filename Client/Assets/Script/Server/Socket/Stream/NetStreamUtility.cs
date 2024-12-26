using System;

namespace ProjectT.Server.Stream
{
    public static class NetStreamUtility
    {
        public static void WriteInt16BigEndian(short value, byte[] bytes, int offset = 0)
        {
            bytes[offset + 0] = (byte)(value >> 8);
            bytes[offset + 1] = (byte)value;
        }

        public static void WriteUInt16BigEndian(ushort value, byte[] bytes, int offset = 0)
        {
            WriteInt16BigEndian((short)value, bytes, offset);
        }

        public static short ReadInt16BigEndian(byte[] bytes, int offset = 0)
        {
            return (short)((bytes[offset + 0] << 8) | bytes[offset + 1]);
        }

        public static ushort ReadUInt16BigEndian(byte[] bytes, int offset = 0)
        {
            return (ushort)ReadUInt16BigEndian(bytes, offset);
        }

        public static void WriteInt32BigEndian(int value, byte[] bytes, int offset = 0)
        {
            byte[] valueBytes = BitConverter.GetBytes(value);

            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes, offset, sizeof(int));

            Array.Copy(valueBytes, 0, bytes, offset, sizeof(int));
        }

        public static void WriteUInt32BigEndian(uint value, byte[] bytes, int offset = 0)
        {
            WriteInt32BigEndian((int)value, bytes, offset);
        }

        public static int ReadInt32BigEndian(byte[] bytes, int offset = 0)
        {
            return (bytes[offset + 0] << 24) |
                    (bytes[offset + 1] << 16) |
                    (bytes[offset + 2] << 8) |
                     bytes[offset + 3];
        }

        public static uint ReadUInt32BigEndian(byte[] bytes, int offset = 0)
        {
            return (uint)ReadInt32BigEndian(bytes, offset);
        }
    }
}