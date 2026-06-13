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

These are exactly the buckets we want to attribute time to.

These lines are already surfaced in a normal build by setting the MSBuild property
**`WebSharperLogImportance=High`** (it maps to the WS task's `StandardOutputImportance` in
`WebSharper.{FSharp,CSharp}.targets`); otherwise they only show at higher MSBuild verbosity.
So "enable timing" requires no compiler change. The problem this idea fixes is that the output
is **human-readable console text only** — not aggregatable, not per-project tagged, and fragile
to parse. The deliverable below makes it structured; enhancing usability of the existing output
(e.g. clearer per-project prefixes) is also in scope and cheap.

## The structured timing sink already exists (#1590)

The opt-in structured emit path this idea called for **is already implemented** in current
`master` (commit "#1590 Add optional JSON logging of performance timers"). Verified in the
tree:

- MSBuild property **`WebSharperTimingLog=<path>`** → `WebSharperTask` writes `--wstimings:<path>`
  → `CommandTools` parses it into `config.TimingLog` → `Compile.fs` sets `logger.TimingLogPath`
  (and `logger.ProjectFile`).
- `LoggerBase.writeTimingRecord` then appends one JSON line per `TimedStage`, **in addition to**
  the unchanged console line, guarded by a static lock and failure-tolerant:
  ```json
  {"t":"2026-06-13T10:00:00.123Z","project":"Client.Components","stage":"WebSharper translation","ms":812.4,"depth":1}
  ```
  - `project` = `Path.GetFileNameWithoutExtension config.ProjectFile`
  - `ms` = `elapsed.TotalMilliseconds` (a JSON number)
  - `depth` = context nesting

So `perf-run.fsx` passes `-p:WebSharperTimingLog=<file>` and reads `project`/`stage`/`ms`
directly — no compiler work needed. (Confirmed: the field names and the `ms`-as-number format
match the driver's parser exactly.) The console-string fallback only matters for compilers
predating #1590 — and even then, note the elapsed there can render as a bare-seconds number,
which the fallback now handles.

### Remaining Idea-3 work (the actual deliverable now)

The sink is done; what's left is **analysis on top of it**:
- Aggregation across projects/stages (below) — the "weakest spots" view.
- Booster concurrency: when Idea 1 makes compilation parallel, ensure interleaved records stay
  attributable (a request id alongside `project` would make demux robust; today the static lock
  keeps lines intact but order across projects is interleaved).

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
