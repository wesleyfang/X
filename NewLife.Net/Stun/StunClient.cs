﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using NewLife.IO;
using NewLife.Linq;
using NewLife.Net.Sockets;
using NewLife.Net.Udp;
using NewLife.Configuration;

namespace NewLife.Net.Stun
{
    /// <summary>Stun客户端。Simple Traversal of UDP over NATs，NAT 的UDP简单穿越。RFC 3489</summary>
    /// <remarks>
    /// <a target="_blank" href="http://baike.baidu.com/view/884586.htm">STUN</a>
    /// 
    /// 国内STUN服务器：220.181.126.73、220.181.126.74，位于北京电信，但不清楚是哪家公司
    /// </remarks>
    /// <example>
    /// <code>
    /// var result = new StunClient().Query();
    /// if(result.Type != StunNetType.UdpBlocked){
    ///     
    /// }
    /// else{
    ///     var publicEP = result.Public;
    /// }
    /// </code>
    /// </example>
    public class StunClient : Netbase
    {
        #region 工作原理
        /*
        In test I, the client sends a STUN Binding Request to a server, without any flags set in the
        CHANGE-REQUEST attribute, and without the RESPONSE-ADDRESS attribute. This causes the server 
        to send the response back to the address and port that the request came from.
            
        In test II, the client sends a Binding Request with both the "change IP" and "change port" flags
        from the CHANGE-REQUEST attribute set.  
              
        In test III, the client sends a Binding Request with only the "change port" flag set.
                          
                            +--------+
                            |  Test  |
                            |   I    |
                            +--------+
                                    |
                                    |
                                    V
                                   /\              /\
                                N /  \ Y          /  \ Y             +--------+
                UDP      <-------/Resp\--------->/ IP \------------->|  Test  |
                Blocked          \ ?  /          \Same/              |   II   |
                                  \  /            \? /               +--------+
                                   \/              \/                    |
                                                    | N                  |
                                                    |                    V
                                                    V                    /\
                                                +--------+  Sym.      N /  \
                                                |  Test  |  UDP    <---/Resp\
                                                |   II   |  Firewall   \ ?  /
                                                +--------+              \  /
                                                    |                    \/
                                                    V                    |Y
                        /\                         /\                    |
         Symmetric  N  /  \       +--------+   N  /  \                   V
            NAT  <--- / IP \<-----|  Test  |<--- /Resp\               Open
                      \Same/      |   I    |     \ ?  /               Internet
                       \? /       +--------+      \  /
                        \/                         \/
                        |                           |Y
                        |                           |
                        |                           V
                        |                           Full
                        |                           Cone
                        V              /\
                    +--------+        /  \ Y
                    |  Test  |------>/Resp\---->Restricted
                    |   III  |       \ ?  /
                    +--------+        \  /
                                       \/
                                       |N
                                       |       Port
                                       +------>Restricted

    */
        #endregion

        #region 服务器
        static String[] servers = new String[] { "stun.nnhy.org", "stun.sipgate.net:10000", "stunserver.org", "stun.xten.com", "stun.fwdnet.net", "stun.iptel.org", "220.181.126.73" };
        private List<String> _Servers;
        /// <summary>Stun服务器</summary>
        public List<String> Servers
        {
            get
            {
                if (_Servers == null)
                {
                    var list = new List<String>();
                    var ss = Config.GetConfigSplit<String>("NewLife.Net.StunServers", null);
                    if (ss != null && ss.Length > 0) list.AddRange(ss);
                    list.AddRange(servers);
                    _Servers = list;
                }
                return _Servers;
            }
        }
        #endregion

        #region 属性
        private ISocket _Socket;
        /// <summary>套接字</summary>
        public ISocket Socket { get { return _Socket; } set { _Socket = value; } }

        private ISocket _Socket2;
        /// <summary>用于测试更换本地套接字的第二套接字</summary>
        public ISocket Socket2 { get { return _Socket2; } set { _Socket2 = value; } }

        private ProtocolType _ProtocolType = ProtocolType.Udp;
        /// <summary>协议，默认Udp</summary>
        public ProtocolType ProtocolType { get { return _ProtocolType; } set { _ProtocolType = value; } }

        private Int32 _Port;
        /// <summary>本地端口</summary>
        public Int32 Port { get { return _Port; } set { _Port = value; } }

        private Int32 _Timeout = 2000;
        /// <summary>超时时间，默认2000ms</summary>
        public Int32 Timeout { get { return _Timeout; } set { _Timeout = value; } }
        #endregion

        #region 构造
        /// <summary>实例化</summary>
        public StunClient() { }

        /// <summary>在指定协议上执行查询</summary>
        /// <param name="protocol"></param>
        /// <returns></returns>
        public StunClient(ProtocolType protocol) : this(protocol, 0) { }

        /// <summary>在指定协议和本地端口上执行查询</summary>
        /// <param name="protocol"></param>
        /// <param name="port"></param>
        /// <returns></returns>
        public StunClient(ProtocolType protocol, Int32 port)
        {
            ProtocolType = protocol;
            Port = port;
        }

        /// <summary>在指定套接字上执行查询</summary>
        /// <param name="socket"></param>
        /// <returns></returns>
        public StunClient(ISocket socket) { Socket = socket; }

        // 如果是外部传进来的Socket，也销毁，就麻烦大了
        ///// <summary>子类重载实现资源释放逻辑时必须首先调用基类方法</summary>
        ///// <param name="disposing">从Dispose调用（释放所有资源）还是析构函数调用（释放非托管资源）</param>
        //protected override void OnDispose(bool disposing)
        //{
        //    base.OnDispose(disposing);

        //    if (_Socket != null)
        //    {
        //        _Socket.Dispose();
        //        _Socket = null;
        //    }
        //    if (_Socket2 != null)
        //    {
        //        _Socket2.Dispose();
        //        _Socket2 = null;
        //    }
        //}
        #endregion

        #region 方法
        void EnsureSocket()
        {
            if (_Socket == null)
            {
                var client = NetService.Resolve<ISocketClient>(ProtocolType);
                client.Port = Port;
                client.Client.SendTimeout = Timeout;
                client.Client.ReceiveTimeout = Timeout;
                client.Bind();
                _Socket = client;
            }
        }

        void EnsureSocket2()
        {
            if (_Socket2 == null)
            {
                var socket = Socket.Socket;
                var ep = socket.LocalEndPoint as IPEndPoint;
                var sto = socket.SendTimeout;
                var rto = socket.ReceiveTimeout;

                // 如果原端口没有启用地址重用，则关闭它
                Object value = socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress);
                if (!Convert.ToBoolean(value)) socket.Close();

                var sk = NetService.Resolve<ISocketClient>(socket.ProtocolType);
                sk.Address = ep.Address;
                sk.Port = ep.Port;
                sk.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                sk.Bind();
                sk.Client.SendTimeout = sto;
                sk.Client.ReceiveTimeout = rto;

                _Socket2 = sk;
            }
        }
        #endregion

        #region 查询
        /// <summary>按服务器列表执行查询</summary>
        /// <returns></returns>
        public StunResult Query()
        {
            foreach (var result in QueryByServers())
            {
                if (result != null && result.Type != StunNetType.Blocked) return result;
            }
            return null;
        }

        IEnumerable<StunResult> QueryByServers()
        {
            // 如果是被屏蔽，很有可能是因为服务器没有响应，可以通过轮换服务器来测试
            StunResult result = null;
            foreach (var item in Servers)
            {
                WriteLog("使用服务器：{0}", item);

                Int32 p = item.IndexOf(":");
                if (p > 0)
                    result = QueryWithServer(item.Substring(0, p), Int32.Parse(item.Substring(p + 1)));
                else
                    result = QueryWithServer(item, 3478);

                yield return result;
            }
        }

        /// <summary>在指定服务器上执行查询</summary>
        /// <param name="host"></param>
        /// <param name="port"></param>
        /// <returns></returns>
        public StunResult QueryWithServer(String host, Int32 port = 3478)
        {
            try
            {
                return QueryWithServer(NetHelper.ParseAddress(host), port);
            }
            catch { return null; }
        }

        /// <summary>在指定服务器上执行查询</summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <returns></returns>
        public StunResult QueryWithServer(IPAddress address, Int32 port)
        {
            EnsureSocket();
            Socket socket = Socket.Socket;
            var remote = new IPEndPoint(address, port);

            // Test I
            // 测试网络是否畅通
            var msg = new StunMessage();
            msg.Type = StunMessageType.BindingRequest;
            var rs = Query(socket, msg, remote);

            // UDP blocked.
            if (rs == null) return new StunResult(StunNetType.Blocked, null);
            WriteLog("Stun服务器：{0}", rs.ServerName);
            WriteLog("映射地址：{0}", rs.MappedAddress);
            WriteLog("源地址：{0}", rs.SourceAddress);
            WriteLog("新地址：{0}", rs.ChangedAddress);
            var remote2 = rs.ChangedAddress;

            // Test II
            // 要求改变IP和端口
            msg.ChangeIP = true;
            msg.ChangePort = true;
            msg.ResetTransactionID();

            // 如果本地地址就是映射地址，表示没有NAT。这里的本地地址应该有问题，永远都会是0.0.0.0
            //if (client.LocalEndPoint.Equals(test1response.MappedAddress))
            var pub = rs.MappedAddress;
            if (pub != null && (socket.LocalEndPoint as IPEndPoint).Port == pub.Port && NetHelper.GetIPs().Any(e => e.Equals(pub.Address)))
            {
                // 要求STUN服务器从另一个地址和端口向当前映射端口发送消息。如果收到，表明是完全开放网络；如果没收到，可能是防火墙阻止了。
                rs = Query(socket, msg, remote);
                // Open Internet.
                if (rs != null) return new StunResult(StunNetType.OpenInternet, pub);

                // Symmetric UDP firewall.
                return new StunResult(StunNetType.SymmetricUdpFirewall, pub);
            }
            else
            {
                rs = Query(socket, msg, remote);
                if (rs != null && pub == null) pub = rs.MappedAddress;
                // Full cone NAT.
                if (rs != null) return new StunResult(StunNetType.FullCone, pub);

                // Test II
                msg.ChangeIP = false;
                msg.ChangePort = false;
                msg.ResetTransactionID();

                // 如果是Tcp，这里需要准备第二个重用的Socket
                if (socket.ProtocolType == ProtocolType.Tcp)
                {
                    EnsureSocket2();
                    socket = Socket2.Socket;
                }

                rs = Query(socket, msg, remote2);
                // 如果第二服务器没响应，重试
                if (rs == null) rs = Query(socket, msg, remote2);
                if (rs != null && pub == null) pub = rs.MappedAddress;
                if (rs == null) return new StunResult(StunNetType.Blocked, pub);

                // 两次映射地址不一样，对称网络
                if (!rs.MappedAddress.Equals(pub)) return new StunResult(StunNetType.Symmetric, pub);

                // Test III
                msg.ChangeIP = false;
                msg.ChangePort = true;
                msg.ResetTransactionID();

                rs = Query(socket, msg, remote2);
                if (rs != null && pub == null) pub = rs.MappedAddress;
                // 受限
                if (rs != null) return new StunResult(StunNetType.RestrictedCone, pub);

                // 端口受限
                return new StunResult(StunNetType.PortRestrictedCone, pub);
            }
        }
        #endregion

        #region 获取公网地址
        /// <summary>获取公网地址</summary>
        /// <returns></returns>
        public IPEndPoint GetPublic()
        {
            EnsureSocket();
            var socket = Socket.Socket;
            var msg = new StunMessage();
            msg.Type = StunMessageType.BindingRequest;
            IPEndPoint ep = null;
            foreach (var item in Servers)
            {
                ep = NetHelper.ParseEndPoint(item, 3478);
                //Int32 p = item.IndexOf(":");
                //if (p > 0)
                //    ep = new IPEndPoint(NetHelper.ParseAddress(item.Substring(0, p)), Int32.Parse(item.Substring(p + 1)));
                //else
                //    ep = new IPEndPoint(NetHelper.ParseAddress(item), 3478);
                var rs = Query(socket, msg, ep);
                if (rs != null && rs.MappedAddress != null) return rs.MappedAddress;
            }
            return null;
        }
        #endregion

        #region 业务
        /// <summary>查询</summary>
        /// <param name="request"></param>
        /// <param name="remoteEndPoint"></param>
        /// <returns></returns>
        public StunMessage Query(StunMessage request, IPEndPoint remoteEndPoint)
        {
            EnsureSocket();
            return Query(Socket.Socket, request, remoteEndPoint);
        }

        static StunMessage Query(Socket socket, StunMessage request, IPEndPoint remoteEndPoint)
        {
            var ms = new MemoryStream();
            request.Write(ms);
            ms.Position = 0;
            Byte[] buffer = null;
            try
            {
                if (socket.ProtocolType == ProtocolType.Tcp)
                {
                    // Tcp协议不支持更换IP或者端口
                    if (request.ChangeIP || request.ChangePort) return null;

                    if (!socket.Connected) socket.Connect(remoteEndPoint);
                    socket.Send(ms.ToArray());
                }
                else
                    socket.SendTo(ms.ToArray(), remoteEndPoint);

                var data = new Byte[150];
                Int32 count = socket.Receive(data);
                buffer = new Byte[count];
                Buffer.BlockCopy(data, 0, buffer, 0, count);
            }
            catch { return null; }

            var rs = StunMessage.Read(new MemoryStream(buffer));
            //if (rs != null && rs.Type != StunMessageType.BindingResponse) return null;
            if (rs == null) return null;

            // 不是同一个会话不要
            if (rs.TransactionID.CompareTo(request.TransactionID) != 0) return null;
            // 不是期望的响应不要
            if (rs.Type != (StunMessageType)((UInt16)request.Type | 0x0100)) return null;
            return rs;
        }
        #endregion
    }
}