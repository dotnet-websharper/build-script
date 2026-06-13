# Benchmark Results Data

Results are stored as **JSONL** (one JSON object per line) in `results.jsonl`, committed to
git so they are diffable and travel with the branch. Every record is tagged with its git
commit + branch + OS so a number can always be traced to the exact compiler that produced it.
Raw build logs are *not* committed — they are uploaded as CI artifacts (too large/noisy).

## Why JSONL committed here

- Self-contained: no external DB/secrets/infra to maintain.
- Diffable: a PR that changes perf shows the new records in the diff.
- Append-only: concurrent runs append distinct lines; merge conflicts are trivial (line-level).

## Record schema

One record per `(commitSha, os, which, scenario, phase, rep)`. See
[results.schema.json](results.schema.json) for the formal schema. Fields:

| Field | Type | Meaning |
|---|---|---|
| `schemaVersion` | int | bump on schema change (start at `1`) |
| `runId` | string | CI run id (`github.run_id`) or `local-<timestamp>` |
| `timestamp` | string (ISO-8601 UTC) | when the measurement was taken |
| `commitSha` | string | full SHA of the core commit under test |
| `branch` | string | branch name the commit came from |
| `baselineSha` | string \| null | the SHA this run is being compared against (null for a baseline leg) |
| `which` | `"branch"` \| `"baseline"` | which leg of the build matrix |
| `os` | `"windows-latest"` \| `"ubuntu-latest"` \| `"macos-latest"` | runner OS |
| `dotnetVersion` | string | SDK version used |
| `scenario` | string | scenario id, e.g. `"multiproject-v1"` |
| `phase` | string | `cold-full` \| `cold-booster` \| `warm-noop` \| `warm-client-edit` \| `warm-leaf-edit` |
| `rep` | int | repetition index (0-based) |
| `wallMs` | number | wall-clock ms for the measured `dotnet build` |
| `stages` | object | map of per-project stage → ms, summed; e.g. `{"Client.Components/WebSharper translation": 812.4}` |
| `notes` | string \| null | freeform (e.g. `"first rep, includes JIT warmup"`) |

Example line:

```json
{"schemaVersion":1,"runId":"1234567890","timestamp":"2026-06-13T10:00:00Z","commitSha":"841f23f3...","branch":"perf/idea1","baselineSha":"deadbeef...","which":"branch","os":"ubuntu-latest","dotnetVersion":"10.0.100","scenario":"multiproject-v1","phase":"cold-full","rep":0,"wallMs":18342.5,"stages":{"App.Web/WebSharper translation":2103.2,"App.Web/Bundling":1540.9},"notes":null}
```

## Aggregation

`aggregate.fsx` (in `build-script`) reads all `results.jsonl` records, reduces reps to
**median + min** per `(os, which, scenario, phase, stage)`, and:

- writes a markdown comparison (baseline vs branch, Δ%) to `$GITHUB_STEP_SUMMARY`, and
- appends the reduced records back here (the raw per-rep records may also be kept; decide by
  file size — if it grows large, keep only reduced records in git and raw reps in artifacts).

## Stability conventions

- Compare **within the same `runId`** (branch vs baseline built and measured in one workflow
  invocation, same runner image). Cross-run absolute numbers are informational only.
- Report median for the headline and min as the "least-disturbed" lower bound.
- Treat the first rep of cold phases as warmup unless explicitly studying cold-cold.
