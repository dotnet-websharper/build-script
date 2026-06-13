# Benchmark Harness Design

The harness must give **reliable, comparable** numbers for a WebSharper branch, on a fixed
representative solution, across Windows/Linux/macOS, for cold compiles and warm recompiles.
It models itself on the existing `build-stack.yml` (branch-driven stack build chained via
`../localnuget`) and `test-templates.yml` (3-OS matrix + scaffolded solution) workflows in
the `build-script` repo.

## What "reliable comparison" requires

Compiler timing is noisy. The harness controls for the main sources:

- **JIT / process warmup** — first invocation of the compiler pays JIT cost. We measure cold
  compile as its own phase and take warm recompiles separately, and we run N repetitions per
  phase, reporting median + min (min ≈ least-disturbed run) rather than a single sample.
- **FCS / booster caches** — cold phase starts from a clean `obj`/`bin` and (for the cold
  number) no booster, or a freshly started booster; warm phase explicitly pre-warms the
  booster then edits.
- **CI runner variance** — absolute numbers differ per runner; we always report a branch
  **relative to a baseline run of `master` from the same workflow invocation**, on the same
  OS, so the comparison is within-runner-class. Every record carries the commit SHA + branch.
- **Disk/filesystem** — scenarios live under the runner workspace; we never measure NuGet
  restore time inside a compile measurement (restore is done in a separate, untimed step).

## Topology: two workflows or one?

The stack build (build WS+UI+Templates from a branch into `../localnuget`) is Windows-centric
and slow; the *measurement* must run on all three OSes. Two viable shapes:

- **A (recommended): build once, measure many.** A `build` job (windows-latest, mirrors
  `build-stack.yml` for `core`+`ui`+`templates` only) produces the `localnuget` packages and
  uploads them as an artifact. A `measure` matrix job (windows/ubuntu/macos) downloads the
  artifact, installs the locally built templates, scaffolds the solution, and runs the
  benchmark. Pro: the heavy stack build happens once; the three measurement legs consume
  identical packages, so cross-OS numbers are comparable. Con: the produced packages are
  built on Windows (compiler is portable .NET, so this is fine — we measure the *compiler*,
  which is the same IL everywhere).
- **B: build-and-measure per OS.** Each matrix leg builds the stack and measures. Pro:
  fully native per OS. Con: 3× the expensive build, and the booster/compiler binaries differ
  per leg, adding a confounder. Rejected unless we specifically need to benchmark
  platform-specific compiler builds.

We go with **A**.

## Workflow: `perf-benchmark.yml` (in build-script)

```yaml
name: 3. Benchmark WebSharper Compiler
on:
  workflow_dispatch:
    inputs:
      branch:        { description: 'Core branch to benchmark',        default: '' }       # WS main
      baselineBranch:{ description: 'Baseline branch to compare to',   default: 'master' }
      uiBranch:      { description: 'WS.UI branch',                     default: 'master' }
      templatesBranch:{description: 'WS.Templates branch',             default: 'master' }
      dotnetVersion: { description: '.NET SDK version',                default: '10.0.x' }
      reps:          { description: 'Repetitions per phase',           default: '5' }

jobs:
  build:
    runs-on: windows-latest
    env: { WSPackageFolder: ../localnuget, BUILD_NUMBER: ${{ github.run_number }} }
    strategy:
      matrix: { which: [branch, baseline] }      # build the stack twice: candidate + baseline
    steps:
      - # setup .NET (target + 8.0.x), paket, esbuild, GH nuget source  (as build-stack.yml)
      - # checkout core @ (which==branch ? inputs.branch : inputs.baselineBranch)
      - run: ./build CI-Release            # core   -> ../localnuget
        working-directory: ./core
      - # purge ~/.nuget/packages/websharper* ; paket update --force on ui
      - run: ./build CI-Release            # ui     -> ../localnuget
        working-directory: ./ui
      - # purge again ; build templates
      - run: ./build CI-Release            # templates -> ../localnuget
        working-directory: ./templates
      - # capture the commit SHA for this 'which' into a file
      - uses: actions/upload-artifact@v4
        with: { name: localnuget-${{ matrix.which }}, path: ./localnuget }

  measure:
    needs: build
    runs-on: ${{ matrix.os }}
    strategy:
      fail-fast: false
      matrix:
        os: [windows-latest, ubuntu-latest, macos-latest]
        which: [branch, baseline]
    steps:
      - # setup .NET ; install esbuild (needed for WS8+ bundling)
      - uses: actions/download-artifact@v4
        with: { name: localnuget-${{ matrix.which }}, path: ./localnuget }
      - run: dotnet nuget add source $PWD/localnuget --name localperf
      - # install the locally built templates from ./localnuget
      - run: dotnet new install WebSharper.Templates::<version-from-localnuget>
      - run: ./perf-scaffold.sh             # creates the scenario solution
      - run: dotnet restore Perf.sln        # untimed
      - run: dotnet fsi ./perf-run.fsx --os ${{ matrix.os }} --which ${{ matrix.which }} \
               --reps ${{ github.event.inputs.reps }} --sha <sha> --branch <branch>
      - uses: actions/upload-artifact@v4    # raw logs
        with: { name: perf-logs-${{ matrix.os }}-${{ matrix.which }}, path: ./perf-out/logs }
      - uses: actions/upload-artifact@v4    # JSONL results
        with: { name: perf-results-${{ matrix.os }}-${{ matrix.which }}, path: ./perf-out/results.jsonl }

  collect:
    needs: measure
    runs-on: ubuntu-latest
    steps:
      - # download all perf-results-* artifacts
      - run: dotnet fsi ./aggregate.fsx      # -> markdown comparison into $GITHUB_STEP_SUMMARY
      - # commit merged JSONL into perftesting/data on the core branch (see data/README.md)
```

Notes:
- Mirrors `build-stack.yml`'s package-chaining recipe (`WSPackageFolder=../localnuget`,
  purge `~/.nuget/packages/<pkg>`, `paket update --force`, build in order core→ui→templates).
  Only those three repos are needed for the scenario.
- Building both `branch` and `baseline` in the same invocation is what makes the comparison
  honest: same SDK, same runner image, same scenario, only the WS compiler differs.

## Scenario scaffolder: `perf-scaffold.sh`

Built on the same `dotnet new websharper-*` template set used by `test-templates.sh`, but
arranged into a **complex dependency structure** designed to exercise both first-compile and
recompile propagation. Target: enough projects/code that totals are well above noise, but the
build still fits comfortably in CI time.

```
Perf.sln
├── Core.Domain        (websharper-lib, F#)      leaf library, pure model + some [<JavaScript>]
├── Core.Shared        (websharper-lib, F#)      -> Core.Domain
├── Client.Components  (websharper-lib, F#)      -> Core.Shared       (client-heavy: UI.Next templates)
├── Client.Components2 (websharper-lib, F#)      -> Core.Shared       (parallel sibling, for fan-out)
├── Server.Api         (websharper-lib, F#)      -> Core.Shared       (Remoting RPCs -> exercises RPC verify)
├── App.Spa            (websharper-spa, F#)      -> Client.Components(2), Core.Shared
└── App.Web            (websharper-web, F#)      -> all of the above  (sitelet: bundling + runtime meta)
```

- Each library gets a parameterized amount of generated source (e.g. `N` modules ×
  `M` functions) so we can scale load without hand-writing it, and so the scenario is
  deterministic. The generator script (`gen-sources.fsx`) is part of the scaffolder.
- `App.Web` is a `websharper-web` sitelet so the bundling + `WebSharper.runtime.meta` path
  and (WS8+) esbuild are measured. `App.Spa` measures the SPA/`Bundle` path.
- Include at least one **macro/generator** use in a library, because that is exactly what
  Idea 1 (#1587) interacts with — we need a scenario where reflection-on-own-output may or
  may not occur, to measure the parallel-compile win.
- Set `WebSharperBuildService=True` (as `test-templates.sh` does) for the warm-phase runs.

## Measurement phases (`perf-run.fsx`)

For each scenario the driver runs these phases, each `reps` times:

1. **cold-full** — `dotnet build-server shutdown`; `dotnet ws stop --force` (kill booster);
   `git clean`/delete all `obj`/`bin`; `dotnet build Perf.sln`. This is the worst case.
2. **cold-booster** — kill booster, clean, then `dotnet ws start`, then build. Isolates booster
   startup from compile.
3. **warm-noop** — booster warm, no edit, rebuild. Measures pure up-to-date overhead.
4. **warm-client-edit** — booster warm, touch one function body in `Client.Components`,
   rebuild affected projects. The case Idea 1/Idea 4 target.
5. **warm-leaf-edit** — **the library-change → dependent-recompile case.** After a completed
   first compile, edit a library (`Core.Domain` for deep fan-out, or `Client.Components` for a
   shallower one) and rebuild. Record the recompilation time **of each dependent project
   separately** (the consuming `App.Spa`/`App.Web` and intermediate libs), not just the
   solution total — this is the warm-recompile behavior the booster exists to speed up and the
   primary signal for Ideas 1 and 4. Run a matrix of edit sites (leaf vs mid-graph) since the
   propagation cost differs.

Each phase records wall-clock for the overall `dotnet build`, **and** parses the compiler's
own `TimedStage` output (see below) to attribute time to pipeline stages per project. For the
dependent-recompile measurement, build with `-p:WebSharperLogImportance=High` and attribute
the parsed per-project timings to each dependent.

Phase isolation rules:
- Restore is done once, untimed, before phase 1.
- Between repetitions of cold phases, the same clean is re-applied.
- The driver discards the first rep of phase 1 as warmup unless `--keep-warmup` is set, but
  records it separately (cold-cold is itself interesting).

## Capturing compiler stage timing (ties into Idea 3)

`LoggerBase.TimedStage` prints `"<stage>: <TimeSpan>"`, indented by context depth. This output
goes to the compiler's stdout, which the WS MSBuild task forwards to MSBuild at the importance
set by the **`WebSharperLogImportance`** property (it is the task's `StandardOutputImportance`,
wired in `WebSharper.{FSharp,CSharp}.targets`). At default importance the timing lines only
appear at higher MSBuild verbosity; set `WebSharperLogImportance=High` to surface them at
normal verbosity. The harness needs this as data. Two layers:

- **Cheap, immediate:** build with `dotnet build -p:WebSharperLogImportance=High` and have
  `perf-run.fsx` parse the emitted lines (stable strings: `"Parsing with FCS"`,
  `"Resolving names"`, `"WebSharper translation"`, `"Writing resources into assembly"`,
  `"Serializing metadata"`, `"Bundling"`, `"Total compilation time"`, etc.). Works with zero
  compiler changes — good enough to start.
- **Robust (Phase 0.4, the Idea-3 deliverable):** add an opt-in structured sink to
  `LoggerBase` so timing is emitted as JSONL (stage, elapsed-ms, project, depth) to a file
  named by `WebSharperTimingLog` (or wsconfig `timingLog`). Default behavior (console text) unchanged.
  See [04-log-analysis.md](04-log-analysis.md) for the exact design. The driver prefers the
  structured sink when present and falls back to console parsing.

## Output: one record per (scenario, phase, OS, which, rep)

`perf-run.fsx` appends JSONL to `perf-out/results.jsonl`. Schema and storage are defined in
[data/README.md](data/README.md). Each record carries `commitSha`, `branch`, `os`, `which`
(branch|baseline), `scenario`, `phase`, `rep`, `wallMs`, and a `stages` map of stage→ms.

## Aggregation (`aggregate.fsx`)

- Reduces reps to median + min per (os, which, scenario, phase, stage).
- Emits a markdown table to `$GITHUB_STEP_SUMMARY`: rows = scenario/phase, columns =
  baseline vs branch per OS, with Δ% and a ✅/⚠️/❌ flag on a configurable regression threshold
  (e.g. warn at +3%, fail at +10% on a key phase). Fail-the-job is opt-in so exploratory runs
  don't go red.
- Appends the reduced records into `perftesting/data/results.jsonl` and pushes (see data doc).

## Local use

The same `perf-scaffold.sh` + `perf-run.fsx` run locally against a dev build of the stack
(point the local nuget source at your `../localnuget`). Local numbers are not comparable across
machines but are fine for "did my change help on this machine" loops. CI is the source of truth
for tracked numbers.

## Deliverables checklist

- [ ] `build-script/.github/workflows/perf-benchmark.yml`
- [ ] `build-script/perf-scaffold.sh` (+ `gen-sources.fsx`)
- [ ] `build-script/perf-run.fsx`
- [ ] `build-script/aggregate.fsx`
- [ ] structured timing sink in `LoggerBase` (Phase 0.4)
- [ ] `perftesting/data/` schema + seeded baseline record
