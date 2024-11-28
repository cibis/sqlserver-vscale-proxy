#nullable enable
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Text;

namespace NetProxy.Core
{

    public class TcpProxy : IProxy
    {
        /// <summary>
        /// Milliseconds
        /// </summary>
        public int ConnectionTimeout { get; set; } = (4 * 60 * 1000);

        private TcpConnection _connection;

        private volatile bool _fullyStopped;
        private volatile bool _stop = false;

        public bool FullyStopped { get { return _fullyStopped; } }

        private static object _newConnectionsCancelationTokensLock = new object();

        private List<CancellationTokenSource> _newConnectionsCancelationTokens = new List<CancellationTokenSource>();

        private void AddNewConnectionsCancellationToken(CancellationTokenSource cancellationToken)
        {
            lock (_newConnectionsCancelationTokensLock)
            {
                _newConnectionsCancelationTokens.Add(cancellationToken);
            }
        }

        private void RemoveNewConnectionsCancellationToken(CancellationTokenSource cancellationToken)
        {
            lock (_newConnectionsCancelationTokensLock)
            {
                _newConnectionsCancelationTokens.Remove(cancellationToken);
            }
        }

        public async Task Start(string remoteServerHostNameOrAddress, ushort remoteServerPort, ushort localPort, string? localIp, string? dbConnectionString)
        {
            var connections = new ConcurrentBag<TcpConnection>();

            IPAddress localIpAddress = string.IsNullOrEmpty(localIp) ? IPAddress.IPv6Any : IPAddress.Parse(localIp);
            var localServer = new TcpListener(new IPEndPoint(localIpAddress, localPort));
            
            localServer.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
            //localServer.Server.LingerState = new LingerOption(true, 10);
            localServer.Start();
            
            Trace.WriteLine($"TCP proxy started [{localIpAddress}]:{localPort} -> [{remoteServerHostNameOrAddress}]:{remoteServerPort}");

            var _ = Task.Run(async () =>
            {
                while (true)
                {
                    ProxyPause.WaitHandle.WaitOne();
                    if (_stop)
                    {
                        Trace.WriteLine($"=====LOCAL TCP proxy STOPPING=====");
                        try
                        {
                            localServer.Stop();
                        }
                        catch { }
                        _fullyStopped = true;
                        return;
                    }                    

                    await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

                    var tempConnections = new List<TcpConnection>(connections.Count);
                    while (connections.TryTake(out var connection))
                    {
                        tempConnections.Add(connection);
                    }

                    foreach (var tcpConnection in tempConnections)
                    {
                        if (tcpConnection.LastActivity + ConnectionTimeout < Environment.TickCount64)
                        {
                            tcpConnection.Stop();
                        }
                        else
                        {
                            connections.Add(tcpConnection);
                        }
                    }
                }
            });
            while (true)
            {
                try
                {
                    CancellationTokenSource ctsNewConnection = new CancellationTokenSource();

                    CancellationToken ctNewConnection = ctsNewConnection.Token;

                    AddNewConnectionsCancellationToken(ctsNewConnection);

                    ProxyPause.WaitHandle.WaitOne();
                    if (_stop)
                    {
                        Trace.WriteLine($"=====TCP proxy STOPPING=====");
                        return;
                    }                    

                    var ips = await Dns.GetHostAddressesAsync(remoteServerHostNameOrAddress).ConfigureAwait(false);

                    
                    var tcpConnection = await TcpConnection.AcceptTcpClientAsync(localServer,
                        new IPEndPoint(ips[0], remoteServerPort), ctNewConnection, dbConnectionString)
                    .ConfigureAwait(false);

                    RemoveNewConnectionsCancellationToken(ctsNewConnection);

                    _connection = tcpConnection;
                    tcpConnection.Run();
                    connections.Add(tcpConnection);

                }
                catch (System.OperationCanceledException ex) { }
                catch (Exception ex)
                {
                    Trace.WriteLine(ex);
                }                
            }
        }

        public bool Stop(int waitTime, bool forceStopAfterWaitTime = false)
        {
            ProxyPause.WaitHandle.Reset();

            _stop = false;

            lock (_newConnectionsCancelationTokensLock) {
                foreach (var ct in _newConnectionsCancelationTokens)
                {
                    try
                    {
                        ct.Cancel();
                    }
                    catch { }
                }
                _newConnectionsCancelationTokens.Clear();
            }

            string op = Guid.NewGuid().ToString();
            using (CancellationTokenSource ctsource = new CancellationTokenSource())
            {
                CancellationToken ct = ctsource.Token;

                Trace.WriteLine($"Starting connection close wait. waitTime: {waitTime}, forceStopAfterWaitTime: {forceStopAfterWaitTime}, op {op}");

                if (Task.Run(() =>
                {
                    while (_connection != null && !_connection.CanStop())
                    {
                        Thread.Sleep(100);
                        if (ct.IsCancellationRequested) return;
                    }
                }, ct).Wait(waitTime))
                {
                    ctsource.Cancel();
                    _stop = true;
                    ProxyPause.WaitHandle.Set();

                    Trace.WriteLine($"Managed to stop, op {op}");
                }
                else
                {
                    if (forceStopAfterWaitTime)
                    {
                        ctsource.Cancel();
                        _stop = true;
                        ProxyPause.WaitHandle.Set();
                        Trace.WriteLine($"Forced stop, op {op}");
                    }
                    else
                    {
                        ctsource.Cancel();
                        Trace.WriteLine($"failed to stop. Unpausing, op {op}");
                        ProxyPause.WaitHandle.Set();
                        return false;
                    }
                }

            }

            try
            {
                if (_connection != null)
                    _connection.Stop();
            }
            catch { }

            return true;
        }
    }

    internal class TcpConnection
    {
        private readonly TcpClient _localServerConnection;
        private readonly EndPoint? _sourceEndpoint;
        private readonly IPEndPoint _remoteEndpoint;
        private string? _dbConnectionString;
        private readonly TcpClient _forwardClient;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly EndPoint? _serverLocalEndpoint;
        private EndPoint? _forwardLocalEndpoint;
        private long _totalBytesForwarded;
        private long _totalBytesResponded;
        public long LastActivity { get; private set; } = Environment.TickCount64;

        public static async Task<TcpConnection> AcceptTcpClientAsync(TcpListener tcpListener, IPEndPoint remoteEndpoint, CancellationToken ct, string? dbConnectionString)
        {
            Trace.WriteLine("AcceptTcpClientAsync");
            var localServerConnection = await tcpListener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            Trace.WriteLine("AcceptTcpClientAsync COMPLETED");
            localServerConnection.NoDelay = true;
            return new TcpConnection(localServerConnection, remoteEndpoint, dbConnectionString);
        }

        private TcpConnection(TcpClient localServerConnection, IPEndPoint remoteEndpoint, string? dbConnectionString)
        {
            _localServerConnection = localServerConnection;
            _remoteEndpoint = remoteEndpoint;

            _forwardClient = new TcpClient { NoDelay = true };

            _sourceEndpoint = _localServerConnection.Client.RemoteEndPoint;
            _serverLocalEndpoint = _localServerConnection.Client.LocalEndPoint;
            _dbConnectionString = dbConnectionString;
        }

        public void Run()
        {
            RunInternal(_cancellationTokenSource.Token);
        }

        public bool IsConnected(TcpClient tcpClient)
        {

            try
            {

                if (!string.IsNullOrWhiteSpace(_dbConnectionString))
                    using (var con = new SqlConnection(_dbConnectionString))
                    {
                        using (var cmd = new SqlCommand()
                        {
                            CommandText = @"
                                select
                                    P.spid
                                ,   right(convert(varchar, 
                                            dateadd(ms, datediff(ms, P.last_batch, getdate()), '1900-01-01'), 
                                            121), 12) as 'batch_duration'
                                ,   P.program_name
                                ,   P.hostname
                                ,   P.loginame
                                from master.dbo.sysprocesses P
                                where P.spid > 50
                                and      P.status not in ('background', 'sleeping')
                                and      P.cmd not in ('AWAITING COMMAND'
                                                    ,'MIRROR HANDLER'
                                                    ,'LAZY WRITER'
                                                    ,'CHECKPOINT SLEEP'
                                                    ,'RA MANAGER')
                                order by batch_duration desc
                            ",
                            CommandType = CommandType.Text,
                            Connection = con
                        })
                        {
                            con.Open();

                            using (var reader = cmd.ExecuteReader())
                            {
                                reader.Read();
                                if (reader.Read())
                                {
                                    Trace.WriteLine($"More than one active query on the DB");
                                    return true;
                                }
                            }
                        }
                    }

                Trace.WriteLine($"IsConnected : false");
                return false;

            }
            catch (Exception ex)
            {
                Trace.WriteLine($"An exception occurred during IsConnected check : {ex}");
                return false;
            }

        }

        public void Stop()
        {
            try
            {
                _cancellationTokenSource.Cancel();
            }
            catch { }

            try
            {
                if (_forwardClient != null)
                {
                    try
                    {
                        NetworkStream networkStream = _forwardClient.GetStream();
                        networkStream.Close();
                    }
                    catch { }
                    _forwardClient.Close();
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"An exception occurred while closing TcpConnection : {ex}");
            }

            try
            {
                if (_localServerConnection != null)
                {
                    try
                    {
                        NetworkStream networkStream = _localServerConnection.GetStream();
                        networkStream.Close();
                    }
                    catch { }
                    _localServerConnection.Close();
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"An exception occurred while closing TcpConnection : {ex}");
            }
        }

        public bool CanStop()
        {
            return (_forwardClient == null || !IsConnected(_forwardClient));
        }

        private void RunInternal(CancellationToken cancellationToken)
        {
            Task.Run(async () =>
            {
                try
                {
                    using (_localServerConnection)
                    using (_forwardClient)
                    {
                        await _forwardClient.ConnectAsync(_remoteEndpoint.Address, _remoteEndpoint.Port, cancellationToken).ConfigureAwait(false);
                        _forwardLocalEndpoint = _forwardClient.Client.LocalEndPoint;

                        Trace.WriteLine($"Established TCP {_sourceEndpoint} => {_serverLocalEndpoint} => {_forwardLocalEndpoint} => {_remoteEndpoint}");

                        using (var serverStream = _forwardClient.GetStream())
                        using (var clientStream = _localServerConnection.GetStream())
                        using (cancellationToken.Register(() =>
                        {
                            serverStream.Close();
                            clientStream.Close();
                        }, true))
                        {
                            await Task.WhenAny(
                                CopyToAsync(clientStream, serverStream, 81920, Direction.Forward, cancellationToken),
                                CopyToAsync(serverStream, clientStream, 81920, Direction.Responding, cancellationToken)
                            ).ConfigureAwait(false);

                        }
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"An exception occurred during TCP stream : {ex}");
                }
                finally
                {
                    Trace.WriteLine($"Closed TCP {_sourceEndpoint} => {_serverLocalEndpoint} => {_forwardLocalEndpoint} => {_remoteEndpoint}. {_totalBytesForwarded} bytes forwarded, {_totalBytesResponded} bytes responded.");
                }
            });
        }

        private async Task CopyToAsync(Stream source, Stream destination, int bufferSize = 81920, Direction direction = Direction.Unknown, CancellationToken cancellationToken = default)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                while (true)
                {
                    int bytesRead = await source.ReadAsync(new Memory<byte>(buffer), cancellationToken).ConfigureAwait(false);
                    if (bytesRead == 0)
                        break;
                    LastActivity = Environment.TickCount64;
                    await destination.WriteAsync(new ReadOnlyMemory<byte>(buffer, 0, bytesRead), cancellationToken).ConfigureAwait(false);

                    switch (direction)
                    {
                        case Direction.Forward:
                            Interlocked.Add(ref _totalBytesForwarded, bytesRead);
                            break;
                        case Direction.Responding:
                            Interlocked.Add(ref _totalBytesResponded, bytesRead);
                            break;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    internal enum Direction
    {
        Unknown = 0,
        Forward,
        Responding,
    }
}
