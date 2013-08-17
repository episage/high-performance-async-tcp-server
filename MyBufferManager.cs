using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace TcpServer
{
    internal class MyBufferManager
    {
        public MyBufferManager(int buffersCount, int bufferSize)
        {
            this.bufferCount = buffersCount;
            this.bufferSize = bufferSize;

            int bytesToAllocate = buffersCount * bufferSize;
            Trace.WriteLine(string.Format("Allocating {0}KBs for a buffer...", ((double)bytesToAllocate) / 1024));
            mainBuffer = new byte[bytesToAllocate];
            Trace.WriteLine("Allocation successful.");

            segments = new Stack<ArraySegment<byte>>(buffersCount);
            for (int i = buffersCount - 1; i >= 0; i--)
            {
                var segment = new ArraySegment<byte>(mainBuffer, i * bufferSize, bufferSize);
                segments.Push(segment);
            }
        }

        int bufferCount, bufferSize;
        byte[] mainBuffer;
        Stack<ArraySegment<byte>> segments;

        public ArraySegment<byte> BorrowBuffer()
        {
            return segments.Pop();
        }

        public void ReturnBuffer(ArraySegment<byte> segment)
        {
            segments.Push(segment);
        }

        public void ReturnBuffer(IEnumerable<ArraySegment<byte>> segments)
        {
            foreach (var segment in segments)
            {
                ReturnBuffer(segment);
            }
        }
    }
}
