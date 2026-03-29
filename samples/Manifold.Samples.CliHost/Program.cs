using System.Text.Json;
using Manifold.Cli;
using Manifold.Generated;
using Manifold.Samples.Operations;
using Microsoft.Extensions.DependencyInjection;

ServiceCollection services = new();
services.AddTransient<WeatherPreviewOperation>();

IServiceProvider serviceProvider = services.BuildServiceProvider();

CliApplication application = new(
    GeneratedOperationRegistry.Operations,
    new GeneratedCliInvoker(),
    serviceProvider,
    rawOutput: Console.OpenStandardOutput(),
    jsonSerializerOptions: new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    });

return await application.ExecuteAsync(args, Console.Out, Console.Error);
