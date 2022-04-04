open System.IO
open System.Diagnostics
open System.Threading
open System.Net.Http

let runSite relPath port =
    let mutable startedOk = false
    let started = new EventWaitHandle(false, EventResetMode.ManualReset)    
    
    let fullPath = Path.Combine(__SOURCE_DIRECTORY__, "templatetest", relPath)

    use proc = new Process()
    proc.StartInfo.FileName <- "dotnet"
    proc.StartInfo.Arguments <- $"run --project {fullPath} --urls=http://localhost:{port}/"
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
            failwith $"Starting project {relPath} failed."
    )

    proc.Start() |> ignore
    proc.BeginOutputReadLine()
    started.WaitOne() |> ignore

    let handler = new HttpClientHandler()
    // ignore localhost SSL errors
    handler.ServerCertificateCustomValidationCallback <-
        fun _ _ _ _ -> true
    use client = new HttpClient(handler, true)

    let resp = client.GetAsync($"http://localhost:{port}/").Result

    if not resp.IsSuccessStatusCode then
        failwith $"Getting home page of project {relPath} failed."     
    else
        printfn $"Testing {relPath} ok." 

    proc.Kill()
    printfn "Run stopped."
    
runSite "Fs.Web/Fs.Web.fsproj" 5000
runSite "Cs.Web/Cs.Web.csproj" 5010
runSite "Fs.Spa/Fs.Spa.fsproj" 5020
runSite "Cs.Spa/Cs.Spa.csproj" 5030
runSite "Fs.Min/Fs.Min.fsproj" 5040
runSite "Cs.Min/Cs.Min.csproj" 5050