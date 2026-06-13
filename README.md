# WebSharper Compiler Performance Work

This folder holds the planning documents, benchmark harness design, and measurement
data for the effort to **improve WebSharper compiler performance, focused on F#**.

It is a working area, not shipped product. Documents here are living: update them as
ideas are validated or discarded by measurement.

## Location

These files live on the **`perftesting` branch of the `build-script` repo** (an orphan
branch, kept separate from `master`/GHA and `websharper80`/build-helpers), to avoid
polluting the `core` repo. Code links below are relative to a checkout that sits as a
sibling of the `core` and `dotnet-ws` repos (e.g. `h:\dotnet-websharper\build-script-perftesting`
next to `h:\dotnet-websharper\core`).

> **Actions caveat:** when the harness lands, `perf-benchmark.yml` must also exist on the
> repo's default branch for GitHub to expose it to `workflow_dispatch`. So the runnable
> workflow goes on `master`; this branch holds the design + data. Keep the two in sync, or
> have the workflow `git checkout` the scenario scripts from this branch at run time.

## Documents

| File | What it covers |
|---|---|
| [00-overview-and-plan.md](00-overview-and-plan.md) | Goals, success criteria, the full work breakdown, sequencing, risks. Read this first. |
| [01-benchmark-harness.md](01-benchmark-harness.md) | The reproducible benchmark: a GitHub Action that builds WS+UI+Templates from a branch via `../localnuget`, scaffolds a representative solution, and measures cold/warm compiles on a 3-OS matrix. |
| [02-parallel-fsharp-and-1587.md](02-parallel-fsharp-and-1587.md) | Idea 1 — run plain F# compilation in parallel outside WebSharper; prerequisite [#1587](https://github.com/dotnet-websharper/core/issues/1587) making current-output-dll reflection opt-in; optional/configurable RPC-verification await. **First implementation target.** |
| [03-sidecar-metadata.md](03-sidecar-metadata.md) | Idea 2 — move WS resources/metadata into `MyProject.dll.websharper` (+ `.websharperruntime`) sidecar files; MSBuild copy + NuGet pack. |
| [04-log-analysis.md](04-log-analysis.md) | Idea 3 — turn `TimedStage` logs into machine-readable per-stage data to find the weakest spots. |
| [05-multiproject-watch.md](05-multiproject-watch.md) | Idea 4 — multi-project dependency tracking in `dotnet ws watch` for client-only changes (translate → propagate JS → esbuild) without recompiling dlls. |
| [data/README.md](data/README.md) | How benchmark results are stored (JSONL, one record per scenario, tagged with commit/branch/OS) and the schema. |

## Quick facts the design relies on (verified in the tree)

- `createAssemblyResolver config includeCurrent` in
  [../core/src/compiler/WebSharper.Compiler.FSharp/Compile.fs](../core/src/compiler/WebSharper.Compiler.FSharp/Compile.fs)
  adds the **just-compiled output dll** (`config.AssemblyFile`) to the resolver search
  paths when `includeCurrent = true`. This is the reflection-on-own-output behavior #1587
  wants to make opt-in, and the thing that currently forces F# compilation to finish
  before WS translation can safely run.
- F# compilation is a single synchronous `checker.Compile(...)` call; reference WS
  metadata is already loaded in parallel via `Task.Run` (`wsRefsMeta`). See `Compile`.
- The booster service (`wsfscservice`,
  [../core/src/compiler/WebSharper.FSharp.Service/Program.fs](../core/src/compiler/WebSharper.FSharp.Service/Program.fs))
  keeps a warm `FSharpChecker.Create(keepAssemblyContents = true)` and caches reference
  metadata + project args/timestamps; this is what makes warm recompiles fast.
- WS metadata is embedded as Mono.Cecil `EmbeddedResource`s (`WebSharper.meta`,
  `WebSharper.runtime.meta`) in
  [../core/src/compiler/WebSharper.Compiler/FrontEnd.fs](../core/src/compiler/WebSharper.Compiler/FrontEnd.fs).
- `dotnet ws watch`
  ([../dotnet-ws/Program.fs](../dotnet-ws/Program.fs)) watches the current dir, maps
  files to projects from each `.fsproj`, and rebuilds **one project** per change with no
  cross-project propagation.
- Timing is emitted by `LoggerBase.TimedStage`
  ([../core/src/compiler/WebSharper.Compiler/LoggerBase.fs](../core/src/compiler/WebSharper.Compiler/LoggerBase.fs))
  as `"<stage>: <TimeSpan>"`, indented by context depth — human-readable only today.

## Decisions taken

- **Results storage:** commit JSONL records to [data/](data/) (diffable, self-contained),
  plus upload raw build logs as CI artifacts.
- **First implementation target:** Idea 1 (parallel F# compile + #1587). Log analysis
  (Idea 3) is done alongside it because it supplies the before/after evidence.
