using NetProxy.Core;

namespace NetProxy.Console
{
    internal class Program
    {
        static void Main(string[] args)
        {
            try
            {
                var configJson = System.IO.File.ReadAllText("config.json");
                Dictionary<string, ProxyConfig>? configs = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ProxyConfig>>(configJson);
                if (configs == null)
                {
                    throw new Exception("configs is null");
                }

                var tasks = configs.Select(c => ProxyFactory.ProxyFromConfig(c.Value, c.Key));
                Task.WhenAll(tasks).Wait();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"An error occurred : {ex}");
            }
        }
    }
}
