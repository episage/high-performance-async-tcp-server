using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;

namespace TcpServer
{
    public abstract class UserTokenObject
    {
        internal Socket AcceptSocket;
        internal SocketAsyncEventArgs SendSaea;
        internal SocketAsyncEventArgs ReceiveSaea;

        public byte[] ReceiveBuffer
        {
            get
            {
                return ReceiveSaea.Buffer;
            }
        }
    }
}
