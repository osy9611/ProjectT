using ProjectT.Server.Time;
using ProjectT.Server.Connection;
using ProjectT.Server.Sockets;
using ProjectT.Server.Util;

namespace ProjectT.Server
{
    internal class HeartbeatSender
    {
        private IInternalClient client;
        private IConnection connection;
        private ITimerActor timerActor;
        private int heartbeatTime;

        public HeartbeatSender(IInternalClient client, IConnection connection, ITimerActor timerActor, int heartbeatTime)
        {
            this.client = client;
            this.connection = connection;
            this.timerActor = timerActor;
            timerActor.Ready(Proc, 0, heartbeatTime);
            this.heartbeatTime = heartbeatTime;
        }
        
        public void Start()
        {
            timerActor.Start();
        }

        private void Proc(object stateInfo)
        {
            long diffTime = TimeWarpper.RealtimeSinceStartup - connection.LastHeartbeatTime;

            if (diffTime > heartbeatTime && connection.Connected)
                client.SendHeartbeat();
        }
    }
}