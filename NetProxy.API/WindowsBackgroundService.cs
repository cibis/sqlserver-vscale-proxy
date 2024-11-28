using NetProxy.API;
using Microsoft.AspNetCore.Builder;
using System.Diagnostics;

namespace App.WindowsService;

public sealed class WindowsBackgroundService(
    ILogger<WindowsBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var apibuilder = WebApplication.CreateBuilder(new string[0]);
            // Add services to the container.

            apibuilder.Services.AddControllers();

            var app = apibuilder.Build();

            // Configure the HTTP request pipeline.

            app.UseAuthorization();


            app.MapControllers();

            app.Run("http://0.0.0.0:" + new ConfigurationBuilder().AddJsonFile("appsettings.json").Build().GetValue(typeof(string), "APIPort"));


            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // When the stopping token is canceled, for example, a call made from services.msc,
            // we shouldn't exit with a non-zero exit code. In other words, this is expected...
        }
        catch (Exception ex)
        {
            Trace.WriteLine("Error: " + ex.Message);

            // Terminates this process and returns an exit code to the operating system.
            // This is required to avoid the 'BackgroundServiceExceptionBehavior', which
            // performs one of two scenarios:
            // 1. When set to "Ignore": will do nothing at all, errors cause zombie services.
            // 2. When set to "StopHost": will cleanly stop the host, and log errors.
            //
            // In order for the Windows Service Management system to leverage configured
            // recovery options, we need to terminate the process with a non-zero exit code.
            Environment.Exit(1);
        }
    }
}