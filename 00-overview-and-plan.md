# Overview & Plan

## Goal

Make the WebSharper compiler faster for F# projects, measured on realistic multi-project
solutions, across Windows/Linux/macOS, for both:

1. **Cold (first) compilation** — clean `obj`/`bin`, no booster warm-up.
2. **Warm recompilation** — booster (`wsfscservice`) already running, small edits, the
   case `dotnet ws` / `WebSharperBuildService` is meant to accelerate.

We treat "faster" as a measured, regression-tracked property, not a one-off. Every change
proposed here is gated on the benchmark harness ([01-benchmark-harness.md](01-benchmark-harness.md))
showing a real improvement with no correctness regression.

## Success criteria

- A repeatable CI benchmark exists that, given a branch name, builds the WS stack and
  measures a fixed scenario set on all three OSes, emitting one JSONL record per
  (commit, branch, OS, scenario, phase) into [data/](data/).
- Per-stage timing of the compiler is available as structured data, so a regression or
  improvement can be attributed to a pipeline stage, not just a total.
- At least Idea 1 lands with a demonstrated cold-compile improvement on the multi-project
  scenario, and no change to emitted JS or metadata for the default configuration.

## Why these phases, in this order

The harness comes first because **nothing else can be trusted without it** — compiler perf
is noisy (JIT warmup, disk cache, FCS internal caches, CI runner variance) and the only
way to compare a branch to baseline honestly is a fixed, automated scenario with multiple
samples.

Idea 3 (log analysis) is folded into the harness work rather than treated as a separate
late phase, because the per-stage timing it produces is the evidence base for every other
idea. It is cheap: the data is already emitted by `TimedStage`, we only need to make it
machine-readable and aggregate it.

Then the heavy ideas, ordered by payoff and independence:

| Phase | Work | Primary win | Risk | Depends on |
|---|---|---|---|---|
| 0 | Benchmark harness + log analysis | trustworthy measurement | low | — |
| 1 | **Idea 1**: parallel F# compile + #1587 opt-in reflection + optional RPC-await | cold compile | medium–high | Phase 0, #1587 |
| 2 | **Idea 4**: multi-project `dotnet ws watch` | inner-loop dev experience | medium | Phase 0 |
| 3 | **Idea 2**: sidecar metadata dll | dll size / load / pack-time; enables future caching | high | Phase 0 |

Idea 1 is the chosen first implementation target. Ideas 2 and 4 are largely independent of
it and of each other, so they can be scheduled by appetite once Phase 1 is underway.

## Work breakdown

### Phase 0 — Harness & measurement (foundation)
- 0.1 GitHub Action `perf-benchmark.yml` in `build-script` (matrix: windows/ubuntu/macos),
  parameterized by branch, building WS→UI→Templates chained via `../localnuget`.
- 0.2 Scenario scaffolder `perf-scaffold.sh` producing the representative solution
  (libs → SPA + Web consumers).
- 0.3 Driver `perf-run.fsx` that runs each scenario (cold + warm phases), parses compiler
  timing, and writes JSONL records tagged with commit/branch/OS.
- 0.3a **Library-change → dependent-recompile measurement.** After a first full compile of
  the solution, edit a library project and measure the recompilation of the projects that
  depend on it (the consuming SPA/Web app). This is the core warm-recompile case the booster
  targets and must be a first-class, separately-recorded measurement, not just folded into a
  whole-solution rebuild. Captured by the `warm-leaf-edit` phase (see harness doc), reported
  per dependent project.
- 0.4 Timing output: **structured per-stage JSON already exists (#1590)** — building with
  `-p:WebSharperTimingLog=<file>` makes the compiler write one JSON record per stage
  (`{project,stage,ms,…}`); `perf-run.fsx` consumes it directly. A console fallback
  (`-p:WebSharperLogImportance=High`, wired to the WS task's `StandardOutputImportance`) covers
  pre-#1590 compilers. So idea 3's *sink* is done; the remaining idea-3 work is the
  **analysis/aggregation** on top of it (0.5).
- 0.5 Aggregation: `data/aggregate.fsx` to roll JSONL up into a comparison table /
  step summary.

### Phase 1 — Parallel F# compilation (Idea 1)
- 1.1 Implement #1587: gate `includeCurrent` reflection behind a config flag
  (`WebSharperReflectOwnOutput` / wsconfig `reflectOwnOutput`), default preserving current
  behavior initially, then flip default to **off** once macros/generators in the stack are
  audited.
- 1.2 Make RPC verification's dependence on the output dll explicit and **awaitable/optional**
  (`WebSharperRpcVerification = on|deferred|off`).
- 1.3 With reflection-on-own-output off, allow the plain F# `checker.Compile` to run
  concurrently with WS metadata loading / early translation setup, joining only where the
  output dll is genuinely required (assembly write, optional RPC verify).
- 1.4 Validate equivalence: identical JS + metadata vs. baseline on the full snippet/test
  suite and the benchmark scenarios.

### Phase 2 — Multi-project watch (Idea 4)
- 2.1 Build a project dependency graph in `dotnet ws watch` from `ProjectReference`s.
- 2.2 On a client-only change, run WS translation and propagate JS through dependents to the
  final project, writing JS (and running esbuild on bundle, WS8+) **without** recompiling dlls.
- 2.3 Fall back to full `dotnet ws build` when a change affects server code, signatures, or
  references.

### Phase 3 — Sidecar metadata (Idea 2)
- 3.1 Write WS resources/metadata to `MyProject.dll.websharper` (+ `MyProject.dll.websharperruntime`)
  instead of embedding, behind a flag.
- 3.2 Reader: prefer sidecar, fall back to embedded resource (back-compat with existing dlls).
- 3.3 MSBuild: ensure the sidecar is copied to `bin` alongside the dll (CopyToOutputDirectory
  + on `ProjectReference` consumers).
- 3.4 NuGet: ensure `dotnet pack` includes the sidecar next to the dll in `lib/<tfm>/`.

## Open questions to resolve with data (not guesses)

- Where is cold-compile time actually spent for a multi-project F# solution — FCS check,
  WS name resolution, translation, metadata serialize, assembly write, or bundling? Phase 0
  answers this and re-prioritizes accordingly.
- How much of warm recompile is booster overhead (pipe/serialization) vs. real work?
- Does reflection-on-own-output actually appear in the template/standard-library macros,
  i.e. how much can Phase 1 realistically overlap?

## Non-goals

- Changing emitted JavaScript or metadata semantics. Every perf change must be output-neutral
  for the default configuration.
- C#-specific optimizations (out of scope for now; the harness includes C# consumers only as
  representative load, not as an optimization target).
