using System;
using System.Collections;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Concurrent;
using System.Net;

namespace TcpServer
{
    public abstract class HighPerformanceAsyncTcpSocketServer<T> where T : UserTokenObject, new()
    {
        protected HighPerformanceAsyncTcpSocketServer(TcpServerSettings settings)
        {
            Trace.WriteLine(
string.Format(@"
High performance TCP socket server started at {0}.
Listening IP address = {1}.
Listening port = {2}.
Maximum simultaneous accepts = {3}.
Maximum connections = {4}.
Buffer size = {5}.
Backlog = {6}.
".Trim(),
            DateTime.Now,
            settings.LocalEp.Address,
            settings.LocalEp.Port,
            settings.MaxSimultaneousAccepts,
            settings.MaxConnections,
            settings.BufferSize,
            settings.Backlog));

            sendQueue = new BlockingCollection<SendElement>(new ConcurrentQueue<SendElement>());
            bufferManager = new MyBufferManager(settings.MaxConnections, settings.BufferSize);

            acceptPool = new SaeaBlockingPool(settings.MaxSimultaneousAccepts);
            for (int i = 0; i < settings.MaxSimultaneousAccepts; i++)
            {
                var saea = CreateAcceptSaea();
                acceptPool.Push(saea);
            }

            sendPool = new SaeaBlockingPool(settings.MaxConnections);
            for (int i = 0; i < settings.MaxConnections; i++)
            {
                var saea = CreateSendSaea();
                sendPool.Push(saea);

            }

            receivePool = new SaeaBlockingPool(settings.MaxConnections);
            for (int i = 0; i < settings.MaxConnections; i++)
            {
                var saea = CreateReceiveSaea();
                receivePool.Push(saea);
            }

            listeningSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listeningSocket.Bind(settings.LocalEp);
            listeningSocket.Listen(settings.Backlog);
            Accept();
            RunSendQueue();
        }

        Socket listeningSocket;
        MyBufferManager bufferManager;
        SaeaBlockingPool acceptPool;
        SaeaBlockingPool sendPool;
        SaeaBlockingPool receivePool;
        BlockingCollection<SendElement> sendQueue;

        protected abstract void OnAccepted(T uto);
        protected abstract void OnReceived(T uto);
        protected abstract void OnSent(T uto);
        protected abstract void OnAcceptError(SocketAsyncEventArgs saea);
        protected abstract void OnReceiveError(T uto);
        protected abstract void OnSendError(T uto);
        protected abstract void OnDisconnected(T uto);

        protected abstract void OnFatalExceptionThrown(Exception exception);

        protected void Send(UserTokenObject uto, byte[] data)
        {
            var se = new SendElement(uto, data);
            sendQueue.Add(se);

            RunSendQueue();
        }

        void RunSendQueue()
        {
            try
            {
                var queuedSend = sendQueue.Take();
                var buffer = queuedSend.Buffer;
                var uto = queuedSend.Uto;

                uto.SendSaea.SetBuffer(buffer, 0, buffer.Length);
                uto.SendSaea.AcceptSocket.SendAsync(uto.SendSaea);
            }
            catch (Exception exception)
            {
                OnFatalExceptionThrown(exception);
            }
        }

        protected void Disconnect(T uto)
        {
            RecycleUserTokenObject(uto);
        }

        void Accept()
        {
            try
            {
                // We use one SAEA from pool. This SAEA will be returned on connection accept or accept error.
                // Thus, we limit number of maximum simultaneous connection accepts this way.
                // Pool is blocking pool.
                var acceptSaea = acceptPool.Pop();

                var completedAsynchronously = listeningSocket.AcceptAsync(acceptSaea);

                if (!completedAsynchronously)
                {
                    ProcessAccepted(acceptSaea);
                }
            }
            catch (Exception exception)
            {
                OnFatalExceptionThrown(exception);
            }
        }

        void AcceptCompleted(object sender, SocketAsyncEventArgs e)
        {
            ProcessAccepted(e);
        }

        void ProcessAccepted(SocketAsyncEventArgs e)
        {
            try
            {
                if (e.SocketError != SocketError.Success)
                {
                    HandleAcceptError(e);
                    return;
                }

                Accept();

                // We pop one connection SAEA from pool. This will be returned on connection close or connection error.
                // This limits max connections to server.
                //var receiveSaea = receivePool.Pop();
                //var sendSaea = sendPool.Pop();
                // A new socket has been created in e.AcceptSocket because it was null on pool creation.
                // "If the SocketAsyncEventArgs.AcceptSocket property is null, a new Socket is constructed with the same AddressFamily, SocketType, and ProtocolType as the current Socket and set as the SocketAsyncEventArgs.AcceptSocket property."
                //receiveSaea.AcceptSocket = e.AcceptSocket;
                //sendSaea.AcceptSocket = e.AcceptSocket;

                var uto = new T
                {
                    AcceptSocket = e.AcceptSocket,
                    ReceiveSaea = receivePool.Pop(),
                    SendSaea = sendPool.Pop()
                };
                uto.ReceiveSaea.AcceptSocket = uto.AcceptSocket;
                uto.ReceiveSaea.UserToken = uto;
                uto.SendSaea.AcceptSocket = uto.AcceptSocket;
                uto.SendSaea.UserToken = uto;

                e.AcceptSocket = null;
                acceptPool.Push(e);

                OnAccepted(uto);

                Receive(uto.ReceiveSaea);
                //TODO start running send queue??
            }
            catch (Exception exception)
            {
                OnFatalExceptionThrown(exception);
            }
        }

        void Receive(SocketAsyncEventArgs e)
        {
            try
            {
                var completedAsynchronously = e.AcceptSocket.ReceiveAsync(e);

                if (!completedAsynchronously)
                {
                    ProcessReceived(e);
                }
            }
            catch (Exception exception)
            {
                OnFatalExceptionThrown(exception);
            }
        }

        void ReceiveCompleted(object sender, SocketAsyncEventArgs e)
        {
            ProcessReceived(e);
        }

        void ProcessReceived(SocketAsyncEventArgs e)
        {
            try
            {
                var uto = e.UserToken as T;

                if (e.SocketError != SocketError.Success)
                {
                    HandleReceiveError(uto);
                    return;
                }

                if (e.BytesTransferred == 0)
                {
                    RecycleUserTokenObject(uto);
                    return;
                }

                // We have received a message and we are ready to process it.
                OnReceived(uto);

                Receive(e);
            }
            catch (Exception exception)
            {
                OnFatalExceptionThrown(exception);
            }
        }

        void SendCompleted(object sender, SocketAsyncEventArgs e)
        {
            ProcessSend(e);
        }

        void ProcessSend(SocketAsyncEventArgs e)
        {
            try
            {
                var uto = e.UserToken as T;

                if (e.SocketError != SocketError.Success)
                {
                    HandleSendError(uto);
                    return;
                }

                OnSent(uto);
            }
            catch (Exception exception)
            {
                OnFatalExceptionThrown(exception);
            }
        }

        void HandleAcceptError(SocketAsyncEventArgs e)
        {
            try
            {
                OnAcceptError(e);
                RecycleAcceptSocket(e);
            }
            catch (Exception exception)
            {
                OnFatalExceptionThrown(exception);
            }
        }

        void RecycleUserTokenObject(T uto)
        {
            OnDisconnected(uto);
            DisposeSocket(uto.AcceptSocket);

            uto.SendSaea.UserToken = null;
            uto.ReceiveSaea.UserToken = null;
            sendPool.Push(uto.SendSaea);
            receivePool.Push(uto.ReceiveSaea);
        }

        void DisposeSocket(Socket socket)
        {
            try
            {
                socket.Shutdown(SocketShutdown.Both);
            }
            catch
            {
            }

            socket.Close();

            socket = null;
        }

        void HandleReceiveError(T uto)
        {
            try
            {
                OnReceiveError(uto);
                RecycleUserTokenObject(uto);
            }
            catch (Exception exception)
            {
                OnFatalExceptionThrown(exception);
            }
        }

        void HandleSendError(T uto)
        {
            try
            {
                OnSendError(uto);
                RecycleUserTokenObject(uto);
            }
            catch (Exception exception)
            {
                OnFatalExceptionThrown(exception);
            }
        }

        void RecycleAcceptSocket(SocketAsyncEventArgs e)
        {
            try
            {
                DisposeSocket(e.AcceptSocket);

                acceptPool.Push(e);
            }
            catch (Exception exception)
            {
                OnFatalExceptionThrown(exception);
            }
        }

        private SocketAsyncEventArgs CreateAcceptSaea()
        {
            var saea = new SocketAsyncEventArgs();
            saea.Completed += AcceptCompleted;
            return saea;
        }

        private SocketAsyncEventArgs CreateReceiveSaea()
        {
            var saea = new SocketAsyncEventArgs();
            saea.Completed += ReceiveCompleted;
            var buffer = bufferManager.BorrowBuffer();
            saea.SetBuffer(buffer.Array, buffer.Offset, buffer.Count);
            return saea;
        }

        private SocketAsyncEventArgs CreateSendSaea()
        {
            var saea = new SocketAsyncEventArgs();
            saea.Completed += SendCompleted;
            return saea;
        }
    }
}