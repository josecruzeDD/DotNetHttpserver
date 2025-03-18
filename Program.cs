using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Trace;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

class Program
{
    static async Task Main(string[] args)
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.AddOpenTelemetry(options =>
                {
                    options.AddOtlpExporter(); // Export logs to OTLP (Datadog)
                });
            })
            .ConfigureServices((context, services) =>
            {
                services.AddOpenTelemetry().WithTracing(builder =>
                {
                    builder
                        .AddSource("DotNetHttpServer")
                        .SetResourceBuilder(OpenTelemetry.Resources.ResourceBuilder.CreateDefault().AddService("DotNetHttpServer"))
                        .AddHttpClientInstrumentation()
                        .AddOtlpExporter(); // Export traces to OTLP (Datadog)
                });
            })
            .Build();

        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        var tracer = TracerProvider.Default.GetTracer("DotNetHttpServer");

        logger.LogInformation("Starting HTTP server...");

        // Start HTTP server
        HttpListener listener = new HttpListener();
        listener.Prefixes.Add("http://*:80/"); // Listen on port 8080
        listener.Start();

        logger.LogInformation("Server started on port 80");

        while (true)
        {
            var context = await listener.GetContextAsync();
            _ = Task.Run(async () =>
            {
                using (var span = tracer.StartActiveSpan("http_request"))
                {
                    HttpListenerRequest request = context.Request;
                    HttpListenerResponse response = context.Response;

                    logger.LogInformation($"Received request: {request.HttpMethod} {request.Url}");

                    string responseString = "Hello from .NET HTTP Server!";
                    byte[] buffer = Encoding.UTF8.GetBytes(responseString);

                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    response.OutputStream.Close();

                    logger.LogInformation("Response sent.");
                }
            });
        }
    }
}

