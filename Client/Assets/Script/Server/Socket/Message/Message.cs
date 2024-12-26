namespace ProjectT.Server.Messages
{
    using ProjectT.Server.Byte;
    using ProjectT.Server.Connection;
    public struct Message
    {
        private int type;
        private int id;
        private ulong connId;

        private Connection connection;
        private PooledBytes pooledBytes;

        public int Type
        {
            get => type;
            internal set => type = value;
        }

        public int Id
        {
            get => id;
            internal set => id = value;
        }

        public ulong ConnId => connId;

        public PooledBytes PooledBytes
        {
            get => pooledBytes;
            internal set => pooledBytes = value;
        }

        public Connection Connection
        {
            get => connection;
            internal set => connection = value;
        }

        public bool Enabled => connection != null && connection.Id == connId;

        public Message(int type, Connection connection)
            : this(type, -1, connection, null)
        {

        }

        public Message(int type, int id, Connection connection)
            : this(type, id, connection, null)
        {

        }

        public Message(int type, int id, Connection connection, PooledBytes pooledBytes)
        {
            this.type = type;
            this.id = id;
            this.connId = connection.Id;
            this.connection = connection;
            this.pooledBytes = pooledBytes;
        }

        public Message(int type, Message moveMessage)
            : this(type, -1 ,moveMessage.connection,moveMessage.pooledBytes)
        {

        }

        public Message(int type,Message moveMessage, bool move = true)
            : this(type, -1, moveMessage.Connection, moveMessage.PooledBytes)
        {
            if (move)
                moveMessage.UnRef();
        }

        internal void UnRef()
        {
            pooledBytes = null;
            connection = null;
        }
    }
}