using ProjectT.Server.Byte;
using ProjectT.Server.Connection;
using ProjectT.Server.Messages;
using ProjectT.Server.Stream;
using ProjectT.Server.Time;
using System;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using Unity.VisualScripting;
using UnityEditor;

namespace ProjectT.Server.Sockets
{
    public class TcpClient : IInternalClient, IDisposable
    {
        public delegate void OnConnectedDelegate();
        public delegate void OnDataDelegate(ArraySegment<byte> message);
        public delegate void OnDisconnectedDelegate();

        private TcpClientOption option;

        private Socket socket = null;
        private SocketAsyncEventArgs connectArgs;
        private SocketAsyncEventArgs receiveArgs;
        private SocketAsyncEventArgs sendArgs;

        private HeartbeatSender heartbeatSender;

        private UInt64 idCounter = 0;

        private byte[] recvBuffer;
        private TcpConnection tcpConnection;
        private MessageQueue<Message> messageQueue;

        public OnConnectedDelegate OnConnected;
        public OnDataDelegate OnData;
        public OnDisconnectedDelegate OnDisconnected;

        public eConnectState ConnectState => tcpConnection.ConnectState;
        public bool Connected => tcpConnection.Connected;

        internal MessageQueue<Message> MessageQueue => messageQueue;

        public TcpClient(ITimerActor heartbeatTimerActor, TcpClientOption? option  =null)
        {
            if (option.HasValue)
                this.option = TcpClientOption.Default;
            else
                this.option = option.Value;

            if(connectArgs == null)
            {
                connectArgs = new SocketAsyncEventArgs();
                connectArgs.Completed += OnConnectCompleted;
            }

            if(receiveArgs == null)
            {
                recvBuffer = new byte[this.option.ReceiveBufferSize];
                receiveArgs = new SocketAsyncEventArgs();
                receiveArgs.SetBuffer(recvBuffer, 0, recvBuffer.Length);
            }

            if(sendArgs ==null)
            {
                sendArgs = new SocketAsyncEventArgs();
                sendArgs.SetBuffer(null, 0, 0);
            }

            var connOption = new TcpConnectionOption()
            {
                PoolCollectionChecks = this.option.PoolCollectionChecks,
                ReceiveBufferSize = this.option.ReceiveBufferSize,
                SendMaxMessageSize = this.option.SendMaxMessageSize,
                SendPoolInitialCapacity = this.option.SendPoolInitialCapacity,
                SendQueueLimit = this.option.SendPoolLimit
            };

            tcpConnection = new TcpConnection(false, connOption, receiveArgs, sendArgs, OnDisconnectedInternal, OnDataInteral);
            messageQueue = new MessageQueue<Message>();

            if(heartbeatTimerActor != null)
            {
                heartbeatSender = new HeartbeatSender(this, tcpConnection, heartbeatTimerActor, this.option.SendHeartbeatTime);
                heartbeatSender.Start();
            }
        }

        public void Connect(IPAddress address, int port, TcpClientOption? option = null)
        {
            Connect(new IPEndPoint(address, port), option);
        }

        public void Connect(string address, int port, TcpClientOption? option = null)
        {
            Connect(new IPEndPoint(IPAddress.Parse(address), port), option);
        }

        public void Connect(DnsEndPoint endpoint, TcpClientOption? option = null)
        {
            Connect(endpoint as EndPoint, endpoint.Host, endpoint.Port, option);
        }

        public void Connect(IPEndPoint endpoint, TcpClientOption? option = null)
        {
            Connect(endpoint as EndPoint, endpoint.Address.ToString(), endpoint.Port, option);
        }

        public void Connect(EndPoint endpoint, string address, int port, TcpClientOption? option)
        {
            if (tcpConnection.ConnectState != eConnectState.Disconnected && tcpConnection.ConnectState != eConnectState.None)
                return;

            if(this.option.DualMode)
            {
                socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
                socket.DualMode = this.option.DualMode;
            }
            else
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            }

            if (this.option.LingerStateUse)
            {
                var lingerState = new LingerOption(true, this.option.LingerStateSecTime);
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, lingerState);
            }

            connectArgs.RemoteEndPoint = endpoint;
            tcpConnection.ConnectState = eConnectState.Connecting;
            socket.ConnectAsync(connectArgs);
        }

        public void Send(byte[] message)
        {
            tcpConnection.Send(message);
        }

        public void Send(NetWriteStream stream)
        {
            tcpConnection.Send(stream);
        }

        void IInternalClient.SendHeartbeat()
        {
            SendHeartbeat();
        }

        public void SendHeartbeat()
        {
            Send(tcpConnection.GetWriteStream(eMessageBuiltInDataId.Heartbeat));
        }

        public void Flush()
        {
            tcpConnection.Flush();
        }

        void OnConnectCompleted(object sender, SocketAsyncEventArgs e)
        {
            if(e.SocketError == SocketError.Success)
            {
                idCounter++;

                if (idCounter == 0)
                    idCounter = 1;

                tcpConnection.SetInitData(idCounter, socket);
                messageQueue.Enqueue(new Message((int)eMessageType.BuiltInEvent, (int)eBuiltInEventId.Connected, tcpConnection));
            }
            else
            {
                Global.Instance.LogError($"OnConnectCompleted : Failed to connect {e.SocketError}");
            }
        }
        private void OnDisconnectedInternal(TcpConnection conn)
        {
            Global.Instance.Log($"OnDisconnectedInternal ({conn.Id})");
            messageQueue.Enqueue(new Message((int)eMessageType.Data, (int)eBuiltInEventId.Disconnected, conn));
        }

        private void OnDataInteral(TcpConnection conn, eMessageBuiltInDataId dataId, PooledBytes pooledBytes)
        {
            messageQueue.Enqueue(new Message((int)eMessageType.Data, (int)dataId, tcpConnection, pooledBytes));
        }

        private void CloseInternal()
        {
            tcpConnection.Close();
            socket = null;
            connectArgs.RemoteEndPoint = null;
        }

        public void Close()
        {
            messageQueue.Enqueue(new Message((int)eMessageType.BuiltInEvent, (int)eBuiltInEventId.Disconnected, tcpConnection));
        }

        public void Dispose()
        {
            CloseInternal();
        }

        public bool Update()
        {
            if(messageQueue.Dequeue(out Message message))
            {
                if(message.Enabled)
                {
                    switch((eMessageType)message.Type)
                    {
                        case eMessageType.BuiltInEvent:
                            UpdateBuiltInMessage(message);
                            break;

                        case eMessageType.Data:
                            UpdateDataMessage(message);
                            break;
                    }
                }

                if (message.PooledBytes != null)
                {
                    message.PooledBytes.Dispose();
                }

                return true;
            }



            return false;
        }

        public void UpdateBuiltInMessage(Message message)
        {
            switch((eBuiltInEventId)message.Id)
            {

                case eBuiltInEventId.Connected:
                    break;

                case eBuiltInEventId.Disconnected:

                    if(tcpConnection.ConnectedPure)
                    {
                        tcpConnection.ConnectState = eConnectState.Disconnected; 
                    }
                    break;
            }
        }

        public void UpdateDataMessage(Message message)
        {
            switch((eMessageBuiltInDataId)message.Id)
            {
                case eMessageBuiltInDataId.Pong:
                    break;
                case eMessageBuiltInDataId.Connected:
                    tcpConnection.ConnectState = eConnectState.Connected;
                    Global.Instance.Log($"Connected ({tcpConnection.Id})");
                    OnConnected?.Invoke();
                    break;
                case eMessageBuiltInDataId.ExternalData:
                    OnData?.Invoke(message.PooledBytes.ArraySegment);
                    break;
            }
        }
    }
}