# Idea 3 — Log Analysis: find the weakest spots

**Status:** folded into Phase 0 (it is the evidence base for everything else). Cheap because
the data already exists; we just make it machine-readable and aggregate it.

## What already exists

`LoggerBase.TimedStage` in
[../core/src/compiler/WebSharper.Compiler/LoggerBase.fs](../core/src/compiler/WebSharper.Compiler/LoggerBase.fs)
already times every stage:

```fsharp
member x.TimedStage name =
    let now = Stopwatch.GetTimestamp()
    let elapsed = TimeSpan.FromSeconds (float (now - timeStamps.Head) / float Stopwatch.Frequency)
    sprintf "%s: %O" name elapsed |> x.Out
    timeStamps <- now :: timeStamps.Tail
```

`EnterContext`/`ExitContext` give nesting (indentation depth). The stages emitted today
(grep `TimedStage`) include, per project:

- `Reading configuration`
- `Running F# source generators` (when used)
- `F# compilation` (the FCS `checker.Compile`)
- `Parsing with FCS`, `Analyzing function arguments`, `Resolving names`
  (in `Compiler.FSharp/ProjectReader.fs`)
- `Checking project`, `Waiting on merged metadata`, `WebSharper translation`
  (in `Compiler.FSharp/Main.fs`)
- `Writing resources into assembly`, `Serializing metadata` (in `FrontEnd.fs`)
- `Bundling`
- `Writing final dll`, `Getting Assembly definition`, `Loading IExtension implementation`
- `Total compilation time`

These are exactly the buckets we want to attribute time to. The problem: today they're only
human-readable console text.

## Deliverable: opt-in structured timing sink

Add a structured emit path to `LoggerBase`, **off by default** so normal console output is
unchanged.

- New env var (or config) `WEBSHARPER_TIMING_LOG=<path>`. When set, `TimedStage` also appends a
  JSON line:
  ```json
  {"t":"2026-06-13T10:00:00.123Z","project":"Client.Components","stage":"WebSharper translation","ms":812.4,"depth":1}
  ```
  - `project`: from `config.ProjectFile` (thread it into the logger, or include it in each
    stage call site; simplest is to set a `logger.Project` once per `Compile`).
  - `depth`: current `timeStamps.Length - 1` (already used by `Indent`).
  - `ms`: the elapsed value it already computes.
- Implementation: keep `TimedStage`'s console line, and additionally write to the sink when
  configured. The sink is a single append-only writer; in the booster, multiple concurrent
  compilations must tag records by project (and a request id) so lines can be demultiplexed.
- Keep it allocation-light and failure-tolerant (a logging error must never fail a build).

This is what the benchmark driver (`perf-run.fsx`) consumes; until it lands, the driver falls
back to parsing the console strings (stable today).

## Analysis outputs

`aggregate.fsx` (the harness aggregator) turns the JSONL into:

- **Per-stage share of total**, per project and summed across the solution, for cold vs warm.
  This is the "weakest spots" view: which stage dominates where.
- **Branch vs baseline per stage** with Δ%, so a regression is attributable to a stage, not
  just a total.
- A "top N slowest (project, stage)" list to direct optimization effort.

## How it drives the other ideas

- Tells us the realistic ceiling for **Idea 1** before building it: if `F# compilation`/
  `Parsing with FCS` dominate and `WebSharper translation` + `Writing resources` are small,
  the overlap win is bounded — measure, then decide how much to invest.
- Surfaces whether **warm** recompiles are dominated by real work or by booster/pipe/serialize
  overhead (`Serializing metadata`, `Writing resources into assembly`), informing Idea 2/4.
- Becomes the permanent regression guard: the harness can fail on a per-stage threshold, not
  only on totals.

## Risks / notes

- The booster runs concurrent compilations (especially after Idea 1); the sink must be
  concurrency-safe and records must carry enough identity to separate interleaved stages.
- Don't change the console wording of existing stages without updating the driver's fallback
  parser — or, better, land the structured sink first so parsing strings becomes a fallback
  only.
