using Manifold.Generated;
using Manifold.Samples.Operations;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:38474");

builder.Services.AddTransient<WeatherPreviewOperation>();
builder.Services.AddGeneratedMcpServer()
    .WithHttpTransport();

WebApplication app = builder.Build();

app.MapGet("/", () => Results.Text(
    "Manifold MCP sample host is running.\nPOST MCP requests to /mcp or connect with a Streamable HTTP MCP client.",
    "text/plain"));

app.MapMcp("/mcp");

await app.RunAsync();
