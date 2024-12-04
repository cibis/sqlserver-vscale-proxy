using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using NetProxy.API.Controllers;
using NetProxy.Core;
using System.Data;
using System.Diagnostics;
using System.Reflection;


namespace NetProxy.Tests
{

    public class NetProxyClient
    {
        private string clusterAPIport;
        
        public NetProxyClient()
        {
            IConfigurationRoot configuration = new ConfigurationBuilder().AddJsonFile("testconfig.json").AddEnvironmentVariables().Build();
            clusterAPIport = ((string)configuration.GetValue(typeof(string), "ClusterAPIport"));
        }                

        public async Task UnPause()
        {
            var client = new HttpClient() { Timeout = TimeSpan.FromSeconds(60) };
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{clusterAPIport}/netproxy/unpause");
            var response = await client.SendAsync(request);
            Trace.WriteLine(await response.Content.ReadAsStringAsync());

        }

        public async Task PausedStart(ProxyConfig proxyConfig)
        {
            var client = new HttpClient() { Timeout = TimeSpan.FromSeconds(60) };
            var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{clusterAPIport}/netproxy/start");
            var content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(proxyConfig), null, "application/json");
            request.Content = content;
            var response = await client.SendAsync(request);
            Trace.WriteLine(await response.Content.ReadAsStringAsync());

        }

        public async Task Stop(int waitTime, bool forceStopAfterWaitTime = false)
        {
            var client = new HttpClient() { Timeout = TimeSpan.FromSeconds(60) };
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{clusterAPIport}/netproxy/stop");
            var response = await client.SendAsync(request);
            Trace.WriteLine(await response.Content.ReadAsStringAsync());
        }

        public async Task<bool> Transfer(ProxyConfig proxyConfig, int waitTime = 5000)
        {
            var client = new HttpClient() { Timeout = TimeSpan.FromSeconds(60) };
            var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{clusterAPIport}/netproxy/transfer");
            var content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(proxyConfig), null, "application/json");
            request.Content = content;
            var response = await client.SendAsync(request);
            Trace.WriteLine(await response.Content.ReadAsStringAsync());
            return response.StatusCode == System.Net.HttpStatusCode.OK;
        }

        public async Task Start(ProxyConfig proxyConfig)
        {

            var client = new HttpClient() { Timeout = TimeSpan.FromSeconds(60) };
            var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{clusterAPIport}/netproxy/start");
            string requestBody = Newtonsoft.Json.JsonConvert.SerializeObject(proxyConfig);
            var content = new StringContent(requestBody, null, "application/json");
            request.Content = content;
            var response = await client.SendAsync(request);
            Trace.WriteLine(await response.Content.ReadAsStringAsync());

        }

        public async Task<bool> PausedTransfer(ProxyConfig proxyConfig, int waitTime = 5000, bool forceStopAfterWaitTime = false)
        {
            var client = new HttpClient() { Timeout = TimeSpan.FromSeconds(60) };
            var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{clusterAPIport}/netproxy/pausedtransfer?waitTime=15000");
            var content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(proxyConfig), null, "application/json");
            request.Content = content;
            var response = await client.SendAsync(request);
            Trace.WriteLine(await response.Content.ReadAsStringAsync());
            return response.StatusCode == System.Net.HttpStatusCode.OK;
        }

        public async Task Reset()
        {
            var client = new HttpClient() { Timeout = TimeSpan.FromSeconds(60) };
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{clusterAPIport}/netproxy/reset");
            var response = await client.SendAsync(request);
            Trace.WriteLine(await response.Content.ReadAsStringAsync());
        }
    }

    /// <summary>
    /// Test NetProxy when running the proxy and the sql server in a Kubernetes cluster
    /// </summary>
    public class KubernetesAPITests
    {
        IConfigurationRoot configuration = new ConfigurationBuilder().AddJsonFile("testconfig.json").AddEnvironmentVariables().Build();


        private async Task<bool> SimulateSQLLoad(int threadNumber, int commandTimeout, int maximumQueryExecutionTime, CancellationToken cancellationToken, int startDelay = 0, bool randomPause = false)
        {
            if (maximumQueryExecutionTime < 2000)
            {
                throw new ArgumentOutOfRangeException("maximumQueryExecutionTime should be at least few seconds long");
            }
            if (commandTimeout < 2000 || commandTimeout <= maximumQueryExecutionTime - 1000)
            {
                throw new ArgumentOutOfRangeException("commandTimeout should be at least few seconds long and bigger than maximumQueryExecutionTime");
            }

            List<Task<bool>> tasks = new List<Task<bool>>();
            Thread.Sleep(startDelay);
            for (int i = 0; i < threadNumber; i++)
            {
                int threadIndex = i;

                tasks.Add(Task.Factory.StartNew(() =>
                {
                    string op = Guid.NewGuid().ToString();
                    try
                    {
                        Random r = new Random(threadIndex);

                        while (!cancellationToken.IsCancellationRequested)
                        {
                            string cnnStr = ((string)configuration.GetValue(typeof(string), "ProxyConnectionString")).Replace(",4433", "," + ((string)configuration.GetValue(typeof(string), "ClusterSQLServerPort")));
                            var getConn = () =>
                            {
                                SqlConnection cnn = new SqlConnection(cnnStr);
                                int connectTries = 5;
                                bool failed = false;
                                do
                                {
                                    try
                                    {
                                        cnn.Open();
                                        failed = false;
                                    }
                                    catch (Exception ex)
                                    {
                                        Trace.WriteLine($"===== HANDLED: thread {threadIndex}, error, {ex.Message} =====");
                                        failed = true;
                                        Thread.Sleep(1000);
                                    }
                                    connectTries--;
                                } while (connectTries > 0 && failed);
                                return cnn;
                            };
                            int waitTime = r.Next(maximumQueryExecutionTime - 1000);
                            using (SqlConnection cnn = getConn())
                            {
                                Trace.WriteLine($" thread {threadIndex}, started query, waitTime {waitTime}, op {op}");

                                using (var cmd = new SqlCommand()
                                {
                                    CommandTimeout = commandTimeout,
                                    CommandText = @"
                                        declare @t datetime = getdate();
                                        declare @delay datetime = dateadd(MILLISECOND, @pauseMilliseconds, convert(DATETIME, 0))
                                        WAITFOR DELAY @delay
                                        select DATEDIFF(MILLISECOND, @t, getdate())
                                    ",
                                    CommandType = CommandType.Text,
                                    Connection = cnn
                                })
                                {
                                    cmd.Parameters.Add("@pauseMilliseconds", SqlDbType.Int).Value = r.Next(waitTime);
                                    string res = cmd.ExecuteScalar().ToString();
                                    Trace.WriteLine($" thread {threadIndex}, executed query, op {op}, res {res} ");
                                }
                            }
                            if (!randomPause)
                                Thread.Sleep(1000);
                            else
                                Thread.Sleep(r.Next(waitTime));
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"===== thread {threadIndex}, op {op}, error, {ex.Message} =====");
                        return false;
                    }
                    return true;
                }, cancellationToken));
            }
            var results = await Task.WhenAll(tasks);
            return !results.Any(o => !o);
        }

        [Fact]
        public async void ProxyStartTest()
        {
            Trace.WriteLine("==========================================================");
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = cancellationTokenSource.Token;
            NetProxyClient proxyService = new NetProxyClient();
            proxyService.Reset().Wait();
            try
            {
                List<Task<bool>> tasks = new List<Task<bool>>();
                int commandTimeout = 10 * 1000;
                tasks.Add(SimulateSQLLoad(5, commandTimeout, commandTimeout - 2000, cancellationToken, 3000));
                tasks.Add(Task.Factory.StartNew(() =>
                {
                    try
                    {

                        string cnnStr = ((string)configuration.GetValue(typeof(string), "DirectConnectionString")).Replace("127.0.0.1", "mssql-service");
                        proxyService.Start(new Core.ProxyConfig()
                        {
                            directConnectionString = cnnStr,
                            forwardIp = "mssql-service",
                            forwardPort = 1433,
                            localPort = 4433,
                            protocol = "tcp"
                        }).Wait();
                        Thread.Sleep(commandTimeout);
                        cancellationTokenSource.Cancel();
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine(ex);
                        return false;
                    }
                    return true;
                }));
                var results = await Task.WhenAll(tasks);

                Assert.True(!results.Any(o => !o));
            }
            finally
            {
                Trace.WriteLine("========================TEST CLEANUP======================");
                cancellationTokenSource.Cancel();
                proxyService.Reset().Wait();
            }
        }




        [Fact]
        public async void ProxyStopTest()
        {
            Trace.WriteLine("==========================================================");
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = cancellationTokenSource.Token;
            int commandTimeout = 10 * 1000;
            NetProxyClient proxyService = new NetProxyClient();
            proxyService.Reset().Wait();
            try
            {
                List<Task<bool>> tasks = new List<Task<bool>>();
                Task<bool> simulateSQLLoadTask;
                Task<bool> netproxyTask;
                tasks.Add(simulateSQLLoadTask = SimulateSQLLoad(5, commandTimeout, commandTimeout - 2000, cancellationToken, 3000));
                tasks.Add(netproxyTask = Task.Factory.StartNew(() =>
                {
                    try
                    {
                        string cnnStr = ((string)configuration.GetValue(typeof(string), "DirectConnectionString")).Replace("127.0.0.1", "mssql-service");
                        proxyService.Start(new Core.ProxyConfig()
                        {
                            directConnectionString = cnnStr,
                            forwardIp = "mssql-service",
                            forwardPort = 1433,
                            localPort = 4433,
                            protocol = "tcp"
                        }).Wait();
                        Thread.Sleep(2000);
                        proxyService.Stop(commandTimeout).Wait();
                        Thread.Sleep(commandTimeout + 2000);
                        cancellationTokenSource.Cancel();
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine(ex);
                        return false;
                    }
                    return true;
                }));
                var results = await Task.WhenAll(tasks);

                Assert.True(netproxyTask.Result);
            }
            finally
            {
                Trace.WriteLine("========================TEST CLEANUP======================");
                cancellationTokenSource.Cancel();
                proxyService.Reset().Wait();
            }
        }


        [Fact]
        public async void ProxyPausedTransfer_SmallLoad_NotForced()
        {
            Trace.WriteLine("==========================================================");
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = cancellationTokenSource.Token;
            NetProxyClient proxyService = new NetProxyClient();
            proxyService.Reset().Wait();
            try
            {
                List<Task<bool>> tasks = new List<Task<bool>>();
                int commandTimeout = 10 * 1000;
                tasks.Add(SimulateSQLLoad(5, commandTimeout, commandTimeout - 2000, cancellationToken, 3000));
                tasks.Add(Task.Factory.StartNew(() =>
                {
                    try
                    {

                        string cnnStr = ((string)configuration.GetValue(typeof(string), "DirectConnectionString")).Replace("127.0.0.1", "mssql-service");
                        var proyConfig = new Core.ProxyConfig()
                        {
                            directConnectionString = cnnStr,
                            forwardIp = "mssql-service",
                            forwardPort = 1433,
                            localPort = 4433,
                            protocol = "tcp"
                        };
                        proxyService.Start(proyConfig).Wait();
                        Thread.Sleep(2000);
                        if (!Task.Run(() => { return proxyService.PausedTransfer(proyConfig, waitTime: commandTimeout); }).Result)
                        {
                            Trace.WriteLine("Proxy was not transfered");
                            cancellationTokenSource.Cancel();
                            return false;
                        }
                        proxyService.UnPause().Wait();
                        Thread.Sleep(10000);
                        cancellationTokenSource.Cancel();
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine(ex);
                        return false;
                    }
                    return true;
                }));
                var results = await Task.WhenAll(tasks);

                Assert.True(!results.Any(o => !o));
            }
            finally
            {
                Trace.WriteLine("========================TEST CLEANUP======================");
                cancellationTokenSource.Cancel();
                proxyService.Reset().Wait();
            }
        }

        [Fact]
        public async void ProxyTransfer_SmallLoad_NotForced()
        {
            Trace.WriteLine("==========================================================");
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = cancellationTokenSource.Token;
            NetProxyClient proxyService = new NetProxyClient();
            proxyService.Reset().Wait();
            try
            {
                List<Task<bool>> tasks = new List<Task<bool>>();
                int commandTimeout = 10 * 1000;
                tasks.Add(SimulateSQLLoad(5, commandTimeout, commandTimeout - 2000, cancellationToken, 3000));
                tasks.Add(Task.Factory.StartNew(() =>
                {
                    try
                    {

                        string cnnStr = ((string)configuration.GetValue(typeof(string), "DirectConnectionString")).Replace("127.0.0.1", "mssql-service");
                        var proyConfig = new Core.ProxyConfig()
                        {
                            directConnectionString = cnnStr,
                            forwardIp = "mssql-service",
                            forwardPort = 1433,
                            localPort = 4433,
                            protocol = "tcp"
                        };
                        proxyService.Start(proyConfig).Wait();
                        Thread.Sleep(2000);
                        if (!Task.Run(() => { return proxyService.Transfer(proyConfig, waitTime: commandTimeout); }).Result)
                        {
                            Trace.WriteLine("Proxy was not transfered");
                            cancellationTokenSource.Cancel();
                            return false;
                        }
                        cancellationTokenSource.Cancel();
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine(ex);
                        return false;
                    }
                    return true;
                }));
                var results = await Task.WhenAll(tasks);

                Assert.True(!results.Any(o => !o));
            }
            finally
            {
                Trace.WriteLine("========================TEST CLEANUP======================");
                cancellationTokenSource.Cancel();
                proxyService.Reset().Wait();
            }
        }

        [Fact]
        public async void ProxyPausedTransfer_BigLoad_Forced()
        {
            Trace.WriteLine("==========================================================");
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = cancellationTokenSource.Token;
            NetProxyClient proxyService = new NetProxyClient();
            proxyService.Reset().Wait();
            try
            {
                List<Task<bool>> tasks = new List<Task<bool>>();
                int commandTimeout = 10 * 1000;
                tasks.Add(SimulateSQLLoad(8, commandTimeout, commandTimeout - 2000, cancellationToken, 3000));
                Task<bool> netproxyTask;
                tasks.Add(netproxyTask = Task.Factory.StartNew(() =>
                {
                    try
                    {

                        string cnnStr = ((string)configuration.GetValue(typeof(string), "DirectConnectionString")).Replace("127.0.0.1", "mssql-service");
                        var proyConfig = new Core.ProxyConfig()
                        {
                            directConnectionString = cnnStr,
                            forwardIp = "mssql-service",
                            forwardPort = 1433,
                            localPort = 4433,
                            protocol = "tcp"
                        };
                        proxyService.Start(proyConfig).Wait();
                        Thread.Sleep(2000);
                        if (!Task.Run(() => { return proxyService.PausedTransfer(proyConfig, waitTime: commandTimeout, forceStopAfterWaitTime: true); }).Result)
                        {
                            Trace.WriteLine("Proxy was not transfered");
                            cancellationTokenSource.Cancel();
                            return false;
                        }
                        proxyService.UnPause().Wait();
                        cancellationTokenSource.Cancel();
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine(ex);
                        return false;
                    }
                    return true;
                }));
                var results = await Task.WhenAll(tasks);

                Assert.True(netproxyTask.Result);
            }
            finally
            {
                Trace.WriteLine("========================TEST CLEANUP======================");
                cancellationTokenSource.Cancel();
                proxyService.Reset().Wait();
            }
        }

        [Fact]
        public async void ProxyPausedTransfer_BigLoad_MultipleTries()
        {
            Trace.WriteLine("==========================================================");
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = cancellationTokenSource.Token;
            NetProxyClient proxyService = new NetProxyClient();
            proxyService.Reset().Wait();
            try
            {
                List<Task<bool>> tasks = new List<Task<bool>>();
                int commandTimeout = 10 * 1000;
                tasks.Add(SimulateSQLLoad(8, commandTimeout, commandTimeout - 2000, cancellationToken, 3000, true));
                tasks.Add(Task.Factory.StartNew(() =>
                {
                    try
                    {

                        string cnnStr = ((string)configuration.GetValue(typeof(string), "DirectConnectionString")).Replace("127.0.0.1", "mssql-service");
                        var proyConfig = new Core.ProxyConfig()
                        {
                            directConnectionString = cnnStr,
                            forwardIp = "mssql-service",
                            forwardPort = 1433,
                            localPort = 4433,
                            protocol = "tcp"
                        };
                        proxyService.Start(proyConfig).Wait();
                        Thread.Sleep(2000);
                        Trace.WriteLine("Starting proxy transfer");
                        for (int i = 0; i < 10; i++)
                        {
                            if (!Task.Run(() => { return proxyService.PausedTransfer(proyConfig, waitTime: commandTimeout); }).Result)
                                Trace.WriteLine("Proxy was not transfered");
                            else
                            {
                                Trace.WriteLine("Proxy was transfered");
                                break;
                            }
                        }
                        proxyService.UnPause().Wait();
                        Trace.WriteLine("Unpausing proxy");

                        cancellationTokenSource.Cancel();
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine(ex);
                        return false;
                    }
                    return true;
                }));
                var results = await Task.WhenAll(tasks);

                Assert.True(!results.Any(o => !o));
            }
            finally
            {
                Trace.WriteLine("========================TEST CLEANUP======================");
                cancellationTokenSource.Cancel();
                proxyService.Reset().Wait();
            }
        }
    }
}