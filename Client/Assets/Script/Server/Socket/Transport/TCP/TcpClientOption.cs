namespace ProjectT.Server.Sockets
{
    public struct TcpClientOption
    {
        public static readonly TcpClientOption Default = new TcpClientOption(true, false);

        public bool Nodelay { get; set; }
        public int SendHeartbeatTime { get; set; }
        public int SendPingPongMsec { get; set; }
        public int SendTimeout { get; set; }
        public int ReceiveTimeout { get; set; }
        public bool DualMode { get; set; }
        public int ReceiveBufferSize { get; set; }
        public int SendMaxMessageSize { get; set; }
        public int SendPoolInitialCapacity { get; set; }
        public int SendPoolLimit { get; set; }
        public bool PoolCollectionChecks { get; set; }

        public bool LingerStateUse { get; set; }
        public int LingerStateSecTime { get; set; }

        public TcpClientOption(bool nodelay, bool dualMode)
        {
            Nodelay = nodelay;
            SendHeartbeatTime = 5000;
            SendPingPongMsec = 1000;
            SendTimeout = 5000;
            ReceiveTimeout = 30000;
            DualMode = dualMode;
            ReceiveBufferSize = 1024;
            SendMaxMessageSize = 1024;
            SendPoolInitialCapacity = 5;
            PoolCollectionChecks = true;
            SendPoolLimit = 100;
            LingerStateUse = false;
            LingerStateSecTime = 10;
        }
    }
}