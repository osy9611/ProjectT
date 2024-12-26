using System;
using System.Threading;

namespace ProjectT.Server.Connection
{
    interface IConnection
    {
        long LastHeartbeatTime
        {
            get;
        }
        bool ConnectedPure
        {
            get;
        }

        bool Connected
        {
            get;
        }
    }

    public class Connection : IDisposable
    {
        protected UInt64 id;
        protected object userObjectToken;
        protected NetType netType;

        private readonly ReaderWriterLockSlim msgJobRWLock;

        public UInt64 Id => id;
        public NetType NetType => netType;
        public object UserObjectToken
        {
            get => userObjectToken;
            set => userObjectToken = value;
        }

        public Connection (NetType netType)
        {
            this.netType = netType;
            this.msgJobRWLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        }

        protected void SetInitData(UInt64 id)
        {
            this.id = id;
        }

        internal virtual void Close()
        {
            this.id = 0;
            this.userObjectToken = null;
        }

        public void Dispose()
        {

        }
    }
}