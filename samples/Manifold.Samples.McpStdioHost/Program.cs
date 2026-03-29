using Manifold.Generated;
using Manifold.Samples.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddTransient<WeatherPreviewOperation>();
builder.Services.AddGeneratedMcpServer()
    .WithStdioServerTransport();

IHost host = builder.Build();
await host.RunAsync();
