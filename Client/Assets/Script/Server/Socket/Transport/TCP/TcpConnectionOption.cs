namespace ProjectT.Server.Sockets
{
    public class TcpConnectionOption
    {
        public bool PoolCollectionChecks { get; set; }
        public int SendMaxMessageSize { get; set; }
        public int SendPoolInitialCapacity { get; set; }
        public int SendQueueLimit { get; set; }
        public int ReceiveBufferSize { get; set; }

        public TcpConnectionOption() { }

        public TcpConnectionOption(bool poolCollectionChecks, int sendMaxMessageSize, int sendPoolInitialCapacity, int sendQueueLimit, int receiveBufferSize)
        {
            PoolCollectionChecks = poolCollectionChecks;
            SendMaxMessageSize = sendMaxMessageSize;
            SendPoolInitialCapacity = sendPoolInitialCapacity;
            SendQueueLimit = sendQueueLimit;
            ReceiveBufferSize = receiveBufferSize;
        }
    }
}