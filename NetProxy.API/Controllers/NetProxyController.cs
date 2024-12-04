using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NetProxy;
using Microsoft.AspNetCore.Mvc.Formatters;
using NetProxy.Core;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using Microsoft.AspNetCore.Http;

namespace NetProxy.API.Controllers
{

    public static class ResultExtensions
    {
        public static bool IsOk(this IActionResult result)
        {
            return (result is StatusCodeResult && ((StatusCodeResult)result).StatusCode == StatusCodes.Status200OK) || (result is ObjectResult && ((ObjectResult)result).StatusCode == StatusCodes.Status200OK);
        }
    }


    [ApiController]
    [Route("[controller]")]
    public class NetProxyController : ControllerBase
    {
        private static object _criticalLock = new object();

        [HttpPost]
        [Route("start")]
        public IActionResult Start([FromBody]ProxyConfig config)
        {
            
            if (ProxyFactory.Proxies.Count > 0)
            {
                Trace.WriteLine("Service is still running");
                return BadRequest("Service is still running");
            }
            ProxyFactory.Proxies.Clear();
            ProxyPause.WaitHandle.Set();
            ProxyFactory.ProxyFromConfig(config);

            Trace.WriteLine("SQL proxy STARTED");
            return Ok("SQL proxy STARTED");
        }

        [HttpPost]
        [Route("pausedstart")]
        public IActionResult PausedStart([FromBody] ProxyConfig config)
        {
            
            if (ProxyFactory.Proxies.Count > 0)
            {
                Trace.WriteLine("Service is still running");
                return BadRequest("Service is still running");
            }
            ProxyFactory.Proxies.Clear();

            new Thread(() =>
            {
                try
                {
                    ProxyPause.WaitHandle.Reset();

                    ProxyFactory.ProxyFromConfig(config);
                }
                catch { }
            }).Start();

            Trace.WriteLine("SQL proxy STARTED");
            return Ok("SQL proxy STARTED");
        }

        [HttpGet]
        [Route("unpause")]
        public IActionResult UnPause()
        {
            ProxyPause.WaitHandle.Set();
            Trace.WriteLine("SQL proxy UNPAUSED");
            return Ok("SQL proxy UNPAUSED");
        }

        private async Task<bool> StopProxies(int waitTime, bool forceStopAfterWaitTime = false)
        {
            Trace.WriteLine($"Trying to stop proxies {ProxyFactory.Proxies.Count}");
            foreach (var p in ProxyFactory.Proxies)
            {
                if (!p.Stop(waitTime, forceStopAfterWaitTime))
                {
                    Trace.WriteLine("Failed to stop proxies");
                    return false;
                }
            }
            while (ProxyFactory.Proxies.Any(o => !o.FullyStopped))
            {
                await Task.Delay(1000);
            }
            Trace.WriteLine("All proxies FULLY STOPPED");
            ProxyFactory.Proxies.Clear();
            return true;
        }


        [HttpGet]
        [Route("stop")]
        public IActionResult Stop(int waitTime = 5000, bool forceStopAfterWaitTime = false)
        {
            Trace.WriteLine($"SQL proxy starting stop waitTime: {waitTime}");
            ProxyPause.WaitHandle.Set();
            if (ProxyFactory.Proxies.Count > 0)
            {
                if (!Task.Run(() => StopProxies(waitTime, forceStopAfterWaitTime)).Result)
                {
                    Trace.WriteLine("SQL proxy FAILED to stop");
                    return BadRequest("SQL proxy FAILED to stop");
                }
            }
            
            Trace.WriteLine("SQL proxy STOPPED");
            return Ok("SQL proxy STOPPED");
        }

        [HttpGet]
        [Route("reset")]
        public IActionResult Reset()
        {
            Trace.WriteLine($"SQL proxy reset");
            ProxyPause.WaitHandle.Set();
            if (ProxyFactory.Proxies.Count > 0)
            {
                if (!Task.Run(() => StopProxies(0, true)).Result)
                {
                    Trace.WriteLine("SQL proxy FAILED to stop");
                    return BadRequest("SQL proxy FAILED to stop");
                }
            }

            Trace.WriteLine("SQL proxy was reset");
            return Ok("SQL proxy was reset");
        }


        [HttpPost]
        [Route("transfer")]
        public IActionResult Transfer([FromBody] ProxyConfig config, int waitTime = 5000, bool forceStopAfterWaitTime = false)
        {
            bool failed = false;
            if (Monitor.TryEnter(_criticalLock, new TimeSpan(0, 0, 1)))
            {
                try
                {
                    if (Stop(waitTime, forceStopAfterWaitTime).IsOk())
                    {
                        Start(config);
                        Trace.WriteLine("SQL proxy TRANSFERRED");
                    }
                    else
                    {
                        Trace.WriteLine("SQL proxy FAILED TO TRANSFERRED");
                        failed = true;
                    }
                }
                catch (Exception ex)
                {
                    failed = true;
                    Trace.WriteLine($" {ex.Message} {ex.StackTrace}");
                }
                finally
                {
                    Monitor.Exit(_criticalLock);
                }
               
                string msg =  $"SQL proxy {(failed ? "FAILED TO TRANSFER" : "TRANSFERRED")} {DateTime.Now}, waitTime {waitTime}, forceStopAfterWaitTime {forceStopAfterWaitTime}";
                if (failed)
                    return BadRequest(msg);
                else
                    return Ok(msg);
            }
            else
            {
                Trace.WriteLine("SERVICE IS IN USE");
                return BadRequest("SERVICE IS IN USE");
            }
        }

        [HttpPost]
        [Route("pausedtransfer")]
        public IActionResult PausedTransfer([FromBody] ProxyConfig config, int waitTime = 5000, bool forceStopAfterWaitTime = false)
        {
            bool failed = false;
            if (Monitor.TryEnter(_criticalLock, new TimeSpan(0, 0, 1)))
            {
                try
                {
                    if (Stop(waitTime, forceStopAfterWaitTime).IsOk())
                    {
                        PausedStart(config);
                        Trace.WriteLine("SQL proxy TRANSFERRED");
                    }
                    else
                    {
                        Trace.WriteLine("SQL proxy FAILED TO TRANSFERRED");
                        failed = true;
                    }
                }
                catch (Exception ex)
                {
                    failed = true;
                    Trace.WriteLine($" {ex.Message} {ex.StackTrace}");
                }
                finally
                {
                    Monitor.Exit(_criticalLock);
                }
                string msg = $"SQL proxy {(failed ? "FAILED TO TRANSFER" : "TRANSFERRED")} {DateTime.Now}, waitTime {waitTime}, forceStopAfterWaitTime {forceStopAfterWaitTime}";
                if (failed)
                    return BadRequest(msg);
                else
                    return Ok(msg);
            }
            else
            {
                Trace.WriteLine("SERVICE IS IN USE");
                return BadRequest("SERVICE IS IN USE");
            }
        }

    }
}
