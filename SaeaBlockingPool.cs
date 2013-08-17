using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace TcpServer
{
    internal class SaeaBlockingPool
    {
        public SaeaBlockingPool(int capacity)
        {
            pool = new Stack<SocketAsyncEventArgs>(capacity);
            guard = new Semaphore(0, capacity);
        }

        Stack<SocketAsyncEventArgs> pool;
        Semaphore guard;

        public void Push(SocketAsyncEventArgs saea)
        {
            lock (pool)
            {
                pool.Push(saea);
                guard.Release();
            }
        }

        public SocketAsyncEventArgs Pop()
        {
            guard.WaitOne();
            lock (pool)
            {
                return pool.Pop();
            }
        }
    }
}
