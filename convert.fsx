#r "paket:
nuget Fake.Core.Target
nuget Fake.DotNet.Cli
nuget Fake.IO.FileSystem
nuget Fake.Tools.Git
//"
#load ".fake/convert.fsx/intellisense.fsx"
open System.IO
open Fake.Core
open Fake.Core.TargetOperators
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.Tools

/// Files to add from this repo's files/ to the destination.
let newFiles =
    [
        "build.fsx"
        "build.cmd"
        "build.sh"
        ".paket/paket.bootstrapper.exe.config"
        ".paket/paket.bootstrapper.proj"
        ".paket/paket.bootstrapper.props"
        ".paket/Paket.Restore.targets"
    ]

/// Files to remove from the destination.
let delFiles =
    [
        ".paket/paket.exe"
    ]

let ignoreFiles =
    [
        ".paket/.store/"
        ".paket/*.exe"
        ".paket/fake"
        ".paket/paket"
        ".paket/paket.bootstrapper"
    ]

let srcDir = __SOURCE_DIRECTORY__ </> "files"
let dstDir = Directory.GetCurrentDirectory()

let gitAdd file =
    Git.Staging.stageFile dstDir file
    |> Trace.logfn "%A"

Target.description "Add the new or modified files"
Target.create "AddFiles" <| fun _ ->
    let srcFiles, dstFiles =
        newFiles
        |> Seq.map (fun rel -> srcDir </> rel, dstDir </> rel)
        |> Array.ofSeq
        |> Array.unzip
    srcFiles
    |> Templates.load
    |> Templates.replaceKeywords []
    |> Seq.mapi (fun i (_, lines) ->
        let dstFile = dstFiles.[i]
        Directory.ensure (Path.getDirectory dstFile)
        dstFile, lines
    )
    |> Templates.saveFiles
    Seq.iter gitAdd dstFiles

Target.description "Remove the obsolete files"
Target.create "RemoveFiles" <| fun _ ->
    let status = Git.FileStatus.getAllFiles dstDir
    delFiles
    |> Seq.filter (fun rel -> status |> Seq.exists (snd >> (=) rel))
    |> Seq.iter (fun rel ->
        Git.CommandHelper.showGitCommand dstDir (sprintf "rm -f %s" rel)
    )

let installPaket() =
    DotNet.exec id "restore" (dstDir </> ".paket")
    |> ignore

let fixPaketDependencies() =
    let path = dstDir </> "paket.dependencies"
    let lines = File.ReadAllLines path

    // Use recent paket
    if lines.[0].StartsWith "version" then
        lines.[0] <- "version 5.203.0"

    // Use the "fake5" branch of build-script
    lines
    |> Array.tryFindIndex (fun l ->
        l.Contains "build-script" && not (l.EndsWith " fake5")
    )
    |> Option.iter (fun i ->
        lines.[i] <- "    git https://github.com/dotnet-websharper/build-script fake5"
    )

    // Use the right dependencies for FAKE by replacing the contents of `group build`.
    let start = lines |> Array.findIndex (fun l -> l.Trim() = "group build")
    let before = lines.[..start - 1]
    let afterGroupDecl = lines.[start + 1..]
    let after =
        match afterGroupDecl |> Array.tryFindIndex (fun l -> l.TrimStart().StartsWith "group ") with
        | None -> [||]
        | Some i -> afterGroupDecl.[i..]
    let newLines = Array.concat [|
        before
        File.ReadAllLines (srcDir </> "paket.dependencies.partial")
        after
    |]
    File.WriteAllLines(path, newLines)
    gitAdd "paket.dependencies"

let paketUpdate() =
    CreateProcess.fromRawCommand (dstDir </> ".paket" </> "paket.exe") ["update"]
    |> CreateProcess.ensureExitCode
    |> Proc.run
    |> ignore
    gitAdd "paket.lock"
    gitAdd ".paket/Paket.Restore.targets" // update may change it

Target.description "Fix the paket dependencies"
Target.create "UpdatePaket" <| fun _ ->
    installPaket()
    fixPaketDependencies()
    paketUpdate()

Target.description "Ignore new generated files"
Target.create "UpdateGitIgnore" <| fun _ ->
    let gitignore = dstDir </> ".gitignore"
    let currentIgnores = File.ReadAllLines gitignore
    let newIgnores =
        ignoreFiles
        |> Seq.filter (fun f -> not (Array.contains f currentIgnores))
        |> Array.ofSeq
    let allIgnores = Array.append currentIgnores newIgnores
    File.WriteAllLines(gitignore, allIgnores)
    gitAdd gitignore

Target.description "Do all the steps of conversion"
Target.create "Convert" ignore

Target.description "Build the project (for checking)"
Target.create "TestBuild" <| fun _ ->
    CreateProcess.fromRawCommand (dstDir </> "build.cmd") []
    |> CreateProcess.withWorkingDirectory dstDir
    |> Proc.run
    |> ignore

Target.description "Convert and check that it builds"
Target.create "All" ignore

"AddFiles"
    ==> "UpdatePaket"
    ==> "UpdateGitIgnore"
    ==> "RemoveFiles"
    ==> "Convert"
    ==> "All"

"Convert"
    ?=> "TestBuild"
    ==> "All"

Target.runOrDefault "All"
