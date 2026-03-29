# Samples

All hosts share the same operations defined in `Manifold.Samples.Operations`.

## CLI

```powershell
dotnet run --project .\samples\Manifold.Samples.CliHost\Manifold.Samples.CliHost.csproj -- math add 2 3
dotnet run --project .\samples\Manifold.Samples.CliHost\Manifold.Samples.CliHost.csproj -- weather preview --city Tokyo --days 3
```

## MCP over stdio

```powershell
dotnet run --project .\samples\Manifold.Samples.McpStdioHost\Manifold.Samples.McpStdioHost.csproj
```

This sample uses the official `ModelContextProtocol` server transport for `stdio`.

## MCP over Streamable HTTP

```powershell
dotnet run --project .\samples\Manifold.Samples.McpHttpHost\Manifold.Samples.McpHttpHost.csproj
```

The HTTP sample listens on `http://127.0.0.1:38474` and maps MCP at `/mcp`.

Quick smoke check:

```powershell
Invoke-WebRequest http://127.0.0.1:38474/
```

This should return a short text response saying that the sample host is running.

If you use MCP Inspector or another Streamable HTTP MCP client, point it at:

- `http://127.0.0.1:38474/mcp`

The current official MCP transports are:

- `stdio`
- `Streamable HTTP`

Older `HTTP+SSE` examples exist in the ecosystem, but they are not the main path to copy for new hosts.
