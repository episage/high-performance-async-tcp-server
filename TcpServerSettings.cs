using System.Net;

namespace TcpServer
{
    public class TcpServerSettings
    {
        public IPEndPoint LocalEp { get; set; }
        public int BufferSize { get; set; }
        public int Backlog { get; set; }
        public int MaxConnections { get; set; }
        public int MaxSimultaneousAccepts { get; set; }
    }
}
