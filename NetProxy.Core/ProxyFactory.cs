using NetProxy;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetProxy.Core
{
    public class ProxyConfig
    {
        public string? protocol { get; set; }
        public ushort? localPort { get; set; }
        public string? localIp { get; set; }
        public string? forwardIp { get; set; }
        public ushort? forwardPort { get; set; }
        public string? directConnectionString { get; set; }
    }

    public interface IProxy
    {
        bool FullyStopped { get; }

        Task Start(string remoteServerHostNameOrAddress, ushort remoteServerPort, ushort localPort, string? localIp = null, string? dbConnectionString = null);

        bool Stop(int waitTime, bool forceStopAfterWaitTime = false);
    }

    public class ProxyPause
    {
        public static ManualResetEvent WaitHandle = new ManualResetEvent(true);
    }

    public class ProxyFactory
    {
        private static object _criticalLock = new object();

        public volatile static List<IProxy> Proxies = new List<IProxy>();

        public static Task ProxyFromConfig(ProxyConfig proxyConfig, string proxyName = "default")
        {
            var forwardPort = proxyConfig.forwardPort;
            var localPort = proxyConfig.localPort;
            var forwardIp = proxyConfig.forwardIp;
            var localIp = proxyConfig.localIp;
            var protocol = proxyConfig.protocol;
            var dbConnectionString = proxyConfig.directConnectionString;

            try
            {
                if (forwardIp == null)
                {
                    throw new Exception("forwardIp is null");
                }
                if (!forwardPort.HasValue)
                {
                    throw new Exception("forwardPort is null");
                }
                if (!localPort.HasValue)
                {
                    throw new Exception("localPort is null");
                }
                if (protocol != "tcp" && protocol != "any")
                {
                    throw new Exception($"protocol is not supported {protocol}");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Failed to start {proxyName} : {ex.Message}");
                throw;
            }

            if (protocol == "tcp" || protocol == "any")
            {
                Task task;
                try
                {
                    var proxy = new TcpProxy();
                    Proxies.Add(proxy);
                    task = proxy.Start(forwardIp, forwardPort.Value, localPort.Value, localIp, dbConnectionString);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Failed to start {proxyName} : {ex.Message}");
                    throw;
                }

                return task;
            }

            throw new InvalidOperationException($"protocol not supported {protocol}");
        }
    }
}
