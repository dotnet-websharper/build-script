open System
open System.Collections.Generic
open System.Globalization
open System.IO
open System.Text.Json

type Record =
    {
        Os: string
        Which: string
        Scenario: string
        Phase: string
        WallMs: float
        Stages: Map<string, float>
    }

let args = fsi.CommandLineArgs |> Array.skip 1 |> Array.toList
let input =
    match args with
    | "--input" :: p :: _ -> p
    | _ -> "perf-out/results.jsonl"

let files =
    if Directory.Exists input then Directory.GetFiles(input, "results.jsonl", SearchOption.AllDirectories)
    elif File.Exists input then [| input |]
    else [||]

let records =
    [
        for file in files do
            for line in File.ReadLines file do
                if not (String.IsNullOrWhiteSpace line) then
                    use doc = JsonDocument.Parse line
                    let r = doc.RootElement
                    let stages =
                        r.GetProperty("stages").EnumerateObject()
                        |> Seq.map (fun p -> p.Name, p.Value.GetDouble())
                        |> Map.ofSeq
                    {
                        Os = r.GetProperty("os").GetString()
                        Which = r.GetProperty("which").GetString()
                        Scenario = r.GetProperty("scenario").GetString()
                        Phase = r.GetProperty("phase").GetString()
                        WallMs = r.GetProperty("wallMs").GetDouble()
                        Stages = stages
                    }
    ]

let median values =
    let a = values |> Seq.sort |> Seq.toArray
    if a.Length = 0 then nan
    elif a.Length % 2 = 1 then a[a.Length / 2]
    else (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2.0

let summary =
    records
    |> Seq.groupBy (fun r -> r.Os, r.Which, r.Scenario, r.Phase)
    |> Seq.map (fun ((os, which, scenario, phase), rs) ->
        let walls = rs |> Seq.map _.WallMs |> Seq.toArray
        os, which, scenario, phase, median walls, Array.min walls)
    |> Seq.toArray

let byKey =
    summary
    |> Seq.map (fun (os, which, scenario, phase, med, min) -> (os, which, scenario, phase), (med, min))
    |> dict

let rows =
    [
        yield "| OS | Scenario | Phase | Baseline median | Branch median | Delta | Min baseline | Min branch |"
        yield "|---|---|---|---:|---:|---:|---:|---:|"
        for os, scenario, phase in summary |> Seq.map (fun (os, _, scenario, phase, _, _) -> os, scenario, phase) |> Seq.distinct |> Seq.sort do
            let bKey = (os, "baseline", scenario, phase)
            let cKey = (os, "branch", scenario, phase)
            if byKey.ContainsKey bKey && byKey.ContainsKey cKey then
                let bMed, bMin = byKey[bKey]
                let cMed, cMin = byKey[cKey]
                let delta = if bMed = 0.0 then nan else ((cMed - bMed) / bMed) * 100.0
                yield sprintf "| %s | %s | %s | %.1f | %.1f | %.1f%% | %.1f | %.1f |" os scenario phase bMed cMed delta bMin cMin
            else
                for which in [ "baseline"; "branch" ] do
                    let key = (os, which, scenario, phase)
                    if byKey.ContainsKey key then
                        let med, min = byKey[key]
                        yield sprintf "| %s | %s | %s (%s) | %.1f |  |  | %.1f |  |" os scenario phase which med min
    ]

let markdown = String.concat Environment.NewLine rows
printfn "%s" markdown

let summaryPath = Environment.GetEnvironmentVariable "GITHUB_STEP_SUMMARY"
if not (String.IsNullOrWhiteSpace summaryPath) then
    File.AppendAllText(summaryPath, "## WebSharper compiler benchmark" + Environment.NewLine + markdown + Environment.NewLine)
