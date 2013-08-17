using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TcpServer
{
    struct SendElement
    {
        public SendElement(UserTokenObject uto, byte[] buffer)
        {
            this.Uto = uto;
            this.Buffer = buffer;
        }

        public UserTokenObject Uto;
        public byte[] Buffer;
    }
}
