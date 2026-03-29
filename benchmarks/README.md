# Benchmarks

This directory contains BenchmarkDotNet suites for the current `Manifold` hot paths.

## CLI benchmarks

`Manifold.Benchmarks` compares:

- `Manifold.Cli`
- `System.CommandLine`
- `ConsoleAppFramework`

Current scenarios:

- positional command parsing and invocation
- option-heavy command parsing and invocation

These are in-process benchmarks that focus on parser and dispatcher overhead. The sample commands write into a shared non-allocating benchmark sink so the comparison is dominated by parsing and invocation work, not by formatter, stdout, or sample-operation string creation.

### Current CLI snapshot

Environment:

- Windows 11
- .NET 10.0.1
- BenchmarkDotNet `ShortRun`

| Scenario | Manifold | ConsoleAppFramework | System.CommandLine |
| --- | ---: | ---: | ---: |
| Positional command | `22.61 ns / 0 B` | `26.57 ns / 0 B` | `1730.82 ns / 4688 B` |
| Option-heavy command | `28.89 ns / 0 B` | `24.76 ns / 0 B` | `2110.84 ns / 5632 B` |

![CLI benchmark chart](../assets/benchmarks/cli-latency.svg)

Source reports:

- `BenchmarkDotNet.Artifacts/results/Manifold.Benchmarks.CliPositionalBenchmarks-report-github.md`
- `BenchmarkDotNet.Artifacts/results/Manifold.Benchmarks.CliOptionBenchmarks-report-github.md`

## MCP benchmarks

`Manifold.Mcp.Benchmarks` compares:

- `Manifold.Mcp`
- `ModelContextProtocol`
- `McpToolkit`
- `mcpdotnet`

Current scenarios:

- cached tool catalog access
- local tool invocation overhead
- in-memory `tools/list` response generation
- in-memory `tools/call` response generation

These are still not transport benchmarks. They measure server-side work only, but now in two layers:

- microbenchmarks for cached metadata access and nearest local invocation
- roundtrip-shape benchmarks that include in-memory `tools/list` / `tools/call` response construction

For Manifold, sync tools go through generated `TryInvokeFastSync(tool name, JsonElement args, ...)`; tools that require async completion still go through the generated `TryInvokeFast(...)` path. The roundtrip benchmarks stop at JSON response bytes in memory, so they are closer to `tools/list` and `tools/call` behavior than the local wrapper microbenchmarks, but they still exclude transport, protocol framing, and I/O.

### Current MCP snapshot

Environment:

- Windows 11
- .NET 10.0.1
- BenchmarkDotNet `ShortRun`

Microbenchmarks:

| Scenario | Manifold | ModelContextProtocol | McpToolkit | McpDotNet |
| --- | ---: | ---: | ---: | ---: |
| Discovery | `0.9560 ns / 0 B` | `1.0672 ns / 0 B` | `0.9664 ns / 0 B` | `0.9727 ns / 0 B` |
| Invocation | `36.92 ns / 0 B` | `0.0261 ns / 0 B` | `151.88 ns / 96 B` | `51.75 ns / 256 B` |

Roundtrip-shape benchmarks:

| Scenario | Manifold | ModelContextProtocol | McpToolkit | McpDotNet |
| --- | ---: | ---: | ---: | ---: |
| `tools/list` response | `756.3 ns / 0 B` | `754.6 ns / 0 B` | `635.4 ns / 0 B` | `816.2 ns / 0 B` |
| `tools/call` response | `47.16 ns / 0 B` | `68.56 ns / 0 B` | `146.34 ns / 96 B` | `93.20 ns / 256 B` |

![MCP benchmark chart](../assets/benchmarks/mcp-roundtrip.svg)

Notes:

- The `ModelContextProtocol` invocation microbenchmark is near `ZeroMeasurement`; treat it as a weak comparison point.
- The roundtrip-shape numbers are more representative than the raw invocation microbenchmark when comparing full `tools/list` and `tools/call` response construction.

Source reports:

- `BenchmarkDotNet.Artifacts/results/Manifold.Mcp.Benchmarks.McpDiscoveryBenchmarks-report-github.md`
- `BenchmarkDotNet.Artifacts/results/Manifold.Mcp.Benchmarks.McpInvocationBenchmarks-report-github.md`
- `BenchmarkDotNet.Artifacts/results/Manifold.Mcp.Benchmarks.McpListToolsRoundtripBenchmarks-report-github.md`
- `BenchmarkDotNet.Artifacts/results/Manifold.Mcp.Benchmarks.McpCallToolRoundtripBenchmarks-report-github.md`

## Running

From the repository root:

```powershell
./build/benchmark.ps1
./build/benchmark.ps1 -Target cli
./build/benchmark.ps1 -Target mcp
./build/benchmark.ps1 -Target cli -- --filter *Option*
```

When no BenchmarkDotNet arguments are provided, the wrapper passes `--filter *` so the run stays non-interactive.
