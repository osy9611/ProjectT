using System;
using System.Net;
using System.Net.Sockets;
using ProjectT.Server.Byte;
using ProjectT.Server.Connection;
using ProjectT.Server.Messages;
using ProjectT.Server.Stream;
using ProjectT.Server.Util;

namespace ProjectT.Server.Sockets
{
    public class TcpConnection : ProjectT.Server.Connection.Connection, IConnection
    {
        public delegate void OnDisconnectedDelegate(TcpConnection socket);
        public delegate void OnDataDelegate(TcpConnection socket, eMessageBuiltInDataId dataId, PooledBytes pooledBytes);

        private Socket socket;
        public Socket Socket => socket;

        private SocketAsyncEventArgs receiveArgs;
        private SocketAsyncEventArgs sendArgs;

        private MessageResolver messageResolver;
        private SendPipe sendPipe;
        public SendPipe SendPipe => sendPipe;

        private TcpConnectionOption option;

        private eConnectState connectState;
        public eConnectState ConnectState { get => connectState; internal set => connectState = value; }

        private OnDisconnectedDelegate onDisconnected;
        private OnDataDelegate onData;

        private long lastHeartbeatTime;
        public long LastHeartbeatTime { get => lastHeartbeatTime; set => lastHeartbeatTime = value; }
        private bool isServer = false;

        public bool ConnectedPure => connectState == eConnectState.Connected || connectState == eConnectState.Connecting;
        public bool Connected => socket != null && socket.Connected && connectState == eConnectState.Connected;


        public TcpConnection(bool server, TcpConnectionOption option,
            SocketAsyncEventArgs receiveArgs, SocketAsyncEventArgs sendArgs, OnDisconnectedDelegate onDisconnected, OnDataDelegate onData)
            : base(NetType.Tcp)
        {
            isServer = server;
            this.option = option;

            this.receiveArgs = receiveArgs;
            this.receiveArgs.Completed += IOCompleted;

            this.sendArgs = sendArgs;
            this.sendArgs.Completed += IOCompleted;


            this.onDisconnected = onDisconnected;
            this.onData = onData;
            this.messageResolver = new MessageResolver(this, option. ReceiveBufferSize, OnMessageInternal);

            this.sendPipe = new SendPipe(option.SendMaxMessageSize, option.SendMaxMessageSize, option.PoolCollectionChecks, option.SendPoolInitialCapacity);
        }

        internal void SetInitData(UInt64 id, Socket socket)
        {
            base.SetInitData(id);

            connectState = eConnectState.Connecting;
            this.socket = socket;

            receiveArgs.UserToken = this;
            sendArgs.UserToken = this;

            StartReceive(receiveArgs);
        }

        public NetWriteStream GetWriteStream()
        {
            return sendPipe.GetWriteStream();
        }

        internal NetWriteStream GetWriteStream(eMessageBuiltInDataId messageDataId)
        {
            return sendPipe.GetWriteStream(messageDataId);
        }

        private void IOCompleted(object sender, SocketAsyncEventArgs e)
        {
            //방금 완료된 작업 유형을 확인하고 관련된 핸들러를 호출한다.
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    ProcessReceive(e);
                    break;
                case SocketAsyncOperation.Send:
                    ProcessSend(e);
                    break;
                default:
                    throw new ArgumentException("The last operation completed on the socket was not a recieve or send");
            }
        }

        private void OnMessageInternal(eMessageBuiltInDataId dataId, PooledBytes pooledBytes)
        {
            onData?.Invoke(this, dataId, pooledBytes);
        }

        private void StartReceive(SocketAsyncEventArgs e)
        {
            TcpConnection token = (TcpConnection)e.UserToken;

            if (token == null || !token.ConnectedPure)
                return;

            try
            {
                bool willRaiseEvent = token.Socket.ReceiveAsync(e);

                if (!willRaiseEvent)
                    ProcessReceive(e);
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public void ProcessReceive(SocketAsyncEventArgs eventArgs)
        {
            //원격 호스트가 연결을 닫았는지 확인한다.
            TcpConnection token = (TcpConnection)eventArgs.UserToken;

            try
            {
                if (token == null || !token.ConnectedPure)
                    return;
            }
            catch (Exception e)
            {
                Global.Instance.LogError($"ProcessReceive token Error : {e.StackTrace}");
                onDisconnected?.Invoke(this);
                return;
            }

            if(eventArgs.BytesTransferred > 0 && eventArgs.SocketError == SocketError.Success)
            {
                bool willRaiseEvent = false;

                try
                {
                    messageResolver.Receive(eventArgs.Buffer, eventArgs.Offset, eventArgs.BytesTransferred);
                }
                catch(Exception ex)
                {
                    Global.Instance.LogError($"ReceiveAsync token Error : {ex.StackTrace}");
                    onDisconnected?.Invoke(this);
                    return;
                }

                if (!willRaiseEvent)
                    ProcessReceive(eventArgs);
            }
            else
            {
                //클라이언트에서 호출 중에 소켓이 BytesTransferred = 0으로 종료되는 경우가 있다.
                //이 경우 Disconnect 처리는 socket이 연결된 경우에만 종료되어야함
                if (eventArgs.SocketError != SocketError.Success)
                    Global.Instance.LogError($"ProcessRecieve Error : {eventArgs.SocketError.ToString()}");

                onDisconnected?.Invoke(this);

            }
        }

        public bool Send(NetWriteStream stream)
        {
            if (!ConnectedPure)
                return false;

            if(stream.Count > option.SendMaxMessageSize)
            {
                Global.Instance.LogError($"Server.Send: message too big : {stream.Count}. Limit: {option.SendMaxMessageSize}");
                return false;
            }

            if(sendPipe.Count > option.SendQueueLimit)
            {
                Global.Instance.LogError($"Server.Send : sendPipe for connection {id} reached limit of {option.SendQueueLimit}. " +
                    $"This can happen if we call send faster than the network can process messages. Disconneting this connection for load balancing.");
                onDisconnected?.Invoke(this);

                return false;
            }

            if (sendPipe.Count <= 0)
            {
                sendPipe.Enqueue(stream);
                ProcessSend(sendArgs);
            }
            else
                sendPipe.Enqueue(stream);

            if (!isServer)
                lastHeartbeatTime = TimeWarpper.RealtimeSinceStartup;

            return true;
        }

        internal bool Send(ArraySegment<byte> message)
        {
            if (!ConnectedPure)
                return false;

            if(message.Count > option.SendMaxMessageSize)
            {
                Global.Instance.LogError($"Server.Send : message too big : {message.Count}. Limit {option.SendMaxMessageSize}");
                return false;
            }

            if(sendPipe.Count > option.SendQueueLimit)
            {
                Global.Instance.LogError($"Server.Send : sendPipe for connection {id} reached limit of {option.SendQueueLimit}. " +
                  $"This can happen if we call send faster than the network can process messages. Disconneting this connection for load balancing.");
                onDisconnected?.Invoke(this);
            }

            if(sendPipe.Count > 5)
            {
                sendPipe.Enqueue(message);
                ProcessSend(sendArgs);
            }
            else
            {
                sendPipe.Enqueue(message);
            }

            if (!isServer)
                lastHeartbeatTime = TimeWarpper.RealtimeSinceStartup;

            return true;
        }

        public void Flush()
        {
            ProcessSend(sendArgs);
        }

        private void ProcessSend(SocketAsyncEventArgs eventArgs)
        {
            TcpConnection token = (TcpConnection)eventArgs.UserToken;

            try 
            {
                if (token == null || !token.ConnectedPure)
                    return;
            }
            catch(Exception ex)
            {
                Global.Instance.LogError($"ProcessSend token Error : {ex.StackTrace}");
                onDisconnected?.Invoke(this);
                return;
            }

            if(eventArgs.SocketError == SocketError.Success)
            {
                ArraySegment<byte> payload = null;
                if (sendPipe.DequeueAndSerializeAll(ref payload))
                {
                    eventArgs.SetBuffer(payload.Array, payload.Offset, payload.Count);

                    bool willRaiseEvent = false;

                    try
                    {
                        willRaiseEvent = token.Socket.SendAsync(eventArgs);
                    }
                    catch(Exception ex)
                    {
                        Global.Instance.LogError($"ProcessSend token Error : {ex.StackTrace}");
                        onDisconnected?.Invoke(this);
                        return;
                    }

                    if (!willRaiseEvent)
                        ProcessSend(eventArgs);
                }
            }
        }

        internal override void Close()
        {
            connectState = eConnectState.Disconnected;

            base.Close();

            messageResolver.Clear();
            sendPipe.Clear();

            receiveArgs.UserToken = null;
            sendArgs.UserToken = null;

            if(socket != null)
            {
                try
                {
                    socket.Shutdown(SocketShutdown.Send);
                }
                catch(Exception ex)
                {
                    Global.Instance.Log($"A client has been disconnected from the server about msg {ex.Message}");
                }
                finally
                {
                    socket.Close();
                    socket = null;
                }
            }
        }
    }
}