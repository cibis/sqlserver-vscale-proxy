using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using NetProxy.API.Controllers;
using System.Data;
using System.Diagnostics;
using System.Reflection;

namespace NetProxy.Tests
{
    public class DirectAPITests
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
                            string cnnStr = ((string)configuration.GetValue(typeof(string), "ProxyConnectionString"));

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
            NetProxyController proxyService = new NetProxyController();
            try
            {
                List<Task<bool>> tasks = new List<Task<bool>>();
                int commandTimeout = 10 * 1000;
                tasks.Add(SimulateSQLLoad(5, commandTimeout, commandTimeout - 2000, cancellationToken, 1000));
                tasks.Add(Task.Factory.StartNew(() =>
                {
                    try
                    {

                        string cnnStr = ((string)configuration.GetValue(typeof(string), "DirectConnectionString"));
                        proxyService.Start(new Core.ProxyConfig()
                        {
                            directConnectionString = cnnStr,
                            forwardIp = "127.0.0.1",
                            localIp = "127.0.0.1",
                            forwardPort = 1433,
                            localPort = 4433,
                            protocol = "tcp"
                        });
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
                proxyService.Stop(waitTime: 0, forceStopAfterWaitTime: true);
            }
        }

        [Fact]
        public async void ProxyPausedStartTest()
        {
            Trace.WriteLine("==========================================================");
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = cancellationTokenSource.Token;
            NetProxyController proxyService = new NetProxyController();
            try
            {
                List<Task<bool>> tasks = new List<Task<bool>>();
                int commandTimeout = 10 * 1000;
                tasks.Add(SimulateSQLLoad(5, commandTimeout, commandTimeout - 2000, cancellationToken, 1000));
                tasks.Add(Task.Factory.StartNew(() =>
                {
                    try
                    {

                        string cnnStr = ((string)configuration.GetValue(typeof(string), "DirectConnectionString"));
                        proxyService.PausedStart(new Core.ProxyConfig()
                        {
                            directConnectionString = cnnStr,
                            forwardIp = "127.0.0.1",
                            localIp = "127.0.0.1",
                            forwardPort = 1433,
                            localPort = 4433,
                            protocol = "tcp"
                        });
                        proxyService.UnPause();
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
                proxyService.Stop(waitTime: 0, forceStopAfterWaitTime: true);
            }
        }


        [Fact]
        public async void ProxyStopTest()
        {
            Trace.WriteLine("==========================================================");
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = cancellationTokenSource.Token;
            int commandTimeout = 10 * 1000;
            NetProxyController proxyService = new NetProxyController();
            try
            {
                List<Task<bool>> tasks = new List<Task<bool>>();
                Task<bool> simulateSQLLoadTask;
                Task<bool> netproxyTask;
                tasks.Add(simulateSQLLoadTask = SimulateSQLLoad(5, commandTimeout, commandTimeout - 2000, cancellationToken, 1000));
                tasks.Add(netproxyTask = Task.Factory.StartNew(() =>
                {
                    try
                    {
                        string cnnStr = ((string)configuration.GetValue(typeof(string), "DirectConnectionString"));
                        proxyService.Start(new Core.ProxyConfig()
                        {
                            directConnectionString = cnnStr,
                            forwardIp = "127.0.0.1",
                            localIp = "127.0.0.1",
                            forwardPort = 1433,
                            localPort = 4433,
                            protocol = "tcp"
                        });
                        Thread.Sleep(2000);
                        proxyService.Stop(commandTimeout);
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
                proxyService.Stop(waitTime: 0, forceStopAfterWaitTime: true);
            }
        }


        [Fact]
        public async void ProxyPausedTransfer_SmallLoad_NotForced()
        {
            Trace.WriteLine("==========================================================");
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = cancellationTokenSource.Token;
            NetProxyController proxyService = new NetProxyController();
            try
            {
                List<Task<bool>> tasks = new List<Task<bool>>();
                int commandTimeout = 10 * 1000;
                tasks.Add(SimulateSQLLoad(5, commandTimeout, commandTimeout - 2000, cancellationToken, 1000));
                tasks.Add(Task.Factory.StartNew(() =>
                {
                    try
                    {

                        string cnnStr = ((string)configuration.GetValue(typeof(string), "DirectConnectionString"));
                        var proyConfig = new Core.ProxyConfig()
                        {
                            directConnectionString = cnnStr,
                            forwardIp = "127.0.0.1",
                            localIp = "127.0.0.1",
                            forwardPort = 1433,
                            localPort = 4433,
                            protocol = "tcp"
                        };
                        proxyService.Start(proyConfig);
                        Thread.Sleep(2000);
                        if (!proxyService.PausedTransfer(proyConfig, waitTime: commandTimeout).IsOk())
                        {
                            Trace.WriteLine("Proxy was not transfered");
                            cancellationTokenSource.Cancel();
                            return false;
                        }
                        proxyService.UnPause();
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
                proxyService.Stop(waitTime: 0, forceStopAfterWaitTime: true);
            }
        }

        [Fact]
        public async void ProxyTransfer_SmallLoad_NotForced()
        {
            Trace.WriteLine("==========================================================");
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = cancellationTokenSource.Token;
            NetProxyController proxyService = new NetProxyController();
            try
            {
                List<Task<bool>> tasks = new List<Task<bool>>();
                int commandTimeout = 10 * 1000;
                tasks.Add(SimulateSQLLoad(5, commandTimeout, commandTimeout - 2000, cancellationToken, 1000));
                tasks.Add(Task.Factory.StartNew(() =>
                {
                    try
                    {

                        string cnnStr = ((string)configuration.GetValue(typeof(string), "DirectConnectionString"));
                        var proyConfig = new Core.ProxyConfig()
                        {
                            directConnectionString = cnnStr,
                            forwardIp = "127.0.0.1",
                            localIp = "127.0.0.1",
                            forwardPort = 1433,
                            localPort = 4433,
                            protocol = "tcp"
                        };
                        proxyService.Start(proyConfig);
                        Thread.Sleep(2000);
                        if (!proxyService.Transfer(proyConfig, waitTime: commandTimeout).IsOk())
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
                proxyService.Stop(waitTime: 0, forceStopAfterWaitTime: true);
            }
        }

        [Fact]
        public async void ProxyPausedTransfer_BigLoad_Forced()
        {
            Trace.WriteLine("==========================================================");
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = cancellationTokenSource.Token;
            NetProxyController proxyService = new NetProxyController();
            try
            {
                List<Task<bool>> tasks = new List<Task<bool>>();
                int commandTimeout = 10 * 1000;
                tasks.Add(SimulateSQLLoad(10, commandTimeout, commandTimeout - 2000, cancellationToken, 1000));
                Task<bool> netproxyTask;
                tasks.Add(netproxyTask = Task.Factory.StartNew(() =>
                {
                    try
                    {

                        string cnnStr = ((string)configuration.GetValue(typeof(string), "DirectConnectionString"));
                        var proyConfig = new Core.ProxyConfig()
                        {
                            directConnectionString = cnnStr,
                            forwardIp = "127.0.0.1",
                            localIp = "127.0.0.1",
                            forwardPort = 1433,
                            localPort = 4433,
                            protocol = "tcp"
                        };
                        proxyService.Start(proyConfig);
                        Thread.Sleep(2000);
                        if (!proxyService.PausedTransfer(proyConfig, waitTime: commandTimeout, forceStopAfterWaitTime: true).IsOk())
                        {
                            Trace.WriteLine("Proxy was not transfered");
                            cancellationTokenSource.Cancel();
                            return false;
                        }
                        proxyService.UnPause();
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
                proxyService.Stop(waitTime: 0, forceStopAfterWaitTime: true);
            }
        }

        [Fact]
        public async void ProxyPausedTransfer_BigLoad_MultipleTries()
        {
            Trace.WriteLine("==========================================================");
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = cancellationTokenSource.Token;
            NetProxyController proxyService = new NetProxyController();
            try
            {
                List<Task<bool>> tasks = new List<Task<bool>>();
                int commandTimeout = 10 * 1000;
                tasks.Add(SimulateSQLLoad(10, commandTimeout, commandTimeout - 2000, cancellationToken, 1000, true));
                tasks.Add(Task.Factory.StartNew(() =>
                {
                    try
                    {

                        string cnnStr = ((string)configuration.GetValue(typeof(string), "DirectConnectionString"));
                        var proyConfig = new Core.ProxyConfig()
                        {
                            directConnectionString = cnnStr,
                            forwardIp = "127.0.0.1",
                            localIp = "127.0.0.1",
                            forwardPort = 1433,
                            localPort = 4433,
                            protocol = "tcp"
                        };
                        proxyService.Start(proyConfig);
                        Thread.Sleep(2000);
                        for (int i = 0; i < 10; i++)
                        {
                            if (!proxyService.PausedTransfer(proyConfig, waitTime: commandTimeout).IsOk())
                                Trace.WriteLine("Proxy was not transfered");
                            else
                                break;
                        }
                        proxyService.UnPause();
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
                proxyService.Stop(waitTime: 0, forceStopAfterWaitTime: true);
            }
        }
    }
}