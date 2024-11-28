using App.WindowsService;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;


public static class Program
{
    public static async Task Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "SQL Server NetProxy";
        });

        builder.Services.AddHostedService<WindowsBackgroundService>();

        IHost host = builder.Build();
        host.Run();
    }
}