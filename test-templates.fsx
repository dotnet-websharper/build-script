open System.IO
open System.Diagnostics
open System.Threading
open System.Net.Http
open System.Net

// ignore localhost SSL errors
ServicePointManager.ServerCertificateValidationCallback <-
    fun _ _ _ _ -> true

let runSite relPath =
    let mutable startedOk = false
    let started = new EventWaitHandle(false, EventResetMode.ManualReset)    
    
    let fullPath = Path.Combine(__SOURCE_DIRECTORY__, "templatetest", relPath)

    use proc = new Process()
    proc.StartInfo.FileName <- "dotnet"
    proc.StartInfo.Arguments <- "run --project " + fullPath
    proc.StartInfo.UseShellExecute <- false
    proc.StartInfo.RedirectStandardOutput <- true
    
    proc.OutputDataReceived.Add(fun d -> 
        if not (isNull d) then
            printfn "%s" d.Data
            if d.Data.Contains("Application started.") then
                startedOk <- true   
                started.Set() |> ignore
    )
    proc.Exited.Add(fun _ -> 
        if not startedOk then
            failwithf "Starting project %s failed." relPath    
    )

    proc.Start() |> ignore
    proc.BeginOutputReadLine()
    started.WaitOne() |> ignore

    use client = new HttpClient()
    let resp = client.GetAsync("http://localhost:5000/").Result

    if not resp.IsSuccessStatusCode then
        failwithf "Getting home page of project %s failed." relPath    
    else
        printfn "Testing %s ok." relPath

    proc.Kill()
    
runSite "Fs.Web/Fs.Web.fsproj"
runSite "Cs.Web/Cs.Web.csproj"
runSite "Fs.Spa/Fs.Spa.fsproj"
runSite "Cs.Spa/Cs.Spa.csproj"
runSite "Fs.Min/Fs.Min.fsproj"
runSite "Cs.Min/Cs.Min.csproj"