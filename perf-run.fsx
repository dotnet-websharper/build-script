open System
open System.Collections.Generic
open System.Diagnostics
open System.Globalization
open System.IO
open System.Text.Json
open System.Text.RegularExpressions

type Options =
    {
        Root: string
        OutDir: string
        Reps: int
        Os: string
        Which: string
        CommitSha: string
        Branch: string
        BaselineSha: string option
        DotNetVersion: string
        RunId: string
        KeepWarmup: bool
    }

let rec parseArgs opts args =
    match args with
    | [] -> opts
    | "--root" :: v :: rest -> parseArgs { opts with Root = v } rest
    | "--out" :: v :: rest -> parseArgs { opts with OutDir = v } rest
    | "--reps" :: v :: rest -> parseArgs { opts with Reps = int v } rest
    | "--os" :: v :: rest -> parseArgs { opts with Os = v } rest
    | "--which" :: v :: rest -> parseArgs { opts with Which = v } rest
    | "--sha" :: v :: rest -> parseArgs { opts with CommitSha = v } rest
    | "--branch" :: v :: rest -> parseArgs { opts with Branch = v } rest
    | "--baseline-sha" :: v :: rest -> parseArgs { opts with BaselineSha = if String.IsNullOrWhiteSpace v then None else Some v } rest
    | "--dotnet-version" :: v :: rest -> parseArgs { opts with DotNetVersion = v } rest
    | "--run-id" :: v :: rest -> parseArgs { opts with RunId = v } rest
    | "--keep-warmup" :: rest -> parseArgs { opts with KeepWarmup = true } rest
    | x :: _ -> failwithf "Unknown argument: %s" x

let runId =
    let v = Environment.GetEnvironmentVariable "GITHUB_RUN_ID"
    if String.IsNullOrWhiteSpace v then "local-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) else v

let defaults =
    {
        Root = "Perf"
        OutDir = "perf-out"
        Reps = 5
        Os = Environment.OSVersion.Platform.ToString()
        Which = "branch"
        CommitSha = "0000000"
        Branch = "local"
        BaselineSha = None
        DotNetVersion = ""
        RunId = runId
        KeepWarmup = false
    }

let opts = parseArgs defaults (fsi.CommandLineArgs |> Array.skip 1 |> Array.toList)
let root = Path.GetFullPath opts.Root
let outDir = Path.GetFullPath opts.OutDir
let logsDir = Path.Combine(outDir, "logs")
Directory.CreateDirectory logsDir |> ignore
Directory.CreateDirectory outDir |> ignore

let solutionFile =
    let slnx = Path.Combine(root, "Perf.slnx")
    let sln = Path.Combine(root, "Perf.sln")
    if File.Exists slnx then "Perf.slnx"
    elif File.Exists sln then "Perf.sln"
    else "Perf.sln"

let runProcess (allowFailure: bool) (env: Map<string, string>) (file: string) (args: string) (workingDir: string) (logName: string) =
    let logPath = Path.Combine(logsDir, logName + ".log")
    let psi = ProcessStartInfo(file, args)
    psi.WorkingDirectory <- workingDir
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    for KeyValue(k, v) in env do
        psi.Environment[k] <- v
    use p = Process.Start psi
    let output = ResizeArray<string>()
    p.OutputDataReceived.Add(fun e -> if e.Data <> null then output.Add e.Data)
    p.ErrorDataReceived.Add(fun e -> if e.Data <> null then output.Add e.Data)
    p.BeginOutputReadLine()
    p.BeginErrorReadLine()
    let sw = Stopwatch.StartNew()
    p.WaitForExit()
    sw.Stop()
    File.WriteAllLines(logPath, output)
    if p.ExitCode <> 0 && not allowFailure then
        failwithf "%s %s failed with exit code %d. See %s" file args p.ExitCode logPath
    sw.Elapsed.TotalMilliseconds, output |> Seq.toList

let runDotNet allowFailure args logName =
    runProcess allowFailure Map.empty "dotnet" args root logName |> ignore

let cleanOutputs () =
    for name in [ "bin"; "obj" ] do
        for dir in Directory.EnumerateDirectories(root, name, SearchOption.AllDirectories) do
            Directory.Delete(dir, true)

let touchGenerated project marker =
    let path = Path.Combine(root, project, "GeneratedPerf.fs")
    let text = File.ReadAllText path
    let updated =
        Regex.Replace(text, @"let domainSummary seed =|let sharedSummary seed =|let renderNumber seed =|let renderSibling seed =",
            fun m -> m.Value)
    File.WriteAllText(path, updated + Environment.NewLine + sprintf "// perf edit %s %O" marker DateTimeOffset.UtcNow)

let timingStages timingPath consoleLines =
    let stages = Dictionary<string, float>()
    let add key value =
        match stages.TryGetValue key with
        | true, old -> stages[key] <- old + value
        | false, _ -> stages[key] <- value
    if File.Exists timingPath then
        for line in File.ReadLines timingPath do
            if not (String.IsNullOrWhiteSpace line) then
                try
                    use doc = JsonDocument.Parse line
                    let root = doc.RootElement
                    let project = root.GetProperty("project").GetString()
                    let stage = root.GetProperty("stage").GetString()
                    let ms = root.GetProperty("ms").GetDouble()
                    add (sprintf "%s/%s" project stage) ms
                with _ -> ()
    else
        // Fallback only: used when no --wstimings JSON file is produced (compiler predating
        // WebSharper #1590). TimedStage may render either a TimeSpan ("00:00:00.0997059") or
        // bare seconds ("0.0997059") depending on compiler version, so accept both.
        let rx = Regex(@"^\s*(.+):\s+([0-9][0-9:.]*)\s*$")
        for line in consoleLines do
            let m = rx.Match line
            if m.Success then
                let v = m.Groups[2].Value
                let ms =
                    match TimeSpan.TryParse(v, CultureInfo.InvariantCulture) with
                    | true, ts -> Some ts.TotalMilliseconds
                    | _ ->
                        match Double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture) with
                        | true, secs -> Some (secs * 1000.0)
                        | _ -> None
                match ms with
                | Some ms -> add ("unknown/" + m.Groups[1].Value.Trim()) ms
                | None -> ()
    stages

let safeFilePart (value: string) =
    let invalid = Path.GetInvalidFileNameChars() |> Set.ofArray
    value
    |> Seq.map (fun c -> if invalid.Contains c || c = '/' || c = '\\' then '-' else c)
    |> Array.ofSeq
    |> String

let dotnetVersion =
    if String.IsNullOrWhiteSpace opts.DotNetVersion then
        let _, lines = runProcess false Map.empty "dotnet" "--version" root "dotnet-version"
        lines |> List.tryHead |> Option.defaultValue ""
    else opts.DotNetVersion

let jsonString (s: string) = JsonSerializer.Serialize s
let jsonStringOrNull = function Some s -> jsonString s | None -> "null"

let writeRecord (scenario: string) (phase: string) (rep: int) (wallMs: float) (stages: Dictionary<string, float>) (notes: string option) =
    let stageJson =
        stages
        |> Seq.map (fun kv -> sprintf "%s:%s" (jsonString kv.Key) (kv.Value.ToString("0.###", CultureInfo.InvariantCulture)))
        |> String.concat ","
    let line =
        sprintf
            """{"schemaVersion":1,"runId":%s,"timestamp":%s,"commitSha":%s,"branch":%s,"baselineSha":%s,"which":%s,"os":%s,"dotnetVersion":%s,"scenario":%s,"phase":%s,"rep":%d,"wallMs":%s,"stages":{%s},"notes":%s}"""
            (jsonString opts.RunId)
            (jsonString (DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)))
            (jsonString opts.CommitSha)
            (jsonString opts.Branch)
            (jsonStringOrNull opts.BaselineSha)
            (jsonString opts.Which)
            (jsonString opts.Os)
            (jsonString dotnetVersion)
            (jsonString scenario)
            (jsonString phase)
            rep
            (wallMs.ToString("0.###", CultureInfo.InvariantCulture))
            stageJson
            (jsonStringOrNull notes)
    File.AppendAllText(Path.Combine(outDir, "results.jsonl"), line + Environment.NewLine)

let build scenario phase rep projectOpt notes =
    let projectName = projectOpt |> Option.defaultValue "solution" |> safeFilePart
    let timingPath = Path.Combine(outDir, sprintf "timing-%s-%d-%s.jsonl" phase rep projectName)
    if File.Exists timingPath then File.Delete timingPath
    let escapedTimingPath = timingPath.Replace("\"", "\\\"")
    let args =
        match projectOpt with
        | None -> sprintf "build %s --no-restore -p:WebSharperLogImportance=High -p:WebSharperTimingLog=\"%s\"" solutionFile escapedTimingPath
        | Some p -> sprintf "build %s --no-restore -p:WebSharperLogImportance=High -p:WebSharperTimingLog=\"%s\"" p escapedTimingPath
    let wallMs, lines = runProcess false Map.empty "dotnet" args root (sprintf "%s-%d-%s" phase rep projectName)
    writeRecord scenario phase rep wallMs (timingStages timingPath lines) notes

let startBooster () = runDotNet true "ws start" "ws-start"
let stopBooster () = runDotNet true "ws stop --force" "ws-stop"
let shutdownBuildServer () = runDotNet true "build-server shutdown" "build-server-shutdown"

let reps = [ 0 .. opts.Reps - 1 ]

for rep in reps do
    shutdownBuildServer()
    stopBooster()
    cleanOutputs()
    build "multiproject-v1" "cold-full" rep None (if rep = 0 && not opts.KeepWarmup then Some "first cold rep includes process/JIT warmup" else None)

for rep in reps do
    stopBooster()
    cleanOutputs()
    startBooster()
    build "multiproject-v1" "cold-booster" rep None None

startBooster()
for rep in reps do
    build "multiproject-v1" "warm-noop" rep None None

for rep in reps do
    startBooster()
    touchGenerated "Client.Components" (sprintf "client-%d" rep)
    build "multiproject-v1" "warm-client-edit" rep None None

let leafDependents =
    [
        "Core.Shared/Core.Shared.fsproj"
        "Client.Components/Client.Components.fsproj"
        "Client.Components2/Client.Components2.fsproj"
        "Server.Api/Server.Api.fsproj"
        "App.Spa/App.Spa.fsproj"
        "App.Web/App.Web.fsproj"
    ]

for rep in reps do
    startBooster()
    touchGenerated "Core.Domain" (sprintf "leaf-%d" rep)
    for project in leafDependents do
        let scenario = "multiproject-v1/" + Path.GetFileNameWithoutExtension project
        build scenario "warm-leaf-edit" rep (Some project) None
