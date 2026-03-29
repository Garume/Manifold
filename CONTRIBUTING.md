# Contributing

## Development flow

From the repository root:

```powershell
./build/restore.ps1
./build/build.ps1 -NoRestore
./build/test.ps1
./build/quality.ps1
./build/pack.ps1
```

## Expectations

- Keep handwritten operation definitions as the source of truth.
- Keep CLI and MCP behavior aligned through generated descriptors and invokers.
- Add or update tests for behavior changes.
- Run `./build/quality.ps1` before opening a pull request.

## Formatting

- `.editorconfig` is the source of truth.
- `./build/format.ps1 -NoRestore` checks formatting.

## Benchmarks

Benchmark suites live under `benchmarks/`.

```powershell
./build/benchmark.ps1 -Target cli
./build/benchmark.ps1 -Target mcp
```

